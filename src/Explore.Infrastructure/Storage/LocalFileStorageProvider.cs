using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
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
    private readonly bool _boundTarget;

    public LocalFileStorageProvider(
        IOptions<LocalFileStorageOptions> options,
        ILogger<LocalFileStorageProvider> logger,
        bool boundTarget = false)
    {
        _options = options.Value;
        _logger = logger;
        _boundTarget = boundTarget;
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

        CreatePrivateDirectory(directory);
        EnsurePathCustody(finalPath);
        var tempPath = $"{finalPath}.tmp-{Guid.CreateVersion7():N}";

        long bytesWritten = 0;
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = BufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using var target = new FileStream(tempPath, options);

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

        RejectVersion(input.ProviderVersionId);
        EnsureRootAccessible();
        var path = ResolveObjectPath(input.ObjectKey);
        return Task.FromResult(ObjectExists(path));
    }

    public Task<FileStorageReadResult> OpenReadAsync(
        FileStorageReadInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        RejectVersion(input.ProviderVersionId);
        EnsureRootAccessible();
        var path = ResolveObjectPath(input.ObjectKey);
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

        RejectVersion(input.ProviderVersionId);
        EnsureRootAccessible();
        var path = ResolveObjectPath(input.ObjectKey);
        var deleted = false;

        if (ObjectExists(path))
        {
            File.Delete(path);
            deleted = true;
        }

        return Task.FromResult(new FileStorageDeleteResult(Provider, input.ObjectKey, deleted));
    }

    public Task<FileStorageProviderStatus> TestAsync(
        CancellationToken cancellationToken,
        bool testWritePermissions = false)
    {
        if (testWritePermissions)
            throw new UnauthorizedAccessException("Write probes require original storage action custody.");
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var root = ResolveRootPath();
            EnsurePathCustody(root);
            EnsureRootAccessible();
            return Task.FromResult(new FileStorageProviderStatus(
                Provider,
                IsAvailable: true,
                SupportsServerSideStreaming: true,
                SupportsBrowserDirectUpload: false,
                Message: "Local storage root is readable; write permissions were not probed."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SystemException)
        {
            _logger.LogWarning(
                "Local storage health check failed. FailureType={FailureType}",
                CategorizeHealthFailure(ex));
            return Task.FromResult(new FileStorageProviderStatus(
                Provider,
                IsAvailable: false,
                SupportsServerSideStreaming: true,
                SupportsBrowserDirectUpload: false,
                FailureCode: "local_storage_unavailable",
                Message: "Local storage root is unavailable."));
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
        var enumeration = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };
        foreach (var path in Directory.EnumerateFiles(root, "*", enumeration))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldSkipInventoryPath(root, path))
            {
                continue;
            }
            EnsurePathCustody(path);

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

    public async Task<FileStorageQuarantineResult> QuarantineAsync(
        FileStorageQuarantineInput input,
        ILocalStorageQuarantineOwner custody,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(custody);
        ArgumentNullException.ThrowIfNull(clock);
        cancellationToken.ThrowIfCancellationRequested();

        if (input.QuarantineId == Guid.Empty || input.ExpectedSize < 0
            || input.ExpectedLastModifiedUtc.Kind != DateTimeKind.Utc
            || input.ProviderBindingId == Guid.Empty || input.ClaimStamp == Guid.Empty
            || input.LeaseExpiresAtUtc == default || input.LeaseExpiresAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("An original quarantine claim, expiry and UTC file snapshot are required.", nameof(input));
        }

        var sourcePath = ResolveObjectPath(input.ObjectKey);
        var canonicalPath = LocalStorageQuarantine.CanonicalizePhysicalPath(sourcePath);
        if (!string.Equals(canonicalPath, input.CanonicalPhysicalPath, StringComparison.Ordinal)
            || !string.Equals(LocalStorageQuarantine.HashPhysicalPath(canonicalPath), input.PhysicalPathHash, StringComparison.Ordinal)
            || input.LeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
            return new(Provider, input.ObjectKey, FileStorageQuarantineOutcome.Blocked);
        var root = ResolveRootPath();
        var quarantinePath = Path.Combine(
            root,
            QuarantineDirectoryName,
            "reconciliation",
            input.QuarantineId.ToString("N"));

        EnsurePathCustody(quarantinePath);
        var source = new FileInfo(sourcePath);
        var destination = new FileInfo(quarantinePath);
        if (!await custody.IsLocalQuarantineClaimCurrentAsync(input, cancellationToken))
            return new(Provider, input.ObjectKey, FileStorageQuarantineOutcome.Blocked);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.LeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
            return new(Provider, input.ObjectKey, FileStorageQuarantineOutcome.Blocked);
        // Refresh the original observation after the awaited inventory/custody read.
        EnsurePathCustody(sourcePath);
        EnsurePathCustody(quarantinePath);
        source.Refresh();
        destination.Refresh();
        bool Matches(FileInfo file) => file.Length == input.ExpectedSize
            && file.LastWriteTimeUtc == input.ExpectedLastModifiedUtc;
        FileStorageQuarantineOutcome outcome;
        if (destination.Exists)
            outcome = !source.Exists && Matches(destination)
                ? FileStorageQuarantineOutcome.AlreadyQuarantined
                : FileStorageQuarantineOutcome.Blocked;
        else if (!source.Exists)
            outcome = FileStorageQuarantineOutcome.AlreadyAbsent;
        else if (!Matches(source))
            outcome = FileStorageQuarantineOutcome.Blocked;
        else
        {
            CreatePrivateDirectory(Path.GetDirectoryName(quarantinePath)
                ?? throw new InvalidOperationException("Unable to resolve quarantine directory."));
            EnsurePathCustody(sourcePath);
            EnsurePathCustody(quarantinePath);
            source.Refresh();
            destination.Refresh();
            if (!source.Exists || destination.Exists || !Matches(source))
                return new(Provider, input.ObjectKey, FileStorageQuarantineOutcome.Blocked);
            cancellationToken.ThrowIfCancellationRequested();
            if (input.LeaseExpiresAtUtc <= clock.GetUtcNow().UtcDateTime)
                return new(Provider, input.ObjectKey, FileStorageQuarantineOutcome.Blocked);
            File.Move(sourcePath, quarantinePath, overwrite: false);
            outcome = FileStorageQuarantineOutcome.Moved;
        }

        return new(Provider, input.ObjectKey, outcome);
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

        EnsurePathCustody(fullPath);
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
        EnsurePathCustody(root);
        return root;
    }

    /// <summary>
    /// Rejects existing linked roots, ancestors and targets. The deployment must keep
    /// these paths under service custody; concurrent hostile filesystem mutation is
    /// unsupported, since checking a path and opening it are not an atomic operation.
    /// </summary>
    private static void EnsurePathCustody(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Local storage requires unlinked service-owned paths.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        EnsurePathCustody(path);
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private void EnsureRootAccessible()
    {
        // Unlike Exists(), enumeration reports missing roots and permission/I/O failures.
        using var entries = Directory.EnumerateFileSystemEntries(ResolveRootPath()).GetEnumerator();
        _ = entries.MoveNext();
    }

    private static bool ObjectExists(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Directory) != 0)
                throw new IOException("Storage object is not a file.");
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void RejectVersion(string? versionId)
    {
        if (versionId is not null)
            throw new InvalidOperationException("Local storage does not support provider versions.");
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
