using ISLAMU.Event.Setup.Core;

namespace ISLAMU.Event.Setup.Artifacts;

public enum ProtectedArtifactStatus
{
    Written,
    InvalidRequest,
    TargetExists,
    UnsafeTarget,
    TargetChanged,
    PermissionDenied,
    IoFailure,
    UnsupportedPlatform
}

public sealed class ProtectedArtifactPreparation : IDisposable
{
    private readonly Func<CancellationToken, Task<ProtectedArtifactStatus>> _commit;
    private readonly Action _cleanup;
    private int _completed;

    internal ProtectedArtifactPreparation(
        Func<CancellationToken, Task<ProtectedArtifactStatus>> commit,
        Action cleanup)
    {
        _commit = commit;
        _cleanup = cleanup;
    }

    internal static ProtectedArtifactPreparation Rejected(ProtectedArtifactStatus status) =>
        new(_ => Task.FromResult(status), static () => { });

    public async Task<ProtectedArtifactStatus> CommitAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0)
            return ProtectedArtifactStatus.InvalidRequest;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _commit(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _cleanup();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _completed, 1) == 0)
            _cleanup();
    }
}

public sealed class ProtectedArtifactWriter
{
    public const int MaximumBytes = 4 * 1024 * 1024;

    public bool IsAvailable => OperatingSystem.IsLinux();

    public static void WritePublic(Stream destination, SetupArtifactKind kind, ReadOnlyMemory<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (bytes.Length > MaximumBytes || !SetupArtifactPolicy.TryProjectPublic(kind, bytes, out var projection))
            throw new IOException("protected-output-required");
        destination.Write(projection.Span);
        destination.Flush();
    }

    public async Task<ProtectedArtifactPreparation> PrepareAsync(
        SetupArtifactKind kind,
        string targetPath,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
            return WindowsProtectedArtifactWriter.Unavailable();
        if (!OperatingSystem.IsLinux())
            return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.UnsupportedPlatform);
        if (targetPath == "-")
            return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.UnsafeTarget);
        if (string.IsNullOrWhiteSpace(targetPath) || bytes.IsEmpty || bytes.Length > MaximumBytes)
            return ProtectedArtifactPreparation.Rejected(ProtectedArtifactStatus.InvalidRequest);

        // Every file, including an unknown kind, uses the same owner-only boundary.
        return await UnixProtectedArtifactWriter.PrepareAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
    }
}
