using Explore.AssuranceAudit;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Event.Architecture.Tests;

public sealed class PersistenceCapabilityAuditTests
{
    private const string EmptyPolicy = """{"schemaVersion":1,"capabilities":[]}""";
    private const string ExecuteApi = "M:System.Data.Common.DbCommand.ExecuteNonQuery";
    private const string Primitive = "M:Example.Primitive.Run(System.Data.Common.DbCommand)";
    private static readonly string[] References = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    [Test]
    [Arguments("Application/Business.cs")]
    [Arguments("Database/ProviderPrimitives/Business.cs")]
    public async Task RawAdoRemainsForbiddenAfterMovingFolders(string path)
    {
        var result = Analyze(PrimitiveSource, path: path);
        await Assert.That(result.Select(value => value.Category).ToArray()).IsEquivalentTo(["persistence-unapproved"]);
        await Assert.That(result.Single().Message).Contains(Primitive);
    }

    [Test]
    public async Task AliasAndWrapperCannotHideRawExecution()
    {
        const string source = """
            using Command = System.Data.Common.DbCommand;
            namespace Example;
            public static class Wrapper
            {
                public static int Run(Command command) => Hidden(command);
                private static int Hidden(Command command) => command.ExecuteNonQuery();
            }
            """;
        var result = Analyze(source);
        await Assert.That(result.Count(value => value.Category == "persistence-unapproved")).IsEqualTo(2);
    }

    [Test]
    public async Task ExactPrimitivePolicyAllowsCallersWithoutFolderExemptions()
    {
        var result = Analyze(PrimitiveSource + """

            namespace Consumer { public static class Business {
                public static int Run(System.Data.Common.DbCommand command) => Example.Primitive.Run(command);
            } }
            """, Policy());
        await Assert.That(result).IsEmpty();
    }

    [Test]
    [Arguments("M:Example.Primitive.Missing(System.Data.Common.DbCommand)", ExecuteApi)]
    [Arguments(Primitive, "M:System.Data.Common.DbCommand.ExecuteScalar")]
    public async Task StaleOwnerOrApiCannotRemainApproved(string member, string api)
    {
        var result = Analyze(PrimitiveSource, Policy(member, api));
        await Assert.That(result.Any(value => value.Category == "persistence-policy")).IsTrue();
        await Assert.That(result.Any(value => value.Category == "persistence-unapproved")).IsTrue();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("{\"schemaVersion\":2,\"capabilities\":[]}")]
    [Arguments("{\"schemaVersion\":1,\"capabilities\":[],\"folderExemptions\":[\"Database/\"]}")]
    public async Task MalformedPolicyFailsClosed(string policy)
    {
        await Assert.That(Analyze(PrimitiveSource, policy).Any(value => value.Category == "persistence-policy")).IsTrue();
    }

    [Test]
    public async Task MissingReferencesAndUnresolvedSymbolsAreFailures()
    {
        var missing = PersistenceCapabilityAudit.Analyze(
            [new("AuditSpec", new Dictionary<string, string> { ["Fixture.cs"] = PrimitiveSource }, [])], EmptyPolicy);
        await Assert.That(missing.Any(value => value.Category == "persistence-unresolved")).IsTrue();
        await Assert.That(Analyze("class Broken { void Run() { Missing.ExecuteSqlRaw(); } }")
            .Any(value => value.Category == "persistence-unresolved")).IsTrue();
    }

    [Test]
    public async Task RealEfExtensionSymbolsAreResolvedIncludingAliases()
    {
        const string source = """
            using EF = Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions;
            public static class Queries {
                public static int Run(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade database)
                    => EF.ExecuteSqlRaw(database, "SELECT 1", new object[0]);
            }
            """;
        var result = Analyze(source);
        await Assert.That(result.Select(value => value.Category).ToArray()).IsEquivalentTo(["persistence-unapproved"]);
    }

    [Test]
    public async Task OrdinaryLinqAndUnrelatedApiNamesAreNotRawPersistence()
    {
        const string source = """
            using System.Linq;
            public static class Ordinary {
                public static int[] Run(int[] values) => values.Where(value => value > 0).ToArray();
                public static int ExecuteSqlRaw() => 1;
                public static int Call() => ExecuteSqlRaw();
            }
            """;
        await Assert.That(Analyze(source)).IsEmpty();
    }

    [Test]
    public async Task CommandTextAssignmentAndDelegateCaptureCannotHideBoundaries()
    {
        const string source = """
            using System;
            using System.Data.Common;
            public static class Commands {
                public static Func<int> Run(DbCommand command) {
                    command.CommandText = "SELECT 1";
                    return command.ExecuteNonQuery;
                }
            }
            """;
        var result = Analyze(source);
        await Assert.That(result.Count(value => value.Category == "persistence-unapproved")).IsEqualTo(2);
    }

    [Test]
    public async Task EmptyScopeAndDynamicCallsCannotProduceGreenAudit()
    {
        await Assert.That(PersistenceCapabilityAudit.Analyze([], EmptyPolicy)).IsNotEmpty();
        await Assert.That(Analyze("class Calls { void Run(dynamic target) { target.Execute(); } }")
            .Any(value => value.Category == "persistence-unresolved")).IsTrue();
    }

    [Test]
    [Arguments("reason", "")]
    [Arguments("nativeTest", "M:Missing.Proof")]
    [Arguments("capability", "business-workflow")]
    public async Task CapabilityRequiresPurposeAndExistingProofOwner(string field, string value)
    {
        var policy = JsonNode.Parse(Policy())!;
        policy["capabilities"]![0]![field] = value;
        await Assert.That(Analyze(PrimitiveSource, policy.ToJsonString())
            .Any(diagnostic => diagnostic.Category == "persistence-policy")).IsTrue();
    }

