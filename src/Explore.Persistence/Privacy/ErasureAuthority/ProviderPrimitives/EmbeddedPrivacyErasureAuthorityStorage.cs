using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;

namespace Explore.Persistence.Privacy.ErasureAuthority;

public sealed class EmbeddedPrivacyErasureAuthorityStorage(
    EmbeddedPrivacyErasureAuthorityOptions options) : IDisposable
{
    private static readonly HashSet<string> NetworkFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "9p", "ceph", "cifs", "fuse.sshfs", "glusterfs", "nfs", "nfs4", "smb3",
    };

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly HashSet<string> _preExistingFiles = new(StringComparer.Ordinal);
    private bool _initialized;

    public EmbeddedPrivacyErasureAuthorityOptions Options { get; } = options;

    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            string directory = Path.GetDirectoryName(Options.Path)
                ?? throw new InvalidOperationException("The embedded authority directory is invalid.");
            EnsureLocalFileSystem(directory);
            RejectExistingPathSymbolicLinks(directory);
            RecordAndValidateExistingFile(Options.Path);
            RecordAndValidateExistingFile(Options.Path + "-wal");
            RecordAndValidateExistingFile(Options.Path + "-shm");
            bool fileExisted = _preExistingFiles.Contains(Options.Path);

            Directory.CreateDirectory(directory);
            HardenDirectory(directory);

            await using var connection = new SqliteConnection(Options.BuildConnectionString());
            await connection.OpenAsync(cancellationToken);
            await ExecuteScalarRequiredAsync(connection, "PRAGMA journal_mode=WAL;", "wal", cancellationToken);
            await ExecuteNonQueryAsync(connection, "PRAGMA synchronous=FULL;", cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                $"PRAGMA busy_timeout={Options.BusyTimeoutSeconds * 1000};",
                cancellationToken);
            await ExecuteNonQueryAsync(connection, "PRAGMA foreign_keys=ON;", cancellationToken);

            if (!fileExisted)
            {
                HardenFile(Options.Path);
            }
            HardenCompanionFiles();
            await VerifyIntegrityCoreAsync(connection, cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    public async Task VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        HardenCompanionFiles();
        ValidateAuthorityFilePermissions();
        await using var connection = new SqliteConnection(Options.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);
        await VerifyIntegrityCoreAsync(connection, cancellationToken);
    }

    public void HardenCompanionFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        HardenNewFileIfPresent(Options.Path);
        HardenNewFileIfPresent(Options.Path + "-wal");
        HardenNewFileIfPresent(Options.Path + "-shm");
    }

    public void Dispose() => _initializationLock.Dispose();

    private static async Task VerifyIntegrityCoreAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await ExecuteScalarRequiredAsync(connection, "PRAGMA quick_check;", "ok", cancellationToken);
        await ExecuteScalarRequiredAsync(connection, "PRAGMA journal_mode;", "wal", cancellationToken);
    }

    private static async Task ExecuteScalarRequiredAsync(
        SqliteConnection connection,
        string commandText,
        string expected,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        object? result = await command.ExecuteScalarAsync(cancellationToken);
        if (!string.Equals(
                Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture),
                expected,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The embedded privacy-erasure authority failed its storage integrity policy.");
        }
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureLocalFileSystem(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            if (directory.StartsWith("\\\\", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("EmbeddedSqlite requires a local filesystem.");
            }
            return;
        }

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string existing = directory;
        while (!Directory.Exists(existing))
        {
            existing = Path.GetDirectoryName(existing)
                ?? throw new InvalidOperationException("EmbeddedSqlite requires a local filesystem.");
        }

        string? fileSystem = FindLinuxFileSystem(existing);
        if (fileSystem is null || NetworkFileSystems.Contains(fileSystem))
        {
            throw new InvalidOperationException("EmbeddedSqlite requires a recognized local filesystem.");
        }
    }

    private static string? FindLinuxFileSystem(string path)
    {
        string normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        string? bestMount = null;
        string? bestFileSystem = null;
        foreach (string line in File.ReadLines("/proc/self/mountinfo"))
        {
            string[] halves = line.Split(" - ", 2, StringSplitOptions.None);
            if (halves.Length != 2)
            {
                continue;
            }

            string[] left = halves[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] right = halves[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (left.Length < 5 || right.Length < 1)
            {
                continue;
            }

            string mount = UnescapeMountPath(left[4]).TrimEnd(Path.DirectorySeparatorChar);
            if (mount.Length == 0)
            {
                mount = Path.DirectorySeparatorChar.ToString();
            }
            if (!IsWithinMount(normalized, mount)
                || bestMount is not null && mount.Length <= bestMount.Length)
            {
                continue;
            }

            bestMount = mount;
            bestFileSystem = right[0];
        }

        return bestFileSystem;
    }

    private static bool IsWithinMount(string path, string mount) =>
        mount == Path.DirectorySeparatorChar.ToString()
        || path.Equals(mount, StringComparison.Ordinal)
        || path.StartsWith(mount + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static string UnescapeMountPath(string path) => path
        .Replace("\\040", " ", StringComparison.Ordinal)
        .Replace("\\011", "\t", StringComparison.Ordinal)
        .Replace("\\012", "\n", StringComparison.Ordinal)
        .Replace("\\134", "\\", StringComparison.Ordinal);

    private static void RejectSymbolicLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("EmbeddedSqlite forbids symbolic-link authority paths.");
        }
    }

    private static void RejectExistingPathSymbolicLinks(string path)
    {
        string? current = Path.GetFullPath(path);
        while (current is not null)
        {
            if (Directory.Exists(current) || File.Exists(current))
            {
                RejectSymbolicLink(current);
            }

            current = Path.GetDirectoryName(current);
        }
    }

    private static void HardenDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private void RecordAndValidateExistingFile(string path)
    {
        try
        {
            RejectSymbolicLink(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if (File.Exists(path))
        {
            ValidateFilePermissions(path);
            _preExistingFiles.Add(path);
        }
    }

    private void HardenNewFileIfPresent(string path)
    {
        if (!_preExistingFiles.Contains(path) && File.Exists(path))
        {
            RejectSymbolicLink(path);
            HardenFile(path);
        }
    }

    private void ValidateAuthorityFilePermissions()
    {
        ValidateFilePermissions(Options.Path);
        ValidateFilePermissionsIfPresent(Options.Path + "-wal");
        ValidateFilePermissionsIfPresent(Options.Path + "-shm");
    }

    private static void ValidateFilePermissionsIfPresent(string path)
    {
        if (File.Exists(path))
        {
            RejectSymbolicLink(path);
            ValidateFilePermissions(path);
        }
    }

    private static void HardenFile(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static void ValidateFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode forbidden =
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        UnixFileMode mode = File.GetUnixFileMode(path);
        if ((mode & forbidden) != 0
            || (mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite))
            != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
        {
            throw new InvalidOperationException("The embedded authority file permissions are unsafe.");
        }
    }
}
