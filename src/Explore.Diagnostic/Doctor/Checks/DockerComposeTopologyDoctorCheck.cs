using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class DockerComposeTopologyDoctorCheck(IDoctorFileSystem fileSystem, string repositoryRoot) : IDoctorCheck
{
    private static readonly string[] RequiredServices =
    [
        "postgres:",
        "redis:",
        "keycloak:",
        "islamu-event-api:",
        "islamu-event-ui:",
    ];

    public string Code => "compose.topology";
    public DoctorCheckCategory Category => DoctorCheckCategory.Topology;

    public Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var composePath = Path.Combine(repositoryRoot, "docker-compose.yml");
        if (!fileSystem.FileExists(composePath))
        {
            return Task.FromResult(DoctorCheckResult.Fail(
                Code,
                Category,
                "docker-compose.yml is missing, so self-hosting service topology cannot be checked.",
                "Restore docker-compose.yml or run doctor from the repository root.",
                "docs/internal/SELF_HOSTING.md"));
        }

        var compose = fileSystem.ReadAllText(composePath);
        var missing = RequiredServices.Where(service => !compose.Contains($"  {service}", StringComparison.Ordinal)).ToList();
        if (missing.Count > 0)
        {
            return Task.FromResult(DoctorCheckResult.Fail(
                Code,
                Category,
                "Docker Compose is missing required platform services.",
                $"Restore service definitions for: {string.Join(", ", missing.Select(service => service.TrimEnd(':')))}.",
                "docs/internal/SELF_HOSTING.md"));
        }

        if (compose.Contains("API_ENDPOINT: ${API_ENDPOINT:-http://eventapi:8080/}", StringComparison.Ordinal) ||
            compose.Contains("API_ENDPOINT: ${API_ENDPOINT:-http://explore-api:8080/}", StringComparison.Ordinal))
        {
            return Task.FromResult(DoctorCheckResult.Warn(
                Code,
                Category,
                "Compose default API_ENDPOINT points at a stale API service name.",
                "Set API_ENDPOINT explicitly or change the default to http://islamu-event-api:8080/ before relying on Compose-only BFF routing.",
                "docs/internal/SELF_HOSTING.md",
                "API_ENDPOINT default uses a stale service name; service is islamu-event-api."));
        }

        return Task.FromResult(DoctorCheckResult.Pass(
            Code,
            Category,
            "Docker Compose contains the required core services and no known service-name drift was detected.",
            "Run `docker compose config` manually before production rollout if Compose files are overridden.",
            "docs/internal/SELF_HOSTING.md"));
    }
}
