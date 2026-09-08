using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.Diagnostic.AiEvaluation;

public static class AiEvaluationReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AiEvaluationReportArtifact Write(AiEvaluationReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var jsonPath = Path.Combine(outputDirectory, "ai-evaluation-report.json");
        var markdownPath = Path.Combine(outputDirectory, "ai-evaluation-report.md");

        File.WriteAllText(jsonPath, ToJson(report));
        File.WriteAllText(markdownPath, ToMarkdown(report));

        return new AiEvaluationReportArtifact(jsonPath, markdownPath);
    }

    public static string ToJson(AiEvaluationReport report)
        => JsonSerializer.Serialize(report, JsonOptions);

    public static string ToMarkdown(AiEvaluationReport report)
    {
        var lines = new List<string>
        {
            "# AI Evaluation Report",
            string.Empty,
            $"Generated: {report.GeneratedAtUtc:O}",
            $"Version: {report.ReportVersion}",
            $"Advisory only: {report.AdvisoryOnly.ToString().ToLowerInvariant()}",
            $"Hard CI gate: {report.ContainsHardCiGate.ToString().ToLowerInvariant()}",
            string.Empty,
            $"Summary: {report.PassCount} PASS, {report.WarnCount} WARN, {report.FailCount} FAIL",
            string.Empty,
            "| Status | Code | Dimension | Summary | Recommendation |",
            "|---|---|---|---|---|",
        };

        foreach (var result in report.Results.OrderBy(result => result.Code, StringComparer.Ordinal))
        {
            lines.Add($"| {result.Status} | `{result.Code}` | {result.Dimension} | {Escape(result.Summary)} | {Escape(result.Recommendation)} |");
        }

        lines.Add(string.Empty);
        lines.Add("> Report artifacts intentionally omit prompts, provider responses, selected-reference content beyond deterministic fixture labels, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, and raw exceptions.");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal);
}

public sealed record AiEvaluationReportArtifact(string JsonPath, string MarkdownPath);
