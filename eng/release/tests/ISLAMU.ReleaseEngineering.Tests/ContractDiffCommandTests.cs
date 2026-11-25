using System.Text.Json;
using ISLAMU.ReleaseEngineering;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class ContractDiffCommandTests
{
    private const string DiffContractsCommand = "diff-contracts";
    private const string ContractDiffFileName = "contract-diff.v1.json";
    private const string OpenApiSchemaPath = "schemas/openapi_islamu-event.json";

    private static readonly string BaseOid = new('a', 40);
    private static readonly string HeadOid = new('b', 40);

    [Test]
    public async Task RejectsMissingOrMalformedArgumentsAndShortOids()
    {
        using var dir = new TempDirectory();
        string outputPath = Path.Combine(dir.Path, ContractDiffFileName);
        var reader = new InMemoryGitObjectReader().AddCommit(BaseOid).AddCommit(HeadOid);

        (int missingCode, string missingOut) = Run(reader, [DiffContractsCommand]);
        (int shortCode, string shortOut) = Run(reader, [DiffContractsCommand, "aaaaaaa", HeadOid, outputPath]);
        (int refCode, string refOut) = Run(reader, [DiffContractsCommand, "HEAD~1", "HEAD", outputPath]);
        (int missingCommitCode, string missingCommitOut) = Run(reader, [DiffContractsCommand, BaseOid, new string('f', 40), outputPath]);

        await Assert.That(missingCode).IsEqualTo(Program.UsageError);
        await Assert.That(missingOut).Contains("invalid_arguments");
        await Assert.That(shortCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(shortOut).Contains("contract_diff_invalid_oid");
        await Assert.That(refCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(refOut).Contains("contract_diff_invalid_oid");
        await Assert.That(missingCommitCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(missingCommitOut).Contains("contract_diff_head_commit_missing");
        await Assert.That(File.Exists(outputPath)).IsFalse();
    }

    [Test]
    public async Task WritesCanonicalSortedArtifactAtomicallyWithoutClobberingExistingFile()
    {
        using var dir = new TempDirectory();
        string outputA = Path.Combine(dir.Path, "a", ContractDiffFileName);
        string outputB = Path.Combine(dir.Path, "b", ContractDiffFileName);

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string>
            {
                [".env.example"] = "Z_KEY=old\nA_KEY=old\n",
                [OpenApiSchemaPath] = """{"openapi":"3.1.1","paths":{"/api/z":{"get":{"responses":{"200":{"description":"OK"}}}},"/api/a":{"delete":{"responses":{"204":{"description":"No Content"}}}}}}""",
            })
            .AddCommit(HeadOid, new Dictionary<string, string>
            {
                [".env.example"] = "A_KEY=new\n",
                [OpenApiSchemaPath] = """{"openapi":"3.1.1","paths":{"/api/b":{"get":{"responses":{"200":{"description":"OK"}}}}}}""",
            });

        (int firstCode, string firstOut) = Run(reader, [DiffContractsCommand, BaseOid, HeadOid, outputA]);
        (int secondCode, string secondOut) = Run(reader, [DiffContractsCommand, BaseOid, HeadOid, outputB]);
        (int clobberCode, string clobberOut) = Run(reader, [DiffContractsCommand, BaseOid, HeadOid, outputA]);

        await Assert.That(firstCode).IsEqualTo(Program.Success).Because(firstOut);
        await Assert.That(secondCode).IsEqualTo(Program.Success).Because(secondOut);
        await Assert.That(clobberCode).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(clobberOut).Contains("contract_diff_output_exists");

        byte[] bytesA = await File.ReadAllBytesAsync(outputA);
        byte[] bytesB = await File.ReadAllBytesAsync(outputB);
        await Assert.That(bytesB).IsEquivalentTo(bytesA);
        await Assert.That(Directory.EnumerateFiles(Path.GetDirectoryName(outputA)!, "*.tmp")).IsEmpty();

        using JsonDocument doc = JsonDocument.Parse(bytesA);
        JsonElement root = doc.RootElement;
        await Assert.That(root.GetProperty("schemaVersion").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("baseOid").GetString()).IsEqualTo(BaseOid);
        await Assert.That(root.GetProperty("headOid").GetString()).IsEqualTo(HeadOid);
        await Assert.That(root.GetProperty("evaluatedFamilies").EnumerateArray().Select(e => e.GetString()!).ToArray())
            .IsEquivalentTo(["openapi", "configuration", "migrations"]);
        await Assert.That(root.GetProperty("summary").GetProperty("breaking").GetInt32()).IsGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task RejectsMalformedJsonWithoutCreatingOutputArtifact()
    {
        using var dir = new TempDirectory();
        string outputPath = Path.Combine(dir.Path, ContractDiffFileName);

        InMemoryGitObjectReader reader = new InMemoryGitObjectReader()
            .AddCommit(BaseOid, new Dictionary<string, string> { [OpenApiSchemaPath] = "{}" })
            .AddCommit(HeadOid, new Dictionary<string, string> { [OpenApiSchemaPath] = "not-json" });

        (int code, string output) = Run(reader, [DiffContractsCommand, BaseOid, HeadOid, outputPath]);

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).Contains("contract_diff_openapi_malformed");
        await Assert.That(File.Exists(outputPath)).IsFalse();
    }

    [Test]
    public async Task RejectsSymlinkedOutputTargetOrParentDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TempDirectory();
        string realDir = Path.Combine(dir.Path, "real");
        string linkedDir = Path.Combine(dir.Path, "linked");
        Directory.CreateDirectory(realDir);
        Directory.CreateSymbolicLink(linkedDir, realDir);

        var reader = new InMemoryGitObjectReader().AddCommit(BaseOid).AddCommit(HeadOid);
        string outputViaLink = Path.Combine(linkedDir, ContractDiffFileName);

        (int code, string output) = Run(reader, [DiffContractsCommand, BaseOid, HeadOid, outputViaLink]);

        await Assert.That(code).IsEqualTo(Program.ToolchainRejected);
        await Assert.That(output).Contains("contract_diff_output_invalid");
        await Assert.That(File.Exists(Path.Combine(realDir, ContractDiffFileName))).IsFalse();
    }

    private static (int ExitCode, string Output) Run(IGitObjectReader reader, string[] args)
    {
        using var output = new StringWriter();
        int code = ContractDiffCommand.Run(args, output, reader);
        return (code, output.ToString());
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"islamu-cdiff-cmd-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
