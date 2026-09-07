// ABOUTME: Proves verify-tag accepts only trusted SSH-signed annotated release tags.
// ABOUTME: Exercises deterministic final evidence, canonical tag messages, and local drift failures.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleaseTagVerificationTests
{
    [Test]
    public async Task VerifyTagAcceptsSignedAnnotatedTagAndWritesDeterministicFinalEvidenceTwice()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = TagFixture.Create();
        fixture.VerifyCandidate();
        string message = fixture.GenerateTagMessage();
        string tagObject = fixture.CreateSignedTag(message);

        (int firstCode, string firstOutput) = fixture.VerifyTag(tagObject);
        await Assert.That(firstCode).IsEqualTo(Program.Success);
        byte[] firstBytes = File.ReadAllBytes(fixture.FinalManifestPath);
        string firstDigest = Sha256(firstBytes);
        (int secondCode, string secondOutput) = fixture.VerifyTag(tagObject);
        byte[] secondBytes = File.ReadAllBytes(fixture.FinalManifestPath);

        using JsonDocument document = JsonDocument.Parse(firstBytes);
        JsonElement root = document.RootElement;
        await Assert.That(secondCode).IsEqualTo(Program.Success);
        await Assert.That(firstOutput).IsEqualTo("release_tag_verified: docs/internal/releases/1.1.0/release-evidence.v1.json\n");
        await Assert.That(secondOutput).IsEqualTo(firstOutput);
        await Assert.That(secondBytes).IsEquivalentTo(firstBytes);
        await Assert.That(firstDigest).IsEqualTo(Sha256(secondBytes));
        await Assert.That(message).Contains($"Candidate-SHA256: {Sha256(File.ReadAllBytes(fixture.CandidateManifestPath))}");
        await Assert.That(root.GetProperty("schemaVersion").GetString()).IsEqualTo("release-evidence.v1");
        await Assert.That(root.GetProperty("version").GetString()).IsEqualTo("1.1.0");
        await Assert.That(root.GetProperty("line").GetString()).IsEqualTo("v1.1");
        await Assert.That(root.GetProperty("tagName").GetString()).IsEqualTo("v1.1.0");
        await Assert.That(root.GetProperty("tagObjectId").GetString()).IsEqualTo(tagObject);
        await Assert.That(root.GetProperty("targetOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(root.GetProperty("candidateManifestSha256").GetString()).IsEqualTo(Sha256(File.ReadAllBytes(fixture.CandidateManifestPath)));
        await Assert.That(root.GetProperty("releaseContextSha256").GetString()).IsEqualTo(Sha256(File.ReadAllBytes(fixture.ContextPath)));
        await Assert.That(root.GetProperty("releaseNotesSha256").GetString()).IsEqualTo(Sha256(File.ReadAllBytes(fixture.NotesPath)));
        await Assert.That(root.GetProperty("signerPrincipal").GetString()).IsEqualTo("fixture-release-operator");
        await Assert.That(root.GetProperty("signerRole").GetString()).IsEqualTo("release");
        await Assert.That(root.GetProperty("signerAlgorithm").GetString()).IsEqualTo("ssh-ed25519");
        await Assert.That(root.ToString()).DoesNotContain("provider");
        await Assert.That(root.ToString()).DoesNotContain("@example");
        await Assert.That(root.ToString()).DoesNotContain("raw");
    }

    [Test]
    public async Task VerifyTagRejectsMovedRecreatedLightweightUnsignedAndWrongTargetTags()
    {
        if (OperatingSystem.IsWindows()) return;

        using var recreated = TagFixture.CreateVerifiedWithSignedTag();
        (int okCode, _) = recreated.VerifyTag(recreated.TagObject);
        recreated.DeleteTag();
        Thread.Sleep(TimeSpan.FromMilliseconds(1100));
        string recreatedObject = recreated.CreateSignedTag(recreated.GenerateTagMessage());
        (int recreatedCode, string recreatedOutput) = recreated.VerifyTag(recreatedObject);

        using var lightweight = TagFixture.CreateVerified();
        string lightweightObject = lightweight.CreateLightweightTag();
        (int lightweightCode, string lightweightOutput) = lightweight.VerifyTag(lightweightObject);

        using var unsigned = TagFixture.CreateVerified();
        string unsignedObject = unsigned.CreateUnsignedAnnotatedTag(unsigned.GenerateTagMessage());
        (int unsignedCode, string unsignedOutput) = unsigned.VerifyTag(unsignedObject);

        using var wrongTarget = TagFixture.CreateVerified();
        string wrongTargetObject = wrongTarget.CreateSignedTag(wrongTarget.GenerateTagMessage(), wrongTarget.A);
        (int wrongTargetCode, string wrongTargetOutput) = wrongTarget.VerifyTag(wrongTargetObject);

        await Assert.That(okCode).IsEqualTo(Program.Success);
        await Assert.That(recreatedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(recreatedOutput).IsEqualTo("verify_tag_failed: release_tag_object_recreated\n");
        await Assert.That(lightweightCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(lightweightOutput).IsEqualTo("verify_tag_failed: release_tag_not_annotated\n");
        await Assert.That(unsignedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unsignedOutput).IsEqualTo("verify_tag_failed: release_tag_signature_invalid\n");
        await Assert.That(wrongTargetCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongTargetOutput).IsEqualTo("verify_tag_failed: release_tag_wrong_target\n");
    }

    [Test]
    public async Task VerifyTagRejectsTrustPolicyAndArtifactDriftFailures()
    {
        if (OperatingSystem.IsWindows()) return;

        using var unauthorized = TagFixture.CreateVerifiedWithSignedTag();
        unauthorized.ReplaceAllowedSignerWithDifferentKey();
        unauthorized.RefreshCandidate();
        unauthorized.ResignReleaseTag();
        (int unauthorizedCode, string unauthorizedOutput) = unauthorized.VerifyTag(unauthorized.TagObject);

        using var wrongRole = TagFixture.CreateVerifiedWithSignedTag();
        wrongRole.WriteReleasePrincipal("fixture-tooling-promoter");
        wrongRole.RefreshCandidate();
        wrongRole.ResignReleaseTag();
        (int wrongRoleCode, string wrongRoleOutput) = wrongRole.VerifyTag(wrongRole.TagObject);

        using var wrongAlgorithm = TagFixture.CreateVerifiedWithSignedTag();
        wrongAlgorithm.WriteAllowedSignerAlgorithm("ssh-rsa");
        wrongAlgorithm.RefreshCandidate();
        wrongAlgorithm.ResignReleaseTag();
        (int wrongAlgorithmCode, string wrongAlgorithmOutput) = wrongAlgorithm.VerifyTag(wrongAlgorithm.TagObject);

        using var expired = TagFixture.CreateVerifiedWithSignedTag();
        expired.WriteAllowedSignerValidity("2026-01-01", "2026-01-31");
        expired.RefreshCandidate();
        expired.ResignReleaseTag();
        (int expiredCode, string expiredOutput) = expired.VerifyTag(expired.TagObject);

        using var revoked = TagFixture.CreateVerifiedWithSignedTag();
        revoked.WriteRevocation("2026-08-14");
        revoked.RefreshCandidate();
        revoked.ResignReleaseTag();
        (int revokedCode, string revokedOutput) = revoked.VerifyTag(revoked.TagObject);

        using var candidateDrift = TagFixture.CreateVerifiedWithSignedTag();
        File.WriteAllText(candidateDrift.CandidateManifestPath, "{}\n");
        (int candidateCode, string candidateOutput) = candidateDrift.VerifyTag(candidateDrift.TagObject);

        using var noteDrift = TagFixture.CreateVerifiedWithSignedTag();
        File.AppendAllText(noteDrift.NotesPath, "drift\n");
        (int noteCode, string noteOutput) = noteDrift.VerifyTag(noteDrift.TagObject);

        await Assert.That(unauthorizedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unauthorizedOutput).IsEqualTo("verify_tag_failed: release_tag_signature_invalid\n");
        await Assert.That(wrongRoleCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongRoleOutput).IsEqualTo("verify_tag_failed: release_signer_unauthorized\n");
        await Assert.That(wrongAlgorithmCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongAlgorithmOutput).IsEqualTo("verify_tag_failed: release_signer_algorithm_forbidden\n");
        await Assert.That(expiredCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(expiredOutput).IsEqualTo("verify_tag_failed: release_signer_not_current\n");
        await Assert.That(revokedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(revokedOutput).IsEqualTo("verify_tag_failed: release_signer_revoked\n");
        await Assert.That(candidateCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(candidateOutput).IsEqualTo("verify_tag_failed: release_candidate_manifest_invalid\n");
        await Assert.That(noteCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(noteOutput).IsEqualTo("verify_tag_failed: release_notes_hash_mismatch\n");
    }

    [Test]
    public async Task VerifyTagRejectsWrongTagLineMessageCandidateAndPriorTagDrift()
    {
        if (OperatingSystem.IsWindows()) return;

        using var wrongLine = TagFixture.CreateVerified();
        string wrongLineObject = wrongLine.CreateSignedTag(wrongLine.GenerateTagMessage(), tagName: "v1.2.0");
        (int wrongLineCode, string wrongLineOutput) = wrongLine.VerifyTag(wrongLineObject);

        using var wrongMessage = TagFixture.CreateVerified();
        string wrongMessageObject = wrongMessage.CreateSignedTag(wrongMessage.GenerateTagMessage() + "manual edit\n");
        (int wrongMessageCode, string wrongMessageOutput) = wrongMessage.VerifyTag(wrongMessageObject);

        using var staleOutput = TagFixture.CreateVerifiedWithSignedTag();
        File.WriteAllText(staleOutput.FinalManifestPath, "{}\n");
        (int staleCode, string staleText) = staleOutput.VerifyTag(staleOutput.TagObject);

        using var priorDrift = TagFixture.CreateVerified();
        string previousTag = priorDrift.ResolveTagObject("v1.0.0");
        priorDrift.DeleteTag("v1.0.0");
        Thread.Sleep(TimeSpan.FromMilliseconds(1100));
        priorDrift.CreateUnsignedAnnotatedTag("v1.0.0\n", tagName: "v1.0.0", target: priorDrift.Initial);
        string priorObject = priorDrift.CreateSignedTag(priorDrift.GenerateTagMessage());
        (int priorCode, string priorOutput) = priorDrift.VerifyTag(priorObject, previousTagObjectId: previousTag);

        await Assert.That(wrongLineCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongLineOutput).IsEqualTo("verify_tag_failed: release_tag_name_mismatch\n");
        await Assert.That(wrongMessageCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongMessageOutput).IsEqualTo("verify_tag_failed: release_tag_message_mismatch\n");
        await Assert.That(staleCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(staleText).IsEqualTo("verify_tag_failed: release_evidence_manifest_stale\n");
        await Assert.That(priorCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(priorOutput).IsEqualTo("verify_tag_failed: git_tag_object_mismatch:v1.0.0\n");
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class TagFixture : IDisposable
    {
        private const string ReleasePolicyYaml = "schemaVersion: 1\nmaximumCommitMessageBytes: 8192\nreleaseVisibleTypes:\n  - feat\n  - fix\n  - perf\n  - revert\n  - docs\ninternalTypes:\n  - test\n  - refactor\n  - style\n  - build\n  - ci\n  - chore\nrequiredBreakingSignals:\n  bang: true\n  footer: BREAKING CHANGE\nskipTrailer: Changelog\nskipValue: skip\nskipReasonTrailer: Changelog-Reason\n";
        private readonly string bundleRoot;
        private readonly string authorityRoot;
        private readonly string promotionPrivateKeyPath;
        private readonly string releasePrivateKeyPath;
        private readonly string receiptPath;
        private readonly string signaturePath;
        private readonly string allowedPromotersPath;
        private readonly string allowedReleaseSignersPath;
        private readonly string configPath;
        private readonly string executablePath;

        private TagFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-tag-{Guid.NewGuid():N}");
            RepositoryPath = Path.Combine(Root, "repo");
            bundleRoot = Path.Combine(Root, "bundle");
            authorityRoot = Path.Combine(Root, "authority");
            promotionPrivateKeyPath = Path.Combine(authorityRoot, "promotion-key");
            releasePrivateKeyPath = Path.Combine(authorityRoot, "release-key");
            receiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
            signaturePath = receiptPath + ".sig";
            allowedPromotersPath = Path.Combine(authorityRoot, "allowed-promoters");
            allowedReleaseSignersPath = Path.Combine(bundleRoot, "trust", "allowed-signers");
            configPath = Path.Combine(bundleRoot, "config", "cliff.toml");
            executablePath = Path.Combine(bundleRoot, "git-cliff");
            ReleaseDirectory = Path.Combine(RepositoryPath, "docs", "internal", "releases", "1.1.0");
            ContextPath = Path.Combine(ReleaseDirectory, "release-context.v1.json");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            CandidateManifestPath = Path.Combine(ReleaseDirectory, "release-candidate.v1.json");
            FinalManifestPath = Path.Combine(ReleaseDirectory, "release-evidence.v1.json");
            Directory.CreateDirectory(RepositoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(authorityRoot);
            Git("init", "--object-format=sha1", "--initial-branch=main");
            CreatePromotionAuthority();
            CreateReleaseAuthority();
            Initial = Commit("fix(events): preserve published event notes");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v1.0.0", Initial, "-m", "v1.0.0");
            A = Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
            Git("branch", "-f", "v1.1", A);
            WriteBundle();
            Prepare();
            Git("add", "docs/internal/releases/1.1.0");
            B = Commit("docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit");
            Git("branch", "-f", "v1.1", B);
        }

        public string Root { get; }
        public string RepositoryPath { get; }
        public string ReleaseDirectory { get; }
        public string ContextPath { get; }
        public string NotesPath { get; }
        public string CandidateManifestPath { get; }
        public string FinalManifestPath { get; }
        public string Initial { get; }
        public string A { get; }
        public string B { get; }
        public string TagObject { get; private set; } = string.Empty;

        public static TagFixture Create() => new();
        public static TagFixture CreateVerified()
        {
            TagFixture fixture = new();
            fixture.VerifyCandidate();
            return fixture;
        }

        public static TagFixture CreateVerifiedWithSignedTag()
        {
            TagFixture fixture = CreateVerified();
            fixture.TagObject = fixture.CreateSignedTag(fixture.GenerateTagMessage());
            return fixture;
        }

        public void VerifyCandidate()
        {
            (int exitCode, string output) = RunWithEnvironment(writer => CandidateCommand.Run(["verify-candidate", "docs/internal/releases/1.1.0", B], writer, RepositoryPath, "linux-x64", TimeSpan.FromSeconds(2)));
            if (exitCode != Program.Success) throw new InvalidOperationException(output);
        }

        public void RefreshCandidate()
        {
            if (File.Exists(CandidateManifestPath)) File.Delete(CandidateManifestPath);
            VerifyCandidate();
        }

        public void ResignReleaseTag()
        {
            DeleteTag();
            Thread.Sleep(TimeSpan.FromMilliseconds(1100));
            TagObject = CreateSignedTag(GenerateTagMessage());
        }

        public string GenerateTagMessage()
        {
            (int exitCode, string output) = RunWithEnvironment(writer => TagCommand.Run(["tag-message", "docs/internal/releases/1.1.0"], writer, RepositoryPath, "linux-x64", TimeSpan.FromSeconds(2)));
            if (exitCode != Program.Success) throw new InvalidOperationException(output);
            return output;
        }

        public (int ExitCode, string Output) VerifyTag(string tagObjectId, string? previousTagObjectId = null)
        {
            List<string> args = ["verify-tag", "docs/internal/releases/1.1.0", B, tagObjectId];
            if (previousTagObjectId is not null) args.Add(previousTagObjectId);
            return RunWithEnvironment(writer => TagCommand.Run(args.ToArray(), writer, RepositoryPath, "linux-x64", TimeSpan.FromSeconds(2)));
        }

        public string CreateSignedTag(string message, string? target = null, string tagName = "v1.1.0")
        {
            string messagePath = Path.Combine(Root, $"{tagName}.message");
            File.WriteAllText(messagePath, message);
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "-c", "gpg.format=ssh", "-c", $"user.signingKey={releasePrivateKeyPath}", "tag", "-s", tagName, target ?? B, "-F", messagePath);
            return ResolveTagObject(tagName);
        }

        public string CreateUnsignedAnnotatedTag(string message, string tagName = "v1.1.0", string? target = null)
        {
            string messagePath = Path.Combine(Root, $"{tagName}.message");
            File.WriteAllText(messagePath, message);
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", tagName, target ?? B, "-F", messagePath);
            return ResolveTagObject(tagName);
        }

        public string CreateLightweightTag()
        {
            Git("tag", "v1.1.0", B);
            return Git("rev-parse", "refs/tags/v1.1.0").Trim();
        }

        public string ResolveTagObject(string tagName) => Git("rev-parse", $"refs/tags/{tagName}^{{object}}").Trim();
        public void DeleteTag(string tagName = "v1.1.0") => Git("tag", "-d", tagName);
        public void WriteReleasePrincipal(string principal) => RewriteSigningPolicy(principal, revokedOn: null);
        public void WriteRevocation(string revokedOn) => RewriteSigningPolicy("fixture-release-operator", revokedOn);

        public void WriteAllowedSignerAlgorithm(string algorithm)
        {
            RewriteSigningPolicy("fixture-release-operator", revokedOn: null, algorithm: algorithm);
        }

        public void WriteAllowedSignerValidity(string validAfter, string validBefore)
        {
            RewriteSigningPolicy("fixture-release-operator", revokedOn: null, validFrom: validAfter, validUntil: validBefore);
        }

        public void ReplaceAllowedSignerWithDifferentKey()
        {
            string otherKey = Path.Combine(authorityRoot, "other-release-key");
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-other-release-fixture", "-f", otherKey);
            string[] parts = File.ReadAllText(otherKey + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            File.WriteAllText(allowedReleaseSignersPath, $"fixture-release-operator namespaces=\"git\",valid-after=\"20260101\",valid-before=\"20261231\" {parts[0]} {parts[1]}\n");
            RewriteManifestAndReceipt();
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private (int ExitCode, string Output) RunWithEnvironment(Func<TextWriter, int> action)
        {
            var variables = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot,
                ["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath,
                ["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath,
                ["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter",
                ["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")),
                ["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering",
                ["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0",
                ["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1",
                ["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1",
                ["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1",
            };
            Dictionary<string, string?> originals = variables.Keys.ToDictionary(name => name, Environment.GetEnvironmentVariable, StringComparer.Ordinal);
            try
            {
                foreach ((string name, string value) in variables) Environment.SetEnvironmentVariable(name, value);
                using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedPromotersPath);
                using var output = new StringWriter();
                int exitCode = action(output);
                return (exitCode, output.ToString());
            }
            finally
            {
                foreach ((string name, string? value) in originals) Environment.SetEnvironmentVariable(name, value);
            }
        }

        private void Prepare()
        {
            Directory.CreateDirectory(ReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes"));
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "eng", "release", "policy"));
            File.WriteAllText(Path.Combine(ReleaseDirectory, "release.yaml"),
                $"Version: 1.1.0\nLine: v1.1\nRelease-Date: 2026-08-14\nBase-Stable-Tag: v1.0.0\nPrevious-Published-Tag: v1.0.0\nRelease-Range:\n  Base-Ref: v1.0.0\n  Base-Oid: {Initial}\n  Previous-Ref: v1.0.0\n  Previous-Oid: {Initial}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(Path.Combine(ReleaseDirectory, "summary.md"), "Attendees can now correct registration details.\n");
            File.WriteAllText(Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"),
                "Change-Id: CHG-2026-0001\nTitle: Registration worker restart\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n");
            File.WriteAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "release-policy.yaml"), ReleasePolicyYaml);
            File.WriteAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "scope-registry.yaml"), "schemaVersion: 1\npublicScopes:\n  - events\n  - registration\nengineeringScopes:\n  - release\n");
            RewriteManifestAndReceipt();
            (int exitCode, string output) = RunWithEnvironment(writer => PrepareCommand.Run(["prepare", "docs/internal/releases/1.1.0"], writer, RepositoryPath, "linux-x64", TimeSpan.FromSeconds(2)));
            if (exitCode != Program.Success) throw new InvalidOperationException(output);
        }

        private string Commit(string message)
        {
            File.AppendAllText(Path.Combine(RepositoryPath, "file.txt"), message + Environment.NewLine);
            Git("add", ".");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        private void WriteBundle()
        {
            File.WriteAllText(configPath, "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n");
            File.WriteAllText(executablePath, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.13.1\\n'; exit 0; fi\nprintf '# Release 1.1.0\\n\\n- registration: let attendees correct registration details (cccccccccccc)\\n'\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            File.WriteAllText(Path.Combine(bundleRoot, "toolchain.lock.json"), $$"""
                {
                  "schemaVersion": 1,
                  "tool": "git-cliff",
                  "version": "2.13.1",
                  "platforms": [{ "platform": "linux-x64", "executable": "git-cliff", "executableSha256": "{{Digest(executablePath)}}" }]
                }
                """);
            RewriteSigningPolicy("fixture-release-operator", revokedOn: null);
            RewriteManifestAndReceipt();
        }

        private void RewriteSigningPolicy(string releasePrincipal, string? revokedOn, string algorithm = "ssh-ed25519", string validFrom = "2026-01-01", string validUntil = "2026-12-31")
        {
            EnsureFile("trust/release-signing-policy.yaml", $"schemaVersion: release-signing-policy.v1\nstatus: fixture-only\nallowedAlgorithms:\n  - ssh-ed25519\nroles:\n  release:\n    tagPattern: v<major>.<minor>.<patch>[-prerelease]\n    tagKind: annotated\n    namespace: git\n    principal: {releasePrincipal}\n    algorithm: {algorithm}\n    validFrom: {validFrom}\n    validUntil: {validUntil}\n{(revokedOn is null ? string.Empty : $"    revokedOn: {revokedOn}\n")}  tooling-promotion:\n    principal: fixture-tooling-promoter\n");
            RewriteManifestAndReceipt();
        }

        private void RewriteManifestAndReceipt()
        {
            if (!File.Exists(executablePath)) return;
            EnsureFile("bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
            EnsureFile("policy/context-version.txt", "context-v1\n");
            EnsureFile("policy/schema-version.txt", "schema-v1\n");
            EnsureFile("policy/release-policy.yaml", ReleasePolicyYaml);
            EnsureFile("trust/release-signing-policy.yaml", File.Exists(Path.Combine(bundleRoot, "trust", "release-signing-policy.yaml")) ? File.ReadAllText(Path.Combine(bundleRoot, "trust", "release-signing-policy.yaml")) : "status: fixture-only\n");
            string manifestJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle.v1",
                bundleId = "islamu-release-engineering",
                bundleVersion = "1.0.0",
                policyVersion = "policy-v1",
                configVersion = "config-v1",
                trustVersion = "trust-v1",
                policyDigest = Digest(Path.Combine(bundleRoot, "policy", "release-policy.yaml")),
                configDigest = Digest(configPath),
                trustDigest = Digest(Path.Combine(bundleRoot, "trust", "release-signing-policy.yaml")),
                files = Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories)
                    .Where(path => Path.GetFileName(path) != "trusted-bundle.manifest.json")
                    .Select(path => new { path = Path.GetRelativePath(bundleRoot, path).Replace(Path.DirectorySeparatorChar, '/'), sha256 = Digest(path) })
                    .OrderBy(item => item.path, StringComparer.Ordinal)
                    .ToArray(),
            });
            File.WriteAllBytes(Path.Combine(bundleRoot, "trusted-bundle.manifest.json"), CanonicalArtifactPolicy.CanonicalizeJson(manifestJson).Bytes!);
            ResignReceipt();
        }

        private string EnsureFile(string path, string content)
        {
            string fullPath = Path.Combine(bundleRoot, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private void CreatePromotionAuthority()
        {
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion-fixture", "-f", promotionPrivateKeyPath);
            string[] publicKey = File.ReadAllText(promotionPrivateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            File.WriteAllText(allowedPromotersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey[0]} {publicKey[1]}\n");
        }

        private void CreateReleaseAuthority()
        {
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-release-fixture", "-f", releasePrivateKeyPath);
            string[] publicKey = File.ReadAllText(releasePrivateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Directory.CreateDirectory(Path.GetDirectoryName(allowedReleaseSignersPath)!);
            File.WriteAllText(allowedReleaseSignersPath, $"fixture-release-operator namespaces=\"git\",valid-after=\"20260101\",valid-before=\"20261231\" {publicKey[0]} {publicKey[1]}\n");
        }

        private void ResignReceipt()
        {
            string manifestPath = Path.Combine(bundleRoot, "trusted-bundle.manifest.json");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            JsonElement root = document.RootElement;
            string receiptJson = JsonSerializer.Serialize(new
            {
                schemaVersion = "trusted-bundle-promotion.v1",
                receiptId = "promotion-fixture-0001",
                bundleManifestSha256 = Digest(manifestPath),
                bundleId = root.GetProperty("bundleId").GetString(),
                bundleVersion = root.GetProperty("bundleVersion").GetString(),
                policyVersion = root.GetProperty("policyVersion").GetString(),
                configVersion = root.GetProperty("configVersion").GetString(),
                trustVersion = root.GetProperty("trustVersion").GetString(),
                policyDigest = root.GetProperty("policyDigest").GetString(),
                configDigest = root.GetProperty("configDigest").GetString(),
                trustDigest = root.GetProperty("trustDigest").GetString(),
                promotionPrincipal = "fixture-tooling-promoter",
            });
            File.WriteAllBytes(receiptPath, CanonicalArtifactPolicy.CanonicalizeJson(receiptJson).Bytes!);
            if (File.Exists(signaturePath)) File.Delete(signaturePath);
            RunProcess("/usr/bin/ssh-keygen", null, "-Y", "sign", "-f", promotionPrivateKeyPath, "-n", "islamu-release-promotion", receiptPath);
        }

        private string Git(params string[] args) => RunProcess("git", RepositoryPath, args);
        private static string RunProcess(string executable, string? workingDirectory, params string[] args)
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory } };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            if (executable == "git")
            {
                process.StartInfo.ArgumentList.Add("-c");
                process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
            }
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}_failed:{error}");
            return output;
        }

        private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
