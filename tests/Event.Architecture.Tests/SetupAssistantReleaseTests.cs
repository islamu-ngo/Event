namespace Event.Architecture.Tests;

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;

public sealed class SetupAssistantReleaseTests
{
    private const string DesktopSurface = "desktop";

    private static readonly string[] ExpectedRuntimeIdentifiers =
        ["linux-x64", "linux-arm64", "osx-arm64", "win-x64"];

    private static readonly string[] ForbiddenReleaseTerms =
        ["Avalonia", "Terminal.Gui", "CommunityToolkit.Mvvm", "OpenTelemetry",
            "ApplicationInsights", "Sentry", "NewRelic", "Datadog", "Telemetry",
            "SetupLive", "Explore.Blazor", "Explore.API", "Refit", "RestSharp",
            "Avalonia.Headless", "Avalonia.Fonts.Inter", "TUnit", "bunit"];

    [Test]
    [Arguments("cli", "event-setup")]
    [Arguments("terminal", "event-setup-terminal")]
    [Arguments(DesktopSurface, "event-setup-desktop")]
    public async Task ReleaseProject_EvaluatesExactStandaloneRuntimeMatrix(string surface, string executable)
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
                    $"-property:SetupAssistantSurface={surface}",
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
                .IsEquivalentTo(surface == DesktopSurface ? ["linux-x64"] : ExpectedRuntimeIdentifiers);
            await Assert.That(root.GetProperty("selfContained").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("publishSingleFile").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("publishTrimmed").GetBoolean()).IsFalse();
            await Assert.That(root.GetProperty("enableCompressionInSingleFile").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("includeNativeLibrariesForSelfExtract").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("useAppHost").GetBoolean()).IsTrue();
            await Assert.That(root.GetProperty("debugSymbols").GetBoolean()).IsFalse();
            await Assert.That(root.GetProperty("executableName").GetString())
                .IsEqualTo(executable);
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
    [Arguments("cli", "event-setup")]
    [Arguments("terminal", "event-setup-terminal")]
    [Arguments(DesktopSurface, "event-setup-desktop")]
    public async Task HostPublish_ProducesIndependentOfflineNativeTarget(string surface, string executable)
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
                    $"-property:SetupAssistantSurface={surface}",
                    $"-property:SetupAssistantRid={hostRuntimeIdentifier}",
                    $"-property:SetupAssistantOutputRoot={outputRoot}",
                    "-nologo",
                    "-verbosity:minimal"
                ]);
            if (surface == DesktopSurface && hostRuntimeIdentifier != "linux-x64")
            {
                await Assert.That(result.ExitCode).IsNotEqualTo(0)
                    .Because("desktop publication must refuse hosts without release evidence");
                return;
            }
            await Assert.That(result.ExitCode).IsEqualTo(0)
                .Because(result.Output);

            string publishDirectory = surface == "cli"
                ? Path.Combine(outputRoot, hostRuntimeIdentifier)
                : Path.Combine(outputRoot, surface, hostRuntimeIdentifier);
            string executableName = executable + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
            string executablePath = Path.Combine(publishDirectory, executableName);
            await Assert.That(File.Exists(executablePath)).IsTrue();

            string[] files = Directory.GetFiles(publishDirectory)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Cast<string>()
                .ToArray();
            IEnumerable<string> forbiddenTerms = surface switch
            {
                "terminal" => ForbiddenReleaseTerms.Where(term => term is not "Terminal.Gui" and not "CommunityToolkit.Mvvm"),
                DesktopSurface => ForbiddenReleaseTerms.Where(term => term is not "Avalonia" and not "CommunityToolkit.Mvvm"),
                _ => ForbiddenReleaseTerms
            };
            string[] forbidden = files.Where(file => forbiddenTerms.Any(term =>
                    file.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            await Assert.That(forbidden).IsEmpty();
            await Assert.That(files.Where(file => file.EndsWith(
                ".dll", StringComparison.OrdinalIgnoreCase))).IsEmpty()
                .Because("single-file publishing must not leave a managed assembly sidecar");

            string project = surface switch
            {
                DesktopSurface => "Event.SetupAssistant.Desktop",
                "terminal" => "Event.SetupAssistant.Terminal",
                _ => "Event.SetupAssistant.Cli"
            };
            using JsonDocument dependencies = JsonDocument.Parse(await File.ReadAllBytesAsync(
                ContextSystemHelpers.RepoPath("src", project, "bin", "Release",
                    "net10.0", hostRuntimeIdentifier, project + ".deps.json")));
            JsonProperty[] libraries = dependencies.RootElement.GetProperty("libraries")
                .EnumerateObject().ToArray();
            await Assert.That(libraries.Where(library => forbiddenTerms.Any(term =>
                library.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))).IsEmpty();
            if (surface == "cli")
                await Assert.That(libraries
                    .Where(library => library.Value.GetProperty("type").GetString() == "package")
                    .Select(library => library.Name)).IsEquivalentTo(
                    ["Spectre.Console/0.57.2", "Spectre.Console.Ansi/0.57.2",
                     "Spectre.Console.Cli/0.56.1", "YamlDotNet/18.1.0"]);
            string[] expectedProjects = surface == "cli"
                ? ["Event.SetupAssistant.Cli", "Event.Setup.Core", "Event.Setup.Artifacts", "Event.Wire.Contracts"]
                : [project, "Event.SetupAssistant", "Event.Setup.Core",
                   "Event.Setup.Artifacts", "Event.Wire.Contracts"];
            await Assert.That(libraries
                .Where(library => library.Value.GetProperty("type").GetString() == "project")
                .Select(library => library.Name.Split('/')[0])).IsEquivalentTo(expectedProjects);
            if (surface == DesktopSurface)
            {
                using FileStream assembly = File.OpenRead(ContextSystemHelpers.RepoPath(
                    "src", project, "bin", "Release", "net10.0", hostRuntimeIdentifier, project + ".dll"));
                using var portableExecutable = new PEReader(assembly);
                MetadataReader metadata = portableExecutable.GetMetadataReader();
                TypeDefinition application = metadata.TypeDefinitions
                    .Select(metadata.GetTypeDefinition)
                    .Single(type => metadata.GetString(type.Name) == "App"
                        && metadata.GetString(type.Namespace) == "ISLAMU.Event.SetupAssistant.Desktop");
                await Assert.That(application.GetMethods()
                    .Select(metadata.GetMethodDefinition)
                    .Any(method => metadata.GetString(method.Name).Contains("XamlIl", StringComparison.Ordinal)))
                    .IsTrue().Because("the published desktop must contain compiled application XAML");
            }
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
