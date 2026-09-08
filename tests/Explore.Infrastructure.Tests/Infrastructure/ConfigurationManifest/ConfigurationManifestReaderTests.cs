namespace Explore.Infrastructure.Tests.Infrastructure.ConfigurationManifest;

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Features.ConfigurationManifest.Ingestion;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Infrastructure.ConfigurationManifest;

public sealed class ConfigurationManifestReaderTests
{
    internal const string ValidManifest =
        """
        {
          "$schema": "https://schemas.islamu.org/event/configuration-manifest/v1alpha2/schema.json",
          "apiVersion": "configuration.islamu.org/v1alpha2",
          "kind": "ConfigurationManifest",
          "metadata": { "name": "primary-deployment" },
          "spec": {
            "instance": {
              "settings": {},
              "documents": {},
              "legalDocuments": {}
            },
            "tenants": [
              {
                "metadata": { "name": "default" },
                "spec": {
                  "displayName": "Primary Community",
                  "settings": {},
                  "documents": {},
                  "legalDocuments": {}
                }
              }
            ]
          }
        }
        """;

    private readonly ConfigurationManifestReader _reader = new();

    [Test]
    public async Task ReadAsync_OffMode_PerformsNoPathValidationOrFileAccess()
    {
        ConfigurationManifestReadResult? result = await _reader.ReadAsync(
            new ConfigurationManifestReadOptions(
                ConfigurationManifestMode.Off,
                "relative-and-missing.json"),
            CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ReadAsync_ExplicitMissingPath_FailsClosed()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.CreateVersion7():N}.json");

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadAsync(
                new ConfigurationManifestReadOptions(
                    ConfigurationManifestMode.ValidateOnly,
                    path),
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.FileMissing);
    }

