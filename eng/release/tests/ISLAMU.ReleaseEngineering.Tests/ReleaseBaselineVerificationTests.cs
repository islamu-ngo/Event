// ABOUTME: Proves non-SemVer changelog baseline verification is signed, authorized, and immutable.
// ABOUTME: Exercises deterministic baseline evidence without creating or mutating release tags.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel("RuntimePromotionTrustRoot")]
public sealed class ReleaseBaselineVerificationTests
{
    [Test]
    public async Task VerifyBaselineAcceptsAuthorizedSignedAnnotatedBaselineAndWritesDeterministicEvidence()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = BaselineFixture.Create();
        string tagObject = fixture.CreateSignedBaselineTag();

        (int firstCode, string firstOutput) = fixture.VerifyBaseline(tagObject);
        byte[] firstBytes = File.ReadAllBytes(fixture.EvidencePath);
        (int secondCode, string secondOutput) = fixture.VerifyBaseline(tagObject);
        (int spawnedCode, string spawnedOutput) = fixture.SpawnVerifyBaseline(tagObject);

        using JsonDocument document = JsonDocument.Parse(firstBytes);
        JsonElement root = document.RootElement;
        await Assert.That(firstCode).IsEqualTo(Program.Success);
        await Assert.That(secondCode).IsEqualTo(Program.Success);
        await Assert.That(spawnedCode).IsEqualTo(Program.Success);
        await Assert.That(firstOutput).IsEqualTo($"release_baseline_verified: docs/internal/releases/baselines/{fixture.BaselineRef}.v1.json\n");
        await Assert.That(secondOutput).IsEqualTo(firstOutput);
        await Assert.That(spawnedOutput).IsEqualTo(firstOutput);
        await Assert.That(File.ReadAllBytes(fixture.EvidencePath)).IsEquivalentTo(firstBytes);
        await Assert.That(root.GetProperty("schemaVersion").GetString()).IsEqualTo("release-baseline.v1");
        await Assert.That(root.GetProperty("baselineRef").GetString()).IsEqualTo(fixture.BaselineRef);
        await Assert.That(root.GetProperty("targetOid").GetString()).IsEqualTo(fixture.BaselineTarget);
        await Assert.That(root.GetProperty("tagObjectId").GetString()).IsEqualTo(tagObject);
        await Assert.That(root.ToString()).DoesNotContain("provider");
    }

    [Test]
    public async Task VerifyBaselineSpawnedCliAcceptsSha256RepositoryWhenGitSupportsIt()
    {
        if (OperatingSystem.IsWindows()) return;

        using BaselineFixture? fixture = BaselineFixture.CreateSha256OrNull();
        if (fixture is null) return;
        string tagObject = fixture.CreateSignedBaselineTag();

        (int exitCode, string output) = fixture.SpawnVerifyBaseline(tagObject);

        await Assert.That(exitCode).IsEqualTo(Program.Success);
        await Assert.That(output).IsEqualTo($"release_baseline_verified: docs/internal/releases/baselines/{fixture.BaselineRef}.v1.json\n");
        await Assert.That(fixture.BaselineTarget.Length).IsEqualTo(64);
        await Assert.That(tagObject.Length).IsEqualTo(64);
    }

    [Test]
    public async Task VerifyBaselineRejectsLightweightUnsignedMovedWrongTargetWrongDateAndShortObjects()
    {
        if (OperatingSystem.IsWindows()) return;

        using var lightweight = BaselineFixture.Create();
        string lightweightObject = lightweight.CreateLightweightBaselineTag();
        (int lightweightCode, string lightweightOutput) = lightweight.VerifyBaseline(lightweightObject);

        using var unsigned = BaselineFixture.Create();
        string unsignedObject = unsigned.CreateUnsignedBaselineTag();
        (int unsignedCode, string unsignedOutput) = unsigned.VerifyBaseline(unsignedObject);

        using var wrongTarget = BaselineFixture.Create();
        string wrongTargetObject = wrongTarget.CreateSignedBaselineTag(wrongTarget.OtherCommit);
        (int wrongTargetCode, string wrongTargetOutput) = wrongTarget.VerifyBaseline(wrongTargetObject);

        using var wrongDate = BaselineFixture.Create();
        string wrongDateObject = wrongDate.CreateSignedBaselineTag(tagName: "changelog-baseline-2026-8-15");
        (int wrongDateCode, string wrongDateOutput) = wrongDate.VerifyBaseline(wrongDateObject, baselineRef: "changelog-baseline-2026-8-15");

        using var shortOid = BaselineFixture.Create();
        string shortObject = shortOid.CreateSignedBaselineTag();
        (int shortCode, string shortOutput) = shortOid.VerifyBaseline(shortObject[..12]);

        using var moved = BaselineFixture.Create();
        string originalObject = moved.CreateSignedBaselineTag();
        moved.DeleteTag();
        Thread.Sleep(TimeSpan.FromMilliseconds(1100));
        string movedObject = moved.CreateSignedBaselineTag(moved.OtherCommit);
        (int movedCode, string movedOutput) = moved.VerifyBaseline(originalObject);

        await Assert.That(lightweightCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(lightweightOutput).IsEqualTo("verify_baseline_failed: release_baseline_tag_not_annotated\n");
        await Assert.That(unsignedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(unsignedOutput).IsEqualTo("verify_baseline_failed: release_baseline_signature_invalid\n");
        await Assert.That(wrongTargetCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongTargetOutput).IsEqualTo("verify_baseline_failed: release_baseline_wrong_target\n");
        await Assert.That(wrongDateCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongDateOutput).IsEqualTo("verify_baseline_failed: release_baseline_ref_invalid\n");
        await Assert.That(shortCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(shortOutput).IsEqualTo("verify_baseline_failed: release_baseline_object_invalid\n");
        await Assert.That(movedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(movedOutput).IsEqualTo("verify_baseline_failed: release_baseline_tag_object_replaced\n");
        await Assert.That(movedObject).IsNotEqualTo(originalObject);
    }

    private sealed class BaselineFixture : IDisposable
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

        private BaselineFixture(string objectFormat)
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-baseline-{Guid.NewGuid():N}");
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
            BaselineRef = "changelog-baseline-2026-08-15";
            EvidencePath = Path.Combine(RepositoryPath, "docs", "internal", "releases", "baselines", BaselineRef + ".v1.json");
            Directory.CreateDirectory(RepositoryPath);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(authorityRoot);
            Git("init", $"--object-format={objectFormat}", "--initial-branch=main");
            CreatePromotionAuthority();
            CreateReleaseAuthority();
            BaselineTarget = Commit("baseline lower bound");
            OtherCommit = Commit("later work");
            WriteBundle();
        }

        public string Root { get; }
        public string RepositoryPath { get; }
        public string BaselineRef { get; }
        public string BaselineTarget { get; }
        public string OtherCommit { get; }
        public string EvidencePath { get; }

        public static BaselineFixture Create(string objectFormat = "sha1") => new(objectFormat);
        public static BaselineFixture? CreateSha256OrNull()
        {
            try
            {
                return new BaselineFixture("sha256");
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
        public string CreateSignedBaselineTag(string? target = null, string? tagName = null)
        {
            string name = tagName ?? BaselineRef;
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "-c", "gpg.format=ssh", "-c", $"user.signingKey={releasePrivateKeyPath}", "tag", "-s", name, target ?? BaselineTarget, "-m", name);
            return ResolveTagObject(name);
        }

        public string CreateUnsignedBaselineTag()
        {
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", BaselineRef, BaselineTarget, "-m", BaselineRef);
            return ResolveTagObject(BaselineRef);
        }

        public string CreateLightweightBaselineTag()
        {
            Git("tag", BaselineRef, BaselineTarget);
            return Git("rev-parse", $"refs/tags/{BaselineRef}").Trim();
        }

        public (int ExitCode, string Output) VerifyBaseline(string tagObjectId, string? baselineRef = null)
        {
            return RunWithEnvironment(writer => BaselineCommand.Run(["verify-baseline", baselineRef ?? BaselineRef, BaselineTarget, tagObjectId], writer, RepositoryPath, TimeSpan.FromSeconds(2)));
        }

        public (int ExitCode, string Output) SpawnVerifyBaseline(string tagObjectId)
        {
            string assemblyPath = typeof(Program).Assembly.Location;
            using RuntimePromotionTrustRootScope trustRoot = RuntimePromotionTrustRootScope.Use(allowedPromotersPath);
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = RepositoryPath,
            };
            foreach (string argument in new[] { assemblyPath, "verify-baseline", BaselineRef, BaselineTarget, tagObjectId }) startInfo.ArgumentList.Add(argument);
            startInfo.Environment["ISLAMU_RELEASE_TRUSTED_BUNDLE"] = bundleRoot;
            startInfo.Environment["ISLAMU_RELEASE_PROMOTION_RECEIPT"] = receiptPath;
            startInfo.Environment["ISLAMU_RELEASE_PROMOTION_SIGNATURE"] = signaturePath;
            startInfo.Environment["ISLAMU_RELEASE_PROMOTION_PRINCIPAL"] = "fixture-tooling-promoter";
            startInfo.Environment["ISLAMU_RELEASE_MANIFEST_SHA256"] = Digest(Path.Combine(bundleRoot, "trusted-bundle.manifest.json"));
            startInfo.Environment["ISLAMU_RELEASE_BUNDLE_ID"] = "islamu-release-engineering";
            startInfo.Environment["ISLAMU_RELEASE_BUNDLE_VERSION"] = "1.0.0";
            startInfo.Environment["ISLAMU_RELEASE_POLICY_VERSION"] = "policy-v1";
            startInfo.Environment["ISLAMU_RELEASE_CONFIG_VERSION"] = "config-v1";
            startInfo.Environment["ISLAMU_RELEASE_TRUST_VERSION"] = "trust-v1";
            using Process process = Process.Start(startInfo)!;
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("spawned_baseline_timeout");
            }

            if (error.Length != 0) throw new InvalidOperationException("spawned_baseline_stderr");
            return (process.ExitCode, output);
        }

        public void DeleteTag() => Git("tag", "-d", BaselineRef);
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

        private string Commit(string message)
        {
            File.AppendAllText(Path.Combine(RepositoryPath, "file.txt"), message + Environment.NewLine);
            Git("add", ".");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        private void WriteBundle()
        {
            File.WriteAllText(configPath, "[changelog]\nbody = \"\"\"\n# Release {{ version }}\n\"\"\"\ntrim = true\nrender_always = true\n");
            File.WriteAllText(executablePath, "#!/bin/sh\nif [ \"$1\" = \"--version\" ]; then printf 'git-cliff 2.13.1\\n'; exit 0; fi\nprintf '# Release\\n'\n");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path.Combine(bundleRoot, "toolchain.lock.json"), $$"""
                {
                  "schemaVersion": 1,
                  "tool": "git-cliff",
                  "version": "2.13.1",
                  "platforms": [{ "platform": "linux-x64", "executable": "git-cliff", "executableSha256": "{{Digest(executablePath)}}" }]
                }
                """);
            RewriteSigningPolicy();
            RewriteManifestAndReceipt();
        }

        private void RewriteSigningPolicy()
        {
            EnsureFile("trust/release-signing-policy.yaml", "schemaVersion: release-signing-policy.v1\nroles:\n  release:\n    principal: fixture-release-operator\n    algorithm: ssh-ed25519\n    validFrom: 2026-01-01\n    validUntil: 2026-12-31\n");
        }

        private void RewriteManifestAndReceipt()
        {
            EnsureFile("bin/ISLAMU.ReleaseEngineering.dll", "release-engine-binary");
            EnsureFile("policy/context-version.txt", "context-v1\n");
            EnsureFile("policy/schema-version.txt", "schema-v1\n");
            EnsureFile("policy/release-policy.yaml", ReleasePolicyYaml);
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
                files = Directory.EnumerateFiles(bundleRoot, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != "trusted-bundle.manifest.json").Select(path => new { path = Path.GetRelativePath(bundleRoot, path).Replace(Path.DirectorySeparatorChar, '/'), sha256 = Digest(path) }).OrderBy(item => item.path, StringComparer.Ordinal).ToArray(),
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
            string publicKey = string.Join(' ', File.ReadAllText(promotionPrivateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(allowedPromotersPath, $"fixture-tooling-promoter namespaces=\"islamu-release-promotion\" {publicKey}\n");
        }

        private void CreateReleaseAuthority()
        {
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-release-fixture", "-f", releasePrivateKeyPath);
            string publicKey = string.Join(' ', File.ReadAllText(releasePrivateKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            Directory.CreateDirectory(Path.GetDirectoryName(allowedReleaseSignersPath)!);
            File.WriteAllText(allowedReleaseSignersPath, $"fixture-release-operator namespaces=\"git\",valid-after=\"20260101\",valid-before=\"20261231\" {publicKey}\n");
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

        private string ResolveTagObject(string tagName) => Git("rev-parse", $"refs/tags/{tagName}^{{object}}").Trim();
        private string Git(params string[] args) => RunProcess("git", RepositoryPath, args);
        private static string Digest(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
        private static string RunProcess(string executable, string? workingDirectory, params string[] args)
        {
            using var process = new Process { StartInfo = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory } };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            if (executable == "git") { process.StartInfo.ArgumentList.Add("-c"); process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}"); }
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"{executable}_failed:{error}");
            return output;
        }
    }
}
