// ABOUTME: Proves the durable CI release bundle consumes the final canonical manifest as identity.
// ABOUTME: Characterizes retained evidence categories and checksum coverage for .ci bundle scripts.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel]
public sealed class ReleaseEvidenceBundleScriptTests
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Test]
    public async Task BundleScriptPreservesExistingCategoriesAndUsesFinalManifestIdentity()
    {
        using var fixture = BundleFixture.Create();

        ScriptResult result = fixture.GenerateBundle();

        await Assert.That(result.ExitCode).IsEqualTo(0);
        using JsonDocument bundle = JsonDocument.Parse(File.ReadAllBytes(fixture.BundleJsonPath));
        JsonElement root = bundle.RootElement;
        JsonElement identity = root.GetProperty("releaseIdentity");
        string[] categories = root.GetProperty("artifacts").EnumerateArray()
            .Select(artifact => artifact.GetProperty("category").GetString()!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        string checksumText = File.ReadAllText(fixture.ChecksumPath);

        await Assert.That(identity.GetProperty("schemaVersion").GetString()).IsEqualTo("release-evidence.v1");
        await Assert.That(identity.GetProperty("version").GetString()).IsEqualTo("1.1.0");
        await Assert.That(identity.GetProperty("tagName").GetString()).IsEqualTo("v1.1.0");
        await Assert.That(identity.GetProperty("tagObjectId").GetString()).IsEqualTo(fixture.TagObjectId);
        await Assert.That(identity.GetProperty("targetOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(identity.GetProperty("candidateManifestSha256").GetString()).IsEqualTo(fixture.CandidateDigest);
        await Assert.That(identity.GetProperty("releaseDescriptorSha256").GetString()).IsEqualTo(fixture.DescriptorDigest);
        await Assert.That(identity.GetProperty("releaseSummarySha256").GetString()).IsEqualTo(fixture.SummaryDigest);
        await Assert.That(identity.GetProperty("releaseContextSha256").GetString()).IsEqualTo(fixture.ContextDigest);
        await Assert.That(identity.GetProperty("releaseNotesSha256").GetString()).IsEqualTo(fixture.NotesDigest);
        await Assert.That(identity.GetProperty("trustedBundleManifestSha256").GetString()).IsEqualTo(fixture.ArtifactDigest("trusted-bundle/trusted-bundle.manifest.json"));
        await Assert.That(identity.GetProperty("trustedBundlePolicySha256").GetString()).IsEqualTo(fixture.ArtifactDigest("trusted-bundle/policy/release-policy.yaml"));
        await Assert.That(identity.GetProperty("trustedBundleConfigSha256").GetString()).IsEqualTo(fixture.ArtifactDigest("trusted-bundle/config/cliff.toml"));
        await Assert.That(identity.GetProperty("trustedBundleTrustSha256").GetString()).IsEqualTo(fixture.ArtifactDigest("trusted-bundle/trust/allowed-signers"));
        await Assert.That(identity.GetProperty("trustedBundleToolchainSha256").GetString()).IsEqualTo(fixture.ArtifactDigest("trusted-bundle/toolchain.lock.json"));
        await Assert.That(identity.GetProperty("trustedBundleGitCliffSha256").GetString()).IsEqualTo(fixture.ArtifactDigest("trusted-bundle/git-cliff"));
        await Assert.That(root.GetProperty("releaseVersion").GetString()).IsEqualTo("1.1.0");
        await Assert.That(root.GetProperty("commitSha").GetString()).IsEqualTo(fixture.B);
        await Assert.That(categories).IsEquivalentTo([
            "container",
            "dependency",
            "deployment",
            "openapi",
            "release-governance",
            "release-identity",
            "scorecard",
            "secret-scanning",
            "security-tests",
            "signer-verification",
            "test-results",
            "trusted-tooling",
            "workflow-security"]);
        await Assert.That(checksumText).Contains("docs/internal/releases/1.1.0/release.yaml");
        await Assert.That(checksumText).Contains("docs/internal/releases/1.1.0/release-evidence.v1.json");
        await Assert.That(checksumText).Contains("trusted-bundle/trusted-bundle.manifest.json");
        await Assert.That(checksumText).Contains("trust/allowed-signers");
        await Assert.That(checksumText).Contains("signer/tag-verification.json");
        await Assert.That(checksumText).Contains("container/checksums.sha256");
        await Assert.That(root.GetProperty("artifacts").EnumerateArray().Count(artifact => artifact.GetProperty("relativePath").GetString()!.EndsWith("release-evidence.v1.json", StringComparison.Ordinal))).IsEqualTo(1);
        await Assert.That(Directory.EnumerateFiles(fixture.OutputRoot).Select(path => Path.GetFileName(path)!).Order(StringComparer.Ordinal)).IsEquivalentTo([
            "release-evidence-checksums.sha256",
            "release-evidence-release-notes.md",
            "release-evidence.json",
            "release-evidence.md",
        ]);
    }

    [Test]
    public async Task BundleScriptKeepsCanonicalIdentityAndChecksumsIndependentOfCollectionMetadata()
    {
        using var fixture = BundleFixture.Create();
        string secondOutput = Path.Combine(fixture.Root, "bundle-second");

        ScriptResult first = fixture.GenerateBundle();
        ScriptResult second = fixture.GenerateBundle(secondOutput, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GITHUB_REPOSITORY"] = "another/provider",
            ["GITHUB_RUN_ID"] = "999",
            ["GITHUB_RUN_ATTEMPT"] = "7",
            ["CLA_STATUS"] = "rechecked",
            ["CI_PROVIDER_URL"] = "https://provider.invalid/run/999",
        });

        await Assert.That(first.ExitCode).IsEqualTo(0);
        await Assert.That(second.ExitCode).IsEqualTo(0);
        using JsonDocument firstBundle = JsonDocument.Parse(File.ReadAllBytes(fixture.BundleJsonPath));
        using JsonDocument secondBundle = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(secondOutput, "release-evidence.json")));
        await Assert.That(firstBundle.RootElement.GetProperty("releaseIdentity").GetRawText()).IsEqualTo(secondBundle.RootElement.GetProperty("releaseIdentity").GetRawText());
        await Assert.That(File.ReadAllBytes(fixture.ChecksumPath)).IsEquivalentTo(File.ReadAllBytes(Path.Combine(secondOutput, "release-evidence-checksums.sha256")));
        await Assert.That(firstBundle.RootElement.GetProperty("generatedAtUtc").GetString()).IsNotEqualTo(string.Empty);
        await Assert.That(firstBundle.RootElement.GetProperty("runId").GetString()).IsNotEqualTo(secondBundle.RootElement.GetProperty("runId").GetString());
    }

    [Test]
    public async Task BundleScriptRejectsDestinationCollisionWithoutMutatingExistingOutput()
    {
        using var fixture = BundleFixture.Create();
        Directory.CreateDirectory(fixture.OutputRoot);
        string jsonCollision = Path.Combine(fixture.OutputRoot, "release-evidence.json");
        Directory.CreateDirectory(jsonCollision);
        string collisionMarker = Path.Combine(jsonCollision, "marker.txt");
        File.WriteAllText(collisionMarker, "keep collision marker\n");
        string checksumPath = Path.Combine(fixture.OutputRoot, "release-evidence-checksums.sha256");
        string markdownPath = Path.Combine(fixture.OutputRoot, "release-evidence.md");
        string releaseNotesPath = Path.Combine(fixture.OutputRoot, "release-evidence-release-notes.md");
        File.WriteAllText(checksumPath, "keep checksums\n");
        File.WriteAllText(markdownPath, "keep markdown\n");
        File.WriteAllText(releaseNotesPath, "keep release notes\n");
        byte[] checksumBefore = File.ReadAllBytes(checksumPath);
        byte[] markdownBefore = File.ReadAllBytes(markdownPath);
        byte[] releaseNotesBefore = File.ReadAllBytes(releaseNotesPath);
        byte[] markerBefore = File.ReadAllBytes(collisionMarker);

        ScriptResult result = fixture.GenerateBundle();

        await Assert.That(File.ReadAllBytes(checksumPath)).IsEquivalentTo(checksumBefore);
        await Assert.That(File.ReadAllBytes(markdownPath)).IsEquivalentTo(markdownBefore);
        await Assert.That(File.ReadAllBytes(releaseNotesPath)).IsEquivalentTo(releaseNotesBefore);
        await Assert.That(File.ReadAllBytes(collisionMarker)).IsEquivalentTo(markerBefore);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("release_bundle_output_destination_invalid");
        await Assert.That(result.Output).DoesNotContain("Unhandled exception");
    }

    [Test]
    public async Task BundleScriptRejectsNonemptyExistingOutputWithoutMutation()
    {
        using var fixture = BundleFixture.Create();
        Directory.CreateDirectory(fixture.OutputRoot);
        string markerPath = Path.Combine(fixture.OutputRoot, "existing-marker.txt");
        File.WriteAllText(markerPath, "keep existing output\n");
        byte[] markerBefore = File.ReadAllBytes(markerPath);

        ScriptResult result = fixture.GenerateBundle();

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("release_bundle_output_destination_invalid");
        await Assert.That(result.Output).DoesNotContain("Unhandled exception");
        await Assert.That(File.ReadAllBytes(markerPath)).IsEquivalentTo(markerBefore);
        await Assert.That(Directory.EnumerateFiles(fixture.OutputRoot).Select(path => Path.GetFileName(path)!)).IsEquivalentTo(["existing-marker.txt"]);
    }

    [Test]
    public async Task BundleScriptPreservesDestinationCreatedDuringPublicationRace()
    {
        using var fixture = BundleFixture.Create();
        Task<ScriptResult> generation = Task.Run(() => fixture.GenerateBundle());
        string stagingPattern = $"{Path.GetFileName(fixture.OutputRoot)}.tmp-*";
        string? stagingDirectory = null;
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline && stagingDirectory is null)
        {
            stagingDirectory = Directory.EnumerateDirectories(fixture.Root, stagingPattern, SearchOption.TopDirectoryOnly).SingleOrDefault();
            if (stagingDirectory is null) await Task.Delay(1);
        }
        await Assert.That(stagingDirectory).IsNotNull();
        Directory.CreateDirectory(fixture.OutputRoot);
        string markerPath = Path.Combine(fixture.OutputRoot, "race-marker.txt");
        File.WriteAllText(markerPath, "keep racing destination\n");
        byte[] markerBefore = File.ReadAllBytes(markerPath);

        ScriptResult result = await generation;

        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).Contains("release_bundle_output_publish_failed");
        await Assert.That(result.Output).DoesNotContain("Unhandled exception");
        await Assert.That(File.ReadAllBytes(markerPath)).IsEquivalentTo(markerBefore);
        await Assert.That(Directory.EnumerateFiles(fixture.OutputRoot).Select(path => Path.GetFileName(path)!)).IsEquivalentTo(["race-marker.txt"]);
        await Assert.That(Directory.EnumerateDirectories(fixture.Root, stagingPattern, SearchOption.TopDirectoryOnly)).IsEmpty();
    }

    [Test]
    public async Task BundleScriptRejectsMissingDuplicateDisagreeingStaleAndTamperedFinalManifests()
    {
        using var missing = BundleFixture.Create();
        File.Delete(missing.FinalManifestPath);
        ScriptResult missingResult = missing.GenerateBundle();

        using var duplicate = BundleFixture.Create();
        string duplicateDirectory = Path.Combine(duplicate.ArtifactRoot, "duplicate");
        Directory.CreateDirectory(duplicateDirectory);
        File.Copy(duplicate.FinalManifestPath, Path.Combine(duplicateDirectory, "release-evidence.v1.json"));
        ScriptResult duplicateResult = duplicate.GenerateBundle();

        using var disagreeingVersion = BundleFixture.Create();
        disagreeingVersion.ReleaseVersion = "9.9.9";
        ScriptResult disagreeingVersionResult = disagreeingVersion.GenerateBundle();

        using var stale = BundleFixture.Create();
        File.AppendAllText(stale.NotesPath, "stale\n");
        ScriptResult staleResult = stale.GenerateBundle();

        using var tampered = BundleFixture.Create();
        tampered.WriteFinalManifest(tagObjectId: new string('d', tampered.B.Length));
        ScriptResult tamperedResult = tampered.GenerateBundle();

        using var disagreeingCommit = BundleFixture.Create();
        ScriptResult disagreeingCommitResult = disagreeingCommit.GenerateBundle(environmentOverrides: new Dictionary<string, string> { ["GITHUB_SHA"] = new string('e', 40) });

        using var disagreeingRef = BundleFixture.Create();
        ScriptResult disagreeingRefResult = disagreeingRef.GenerateBundle(environmentOverrides: new Dictionary<string, string> { ["GITHUB_REF"] = "refs/tags/v9.9.9" });

        using var malformed = BundleFixture.Create();
        File.WriteAllText(malformed.FinalManifestPath, "{\n");
        ScriptResult malformedResult = malformed.GenerateBundle();

        using var noncanonical = BundleFixture.Create();
        File.WriteAllText(noncanonical.FinalManifestPath, File.ReadAllText(noncanonical.FinalManifestPath).Replace("\n", "\r\n", StringComparison.Ordinal));
        ScriptResult noncanonicalResult = noncanonical.GenerateBundle();

        using var injected = BundleFixture.Create();
        injected.WriteFinalManifest(extraPropertyName: "prompt");
        ScriptResult injectedResult = injected.GenerateBundle();

        await Assert.That(missingResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(missingResult.Output).Contains("release_bundle_final_manifest_missing");
        await Assert.That(duplicateResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(duplicateResult.Output).Contains("release_bundle_final_manifest_duplicate");
        await Assert.That(disagreeingVersionResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(disagreeingVersionResult.Output).Contains("release_bundle_version_mismatch");
        await Assert.That(staleResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(staleResult.Output).Contains("release_bundle_notes_hash_mismatch");
        await Assert.That(tamperedResult.ExitCode).IsNotEqualTo(0);
        await Assert.That(tamperedResult.Output).Contains("release_bundle_tag_object_mismatch");
        await Assert.That(disagreeingCommitResult.Output).Contains("release_bundle_commit_mismatch");
        await Assert.That(disagreeingRefResult.Output).Contains("release_bundle_ref_mismatch");
        await Assert.That(malformedResult.Output).Contains("release_bundle_final_manifest_malformed");
        await Assert.That(malformedResult.Output).DoesNotContain("Unhandled exception");
        await Assert.That(noncanonicalResult.Output).Contains("release_bundle_final_manifest_canonical_invalid");
        await Assert.That(injectedResult.Output).Contains("release_bundle_final_manifest_schema_invalid");
        await Assert.That(new[] { missing, duplicate, disagreeingVersion, stale, tampered, disagreeingCommit, disagreeingRef, malformed, noncanonical, injected }.Any(fixture => Directory.Exists(fixture.OutputRoot))).IsFalse();
    }

    [Test]
    public async Task BundleScriptRejectsPathAliasesUnicodeAndOversizedArtifactsWithoutPartialOutput()
    {
        using var caseAlias = BundleFixture.Create();
        string alias = Path.Combine(caseAlias.ArtifactRoot, "Docs", "internal", "releases", "1.1.0");
        Directory.CreateDirectory(alias);
        File.Copy(caseAlias.FinalManifestPath, Path.Combine(alias, "release-evidence.v1.json"));
        ScriptResult caseResult = caseAlias.GenerateBundle();

        using var unicode = BundleFixture.Create();
        File.WriteAllText(Path.Combine(unicode.ArtifactRoot, "e\u0301vidence.txt"), "alias\n");
        ScriptResult unicodeResult = unicode.GenerateBundle();

        using var symlink = BundleFixture.Create();
        string external = Path.Combine(symlink.Root, "external.txt");
        File.WriteAllText(external, "outside\n");
        File.CreateSymbolicLink(Path.Combine(symlink.ArtifactRoot, "linked.txt"), external);
        ScriptResult symlinkResult = symlink.GenerateBundle();

        using var symlinkDirectory = BundleFixture.Create();
        string externalDirectory = Path.Combine(symlinkDirectory.Root, "external-directory");
        Directory.CreateDirectory(externalDirectory);
        File.WriteAllText(Path.Combine(externalDirectory, "blocked.txt"), "blocked\n");
        Directory.CreateSymbolicLink(Path.Combine(symlinkDirectory.ArtifactRoot, "linked-directory"), externalDirectory);
        ScriptResult symlinkDirectoryResult = symlinkDirectory.GenerateBundle();

        using var oversized = BundleFixture.Create();
        using (FileStream stream = File.Create(Path.Combine(oversized.ArtifactRoot, "oversized.bin"))) stream.SetLength(1_073_741_825);
        ScriptResult oversizedResult = oversized.GenerateBundle();

        await Assert.That(caseResult.Output).Contains("release_bundle_artifact_path_alias");
        await Assert.That(unicodeResult.Output).Contains("release_bundle_artifact_path_invalid");
        await Assert.That(symlinkResult.Output).Contains("release_bundle_artifact_path_alias");
        await Assert.That(symlinkDirectoryResult.Output).Contains("release_bundle_artifact_path_alias");
        await Assert.That(oversizedResult.Output).Contains("release_bundle_artifact_size_invalid");
        await Assert.That(new[] { caseAlias, unicode, symlink, symlinkDirectory, oversized }.Any(fixture => Directory.Exists(fixture.OutputRoot))).IsFalse();
    }

    [Test]
    public async Task BundleScriptRejectsHardlinkedArtifactsWithoutPartialOutput()
    {
        using var hardlink = BundleFixture.Create();
        CreateHardLink(hardlink.ArtifactPath("trusted-bundle/git-cliff-hardlink"), hardlink.ArtifactPath("trusted-bundle/git-cliff"));

        ScriptResult result = hardlink.GenerateBundle();

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("release_bundle_artifact_path_alias");
        await Assert.That(Directory.Exists(hardlink.OutputRoot)).IsFalse();
    }

    [Test]
    public async Task ChecksumWriterRejectsHardlinkedArtifactsWithoutPartialOutput()
    {
        using var hardlink = BundleFixture.Create();
        CreateHardLink(hardlink.ArtifactPath("trusted-bundle/git-cliff-hardlink"), hardlink.ArtifactPath("trusted-bundle/git-cliff"));
        string rejectedPath = Path.Combine(hardlink.Root, "hardlink-checksums.sha256");

        ScriptResult result = RunDotnetScript(".ci/scripts/write-artifact-checksums.cs", new Dictionary<string, string>(), hardlink.ArtifactRoot, rejectedPath);

        await Assert.That(result.ExitCode).IsNotEqualTo(0);
        await Assert.That(result.Output).Contains("artifact_checksums_path_alias");
        await Assert.That(File.Exists(rejectedPath)).IsFalse();
    }

    [Test]
    public async Task BundleAndChecksumScriptsInspectWindowsHardlinkCounts()
    {
        string repoRoot = FindRepositoryRoot();
        string bundleScript = File.ReadAllText(Path.Combine(repoRoot, ".ci", "scripts", "generate-release-evidence-bundle.cs"));
        string checksumScript = File.ReadAllText(Path.Combine(repoRoot, ".ci", "scripts", "write-artifact-checksums.cs"));

        foreach (string script in new[] { bundleScript, checksumScript })
        {
            await Assert.That(script).Contains("OperatingSystem.IsWindows()");
            await Assert.That(script).Contains("CreateFileW");
            await Assert.That(script).Contains("GetFileInformationByHandle");
            await Assert.That(script).Contains("NumberOfLinks != 1");
            await Assert.That(script).DoesNotContain("!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return false");
        }
    }

    [Test]
    public async Task ChecksumWriterProducesSortedCompleteManifestAndRejectsCaseAliases()
    {
        using var valid = BundleFixture.Create();
        string checksumPath = Path.Combine(valid.Root, "standalone-checksums.sha256");
        ScriptResult validResult = RunDotnetScript(".ci/scripts/write-artifact-checksums.cs", new Dictionary<string, string>(), valid.ArtifactRoot, checksumPath);
        string[] lines = File.ReadAllLines(checksumPath);
        string[] paths = lines.Select(line => line[(line.IndexOf("  ", StringComparison.Ordinal) + 2)..]).ToArray();

        using var alias = BundleFixture.Create();
        File.WriteAllText(Path.Combine(alias.ArtifactRoot, "ALIAS.txt"), "one\n");
        File.WriteAllText(Path.Combine(alias.ArtifactRoot, "alias.txt"), "two\n");
        string rejectedPath = Path.Combine(alias.Root, "rejected-checksums.sha256");
        ScriptResult aliasResult = RunDotnetScript(".ci/scripts/write-artifact-checksums.cs", new Dictionary<string, string>(), alias.ArtifactRoot, rejectedPath);

        await Assert.That(validResult.ExitCode).IsEqualTo(0);
        await Assert.That(lines.Length).IsEqualTo(Directory.EnumerateFiles(valid.ArtifactRoot, "*", SearchOption.AllDirectories).Count());
        await Assert.That(paths.SequenceEqual(paths.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        await Assert.That(lines.All(line => line.Length > 66 && line[64..66] == "  ")).IsTrue();
        await Assert.That(aliasResult.Output).Contains("artifact_checksums_path_alias");
        await Assert.That(File.Exists(rejectedPath)).IsFalse();
    }

    private sealed class BundleFixture : IDisposable
    {
        private BundleFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-bundle-{Guid.NewGuid():N}");
            ArtifactRoot = Path.Combine(Root, "artifacts");
            OutputRoot = Path.Combine(Root, "bundle");
            ReleaseDirectory = Path.Combine(ArtifactRoot, "docs", "internal", "releases", "1.1.0");
            FinalManifestPath = Path.Combine(ReleaseDirectory, "release-evidence.v1.json");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            BundleJsonPath = Path.Combine(OutputRoot, "release-evidence.json");
            ChecksumPath = Path.Combine(OutputRoot, "release-evidence-checksums.sha256");
            B = new string('a', 40);
            TagObjectId = new string('c', 40);
            Directory.CreateDirectory(ReleaseDirectory);
            WriteAllArtifacts();
        }

        public string Root { get; }
        public string ArtifactRoot { get; }
        public string OutputRoot { get; }
        public string ReleaseDirectory { get; }
        public string FinalManifestPath { get; }
        public string NotesPath { get; }
        public string BundleJsonPath { get; }
        public string ChecksumPath { get; }
        public string B { get; }
        public string CandidateDigest => ArtifactDigest("docs/internal/releases/1.1.0/release-candidate.v1.json");
        public string TagObjectId { get; }
        public string DescriptorDigest => Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "release.yaml")));
        public string SummaryDigest => Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "summary.md")));
        public string ContextDigest => Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "release-context.v1.json")));
        public string NotesDigest => Sha256(File.ReadAllBytes(NotesPath));
        public string ArtifactDigest(string relativePath) => Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))));
        public string ArtifactPath(string relativePath) => Path.Combine(ArtifactRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        public string ReleaseVersion { get; set; } = "1.1.0";

        public static BundleFixture Create() => new();

        public ScriptResult GenerateBundle(string? outputRoot = null, IReadOnlyDictionary<string, string>? environmentOverrides = null)
        {
            Dictionary<string, string> environment = new(StringComparer.Ordinal)
            {
                ["RELEASE_VERSION"] = ReleaseVersion,
                ["GITHUB_SHA"] = B,
                ["GITHUB_REF"] = "refs/tags/v1.1.0",
                ["GITHUB_REPOSITORY"] = "islamu-ngo/Event",
                ["GITHUB_RUN_ID"] = Guid.NewGuid().ToString("N"),
                ["GITHUB_RUN_ATTEMPT"] = "1",
                ["CLA_STATUS"] = "passed",
                ["RELEASE_TAG_OBJECT_ID"] = TagObjectId,
            };
            if (environmentOverrides is not null)
            {
                foreach ((string key, string value) in environmentOverrides) environment[key] = value;
            }
            return RunDotnetScript(".ci/scripts/generate-release-evidence-bundle.cs", environment, ArtifactRoot, outputRoot ?? OutputRoot);
        }

        public void WriteFinalManifest(string? version = null, string? tagObjectId = null, string? targetOid = null, string? extraPropertyName = null)
        {
            string summarySha = Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "summary.md")));
            string contextSha = Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "release-context.v1.json")));
            string notesSha = Sha256(File.ReadAllBytes(NotesPath));
            string candidateSha = Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "release-candidate.v1.json")));
            string descriptorSha = Sha256(File.ReadAllBytes(Path.Combine(ReleaseDirectory, "release.yaml")));
            var values = new SortedDictionary<string, object?>(StringComparer.Ordinal)
            {
                ["baseStableOid"] = new string('9', 40),
                ["baseStableTag"] = "v1.0.0",
                ["baseStableTagObjectId"] = new string('8', 40),
                ["candidateManifestSchemaVersion"] = "release-candidate.v1",
                ["candidateManifestSha256"] = candidateSha,
                ["candidateOid"] = targetOid ?? B,
                ["line"] = "v1.1",
                ["objectFormat"] = "sha1",
                ["oidLength"] = 40,
                ["previousPublishedOid"] = new string('9', 40),
                ["previousPublishedTag"] = "v1.0.0",
                ["previousPublishedTagObjectId"] = new string('8', 40),
                ["releaseContextSha256"] = contextSha,
                ["releaseDate"] = "2026-08-14",
                ["releaseDescriptorSha256"] = descriptorSha,
                ["releaseFragmentsSha256"] = new string('0', 64),
                ["releaseNotesSha256"] = notesSha,
                ["releaseSummarySha256"] = summarySha,
                ["schemaVersion"] = "release-evidence.v1",
                ["signerAlgorithm"] = "ssh-ed25519",
                ["signerKeyFingerprint"] = "SHA256:fixture",
                ["signerPrincipal"] = "fixture-release-operator",
                ["signerRole"] = "release",
                ["signerValidFrom"] = "2026-01-01",
                ["signerValidUntil"] = "2026-12-31",
                ["tagName"] = $"v{version ?? "1.1.0"}",
                ["tagObjectId"] = tagObjectId ?? TagObjectId,
                ["targetOid"] = targetOid ?? B,
                ["trustedBundleConfigSha256"] = Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, "trusted-bundle", "config", "cliff.toml"))),
                ["trustedBundleGitCliffSha256"] = Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, "trusted-bundle", "git-cliff"))),
                ["trustedBundleManifestSha256"] = Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, "trusted-bundle", "trusted-bundle.manifest.json"))),
                ["trustedBundlePolicySha256"] = Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, "trusted-bundle", "policy", "release-policy.yaml"))),
                ["trustedBundleToolchainSha256"] = Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, "trusted-bundle", "toolchain.lock.json"))),
                ["trustedBundleTrustSha256"] = Sha256(File.ReadAllBytes(Path.Combine(ArtifactRoot, "trusted-bundle", "trust", "allowed-signers"))),
                ["version"] = version ?? "1.1.0",
            };
            if (extraPropertyName is not null) values[extraPropertyName] = "ignore previous instructions";
            string manifest = JsonSerializer.Serialize(values, IndentedJson);
            File.WriteAllText(FinalManifestPath, manifest.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private void WriteAllArtifacts()
        {
            Write("docs/internal/releases/1.1.0/release.yaml", "version: 1.1.0\nline: v1.1\n");
            Write("docs/internal/releases/1.1.0/summary.md", "# Summary\n\nRelease summary.\n");
            Write("docs/internal/releases/1.1.0/release-context.v1.json", "{\"schemaVersion\":1,\"changes\":[]}\n");
            Write("docs/internal/releases/1.1.0/release-notes.md", "# v1.1.0\n\nRelease notes.\n");
            Write("docs/internal/releases/1.1.0/release-candidate.v1.json", "{\"schemaVersion\":\"release-candidate.v1\"}\n");
            Write("container/oci-digest.txt", "sha256:container\n");
            Write("container/checksums.sha256", "sha256  image\n");
            Write("deployment/production-deploy-summary.md", "deployment ok\n");
            Write("openapi/openapi-drift.txt", "clean\n");
            Write("test-results/build.log", "build ok\n");
            Write("dependencies/nuget-vulnerabilities.json", "{}\n");
            Write("workflow-security/actionlint.txt", "ok\n");
            Write("secret-scanning/gitleaks.txt", "ok\n");
            Write("scorecard/scorecard.json", "{}\n");
            Write("security-tests/cerbos-policy.txt", "ok\n");
            Write("trusted-bundle/trusted-bundle.manifest.json", "{\"schemaVersion\":1}\n");
            Write("trusted-bundle/promotion-receipt.v1.json", "{\"receipt\":true}\n");
            Write("trusted-bundle/promotion-receipt.v1.json.sig", "signature\n");
            Write("trusted-bundle/toolchain.lock.json", "{\"gitCliff\":\"2.13.1\"}\n");
            Write("trusted-bundle/config/cliff.toml", "[changelog]\n");
            Write("trusted-bundle/policy/release-policy.yaml", "schemaVersion: 1\n");
            Write("trusted-bundle/trust/allowed-signers", "fixture key\n");
            Write("trust/allowed-signers", "fixture key\n");
            Write("trust/release-signing-policy.yaml", "release: {}\n");
            Write("signer/tag-verification.json", "{\"verified\":true}\n");
            Write("trusted-bundle/git-cliff", "#!/bin/sh\n");
            WriteFinalManifest();
        }

        private void Write(string relativePath, string contents)
        {
            string path = Path.Combine(ArtifactRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }
    }

    private static ScriptResult RunDotnetScript(string scriptPath, IReadOnlyDictionary<string, string> environment, params string[] arguments)
    {
        string repoRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("--");
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        foreach ((string key, string value) in environment) startInfo.Environment[key] = value;
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("dotnet failed to start");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(output);
        }
        return new ScriptResult(process.ExitCode, output);
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !Directory.Exists(Path.Combine(current.FullName, ".ci"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("repository root not found");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static void RunProcess(string executable, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"failed to start {executable}");
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(output);
    }

    private static void CreateHardLink(string linkPath, string existingPath)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLinkW(linkPath, existingPath, IntPtr.Zero)) throw new InvalidOperationException($"CreateHardLinkW failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        RunProcess("/usr/bin/ln", existingPath, linkPath);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, IntPtr securityAttributes);

    private sealed record ScriptResult(int ExitCode, string Output);
}
