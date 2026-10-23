using Explore.AssuranceAudit;

namespace Event.Architecture.Tests;

public sealed class AssuranceAuditTests
{
    [Test]
    public async Task PortfolioInventory_PreservesPartialOwnersAndDeclaredVariants()
    {
        string root = Path.Combine(Path.GetTempPath(), $"portfolio-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Fixture.cs"), """
                namespace Example;
                [Category("Runtime")]
                public partial class Fixture : DatabaseFixture { }
                """);
            File.WriteAllText(Path.Combine(root, "Cases.cs"), """
                namespace Example;
                public partial class Fixture
                {
                    [Test, Arguments(false), Arguments(true)]
                    public void RejectsMutation(bool locked) { }
                    [Test, MethodDataSource(nameof(Providers))]
                    public void ProviderBoundary(string provider) { }
                }
                """);
            File.WriteAllText(Path.Combine(root, "vector.json"), "{}");
            string metadata = """
                {"Properties":{"MSBuildProjectFullPath":"Owner.csproj"},"Items":{
                  "Compile":[{"FullPath":"Fixture.cs"},{"FullPath":"Cases.cs","Link":"Shared/Cases.cs"}],
                  "None":[{"FullPath":"vector.json"}]}}
                """;
            string secondOwner = metadata.Replace("Owner.csproj", "Linked.csproj", StringComparison.Ordinal);

            PortfolioInventory.Report report = PortfolioInventory.Create(root, [metadata, secondOwner]);

