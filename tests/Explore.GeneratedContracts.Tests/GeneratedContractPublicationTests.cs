namespace Explore.GeneratedContracts.Tests;

public sealed class GeneratedContractPublicationTests
{
    [Test]
    public async Task ReaderSeesCompletePreviousFileWhileProducerIsWriting()
    {
        using var files = new TemporaryContracts();
        string destination = files.Write("client.cs", "complete previous generation");
        var writing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task publication = GeneratedContractPublication.PublishAsync(destination, async candidate =>
        {
            await using var stream = new FileStream(candidate, FileMode.Create, FileAccess.Write, FileShare.Read);
            await stream.WriteAsync("incomplete"u8.ToArray());
            await stream.FlushAsync();
            writing.SetResult();
            await finish.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await stream.WriteAsync(" but now complete"u8.ToArray());
        });

        string observed;
        try
        {
            await writing.Task.WaitAsync(TimeSpan.FromSeconds(10));
            observed = await File.ReadAllTextAsync(destination);
        }
        finally
        {
            finish.TrySetResult();
            await publication.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await Assert.That(observed).IsEqualTo("complete previous generation");
        await Assert.That(await File.ReadAllTextAsync(destination)).IsEqualTo("incomplete but now complete");
        await Assert.That(Directory.GetFiles(files.Directory)).IsEquivalentTo([destination]);
    }

    [Test]
    public async Task FailedProducerPreservesPublishedFileAndRemovesCandidate()
    {
        using var files = new TemporaryContracts();
        string destination = files.Write("client.cs", "complete previous generation");
        await Assert.That(() => GeneratedContractPublication.PublishAsync(destination, async candidate =>
        {
            await File.WriteAllTextAsync(candidate, "incomplete");
            throw new InvalidDataException("Rejected generated contract");
        })).Throws<InvalidDataException>();

        await Assert.That(await File.ReadAllTextAsync(destination)).IsEqualTo("complete previous generation");
        await Assert.That(Directory.GetFiles(files.Directory)).IsEquivalentTo([destination]);
    }

    [Test]
    public async Task IdenticalPublicationPreservesDestinationTimestamp()
    {
        using var files = new TemporaryContracts();
        string destination = files.Write("schema.json", "{}");
        DateTime timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(destination, timestamp);
        await GeneratedContractPublication.PublishAsync(destination,
            candidate => File.WriteAllTextAsync(candidate, "{}"));
        await Assert.That(File.GetLastWriteTimeUtc(destination)).IsEqualTo(timestamp);
    }

    [Test]
    public async Task BuildCaptureDoesNotFollowLaterSourcePublication()
    {
        using var files = new TemporaryContracts();
        string schema = files.Write("schema.json", "{\"generation\":1}");
        string client = files.Write("client.cs", "public record GenerationOne;");
        string policy = files.Write("policy.txt", "GenerationOne");
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task writer = Task.Run(async () =>
        {
            await captured.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(schema, "{\"generation\":2}");
            await File.WriteAllTextAsync(client, "public record GenerationTwo;");
            await File.WriteAllTextAsync(policy, "GenerationTwo");
        });

        GeneratedContractFiles snapshot;
        try
        {
            snapshot = GeneratedContractCapture.Create(schema, client, policy,
                Path.Combine(files.Directory, "capture"));
        }
        finally
        {
            captured.TrySetResult();
            await writer.WaitAsync(TimeSpan.FromSeconds(10));
        }

        await Assert.That(await File.ReadAllTextAsync(snapshot.Schema)).IsEqualTo("{\"generation\":1}");
        await Assert.That(await File.ReadAllTextAsync(snapshot.Client)).IsEqualTo("public record GenerationOne;");
        await Assert.That(await File.ReadAllTextAsync(snapshot.MutablePolicy)).IsEqualTo("GenerationOne");
    }

    [Test]
    public async Task MissingCaptureInputFailsInsteadOfReturningLiveFallback()
    {
        using var files = new TemporaryContracts();
        string schema = files.Write("schema.json", "{}");
        string policy = files.Write("policy.txt", "");
        await Assert.That(() => GeneratedContractCapture.Create(schema,
            Path.Combine(files.Directory, "missing.cs"), policy, Path.Combine(files.Directory, "capture")))
            .Throws<FileNotFoundException>();
    }

    private sealed class TemporaryContracts : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("contract-publication-").FullName;

        public string Write(string name, string content)
        {
            string path = Path.Combine(Directory, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