    [Test]
    public async Task ReadStreamAsync_ValidManifest_ReturnsExactDigestAndLength()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ValidManifest);
        await using var stream = new MemoryStream(bytes, writable: false);

        ConfigurationManifestReadResult result = await _reader.ReadStreamAsync(
            stream,
            ConfigurationManifestMode.ValidateOnly,
            CancellationToken.None);

        await Assert.That(result.ByteLength).IsEqualTo(bytes.Length);
        await Assert.That(result.Sha256Digest).IsEqualTo(
            Convert.ToHexStringLower(SHA256.HashData(bytes)));
        await Assert.That(result.Manifest.Spec.Tenants[0].Metadata.Name).IsEqualTo("default");
    }

    [Test]
    public async Task ReadStreamAsync_EmptyStream_FailsClosed()
    {
        await using var stream = new MemoryStream();

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.Empty);
    }

    [Test]
    public async Task ReadStreamAsync_NonSeekableOversizeStream_ReadsOnlyMaximumPlusOne()
    {
        await using var stream = new CountingGeneratedStream(
            ConfigurationManifestReader.MaximumBytes + 4096);

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.Bootstrap,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.TooLarge);
        await Assert.That(stream.BytesRead).IsEqualTo(
            ConfigurationManifestReader.MaximumBytes + 1L);
    }

    [Test]
    [MethodDataSource(nameof(DuplicateProperties))]
    public async Task ReadStreamAsync_DuplicateAndEscapedDuplicateProperties_FailClosed(string duplicate)
    {
        string json = ValidManifest.Replace(
            "\"kind\": \"ConfigurationManifest\",",
            $"\"kind\": \"ConfigurationManifest\", {duplicate}",
            StringComparison.Ordinal);
        await using var stream = Utf8(json);

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.DuplicateProperty);
    }

    public static IEnumerable<string> DuplicateProperties()
    {
        yield return "\"kind\": \"Other\",";
        yield return "\"\\u006bind\": \"Other\",";
    }

    [Test]
    [MethodDataSource(nameof(UnknownMembers))]
    public async Task ReadStreamAsync_UnknownOrCaseMismatchedMember_FailsContract(string member)
    {
        string json = ValidManifest.Replace(
            "\"metadata\": { \"name\": \"primary-deployment\" },",
            $"\"metadata\": {{ \"name\": \"primary-deployment\", {member}: true }},",
            StringComparison.Ordinal);
        await using var stream = Utf8(json);

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestFailureCodes.ContractInvalid);
    }

    public static IEnumerable<string> UnknownMembers()
    {
        yield return "\"unexpected\"";
        yield return "\"Name\"";
    }

    [Test]
    [MethodDataSource(nameof(NullRequiredStructures))]
    public async Task ReadStreamAsync_NullRequiredStructure_FailsContractWithoutNullReference(
        string json)
    {
        await using var stream = Utf8(json);

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestFailureCodes.ContractInvalid);
        await Assert.That(exception.InnerException is NullReferenceException).IsFalse();
    }

    public static IEnumerable<string> NullRequiredStructures()
    {
        yield return ValidManifest.Replace(
            "\"metadata\": { \"name\": \"primary-deployment\" }",
            "\"metadata\": null",
            StringComparison.Ordinal);

        int specStart = ValidManifest.IndexOf("  \"spec\":", StringComparison.Ordinal);
        yield return ValidManifest[..specStart] + "  \"spec\": null\n}";

        yield return ValidManifest.Replace(
            "\"instance\": {\n      \"settings\": {},\n      \"documents\": {},\n      \"legalDocuments\": {}\n    }",
            "\"instance\": null",
            StringComparison.Ordinal);

        int tenantsStart = ValidManifest.IndexOf("    \"tenants\":", StringComparison.Ordinal);
        yield return ValidManifest[..tenantsStart] + "    \"tenants\": null\n  }\n}";

        yield return ValidManifest.Replace(
            "\"tenants\": [\n      {",
            "\"tenants\": [\n      null,\n      {",
            StringComparison.Ordinal);

        yield return ValidManifest.Replace(
            "\"metadata\": { \"name\": \"default\" }",
            "\"metadata\": null",
            StringComparison.Ordinal);

        int tenantSpecStart = ValidManifest.IndexOf(
            "        \"spec\":",
            StringComparison.Ordinal);
        yield return ValidManifest[..tenantSpecStart]
            + "        \"spec\": null\n      }\n    ]\n  }\n}";
    }

    [Test]
    [MethodDataSource(nameof(InvalidJsonInputs))]
    public async Task ReadStreamAsync_MalformedUtf8OrTrailingRoot_FailsJson(string invalid)
    {
        byte[] bytes = invalid == "utf8"
            ? [0x7B, 0x22, 0x78, 0x22, 0x3A, 0x22, 0x80, 0x22, 0x7D]
            : Encoding.UTF8.GetBytes($"{ValidManifest}{{}}");
        await using var stream = new MemoryStream(bytes, writable: false);

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestIngestionFailureCodes.JsonInvalid);
    }

    public static IEnumerable<string> InvalidJsonInputs()
    {
        yield return "utf8";
        yield return "trailing";
    }

    [Test]
    public async Task ReadStreamAsync_SensitiveSetting_DoesNotLeakValue()
    {
        const string sensitiveValue = "do-not-leak";
        string json = ValidManifest.Replace(
            "\"settings\": {}",
            $"\"settings\": {{ \"email.smtp_password\": \"{sensitiveValue}\" }}",
            StringComparison.Ordinal);
        await using var stream = Utf8(json);

        ConfigurationManifestIngestionException exception = await CaptureIngestionAsync(
            () => _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                CancellationToken.None));

        await Assert.That(exception.FailureCode).IsEqualTo(
            ConfigurationManifestFailureCodes.SensitiveKeyForbidden);
        await Assert.That(exception.Message.Contains(sensitiveValue, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task ReadStreamAsync_CancellationDuringRead_PropagatesWithoutTranslation()
    {
        using var cancellation = new CancellationTokenSource();
        await using var stream = new CancellingStream(cancellation);
        OperationCanceledException? exception = null;

        try
        {
            _ = await _reader.ReadStreamAsync(
                stream,
                ConfigurationManifestMode.ValidateOnly,
                cancellation.Token);
        }
        catch (OperationCanceledException caught)
        {
            exception = caught;
        }

        await Assert.That(exception).IsNotNull();
    }

    private static MemoryStream Utf8(string value) =>
        new(Encoding.UTF8.GetBytes(value), writable: false);

    private static async Task<ConfigurationManifestIngestionException> CaptureIngestionAsync(
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

    private sealed class CountingGeneratedStream(long length) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long remaining = length - BytesRead;
            if (remaining <= 0)
                return ValueTask.FromResult(0);

            int count = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..count].Fill((byte)' ');
            BytesRead += count;
            return ValueTask.FromResult(count);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CancellingStream(CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromCanceled<int>(cancellationToken);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
