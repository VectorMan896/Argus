namespace Argus.Tests;

public sealed class CpuMathTests
{
    [Fact]
    public void Busy_is_kernel_plus_user_minus_idle()
    {
        // 100 ticks total (kernel 60 already includes 50 idle, user 40): 50 busy
        Assert.Equal(50.0, CpuMeter.Percent(idleDelta: 50, kernelDelta: 60, userDelta: 40), 3);
    }

    [Fact]
    public void Fully_idle_and_fully_busy()
    {
        Assert.Equal(0.0, CpuMeter.Percent(100, 100, 0), 3);
        Assert.Equal(100.0, CpuMeter.Percent(0, 70, 30), 3);
    }

    [Fact]
    public void No_elapsed_time_is_zero_not_a_crash() => Assert.Equal(0.0, CpuMeter.Percent(0, 0, 0));

    [Fact]
    public void Odd_readings_are_clamped_to_0_to_100()
    {
        Assert.InRange(CpuMeter.Percent(500, 100, 0), 0, 100);        // idle larger than total (clock skew)
    }

    [Fact]
    public void A_real_reading_is_a_percentage()
    {
        var m = new CpuMeter();
        Assert.Null(m.Read());                                         // the first call only sets the baseline
        Thread.Sleep(400);
        var v = m.Read();
        Assert.NotNull(v);
        Assert.InRange(v!.Value, 0, 100);
    }
}

public sealed class GpuMathTests
{
    [Theory]
    [InlineData("pid_1234_luid_0x00000000_0x0000F5A8_phys_0_eng_0_engtype_3D", "0x00000000_0x0000F5A8", "3D")]
    [InlineData("pid_4_luid_0x00000000_0x0000F5A8_phys_0_eng_5_engtype_VideoDecode", "0x00000000_0x0000F5A8", "VideoDecode")]
    [InlineData("pid_99_luid_0x00000000_0x00010001_phys_0_eng_2_engtype_Copy", "0x00000000_0x00010001", "Copy")]
    public void Instance_names_are_parsed(string name, string luid, string engine)
    {
        Assert.True(Gpu.TryParse(name, out var l, out var e));
        Assert.Equal(luid, l);
        Assert.Equal(engine, e);
    }

    [Theory]
    [InlineData("")]
    [InlineData("_Total")]
    [InlineData("pid_1_luid_x")]
    public void Unrelated_names_are_ignored(string name) => Assert.False(Gpu.TryParse(name, out _, out _));

    [Fact]
    public void Programs_add_up_per_engine_and_the_busiest_engine_wins()
    {
        double pct = Gpu.Aggregate(new[]
        {
            ("pid_1_luid_A_phys_0_eng_0_engtype_3D", 30.0),
            ("pid_2_luid_A_phys_0_eng_0_engtype_3D", 25.0),         // 3D engine total: 55
            ("pid_2_luid_A_phys_0_eng_1_engtype_Copy", 10.0),
        });
        Assert.Equal(55.0, pct, 3);
    }

    [Fact]
    public void Two_adapters_are_not_added_together()
    {
        double pct = Gpu.Aggregate(new[]
        {
            ("pid_1_luid_A_phys_0_eng_0_engtype_3D", 20.0),
            ("pid_1_luid_B_phys_0_eng_0_engtype_3D", 70.0),
        });
        Assert.Equal(70.0, pct, 3);
    }

    [Fact]
    public void Idle_or_empty_is_zero_and_the_total_never_exceeds_100()
    {
        Assert.Equal(0.0, Gpu.Aggregate(Array.Empty<(string, double)>()));
        Assert.Equal(0.0, Gpu.Aggregate(new[] { ("_Total", 50.0) }));
        Assert.Equal(100.0, Gpu.Aggregate(new[]
        {
            ("pid_1_luid_A_phys_0_eng_0_engtype_3D", 80.0),
            ("pid_2_luid_A_phys_0_eng_0_engtype_3D", 60.0),
        }));
    }

    [Fact]
    public void The_meter_either_works_or_says_it_is_unavailable()
    {
        using var m = new GpuMeter();
        if (!m.Available) return;                                      // PCs / CI runners without GPU counters: nothing to read
        m.Read();
        Thread.Sleep(1100);
        var v = m.Read();
        if (v != null) Assert.InRange(v.Value, 0, 100);
    }
}

public sealed class HardwareTests
{
    [Fact]
    public void Hardware_names_never_throw_and_are_tidy()
    {
        foreach (var n in Hardware.GpuNames())
        {
            Assert.False(string.IsNullOrWhiteSpace(n));
            Assert.Equal(n.Trim(), n);
            Assert.DoesNotContain("  ", n);
        }
        Assert.NotNull(Hardware.CpuName());
    }
}

public sealed class LineGraphTests
{
    private static Bitmap Render(LineGraph g)
    {
        g.Size = new Size(300, 220);
        var bmp = new Bitmap(g.Width, g.Height);
        g.DrawToBitmap(bmp, new Rectangle(0, 0, g.Width, g.Height));
        return bmp;
    }

    private static bool HasInk(Bitmap b)
    {
        for (int y = 0; y < b.Height; y += 2)
            for (int x = 0; x < b.Width; x += 2)
                if (b.GetPixel(x, y).ToArgb() != Theme.Card.ToArgb()) return true;
        return false;
    }

    [Fact]
    public void Both_states_render_something_and_never_throw()
    {
        var withData = new LineGraph();
        var rnd = new Random(1);
        for (int i = 0; i < 90; i++) withData.Push(20 + 60 * Math.Abs(Math.Sin(i / 9.0)) + rnd.Next(0, 10));
        var none = new LineGraph();
        none.SetUnavailable("GPU usage is not available on this PC.\nIt needs Windows 10 (1709 or newer) and a recent graphics driver.");

        using var a = Render(withData);
        using var b = Render(none);
        Assert.True(HasInk(a));
        Assert.True(HasInk(b));

        string? dir = Environment.GetEnvironmentVariable("ARGUS_TEST_SNAPSHOT_DIR");   // optional: save the pictures to look at them
        if (!string.IsNullOrEmpty(dir)) { a.Save(Path.Combine(dir, "graph-data.png")); b.Save(Path.Combine(dir, "graph-unavailable.png")); }
    }
    [Fact]
    public void History_is_capped_and_values_are_clamped()
    {
        var g = new LineGraph { Capacity = 30 };
        for (int i = 0; i < 100; i++) g.Push(i * 3);                  // goes well past 100
        Assert.Equal(30, g.Count);
        Assert.Equal(100, g.Last);
    }

    [Fact]
    public void An_empty_graph_has_no_last_value() => Assert.Null(new LineGraph().Last);
}
