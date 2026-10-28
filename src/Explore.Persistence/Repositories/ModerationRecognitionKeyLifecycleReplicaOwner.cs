using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Singleton, lifetime-exclusive standalone SQLite owner. This is a real deployment restriction:
/// every recognition resolver must retain this lock on the selected physical database, so a
/// second process cannot become an unacknowledged replica. A host UUID alone proves nothing.
/// Unix uses the exclusive .NET flock on the opened file; Windows uses a handle-specific byte
/// lock far beyond SQLite's lock-byte page and maximum database size. Hard links reach the same
/// lock, unlike a pathname-derived sidecar. Neither lock certifies remote topology or prevents
/// every native writer or Unix rename. All processes must stop before restore/file replacement;
/// reopening obtains a new runtime identity and independently requalifies retained floors.
/// Network databases, in-memory databases and remote moderation need their own native owners.
/// </summary>
public sealed class ModerationRecognitionKeyLifecycleReplicaOwner : IDisposable
{
    private readonly object gate = new();
    private readonly Guid runtimeId = Guid.CreateVersion7();
    private FileStream? lifetimeLock;
    private string? databasePath;
    private bool disposed;
    private const long OwnershipByte = long.MaxValue - 1;

    public Guid Acquire(DbContext context)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!context.Database.IsSqlite() || context.Database.GetDbConnection() is not SqliteConnection connection
                || connection.State != System.Data.ConnectionState.Open)
                throw Unavailable();
            var settings = new SqliteConnectionStringBuilder(connection.ConnectionString);
            if (settings.Mode == SqliteOpenMode.Memory || string.IsNullOrWhiteSpace(connection.DataSource)
                || connection.DataSource == ":memory:" || connection.DataSource.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                throw Unavailable();
            var path = Path.GetFullPath(connection.DataSource);
            var info = new FileInfo(path);
            if (!info.Exists || info.LinkTarget is not null) throw Unavailable();
            if (lifetimeLock is not null)
            {
                if (!StringComparer.Ordinal.Equals(databasePath, path)) throw Unavailable();
                return runtimeId;
            }
            FileStream? candidate = null;
            try
            {
                candidate = OpenOwnershipHandle(path);
                var excludesAnotherHandle = false;
                try
                {
                    using var competing = OpenOwnershipHandle(path);
                }
                catch (IOException exception) when ((exception.HResult & 0xffff) == (OperatingSystem.IsWindows() ? 33 : 32))
                {
                    excludesAnotherHandle = true;
                }
                // .NET can disable implicit Unix file locking. An independently observed
                // sharing/lock violation is required; opening a handle is not sufficient.
                if (!excludesAnotherHandle) throw Unavailable();
                lifetimeLock = candidate;
                candidate = null;
                databasePath = path;
                return runtimeId;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                throw Unavailable();
            }
            finally
            {
                candidate?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            try
            {
                if (OperatingSystem.IsWindows()) lifetimeLock?.Unlock(OwnershipByte, 1);
            }
            finally
            {
                lifetimeLock?.Dispose();
                lifetimeLock = null;
            }
        }
    }
    private static FileStream OpenOwnershipHandle(string path)
    {
        if (!OperatingSystem.IsWindows())
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
        var handle = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1, FileOptions.None);
        var locked = false;
        try
        {
            // Windows binds this beyond-EOF lock to this handle, without allocating bytes.
            handle.Lock(OwnershipByte, 1);
            locked = true;
            return handle;
        }
        finally
        {
            if (!locked) handle.Dispose();
        }
    }
    private static InvalidOperationException Unavailable() => new("moderation_recognition_topology_unavailable");
}
