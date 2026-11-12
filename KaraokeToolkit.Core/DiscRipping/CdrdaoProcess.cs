using System.Diagnostics;
using System.Text;
namespace KaraokeToolkit.Core.DiscRipping;

internal static class CdrdaoProcess
{
    internal static string Resolve() => Resolve(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("KARAOKETOOLKIT_CDRDAO"));
    internal static string Resolve(string baseDirectory, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? Path.GetFullPath(configured) : throw new FileNotFoundException("Configured cdrdao executable is missing.", configured);
        string bundled = Path.Combine(baseDirectory, "cdrdao", OperatingSystem.IsWindows() ? "cdrdao.exe" : "cdrdao");
        return File.Exists(bundled) ? bundled : "cdrdao"; // Installed tools on Linux/macOS can be resolved through PATH.
    }
    internal static async Task<string> RunAsync(ProcessStartInfo start, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        start.UseShellExecute = false; start.CreateNoWindow = true;
        start.RedirectStandardOutput = true; start.RedirectStandardError = true; start.RedirectStandardInput = true;
        Process process;
        try { process = Process.Start(start) ?? throw new IOException("Could not start cdrdao."); }
        catch (System.ComponentModel.Win32Exception error)
        { throw new IOException("cdrdao is unavailable. Install it or configure KARAOKETOOLKIT_CDRDAO with its executable path.", error); }
        using (process)
        {
            process.StandardInput.Close();
            var output = Drain(process.StandardOutput);
            var errors = Drain(process.StandardError);
            try { await process.WaitForExitAsync(token); }
            catch
            {
                try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync();
                await Task.WhenAll(output, errors);
                throw;
            }
            string stdout = await output, stderr = await errors;
            if (process.ExitCode != 0) throw new IOException($"cdrdao exited with code {process.ExitCode}: {stderr}");
            return stdout + "\n" + stderr;
        }
    }
    private static async Task<string> Drain(StreamReader stream)
    {
        var result = new StringBuilder();
        char[] buffer = new char[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer)) != 0)
            if (result.Length < 65536) result.Append(buffer, 0, Math.Min(count, 65536 - result.Length));
        return result.ToString();
    }
}
