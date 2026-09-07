// ABOUTME: Proves verify-candidate binds release evidence to exact preparation commit B.
// ABOUTME: Exercises deterministic candidate manifests, stale outputs, wrong commits, and artifact drift.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleaseCandidateVerificationTests
{
    [Test]
    public async Task VerifyCandidateAcceptsExactBAndWritesDeterministicManifestTwice()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = CandidateFixture.Create();

        (int firstCode, string firstOutput) = fixture.Verify(fixture.B);
        byte[] firstBytes = File.ReadAllBytes(fixture.CandidateManifestPath);
        string firstDigest = Sha256(firstBytes);
        (int secondCode, string secondOutput) = fixture.Verify(fixture.B);
        byte[] secondBytes = File.ReadAllBytes(fixture.CandidateManifestPath);

        using JsonDocument document = JsonDocument.Parse(firstBytes);
        JsonElement root = document.RootElement;
        await Assert.That(firstCode).IsEqualTo(Program.Success);
        await Assert.That(secondCode).IsEqualTo(Program.Success);
        await Assert.That(secondOutput).IsEqualTo(firstOutput);
        await Assert.That(secondBytes).IsEquivalentTo(firstBytes);
        await Assert.That(firstOutput).IsEqualTo("release_candidate_verified: docs/internal/releases/1.1.0/release-candidate.v1.json\n");
        await Assert.That(root.GetProperty("schemaVersion").GetString()).IsEqualTo("release-candidate.v1");
        await Assert.That(root.GetProperty("objectFormat").GetString()).IsEqualTo(fixture.ObjectFormat);
        await Assert.That(root.GetProperty("candidateOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(root.GetProperty("candidateParentOid").GetString()).IsEqualTo(fixture.A);
        await Assert.That(root.TryGetProperty("releaseBranchRef", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("releaseLineHeadOid", out _)).IsFalse();
        await Assert.That(root.GetProperty("expectedIntegrationOldOid").GetString()).IsEqualTo(fixture.A);
        await Assert.That(root.GetProperty("expectedIntegrationNewOid").GetString()).IsEqualTo(fixture.B);
        await Assert.That(root.GetProperty("baseStableRef").GetString()).IsEqualTo("refs/tags/v1.0.0");
        await Assert.That(root.GetProperty("previousPublishedRef").GetString()).IsEqualTo("refs/tags/v1.0.0");
        await Assert.That(root.GetProperty("trustedBundleTrustSha256").GetString()?.Length).IsEqualTo(64);
        await Assert.That(root.GetProperty("rangeOids").EnumerateArray().Select(item => item.GetString()!).ToArray()).IsEquivalentTo(new[] { fixture.A, fixture.B });
        await Assert.That(root.GetProperty("releaseContextSha256").GetString()).IsEqualTo(Sha256(File.ReadAllBytes(fixture.ContextPath)));
        await Assert.That(root.GetProperty("releaseNotesSha256").GetString()).IsEqualTo(Sha256(File.ReadAllBytes(fixture.NotesPath)));
        await Assert.That(root.TryGetProperty("tagObjectId", out _)).IsFalse();
        await Assert.That(root.ToString()).DoesNotContain("provider");
        await Assert.That(root.ToString()).DoesNotContain("@example");
        await Assert.That(firstDigest).IsEqualTo(Sha256(secondBytes));
    }

    [Test]
    public async Task VerifyCandidateRejectsParentADriftAndStaleManifest()
    {
        if (OperatingSystem.IsWindows()) return;

        using var parent = CandidateFixture.Create();
        (int parentCode, string parentOutput) = parent.Verify(parent.A);

        using var drift = CandidateFixture.Create();
        File.AppendAllText(drift.NotesPath, "manual drift\n");
        (int driftCode, string driftOutput) = drift.Verify(drift.B);

        using var stale = CandidateFixture.Create();
        File.WriteAllText(stale.CandidateManifestPath, "{}\n");
        (int staleCode, string staleOutput) = stale.Verify(stale.B);

        await Assert.That(parentCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(parentOutput).IsEqualTo("verify_candidate_failed: candidate_committed_artifact_mismatch\n");
        await Assert.That(driftCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(driftOutput).IsEqualTo("verify_candidate_failed: candidate_generated_artifacts_dirty\n");
        await Assert.That(staleCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(staleOutput).IsEqualTo("verify_candidate_failed: candidate_manifest_stale\n");
    }

    [Test]
    public async Task VerifyCandidateSupportsSha256RepositoriesWhenGitSupportsThem()
    {
        if (OperatingSystem.IsWindows()) return;

        using CandidateFixture? fixture = CandidateFixture.CreateSha256OrNull();
        if (fixture is null) return;

        (int exitCode, _) = fixture.Verify(fixture.B);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixture.CandidateManifestPath));

        await Assert.That(exitCode).IsEqualTo(Program.Success);
        await Assert.That(document.RootElement.GetProperty("objectFormat").GetString()).IsEqualTo("sha256");
        await Assert.That(document.RootElement.GetProperty("candidateOid").GetString()?.Length).IsEqualTo(64);
    }

    [Test]
    public async Task VerifyCandidateFailsClosedForTrustFooterAndReleaseArtifactDrift()
    {
        if (OperatingSystem.IsWindows()) return;

        using var policy = CandidateFixture.Create();
        policy.DriftPolicy();
        (int policyCode, string policyOutput) = policy.Verify(policy.B);

        using var footer = CandidateFixture.Create();
        string replacedB = footer.ReplaceTerminalFooter();
        (int footerCode, string footerOutput) = footer.Verify(replacedB);

        using var summary = CandidateFixture.Create();
        summary.DriftSummary();
        (int summaryCode, string summaryOutput) = summary.Verify(summary.B);

        using var context = CandidateFixture.Create();
        context.DriftContext();
        (int contextCode, string contextOutput) = context.Verify(context.B);

        using var missing = CandidateFixture.Create();
        missing.DeleteNotes();
        (int missingCode, string missingOutput) = missing.Verify(missing.B);

        using var linked = CandidateFixture.Create();
        linked.ReplaceNotesWithSymlink();
        (int linkedCode, string linkedOutput) = linked.Verify(linked.B);

        using var unrelated = CandidateFixture.Create();
        unrelated.DirtyUnrelatedFile();
        (int unrelatedCode, _) = unrelated.Verify(unrelated.B);

        await Assert.That(policyCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(policyOutput).IsEqualTo("verify_candidate_failed: candidate_policy_digest_mismatch\n");
        await Assert.That(footerCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(footerOutput).IsEqualTo("verify_candidate_failed: candidate_terminal_commit_not_release_metadata_skip\n");
        await Assert.That(summaryCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(summaryOutput).IsEqualTo("verify_candidate_failed: candidate_generated_artifacts_dirty\n");
        await Assert.That(contextCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(contextOutput).IsEqualTo("verify_candidate_failed: candidate_generated_artifacts_dirty\n");
        await Assert.That(missingCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingOutput).IsEqualTo("verify_candidate_failed: candidate_input_invalid\n");
        await Assert.That(linkedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(linkedOutput).IsEqualTo("verify_candidate_failed: candidate_input_invalid\n");
        await Assert.That(unrelatedCode).IsEqualTo(Program.Success);
        await Assert.That(Directory.EnumerateFiles(missing.ReleaseDirectory, "*.tmp").Any()).IsFalse();
        await Assert.That(Directory.EnumerateFiles(linked.ReleaseDirectory, "*.tmp").Any()).IsFalse();
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class CandidateFixture : IDisposable
    {
        private const string ReleasePolicyYaml = "schemaVersion: 1\nmaximumCommitMessageBytes: 8192\nreleaseVisibleTypes:\n  - feat\n  - fix\n  - perf\n  - revert\n  - docs\ninternalTypes:\n  - test\n  - refactor\n  - style\n  - build\n  - ci\n  - chore\nrequiredBreakingSignals:\n  bang: true\n  footer: BREAKING CHANGE\nskipTrailer: Changelog\nskipValue: skip\nskipReasonTrailer: Changelog-Reason\n";
        private readonly string bundleRoot;
        private readonly string authorityRoot;
        private readonly string privateKeyPath;
        private readonly string receiptPath;
        private readonly string signaturePath;
        private readonly string allowedSignersPath;
        private readonly string configPath;
        private readonly string executablePath;

        private CandidateFixture(string objectFormat)
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-candidate-{Guid.NewGuid():N}");
            RepositoryPath = Path.Combine(Root, "repo");
            bundleRoot = Path.Combine(Root, "bundle");
            authorityRoot = Path.Combine(Root, "authority");
            privateKeyPath = Path.Combine(authorityRoot, "promotion-key");
            receiptPath = Path.Combine(authorityRoot, "promotion-receipt.v1.json");
            signaturePath = receiptPath + ".sig";
            allowedSignersPath = Path.Combine(authorityRoot, "allowed-promoters");
            configPath = Path.Combine(bundleRoot, "config", "cliff.toml");
            executablePath = Path.Combine(bundleRoot, "git-cliff");
            ReleaseDirectory = Path.Combine(RepositoryPath, "docs", "internal", "releases", "1.1.0");
            ContextPath = Path.Combine(ReleaseDirectory, "release-context.v1.json");
            NotesPath = Path.Combine(ReleaseDirectory, "release-notes.md");
            CandidateManifestPath = Path.Combine(ReleaseDirectory, "release-candidate.v1.json");
            ObjectFormat = objectFormat == "sha256" ? "sha256" : "sha1";
            Directory.CreateDirectory(RepositoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(authorityRoot);
            Git("init", $"--object-format={objectFormat}", "--initial-branch=main");
            CreatePromotionAuthority();
            string previous = Commit("fix(events): preserve published event notes");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v1.0.0", previous, "-m", "v1.0.0");
            A = Commit("feat(registration): let attendees correct registration details\n\nChange-Id: CHG-2026-0001");
            WriteBundle();
            Prepare();
            Git("add", "docs/internal/releases/1.1.0");
            B = Commit("docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: release metadata commit");
        }

        public string Root { get; }
        public string RepositoryPath { get; }
        public string ReleaseDirectory { get; }
        public string ContextPath { get; }
        public string NotesPath { get; }
        public string CandidateManifestPath { get; }
        public string A { get; }
        public string B { get; }
        public string ObjectFormat { get; }

        public static CandidateFixture Create() => new("sha1");
        public static CandidateFixture? CreateSha256OrNull()
        {
            try { return new CandidateFixture("sha256"); }
            catch (InvalidOperationException) { return null; }
        }

        public void DriftPolicy() => File.AppendAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "release-policy.yaml"), "# drift\n");
        public void DriftSummary() => File.AppendAllText(Path.Combine(ReleaseDirectory, "summary.md"), "drift\n");
        public void DriftContext() => File.AppendAllText(ContextPath, " ");
        public void DeleteNotes() => File.Delete(NotesPath);
        public void DirtyUnrelatedFile() => File.AppendAllText(Path.Combine(RepositoryPath, "file.txt"), "unrelated dirty worktree state\n");

        public void ReplaceNotesWithSymlink()
        {
            File.Delete(NotesPath);
            File.CreateSymbolicLink(NotesPath, Path.Combine(ReleaseDirectory, "summary.md"));
        }

        public string ReplaceTerminalFooter()
        {
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "--amend", "-m", "docs(release): prepare 1.1.0\n\nChangelog: skip\nChangelog-Reason: altered");
            string oid = Git("rev-parse", "HEAD").Trim();
            Git("branch", "-f", "v1.1", oid);
            return oid;
        }

        public (int ExitCode, string Output) Verify(string candidateOid)
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
                using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
                using var output = new StringWriter();
                int exitCode = CandidateCommand.Run(["verify-candidate", "docs/internal/releases/1.1.0", candidateOid], output, RepositoryPath, "linux-x64", TimeSpan.FromSeconds(2));
                return (exitCode, output.ToString());
            }
            finally
            {
                foreach ((string name, string? value) in originals) Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private void Prepare()
        {
            Directory.CreateDirectory(ReleaseDirectory);
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes"));
            Directory.CreateDirectory(Path.Combine(RepositoryPath, "eng", "release", "policy"));
            string previous = Git("rev-list", "--max-parents=0", "HEAD").Trim();
            File.WriteAllText(Path.Combine(ReleaseDirectory, "release.yaml"),
                $"Version: 1.1.0\nLine: v1.1\nRelease-Date: 2026-08-14\nBase-Stable-Tag: v1.0.0\nPrevious-Published-Tag: v1.0.0\nRelease-Range:\n  Base-Ref: v1.0.0\n  Base-Oid: {previous}\n  Previous-Ref: v1.0.0\n  Previous-Oid: {previous}\nCompatibility:\n  - v1\nImpact-Dispositions:\n  breaking: not-applicable\n  security: not-applicable\n  migration: not-applicable\n  configuration: not-applicable\n  openapi: not-applicable\n  operator: documented\n");
            File.WriteAllText(Path.Combine(ReleaseDirectory, "summary.md"), "Attendees can now correct registration details.\n");
            File.WriteAllText(Path.Combine(RepositoryPath, "docs", "internal", "releases", "changes", "CHG-2026-0001.yaml"),
                "Change-Id: CHG-2026-0001\nTitle: Registration worker restart\nType: feat\nScope: registration\nSummary: Attendees can now correct registration details.\nSupersedes: []\nImpacts:\n  Breaking:\n    Reference: docs/internal/releases/README.md\n    Disposition: not-applicable\n  Security:\n    Reference: docs/SECURITY_OVERVIEW.md\n    Disposition: not-applicable\n  Migration:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: not-applicable\n  Configuration:\n    Reference: docs/CONFIGURATION.md\n    Disposition: not-applicable\n  OpenAPI:\n    Reference: docs/API_CHANGELOG.md\n    Disposition: not-applicable\n  Operator:\n    Reference: docs/RELEASE_RUNBOOK.md\n    Disposition: documented\n    Detail: Restart registration workers after deployment.\n");
            File.WriteAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "release-policy.yaml"),
                ReleasePolicyYaml);
            File.WriteAllText(Path.Combine(RepositoryPath, "eng", "release", "policy", "scope-registry.yaml"),
                "schemaVersion: 1\npublicScopes:\n  - events\n  - registration\nengineeringScopes:\n  - release\n");
            RewriteManifestAndReceipt();
            (int exitCode, _) = RunPrepareCommand();
            if (exitCode != Program.Success) throw new InvalidOperationException("prepare_fixture_failed");
        }

        private (int ExitCode, string Output) RunPrepareCommand()
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
                using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedSignersPath);
                using var output = new StringWriter();
                int exitCode = PrepareCommand.Run(["prepare", "docs/internal/releases/1.1.0"], output, RepositoryPath, "linux-x64", TimeSpan.FromSeconds(2));
                return (exitCode, output.ToString());
            }
            finally
            {
                foreach ((string name, string? value) in originals) Environment.SetEnvironmentVariable(name, value);
            }
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
            File.WriteAllText(configPath,
                "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n{% for commit in commits %}\n- {{ commit.group }}: {{ commit.message }} ({{ commit.id }})\n{% endfor %}\n\"\"\"\ntrim = true\nrender_always = true\n");
            File.WriteAllText(executablePath,
                "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.13.1\\n'; exit 0; fi\nprintf '# Release 1.1.0\\n\\n- registration: let attendees correct registration details (" + APlaceholder + ")\\n'\n");
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
            RewriteManifestAndReceipt();
        }

        private string APlaceholder => A.Length == 64 ? A[..12] : "cccccccccccc";

        private void RewriteManifestAndReceipt()
        {
            if (!File.Exists(executablePath)) return;
            EnsureFile("bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
            EnsureFile("policy/context-version.txt", "context-v1\n");
            EnsureFile("policy/schema-version.txt", "schema-v1\n");
            EnsureFile("policy/release-policy.yaml", ReleasePolicyYaml);
            EnsureFile("trust/allowed-signers", "# production signers absent\n");
            EnsureFile("trust/release-signing-policy.yaml", "status: inactive-fixture-only\n");
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
            if (!File.Exists(fullPath)) File.WriteAllText(fullPath, content);
            return fullPath;
        }

        private void CreatePromotionAuthority()
        {
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion-fixture", "-f", privateKeyPath);
            string publicKey = string.Join(' ', File.ReadAllText(privateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedSignersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey}\n");
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
            RunProcess("/usr/bin/ssh-keygen", null, "-Y", "sign", "-f", privateKeyPath, "-n", "islamu-release-promotion", receiptPath);
        }

        private string Git(params string[] args) => RunGit(RepositoryPath, args);
        private static string RunGit(string workingDirectory, params string[] args) => RunProcess("git", workingDirectory, args);
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
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}_failed");
            return output;
        }

        private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    }
}
