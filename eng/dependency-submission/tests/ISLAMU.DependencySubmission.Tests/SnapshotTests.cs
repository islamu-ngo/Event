using System.Collections.Immutable;
using System.Text.Json;

namespace ISLAMU.DependencySubmission.Tests;

public sealed class SnapshotTests
{
    private const string Lock = """
        {"version":2,"dependencies":{
          "net10.0":{
            "Root":{"type":"Direct","resolved":"1.2.3","dependencies":{"CHILD":"[2.0.0, )","Local":"1.0.0"}},
            "Child":{"type":"CentralTransitive","resolved":"2.0.0"},
            "Local":{"type":"Project","dependencies":{"Child":"2.0.0"}}
          },
          "net10.0/linux-x64":{
            "Root":{"type":"Direct","resolved":"1.2.4"},
            "Child":{"type":"Transitive","resolved":"3.0.0"}
          }
        }}
        """;

    internal static SnapshotContext Context { get; } = new(
        new string('a', 40), "refs/heads/develop",
        new SnapshotJob(123, "dependency-submission", new Uri("https://github.com/islamu-ngo/Event/actions/runs/123")),
        new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));

    [Test]
    public async Task Build_FrameworksCentralPinsAndProjects_PreservesExternalClosure()
    {
        var manifests = SnapshotBuilder.BuildManifests([new LockInput("src/App/packages.lock.json", Lock)]);
        await Assert.That(manifests.Count).IsEqualTo(2);
        var manifest = manifests["src/App/packages.lock.json::net10.0"];
        await Assert.That(manifest.Resolved.Count).IsEqualTo(2);
        var root = manifest.Resolved["pkg:nuget/root@1.2.3"];
        await Assert.That(root.Relationship).IsEqualTo(DependencyRelationship.Direct);
        await Assert.That(root.Scope).IsEqualTo(DependencyScope.Runtime);
        await Assert.That(root.Dependencies.Single().OriginalString).IsEqualTo("pkg:nuget/child@2.0.0");
        await Assert.That(manifest.Resolved["pkg:nuget/child@2.0.0"].Relationship).IsEqualTo(DependencyRelationship.Indirect);
        await Assert.That(manifests["src/App/packages.lock.json::net10.0/linux-x64"].Resolved.ContainsKey("pkg:nuget/root@1.2.4")).IsTrue();
    }

    [Test]
    public async Task Serialize_Snapshot_UsesTypedImmutableWireContract()
    {
        var manifests = SnapshotBuilder.BuildManifests([new LockInput("eng/tool/tests/Tool.Tests/packages.lock.json", Lock)]);
        var snapshot = SnapshotBuilder.Build(Context, manifests);
        using var json = JsonDocument.Parse(SnapshotJson.Serialize(snapshot));
        await Assert.That(json.RootElement.GetProperty("version").GetInt32()).IsEqualTo(0);
        await Assert.That(json.RootElement.GetProperty("ref").GetString()).IsEqualTo("refs/heads/develop");
        await Assert.That(json.RootElement.GetProperty("scanned").GetDateTimeOffset()).IsEqualTo(Context.Scanned);
        var manifest = json.RootElement.GetProperty("manifests").GetProperty("eng/tool/tests/Tool.Tests/packages.lock.json::net10.0");
        await Assert.That(manifest.GetProperty("file").GetProperty("source_location").GetString()).IsEqualTo("eng/tool/tests/Tool.Tests/packages.lock.json");
        var root = manifest.GetProperty("resolved").GetProperty("pkg:nuget/root@1.2.3");
        await Assert.That(root.GetProperty("relationship").GetString()).IsEqualTo("direct");
        await Assert.That(root.GetProperty("scope").GetString()).IsEqualTo("development");
        await Assert.That(root.GetProperty("dependencies")[0].GetString()).IsEqualTo("pkg:nuget/child@2.0.0");
        await Assert.That(snapshot.Manifests).IsTypeOf<ImmutableSortedDictionary<string, SnapshotManifest>>();
    }

    [Test]
    [Arguments("""{"version":2,"dependencies":{"net10.0":{"Root":{"type":"Unknown","resolved":"1.0"}}}}""")]
    [Arguments("""{"version":2,"dependencies":{"net10.0":{"Root":{"type":"Direct"}}}}""")]
    [Arguments("""{"version":3,"dependencies":{}}""")]
    [Arguments("""{"version":2,"dependencies":{"net10.0":{"Root":{"type":"Direct","resolved":"1.0"},"ROOT":{"type":"Transitive","resolved":"2.0"}}}}""")]
    [Arguments("""{"version":2,"version":1,"dependencies":{}}""")]
    public async Task Build_InvalidLock_RejectsInsteadOfSubmittingPartialGraph(string input)
    {
        await Assert.That(() => SnapshotBuilder.BuildManifests([new LockInput("src/App/packages.lock.json", input)]))
            .Throws<SnapshotValidationException>();
    }

    [Test]
    public async Task Build_FrameworkClosure_DoesNotBorrowFromAnotherFramework()
    {
        const string input = """
            {"version":1,"dependencies":{
              "net10.0":{"Root":{"type":"Direct","resolved":"1.0","dependencies":{"Child":"1.0"}}},
              "net9.0":{"Child":{"type":"Transitive","resolved":"1.0"}}
            }}
            """;
        var manifests = SnapshotBuilder.BuildManifests([new LockInput("src/App/packages.lock.json", input)]);
        await Assert.That(manifests["src/App/packages.lock.json::net10.0"].Resolved.Count).IsEqualTo(1);
        await Assert.That(manifests["src/App/packages.lock.json::net10.0"].Resolved.Values.Single().Dependencies.Length).IsEqualTo(0);
        await Assert.That(manifests["src/App/packages.lock.json::net9.0"].Resolved.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Build_ExcludedDeclaration_DoesNotInventResolvedPackageVersions()
    {
        const string input = """{"version":2,"dependencies":{"net10.0":{"Root":{"type":"Direct","resolved":"1.0","dependencies":{"Missing":"1.0"}}}}}""";
        var manifest = SnapshotBuilder.BuildManifests([new LockInput("src/App/packages.lock.json", input)]).Values.Single();
        await Assert.That(manifest.Resolved.Count).IsEqualTo(1);
        await Assert.That(manifest.Resolved.Values.Single().Dependencies.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Build_ExcludedProjectPrivateEdges_DoesNotInventExternalPackages()
    {
        const string input = """
            {"version":2,"dependencies":{"net10.0":{
              "Root":{"type":"Direct","resolved":"1.0.0","dependencies":{"Local":"1.0"}},
              "Local":{"type":"Project","dependencies":{"PrivateBuildOnly":"1.0"}}
            }}}
            """;
        var manifest = SnapshotBuilder.BuildManifests([new LockInput(".agents/hooks/packages.lock.json", input)]).Values.Single();
        await Assert.That(manifest.Resolved.Count).IsEqualTo(1);
        await Assert.That(manifest.Resolved.Values.Single().Dependencies.Length).IsEqualTo(0);
        await Assert.That(manifest.Resolved.Values.Single().Scope).IsEqualTo(DependencyScope.Development);
    }

    [Test]
    public async Task Build_OrderAndCasing_NormalizesPackageIdentifiers()
    {
        const string input = """{"version":1,"dependencies":{"net10.0":{"Package.Name":{"type":"Direct","resolved":"1.0.0-beta+build"}}}}""";
        var manifests = SnapshotBuilder.BuildManifests([new LockInput("tests/App/packages.lock.json", input)]);
        var dependency = manifests.Values.Single().Resolved.Values.Single();
        await Assert.That(dependency.PackageUrl.OriginalString).IsEqualTo("pkg:nuget/package.name@1.0.0-beta%2Bbuild");
        await Assert.That(dependency.Scope).IsEqualTo(DependencyScope.Development);
        var first = new LockInput("tests/A/packages.lock.json", input);
        var second = new LockInput("tests/B/packages.lock.json", input);
        var before = SnapshotJson.Serialize(SnapshotBuilder.Build(Context,
            SnapshotBuilder.BuildManifests([first, second])));
        var reordered = SnapshotJson.Serialize(SnapshotBuilder.Build(Context,
            SnapshotBuilder.BuildManifests([second, first])));
        await Assert.That(reordered.SequenceEqual(before)).IsTrue();
    }
}
