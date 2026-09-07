using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Explore.Diagnostic.AiReplay;

public static class AiReplayReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters =
        {
            new JsonStringEnumConverter<AiReplayScenarioStatus>(),
            new JsonStringEnumConverter<AiReplayFailureClass>()
        }
    };

    public static AiReplayReportArtifact Write(AiReplayReport report, string outputDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("AI replay report output directory is required.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        var jsonPath = Path.Combine(outputDirectory, "ai-replay-report.json");
        var markdownPath = Path.Combine(outputDirectory, "ai-replay-report.md");

        File.WriteAllText(jsonPath, ToJson(report), Encoding.UTF8);
        File.WriteAllText(markdownPath, ToMarkdown(report), Encoding.UTF8);

        return new AiReplayReportArtifact(jsonPath, markdownPath);
    }

    public static string ToJson(AiReplayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string ToMarkdown(AiReplayReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var lines = new List<string>
        {
            "# AI Fake/Replay Usability Report",
            string.Empty,
            $"Generated: {report.GeneratedAtUtc.ToString("O", CultureInfo.InvariantCulture)}",
            $"Version: {report.ReportVersion}",
            $"Uses live provider credentials: {ToLowerInvariant(report.UsesLiveProviderCredentials)}",
            $"Contains content-bearing artifacts: {ToLowerInvariant(report.ContainsContentBearingArtifacts)}",
            $"Database side effects detected: {ToLowerInvariant(report.HasDatabaseSideEffects)}",
            $"Pass rate: {report.PassRate.ToString("P2", CultureInfo.InvariantCulture)}",
            string.Empty,
            FormattableString.Invariant($"Summary: {report.PassCount} PASS, {report.WarnCount} WARN, {report.FailCount} FAIL"),
            string.Empty,
            "| Status | Code | Failure class | Summary | Diagnostics |",
            "|---|---|---|---|---|",
        };

        foreach (var result in report.Results.OrderBy(result => result.Code, StringComparer.Ordinal))
        {
            lines.Add(FormattableString.Invariant(
                $"| {result.Status} | `{result.Code}` | {result.FailureClass} | {Escape(result.Summary)} | {Escape(result.Diagnostics)} |"));
        }

        lines.Add(string.Empty);
        lines.Add("> Fake/replay artifacts intentionally omit prompts, provider responses, selected-reference content, raw tool payloads, tenant/user identifiers, endpoint URLs, API keys, model secrets, screenshots with user content, and raw exception bodies. Live-provider usability runs remain manual/nightly only and must use separately governed artifact retention.");

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string ToLowerInvariant(bool value)
        => value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();

    private static string Escape(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace(Environment.NewLine, " ", StringComparison.Ordinal);
}

public sealed record AiReplayReportArtifact(string JsonPath, string MarkdownPath);
