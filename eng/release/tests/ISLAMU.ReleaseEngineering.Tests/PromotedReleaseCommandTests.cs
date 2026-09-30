using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class PromotedReleaseCommandTests
{
    [Test]
    public async Task OnlyPublicationCommandsAreAcceptedAndLauncherCannotRecurse()
    {
        foreach (string[] args in new string[][]
        {
            [], ["run-promoted"], ["run-promoted", "run-promoted"],
            ["run-promoted", "verify-tools"], ["run-promoted", "sync-public-changelog; echo secret"],
        })
        {
            using var output = new StringWriter();
            await Assert.That(PromotedReleaseCommand.Run(args, output, RepositoryRoot.Find()))
                .IsEqualTo(Program.UsageError);
        }
    }

    [Test]
    public async Task MissingMutatedAndIncompletePromotionsNeverExecute()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string bundle = Path.Combine(fixture.Root, "bundle");
        string dll = Path.Combine(bundle, "bin", "ISLAMU.ReleaseEngineering.dll");
        using var output = new StringWriter();
        int incomplete = WithEnvironment(fixture, () => PromotedReleaseCommand.Run(
            ["run-promoted", "prepare-publication-inventory"], output, fixture.RepositoryPath));
        await Assert.That(incomplete).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.ToString().Trim()).IsEqualTo("promoted_command_runtime_incomplete");

        File.AppendAllText(dll, "mutated");
        output.GetStringBuilder().Clear();
        int mutated = WithEnvironment(fixture, () => PromotedReleaseCommand.Run(
            ["run-promoted", "sync-public-changelog"], output, fixture.RepositoryPath));
        await Assert.That(mutated).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.ToString().Trim()).IsEqualTo("promoted_command_trusted_bundle_invalid");
        File.Delete(dll);
        int missing = WithEnvironment(fixture, () => PromotedReleaseCommand.Run(
            ["run-promoted", "sync-public-changelog"], output, fixture.RepositoryPath));
        await Assert.That(missing).IsEqualTo(Program.ToolchainRejected);
    }

    [Test]
    public async Task IndependentlyPromotedRealEngineCreatesProposalAndPropagatesUsageExit()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        Require(fixture.VerifyCandidate("1.1.0", fixture.B));
        Require(fixture.VerifyTag("1.1.0", fixture.B, fixture.FirstTagObject));
        PromoteInstalledEngine(fixture);
        string inventory = Path.Combine(fixture.Root, "empty inventory.json");
        File.WriteAllText(inventory,
            """{"schemaVersion":"authorized-inventory.v1","producer":"final-lane","completeSet":true,"entries":[]}""");
        string proposal = Path.Combine(fixture.Root, "proposal with spaces");
        string refsBefore = fixture.AllRefs();
        string[] stagesBefore = Directory.GetDirectories(Path.GetTempPath(), "islamu-promoted-*");
        using var output = new StringWriter();
        int code = WithEnvironment(fixture, () => PromotedReleaseCommand.Run(
            ["run-promoted", "prepare-publication-inventory",
                "--inventory", inventory, "--retained-evidence", fixture.RepositoryPath,
                "--release-evidence", "docs/internal/releases/1.1.0/release-evidence.v1.json",
                "--release-directory", "docs/internal/releases/1.1.0",
                "--disclosure-approved", "1.1.0", "--output-directory", proposal],
            output, fixture.RepositoryPath));
        await Assert.That(code).IsEqualTo(Program.Success);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(proposal, "authorized-inventory.v1.json")));
        await Assert.That(document.RootElement.GetProperty("entries")[0].GetProperty("tagObjectId").GetString())
            .IsEqualTo(fixture.FirstTagObject);
        await Assert.That(File.Exists(Path.Combine(proposal, "publication-approvals", "1.1.0.json"))).IsTrue();
        await Assert.That(fixture.AllRefs()).IsEqualTo(refsBefore);
        await Assert.That(Directory.GetDirectories(Path.GetTempPath(), "islamu-promoted-*"))
            .IsEquivalentTo(stagesBefore);
        await Assert.That(output.ToString().Trim()).IsEqualTo("promoted_command_completed");

        // This is the real promoted Program's usage exit, not a launcher validation result.
        output.GetStringBuilder().Clear();
        code = WithEnvironment(fixture, () => PromotedReleaseCommand.Run(
            ["run-promoted", "sync-public-changelog", "--unknown", "operator-secret-sentinel"],
            output, fixture.RepositoryPath));
        await Assert.That(code).IsEqualTo(Program.UsageError);
        await Assert.That(output.ToString().Trim()).IsEqualTo("promoted_command_completed");
        await Assert.That(output.ToString()).DoesNotContain("operator-secret-sentinel");
        await Assert.That(Directory.GetDirectories(Path.GetTempPath(), "islamu-promoted-*"))
            .IsEquivalentTo(stagesBefore);
    }

    [Test]
    public async Task AuthenticatedDepsCannotResolveAnUnpackagedRuntimeAssembly()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        PromoteInstalledEngine(fixture, omitDependency: true);
        using var output = new StringWriter();
        int code = WithEnvironment(fixture, () => PromotedReleaseCommand.Run(
            ["run-promoted", "prepare-publication-inventory"], output, fixture.RepositoryPath));
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.ToString().Trim()).IsEqualTo("promoted_command_runtime_incomplete");
    }

    private static void PromoteInstalledEngine(GovernedReleaseFixture fixture, bool omitDependency = false)
    {
        string bundle = Path.Combine(fixture.Root, "bundle");
        string bin = Path.Combine(bundle, "bin");
        string engineDirectory = Path.GetDirectoryName(typeof(Program).Assembly.Location)!;
        // Copy actual compiled product runtime, not a callback, stub, or candidate source tree.
        foreach (string name in new[]
        {
            "ISLAMU.ReleaseEngineering.dll", "ISLAMU.ReleaseEngineering.deps.json",
            "ISLAMU.ReleaseEngineering.runtimeconfig.json", "YamlDotNet.dll",
        }.Where(name => !omitDependency || name != "YamlDotNet.dll"))
        {
            File.Copy(Path.Combine(engineDirectory, name), Path.Combine(bin, name), overwrite: true);
        }
        string manifestPath = Path.Combine(bundle, "trusted-bundle.manifest.json");
        using JsonDocument original = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        Dictionary<string, object?> fields = original.RootElement.EnumerateObject()
            .Where(property => property.Name != "files")
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        fields["files"] = Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories)
            .Where(path => path != manifestPath)
            .Select(path => new { path = Path.GetRelativePath(bundle, path).Replace(Path.DirectorySeparatorChar, '/'), sha256 = Digest(path) })
            .OrderBy(item => item.path, StringComparer.Ordinal).ToArray();
        File.WriteAllBytes(manifestPath, ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(fields)).Bytes!);
        string authority = Path.Combine(fixture.Root, "authority");
        string receipt = Path.Combine(authority, "promotion-receipt.v1.json");
        using JsonDocument previous = JsonDocument.Parse(File.ReadAllBytes(receipt));
        Dictionary<string, object?> promotion = previous.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        promotion["bundleManifestSha256"] = Digest(manifestPath);
        File.WriteAllBytes(receipt, ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(promotion)).Bytes!);
        File.Delete(receipt + ".sig");
        Run("ssh-keygen", "-Y", "sign", "-f", Path.Combine(authority, "promotion-key"),
            "-n", "islamu-release-promotion", receipt);
    }

    private static int WithEnvironment(GovernedReleaseFixture fixture, Func<int> action)
    {
        string bundle = Path.Combine(fixture.Root, "bundle");
        string authority = Path.Combine(fixture.Root, "authority");
        var variables = new Dictionary<string, string>
        {
            ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundle,
            ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = Path.Combine(authority, "promotion-receipt.v1.json"),
            ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = Path.Combine(authority, "promotion-receipt.v1.json.sig"),
            ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
            ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundle, "trusted-bundle.manifest.json")),
            ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
            ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
            ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
            ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
            ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
            ["DOTNET_STARTUP_HOOKS"] = Path.Combine(fixture.Root, "must-not-load.dll"),
        };
        Dictionary<string, string?> originals = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        using RuntimePromotionTrustRootScope trust = RuntimePromotionTrustRootScope.Use(Path.Combine(authority, "allowed-promoters"));
        try
        {
            foreach ((string name, string value) in variables) Environment.SetEnvironmentVariable(name, value);
            return action();
        }
        finally
        {
            foreach ((string name, string? value) in originals) Environment.SetEnvironmentVariable(name, value);
        }
    }

    private static void Require((int ExitCode, string Output) result)
    {
        if (result.ExitCode != Program.Success) throw new InvalidOperationException(result.Output);
    }

    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static void Run(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("promotion_fixture_timeout");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException("promotion_fixture_failed");
    }
}
