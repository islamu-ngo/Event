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
