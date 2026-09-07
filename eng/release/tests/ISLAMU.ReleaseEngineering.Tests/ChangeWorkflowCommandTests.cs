// ABOUTME: Proves collision-resistant Change-Id allocation, creation, preflight, and repair workflows.
// ABOUTME: Uses disposable Git repositories to verify collisions fail before commits or merges.

using System.Diagnostics;
using System.Text.RegularExpressions;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel]
public sealed class ChangeWorkflowCommandTests
{
    private static readonly Regex GeneratedId = new(
        "^CHG-[0-9A-HJKMNP-TV-Z]{26}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    [Test]
    public async Task CollisionResistantIdsAreSortableAndNeverUseSequentialFormat()
    {
        byte[] entropy = Enumerable.Range(0, 10).Select(value => (byte)value).ToArray();

        string first = ChangeIdPolicy.Create(new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero), entropy);
        string second = ChangeIdPolicy.Create(new DateTimeOffset(2026, 8, 27, 12, 0, 1, TimeSpan.Zero), entropy);

        await Assert.That(GeneratedId.IsMatch(first)).IsTrue();
        await Assert.That(GeneratedId.IsMatch(second)).IsTrue();
        await Assert.That(string.CompareOrdinal(first, second)).IsLessThan(0);
        await Assert.That(ChangeIdPolicy.IsGenerated(first)).IsTrue();
        await Assert.That(ChangeIdPolicy.IsValid("CHG-2026-0014")).IsTrue();
        await Assert.That(ChangeIdPolicy.IsGenerated("CHG-2026-0014")).IsFalse();
    }

    [Test]
    public async Task AllocateAndCreateEmitUnusedIdFragmentAndExactFooter()
    {
        using var repository = ChangeRepositoryFixture.Create();

        (int allocationCode, string allocationOutput) = repository.Run("allocate-change-id", "--target", "develop");
        string allocated = allocationOutput.Trim().Split(' ').Last();
        (int createCode, string createOutput) = repository.Run(
            "create-change",
            "--target", "develop",
            "--type", "feat",
            "--scope", "registration",
            "--title", "Attendee correction window",
            "--summary", "Attendees can correct registration details.",
            "--group", "registration-correction");

        string created = createOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Split("id=", StringSplitOptions.None)[1].Split(' ')[0];
        string fragmentPath = Path.Combine(repository.Path, "docs", "internal", "releases", "changes", created + ".yaml");
        repository.Git("add", Path.GetRelativePath(repository.Path, fragmentPath));
        string messagePath = Path.Combine(repository.Path, "COMMIT_EDITMSG");
        File.WriteAllText(
            messagePath,
            $"feat(registration): add attendee correction window\n\nChange-Id: {created}\n");
        (int preflightCode, string preflightOutput) = repository.Run(
            "preflight-commit", messagePath, "--target", "develop");

        await Assert.That(allocationCode).IsEqualTo(Program.Success);
        await Assert.That(GeneratedId.IsMatch(allocated)).IsTrue();
        await Assert.That(createCode).IsEqualTo(Program.Success);
        await Assert.That(GeneratedId.IsMatch(created)).IsTrue();
        await Assert.That(created).IsNotEqualTo(allocated);
        await Assert.That(File.Exists(fragmentPath)).IsTrue();
        await Assert.That(File.ReadAllText(fragmentPath)).Contains($"Change-Id: {created}");
        await Assert.That(File.ReadAllText(fragmentPath)).Contains("Group: registration-correction");
        await Assert.That(createOutput).Contains($"commit_footer: Change-Id: {created}");
        await Assert.That(preflightCode).IsEqualTo(Program.Success);
        await Assert.That(preflightOutput).Contains($"change_commit_verified: change-id={created}");
    }

