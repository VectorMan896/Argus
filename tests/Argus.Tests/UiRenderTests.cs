namespace Argus.Tests;

/// <summary>Draws pieces of the interface to pictures: catches drawing errors, and can save them for a human to look at.</summary>
public sealed class UiRenderTests
{
    private static void Save(Bitmap b, string name)
    {
        string? dir = Environment.GetEnvironmentVariable("ARGUS_TEST_SNAPSHOT_DIR");   // optional: set it to keep the pictures
        if (!string.IsNullOrEmpty(dir)) b.Save(Path.Combine(dir, name));
    }

    private static Bitmap Draw(Control c, int width)
    {
        c.Width = width;
        var bmp = new Bitmap(c.Width, c.Height);
        c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
        return bmp;
    }

    private static bool HasInk(Bitmap b)
    {
        for (int y = 0; y < b.Height; y += 3)
            for (int x = 0; x < b.Width; x += 3)
                if (b.GetPixel(x, y).ToArgb() != Theme.Bg.ToArgb()) return true;
        return false;
    }

    [Fact]
    public void The_updates_card_draws_with_its_toggle_button_and_changing_status_text()
    {
        var card = new RowCard { Title = "Updates", Subtitle = "Off by default. Argus only contacts GitHub if you turn this on or press Check now." };
        var check = new PillButton("Check now", "", PillStyle.Secondary, 150);
        card.AddRow("Check for updates", "About once a day, from github.com only", new Toggle());
        card.AddRow("Argus 0.4.0", "Press Check now to look for a newer version", check);

        using (var before = Draw(card, 520)) { Assert.True(HasInk(before)); Save(before, "updates-card-before.png"); }

        card.SetHint(check, "Version 0.4.1 is available");
        using (var after = Draw(card, 520)) { Assert.True(HasInk(after)); Save(after, "updates-card-after.png"); }
    }

    [Fact]
    public void The_accent_button_and_all_pill_styles_draw()
    {
        foreach (var style in Enum.GetValues<PillStyle>())
        {
            var b = new PillButton("Update 0.4.1", "", style);
            b.BackColor = Theme.Bg;
            using var bmp = new Bitmap(b.Width, b.Height);
            b.DrawToBitmap(bmp, new Rectangle(0, 0, b.Width, b.Height));
            Assert.True(HasInk(bmp), $"style {style} drew nothing");
            if (style == PillStyle.Accent) Save(bmp, "pill-accent.png");
        }
    }
}
