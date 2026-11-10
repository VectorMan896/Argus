namespace Argus;

internal sealed partial class MainForm
{
    private LineGraph _cpuGraph = null!, _gpuGraph = null!;

    private (Card Cpu, Card Gpu) BuildGraphCards()
    {
        string cpuName = Hardware.CpuName();
        var cpuCard = new Card { Dock = DockStyle.Fill, Title = "CPU", Subtitle = $"{Environment.ProcessorCount} logical processors" };
        _cpuGraph = new LineGraph { Dock = DockStyle.Fill };
        cpuCard.Controls.Add(_cpuGraph);
        if (cpuName.Length > 0) _tt.SetToolTip(_cpuGraph, cpuName);

        var gpus = Hardware.GpuNames();
        var gpuCard = new Card
        {
            Dock = DockStyle.Fill, Title = "GPU",
            Subtitle = gpus.Count == 0 ? "Graphics" : gpus[0] + (gpus.Count > 1 ? $"  (+{gpus.Count - 1})" : ""),
        };
        _gpuGraph = new LineGraph { Dock = DockStyle.Fill };
        gpuCard.Controls.Add(_gpuGraph);
        if (gpus.Count > 0) _tt.SetToolTip(_gpuGraph, string.Join("\n", gpus) + "\nShows the busiest part of the GPU, like Task Manager does.");

        _sampler.Sampled += (c, g) => { if (IsHandleCreated) BeginInvoke(() => OnSample(c, g)); };
        Resize += (_, _) => UpdateSampling();
        VisibleChanged += (_, _) => UpdateSampling();
        return (cpuCard, gpuCard);
    }

    private void OnSample(double? cpu, double? gpu)
    {
        if (cpu != null) _cpuGraph.Push(cpu.Value);
        if (!_sampler.GpuAvailable)
            _gpuGraph.SetUnavailable("GPU usage is not available on this PC.\nIt needs Windows 10 (1709 or newer) and a recent graphics driver.");
        else
        {
            _gpuGraph.SetUnavailable(null);
            if (gpu != null) _gpuGraph.Push(gpu.Value);
        }
    }

    /// <summary>Only sample while the Overview is actually on screen: hidden or minimised costs nothing.</summary>
    private void UpdateSampling()
    {
        bool on = Visible && WindowState != FormWindowState.Minimized;
        _sampler.Enabled = on && _current == 0;
        bool disk = on && _current == 2;                                  // the Performance page; nothing is read for the disk anywhere else
        _diskSampler.WantUsers = disk;
        _diskSampler.Enabled = disk;
        if (!disk) { _diskRecent.Clear(); _diskTipShown = false; }
    }
}