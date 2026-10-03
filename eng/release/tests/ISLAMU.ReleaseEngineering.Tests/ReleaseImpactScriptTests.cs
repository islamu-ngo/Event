using System.Diagnostics;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering.Tests;

[NotInParallel]
public sealed class ReleaseImpactScriptTests
{
    [Test]
    public async Task ImgBotModifiedAssetsDoNotRequireHumanChecklist()
    {
        int exit = await RunAsync("imgbot[bot]", "Bot", null, 1,
            new { filename = "assets/banner.png", status = "modified" });

        await Assert.That(exit).IsEqualTo(0);
    }

    [Test]
    [Arguments("imgbot[bot]", "User", "assets/banner.png", "modified")]
    [Arguments("untrusted[bot]", "Bot", "assets/banner.png", "modified")]
    [Arguments("imgbot[bot]", "Bot", "src/page.svg", "modified")]
    [Arguments("imgbot[bot]", "Bot", "assets/banner.png", "added")]
    [Arguments("imgbot[bot]", "Bot", "assets/banner.png", "renamed")]
    [Arguments("imgbot[bot]", "Bot", "assets/security.png", "modified")]
    public async Task AutomaticDispositionRequiresExactIdentityAndSafeModifiedAssets(
        string author, string accountType, string filename, string status)
    {
        int exit = await RunAsync(author, accountType, null, 1, new { filename, status });

        await Assert.That(exit).IsNotEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    public async Task MissingOrIncompleteFilesCannotProveNoImpact(int expectedCount)
    {
        int exit = await RunAsync("contributor", "User", "## Release Impact\n- [x] Not applicable\n",
            expectedCount, new { filename = "assets/banner.png", status = "modified" });

        await Assert.That(exit).IsNotEqualTo(0);
    }

    [Test]
    public async Task RenameClassifiesBothOriginalAndCurrentPaths()
    {
        int exit = await RunAsync("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1,
            new { filename = "assets/banner.png", status = "renamed", previous_filename = "src/security.cs" });

        await Assert.That(exit).IsNotEqualTo(0);
    }

    [Test]
    public async Task ImgBotMixedSourceAndImageChangesCannotReceiveAutomaticDisposition()
    {
        int exit = await RunAsync("imgbot[bot]", "Bot", null, 2,
            new { filename = "assets/banner.png", status = "modified" },
            new { filename = "src/security.cs", status = "modified" });

        await Assert.That(exit).IsNotEqualTo(0);
    }

    [Test]
    public async Task EmptyFileMetadataCannotEstablishNoImpact()
    {
        int exit = await RunAsync("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1);

        await Assert.That(exit).IsNotEqualTo(0);
    }

    [Test]
    [Arguments("src/security.cs", "Security/auth impact documented")]
    [Arguments("src/Migrations/Init.cs", "Migration/data/rollback impact documented")]
    [Arguments("appsettings.json", "Configuration/secrets/deployment impact documented")]
    [Arguments("src/Explore.Blazor.Client/Clients/EventApiTagClients.g.cs", "OpenAPI/client contract impact documented")]
    [Arguments("docs/OPERATIONS.md", "Operator/self-hosting/release-note impact documented")]
    public async Task EveryImpactCategoryRequiresItsOwnDisposition(string filename, string label)
    {
        var metadata = new { filename, status = "modified" };
        int omitted = await RunAsync("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1, metadata);
        int documented = await RunAsync("contributor", "User",
            $"## Release Impact\n- [x] {label}\nDetails:\nReviewed release impact.\n", 1, metadata);

        await Assert.That(omitted).IsNotEqualTo(0);
        await Assert.That(documented).IsEqualTo(0);
    }

    [Test]
    public async Task HumanNoImpactAndDocumentedSecurityDispositionsRemainValid()
    {
        int noImpact = await RunAsync("contributor", "User", "## Release Impact\n- [x] Not applicable\n", 1,
            new { filename = "assets/banner.png", status = "modified" });
        int security = await RunAsync("contributor", "User",
            "## Release Impact\n- [x] Security/auth impact documented\nDetails:\nReviewed security change.\n", 1,
            new { filename = "src/security.cs", status = "modified" });

        await Assert.That(noImpact).IsEqualTo(0);
        await Assert.That(security).IsEqualTo(0);
    }

    private static async Task<int> RunAsync(
        string author, string accountType, string? body, int expectedCount, params object[] files)
    {
        string root = FindRepositoryRoot();
        string directory = Path.Combine(Path.GetTempPath(), $"release-impact-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string eventPath = Path.Combine(directory, "event.json");
            await File.WriteAllTextAsync(eventPath, JsonSerializer.Serialize(new
            {
                pull_request = new
                {
                    user = new { login = author, type = accountType },
                    body,
                    changed_files = expectedCount
                }
            }));
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("run");
            start.ArgumentList.Add(".ci/scripts/validate-release-impact-pr.cs");
            start.ArgumentList.Add("--");
            start.ArgumentList.Add(eventPath);
            start.Environment.Remove("GITHUB_TOKEN");
            start.Environment["RELEASE_IMPACT_FILE_METADATA"] = JsonSerializer.Serialize(files);
            using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (_, _) => exited.TrySetResult();
            if (!process.Start()) throw new InvalidOperationException("release-impact-process-start-failed");
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            try
            {
                await exited.Task.WaitAsync(deadline.Token);
                await Task.WhenAll(output, error).WaitAsync(deadline.Token);
            }
            catch
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            return process.ExitCode;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Explore.slnx"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("release-impact-repository-not-found");
    }
}