    [Test]
    public async Task DuplicateCapabilityIsRejected()
    {
        var policy = JsonNode.Parse(Policy())!;
        var capabilities = policy["capabilities"]!.AsArray();
        capabilities.Add(capabilities[0]!.DeepClone());
        await Assert.That(Analyze(PrimitiveSource, policy.ToJsonString())
            .Any(diagnostic => diagnostic.Category == "persistence-policy")).IsTrue();
    }

    [Test]
    public async Task OrdinaryMethodCannotBeDeclaredAsNativeTestOwner()
    {
        var source = PrimitiveSource.Replace("[TUnit.Core.Test]", "", StringComparison.Ordinal);
        await Assert.That(Analyze(source, Policy()).Any(value => value.Category == "persistence-policy")).IsTrue();
    }

    [Test]
    public async Task AdoBatchesCannotBypassCommandPolicy()
    {
        const string source = """
            public static class Batches {
                public static int Run(System.Data.Common.DbBatch batch) => batch.ExecuteNonQuery();
                public static void Configure(System.Data.Common.DbBatchCommand command) => command.CommandText = "SELECT 1";
                public static System.Data.Common.DbCommand Create(System.Data.Common.DbDataSource source) => source.CreateCommand("SELECT 1");
            }
            """;
        await Assert.That(Analyze(source).Count(value => value.Category == "persistence-unapproved")).IsEqualTo(3);
    }

    [Test]
    public async Task ConcreteProviderOverrideUsesCanonicalAdoIdentity()
    {
        string source = PrimitiveSource.Replace("System.Data.Common.DbCommand", "Microsoft.Data.Sqlite.SqliteCommand", StringComparison.Ordinal);
        await Assert.That(Analyze(source, Policy("M:Example.Primitive.Run(Microsoft.Data.Sqlite.SqliteCommand)"))).IsEmpty();
    }

    [Test]
    public async Task OrdinalReaderMappingIsAStorageBoundary()
    {
        const string source = """
            public static class Reader {
                public static object Run(System.Data.Common.DbDataReader reader) => reader[0];
            }
            """;
        await Assert.That(Analyze(source).Select(value => value.Category).ToArray()).IsEquivalentTo(["persistence-unapproved"]);
    }

    [Test]
    [Arguments(true, true)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    public async Task EvaluatedInputsIncludeGeneratedSourcesAndRequireTheirExport(bool emitted, bool directoryPresent)
    {
        string root = Path.Combine(Path.GetTempPath(), $"persistence-audit-{Guid.CreateVersion7():N}");
        string generated = Path.Combine(root, "obj", "generated");
        Directory.CreateDirectory(generated);
        try
        {
            string project = Path.Combine(root, "Fixture.csproj");
            string ordinary = Path.Combine(root, "Ordinary.cs");
            File.WriteAllText(project, "<Project />");
            File.WriteAllText(ordinary, "public sealed class Ordinary { }");
            File.WriteAllText(Path.Combine(generated, "Generated.cs"), "#if RAW_STORAGE\n" + PrimitiveSource + "\n#endif");
            if (!directoryPresent) Directory.Delete(generated, recursive: true);
            string metadata = JsonSerializer.Serialize(new
            {
                Properties = new {
                    MSBuildProjectFullPath = project, AssemblyName = "AuditSpec", DefineConstants = "RAW_STORAGE",
                    LangVersion = "preview", AllowUnsafeBlocks = "false", OutputType = "Library",
                    PersistenceAuditExportReady = "true",
                    EmitCompilerGeneratedFiles = emitted ? "true" : "false", CompilerGeneratedFilesOutputPath = generated
                },
                Items = new {
                    Compile = new[] { new { FullPath = ordinary } },
                    PersistenceAuditGeneratedCompile = new[] { new { FullPath = Path.Combine(generated, "Generated.cs") } },
                    ReferencePath = References.Select(path => new { FullPath = path }).ToArray()
                }
            });
            if (!emitted || !directoryPresent)
            {
                await Assert.That(() => PersistenceCapabilityAudit.AnalyzeEvaluatedProjects(root, [metadata], EmptyPolicy))
                    .Throws<InvalidDataException>();
                return;
            }
            var findings = PersistenceCapabilityAudit.AnalyzeEvaluatedProjects(root, [metadata], EmptyPolicy);
            await Assert.That(findings.Select(value => value.Category).ToArray()).IsEquivalentTo(["persistence-unapproved"]);
            await Assert.That(findings.Single().Path).IsEqualTo("obj/generated/Generated.cs");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static IReadOnlyList<AssuranceDiagnostic> Analyze(string source, string policy = EmptyPolicy, string path = "Fixture.cs") =>
        PersistenceCapabilityAudit.Analyze([new("AuditSpec", new Dictionary<string, string> { [path] = source }, References)], policy);

    private static string Policy(string member = Primitive, string api = ExecuteApi) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        capabilities = new[] { new {
            assembly = "AuditSpec", member, capability = "native-test", reason = "Observe a native storage contract.",
            providerGap = "Native behavior requires direct command execution.", allowedApis = new[] { api },
            identifierHandling = "No interpolated identifiers.", parameterHandling = "No user input.",
            nativeTest = "M:Example.Proof.NativeCase"
        } }
    });

    private const string PrimitiveSource = """
        namespace Example {
            public static class Primitive {
                public static int Run(System.Data.Common.DbCommand command) => command.ExecuteNonQuery();
            }
            public static class Proof { [TUnit.Core.Test] public static void NativeCase() { } }
        }
        """;
}
