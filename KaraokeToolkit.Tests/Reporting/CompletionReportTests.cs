using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.FileOperations;
using KaraokeToolkit.Core.Recovery;
using KaraokeToolkit.Core.Reporting;
using KaraokeToolkit.Core.Validation;
using System.IO.Compression;
using System.Text.Json;
using KaraokeToolkit.App.Services;
using KaraokeToolkit.Core;
using Action = KaraokeToolkit.Core.Conversion.OperationCatalog.defAction;

namespace KaraokeToolkit.Tests.Reporting;

public class CompletionReportTests
{
    [Fact]
    public void ReportsPreserveStatusesOutputsAndMessages()
    {
        var report = CompletionReport.FromConversion("Extract", new([new("a.zip", ConversionStatus.Succeeded, ["a.cdg"], "done"), new("b.zip", ConversionStatus.Failed, [], "CRC failed")], false));
        Assert.Contains("CRC failed", report.ToText());
        Assert.Contains("Output: a.cdg", report.ToText());
        using var parsed = JsonDocument.Parse(report.ToJson());
        Assert.Equal(2, parsed.RootElement.GetProperty("Entries").GetArrayLength());
        Assert.Equal(ApplicationInfo.Name, parsed.RootElement.GetProperty("Header").GetProperty("AppName").GetString());
        Assert.Equal(report.Header.AppVersion, parsed.RootElement.GetProperty("Header").GetProperty("AppVersion").GetString());
        Assert.StartsWith("App Name: " + ApplicationInfo.Name, report.ToText());
        Assert.Contains("Report Type: Conversion Report", report.ToText());
        Assert.DoesNotContain("REM ", report.ToText());
        var stamp = new DateTimeOffset(2026, 9, 28, 15, 4, 5, TimeSpan.FromHours(2));
        var header = new ReportHeader("Validation Report", stamp);
        Assert.Equal("2026-09-28", header.Date);
        Assert.Equal("15:04:05 +02:00", header.Time);
        Assert.Equal("Validation Report", CompletionReport.FromValidation(new ValidationResult([], false, TimeSpan.Zero)).Header.ReportType);
    }
}
