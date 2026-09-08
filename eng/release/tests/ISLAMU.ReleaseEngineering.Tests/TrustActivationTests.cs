using System.Diagnostics;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

/// <summary>
/// Genesis activation is the one step no earlier release engine can validate, so the guarantees have
/// to come from the command that writes the roots. The property that matters most is separation of
/// duty: if a single key could both promote the tooling bundle and sign the release that bundle
/// attests, one compromised key forges the whole chain. That is why activation needs two principals
/// and cannot be completed by a single operator.
/// </summary>
[NotInParallel]
public sealed class TrustActivationTests
{
    [Test]
    public async Task ActivationWritesUsableRootsAndReportsBothFingerprints()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = ActivationFixture.Create();

        (int exitCode, string output) = fixture.Activate();
        string allowedSigners = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "allowed-signers"));
        string promotionSigners = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "promotion-allowed-signers"));
        string policy = File.ReadAllText(Path.Combine(fixture.OutputDirectory, "release-signing-policy.yaml"));

        await Assert.That(exitCode).IsEqualTo(Program.Success).Because(output);
        await Assert.That(output).Contains($"trust_release_signer: principal=release-operator algorithm=ssh-ed25519 fingerprint={fixture.ReleaseFingerprint}");
        await Assert.That(output).Contains($"trust_promotion_signer: principal=tooling-promoter algorithm=ssh-ed25519 fingerprint={fixture.PromotionFingerprint}");
        await Assert.That(allowedSigners).Contains("release-operator namespaces=\"git\",valid-after=\"20260101\",valid-before=\"20261231\" ssh-ed25519 ");
        await Assert.That(promotionSigners).Contains("tooling-promoter namespaces=\"islamu-release-promotion\" ssh-ed25519 ");
        await Assert.That(policy).Contains("status: active");
        await Assert.That(policy).Contains("principal: release-operator");
        await Assert.That(policy).Contains("principal: tooling-promoter");
        await Assert.That(policy).Contains("releaseSignerCannotPromoteOwnCandidateBundle: true");

        // Public key material only. A private key must never reach these files.
        foreach (string content in new[] { allowedSigners, promotionSigners, policy })
        {
            await Assert.That(content).DoesNotContain("PRIVATE KEY");
        }
    }

    [Test]
    public async Task ComputedFingerprintsMatchSshKeygenExactly()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = ActivationFixture.Create();
        fixture.Activate();

        // Evidence records the fingerprint that TagCommand derives via `ssh-keygen -lf`. If this
        // command computed it differently, activated roots would authorize but record a value that
        // never matches the released evidence.
        await Assert.That(fixture.ReleaseFingerprint).IsEqualTo(fixture.SshKeygenFingerprint(fixture.ReleasePublicKeyPath));
        await Assert.That(fixture.PromotionFingerprint).IsEqualTo(fixture.SshKeygenFingerprint(fixture.PromotionPublicKeyPath));
    }

    [Test]
    public async Task SeparationOfDutyViolationsFailClosed()
    {
        if (OperatingSystem.IsWindows()) return;

        using var sameKey = ActivationFixture.Create();
        (int sameKeyCode, string sameKeyOutput) = sameKey.Activate(promotionKeyPath: sameKey.ReleasePublicKeyPath);

        using var samePrincipal = ActivationFixture.Create();
        (int samePrincipalCode, string samePrincipalOutput) = samePrincipal.Activate(promotionPrincipal: "release-operator");

        await Assert.That(sameKeyCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(sameKeyOutput).IsEqualTo("activate_trust_failed: trust_activation_separation_of_duty\n");
        await Assert.That(samePrincipalCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(samePrincipalOutput).IsEqualTo("activate_trust_failed: trust_activation_separation_of_duty\n");
        await Assert.That(File.Exists(Path.Combine(sameKey.OutputDirectory, "allowed-signers"))).IsFalse();
    }

    [Test]
    public async Task PrivateKeysWrongAlgorithmsAndMalformedKeysAreRefused()
    {
        if (OperatingSystem.IsWindows()) return;

        using var privateKey = ActivationFixture.Create();
        (int privateCode, string privateOutput) = privateKey.Activate(releaseKeyPath: privateKey.ReleasePrivateKeyPath);

        using var wrongAlgorithm = ActivationFixture.Create();
        string rsaPath = wrongAlgorithm.WriteKeyFile("rsa.pub", "ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABgQC comment\n");
        (int algorithmCode, string algorithmOutput) = wrongAlgorithm.Activate(releaseKeyPath: rsaPath);

        using var malformed = ActivationFixture.Create();
        string malformedPath = malformed.WriteKeyFile("bad.pub", "not-a-key\n");
        (int malformedCode, string malformedOutput) = malformed.Activate(releaseKeyPath: malformedPath);

        using var missing = ActivationFixture.Create();
        (int missingCode, string missingOutput) = missing.Activate(releaseKeyPath: Path.Combine(missing.Root, "absent.pub"));

        await Assert.That(privateCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(privateOutput).IsEqualTo("activate_trust_failed: trust_activation_private_key_supplied\n");
        await Assert.That(algorithmCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(algorithmOutput).IsEqualTo("activate_trust_failed: trust_activation_algorithm_forbidden\n");
        await Assert.That(malformedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(malformedOutput).IsEqualTo("activate_trust_failed: trust_activation_key_malformed\n");
        await Assert.That(missingCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingOutput).IsEqualTo("activate_trust_failed: trust_activation_key_missing\n");
    }

    [Test]
    public async Task InvalidValidityWindowsAndPrincipalsFailClosed()
    {
        if (OperatingSystem.IsWindows()) return;

        using var inverted = ActivationFixture.Create();
        (int invertedCode, string invertedOutput) = inverted.Activate(validFrom: "2026-12-31", validUntil: "2026-01-01");

        using var malformedDate = ActivationFixture.Create();
        (int dateCode, string dateOutput) = malformedDate.Activate(validFrom: "31-12-2026");

        using var badPrincipal = ActivationFixture.Create();
        (int principalCode, string principalOutput) = badPrincipal.Activate(releasePrincipal: "Release Operator");

        await Assert.That(invertedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(invertedOutput).IsEqualTo("activate_trust_failed: trust_activation_validity_invalid\n");
        await Assert.That(dateCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(dateOutput).IsEqualTo("activate_trust_failed: trust_activation_validity_invalid\n");
        await Assert.That(principalCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(principalOutput).IsEqualTo("activate_trust_failed: trust_activation_principal_invalid\n");
    }

    [Test]
    public async Task ReRunningIsIdempotentAndReplacingAnExistingRootIsAnExplicitDecision()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = ActivationFixture.Create();
        (int firstCode, _) = fixture.Activate();
        byte[] firstRoot = File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, "allowed-signers"));

        (int repeatCode, _) = fixture.Activate();
        byte[] repeatRoot = File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, "allowed-signers"));

        // A different key against an already-activated root is a rotation, not an activation.
        string rotatedKey = fixture.CreateAdditionalKey("rotated");
        (int rotationCode, string rotationOutput) = fixture.Activate(releaseKeyPath: rotatedKey);
        byte[] afterRefusedRotation = File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, "allowed-signers"));

        (int replaceCode, _) = fixture.Activate(releaseKeyPath: rotatedKey, replace: true);
        byte[] afterReplace = File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, "allowed-signers"));

        await Assert.That(firstCode).IsEqualTo(Program.Success);
        await Assert.That(repeatCode).IsEqualTo(Program.Success);
        await Assert.That(repeatRoot).IsEquivalentTo(firstRoot);
        await Assert.That(rotationCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(rotationOutput).IsEqualTo("activate_trust_failed: trust_activation_would_replace_existing_root\n");
        await Assert.That(afterRefusedRotation).IsEquivalentTo(firstRoot);
        await Assert.That(replaceCode).IsEqualTo(Program.Success);
        await Assert.That(afterReplace).IsNotEquivalentTo(firstRoot);
    }

    [Test]
    public async Task ActivatedRootsAuthorizeARealSignedTagThroughTheShippedSignerPolicy()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = ActivationFixture.Create();
        fixture.Activate();

        // End-to-end proof that the produced bytes are usable: sign a real tag with the release key
        // and verify it against the generated allowed-signers root using git's own SSH verification.
        string verification = fixture.SignAndVerifyTag();

        await Assert.That(verification).Contains("Good \"git\" signature for release-operator");
    }

    private sealed class ActivationFixture : IDisposable
    {
        private ActivationFixture(string root)
        {
            Root = root;
            OutputDirectory = Path.Combine(root, "trust");
            RepositoryPath = Path.Combine(root, "repo");
            ReleasePrivateKeyPath = Path.Combine(root, "release-key");
            PromotionPrivateKeyPath = Path.Combine(root, "promotion-key");
            Directory.CreateDirectory(OutputDirectory);
            Directory.CreateDirectory(RepositoryPath);
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-release", "-f", ReleasePrivateKeyPath);
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "synthetic-promotion", "-f", PromotionPrivateKeyPath);
            ReleaseFingerprint = SshKeygenFingerprint(ReleasePublicKeyPath);
            PromotionFingerprint = SshKeygenFingerprint(PromotionPublicKeyPath);
        }

        public string Root { get; }
        public string OutputDirectory { get; }
        public string RepositoryPath { get; }
        public string ReleasePrivateKeyPath { get; }
        public string PromotionPrivateKeyPath { get; }
        public string ReleasePublicKeyPath => ReleasePrivateKeyPath + ".pub";
        public string PromotionPublicKeyPath => PromotionPrivateKeyPath + ".pub";
        public string ReleaseFingerprint { get; }
        public string PromotionFingerprint { get; }

        public static ActivationFixture Create() => new(Path.Combine(Path.GetTempPath(), $"islamu-trust-activation-{Guid.NewGuid():N}"));

        public (int ExitCode, string Output) Activate(
            string? releasePrincipal = null,
            string? releaseKeyPath = null,
            string? promotionPrincipal = null,
            string? promotionKeyPath = null,
            string? validFrom = null,
            string? validUntil = null,
            bool replace = false)
        {
            List<string> args =
            [
                "activate-trust",
                "--release-principal", releasePrincipal ?? "release-operator",
                "--release-key", releaseKeyPath ?? ReleasePublicKeyPath,
                "--promotion-principal", promotionPrincipal ?? "tooling-promoter",
                "--promotion-key", promotionKeyPath ?? PromotionPublicKeyPath,
                "--valid-from", validFrom ?? "2026-01-01",
                "--valid-until", validUntil ?? "2026-12-31",
                "--output", OutputDirectory,
            ];
            if (replace) args.Add("--replace");

            using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
            int exitCode = TrustActivationCommand.Run(args.ToArray(), writer, Root);
            return (exitCode, writer.ToString());
        }

        public string WriteKeyFile(string name, string content)
        {
            string path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public string CreateAdditionalKey(string name)
        {
            string path = Path.Combine(Root, name);
            RunProcess("/usr/bin/ssh-keygen", null, "-q", "-t", "ed25519", "-N", string.Empty, "-C", $"synthetic-{name}", "-f", path);
            return path + ".pub";
        }

        public string SshKeygenFingerprint(string publicKeyPath)
        {
            string output = RunProcess("/usr/bin/ssh-keygen", null, "-lf", publicKeyPath).Trim();
            return output.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        }

        public string SignAndVerifyTag()
        {
            RunProcess("git", RepositoryPath, "init", "--initial-branch=develop");
            File.WriteAllText(Path.Combine(RepositoryPath, "file.txt"), "content\n");
            RunProcess("git", RepositoryPath, "add", ".");
            RunProcess("git", RepositoryPath, "-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", "chore(release): fixture");
            RunProcess("git", RepositoryPath, "-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "-c", "gpg.format=ssh", "-c", $"user.signingKey={ReleasePrivateKeyPath}", "tag", "-s", "v1.0.0", "-m", "v1.0.0");
            return RunProcess("git", RepositoryPath, true, "-c", "gpg.format=ssh", "-c", $"gpg.ssh.allowedSignersFile={Path.Combine(OutputDirectory, "allowed-signers")}", "verify-tag", "-v", "v1.0.0");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static string RunProcess(string executable, string? workingDirectory, params string[] args) => RunProcess(executable, workingDirectory, false, args);

        private static string RunProcess(string executable, string? workingDirectory, bool allowFailure, params string[] args)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                },
            };
            string nullDevice = "/dev/null";
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 && !allowFailure) throw new InvalidOperationException($"{executable}_failed:{error}");
            return output + error;
        }
    }
}
