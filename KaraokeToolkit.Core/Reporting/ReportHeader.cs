using System.Globalization;

namespace KaraokeToolkit.Core.Reporting;

/// <summary>Shared identity and timestamp fields for exported reports and track metadata.</summary>
public sealed record ReportHeader(string ReportType, DateTimeOffset GeneratedAt)
{
    public string AppName => ApplicationInfo.Name;
    public string AppVersion => ApplicationInfo.Version;
    public string Date => GeneratedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string Time => GeneratedAt.ToString("HH:mm:ss zzz", CultureInfo.InvariantCulture);

    public string ToText(string prefix = "") => string.Join("\r\n", new[]
    {
        $"App Name: {AppName}", $"App Version: {AppVersion}",
        $"Date: {Date}", $"Time: {Time}", $"Report Type: {ReportType}"
    }.Select(line => prefix + line)) + "\r\n\r\n";
}
