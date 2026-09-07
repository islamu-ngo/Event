// ABOUTME: Proves verify-main validates stable-main topology without mutating Git refs.
// ABOUTME: Covers newest stable, older lines, prereleases, CAS races, stale tags, and deterministic output.

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel]
public sealed class ReleaseMainVerificationTests
{
    [Test]
    public async Task VerifyMainNewestStableEmitsMoveActionAndIsDeterministicWithoutMutation()
    {
        using var repo = MainFixture.Create();
        string before = repo.SnapshotRefs();

        (int firstCode, string firstOutput) = repo.VerifyMain(repo.V100, repo.V110TagObject);
        (int secondCode, string secondOutput) = repo.VerifyMain(repo.V100, repo.V110TagObject);

        await Assert.That(firstCode).IsEqualTo(Program.Success);
        await Assert.That(secondCode).IsEqualTo(Program.Success);
        await Assert.That(firstOutput).IsEqualTo(secondOutput);
        await Assert.That(firstOutput).IsEqualTo($"release_main_verified: action=move-main old={repo.V100} new={repo.V110} tag=v1.1.0 instruction=update-main-fast-forward\n");
        await Assert.That(repo.SnapshotRefs()).IsEqualTo(before);
    }

    [Test]
    public async Task VerifyMainAlreadyAtTargetIsIdempotent()
    {
        using var repo = MainFixture.Create();
        repo.SetRemoteMain(repo.V110);

        (int code, string output) = repo.VerifyMain(repo.V110, repo.V110TagObject);

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output).IsEqualTo($"release_main_verified: action=already-at-target old={repo.V110} new={repo.V110} tag=v1.1.0 instruction=no-op-main-already-at-release\n");
    }

    [Test]
    public async Task VerifyMainAlreadyAtTargetStillRequiresObservedRemoteMainCas()
    {
        using var repo = MainFixture.Create();

        (int code, string output) = repo.VerifyMain(repo.V110, repo.V110TagObject);

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("verify_main_failed: release_main_cas_mismatch\n");
    }

    [Test]
    public async Task VerifyMainSupportsSha256Repositories()
    {
        using var repo = MainFixture.Create("sha256");

        (int code, string output) = repo.VerifyMain(repo.V100, repo.V110TagObject);

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(repo.V100.Length).IsEqualTo(64);
        await Assert.That(output).IsEqualTo($"release_main_verified: action=move-main old={repo.V100} new={repo.V110} tag=v1.1.0 instruction=update-main-fast-forward\n");
    }

    [Test]
    public async Task VerifyMainOlderLinePatchReturnsNoMainMoveAndNeverMovesBackward()
    {
        using var repo = MainFixture.Create();
        repo.SetRemoteMain(repo.V110);
        string before = repo.SnapshotRefs();

        (int code, string output) = repo.VerifyMain(repo.V110, repo.V101TagObject, releaseDirectory: "docs/internal/releases/1.0.1");

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output).IsEqualTo($"release_main_verified: action=no-main-move old={repo.V110} new={repo.V101} tag=v1.0.1 instruction=publish-release-without-main-update\n");
        await Assert.That(repo.SnapshotRefs()).IsEqualTo(before);
    }

    [Test]
    public async Task VerifyMainOlderLineNoMoveStillRequiresObservedRemoteMainCas()
    {
        using var repo = MainFixture.Create();

        (int code, string output) = repo.VerifyMain(repo.V110, repo.V101TagObject, releaseDirectory: "docs/internal/releases/1.0.1");

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("verify_main_failed: release_main_cas_mismatch\n");
    }

    [Test]
    public async Task VerifyMainRejectsPrereleaseCasRaceNonDescendantAndMissingObjects()
    {
        using var prerelease = MainFixture.Create();
        (int prereleaseCode, string prereleaseOutput) = prerelease.VerifyMain(prerelease.V100, prerelease.V111RcTagObject, releaseDirectory: "docs/internal/releases/1.1.1-rc.1");

        using var race = MainFixture.Create();
        race.SetRemoteMain(race.V110);
        (int raceCode, string raceOutput) = race.VerifyMain(race.V100, race.V110TagObject);

        using var nonDescendant = MainFixture.Create();
        nonDescendant.SetRemoteMain(nonDescendant.Parallel);
        (int nonDescendantCode, string nonDescendantOutput) = nonDescendant.VerifyMain(nonDescendant.Parallel, nonDescendant.V110TagObject);

        using var missing = MainFixture.Create();
        (int missingCode, string missingOutput) = missing.VerifyMain(new string('f', missing.V100.Length), missing.V110TagObject);

        await Assert.That(prereleaseCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(prereleaseOutput).IsEqualTo("verify_main_failed: release_main_prerelease_no_move\n");
        await Assert.That(raceCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(raceOutput).IsEqualTo("verify_main_failed: release_main_cas_mismatch\n");
        await Assert.That(nonDescendantCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(nonDescendantOutput).IsEqualTo("verify_main_failed: release_main_non_fast_forward\n");
        await Assert.That(missingCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingOutput).IsEqualTo("verify_main_failed: release_main_expected_old_missing\n");
    }

    [Test]
    public async Task VerifyMainRejectsStaleFinalEvidenceMovedTagAndShortOids()
    {
        using var stale = MainFixture.Create();
        stale.WriteEvidence("docs/internal/releases/1.1.0", stale.V110, stale.V110TagObject, targetOverride: stale.V100);
        (int staleCode, string staleOutput) = stale.VerifyMain(stale.V100, stale.V110TagObject);

        using var moved = MainFixture.Create();
        moved.DeleteTag("v1.1.0");
        moved.AnnotatedTag("v1.1.0", moved.V100, "moved");
        (int movedCode, string movedOutput) = moved.VerifyMain(moved.V100, moved.V110TagObject);

        using var shortOid = MainFixture.Create();
        (int shortCode, string shortOutput) = shortOid.VerifyMain(shortOid.V100[..12], shortOid.V110TagObject);

        await Assert.That(staleCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(staleOutput).IsEqualTo("verify_main_failed: release_main_evidence_target_mismatch\n");
        await Assert.That(movedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(movedOutput).IsEqualTo("verify_main_failed: release_main_tag_object_mismatch\n");
        await Assert.That(shortCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(shortOutput).IsEqualTo("verify_main_failed: release_main_oid_not_full\n");
    }

    [Test]
    public async Task VerifyMainIgnoresUnvalidatedHigherReleaseDirectory()
    {
        using var repo = MainFixture.Create();
        string ambient = Path.Combine(repo.Root, "docs", "internal", "releases", "9.0.0");
        Directory.CreateDirectory(ambient);
        File.WriteAllBytes(Path.Combine(ambient, "release-evidence.v1.json"), [0]);

        (int code, string output) = repo.VerifyMain(repo.V100, repo.V110TagObject);

        await Assert.That(code).IsEqualTo(Program.Success);
        await Assert.That(output).IsEqualTo($"release_main_verified: action=move-main old={repo.V100} new={repo.V110} tag=v1.1.0 instruction=update-main-fast-forward\n");
    }

    [Test]
    public async Task VerifyMainRejectsOversizedAndMalformedStableVersionsWithoutThrowing()
    {
        using var repo = MainFixture.Create();
        string oversized = $"{new string('9', 128)}.0.0";
        repo.WriteEvidence($"docs/internal/releases/{oversized}", repo.V110, repo.V110TagObject, versionOverride: oversized);

        (int code, string output) = repo.VerifyMain(repo.V100, repo.V110TagObject, $"docs/internal/releases/{oversized}");

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).IsEqualTo("verify_main_failed: release_main_version_invalid\n");
    }

    [Test]
    public async Task VerifyMainRequiresOlderLineBackportIdentityAlreadyPresentOnMain()
    {
        using var missing = MainFixture.Create();
        missing.SetRemoteMain(missing.V110);
        File.Delete(Path.Combine(missing.Root, "docs", "internal", "releases", "1.0.1", "release-context.v1.json"));
        (int missingCode, string missingOutput) = missing.VerifyMain(missing.V110, missing.V101TagObject, "docs/internal/releases/1.0.1");

        using var mismatched = MainFixture.Create();
        mismatched.SetRemoteMain(mismatched.V110);
        mismatched.WriteEvidence("docs/internal/releases/1.0.1", mismatched.V101, mismatched.V101TagObject, backportOverride: mismatched.Parallel);
        (int mismatchedCode, string mismatchedOutput) = mismatched.VerifyMain(mismatched.V110, mismatched.V101TagObject, "docs/internal/releases/1.0.1");

        await Assert.That(missingCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingOutput).IsEqualTo("verify_main_failed: release_main_forward_port_evidence_invalid\n");
        await Assert.That(mismatchedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(mismatchedOutput).IsEqualTo("verify_main_failed: release_main_forward_port_not_on_main:CHG-2026-0001\n");
    }

    [Test]
    public async Task VerifyMainBoundsHungGitAndKillsTheProcessTree()
    {
        if (OperatingSystem.IsWindows()) return;

        using var repo = MainFixture.Create();
        string pidPath = Path.Combine(repo.Root, "hung-git.pid");
        await AssertHungGitFails(repo, "fake-bin", pidPath, $"#!/bin/sh\nprintf '%s' $$ > '{pidPath}'\nsleep 10\n");
    }

    [Test]
    public async Task VerifyMainKillsGitChildThatOutlivesParentAndHoldsStdoutOpen()
    {
        if (OperatingSystem.IsWindows()) return;

        using var repo = MainFixture.Create();
        string childPidPath = Path.Combine(repo.Root, "hung-git-child.pid");
        await AssertHungGitFails(repo, "fake-bin-child", childPidPath, $"#!/bin/sh\n(sh -c 'printf %s $$ > {childPidPath}; sleep 10') &\nexit 0\n");
    }

    private static async Task AssertHungGitFails(MainFixture repo, string fakeBinName, string pidPath, string script)
    {
        string fakeBin = Path.Combine(repo.Root, fakeBinName);
        Directory.CreateDirectory(fakeBin);
        string fakeGit = Path.Combine(fakeBin, "git");
        File.WriteAllText(fakeGit, script);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(fakeGit, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", fakeBin + Path.PathSeparator + originalPath);
            var stopwatch = Stopwatch.StartNew();
            (int code, string output) = repo.VerifyMain(repo.V100, repo.V110TagObject, timeout: TimeSpan.FromMilliseconds(100));
            stopwatch.Stop();

            await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
            await Assert.That(output).IsEqualTo("verify_main_failed: release_main_git_failed\n");
            await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(2));
            int pid = int.Parse(File.ReadAllText(pidPath), System.Globalization.CultureInfo.InvariantCulture);
            await Assert.That(Process.GetProcesses().Any(process => process.Id == pid)).IsFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            if (File.Exists(pidPath) && int.TryParse(File.ReadAllText(pidPath), out int childPid))
            {
                try { Process.GetProcessById(childPid).Kill(entireProcessTree: true); } catch (ArgumentException) { }
            }
        }
    }

    [Test]
    public async Task VerifyMainRejectsHostileRepositoryStateAndIgnoresRepositoryEnvironment()
    {
        using var environment = MainFixture.Create();
        string? originalGitDirectory = Environment.GetEnvironmentVariable("GIT_DIR");
        try
        {
            Environment.SetEnvironmentVariable("GIT_DIR", Path.Combine(environment.Root, "missing-git-dir"));
            (int environmentCode, _) = environment.VerifyMain(environment.V100, environment.V110TagObject);
            await Assert.That(environmentCode).IsEqualTo(Program.Success);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_DIR", originalGitDirectory);
        }

        using var shallow = MainFixture.Create();
        File.WriteAllText(Path.Combine(shallow.GitDirectory, "shallow"), shallow.Initial + "\n");
        (int shallowCode, string shallowOutput) = shallow.VerifyMain(shallow.V100, shallow.V110TagObject);

        using var grafted = MainFixture.Create();
        Directory.CreateDirectory(Path.Combine(grafted.GitDirectory, "info"));
        File.WriteAllText(Path.Combine(grafted.GitDirectory, "info", "grafts"), $"{grafted.V110} {grafted.V100}\n");
        (int graftedCode, string graftedOutput) = grafted.VerifyMain(grafted.V100, grafted.V110TagObject);

        await Assert.That(shallowCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(shallowOutput).IsEqualTo("verify_main_failed: release_main_repository_state_invalid\n");
        await Assert.That(graftedCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(graftedOutput).IsEqualTo("verify_main_failed: release_main_repository_state_invalid\n");
    }

    [Test]
    public async Task VerifyMainRejectsMalformedFinalEvidenceWithBoundedDiagnostic()
    {
        using var missing = MainFixture.Create();
        missing.WriteEvidenceJson("docs/internal/releases/1.1.0", new
        {
            schemaVersion = "release-evidence.v1",
            version = "1.1.0",
            line = "v1.1",
            tagName = "v1.1.0",
            tagObjectId = missing.V110TagObject,
            targetOid = missing.V110,
        });
        (int missingCode, string missingOutput) = missing.VerifyMain(missing.V100, missing.V110TagObject);

        using var wrongType = MainFixture.Create();
        wrongType.WriteEvidenceJson("docs/internal/releases/1.1.0", new
        {
            schemaVersion = "release-evidence.v1",
            version = "1.1.0",
            line = "v1.1",
            tagName = "v1.1.0",
            tagObjectId = wrongType.V110TagObject,
            targetOid = 7,
            candidateOid = wrongType.V110,
        });
        (int wrongTypeCode, string wrongTypeOutput) = wrongType.VerifyMain(wrongType.V100, wrongType.V110TagObject);

        using var nullField = MainFixture.Create();
        nullField.WriteEvidenceJson("docs/internal/releases/1.1.0", new
        {
            schemaVersion = "release-evidence.v1",
            version = "1.1.0",
            line = "v1.1",
            tagName = "v1.1.0",
            tagObjectId = (string?)null,
            targetOid = nullField.V110,
            candidateOid = nullField.V110,
        });
        (int nullCode, string nullOutput) = nullField.VerifyMain(nullField.V100, nullField.V110TagObject);

        await Assert.That(missingCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingOutput).IsEqualTo("verify_main_failed: release_main_evidence_invalid\n");
        await Assert.That(wrongTypeCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(wrongTypeOutput).IsEqualTo("verify_main_failed: release_main_evidence_invalid\n");
        await Assert.That(nullCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(nullOutput).IsEqualTo("verify_main_failed: release_main_evidence_invalid\n");
    }

    private sealed class MainFixture : IDisposable
    {
        private MainFixture(string objectFormat)
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-main-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Git("init", $"--object-format={objectFormat}", "--initial-branch=main");
            Initial = Commit("initial");
            V100 = Commit("v1.0.0");
            AnnotatedTag("v1.0.0", V100);
            V110 = Commit("v1.1.0");
            Branch("v1.1", V110);
            AnnotatedTag("v1.1.0", V110);
            V110TagObject = Resolve("refs/tags/v1.1.0^{object}");
            V101 = CommitOnBranch("v1.0", V100, "v1.0.1");
            AnnotatedTag("v1.0.1", V101);
            V101TagObject = Resolve("refs/tags/v1.0.1^{object}");
            Checkout("v1.1");
            V111Rc = Commit("v1.1.1-rc.1");
            AnnotatedTag("v1.1.1-rc.1", V111Rc);
            V111RcTagObject = Resolve("refs/tags/v1.1.1-rc.1^{object}");
            Orphan("parallel");
            Parallel = Commit("parallel main");
            Checkout("v1.1");
            SetRemoteMain(V100);
            WriteEvidence("docs/internal/releases/1.1.0", V110, V110TagObject);
            WriteEvidence("docs/internal/releases/1.0.1", V101, V101TagObject);
            WriteEvidence("docs/internal/releases/1.1.1-rc.1", V111Rc, V111RcTagObject);
        }

        public string Root { get; }
        public string GitDirectory => Path.Combine(Root, ".git");
        public string Initial { get; }
        public string V100 { get; }
        public string V110 { get; }
        public string V110TagObject { get; }
        public string V101 { get; }
        public string V101TagObject { get; }
        public string V111Rc { get; }
        public string V111RcTagObject { get; }
        public string Parallel { get; }

        public static MainFixture Create(string objectFormat = "sha1") => new(objectFormat);

        public (int ExitCode, string Output) VerifyMain(string oldOid, string tagObjectId, string releaseDirectory = "docs/internal/releases/1.1.0", TimeSpan? timeout = null)
        {
            using var writer = new StringWriter();
            int exitCode = MainCommand.Run(["verify-main", releaseDirectory, oldOid, tagObjectId], writer, Root, timeout ?? TimeSpan.FromSeconds(2));
            return (exitCode, writer.ToString());
        }

        public void WriteEvidence(string relativeDirectory, string target, string tagObject, string? targetOverride = null, string? versionOverride = null, string? backportOverride = null)
        {
            string directory = Path.Combine(Root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);
            string version = versionOverride ?? Path.GetFileName(directory);
            string line = string.Join('.', version.Split('-')[0].Split('.')[..2]).Insert(0, "v");
            WriteContext(relativeDirectory, target, version == "1.0.1" ? "CHG-2026-0001" : null, backportOverride ?? V110);
            string contextSha256 = Sha256(File.ReadAllBytes(Path.Combine(directory, "release-context.v1.json")));
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = "release-evidence.v1",
                version,
                line,
                tagName = $"v{version}",
                tagObjectId = tagObject,
                targetOid = targetOverride ?? target,
                candidateOid = target,
                releaseContextSha256 = contextSha256,
            });
            File.WriteAllBytes(Path.Combine(directory, "release-evidence.v1.json"), CanonicalArtifactPolicy.CanonicalizeJson(json).Bytes!);
        }

        public void WriteContext(string relativeDirectory, string oid, string? changeId, string backportOf)
        {
            string path = Path.Combine(Root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar), "release-context.v1.json");
            string json = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                changes = changeId is null ? [] : new[] { new { oid, changeId, backport = true, backportOf } },
            });
            File.WriteAllBytes(path, CanonicalArtifactPolicy.CanonicalizeJson(json).Bytes!);
        }

        public void WriteEvidenceJson(string relativeDirectory, object payload)
        {
            string directory = Path.Combine(Root, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "release-evidence.v1.json"), JsonSerializer.Serialize(payload));
        }

        public void SetRemoteMain(string oid) => Git("update-ref", "refs/remotes/origin/main", oid);
        public void Branch(string name, string target) => Git("branch", "-f", name, target);
        public void Checkout(string name) => Git("checkout", name);
        public void AnnotatedTag(string name, string target, string? message = null) => Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", name, target, "-m", message ?? name);
        public void DeleteTag(string name) => Git("tag", "--delete", name);
        public string Resolve(string reference) => Git("rev-parse", "--verify", reference).Trim();
        public string SnapshotRefs() => Git("for-each-ref", "--format=%(refname) %(objectname)", "refs/heads", "refs/remotes", "refs/tags");

        private string CommitOnBranch(string branch, string start, string message)
        {
            Branch(branch, start);
            Checkout(branch);
            return Commit(message);
        }

        private void Orphan(string message)
        {
            Git("checkout", "--orphan", "parallel");
            File.WriteAllText(Path.Combine(Root, "file.txt"), message + Environment.NewLine);
            Git("add", "file.txt");
        }

        private string Commit(string message)
        {
            File.AppendAllText(Path.Combine(Root, "file.txt"), message + Environment.NewLine);
            Git("add", "file.txt");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
            return Resolve("HEAD");
        }

        private string Git(params string[] args)
        {
            using var process = new Process { StartInfo = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = Root } };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException(error);
            return output;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
