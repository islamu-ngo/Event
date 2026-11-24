using System.Diagnostics;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class GitCliffIntegrationTests
{
    [Test]
    public async Task RealBinaryExportsContextAndSuggestsInitialTagForRootInclusiveFirstRelease()
    {
        using var repo = new TemporaryGitRepository();
        string firstOid = repo.Commit("feat(events): add community event discovery");
        string configPath = repo.WriteCliffConfig(MinimalConfig);

        GitCliffToolConfiguration tool = CreateVerifiedToolConfiguration(configPath);
        var executor = new GitCliffProcessExecutor();

        GitCliffContextExportResult exported = executor.ExportContext(
            new GitCliffContextExportRequest(tool, repo.Root, "v0.1.0", CommitRange: null));
        GitCliffBumpVersionResult bumped = executor.SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(tool, repo.Root, CommitRange: null));

        await Assert.That(exported.IsValid).IsTrue().Because(exported.Diagnostic ?? "export");
        using JsonDocument doc = JsonDocument.Parse(exported.RawContextJson!);
        await Assert.That(doc.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(1);
        JsonElement release = doc.RootElement[0];
        await Assert.That(release.GetProperty("version").GetString()).IsEqualTo("v0.1.0");
        await Assert.That(release.GetProperty("commits").GetArrayLength()).IsEqualTo(1);
        await Assert.That(release.GetProperty("commits")[0].GetProperty("id").GetString()).IsEqualTo(firstOid);

        await Assert.That(bumped.IsValid).IsTrue().Because(bumped.Diagnostic ?? "bump");
        await Assert.That(bumped.SuggestedVersion).IsEqualTo("0.1.0");
    }

    [Test]
    public async Task RealBinaryExportsContextAndSuggestsVersionForTaggedRange()
    {
        using var repo = new TemporaryGitRepository();
        string baseOid = repo.Commit("fix(events): baseline event fix");
        repo.TagAnnotated("v1.0.0", baseOid);
        string headOid = repo.Commit("feat(registration): add attendee self-service edit");
        string configPath = repo.WriteCliffConfig(MinimalConfig);

        GitCliffToolConfiguration tool = CreateVerifiedToolConfiguration(configPath);
        var executor = new GitCliffProcessExecutor();

        GitCliffContextExportResult exported = executor.ExportContext(
            new GitCliffContextExportRequest(tool, repo.Root, "v1.1.0", "v1.0.0..HEAD"));
        GitCliffBumpVersionResult bumped = executor.SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(tool, repo.Root, "v1.0.0..HEAD"));

        await Assert.That(exported.IsValid).IsTrue().Because(exported.Diagnostic ?? "export");
        using JsonDocument doc = JsonDocument.Parse(exported.RawContextJson!);
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(1);
        JsonElement commits = doc.RootElement[0].GetProperty("commits");
        await Assert.That(commits.GetArrayLength()).IsEqualTo(1);
        await Assert.That(commits[0].GetProperty("id").GetString()).IsEqualTo(headOid);

        await Assert.That(bumped.IsValid).IsTrue().Because(bumped.Diagnostic ?? "bump");
        await Assert.That(bumped.SuggestedVersion).IsEqualTo("1.1.0");
        await Assert.That(bumped.RawSuggestedVersion).IsEqualTo("v1.1.0");
    }

    [Test]
    public async Task RealBinaryRendersMinimalAllowlistedContextFromStdinOffline()
    {
        using var repo = new TemporaryGitRepository();
        string configPath = repo.WriteCliffConfig(MinimalConfig);
        GitCliffToolConfiguration tool = CreateVerifiedToolConfiguration(configPath);

        const string allowlistedContext = """
            [
              {
                "version": "v1.1.0",
                "commit_id": "cccccccccccccccccccccccccccccccccccccccc",
                "previous": {
                  "version": "v1.0.0",
                  "commit_id": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                },
                "commits": [
                  {
                    "id": "cccccccccccccccccccccccccccccccccccccccc",
                    "group": "Features",
                    "scope": "registration",
                    "message": "let attendees edit details",
                    "breaking": false,
                    "breaking_description": null,
                    "conventional": true,
                    "merge_commit": false,
                    "extra": {
                      "changeId": "CHG-2026-0001"
                    }
                  }
                ],
                "extra": {
                  "releaseLine": "v1.1"
                }
              }
            ]
            """;

        GitCliffRenderFromContextResult rendered = new GitCliffProcessExecutor().RenderFromContext(
            new GitCliffRenderFromContextRequest(tool, repo.Root, allowlistedContext));

        await Assert.That(rendered.IsValid).IsTrue().Because(rendered.Diagnostic ?? "render");
        await Assert.That(rendered.Markdown!).Contains("# Release v1.1.0");
        await Assert.That(rendered.Markdown!).Contains("Features: let attendees edit details");
        await Assert.That(rendered.Markdown!).Contains("CHG-2026-0001");
        await Assert.That(rendered.Markdown!).Contains("Line: v1.1");
    }

    [Test]
    public async Task SharedCommitCorpusMatchesCSharpCommitPolicyAndRealGitCliffBinary()
    {
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(RepositoryRoot.Find());
        string repoCliffConfig = Path.Combine(RepositoryRoot.Find(), "eng", "release", "cliff.toml");
        GitCliffToolConfiguration tool = CreateVerifiedToolConfiguration(repoCliffConfig);
        var executor = new GitCliffProcessExecutor();

        using var repo = new TemporaryGitRepository();
        var corpus = new (string Message, string Label)[]
        {
            ("feat(events): publish community event calendar", "public-feat"),
            ("fix(registration): prevent duplicate attendee confirmation", "public-fix"),
            ("perf(discovery): index location filter queries", "public-perf"),
            ("revert(events): restore previous schedule ordering", "public-revert"),
            ("docs(self-hosting): clarify reverse proxy headers", "public-docs"),
            ("test(events): add registration cancellation coverage", "internal-test-public-scope"),
            ("refactor(architecture): simplify handler registration", "internal-refactor-eng-scope"),
            ("style(events): format component markup", "internal-style"),
            ("build(build): align release analyzer flags", "internal-build"),
            ("ci(ci): run release unit tests directly", "internal-ci"),
            ("chore(release): prune obsolete local fixtures", "internal-chore"),
            ("feat(release): add internal release verification tool", "public-type-eng-scope"),
            ("feat(events): superseded draft feature\n\nChangelog: skip\nChangelog-Reason: superseded before release", "explicit-skip"),
            ("feat(registration)!: replace check-in payload\n\nBREAKING CHANGE: Send ticketId instead of legacy code.", "public-breaking"),
            ("refactor(release)!: retire legacy release CLI\n\nBREAKING CHANGE: Remove obsolete internal release commands.", "internal-breaking"),
        };

        var expectedVisible = new Dictionary<string, (string Group, bool Breaking, string Scope)>(StringComparer.Ordinal);
        var expectedOmittedOrSkipped = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string message, string label) in corpus)
        {
            string oid = repo.Commit(message);
            CommitPolicyResult evaluated = policy.EvaluateCommit(message);
            await Assert.That(evaluated.IsValid).IsTrue().Because($"C# policy rejected valid corpus entry '{label}': {string.Join(", ", evaluated.Diagnostics)}");

            if (evaluated.ReleaseVisibility == ReleaseVisibility.Visible)
            {
                string? group = policy.GetReleaseGroup(evaluated);
                await Assert.That(group).IsNotNull().Because($"Missing group for '{label}'");
                expectedVisible[oid] = (group!, evaluated.IsBreaking, evaluated.Scope!);
            }
            else
            {
                expectedOmittedOrSkipped.Add(oid);
            }
        }

        // Also include a real merge commit on a side branch and assert both C# and git-cliff skip the merge commit while preserving its valid child commit.
        string sideCommitOid = repo.CommitOnSideBranchAndMerge("fix(access): enforce tenant boundary on export", "Merge branch 'side-parity'");
        CommitPolicyResult sideEvaluated = policy.EvaluateCommit("fix(access): enforce tenant boundary on export");
        expectedVisible[sideCommitOid] = (policy.GetReleaseGroup(sideEvaluated)!, false, "access");
        string mergeOid = repo.HeadOid();
        CommitPolicyResult mergeEvaluated = policy.EvaluateCommit("Merge branch 'side-parity'");
        await Assert.That(mergeEvaluated.IsValid).IsTrue();
        await Assert.That(mergeEvaluated.ReleaseVisibility).IsEqualTo(ReleaseVisibility.Omitted);
        expectedOmittedOrSkipped.Add(mergeOid);

        GitCliffContextExportResult exported = executor.ExportContext(
            new GitCliffContextExportRequest(tool, repo.Root, "v0.1.0", CommitRange: null));
        await Assert.That(exported.IsValid).IsTrue().Because(exported.Diagnostic ?? "export");

        using JsonDocument doc = JsonDocument.Parse(exported.RawContextJson!);
        await Assert.That(doc.RootElement.GetArrayLength()).IsEqualTo(1);
        JsonElement commits = doc.RootElement[0].GetProperty("commits");

        var actualVisible = new Dictionary<string, (string Group, bool Breaking, string Scope)>(StringComparer.Ordinal);
        foreach (JsonElement commit in commits.EnumerateArray())
        {
            string id = commit.GetProperty("id").GetString()!;
            string group = commit.GetProperty("group").GetString()!;
            bool breaking = commit.GetProperty("breaking").GetBoolean();
            string scope = commit.GetProperty("scope").GetString()!;
            actualVisible[id] = (group, breaking, scope);
        }

        await Assert.That(actualVisible.Keys).IsEquivalentTo(expectedVisible.Keys);
        foreach ((string oid, (string Group, bool Breaking, string Scope) expected) in expectedVisible)
        {
            await Assert.That(actualVisible[oid]).IsEqualTo(expected);
        }

        foreach (string omittedOid in expectedOmittedOrSkipped)
        {
            await Assert.That(actualVisible.ContainsKey(omittedOid)).IsFalse();
        }
    }

    [Test]
    [Arguments("feat(api): forbidden layer scope")]
    [Arguments("feat: missing required scope")]
    [Arguments("ship(events): unknown commit type")]
    [Arguments("plain non-conventional commit message")]
    [Arguments("feat(api): forbidden scope trying to hide\n\nChangelog: skip\nChangelog-Reason: invalid scope must not skip")]
    public async Task InvalidOrUnmatchedCommitsAreRejectedByBothCSharpPolicyAndRealGitCliff(string invalidMessage)
    {
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(RepositoryRoot.Find());
        string repoCliffConfig = Path.Combine(RepositoryRoot.Find(), "eng", "release", "cliff.toml");
        GitCliffToolConfiguration tool = CreateVerifiedToolConfiguration(repoCliffConfig);

        CommitPolicyResult csharp = policy.EvaluateCommit(invalidMessage);
        await Assert.That(csharp.IsValid).IsFalse();

        using var repo = new TemporaryGitRepository();
        repo.Commit(invalidMessage);
        GitCliffContextExportResult cliff = new GitCliffProcessExecutor().ExportContext(
            new GitCliffContextExportRequest(tool, repo.Root, "v0.1.0", CommitRange: null));

        await Assert.That(cliff.IsValid).IsFalse();
        await Assert.That(cliff.Diagnostic).IsEqualTo("git_cliff_exit_nonzero");
    }

    [Test]
    public async Task RealBinaryMatchesCSharpSemVerBumpRulesAcrossZeroXPostOneAndInternalOnlyRanges()
    {
        string repoCliffConfig = Path.Combine(RepositoryRoot.Find(), "eng", "release", "cliff.toml");
        GitCliffToolConfiguration tool = CreateVerifiedToolConfiguration(repoCliffConfig);
        var executor = new GitCliffProcessExecutor();

        using var repo = new TemporaryGitRepository();
        repo.Commit("feat(events): initial feature");
        GitCliffBumpVersionResult initialBump = executor.SuggestBumpedVersion(new GitCliffBumpVersionRequest(tool, repo.Root, null));
        await Assert.That(initialBump.IsValid).IsTrue().Because(initialBump.Diagnostic ?? "initial");
        await Assert.That(initialBump.SuggestedVersion).IsEqualTo("0.1.0");

        string zeroTagBase = repo.HeadOid();
        repo.TagAnnotated("v0.2.0", zeroTagBase);
        repo.Commit("feat(events)!: breaking change on 0.x\n\nBREAKING CHANGE: Breaking on 0.x bumps minor.");
        GitCliffBumpVersionResult zeroXBreaking = executor.SuggestBumpedVersion(new GitCliffBumpVersionRequest(tool, repo.Root, null));
        await Assert.That(zeroXBreaking.IsValid).IsTrue().Because(zeroXBreaking.Diagnostic ?? "0.x breaking");
        await Assert.That(zeroXBreaking.SuggestedVersion).IsEqualTo("0.3.0");

        string oneTagBase = repo.HeadOid();
        repo.TagAnnotated("v1.2.0", oneTagBase);
        repo.Commit("feat(events)!: breaking change after 1.0\n\nBREAKING CHANGE: Breaking after 1.0 bumps major.");
        GitCliffBumpVersionResult postOneBreaking = executor.SuggestBumpedVersion(new GitCliffBumpVersionRequest(tool, repo.Root, null));
        await Assert.That(postOneBreaking.IsValid).IsTrue().Because(postOneBreaking.Diagnostic ?? "1.x breaking");
        await Assert.That(postOneBreaking.SuggestedVersion).IsEqualTo("2.0.0");

        string internalBase = repo.HeadOid();
        repo.TagAnnotated("v2.0.0", internalBase);
        repo.Commit("refactor(architecture): internal cleanup without skip trailer");
        GitCliffBumpVersionResult internalOnly = executor.SuggestBumpedVersion(new GitCliffBumpVersionRequest(tool, repo.Root, null));
        await Assert.That(internalOnly.IsValid).IsTrue().Because(internalOnly.Diagnostic ?? "internal only");
        await Assert.That(internalOnly.SuggestedVersion).IsEqualTo("2.0.0");
    }

    private static GitCliffToolConfiguration CreateVerifiedToolConfiguration(string configPath)
    {
        string repoRoot = RepositoryRoot.Find();
        string lockPath = Path.Combine(repoRoot, "eng", "release", "toolchain.lock.json");
        string platform = GitCliffToolchainStore.GetCurrentPlatform();
        string toolDirectory = GitCliffToolchainStore.ResolveConfiguredDirectory(
            lockPath,
            Environment.GetEnvironmentVariable(GitCliffToolchainStore.DefaultBundleEnvironmentVariable),
            platform);

        return new GitCliffToolConfiguration(
            lockPath,
            toolDirectory,
            configPath,
            platform,
            TimeSpan.FromSeconds(10));
    }

    private const string MinimalConfig = """
        [changelog]
        body = '''
        # Release {{ version }}
        {% if extra and extra.releaseLine %}Line: {{ extra.releaseLine }}{% endif %}
        {% for commit in commits %}
        - {{ commit.group }}: {{ commit.message }} ({{ commit.id }}){% if commit.extra and commit.extra.changeId %} {{ commit.extra.changeId }}{% endif %}
        {% endfor %}
        '''
        trim = true
        render_always = true
        format = true

        [git]
        conventional_commits = true
        require_conventional = true
        filter_unconventional = false
        split_commits = false
        protect_breaking_commits = true
        fail_on_unmatched_commit = true
        filter_commits = false
        tag_pattern = "^v?[0-9]+\\.[0-9]+\\.[0-9]+(-(?:alpha|beta|rc)\\.[0-9]+)?$"
        commit_parsers = [
          { message = "^feat", group = "Features" },
          { message = "^fix", group = "Bug Fixes" },
          { message = "^chore", skip = true },
        ]

        [bump]
        features_always_bump_minor = true
        breaking_always_bump_major = false
        initial_tag = "0.1.0"

        [remote]
        offline = true
        """;

    private sealed class TemporaryGitRepository : IDisposable
    {
        private int sequence;

        public TemporaryGitRepository()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-gc-int-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Git("init", "--initial-branch=main");
        }

        public string Root { get; }

        public string WriteCliffConfig(string toml)
        {
            string path = Path.Combine(Root, "cliff.toml");
            File.WriteAllText(path, toml);
            return path;
        }

        public string Commit(string message)
        {
            sequence++;
            string file = Path.Combine(Root, $"change-{sequence}.txt");
            File.WriteAllText(file, $"change {sequence}\n");
            Git("add", file);
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-q", "-m", message);
            return Git("rev-parse", "HEAD^{commit}").Trim();
        }

        public void TagAnnotated(string name, string target) =>
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "tag", "-a", name, target, "-m", name);

        public string HeadOid() => Git("rev-parse", "HEAD^{commit}").Trim();

        public string CommitOnSideBranchAndMerge(string sideMessage, string mergeMessage)
        {
            Git("checkout", "-q", "-b", "side-branch");
            string sideOid = Commit(sideMessage);
            Git("checkout", "-q", "main");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "merge", "-q", "--no-ff", "side-branch", "-m", mergeMessage);
            return sideOid;
        }

        private string Git(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(ReleaseToolPaths.Git)
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)!;
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(TimeSpan.FromSeconds(10)) || process.ExitCode != 0)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw new InvalidOperationException($"git failed: {stderr}");
            }

            return stdout;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
