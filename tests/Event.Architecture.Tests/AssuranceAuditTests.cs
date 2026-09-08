using Explore.AssuranceAudit;

namespace Event.Architecture.Tests;

public sealed class AssuranceAuditTests
{
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
