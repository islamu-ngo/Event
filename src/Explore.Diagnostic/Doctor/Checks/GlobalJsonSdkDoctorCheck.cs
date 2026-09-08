using System.Text.Json;
using Explore.Diagnostic.Doctor.Infrastructure;

namespace Explore.Diagnostic.Doctor.Checks;

public sealed class GlobalJsonSdkDoctorCheck(
    IDoctorFileSystem fileSystem,
    IDoctorProcessRunner processRunner,
    string repositoryRoot) : IDoctorCheck
{
    public string Code => "tooling.dotnet-sdk";
    public DoctorCheckCategory Category => DoctorCheckCategory.Tooling;

    public async Task<DoctorCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        var globalJsonPath = Path.Combine(repositoryRoot, "global.json");
        if (!fileSystem.FileExists(globalJsonPath))
        {
            return DoctorCheckResult.Warn(
                Code,
                Category,
                "global.json is missing, so the expected .NET SDK cannot be pinned.",
                "Restore global.json or document the supported SDK version in docs/internal/CONFIGURATION.md.",
                "docs/internal/CONFIGURATION.md");
        }

        var expected = ReadExpectedSdkVersion(fileSystem.ReadAllText(globalJsonPath));
        if (string.IsNullOrWhiteSpace(expected))
        {
            return DoctorCheckResult.Fail(
                Code,
                Category,
                "global.json does not contain sdk.version.",
                "Add sdk.version to global.json so contributors and CI use a deterministic SDK.",
                "docs/internal/CONFIGURATION.md");
        }

        var dotnet = await processRunner.RunAsync("dotnet", "--version", cancellationToken);
        if (dotnet.ExitCode != 0)
        {
            return DoctorCheckResult.Fail(
                Code,
                Category,
                "dotnet CLI is unavailable or failed to report a version.",
                $"Install the .NET SDK pinned in global.json ({expected}).",
                "docs/internal/CONFIGURATION.md",
                DoctorRedactor.Redact(string.IsNullOrWhiteSpace(dotnet.StandardError) ? dotnet.StandardOutput : dotnet.StandardError));
        }

        var actual = dotnet.StandardOutput.Trim();
        if (string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            return DoctorCheckResult.Pass(
                Code,
                Category,
                $"Installed .NET SDK matches global.json ({expected}).",
                "No action required.",
                "docs/internal/CONFIGURATION.md",
                $"dotnet --version: {DoctorRedactor.Redact(actual)}");
        }

        return DoctorCheckResult.Warn(
            Code,
            Category,
            $"Installed .NET SDK ({actual}) differs from global.json ({expected}).",
            "Install the pinned SDK or confirm the installed SDK roll-forward behavior before building.",
            "docs/internal/CONFIGURATION.md",
            $"expected={DoctorRedactor.Redact(expected)} actual={DoctorRedactor.Redact(actual)}");
    }

    private static string? ReadExpectedSdkVersion(string globalJson)
    {
        using var document = JsonDocument.Parse(globalJson);
        return document.RootElement.TryGetProperty("sdk", out var sdk)
            && sdk.TryGetProperty("version", out var version)
            ? version.GetString()
            : null;
    }
}
