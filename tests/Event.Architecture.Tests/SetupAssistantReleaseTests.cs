namespace Event.Architecture.Tests;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

public sealed class SetupAssistantReleaseTests
{
    private static readonly string[] ExpectedRuntimeIdentifiers =
        ["linux-x64", "linux-arm64", "osx-arm64", "win-x64"];

    private static readonly string[] ForbiddenReleaseTerms =
        ["Avalonia", "Terminal.Gui", "CommunityToolkit.Mvvm", "OpenTelemetry",
            "ApplicationInsights", "Sentry", "NewRelic", "Datadog", "Telemetry"];

    [Test]
    public async Task ReleaseProject_EvaluatesExactStandaloneRuntimeMatrix()
    {
        string contractPath = Path.Combine(
            Path.GetTempPath(),
            "event-setup-release-contract",
            Guid.NewGuid().ToString("N"),
            "release-contract.json");
        try
        {
            ProcessResult result = await RunProcessAsync(
                "dotnet",
                [
                    "msbuild",
                    ReleaseProjectPath(),
                    "-target:WriteSetupAssistantReleaseContract",
                    $"-property:SetupAssistantContractOutput={contractPath}",
                    "-nologo",
                    "-verbosity:minimal"
                ]);
            await Assert.That(result.ExitCode).IsEqualTo(0)
                .Because(result.Output);

            using JsonDocument contract = JsonDocument.Parse(
                await File.ReadAllBytesAsync(contractPath));
            JsonElement root = contract.RootElement;
            string[] runtimeIdentifiers = root.GetProperty("runtimeIdentifiers")
                .EnumerateArray()
                .Select(item => item.GetString()!)
                .ToArray();

            await Assert.That(runtimeIdentifiers)
                .IsEquivalentTo(ExpectedRuntimeIdentifiers);
            await Assert.That(root.GetProperty("selfContained").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("publishSingleFile").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("publishTrimmed").GetBoolean()).IsFalse();
            await Assert.That(root.GetProperty("enableCompressionInSingleFile").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("includeNativeLibrariesForSelfExtract").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("useAppHost").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("debugSymbols").GetBoolean()).IsFalse();
            await Assert.That(root.GetProperty("executableName").GetString())
                .IsEqualTo("event-setup");
        }
        finally
        {
            string? directory = Path.GetDirectoryName(contractPath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    [NotInParallel]
    public async Task HostPublish_ProducesStandaloneCliWithoutUiOrTelemetryAssemblies()
    {
        string hostRuntimeIdentifier = RuntimeInformation.RuntimeIdentifier;
        await Assert.That(ExpectedRuntimeIdentifiers).Contains(hostRuntimeIdentifier)
            .Because("the release contract must include the current verification host");

        string outputRoot = Path.Combine(
            Path.GetTempPath(),
            "event-setup-release-contract",
            Guid.NewGuid().ToString("N"));
        try
        {
            ProcessResult result = await RunProcessAsync(
                "dotnet",
                [
                    "msbuild",
                    ReleaseProjectPath(),
                    "-target:PublishSetupAssistant",
                    $"-property:SetupAssistantRid={hostRuntimeIdentifier}",
                    $"-property:SetupAssistantOutputRoot={outputRoot}",
                    "-nologo",
                    "-verbosity:minimal"
                ]);
            await Assert.That(result.ExitCode).IsEqualTo(0)
                .Because(result.Output);

            string publishDirectory = Path.Combine(outputRoot, hostRuntimeIdentifier);
            string executableName = OperatingSystem.IsWindows()
                ? "event-setup.exe"
                : "event-setup";
            string executablePath = Path.Combine(publishDirectory, executableName);
            await Assert.That(File.Exists(executablePath)).IsTrue();

            string[] files = Directory.GetFiles(publishDirectory)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();
            string[] forbidden = files.Where(file => ForbiddenReleaseTerms.Any(term =>
                    file.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            await Assert.That(forbidden).IsEmpty();
            await Assert.That(files.Where(file => file.EndsWith(
                ".dll", StringComparison.OrdinalIgnoreCase))).IsEmpty()
                .Because("single-file publishing must not leave a managed assembly sidecar");

            using JsonDocument dependencies = JsonDocument.Parse(await File.ReadAllBytesAsync(
                ContextSystemHelpers.RepoPath("src", "Event.SetupAssistant.Cli", "bin", "Release",
                    "net10.0", hostRuntimeIdentifier, "Event.SetupAssistant.Cli.deps.json")));
            JsonProperty[] libraries = dependencies.RootElement.GetProperty("libraries")
                .EnumerateObject().ToArray();
            await Assert.That(libraries
                .Where(library => library.Value.GetProperty("type").GetString() == "package")
                .Select(library => library.Name)).IsEquivalentTo(
                ["Spectre.Console/0.57.2", "Spectre.Console.Ansi/0.57.2",
                 "Spectre.Console.Cli/0.56.1", "YamlDotNet/18.1.0"]);
            await Assert.That(libraries
                .Where(library => library.Value.GetProperty("type").GetString() == "project")
                .Select(library => library.Name.Split('/')[0])).IsEquivalentTo(
                ["Event.SetupAssistant.Cli", "Event.Setup.Core",
                 "Event.Setup.Artifacts", "Event.Wire.Contracts"]);
        }
        finally
        {
            if (Directory.Exists(outputRoot))
            {
                Directory.Delete(outputRoot, recursive: true);
            }
        }
    }

    private static string ReleaseProjectPath() =>
        ContextSystemHelpers.RepoPath(
            "eng", "setup-assistant", "SetupAssistant.Release.proj");

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = ContextSystemHelpers.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("release-process-start-failed");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            return new ProcessResult(
                process.ExitCode,
                await output + await error);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
