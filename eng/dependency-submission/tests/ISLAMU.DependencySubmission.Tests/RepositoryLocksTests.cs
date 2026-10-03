namespace ISLAMU.DependencySubmission.Tests;

public sealed class RepositoryLocksTests
{
    [Test]
    public async Task Read_TrackedGitPaths_IncludesNestedLocksAndExcludesGeneratedFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dependency locks {Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await RepositoryLocks.GitAsync(root, ["init", "--quiet"]);
            Directory.CreateDirectory(Path.Combine(root, "src", "App with spaces"));
            Directory.CreateDirectory(Path.Combine(root, "generated"));
            const string json = """{"version":2,"dependencies":{"net10.0":{"Root":{"type":"Direct","resolved":"1.0.0"}}}}""";
            await File.WriteAllTextAsync(Path.Combine(root, "packages.lock.json"), json);
            await File.WriteAllTextAsync(Path.Combine(root, "src", "App with spaces", "packages.lock.json"), json);
            await File.WriteAllTextAsync(Path.Combine(root, "generated", "packages.lock.json"), json);
            await RepositoryLocks.GitAsync(root, ["add", "--", "packages.lock.json", "src/App with spaces/packages.lock.json"]);
            var inputs = await RepositoryLocks.ReadAsync(root);
            await Assert.That(inputs.Length).IsEqualTo(2);
            await Assert.That(inputs[0].Path).IsEqualTo("packages.lock.json");
            await Assert.That(inputs[1].Path).IsEqualTo("src/App with spaces/packages.lock.json");
            await Assert.That(SnapshotBuilder.BuildManifests(inputs).Count).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Arguments("../packages.lock.json")]
    [Arguments("/packages.lock.json")]
    [Arguments("src\\packages.lock.json")]
    [Arguments("src/../packages.lock.json")]
    public async Task Build_UnsafeRepositoryPath_RejectsTraversal(string path)
    {
        await Assert.That(() => SnapshotBuilder.BuildManifests([new LockInput(path, "{}")])).Throws<SnapshotValidationException>();
    }
}
