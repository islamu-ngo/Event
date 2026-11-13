using System.Reflection;
using System.Xml.Linq;
using Event.Testing;
using Mono.Cecil;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Architecture.Tests;

public sealed class TestExecutionGovernanceArchitectureTests
{
    private const int ExpectedTUnitTestProjectCount = 16;

    [Test]
    public async Task AllTUnitTestProjects_LinkBoundedDiscoveryHookAndSuiteKind()
    {
        var tunitProjects = DiscoverTUnitProjects();

        await Assert.That(tunitProjects.Count).IsEqualTo(ExpectedTUnitTestProjectCount)
            .Because("every TUnit test assembly under tests/ (13) and eng/ (3) must be governed");

        var missingHook = new List<string>();
        var missingProfile = new List<string>();
        var invalidSuiteKind = new List<string>();

        foreach (var projectPath in tunitProjects)
        {
            var relativePath = Path.GetRelativePath(ContextSystemHelpers.RepoRoot, projectPath).Replace('\\', '/');
            var doc = XDocument.Load(projectPath);

            var compileIncludes = doc.Descendants("Compile")
                .Select(e => e.Attribute("Include")?.Value?.Replace('\\', '/'))
                .Where(v => v is not null)
                .ToList();

            if (!compileIncludes.Any(v => v!.EndsWith("tests/Shared/Execution/TestExecutionProfile.cs", StringComparison.Ordinal) ||
                                          v.EndsWith("Shared/Execution/TestExecutionProfile.cs", StringComparison.Ordinal)))
            {
                missingProfile.Add(relativePath);
            }

            if (!compileIncludes.Any(v => v!.EndsWith("tests/Shared/Execution/BoundedTestDiscoveryHook.cs", StringComparison.Ordinal) ||
                                          v.EndsWith("Shared/Execution/BoundedTestDiscoveryHook.cs", StringComparison.Ordinal)))
            {
                missingHook.Add(relativePath);
            }

            var suiteKindRaw = doc.Descendants("AssemblyAttribute")
                .Where(e => string.Equals(e.Attribute("Include")?.Value, "Event.Testing.TestSuiteKindAttribute", StringComparison.Ordinal))
                .Select(e => e.Element("_Parameter1")?.Value)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(suiteKindRaw) ||
                !Enum.TryParse<TestSuiteKind>(suiteKindRaw, ignoreCase: false, out var parsedKind))
            {
                invalidSuiteKind.Add($"{relativePath} ({suiteKindRaw ?? "<missing>"})");
                continue;
            }

            if (relativePath.Contains("Persistence.IntegrationTests", StringComparison.Ordinal) ||
                relativePath.Contains("API.IntegrationTests", StringComparison.Ordinal) ||
                relativePath.Contains("Infrastructure.Tests", StringComparison.Ordinal) ||
                relativePath.Contains("Blazor.IntegrationTests", StringComparison.Ordinal))
            {
                await Assert.That(parsedKind).IsEqualTo(TestSuiteKind.ContainerBacked)
                    .Because($"{relativePath} uses Docker/Testcontainers and must not run with UnitPure concurrency");
            }

            if (relativePath.Contains("Distributed.IntegrationTests", StringComparison.Ordinal))
            {
                await Assert.That(parsedKind).IsEqualTo(TestSuiteKind.DistributedAppHost)
                    .Because($"{relativePath} launches Aspire AppHost and Playwright");
            }
        }

        await Assert.That(missingProfile).IsEmpty()
            .Because("every TUnit test project must link tests/Shared/Execution/TestExecutionProfile.cs");
        await Assert.That(missingHook).IsEmpty()
            .Because("every TUnit test project must link tests/Shared/Execution/BoundedTestDiscoveryHook.cs");
        await Assert.That(invalidSuiteKind).IsEmpty()
            .Because("every TUnit test project must declare a valid Event.Testing.TestSuiteKindAttribute");
    }

    [Test]
    public async Task LegacyStaticParallelLimiters_AreRetiredAndAbsentFromCompiledAssemblies()
    {
        var apiLimiterPath = ContextSystemHelpers.RepoPath("tests", "Event.API.IntegrationTests", "ApiTestParallelLimit.cs");
        var clientLimiterPath = ContextSystemHelpers.RepoPath("tests", "Explore.Blazor.Client.Tests", "ClientTestParallelLimit.cs");

        await Assert.That(File.Exists(apiLimiterPath)).IsFalse()
            .Because("ApiTestParallelLimit.cs silently clamps CLI --maximum-parallel-tests overrides");
        await Assert.That(File.Exists(clientLimiterPath)).IsFalse()
            .Because("ClientTestParallelLimit.cs silently clamps CLI --maximum-parallel-tests overrides");

        var currentAssembly = typeof(TestExecutionGovernanceArchitectureTests).Assembly;
        var currentLimiterAttrs = currentAssembly.GetCustomAttributes()
            .Where(attr => attr.GetType().FullName?.StartsWith("TUnit.Core.ParallelLimiterAttribute", StringComparison.Ordinal) == true)
            .ToList();

        await Assert.That(currentLimiterAttrs).IsEmpty();
        await Assert.That(currentAssembly.GetCustomAttribute<TestSuiteKindAttribute>()).IsNotNull();

        var builtTestDlls = DiscoverTUnitProjects()
            .Select(projectPath =>
            {
                var dir = Path.GetDirectoryName(projectPath)!;
                var name = Path.GetFileNameWithoutExtension(projectPath);
                var output = new DirectoryInfo(AppContext.BaseDirectory);
                return Path.Combine(dir, "bin", output.Parent!.Name, output.Name, $"{name}.dll");
            })
            .Where(File.Exists)
            .ToList();

        var staticLimitViolations = new List<string>();
        foreach (var dllPath in builtTestDlls)
        {
            using var definition = AssemblyDefinition.ReadAssembly(dllPath);
            bool hasParallelLimiterAttr = definition.CustomAttributes.Any(attr =>
                attr.AttributeType.FullName.StartsWith("TUnit.Core.ParallelLimiterAttribute", StringComparison.Ordinal));
            bool implementsIParallelLimit = definition.MainModule.Types.Any(type =>
                type.Interfaces.Any(iface => iface.InterfaceType.FullName == typeof(IParallelLimit).FullName));

            if (hasParallelLimiterAttr || implementsIParallelLimit)
            {
                staticLimitViolations.Add(Path.GetFileName(dllPath));
            }
        }

        await Assert.That(staticLimitViolations).IsEmpty()
            .Because("static ParallelLimiter attributes/implementations override or fight CLI --maximum-parallel-tests precedence");
    }

    private static IReadOnlyList<string> DiscoverTUnitProjects()
    {
        var roots = new[]
        {
            ContextSystemHelpers.RepoPath("tests"),
            ContextSystemHelpers.RepoPath("eng")
        };

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(csproj =>
            {
                var doc = XDocument.Load(csproj);
                return doc.Descendants("PackageReference")
                    .Any(pkg => string.Equals(pkg.Attribute("Include")?.Value, "TUnit", StringComparison.Ordinal));
            })
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();
    }
}
