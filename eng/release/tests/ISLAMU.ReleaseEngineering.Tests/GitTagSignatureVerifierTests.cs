using System.Diagnostics;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class GitTagSignatureVerifierTests
{
    [Test]
    public async Task RepositoryDefaultAllowedSignersFailsClosedUntilProductionSignerIsEnrolled()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SignedTagFixture();
        string commit = fixture.Commit("feat(events): publish first release");
        fixture.CreateSignedTag("v0.1.0", commit, fixture.AuthorizedKeyPath);
        string repoAllowedSigners = Path.Combine(RepositoryRoot.Find(), "eng", "release", "trust", "allowed-signers");

        GitTagVerificationResult result = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0",
            repoAllowedSigners,
            ExpectedTargetOid: commit));

        await Assert.That(result.IsValid).IsFalse();
        await Assert.That(result.Diagnostic).IsEqualTo("tag_allowed_signers_empty");
    }

    [Test]
    public async Task VerifiesSignedAnnotatedTagAndEnforcesTagTargetEventShaAndSignerBinding()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SignedTagFixture();
        string firstCommit = fixture.Commit("feat(events): publish first release");
        string secondCommit = fixture.Commit("fix(events): correct schedule alignment");
        fixture.CreateSignedTag("v0.1.0", secondCommit, fixture.AuthorizedKeyPath);

        GitTagVerificationResult valid = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0",
            fixture.AllowedSignersPath,
            ExpectedTargetOid: secondCommit,
            ExpectedEventSha: secondCommit,
            ExpectedSignerPrincipal: "release-operator"));

        await Assert.That(valid.IsValid).IsTrue().Because(valid.Diagnostic ?? "valid SSH signed tag");
        await Assert.That(valid.TagName).IsEqualTo("v0.1.0");
        await Assert.That(valid.TargetCommitOid).IsEqualTo(secondCommit);
        await Assert.That(valid.SignerPrincipal).IsEqualTo("release-operator");
        await Assert.That(valid.TagObjectId).IsNotNull();

        GitTagVerificationResult wrongTarget = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0",
            fixture.AllowedSignersPath,
            ExpectedTargetOid: firstCommit));
        await Assert.That(wrongTarget.IsValid).IsFalse();
        await Assert.That(wrongTarget.Diagnostic).IsEqualTo("tag_target_mismatch");

        GitTagVerificationResult wrongEventSha = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0",
            fixture.AllowedSignersPath,
            ExpectedTargetOid: secondCommit,
            ExpectedEventSha: firstCommit));
        await Assert.That(wrongEventSha.IsValid).IsFalse();
        await Assert.That(wrongEventSha.Diagnostic).IsEqualTo("tag_event_sha_mismatch");

        GitTagVerificationResult wrongPrincipal = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0",
            fixture.AllowedSignersPath,
            ExpectedTargetOid: secondCommit,
            ExpectedSignerPrincipal: "other-operator"));
        await Assert.That(wrongPrincipal.IsValid).IsFalse();
        await Assert.That(wrongPrincipal.Diagnostic).IsEqualTo("tag_signer_unauthorized");
    }

    [Test]
    public async Task RejectsLightweightUnsignedUnauthorizedAndRenamedTags()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SignedTagFixture();
        string commit = fixture.Commit("feat(events): initial release");

        fixture.Git("tag", "v0.1.0-alpha.1", commit);
        GitTagVerificationResult lightweight = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0-alpha.1",
            fixture.AllowedSignersPath));
        await Assert.That(lightweight.IsValid).IsFalse();
        await Assert.That(lightweight.Diagnostic).IsEqualTo("tag_not_annotated");

        fixture.Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", "v0.1.0-alpha.2", commit, "-m", "unsigned annotated tag");
        GitTagVerificationResult unsigned = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0-alpha.2",
            fixture.AllowedSignersPath));
        await Assert.That(unsigned.IsValid).IsFalse();
        await Assert.That(unsigned.Diagnostic).IsEqualTo("tag_not_signed");

        fixture.CreateSignedTag("v0.1.0-alpha.3", commit, fixture.UnauthorizedKeyPath);
        GitTagVerificationResult unauthorized = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.1.0-alpha.3",
            fixture.AllowedSignersPath));
        await Assert.That(unauthorized.IsValid).IsFalse();
        await Assert.That(unauthorized.Diagnostic).IsEqualTo("tag_signature_invalid");

        fixture.CreateSignedTag("v0.1.0", commit, fixture.AuthorizedKeyPath);
        string signedTagObjectId = fixture.Git("rev-parse", "refs/tags/v0.1.0^{object}").Trim();
        fixture.Git("update-ref", "refs/tags/v0.2.0", signedTagObjectId);
        GitTagVerificationResult renamed = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "v0.2.0",
            fixture.AllowedSignersPath));
        await Assert.That(renamed.IsValid).IsFalse();
        await Assert.That(renamed.Diagnostic).IsEqualTo("tag_name_mismatch");
    }

    [Test]
    public async Task ReadsRootInclusiveFirstReleaseRangeWhenNoPriorTagsExistAndBoundedRangeAfterSignedBaseline()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new SignedTagFixture();
        string rootCommit = fixture.Commit("feat(events): establish event catalog");
        string secondCommit = fixture.Commit("fix(events): preserve organizer timezone");

        IReadOnlyList<ReleaseCommit> rootInclusive = GitTagSignatureVerifier.ReadCommitRange(
            fixture.RepositoryRoot,
            previousExclusiveRef: null,
            endRef: "HEAD");
        await Assert.That(rootInclusive.Select(commit => commit.Oid).ToArray()).IsEquivalentTo([rootCommit, secondCommit]);

        fixture.CreateSignedTag("changelog-baseline-2026-08-15", rootCommit, fixture.AuthorizedKeyPath);
        GitTagVerificationResult baseline = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            fixture.RepositoryRoot,
            "changelog-baseline-2026-08-15",
            fixture.AllowedSignersPath,
            ExpectedTargetOid: rootCommit));
        await Assert.That(baseline.IsValid).IsTrue().Because(baseline.Diagnostic ?? "signed baseline tag");

        IReadOnlyList<ReleaseCommit> afterBaseline = GitTagSignatureVerifier.ReadCommitRange(
            fixture.RepositoryRoot,
            previousExclusiveRef: "changelog-baseline-2026-08-15",
            endRef: "HEAD");
        await Assert.That(afterBaseline.Select(commit => commit.Oid).ToArray()).IsEquivalentTo([secondCommit]);
    }

    private sealed class SignedTagFixture : IDisposable
    {
        private int commitSequence;

        public SignedTagFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-signed-tag-{Guid.NewGuid():N}");
            RepositoryRoot = Path.Combine(Root, "repo");
            AuthorizedKeyPath = Path.Combine(Root, "authorized-key");
            UnauthorizedKeyPath = Path.Combine(Root, "unauthorized-key");
            AllowedSignersPath = Path.Combine(Root, "allowed-signers");
            Directory.CreateDirectory(RepositoryRoot);

            Run(ReleaseToolPaths.SshKeygen, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "authorized", "-f", AuthorizedKeyPath);
            Run(ReleaseToolPaths.SshKeygen, "-q", "-t", "ed25519", "-N", string.Empty, "-C", "unauthorized", "-f", UnauthorizedKeyPath);
            string authorizedPublic = string.Join(' ', File.ReadAllText(AuthorizedKeyPath + ".pub").Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2));
            File.WriteAllText(AllowedSignersPath, $"release-operator namespaces=\"git\" {authorizedPublic}\n");

            Git("init", "--initial-branch=main");
        }

        public string Root { get; }
        public string RepositoryRoot { get; }
        public string AuthorizedKeyPath { get; }
        public string UnauthorizedKeyPath { get; }
        public string AllowedSignersPath { get; }

        public string Commit(string message)
        {
            commitSequence++;
            string filePath = Path.Combine(RepositoryRoot, $"file-{commitSequence}.txt");
            File.WriteAllText(filePath, $"content {commitSequence}\n");
            Git("add", filePath);
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-q", "-m", message);
            return Git("rev-parse", "HEAD^{commit}").Trim();
        }

        public void CreateSignedTag(string tagName, string targetOid, string keyPath)
        {
            Git(
                "-c", "user.name=Release Test",
                "-c", "user.email=release@example.invalid",
                "-c", "gpg.format=ssh",
                "-c", $"user.signingKey={keyPath}",
                "tag", "-s", tagName, targetOid, "-m", $"Release {tagName}");
        }

        public string Git(params string[] arguments) => RunIn(ReleaseToolPaths.Git, RepositoryRoot, arguments);

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static string Run(string executable, params string[] arguments) => RunIn(executable, Path.GetTempPath(), arguments);

        private static string RunIn(string executable, string workingDirectory, params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw new InvalidOperationException($"Command failed: {executable} {string.Join(' ', arguments)} -> {stderr}");
            }

            return stdout;
        }
    }
}
