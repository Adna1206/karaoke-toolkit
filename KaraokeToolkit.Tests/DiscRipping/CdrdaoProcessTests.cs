using System.Diagnostics;
using KaraokeToolkit.Core.DiscRipping;
namespace KaraokeToolkit.Tests.DiscRipping;

public class CdrdaoProcessTests
{
    private static ProcessStartInfo Probe(string mode, string? pid = null)
    {
        var start = new ProcessStartInfo("dotnet");
        start.ArgumentList.Add(typeof(CdrdaoProcessTests).Assembly.Location);
        start.ArgumentList.Add("--cdrdao-process-probe"); start.ArgumentList.Add(mode);
        if (pid != null) start.ArgumentList.Add(pid);
        return start;
    }
    [Fact]
    public async Task SuccessfulProcessCapturesBothDiscoveryStreams()
    {
        string text = await CdrdaoProcess.RunAsync(Probe("ok"), default);
        Assert.Contains("stdout discovery", text); Assert.Contains("stderr discovery", text);
    }
    [Fact]
    public async Task NonzeroExitIncludesDiagnostic()
    {
        var error = await Assert.ThrowsAsync<IOException>(() => CdrdaoProcess.RunAsync(Probe("error"), default));
        Assert.Contains("code 3", error.Message); Assert.Contains("stderr discovery", error.Message);
    }
    [Fact]
    public async Task CancelledProcessIsTerminatedAndReaped()
    {
        string pid = Path.Combine(TestMediaFixture.NewFolder(), "pid");
        using var cancel = new CancellationTokenSource();
        var run = CdrdaoProcess.RunAsync(Probe("wait", pid), cancel.Token);
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(pid) && deadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        Assert.True(File.Exists(pid));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(int.Parse(File.ReadAllText(pid))));
    }
    [Fact]
    public async Task PreCancelledRequestNeverStartsAChild()
    {
        string pid = Path.Combine(TestMediaFixture.NewFolder(), "pid");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CdrdaoProcess.RunAsync(Probe("wait", pid), new(true)));
        Assert.False(File.Exists(pid));
    }

    [Fact]
    public async Task TimeoutCancellationTerminatesAndReapsAStalledChild()
    {
        string pid = Path.Combine(TestMediaFixture.NewFolder(), "pid");
        using var timeout = new CancellationTokenSource();
        var run = CdrdaoProcess.RunAsync(Probe("wait", pid), timeout.Token);
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(pid) && watch.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
            Assert.True(File.Exists(pid));
            timeout.CancelAfter(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(int.Parse(File.ReadAllText(pid))));
        }
        finally
        {
            timeout.Cancel();
            try { await run; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task FullOutputPipesAreDrainedWithoutDeadlockAndDiagnosticsAreBounded()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string text = await CdrdaoProcess.RunAsync(Probe("flood"), timeout.Token);
        Assert.Equal(new string('O', 65536) + "\n" + new string('E', 65536), text);
    }

    [Fact]
    public void RuntimeResolutionSupportsConfiguredBundledAndInstalledTools()
    {
        string folder = TestMediaFixture.NewFolder();
        Assert.Equal("cdrdao", CdrdaoProcess.Resolve(folder, null));
        Assert.Throws<FileNotFoundException>(() => CdrdaoProcess.Resolve(folder, Path.Combine(folder, "missing")));
        string configured = Path.Combine(folder, "custom cdrdao"); File.WriteAllText(configured, "fixture");
        Assert.Equal(configured, CdrdaoProcess.Resolve(folder, configured));
        string runtime = Directory.CreateDirectory(Path.Combine(folder, "cdrdao")).FullName;
        string bundled = Path.Combine(runtime, OperatingSystem.IsWindows() ? "cdrdao.exe" : "cdrdao"); File.WriteAllText(bundled, "fixture");
        Assert.Equal(bundled, CdrdaoProcess.Resolve(folder, null));
    }
}
