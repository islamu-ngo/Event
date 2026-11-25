using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ContractDiffIntegrationTests
{
    private const string OpenApiSchemaPath = "schemas/openapi_islamu-event.json";

    [Test]
    public async Task ReadsOnlyPinnedGitRevisionsIndependentOfCorruptedWorkingTreeAndRepeatsByteIdentically()
    {
        using var repo = new TemporaryContractRepo();
        repo.WriteFile(".env.example", "DATABASE_PROVIDER=postgres\nLEGACY_SECRET=alpha-secret-value\n");
        repo.WriteFile(OpenApiSchemaPath, """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/events": {
                  "get": {
                    "responses": { "200": { "description": "OK" } }
                  }
                }
              }
            }
            """);
        repo.WriteFile("schemas/configuration-manifest-v1alpha2.schema.json", """
            {
              "type": "object",
              "properties": { "mode": { "type": "string" } }
            }
            """);
        repo.WriteFile("src/Explore.Persistence/Migrations/ExploreDbContextModelSnapshot.cs", "// v1");
        repo.WriteFile("src/Explore.Persistence/Migrations/20260901000000_Init.cs", "// init");
        repo.WriteFile("src/Explore.Persistence/Migrations/20260901000000_Init.Designer.cs", "// designer");
        string baseOid = repo.CommitAll("feat(events): baseline contract");

        repo.WriteFile(".env.example", "DATABASE_PROVIDER=postgres\n# NEW_OPT=beta-secret-value\n");
        repo.WriteFile(OpenApiSchemaPath, """
            {
              "openapi": "3.1.1",
              "paths": {
                "/api/events": {
                  "get": {
                    "responses": { "200": { "description": "OK" } }
                  },
                  "post": {
                    "responses": { "201": { "description": "Created" } }
                  }
                }
              }
            }
            """);
        repo.WriteFile("src/Explore.Persistence/Migrations/ExploreDbContextModelSnapshot.cs", "// v2");
        repo.WriteFile("src/Explore.Persistence/Migrations/20261124120000_AddAgenda.cs", "// add agenda");
        repo.WriteFile("src/Explore.Persistence/Migrations/20261124120000_AddAgenda.Designer.cs", "// designer");
        string headOid = repo.CommitAll("feat(events): expand contract");

        // Run against clean working tree first.
        string cleanOutput = Path.Combine(repo.OutputDir, "clean.json");
        using var cleanWriter = new StringWriter();
        int cleanCode = ContractDiffCommand.Run(
            ["diff-contracts", baseOid, headOid, cleanOutput],
            cleanWriter,
            repo.RepoRoot,
            TimeSpan.FromSeconds(10));
        await Assert.That(cleanCode).IsEqualTo(Program.Success).Because(cleanWriter.ToString());

        // Corrupt working tree severely: invalid OpenAPI JSON, deleted .env.example, extra untracked migration.
        repo.WriteFile(OpenApiSchemaPath, "{{{{ CORRUPTED WORKING TREE");
        File.Delete(Path.Combine(repo.RepoRoot, ".env.example"));
        repo.WriteFile("src/Explore.Persistence/Migrations/20261125000000_UncommittedDirty.cs", "// dirty");

        string dirtyOutput = Path.Combine(repo.OutputDir, "dirty.json");
        using var dirtyWriter = new StringWriter();
        int dirtyCode = ContractDiffCommand.Run(
            ["diff-contracts", baseOid, headOid, dirtyOutput],
            dirtyWriter,
            repo.RepoRoot,
            TimeSpan.FromSeconds(10));
        await Assert.That(dirtyCode).IsEqualTo(Program.Success).Because(dirtyWriter.ToString());

        byte[] cleanBytes = await File.ReadAllBytesAsync(cleanOutput);
        byte[] dirtyBytes = await File.ReadAllBytesAsync(dirtyOutput);
        await Assert.That(dirtyBytes).IsEquivalentTo(cleanBytes);

        string json = Encoding.UTF8.GetString(cleanBytes);
        await Assert.That(json).DoesNotContain("alpha-secret-value");
        await Assert.That(json).DoesNotContain("beta-secret-value");
        await Assert.That(json).DoesNotContain("UncommittedDirty");

        using JsonDocument doc = JsonDocument.Parse(cleanBytes);
        JsonElement changes = doc.RootElement.GetProperty("changes");
        await Assert.That(changes.EnumerateArray().Any(c => c.GetProperty("kind").GetString() == "operation_added")).IsTrue();
        await Assert.That(changes.EnumerateArray().Any(c => c.GetProperty("kind").GetString() == "env_var_removed")).IsTrue();
        await Assert.That(changes.EnumerateArray().Any(c => c.GetProperty("kind").GetString() == "migration_added")).IsTrue();
    }

    [Test]
    public async Task RejectsOversizedPinnedJsonBlobFailClosed()
    {
        using var repo = new TemporaryContractRepo();
        repo.WriteFile(OpenApiSchemaPath, """{"openapi":"3.1.1","paths":{}}""");
        string baseOid = repo.CommitAll("feat(events): small spec");
        repo.WriteFile(OpenApiSchemaPath, "{\"openapi\":\"3.1.1\",\"info\":{\"description\":\"" + new string('x', 4096) + "\"},\"paths\":{}}");
        string headOid = repo.CommitAll("feat(events): oversized spec");

        var boundedReader = new GitProcessObjectReader(repo.RepoRoot, TimeSpan.FromSeconds(10), maximumBlobBytes: 512);
        string outputPath = Path.Combine(repo.OutputDir, "oversized.json");
        using var writer = new StringWriter();
        int code = ContractDiffCommand.Run(["diff-contracts", baseOid, headOid, outputPath], writer, boundedReader);

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(writer.ToString()).Contains("git_object_blob_too_large");
        await Assert.That(File.Exists(outputPath)).IsFalse();
    }

    private sealed class TemporaryContractRepo : IDisposable
    {
        public TemporaryContractRepo()
        {
            Root = Path.Combine(Path.GetTempPath(), $"islamu-cdiff-int-{Guid.NewGuid():N}");
            RepoRoot = Path.Combine(Root, "repo");
            OutputDir = Path.Combine(Root, "out");
            Directory.CreateDirectory(RepoRoot);
            Directory.CreateDirectory(OutputDir);
            Git("init", "--initial-branch=main");
        }

        public string Root { get; }
        public string RepoRoot { get; }
        public string OutputDir { get; }

        public void WriteFile(string relativePath, string content)
        {
            string full = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }

        public string CommitAll(string message)
        {
            Git("add", "-A");
            Git("-c", "user.name=Release Test", "-c", "user.email=release@example.invalid", "commit", "-q", "-m", message);
            return Git("rev-parse", "HEAD^{commit}").Trim();
        }

        private string Git(params string[] arguments)
        {
            var startInfo = new ProcessStartInfo(ReleaseToolPaths.Git)
            {
                WorkingDirectory = RepoRoot,
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
            foreach (string arg in arguments)
            {
                startInfo.ArgumentList.Add(arg);
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
