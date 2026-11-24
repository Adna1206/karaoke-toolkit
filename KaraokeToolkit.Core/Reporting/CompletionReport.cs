using KaraokeToolkit.Core.Conversion;
using KaraokeToolkit.Core.Validation;
using System.Text;
using System.Text.Json;

namespace KaraokeToolkit.Core.Reporting;

public sealed record ReportEntry(string Source, string Status, string Message, IReadOnlyList<string> Outputs);
public sealed record CompletionReport(string Operation, DateTimeOffset CompletedAt, bool Cancelled, IReadOnlyList<ReportEntry> Entries)
{
    public string ReportType { get; init; } = "Operation Report";
    [System.Text.Json.Serialization.JsonPropertyOrder(-1)]
    public ReportHeader Header => new(ReportType, CompletedAt);
    public static CompletionReport FromConversion(string operation, ConversionResult result) => new(operation, DateTimeOffset.Now, result.Cancelled,
        result.Files.Select(f => new ReportEntry(f.Source, f.Status.ToString(),
            f.Warning == null ? f.Message : $"{f.Message} Warning — {f.Warning}", f.Outputs)).ToArray()) { ReportType = "Conversion Report" };
    public static CompletionReport FromValidation(ValidationResult result) => new("Validate Karaoke Files", DateTimeOffset.Now, result.Cancelled,
        result.Findings.Select(f => new ReportEntry(f.Source, f.Status.ToString(), f.Message, [])).ToArray()) { ReportType = "Validation Report" };
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
    public string ToText()
    {
        var text = new StringBuilder(Header.ToText()).AppendLine("Operation: " + Operation).AppendLine("Status: " + (Cancelled ? "Cancelled" : "Finished")).AppendLine();
        foreach (var item in Entries)
        {
            text.AppendLine($"{item.Status}: {item.Source}").AppendLine(item.Message);
            foreach (string output in item.Outputs)
                text.AppendLine("  Output: " + output);
            text.AppendLine();
        }
        return text.ToString();
    }
}
