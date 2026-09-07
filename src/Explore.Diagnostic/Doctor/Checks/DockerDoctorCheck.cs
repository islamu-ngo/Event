using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class DockerDoctorCheck(IDoctorProcessRunner processRunner) : IDoctorCheck
{
    public string Code => "tooling.docker";
    public DoctorCheckCategory Category => DoctorCheckCategory.Tooling;

    public async Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var docker = await processRunner.RunAsync("docker", "--version", cancellationToken);
        if (docker.ExitCode != 0)
        {
            return DoctorCheckResult.Fail(
                Code,
                Category,
                "Docker CLI is unavailable, so Compose-based self-hosting cannot be diagnosed.",
                "Install Docker and verify `docker --version` before running the stack.",
                "docs/internal/SELF_HOSTING.md",
                DoctorRedactor.Redact(string.IsNullOrWhiteSpace(docker.StandardError) ? docker.StandardOutput : docker.StandardError));
        }

        var compose = await processRunner.RunAsync("docker", "compose version", cancellationToken);
        if (compose.ExitCode != 0)
        {
            return DoctorCheckResult.Fail(
                Code,
                Category,
                "Docker is available but `docker compose` is unavailable.",
                "Install the Docker Compose plugin and verify `docker compose version`.",
                "docs/internal/SELF_HOSTING.md",
                DoctorRedactor.Redact(string.IsNullOrWhiteSpace(compose.StandardError) ? compose.StandardOutput : compose.StandardError));
        }

        return DoctorCheckResult.Pass(
            Code,
            Category,
            "Docker and Docker Compose are available for self-hosting diagnostics.",
            "Use `docker compose config` manually for full Compose interpolation checks; doctor does not start containers.",
            "docs/internal/SELF_HOSTING.md",
            DoctorRedactor.Redact($"{docker.StandardOutput.Trim()} | {compose.StandardOutput.Trim()}"));
    }
}
