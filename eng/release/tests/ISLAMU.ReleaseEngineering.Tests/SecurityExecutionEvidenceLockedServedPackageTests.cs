using System.Text.Json;
using static ISLAMU.ReleaseEngineering.SecurityExecutionEvidence;

namespace ISLAMU.ReleaseEngineering.Tests;

public sealed class SecurityExecutionEvidenceLockedServedPackageTests
{
    [Test]
    [Arguments("owner", true)]
    [Arguments("referenced", true)]
    [Arguments("unrelated", false)]
    [Arguments("wrong-hash", false)]
    [Arguments("wrong-version", false)]
    [Arguments("wrong-project", false)]
    [Arguments("untracked-lock", false)]
    public async Task ServedPackagesRequireMatchingLocksWithinTheSourceReferenceClosure(string scenario, bool accepted)
    {
        string root = Directory.CreateTempSubdirectory("locked-served-package-").FullName;
        try
        {
            string repository = Directory.CreateDirectory(Path.Combine(root, "repository")).FullName;
            string packages = Directory.CreateDirectory(Path.Combine(root, "packages")).FullName;
            string package = Directory.CreateDirectory(Path.Combine(packages, "fixture.assets", "1.0.0")).FullName;
            string content = Directory.CreateDirectory(Path.Combine(package, "staticwebassets")).FullName;
            const string host = "src/Host/Host.csproj";
            const string referenced = "src/Referenced/Referenced.csproj";
            const string unrelated = "src/Unrelated/Unrelated.csproj";
            string owner = scenario == "owner" ? host : scenario == "unrelated" ? unrelated : referenced;
            var tracked = new Dictionary<string, string>(StringComparer.Ordinal);
            var indexes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                [host] = [referenced],
                [referenced] = [],
                [unrelated] = []
            };
            foreach (string project in graph.Keys)
            {
                string directory = Directory.CreateDirectory(Path.Combine(repository, Path.GetDirectoryName(project)!)).FullName;
                string lockPath = Path.Combine(directory, "packages.lock.json");
                string assetsPath = Path.Combine(directory, "project.assets.json");
                bool owns = project == owner;
                var dependencies = new Dictionary<string, object>();
                if (owns)
                    dependencies["Fixture.Assets"] = new
                    {
                        resolved = scenario == "wrong-version" ? "2.0.0" : "1.0.0",
                        contentHash = "fixture-content-hash"
                    };
                await File.WriteAllTextAsync(lockPath, JsonSerializer.Serialize(new
                {
                    dependencies = new Dictionary<string, object> { ["net10.0"] = dependencies }
                }));
                var libraries = new Dictionary<string, object>();
                if (owns)
                    libraries["Fixture.Assets/1.0.0"] = new
                    {
                        type = "package",
                        path = "fixture.assets/1.0.0",
                        sha512 = scenario == "wrong-hash" ? "different-content-hash" : "fixture-content-hash"
                    };
                await File.WriteAllTextAsync(assetsPath, JsonSerializer.Serialize(new
                {
                    project = new
                    {
                        restore = new
                        {
                            projectPath = scenario == "wrong-project" && owns
                                ? Path.Combine(repository, unrelated) : Path.Combine(repository, project)
                        }
                    },
                    libraries,
                    packageFolders = new Dictionary<string, object> { [packages + "/"] = new { } }
                }));
                if (scenario != "untracked-lock" || !owns)
                    tracked[Path.GetRelativePath(repository, lockPath)] = "tracked-lock";
                indexes[project] = [assetsPath];
            }
            string runtime = Path.Combine(repository, "runtime.json");
            await File.WriteAllTextAsync(runtime, JsonSerializer.Serialize(new { ContentRoots = new[] { content } }));
            if (accepted)
            {
                var actual = ReadServedLockedPackageRoots(repository, host, runtime, indexes, graph, tracked, out string digest);
                await Assert.That(actual.SequenceEqual(new[] { package })).IsTrue();
                await Assert.That(digest.Length).IsEqualTo(64);
            }
            else
                await Assert.That(() => ReadServedLockedPackageRoots(repository, host, runtime, indexes, graph, tracked, out _))
                    .Throws<InvalidDataException>();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
