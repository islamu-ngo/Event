using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Storage;

public sealed class LocalFileStorageProvider : IFileStorageInventoryProvider
{
    private const int BufferSize = 128 * 1024;
    private const string QuarantineDirectoryName = ".quarantine";
    private static readonly char[] InvalidExtensionChars = Path.GetInvalidFileNameChars();

    private readonly LocalFileStorageOptions _options;
    private readonly ILogger<LocalFileStorageProvider> _logger;

    public LocalFileStorageProvider(
        IOptions<LocalFileStorageOptions> options,
        ILogger<LocalFileStorageProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string Provider => StorageProviders.Local;

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
            throw new ArgumentException("A tenant id is required for local storage writes.", nameof(input));
        }

        if (string.IsNullOrWhiteSpace(input.ContentType))
        {
            throw new ArgumentException("A content type is required for local storage writes.", nameof(input));
        }

        if (input.ExpectedSizeBytes is < 0 || input.MaxSizeBytes is < 0)
        {
            throw new ArgumentException("Storage write byte limits cannot be negative.", nameof(input));
        }

        var objectKey = ResolveWriteObjectKey(input.TenantId, input.Extension, input.ObjectKey);
        var finalPath = ResolveObjectPath(objectKey);
        var directory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("Unable to resolve storage object directory.");

        Directory.CreateDirectory(directory);
        var tempPath = $"{finalPath}.tmp-{Guid.CreateVersion7():N}";

        long bytesWritten = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            await using var target = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            using var sha256 = SHA256.Create();

            while (true)
            {
                var bytesRead = await input.Content.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                bytesWritten += bytesRead;
                if (input.MaxSizeBytes is { } maxSizeBytes && bytesWritten > maxSizeBytes)
                {
                    throw new InvalidOperationException("Local storage write exceeded the requested byte limit.");
                }

                await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                _ = sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            }

            _ = sha256.TransformFinalBlock([], 0, 0);
            if (input.ExpectedSizeBytes is { } expectedSizeBytes && bytesWritten != expectedSizeBytes)
            {
                throw new InvalidOperationException("Local storage write did not match the expected byte count.");
            }

            await target.FlushAsync(cancellationToken);

            File.Move(tempPath, finalPath, overwrite: false);

