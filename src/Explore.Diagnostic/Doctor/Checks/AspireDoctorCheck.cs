using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class AspireDoctorCheck(IDoctorProcessRunner processRunner) : IDoctorCheck
{
    public string Code => "tooling.aspire";
    public DoctorCheckCategory Category => DoctorCheckCategory.Tooling;

    public async Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("aspire", "--version", cancellationToken);
        if (result.ExitCode == 0)
        {
            return DoctorCheckResult.Pass(
                Code,
                Category,
                "Aspire CLI is available for local orchestration diagnostics.",
                "Use `aspire start --isolated` only when intentionally running the stack; doctor does not start Aspire.",
                "docs/internal/OPERATIONS.md#local-startup-topology-aspire",
                DoctorRedactor.Redact(result.StandardOutput.Trim()));
        }

        return DoctorCheckResult.Warn(
            Code,
            Category,
            "Aspire CLI was not found or did not return a version.",
            "Install/configure the Aspire CLI before using AppHost diagnostics, or use Docker Compose for self-hosting checks.",
            "docs/internal/OPERATIONS.md#local-startup-topology-aspire",
            DoctorRedactor.Redact(string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError));
    }
}