    [Test]
    public async Task CommitAndRangePreflightRejectTargetCollisionsBeforeHistoryChanges()
    {
        using var repository = ChangeRepositoryFixture.Create();
        string collision = "CHG-2026-0011";
        string messagePath = Path.Combine(repository.Path, "COMMIT_EDITMSG");
        File.WriteAllText(messagePath, $"feat(registration): add another correction flow\n\nChange-Id: {collision}\n");
        repository.WriteFragment(collision);

        (int commitCode, string commitOutput) = repository.Run(
            "preflight-commit", messagePath, "--target", "develop");

        repository.CreateBranch("feature");
        repository.Commit(
            "feature.txt",
            "feature\n",
            $"feat(registration): add another correction flow\n\nChange-Id: {collision}");
        (int rangeCode, string rangeOutput) = repository.Run(
            "preflight-range", "--target", "develop", "--head", "HEAD");

        await Assert.That(commitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(commitOutput).Contains($"change_preflight_failed: change_id_already_reachable:{collision}");
        await Assert.That(rangeCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(rangeOutput).Contains($"change_preflight_failed: change_id_target_collision:{collision}");
    }

    [Test]
    public async Task ExactCommitRenameMakesRangeValidWithoutRewritingFooter()
    {
        using var repository = ChangeRepositoryFixture.Create();
        repository.CreateBranch("feature");
        string commit = repository.Commit(
            "feature.txt",
            "feature\n",
            "feat(registration): add another correction flow\n\nChange-Id: CHG-2026-0011");
        string replacement = ChangeIdPolicy.Create(
            new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero),
            Enumerable.Repeat((byte)7, 10).ToArray());
        repository.WriteFragment(replacement);

        (int renameCode, string renameOutput) = repository.Run(
            "rename-change",
            "--commit", commit,
            "--from", "CHG-2026-0011",
            "--to", replacement,
            "--reason", "Target branch already owns the original identifier.");
        repository.Git("add", "docs/internal/releases");
        repository.CommitStaged("chore(release): bind collision correction");
        (int rangeCode, string rangeOutput) = repository.Run(
            "preflight-range", "--target", "develop", "--head", "HEAD");
        string message = repository.Git("show", "-s", "--format=%B", commit);
        string renamePath = Path.Combine(
            repository.Path,
            "docs",
            "internal",
            "releases",
            "change-id-renames",
            commit + ".yaml");

        await Assert.That(renameCode).IsEqualTo(Program.Success);
        await Assert.That(renameOutput).Contains($"change_renamed: commit={commit} from=CHG-2026-0011 to={replacement}");
        await Assert.That(File.Exists(renamePath)).IsTrue();
        await Assert.That(File.ReadAllText(renamePath)).Contains($"Commit-Oid: {commit}");
        await Assert.That(File.ReadAllText(renamePath)).Contains($"New-Change-Id: {replacement}");
        await Assert.That(message).Contains("Change-Id: CHG-2026-0011");
        await Assert.That(message).DoesNotContain(replacement);
        await Assert.That(rangeCode).IsEqualTo(Program.Success);
        await Assert.That(rangeOutput).Contains("change_range_verified:");
    }

    [Test]
    public async Task RenamePolicyLinksReplacementFragmentToUnchangedCommit()
    {
        string oid = new('a', 40);
        string replacement = ChangeIdPolicy.Create(
            new DateTimeOffset(2026, 8, 27, 12, 0, 0, TimeSpan.Zero),
            Enumerable.Repeat((byte)9, 10).ToArray());
        ChangeIdRename rename = new(oid, "CHG-2026-0011", replacement, "Collision correction.");
        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(
            ReleaseYaml(),
            [FragmentYaml(replacement)],
            []);

        ReleaseContextValidationResult result = ReleaseContextPolicy.Build(
            input,
            [new ReleaseCommit(oid, "feat(registration): add another correction flow\n\nChange-Id: CHG-2026-0011")],
            ReleasePolicy.LoadFromRepositoryRoot(RepositoryRoot.Find()),
            changeIdRenames: [rename]);

        await Assert.That(result.IsValid).IsTrue();
        await Assert.That(result.Context?.Changes.Single().ChangeId).IsEqualTo(replacement);
        await Assert.That(result.Context?.Changes.Single().Oid).IsEqualTo(oid);
    }

    [Test]
    public async Task HookInstallerChainsExistingCheckAndRefusesAmbiguousOverwrite()
    {
        using var repository = ChangeRepositoryFixture.Create();

        string gitDirectory = repository.Git(
            "rev-parse",
            "--path-format=absolute",
            "--git-common-dir").Trim();
        string preCommit = Path.Combine(gitDirectory, "hooks", "pre-commit");
        string commitMessage = Path.Combine(gitDirectory, "hooks", "commit-msg");
        File.WriteAllText(preCommit, "#!/bin/sh\nexit 0\n");
        (int firstCode, string firstOutput) = repository.Run("install-change-hooks", "--target", "develop");
        (int secondCode, _) = repository.Run("install-change-hooks", "--target", "develop");
        string backup = preCommit + ".before-islamu-release";
        string managedPreCommit = File.ReadAllText(preCommit);
        File.WriteAllText(preCommit, "#!/bin/sh\nexit 7\n");
        (int thirdCode, string thirdOutput) = repository.Run("install-change-hooks", "--target", "develop");

        await Assert.That(firstCode).IsEqualTo(Program.Success);
        await Assert.That(firstOutput).Contains("change_hooks_installed:");
        await Assert.That(File.Exists(preCommit)).IsTrue();
        await Assert.That(File.Exists(commitMessage)).IsTrue();
        await Assert.That(File.Exists(backup)).IsTrue();
        await Assert.That(File.ReadAllText(backup)).Contains("exit 0");
        await Assert.That(managedPreCommit).Contains(backup);
        await Assert.That(File.ReadAllText(commitMessage)).Contains("preflight-commit");
        await Assert.That(secondCode).IsEqualTo(Program.Success);
        await Assert.That(thirdCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(thirdOutput).Contains("change_hooks_failed: existing_hook_not_managed:pre-commit");
    }

    private static string ReleaseYaml() =>
        """
        Version: 1.1.0
        Line: v1.1
        Release-Date: 2026-08-27
        Base-Stable-Tag: v1.0.0
        Previous-Published-Tag: v1.0.0
        Release-Range:
          Base-Ref: v1.0.0
          Base-Oid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
          Previous-Ref: v1.0.0
          Previous-Oid: cccccccccccccccccccccccccccccccccccccccc
        Compatibility:
          - v1
        Impact-Dispositions:
          breaking: not-applicable
          security: not-applicable
          migration: not-applicable
          configuration: not-applicable
          openapi: not-applicable
          operator: not-applicable
        """;

    private static string FragmentYaml(string changeId) =>
        $"""
        Change-Id: {changeId}
        Title: Registration correction
        Type: feat
        Scope: registration
        Summary: Attendees can correct registration details.
        Supersedes: []
        Impacts:
          Breaking:
            Reference: docs/internal/releases/README.md
            Disposition: not-applicable
          Security:
            Reference: docs/SECURITY_OVERVIEW.md
            Disposition: not-applicable
          Migration:
            Reference: docs/RELEASE_RUNBOOK.md
            Disposition: not-applicable
          Configuration:
            Reference: docs/CONFIGURATION.md
            Disposition: not-applicable
          OpenAPI:
            Reference: docs/API_CHANGELOG.md
            Disposition: not-applicable
          Operator:
            Reference: docs/RELEASE_CHECKLIST.md
            Disposition: not-applicable
        """;

    private sealed class ChangeRepositoryFixture : IDisposable
    {
        private ChangeRepositoryFixture(string path) => Path = path;

        public string Path { get; }

        public static ChangeRepositoryFixture Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"islamu-change-workflow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            var fixture = new ChangeRepositoryFixture(path);
            fixture.Git("init", "--initial-branch=develop");
            Directory.CreateDirectory(System.IO.Path.Combine(path, "docs", "internal", "releases", "changes"));
            Directory.CreateDirectory(System.IO.Path.Combine(path, "eng", "release", "policy"));
            File.WriteAllText(
                System.IO.Path.Combine(path, "eng", "release", "policy", "release-policy.yaml"),
                File.ReadAllText(System.IO.Path.Combine(RepositoryRoot.Find(), "eng", "release", "policy", "release-policy.yaml")));
            File.WriteAllText(
                System.IO.Path.Combine(path, "eng", "release", "policy", "scope-registry.yaml"),
                File.ReadAllText(System.IO.Path.Combine(RepositoryRoot.Find(), "eng", "release", "policy", "scope-registry.yaml")));
            fixture.WriteFragment("CHG-2026-0011");
            fixture.Commit(
                "base.txt",
                "base\n",
                "feat(registration): existing correction flow\n\nChange-Id: CHG-2026-0011");
            return fixture;
        }

        public (int ExitCode, string Output) Run(params string[] args)
        {
            using var output = new StringWriter();
            int exitCode = ChangeWorkflowCommand.Run(args, output, Path, TimeSpan.FromSeconds(5));
            return (exitCode, output.ToString());
        }

        public void WriteFragment(string changeId) =>
            File.WriteAllText(
                System.IO.Path.Combine(Path, "docs", "internal", "releases", "changes", changeId + ".yaml"),
                FragmentYaml(changeId));

        public void CreateBranch(string name) => Git("switch", "-c", name);

        public string Commit(string file, string content, string message)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, file), content);
            Git("add", ".");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        public string CommitStaged(string message)
        {
            Git(
                "-c", "user.name=Release Test",
                "-c", "user.email=release@example.invalid",
                "commit", "-m", message);
            return Git("rev-parse", "HEAD").Trim();
        }

        public string Git(params string[] args)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = Path,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                },
            };
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0
                ? output
                : throw new InvalidOperationException($"git_failed:{string.Join(' ', args)}:{error}");
        }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
