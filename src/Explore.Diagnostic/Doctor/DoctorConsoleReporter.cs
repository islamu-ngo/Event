namespace Explore.Diagnostic.Doctor;

public static class DoctorConsoleReporter
{
    public static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Explore doctor - read-only platform diagnostics");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  dotnet run --project Explore.Diagnostic -- [--root <path>] [--timeout-seconds <seconds>]");
        writer.WriteLine("  dotnet run --project Explore.Diagnostic -- ai-eval-report [--root <path>] [--output <path>]");
        writer.WriteLine();
        writer.WriteLine("The doctor does not migrate, seed, bootstrap, repair, or write secrets.");
    }

    public static void WriteReport(TextWriter writer, DoctorReport report)
    {
        writer.WriteLine("Explore Doctor");
        writer.WriteLine("==============");
        writer.WriteLine();

        foreach (var result in report.Results.OrderBy(result => result.Category).ThenBy(result => result.Code, StringComparer.Ordinal))
        {
            writer.WriteLine($"[{FormatStatus(result.Status)}] {result.Code} ({result.Category})");
            writer.WriteLine($"  {result.Summary}");
            writer.WriteLine($"  Fix: {result.Remediation}");
            writer.WriteLine($"  Docs: {result.DocsLink}");
            if (!string.IsNullOrWhiteSpace(result.RedactedEvidence))
            {
                writer.WriteLine($"  Evidence: {result.RedactedEvidence}");
            }

            writer.WriteLine();
        }

        writer.WriteLine($"Summary: {report.PassCount} PASS, {report.WarnCount} WARN, {report.FailCount} FAIL");
    }

    private static string FormatStatus(DoctorCheckStatus status) => status switch
    {
        DoctorCheckStatus.Pass => "PASS",
        DoctorCheckStatus.Warn => "WARN",
        DoctorCheckStatus.Fail => "FAIL",
        _ => status.ToString().ToUpperInvariant(),
    };
}
