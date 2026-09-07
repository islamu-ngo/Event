using System.Globalization;

namespace Explore.Diagnostic.AiReplay;

public static class AiReplayConsoleReporter
{
    public static void WriteHelp(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteLine("Usage: dotnet run --project Explore.Diagnostic -- ai-replay-report [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  --root <path>    Repository root. Defaults to auto-detected root.");
        writer.WriteLine("  --output <path>  Output directory. Defaults to artifacts/ai-replay.");
        writer.WriteLine("  -h, --help       Show this help text.");
    }

    public static void WriteReport(TextWriter writer, AiReplayReport report, AiReplayReportArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(artifact);

        writer.WriteLine("AI fake/replay usability report");
        writer.WriteLine($"Version: {report.ReportVersion}");
        writer.WriteLine(FormattableString.Invariant($"Summary: {report.PassCount} PASS, {report.WarnCount} WARN, {report.FailCount} FAIL"));
        writer.WriteLine($"Live provider credentials: {ToLowerInvariant(report.UsesLiveProviderCredentials)}");
        writer.WriteLine($"Content-bearing artifacts: {ToLowerInvariant(report.ContainsContentBearingArtifacts)}");
        writer.WriteLine($"JSON: {artifact.JsonPath}");
        writer.WriteLine($"Markdown: {artifact.MarkdownPath}");
    }

    private static string ToLowerInvariant(bool value)
        => value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
}