            return new FileStorageWriteResult(
                Provider,
                objectKey,
                bytesWritten,
                input.ContentType,
                Convert.ToHexString(sha256.Hash!).ToLowerInvariant());
        }
        catch
        {
            TryDeleteTempFile(tempPath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task<bool> ExistsAsync(
        FileStorageExistsInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolveObjectPath(input.ObjectKey);
        return Task.FromResult(File.Exists(path));
    }

    public Task<FileStorageReadResult> OpenReadAsync(
        FileStorageReadInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolveObjectPath(input.ObjectKey);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Stored file was not found.", input.ObjectKey);
        }

        var fileInfo = new FileInfo(path);
        var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return Task.FromResult(new FileStorageReadResult(
            stream,
            string.IsNullOrWhiteSpace(input.ContentType) ? "application/octet-stream" : input.ContentType,
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc));
    }

    public Task<FileStorageDeleteResult> DeleteAsync(
        FileStorageDeleteInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        var path = ResolveObjectPath(input.ObjectKey);
        var deleted = false;

        if (File.Exists(path))
        {
            File.Delete(path);
            deleted = true;
        }

        return Task.FromResult(new FileStorageDeleteResult(Provider, input.ObjectKey, deleted));
    }

    public async Task<FileStorageProviderStatus> TestAsync(
        CancellationToken cancellationToken,
        bool testWritePermissions = false)
    {
        try
        {
            var root = ResolveRootPath();
            Directory.CreateDirectory(root);

            var probePath = Path.Combine(root, $".health-{Guid.CreateVersion7():N}");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);

            return new FileStorageProviderStatus(
                Provider,
                IsAvailable: true,
                SupportsServerSideStreaming: true,
                SupportsBrowserDirectUpload: false,
                Message: "Local storage root is writable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            _logger.LogWarning(
                "Local storage health check failed. FailureType={FailureType}",
                CategorizeHealthFailure(ex));
            return new FileStorageProviderStatus(
                Provider,
                IsAvailable: false,
                SupportsServerSideStreaming: true,
                SupportsBrowserDirectUpload: false,
                FailureCode: "local_storage_unavailable",
                Message: "Local storage root is not writable.");
        }
    }

    public async IAsyncEnumerable<FileStorageInventoryObject> ListObjectsAsync(
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            yield break;
        }

        var root = ResolveRootPath();
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var yielded = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipInventoryPath(root, path))
            {
                continue;
            }

            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
            {
                continue;
            }

            var objectKey = Path.GetRelativePath(root, path)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            yield return new FileStorageInventoryObject(
                Provider,
                objectKey,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc);

            yielded++;
            if (yielded >= limit)
            {
                yield break;
            }

            await Task.Yield();
        }
    }

    public Task<FileStorageQuarantineResult> QuarantineAsync(
        FileStorageQuarantineInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(input.Reason))
        {
            throw new ArgumentException("A quarantine reason is required.", nameof(input));
        }

        var sourcePath = ResolveObjectPath(input.ObjectKey);
        if (!File.Exists(sourcePath))
        {
            return Task.FromResult(new FileStorageQuarantineResult(Provider, input.ObjectKey, Quarantined: false));
        }

        var root = ResolveRootPath();
        var quarantinePath = Path.Combine(
            root,
            QuarantineDirectoryName,
            input.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            $"{HashObjectKey(input.ObjectKey)}-{Path.GetFileName(sourcePath)}");

        Directory.CreateDirectory(Path.GetDirectoryName(quarantinePath)
            ?? throw new InvalidOperationException("Unable to resolve quarantine directory."));

        File.Move(sourcePath, quarantinePath, overwrite: false);

        return Task.FromResult(new FileStorageQuarantineResult(Provider, input.ObjectKey, Quarantined: true));
    }

    internal string ResolveObjectPath(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("A storage object key is required.", nameof(objectKey));
        }

        var normalizedKey = objectKey.Replace('\\', '/');
        if (normalizedKey.StartsWith('/') ||
            normalizedKey.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage object key must be a relative path.", nameof(objectKey));
        }

        var segments = normalizedKey.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Storage object key contains an unsafe path segment.", nameof(objectKey));
        }

        if (segments.Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new ArgumentException("Storage object key contains invalid filename characters.", nameof(objectKey));
        }

        var root = ResolveRootPath();
        var fullPath = Path.GetFullPath(Path.Combine([root, .. segments]));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
        {
            throw new ArgumentException("Storage object key escapes the configured storage root.", nameof(objectKey));
        }

        return fullPath;
    }

    private static bool ShouldSkipInventoryPath(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');

        var segments = relativePath.Split('/');
        if (segments.Any(segment => string.Equals(segment, QuarantineDirectoryName, StringComparison.Ordinal)))
        {
            return true;
        }

        var fileName = Path.GetFileName(path);
        return fileName.StartsWith(".health-", StringComparison.Ordinal) ||
               fileName.Contains(".tmp-", StringComparison.Ordinal);
    }

    private string ResolveRootPath()
    {
        var root = Path.GetFullPath(_options.RootPath);
        if (_options.CreateRootIfMissing)
        {
            Directory.CreateDirectory(root);
        }

        return root;
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

        if (trimmed.Length > 32 || trimmed.IndexOfAny(InvalidExtensionChars) >= 0 || trimmed.Contains('/', StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return trimmed.ToLowerInvariant();
    }

    private static string HashObjectKey(string objectKey)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(objectKey);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    private static string CategorizeHealthFailure(Exception exception) =>
        exception switch
        {
            UnauthorizedAccessException => "access_denied",
            IOException => "storage_io",
            TimeoutException => "timeout",
            OperationCanceledException => "operation_canceled",
            _ => "unavailable"
        };

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch
        {
            // Best effort cleanup only; the original write exception should remain authoritative.
        }
    }
}
