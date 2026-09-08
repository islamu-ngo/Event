using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class BootstrapConfigurationDoctorCheck(IDoctorFileSystem fileSystem, string repositoryRoot) : IDoctorCheck
{
    private static readonly string[] RequiredDatabaseVariables =
    [
        "Database__Provider",
        "Database__Host",
        "Database__Port",
        "Database__Database",
        "Database__Runtime__Username",
        "Database__Runtime__Password",
        "Database__Migrator__Username",
        "Database__Migrator__Password",
    ];

    public string Code => "bootstrap.database.structured-env";
    public DoctorCheckCategory Category => DoctorCheckCategory.Bootstrap;

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var composePath = Path.Combine(repositoryRoot, "docker-compose.yml");
        if (!fileSystem.FileExists(composePath))
        {
            return Task.FromResult(DoctorCheckResult.Fail(
                Code,
                Category,
                "docker-compose.yml is missing, so bootstrap environment variables cannot be verified.",
                "Restore docker-compose.yml or run doctor from the repository root.",
                "docs/internal/SELF_HOSTING.md"));
        }

        var compose = fileSystem.ReadAllText(composePath);
        var activeComposeLines = string.Join(
            Environment.NewLine,
            compose
                .Split('\n')
                .Select(line => line.TrimStart())
                .Where(line => !line.StartsWith('#')));
        var missing = RequiredDatabaseVariables.Where(variable => !activeComposeLines.Contains(variable, StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            return Task.FromResult(DoctorCheckResult.Fail(
                Code,
                Category,
                "Compose database configuration is missing required structured runtime or migrator variables.",
                $"Add the missing variables to the database environment block: {string.Join(", ", missing)}.",
                "docs/internal/SECRETS.md"));
        }

        if (activeComposeLines.Contains("ConnectionStrings__DefaultConnection", StringComparison.Ordinal))
        {
            return Task.FromResult(DoctorCheckResult.Fail(
                Code,
                Category,
                "Compose configures raw ConnectionStrings__DefaultConnection instead of structured database settings.",
                "Remove the raw connection string and provide the Database__* runtime and migrator fields.",
                "docs/internal/SECRETS.md"));
        }

        return Task.FromResult(DoctorCheckResult.Pass(
            Code,
            Category,
            "Compose uses the structured database contract expected by runtime and migration composition.",
            "Keep Database__* values structured and never print credentials or derived connection strings in diagnostics.",
            "docs/internal/SECRETS.md"));
    }
}
