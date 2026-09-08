using System.Globalization;
using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Domain;

namespace Explore.Infrastructure.Storage;

public sealed class S3FileStorageProvider : IFileStorageProvider
{
    private readonly IS3ConfigResolver _configResolver;
    private readonly IS3ClientFactory _clientFactory;
    private readonly IS3PreflightVerifier _preflightVerifier;

    public S3FileStorageProvider(
        IS3ConfigResolver configResolver,
        IS3ClientFactory clientFactory,
        IS3PreflightVerifier preflightVerifier)
    {
        _configResolver = configResolver;
        _clientFactory = clientFactory;
        _preflightVerifier = preflightVerifier;
    }

    public string Provider => StorageProviders.S3Compatible;

    public async Task<bool> ExistsAsync(
        FileStorageExistsInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.ObjectKey))
        {
            throw new ArgumentException("A storage object key is required.", nameof(input));
        }

        var config = await ResolveRequiredConfigAsync(cancellationToken);
        var client = _clientFactory.CreateDataClient(config);

        try
        {
            await client.GetObjectMetadataAsync(
                new GetObjectMetadataRequest
                {
                    BucketName = config.BucketName,
                    Key = input.ObjectKey
                },
                cancellationToken);

            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<FileStorageWriteResult> WriteAsync(
        FileStorageWriteInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Content);

        if (!input.Content.CanRead)
        {
            throw new ArgumentException("Storage write content stream must be readable.", nameof(input));
        }

        if (input.TenantId == Guid.Empty)
        {
            throw new ArgumentException("A tenant id is required for S3 storage writes.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.ContentType))
        {
            throw new ArgumentException("A content type is required for S3 storage writes.", nameof(input));
        }

        if (input.ExpectedSizeBytes is < 0 || input.MaxSizeBytes is < 0)
        {
            throw new ArgumentException("Storage write byte limits cannot be negative.", nameof(input));
        }

        var objectKey = ResolveWriteObjectKey(input.TenantId, input.Extension, input.ObjectKey);
        var config = await ResolveRequiredConfigAsync(cancellationToken);
        using var hashingStream = new BoundedHashingReadStream(input.Content, input.ExpectedSizeBytes, input.MaxSizeBytes);

        var request = new PutObjectRequest
        {
            BucketName = config.BucketName,
            Key = objectKey,
            ContentType = input.ContentType,
            InputStream = hashingStream,
            AutoCloseStream = false
        };
        if (input.ExpectedSizeBytes is { } expectedSizeBytes)
        {
            request.Headers.ContentLength = expectedSizeBytes;
        }

        var client = _clientFactory.CreateDataClient(config);
        await client.PutObjectAsync(request, cancellationToken);

        return new FileStorageWriteResult(
            Provider,
            objectKey,
            hashingStream.BytesRead,
            input.ContentType,
            hashingStream.GetChecksum());
    }

    public async Task<FileStorageReadResult> OpenReadAsync(
        FileStorageReadInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.ObjectKey))
        {
            throw new ArgumentException("A storage object key is required.", nameof(input));
        }

        var config = await ResolveRequiredConfigAsync(cancellationToken);
        var client = _clientFactory.CreateDataClient(config);

        try
        {
            var response = await client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = config.BucketName,
                    Key = input.ObjectKey
                },
                cancellationToken);

            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? input.ContentType ?? "application/octet-stream"
                : response.Headers.ContentType;

            return new FileStorageReadResult(
                response.ResponseStream,
                contentType,
                response.Headers.ContentLength,
                response.LastModified == default ? null : response.LastModified);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Stored S3 object was not found.", input.ObjectKey, ex);
        }
    }

    public async Task<FileStorageDeleteResult> DeleteAsync(
        FileStorageDeleteInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.ObjectKey))
        {
            throw new ArgumentException("A storage object key is required.", nameof(input));
        }

        var config = await ResolveRequiredConfigAsync(cancellationToken);
        var client = _clientFactory.CreateDataClient(config);
        await client.DeleteObjectAsync(
            new DeleteObjectRequest
            {
                BucketName = config.BucketName,
                Key = input.ObjectKey
            },
            cancellationToken);

        return new FileStorageDeleteResult(Provider, input.ObjectKey, Deleted: true);
    }

    public async Task<FileStorageProviderStatus> TestAsync(
        CancellationToken cancellationToken,
        bool testWritePermissions = false)
    {
        var preflight = await _preflightVerifier.VerifyAsync(
            new S3PreflightRequest { TestWritePermissions = testWritePermissions },
            cancellationToken);
        var failure = preflight.Steps.FirstOrDefault(step =>
            step.Status is S3PreflightStepStatus.Failed or S3PreflightStepStatus.Warning);

        return new FileStorageProviderStatus(
            Provider,
            IsAvailable: preflight.IsSuccess,
            SupportsServerSideStreaming: true,
            SupportsBrowserDirectUpload: true,
            FailureCode: failure?.ErrorCode,
            Message: failure?.Message ?? (preflight.CanWrite
                ? "S3-compatible storage bucket is reachable and writable."
                : "S3-compatible storage bucket is reachable."),
            Preflight: preflight);
    }

    private async Task<Explore.Application.Models.S3Configuration> ResolveRequiredConfigAsync(CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveAsync(cancellationToken);
        return config ?? throw new InvalidOperationException("S3-compatible storage is not configured.");
    }

    private static string BuildObjectKey(Guid tenantId, string? extension)
    {
        var safeExtension = NormalizeExtension(extension);
        var utcNow = DateTime.UtcNow;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"tenants/{tenantId:N}/{utcNow:yyyy/MM/dd}/{Guid.CreateVersion7():N}{safeExtension}");
    }

    private static string ResolveWriteObjectKey(Guid tenantId, string? extension, string? objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return BuildObjectKey(tenantId, extension);
        }

        var normalized = objectKey.Trim();
        if (!normalized.StartsWith($"tenants/{tenantId:N}/", StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage object key must belong to the requested tenant.", nameof(objectKey));
        }

        return normalized;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        var trimmed = extension.Trim();
        if (!trimmed.StartsWith('.'))
        {
            trimmed = "." + trimmed;
        }

        if (trimmed.Length > 32 ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains('/', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return trimmed.ToLowerInvariant();
    }

    private sealed class BoundedHashingReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long? _expectedSizeBytes;
        private readonly long? _maxSizeBytes;
        private readonly IncrementalHash _sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _finalized;
        private string? _checksum;

        public BoundedHashingReadStream(Stream inner, long? expectedSizeBytes, long? maxSizeBytes)
        {
            _inner = inner;
            _expectedSizeBytes = expectedSizeBytes;
            _maxSizeBytes = maxSizeBytes;
        }

        public long BytesRead { get; private set; }

        public override bool CanRead => _inner.CanRead;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public string GetChecksum()
        {
            FinalizeHashIfNeeded();
            return _checksum!;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            TrackRead(buffer.AsSpan(offset, read), read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken);
            TrackRead(buffer.Span[..read], read);
            return read;
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sha256.Dispose();
            }

            base.Dispose(disposing);
        }

        private void TrackRead(ReadOnlySpan<byte> buffer, int bytesRead)
        {
            if (bytesRead == 0)
            {
                FinalizeHashIfNeeded();
                return;
            }

            BytesRead += bytesRead;
            if (_maxSizeBytes is { } maxSizeBytes && BytesRead > maxSizeBytes)
            {
                throw new InvalidOperationException("S3 storage write exceeded the requested byte limit.");
            }

            _sha256.AppendData(buffer);
        }

        private void FinalizeHashIfNeeded()
        {
            if (_finalized)
            {
                return;
            }

            if (_expectedSizeBytes is { } expectedSizeBytes && BytesRead != expectedSizeBytes)
            {
                throw new InvalidOperationException("S3 storage write did not match the expected byte count.");
            }

            _checksum = Convert.ToHexString(_sha256.GetHashAndReset()).ToLowerInvariant();
            _finalized = true;
        }
    }
}
