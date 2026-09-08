namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Infrastructure.ConfigurationManifest;

public sealed class ConfigurationManifestFileReaderTests
{
    private readonly ConfigurationManifestReader _reader = new();

    [Test]
    public async Task ReadAsync_AbsentConventionFile_IsNoOp()
    {
        await Assert.That(File.Exists(ConfigurationManifestReader.ConventionPath)).IsFalse();

        ConfigurationManifestReadResult? result = await _reader.ReadAsync(
            new ConfigurationManifestReadOptions(
                ConfigurationManifestMode.ValidateOnly,
                ConfiguredPath: null),
            CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_ExplicitRegularFile_ReturnsValidatedManifest()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.File("configuration-manifest.json");
        await File.WriteAllTextAsync(
            path,
            ConfigurationManifestReaderTests.ValidManifest);

        ConfigurationManifestReadResult? result = await _reader.ReadAsync(
            new ConfigurationManifestReadOptions(
                ConfigurationManifestMode.Bootstrap,
                path),
            CancellationToken.None);

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Mode).IsEqualTo(ConfigurationManifestMode.Bootstrap);
        await Assert.That(result.Manifest.Spec.Tenants[0].Metadata.Name).IsEqualTo("default");
    }

    [Test]
    public async Task ReadAsync_ExplicitDirectory_RejectsNonRegularSource()
    {
        using var directory = new TemporaryDirectory();

        ConfigurationManifestIngestionException exception = await CaptureAsync(
            () => _reader.ReadAsync(
                new ConfigurationManifestReadOptions(
                    ConfigurationManifestMode.ValidateOnly,
                    directory.Path),
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.FileNotRegular);
    }

    [Test]
    public async Task ReadAsync_ExplicitSymbolicLink_RejectsBeforeReading()
    {
        using var directory = new TemporaryDirectory();
        string target = directory.File("target.json");
        string link = directory.File("link.json");
        await File.WriteAllTextAsync(
            target,
            ConfigurationManifestReaderTests.ValidManifest);
        _ = File.CreateSymbolicLink(link, target);

        ConfigurationManifestIngestionException exception = await CaptureAsync(
            () => _reader.ReadAsync(
                new ConfigurationManifestReadOptions(
                    ConfigurationManifestMode.ValidateOnly,
                    link),
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.FileSymlinkNotAllowed);
    }

    [Test]
    public async Task ReadAsync_UnreadableExplicitFile_FailsWithSafeCode()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.File("unreadable.json");
        await File.WriteAllTextAsync(
            path,
            ConfigurationManifestReaderTests.ValidManifest);
        File.SetUnixFileMode(path, UnixFileMode.None);

        ConfigurationManifestIngestionException exception;
        try
        {
            exception = await CaptureAsync(
                () => _reader.ReadAsync(
                    new ConfigurationManifestReadOptions(
                        ConfigurationManifestMode.ValidateOnly,
                        path),
                    CancellationToken.None));
        }
        finally
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.FileUnreadable);
        await Assert.That(exception.Message.Contains(path, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReadAsync_UnsupportedApiVersion_FailsContractBeforeReturning()
    {
        using var directory = new TemporaryDirectory();
        string path = directory.File("unsupported.json");
        string json = ConfigurationManifestReaderTests.ValidManifest.Replace(
            "configuration.islamu.org/v1alpha2",
            "configuration.islamu.org/v2",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(path, json);

        ConfigurationManifestIngestionException exception = await CaptureAsync(
            () => _reader.ReadAsync(
                new ConfigurationManifestReadOptions(
                    ConfigurationManifestMode.ValidateOnly,
                    path),
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestFailureCodes.ContractInvalid);
    }

    private static async Task<ConfigurationManifestIngestionException> CaptureAsync(
        Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ConfigurationManifestIngestionException exception)
        {
            return exception;
        }

        throw new InvalidOperationException("The expected ingestion exception was not thrown.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"configuration-manifest-{Guid.CreateVersion7():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