            await Assert.That(report.Projects.Select(project => project.Path).ToArray())
                .IsEquivalentTo(["Linked.csproj", "Owner.csproj"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            PortfolioInventory.Source cases = report.Sources.Single(source => source.Path == "Cases.cs");
            await Assert.That(cases.Owners.Count).IsEqualTo(2);
            await Assert.That(cases.Owners.All(owner => owner.Link == "Shared/Cases.cs")).IsTrue();
            PortfolioInventory.Declaration declaration = cases.Declarations.Single();
            await Assert.That(declaration.Type).IsEqualTo("Example.Fixture");
            await Assert.That(declaration.Partial).IsTrue();
            await Assert.That(declaration.Tests[0].Attributes.ToArray()).IsEquivalentTo(
                ["Test", "Arguments(false)", "Arguments(true)"], TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await Assert.That(declaration.Tests[1].Attributes).Contains("MethodDataSource(nameof(Providers))");
            await Assert.That(report.Sources.Single(source => source.Path == "Fixture.cs").Declarations.Single().BaseTypes)
                .Contains("DatabaseFixture");
            await Assert.That(report.Projects.All(project => project.Assets.Single().Path == "vector.json")).IsTrue();
            await Assert.That(cases.Sha256.Length).IsEqualTo(64);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PortfolioInventory_RejectsMalformedMetadataAndEscapingPaths()
    {
        await Assert.That(() => PortfolioInventory.Create(Path.GetTempPath(), ["{}"])).Throws<InvalidDataException>();
        const string escaping = """
            {"Properties":{"MSBuildProjectFullPath":"Owner.csproj"},"Items":{
              "Compile":[{"FullPath":"../outside.cs"}]}}
            """;
        await Assert.That(() => PortfolioInventory.Create(Path.GetTempPath(), [escaping])).Throws<InvalidDataException>();
    }

    [Test]
    public async Task PortfolioInventory_IndexesExternalAssetReferencesWithoutReadingThem()
    {
        const string metadata = """
            {"Properties":{"MSBuildProjectFullPath":"Owner.csproj"},"Items":{
              "None":[{"FullPath":"../external-package/fixture.dll"}]}}
            """;

        PortfolioInventory.Report report = PortfolioInventory.Create(Path.GetTempPath(), [metadata]);

        PortfolioInventory.Asset asset = report.Projects.Single().Assets.Single();
        await Assert.That(asset.External).IsTrue();
        await Assert.That(asset.Exists).IsNull();
        await Assert.That(asset.Sha256).IsNull();
    }

    [Test]
    public async Task PortfolioInventory_KeepsConditionalDeclarationsWithTheirActualCompiledOwner()
    {
        string root = Path.Combine(Path.GetTempPath(), $"portfolio-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "Cases.cs"), """
                public class Cases
                {
                #if REAL_PROVIDER
                    [Test] public void ProviderBoundary() { }
                #else
                    [Test] public void PureBoundary() { }
                #endif
                }
                """);
            const string provider = """
                {"Properties":{"MSBuildProjectFullPath":"Provider.csproj","DefineConstants":"REAL_PROVIDER"},
                 "Items":{"Compile":[{"FullPath":"Cases.cs"}]}}
                """;
            const string pure = """
                {"Properties":{"MSBuildProjectFullPath":"Pure.csproj","DefineConstants":""},
                 "Items":{"Compile":[{"FullPath":"Cases.cs"}]}}
                """;

            PortfolioInventory.Report report = PortfolioInventory.Create(root, [provider, pure]);

            PortfolioInventory.Declaration[] declarations = report.Sources.Single().Declarations.ToArray();
            await Assert.That(declarations.Single(declaration => declaration.Tests.Single().Name == "ProviderBoundary")
                .CompiledOwners.Single()).IsEqualTo("Provider.csproj");
            await Assert.That(declarations.Single(declaration => declaration.Tests.Single().Name == "PureBoundary")
                .CompiledOwners.Single()).IsEqualTo("Pure.csproj");

            File.WriteAllText(Path.Combine(root, "Cases.cs"), "public class Cases {");
            await Assert.That(() => PortfolioInventory.Create(root, [pure])).Throws<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PortfolioInventory_HashesExactInputBytesWhileParsingBomEncodedSource()
    {
        string root = Path.Combine(Path.GetTempPath(), $"portfolio-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "Cases.cs"),
                [0xef, 0xbb, 0xbf, .. System.Text.Encoding.UTF8.GetBytes("class Cases { [Test] public void Runs() { } }")]);
            const string metadata = """
                {"Properties":{"MSBuildProjectFullPath":"Owner.csproj"},
                 "Items":{"Compile":[{"FullPath":"Cases.cs"}]}}
                """;

            PortfolioInventory.Source source = PortfolioInventory.Create(root, [metadata]).Sources.Single();

            await Assert.That(source.Sha256).IsEqualTo("4D2A5DA6EA010EA2483C74F752F68E4C9D8AB65EBA7A230D5145B955ABB73555");
            await Assert.That(source.Declarations.Single().Tests.Single().Name).IsEqualTo("Runs");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("var value = Activator.CreateInstance(typeof(Service));", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var value = Activator.CreateInstance(\"Product.Assembly\", \"Product.Service\");", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var proxy = DispatchProxy.Create<IService, Proxy>();", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var type = typeof(Service).Assembly.GetType(\"Product.Service\"); var method = type!.GetMethod(\"Run\"); method!.Invoke(new Service(), null);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var name = \"Product.Service\"; var type = typeof(Service).Assembly.GetType(name); var method = type!.GetMethod(\"Run\"); method!.Invoke(new Service(), null);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var method = typeof(Service).GetMethod(\"Run\"); method!.Invoke(new Service(), null);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var field = typeof(Service).GetField(\"Value\"); field!.GetValue(new Service());", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var field = typeof(Service).GetField(\"Value\"); field!.SetValue(new Service(), 1);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var property = typeof(Service).GetProperty(\"Value\"); property!.GetValue(new Service());", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var property = typeof(Service).GetProperty(\"Value\"); property!.SetValue(new Service(), 1);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("MethodInfo? method; method = typeof(Service).GetMethod(\"Run\"); method!.Invoke(new Service(), null);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("typeof(Service).InvokeMember(\"Run\", BindingFlags.InvokeMethod, null, new Service(), null);", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("Delegate action = (Action)(() => { }); action.DynamicInvoke();", AssuranceAudit.ReflectiveBehaviorDispatch)]
    [Arguments("var type = typeof(Service).Assembly.GetType(\"Product.Service\");", AssuranceAudit.StringSelectedProductionType)]
    [Arguments("var source = File.ReadAllText(\"src/Product.cs\"); Assert.True(source.Contains(\"token\"));", AssuranceAudit.RawProductSourceAssurance)]
    [Arguments("var markup = File.ReadAllText(Path.Combine(\"src\", \"Page.razor\")); Regex.IsMatch(markup, \"token\");", AssuranceAudit.RawProductSourceAssurance)]
    [Arguments("var source = System.IO.File.ReadAllText(\"src/Product.cs\");", AssuranceAudit.RawProductSourceAssurance)]
    [Arguments("var source = File.ReadAllBytes(\"src/Product.cs\");", AssuranceAudit.RawProductSourceAssurance)]
    [Arguments("var source = File.ReadLines(\"src/Product.cs\");", AssuranceAudit.RawProductSourceAssurance)]
    [Arguments("using var source = File.OpenRead(\"docs/Product.md\");", AssuranceAudit.RawProductSourceAssurance)]
    [Arguments("using var source = File.OpenText(\"docs/Product.md\");", AssuranceAudit.RawProductSourceAssurance)]
    public async Task ProhibitedFixtures_ReportExpectedCategory(string statement, string expectedCategory)
    {
        string source = WrapInMethod(statement);

        IReadOnlyList<AssuranceDiagnostic> diagnostics = AssuranceAudit.AnalyzeSource(source, "Synthetic.cs");

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Category)).Contains(expectedCategory);
        await Assert.That(diagnostics.All(diagnostic => diagnostic.Path == "Synthetic.cs")).IsTrue();
        await Assert.That(diagnostics.All(diagnostic => diagnostic.Line > 0 && diagnostic.Column > 0)).IsTrue();
    }

    [Test]
    public async Task PermittedFixtures_ProduceNoDiagnostics()
    {
        string source = WrapInMethod("""
            var type = typeof(Service);
            var metadata = type.GetCustomAttributes(inherit: false);
            var schema = JsonDocument.Parse(File.ReadAllText("schemas/openapi.json"));
            var project = XDocument.Load("Product.csproj");
            var yaml = File.ReadAllText("config/settings.yaml");
            """);

        IReadOnlyList<AssuranceDiagnostic> diagnostics = AssuranceAudit.AnalyzeSource(source, "Allowed.cs");

        await Assert.That(diagnostics).IsEmpty();
    }

    [Test]
    public async Task ConstructorsAndTopLevelStatements_AreAudited()
    {
        const string constructorSource = """
            using System;
            public sealed class Fixture
            {
                public Fixture()
                {
                    var value = Activator.CreateInstance(typeof(Fixture));
                }
            }
            """;
        const string topLevelSource = """
            using System.IO;
            var source = System.IO.File.ReadAllText("src/Product.cs");
            """;

        IReadOnlyList<AssuranceDiagnostic> constructorDiagnostics =
            AssuranceAudit.AnalyzeSource(constructorSource, "Constructor.cs");
        IReadOnlyList<AssuranceDiagnostic> topLevelDiagnostics =
            AssuranceAudit.AnalyzeSource(topLevelSource, "TopLevel.cs");

        await Assert.That(constructorDiagnostics.Select(diagnostic => diagnostic.Category))
            .Contains(AssuranceAudit.ReflectiveBehaviorDispatch);
        await Assert.That(topLevelDiagnostics.Select(diagnostic => diagnostic.Category))
            .Contains(AssuranceAudit.RawProductSourceAssurance);
    }

    [Test]
    public async Task AliasedSystemFileRead_IsAudited()
    {
        const string source = """
            using ProductFile = System.IO.File;
            var source = ProductFile.ReadAllText("src/Product.cs");
            """;

        IReadOnlyList<AssuranceDiagnostic> diagnostics = AssuranceAudit.AnalyzeSource(source, "AliasedFile.cs");

        await Assert.That(diagnostics.Select(diagnostic => diagnostic.Category))
            .Contains(AssuranceAudit.RawProductSourceAssurance);
    }

    [Test]
    public async Task AliasedReflectiveConstruction_IsAudited()
    {
        const string activatorSource = """
            using Factory = System.Activator;
            public sealed class Service { }
            public sealed class Fixture
            {
                public void Test() => Factory.CreateInstance(typeof(Service));
            }
            """;
        const string proxySource = """
            using System.Reflection;
            using ProxyFactory = System.Reflection.DispatchProxy;
            public interface IService { }
            public sealed class Proxy : DispatchProxy
            {
                protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null;
            }
            public sealed class Fixture
            {
                public void Test() => ProxyFactory.Create<IService, Proxy>();
            }
            """;

        IReadOnlyList<AssuranceDiagnostic> activatorDiagnostics =
            AssuranceAudit.AnalyzeSource(activatorSource, "AliasedActivator.cs");
        IReadOnlyList<AssuranceDiagnostic> proxyDiagnostics =
            AssuranceAudit.AnalyzeSource(proxySource, "AliasedDispatchProxy.cs");

        await Assert.That(activatorDiagnostics.Select(diagnostic => diagnostic.Category))
            .Contains(AssuranceAudit.ReflectiveBehaviorDispatch);
        await Assert.That(proxyDiagnostics.Select(diagnostic => diagnostic.Category))
            .Contains(AssuranceAudit.ReflectiveBehaviorDispatch);
    }

    [Test]
    public async Task Diagnostics_AreSortedByOrdinalPathThenLocation()
    {
        IReadOnlyList<AssuranceDiagnostic> diagnostics = AssuranceAudit.AnalyzeSource(
            WrapInMethod("""
                var second = File.ReadAllText("B.cs");
                var first = Activator.CreateInstance(typeof(Service));
                """),
            "Z.cs");

        int[] lines = diagnostics.Select(diagnostic => diagnostic.Line).ToArray();
        await Assert.That(lines).IsEquivalentTo(
            lines.Order().ToArray(),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    [Test]
    public async Task ExplicitRootScan_IsDeterministicAcrossFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), $"assurance-audit-{Guid.CreateVersion7():N}");
        Directory.CreateDirectory(Path.Combine(root, "tests", "Governed"));
        try
        {
            File.WriteAllText(
                Path.Combine(root, "tests", "Governed", "B.cs"),
                WrapInMethod("var type = typeof(Service).Assembly.GetType(\"Product.B\");"));
            File.WriteAllText(
                Path.Combine(root, "tests", "Governed", "A.cs"),
                WrapInMethod("var type = typeof(Service).Assembly.GetType(\"Product.A\");"));

            IReadOnlyList<AssuranceDiagnostic> diagnostics = AssuranceAudit.AnalyzeFiles(
                root,
                ["tests/Governed"]);

            await Assert.That(diagnostics.Select(diagnostic => diagnostic.Path).ToArray())
                .IsEquivalentTo(
                    ["tests/Governed/A.cs", "tests/Governed/B.cs"],
                    TUnit.Assertions.Enums.CollectionOrdering.Matching);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string WrapInMethod(string statement) => $$"""
        using System;
        using System.IO;
        using System.Linq;
        using System.Reflection;
        using System.Text.Json;
        using System.Text.RegularExpressions;
        using System.Xml.Linq;

        public interface IService { }
        public sealed class Proxy : DispatchProxy { protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => null; }
        public sealed class Service { public int Value; public void Run() { } }
        public sealed class Fixture
        {
            public void Test()
            {
                {{statement}}
            }
        }
        """;
}
