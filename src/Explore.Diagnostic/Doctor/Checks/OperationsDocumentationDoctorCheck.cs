using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class OperationsDocumentationDoctorCheck(IDoctorFileSystem fileSystem, string repositoryRoot) : IDoctorCheck
{
    private static readonly string[] RequiredDocs =
    [
        "docs/internal/OPERATIONS.md",
        "docs/internal/TROUBLESHOOTING.md",
        "docs/internal/CONFIGURATION.md",
        "docs/internal/SELF_HOSTING.md",
        "docs/internal/SECRETS.md",
    ];

    public string Code => "docs.remediation-links";
    public DoctorCheckCategory Category => DoctorCheckCategory.Documentation;

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var missing = RequiredDocs
            .Where(path => !fileSystem.FileExists(Path.Combine(repositoryRoot, path)))
            .ToList();

        if (missing.Count > 0)
        {
            return Task.FromResult(DoctorCheckResult.Warn(
                Code,
                Category,
                "Some doctor remediation documents are missing.",
                $"Restore or update remediation links for: {string.Join(", ", missing)}.",
                "docs/internal/OPERATIONS.md"));
        }

        return Task.FromResult(DoctorCheckResult.Pass(
            Code,
            Category,
            "Required operator remediation documents exist.",
            "Keep docs links current whenever doctor checks are added or changed.",
            "docs/internal/OPERATIONS.md"));
    }
}
