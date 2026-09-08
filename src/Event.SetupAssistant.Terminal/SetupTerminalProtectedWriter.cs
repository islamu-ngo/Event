namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.Buffers;
using System.Security.Cryptography;

internal sealed class SetupTerminalProtectedWriter
{
    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly string _baseDirectory;
    private readonly Func<CancellationToken, Task> _beforeCommit;

    internal SetupTerminalProtectedWriter(string baseDirectory)
        : this(baseDirectory, _ => Task.CompletedTask)
    {
    }

    internal SetupTerminalProtectedWriter(
        string baseDirectory,
        Func<CancellationToken, Task> beforeCommit)
    {
        ArgumentException.ThrowIfNullOrEmpty(baseDirectory);
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _beforeCommit = beforeCommit ?? throw new ArgumentNullException(nameof(beforeCommit));
    }

    internal bool IsAvailable => (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        && IsDirectoryProtected();

    internal async Task<bool> WriteCreateNewAsync(
        string fileName,
        ReadOnlyMemory<byte> bytes,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD()))
            return false;
        if (!IsAvailable
            || !SetupTerminalFileName.IsSafe(fileName)
            || bytes.Length == 0
            || bytes.Length > maximumBytes)
            return false;

        string path = Path.Combine(_baseDirectory, fileName);
        string temporaryPath = Path.Combine(
            _baseDirectory,
            $".islamu-setup-{Guid.CreateVersion7():N}.tmp");
        bool temporaryCreated = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                UnixCreateMode = OwnerOnly
            };
            await using var stream = new FileStream(temporaryPath, options);
            temporaryCreated = true;
            File.SetUnixFileMode(stream.SafeFileHandle, OwnerOnly);
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
            if (File.GetUnixFileMode(stream.SafeFileHandle) != OwnerOnly)
                throw new IOException("protected-output-mode-invalid");
            await _beforeCommit(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var staged = new FileInfo(temporaryPath);
            staged.Refresh();
            if (staged.LinkTarget is not null
                || staged.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || staged.Length != bytes.Length
                || File.GetUnixFileMode(temporaryPath) != OwnerOnly)
                throw new IOException("protected-output-staging-identity-invalid");
            if (!await StagedContentMatchesAsync(
                    temporaryPath,
                    bytes,
                    cancellationToken).ConfigureAwait(false))
                throw new IOException("protected-output-staging-content-invalid");
            File.Move(temporaryPath, path, overwrite: false);
            temporaryCreated = false;
            if (stream.Length != bytes.Length
                || File.GetUnixFileMode(stream.SafeFileHandle) != OwnerOnly)
                throw new IOException("protected-output-commit-identity-invalid");
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporaryCreated)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private bool IsDirectoryProtected()
    {
        if (OperatingSystem.IsWindows())
            return false;
        try
        {
            UnixFileMode mode = File.GetUnixFileMode(_baseDirectory);
            return (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return false;
        }
    }

    private static async Task<bool> StagedContentMatchesAsync(
        string path,
        ReadOnlyMemory<byte> expected,
        CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(Math.Min(expected.Length, 4096));
        try
        {
            await using var verification = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                rented.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (verification.Length != expected.Length)
                return false;
            int offset = 0;
            while (offset < expected.Length)
            {
                int requested = Math.Min(rented.Length, expected.Length - offset);
                int read = await verification.ReadAsync(
                    rented.AsMemory(0, requested),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0
                    || !rented.AsSpan(0, read).SequenceEqual(expected.Span.Slice(offset, read)))
                    return false;
                offset += read;
            }
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
        }
    }
}

internal static class SetupTerminalFileName
{
    internal const int MaximumLength = 64;

    internal static bool IsPartialSafe(string value) => value.Length <= MaximumLength && value.All(IsAllowed);

    internal static bool IsSafe(string value) => value.Length is > 0 and <= MaximumLength
        && value.All(IsAllowed)
        && value is not "-" and not "." and not "..";

    private static bool IsAllowed(char value) => value is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z'
        or >= '0' and <= '9'
        or '.' or '_' or '-';
}
