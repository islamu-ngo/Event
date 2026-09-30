using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class PublicationInventoryCommandTests
{
    [Test]
    public async Task SignedReleaseProducesUnsignedProposalAndRepeatHasNoDuplicates()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        RestoreEvidence(fixture);
        string inventory = EmptyInventory(fixture);
        string proposal = Path.Combine(fixture.Root, "first-proposal");
        (int code, string output) = Prepare(fixture, inventory, "1.1.0", proposal);
        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output.Trim()).IsEqualTo("publication_inventory_pending_signature");
        string proposedInventory = Path.Combine(proposal, "authorized-inventory.v1.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(proposedInventory));
        JsonElement entry = document.RootElement.GetProperty("entries")[0];
        await Assert.That(entry.GetProperty("tagObjectId").GetString()).IsEqualTo(fixture.FirstTagObject);
        await Assert.That(entry.GetProperty("targetOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(entry.GetProperty("sourceDocuments").GetArrayLength()).IsEqualTo(1);
        string receipt = Path.Combine(proposal, "publication-approvals", "1.1.0.json");
        await Assert.That(entry.GetProperty("authorizationEvidenceSha256").GetString()).IsEqualTo(Digest(receipt));
        await Assert.That(File.Exists(proposedInventory + ".sig")).IsFalse();
        RetainApprovals(fixture, proposal);
        // A retained detached signature must survive unchanged. Proposals never inherit it.
        File.WriteAllText(proposedInventory + ".sig", "accepted-signature-sentinel");
        string repeated = Path.Combine(fixture.Root, "repeat-proposal");
        (code, _) = Prepare(fixture, proposedInventory, "1.1.0", repeated);
        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(File.ReadAllBytes(Path.Combine(repeated, "authorized-inventory.v1.json")))
            .IsEquivalentTo(File.ReadAllBytes(proposedInventory));
        await Assert.That(File.ReadAllText(proposedInventory + ".sig")).IsEqualTo("accepted-signature-sentinel");
        await Assert.That(File.Exists(Path.Combine(repeated, "authorized-inventory.v1.json.sig"))).IsFalse();
    }

    [Test]
    public async Task AllLineUnionRetainsUnpublishedMiddleReleaseAndRejectsOmission()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        RestoreEvidence(fixture);
        string inventory = EmptyInventory(fixture);
        foreach (string version in new[] { "1.1.0", "1.1.1" })
        {
            string proposal = Path.Combine(fixture.Root, "proposal-" + version);
            (int code, string output) = Prepare(fixture, inventory, version, proposal);
            if (code != Program.Success) throw new InvalidOperationException(output);
            RetainApprovals(fixture, proposal);
            inventory = Path.Combine(proposal, "authorized-inventory.v1.json");
        }
        AddMainlineRelease(fixture);
        string allLines = Path.Combine(fixture.Root, "all-lines");
        (int result, string diagnostic) = Prepare(fixture, inventory, "2.0.0", allLines);
        if (result != Program.Success) throw new InvalidOperationException(diagnostic);
        using JsonDocument union = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(allLines, "authorized-inventory.v1.json")));
        JsonElement[] entries = union.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        await Assert.That(entries.Select(entry => entry.GetProperty("version").GetString()!).ToArray())
            .IsEquivalentTo(new[] { "1.1.0", "1.1.1", "2.0.0" });
        await Assert.That(entries.Select(entry => entry.GetProperty("line").GetString()).Distinct().Count()).IsEqualTo(2);

        string omitted = Path.Combine(fixture.Root, "omitted");
        (result, diagnostic) = Prepare(fixture, EmptyInventory(fixture), "2.0.0", omitted);
        await Assert.That(result).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(diagnostic.Trim()).IsEqualTo("publication_inventory_retained_release_omitted");
        await Assert.That(Directory.Exists(omitted)).IsFalse();
    }

    [Test]
    public async Task MissingFinalEvidenceFailsBeforeAnyOutput()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        string proposal = Path.Combine(fixture.Root, "missing-evidence");
        (int code, string output) = Prepare(fixture, EmptyInventory(fixture), "1.1.0", proposal);
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.Trim()).IsEqualTo("inventory_evidence_missing");
        await Assert.That(Directory.Exists(proposal)).IsFalse();
    }

    [Test]
    public async Task DisclosureMustExplicitlyApproveTheReleaseVersion()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        RestoreEvidence(fixture);
        string proposal = Path.Combine(fixture.Root, "not-approved");
        (int code, string output) = Prepare(fixture, EmptyInventory(fixture), "1.1.0", proposal, approval: "1.1.1");
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.Trim()).IsEqualTo("publication_inventory_disclosure_approval_required");
        await Assert.That(Directory.Exists(proposal)).IsFalse();
    }

    [Test]
    public async Task InvalidPromotionCannotBeSubstitutedByReleaseEvidence()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        RestoreEvidence(fixture);
        File.AppendAllText(Path.Combine(fixture.Root, "authority", "promotion-receipt.v1.json"), "tampered");
        string proposal = Path.Combine(fixture.Root, "unpromoted");
        (int code, string output) = Prepare(fixture, EmptyInventory(fixture), "1.1.0", proposal);
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.Trim()).IsEqualTo("release_trusted_bundle_invalid");
        await Assert.That(Directory.Exists(proposal)).IsFalse();
    }

    [Test]
    public async Task MovedTagCannotReuseRetainedFinalEvidence()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        RestoreEvidence(fixture);
        fixture.DeleteTag("v1.1.0");
        fixture.CreateUnsignedAnnotatedTag("v1.1.0", fixture.B, "replacement");
        string proposal = Path.Combine(fixture.Root, "moved-tag");
        (int code, string output) = Prepare(fixture, EmptyInventory(fixture), "1.1.0", proposal);
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output.Trim()).IsEqualTo("publication_inventory_release_signature_invalid");
        await Assert.That(Directory.Exists(proposal)).IsFalse();
    }

    [Test]
    public async Task TraversalSymlinksAndExistingOutputAreRejected()
    {
        using GovernedReleaseFixture fixture = GovernedReleaseFixture.CreateSha1();
        RestoreEvidence(fixture);
        string inventory = EmptyInventory(fixture);
        string proposal = Path.Combine(fixture.Root, "existing");
        Directory.CreateDirectory(proposal);
        File.WriteAllText(Path.Combine(proposal, "authorized-inventory.v1.json.sig"), "keep");
        (int code, _) = Prepare(fixture, inventory, "1.1.0", proposal);
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(File.ReadAllText(Path.Combine(proposal, "authorized-inventory.v1.json.sig"))).IsEqualTo("keep");
        (code, _) = Prepare(fixture, inventory, "1.1.0", Path.Combine(fixture.Root, "safe"), evidence: "../release-evidence.v1.json");
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        string linked = Path.Combine(fixture.Root, "linked-inventory");
        File.CreateSymbolicLink(linked, inventory);
        (code, _) = Prepare(fixture, linked, "1.1.0", Path.Combine(fixture.Root, "symlink"));
        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(Directory.Exists(Path.Combine(fixture.Root, "safe"))).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(fixture.Root, "symlink"))).IsFalse();
    }

    [Test]
    public async Task MissingAndDuplicateFlagsAreUsageErrors()
    {
        using var output = new StringWriter();
        await Assert.That(PublicationInventoryCommand.Run(["prepare-publication-inventory"], output, RepositoryRoot.Find()))
            .IsEqualTo(Program.UsageError);
        string[] repeated = ["prepare-publication-inventory", "--inventory", "x", "--inventory", "x",
            "--inventory", "x", "--inventory", "x", "--inventory", "x", "--inventory", "x"];
        await Assert.That(PublicationInventoryCommand.Run(repeated, output, RepositoryRoot.Find())).IsEqualTo(Program.UsageError);
    }

    private static void RestoreEvidence(GovernedReleaseFixture fixture)
    {
        foreach ((string version, string target, string tag) in new[]
        {
            ("1.1.0", fixture.B, fixture.FirstTagObject), ("1.1.1", fixture.D, fixture.SecondTagObject),
        })
        {
            Require(fixture.VerifyCandidate(version, target));
            Require(fixture.VerifyTag(version, target, tag));
        }
    }

    private static void AddMainlineRelease(GovernedReleaseFixture fixture)
    {
        fixture.DeleteGeneratedManifests("1.1.0");
        fixture.DeleteGeneratedManifests("1.1.1");
        Git(fixture, "-c", "user.name=Release Test", "-c", "user.email=release@example.invalid",
            "commit", "--allow-empty", "-m", "fix(events): preserve mainline event notes");
        string release = Path.Combine(fixture.RepositoryPath, "docs", "internal", "releases", "2.0.0");
        Directory.CreateDirectory(release);
        File.WriteAllText(Path.Combine(release, "release.yaml"), $"""
            Version: 2.0.0
            Line: v2.0
            Release-Date: 2026-08-16
            Base-Stable-Tag: v1.1.1
            Previous-Published-Tag: v1.1.1
            Release-Range:
              Base-Ref: v1.1.1
              Base-Oid: {fixture.D}
              Previous-Ref: v1.1.1
              Previous-Oid: {fixture.D}
            Compatibility:
              - v2
            Impact-Dispositions:
              breaking: not-applicable
              security: not-applicable
              migration: not-applicable
              configuration: not-applicable
              openapi: not-applicable
              operator: not-applicable
            """ + "\n");
        File.WriteAllText(Path.Combine(release, "summary.md"), "Mainline event notes remain available.\n");
        WithEnvironment(fixture, () =>
        {
            using var output = new StringWriter();
            int code = PrepareCommand.Run(["prepare", "docs/internal/releases/2.0.0"],
                output, fixture.RepositoryPath, "linux-x64", TimeSpan.FromSeconds(10));
            Require((code, output.ToString()));
            return 0;
        });
        Git(fixture, "add", "docs/internal/releases/2.0.0");
        Git(fixture, "-c", "user.name=Release Test", "-c", "user.email=release@example.invalid",
            "commit", "-m", "docs(release): prepare 2.0.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit");
        string target = fixture.ResolveRef("HEAD");
        Require(fixture.VerifyCandidate("2.0.0", target));
        string tag = fixture.CreateSignedTag("v2.0.0", target, fixture.GenerateTagMessage("2.0.0"));
        Require(fixture.VerifyTag("2.0.0", target, tag));
        RestoreEvidence(fixture);
    }

    private static string EmptyInventory(GovernedReleaseFixture fixture)
    {
        string path = Path.Combine(fixture.Root, "empty-inventory.json");
        File.WriteAllText(path, """{"schemaVersion":"authorized-inventory.v1","producer":"final-lane","completeSet":true,"entries":[]}""");
        return path;
    }

    private static void RetainApprovals(GovernedReleaseFixture fixture, string proposal)
    {
        string destination = Path.Combine(fixture.RepositoryPath, "publication-approvals");
        Directory.CreateDirectory(destination);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(proposal, "publication-approvals")))
            File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), overwrite: true);
    }

    private static (int Code, string Output) Prepare(GovernedReleaseFixture fixture, string inventory,
        string version, string proposal, string? approval = null, string? evidence = null)
    {
        using var output = new StringWriter();
        int code = WithEnvironment(fixture, () => PublicationInventoryCommand.Run(
            ["prepare-publication-inventory", "--inventory", inventory, "--retained-evidence", fixture.RepositoryPath,
                "--release-evidence", evidence ?? $"docs/internal/releases/{version}/release-evidence.v1.json",
                "--release-directory", $"docs/internal/releases/{version}",
                "--disclosure-approved", approval ?? version, "--output-directory", proposal],
            output, fixture.RepositoryPath));
        return (code, output.ToString());
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

    private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require((int ExitCode, string Output) result)
    {
        if (result.ExitCode != Program.Success) throw new InvalidOperationException(result.Output);
    }

    private static void Git(GovernedReleaseFixture fixture, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = fixture.RepositoryPath, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            },
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Result);
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }
}
