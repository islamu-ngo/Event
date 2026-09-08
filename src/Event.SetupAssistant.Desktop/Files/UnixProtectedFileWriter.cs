namespace ISLAMU.Event.SetupAssistant.Desktop.Files;

using System.Runtime.Versioning;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
[SupportedOSPlatform("freebsd")]
public sealed class UnixProtectedFileWriter : IProtectedFileWriter
{
    private const int MaximumBytes = 4 * 1024 * 1024;
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public bool IsAvailable =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD();

    public async Task<ProtectedWritePreparation> PrepareAsync(
        ProtectedWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsAvailable)
            return ProtectedWritePreparation.FromResult(ProtectedWriteResult.Unsupported());
        if (request.Bytes.IsEmpty || request.Bytes.Length > MaximumBytes)
            return Rejected(ProtectedWriteFailureCode.InvalidRequest);

        string target;
        string directory;
        try
        {
            target = Path.GetFullPath(request.TargetPath);
            directory = Path.GetDirectoryName(target) ?? string.Empty;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Rejected(ProtectedWriteFailureCode.InvalidRequest);
        }

        if (directory.Length == 0 || !Directory.Exists(directory)
            || DirectoryChainContainsLink(directory))
            return Rejected(ProtectedWriteFailureCode.UnsafeTarget);

        TargetSnapshot initial = Capture(target);
        if (initial.Unsafe)
            return Rejected(ProtectedWriteFailureCode.UnsafeTarget);
        if (initial.Exists && !request.AllowOverwrite)
            return Rejected(ProtectedWriteFailureCode.TargetExists);

        string temporary = Path.Combine(
            directory,
            $".event-setup-{Guid.CreateVersion7():N}.tmp");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough | FileOptions.Asynchronous,
                UnixCreateMode = OwnerOnly
            };
            await using (var stream = new FileStream(temporary, options))
            {
                File.SetUnixFileMode(stream.SafeFileHandle, OwnerOnly);
                await stream.WriteAsync(request.Bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
                if (File.GetUnixFileMode(stream.SafeFileHandle) != OwnerOnly)
                    throw new UnauthorizedAccessException();
            }
        }
        catch (OperationCanceledException)
        {
            DeleteIfPresent(temporary);
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            DeleteIfPresent(temporary);
            return Rejected(ProtectedWriteFailureCode.PermissionDenied);
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException)
        {
            DeleteIfPresent(temporary);
            return Rejected(ProtectedWriteFailureCode.IoFailure);
        }

        return new ProtectedWritePreparation(
            token => CommitAsync(
                target,
                temporary,
                initial,
                request.AllowOverwrite,
                request.Bytes.Length,
                token),
            () => DeleteIfPresent(temporary));
    }

    private static async Task<ProtectedWriteResult> CommitAsync(
        string target,
        string temporary,
        TargetSnapshot initial,
        bool allowOverwrite,
        int expectedLength,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TargetSnapshot current = Capture(target);
        if (!current.SameAs(initial))
            return ProtectedWriteResult.Rejected(ProtectedWriteFailureCode.TargetChanged);

        try
        {
            File.Move(
                temporary,
                target,
                overwrite: allowOverwrite && initial.Exists);
            TargetSnapshot installed = Capture(target);
            if (!installed.Exists || installed.Unsafe || installed.Length != expectedLength
                || File.GetUnixFileMode(target) != OwnerOnly)
            {
                DeleteIfPresent(target);
                return ProtectedWriteResult.Rejected(
                    ProtectedWriteFailureCode.PermissionVerificationFailed);
            }

            await Task.CompletedTask;
            return ProtectedWriteResult.Written();
        }
        catch (UnauthorizedAccessException)
        {
            return ProtectedWriteResult.Rejected(ProtectedWriteFailureCode.PermissionDenied);
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException)
        {
            return ProtectedWriteResult.Rejected(ProtectedWriteFailureCode.IoFailure);
        }
    }

    private static TargetSnapshot Capture(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return TargetSnapshot.Missing;

            var info = new FileInfo(path);
            info.Refresh();
            FileAttributes attributes = File.GetAttributes(path);
            bool unsafeTarget = (attributes
                    & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device))
                != 0
                || info.LinkTarget is not null;
            return new TargetSnapshot(
                Exists: true,
                Unsafe: unsafeTarget,
                Length: unsafeTarget ? -1 : info.Length,
                CreationTimeUtc: info.CreationTimeUtc,
                LastWriteTimeUtc: info.LastWriteTimeUtc,
                Attributes: attributes);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or NotSupportedException)
        {
            return TargetSnapshot.UnsafeExisting;
        }
    }

    private static bool DirectoryChainContainsLink(string directory)
    {
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            current.Refresh();
            if (current.LinkTarget is not null
                || (current.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
            current = current.Parent;
        }

        return false;
    }

    private static ProtectedWritePreparation Rejected(ProtectedWriteFailureCode code) =>
        ProtectedWritePreparation.FromResult(ProtectedWriteResult.Rejected(code));

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private readonly record struct TargetSnapshot(
        bool Exists,
        bool Unsafe,
        long Length,
        DateTime CreationTimeUtc,
        DateTime LastWriteTimeUtc,
        FileAttributes Attributes)
    {
        internal static TargetSnapshot Missing => new(
            false,
            false,
            0,
            default,
            default,
            default);

        internal static TargetSnapshot UnsafeExisting => new(
            true,
            true,
            -1,
            default,
            default,
            default);

        internal bool SameAs(TargetSnapshot other) =>
            Exists == other.Exists
            && Unsafe == other.Unsafe
            && Length == other.Length
            && CreationTimeUtc == other.CreationTimeUtc
            && LastWriteTimeUtc == other.LastWriteTimeUtc
            && Attributes == other.Attributes;
    }
}
