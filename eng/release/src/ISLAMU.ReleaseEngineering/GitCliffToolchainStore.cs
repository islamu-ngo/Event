using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ISLAMU.ReleaseEngineering;

public sealed record ToolchainLockDocument(
    int SchemaVersion,
    string Tool,
    string Version,
    IReadOnlyList<ToolchainPlatformEntry> Platforms);

public sealed record ToolchainPlatformEntry(
    string Platform,
    string Executable,
    string ExecutableSha256,
    string Archive,
    string ArchiveSha256,
    string Source);

public sealed record ToolchainLockLoadResult(
    bool IsValid,
    ToolchainLockDocument? Lock,
    string? Diagnostic,
    bool IsMissingLock = false);

public sealed record VerifiedGitCliffExecutable(
    string Version,
    string Platform,
    string ExecutablePath,
    string ExecutableSha256);

public sealed record ToolchainVerificationResult(
    bool IsValid,
    VerifiedGitCliffExecutable? Executable,
    string? Diagnostic,
    bool IsMissingLock = false);

/// <summary>
/// Manages pinned, SHA-256 verified <c>git-cliff</c> executables.
/// Only <see cref="EnsureProvisioned"/> may fetch an archive over HTTPS; verification and execution remain strictly offline.
/// </summary>
public static class GitCliffToolchainStore
{
    public const string DefaultBundleEnvironmentVariable = "ISLAMU_RELEASE_TOOL_BUNDLE";
    public const string DefaultCacheEnvironmentVariable = "ISLAMU_RELEASE_TOOL_CACHE";

    private const int MaximumProcessOutputCharacters = 4_096;
    private const long MaximumArchiveBytes = 64L * 1024 * 1024;
    private const long MaximumExecutableBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(60);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static string GetCurrentPlatform()
    {
        string os = OperatingSystem.IsLinux()
            ? "linux"
            : OperatingSystem.IsMacOS()
                ? "osx"
                : OperatingSystem.IsWindows()
                    ? "windows"
                    : "unsupported";
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "unsupported",
        };
        return $"{os}-{arch}";
    }

    public static string GetDefaultStoreDirectory(string version, string platform)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);

        string? explicitBundle = Environment.GetEnvironmentVariable(DefaultBundleEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitBundle))
        {
            return Path.GetFullPath(explicitBundle);
        }

        string? explicitCache = Environment.GetEnvironmentVariable(DefaultCacheEnvironmentVariable);
        string cacheRoot;
        if (!string.IsNullOrWhiteSpace(explicitCache))
        {
            cacheRoot = Path.GetFullPath(explicitCache);
        }
        else
        {
            string? xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdgCache))
            {
                cacheRoot = Path.Combine(Path.GetFullPath(xdgCache), "islamu-event", "tools");
            }
            else
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                cacheRoot = !string.IsNullOrWhiteSpace(home)
                    ? Path.Combine(home, ".cache", "islamu-event", "tools")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "islamu-event", "tools");
            }
        }

        return Path.Combine(cacheRoot, "git-cliff", version, platform);
    }

    public static string ResolveConfiguredDirectory(string lockPath, string? explicitBundlePath, string platform)
    {
        if (!string.IsNullOrWhiteSpace(explicitBundlePath))
        {
            return Path.GetFullPath(explicitBundlePath);
        }

        ToolchainLockLoadResult loaded = LoadLock(lockPath);
        string version = loaded.IsValid && loaded.Lock is not null ? loaded.Lock.Version : "unknown";
        return GetDefaultStoreDirectory(version, platform);
    }

    public static ToolchainLockLoadResult LoadLock(string lockPath)
    {
        if (string.IsNullOrWhiteSpace(lockPath) || !File.Exists(lockPath))
        {
            return new ToolchainLockLoadResult(false, null, "toolchain lock is not configured", IsMissingLock: true);
        }

        if (IsReparsePoint(lockPath))
        {
            return new ToolchainLockLoadResult(false, null, "toolchain lock is malformed");
        }

        RawToolchainLock? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawToolchainLock>(File.ReadAllText(lockPath, StrictUtf8), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            return new ToolchainLockLoadResult(false, null, "toolchain lock is malformed");
        }

        if (raw is null ||
            raw.SchemaVersion != 1 ||
            !string.Equals(raw.Tool, "git-cliff", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(raw.Version) ||
            raw.Platforms is null ||
            raw.Platforms.Length == 0)
        {
            return new ToolchainLockLoadResult(false, null, "toolchain lock is malformed");
        }

        var entries = new List<ToolchainPlatformEntry>(raw.Platforms.Length);
        var seenPlatforms = new HashSet<string>(StringComparer.Ordinal);
        foreach (RawToolPlatform candidate in raw.Platforms)
        {
            if (string.IsNullOrWhiteSpace(candidate.Platform) ||
                !seenPlatforms.Add(candidate.Platform) ||
                !IsSafeSingleFileName(candidate.Executable) ||
                !IsSha256Hex(candidate.ExecutableSha256) ||
                !IsSafeSingleFileName(candidate.Archive) ||
                !IsSha256Hex(candidate.ArchiveSha256) ||
                !IsHttpsUriWithoutUserInfo(candidate.Source))
            {
                return new ToolchainLockLoadResult(false, null, "toolchain lock is malformed");
            }

            entries.Add(new ToolchainPlatformEntry(
                candidate.Platform,
                candidate.Executable!,
                candidate.ExecutableSha256!.ToLowerInvariant(),
                candidate.Archive!,
                candidate.ArchiveSha256!.ToLowerInvariant(),
                candidate.Source!));
        }

        return new ToolchainLockLoadResult(
            true,
            new ToolchainLockDocument(raw.SchemaVersion, raw.Tool!, raw.Version!, entries),
            null);
    }

    public static ToolchainVerificationResult VerifyInstalled(
        string lockPath,
        string? bundleDirectory,
        string platform,
        TimeSpan timeout)
    {
        ToolchainLockLoadResult loaded = LoadLock(lockPath);
        if (!loaded.IsValid || loaded.Lock is null)
        {
            return new ToolchainVerificationResult(false, null, loaded.Diagnostic, loaded.IsMissingLock);
        }

        if (string.IsNullOrWhiteSpace(bundleDirectory) || !Directory.Exists(bundleDirectory) || IsReparsePoint(bundleDirectory))
        {
            return new ToolchainVerificationResult(false, null, "local tool bundle directory is required");
        }

        ToolchainPlatformEntry? approved = loaded.Lock.Platforms
            .SingleOrDefault(candidate => string.Equals(candidate.Platform, platform, StringComparison.Ordinal));
        if (approved is null)
        {
            return new ToolchainVerificationResult(false, null, $"platform is not approved: {platform}");
        }

        string executablePath = Path.Combine(Path.GetFullPath(bundleDirectory), approved.Executable);
        if (!File.Exists(executablePath) || IsReparsePoint(executablePath))
        {
            return new ToolchainVerificationResult(false, null, $"required executable is missing: {approved.Executable}");
        }

        byte[] executableBytes;
        try
        {
            var info = new FileInfo(executablePath);
            if (info.Length <= 0 || info.Length > MaximumExecutableBytes)
            {
                return new ToolchainVerificationResult(false, null, $"executable digest mismatch: {approved.Executable}");
            }

            executableBytes = File.ReadAllBytes(executablePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ToolchainVerificationResult(false, null, $"required executable is missing: {approved.Executable}");
        }

        if (!MatchesSha256(executableBytes, approved.ExecutableSha256))
        {
            return new ToolchainVerificationResult(false, null, $"executable digest mismatch: {approved.Executable}");
        }

        string? versionDiagnostic = VerifyReportedVersion(executablePath, loaded.Lock.Version, timeout);
        if (versionDiagnostic is not null)
        {
            return new ToolchainVerificationResult(false, null, versionDiagnostic);
        }

        return new ToolchainVerificationResult(
            true,
            new VerifiedGitCliffExecutable(loaded.Lock.Version, platform, executablePath, approved.ExecutableSha256),
            null);
    }

    public static ToolchainVerificationResult EnsureProvisioned(
        string lockPath,
        string? targetDirectory,
        string platform,
        TimeSpan timeout,
        Func<Uri, byte[]>? archiveFetcher = null)
    {
        ToolchainLockLoadResult loaded = LoadLock(lockPath);
        if (!loaded.IsValid || loaded.Lock is null)
        {
            return new ToolchainVerificationResult(false, null, loaded.Diagnostic, loaded.IsMissingLock);
        }

        ToolchainPlatformEntry? approved = loaded.Lock.Platforms
            .SingleOrDefault(candidate => string.Equals(candidate.Platform, platform, StringComparison.Ordinal));
        if (approved is null)
        {
            return new ToolchainVerificationResult(false, null, $"platform is not approved: {platform}");
        }

        string installDirectory = string.IsNullOrWhiteSpace(targetDirectory)
            ? GetDefaultStoreDirectory(loaded.Lock.Version, platform)
            : Path.GetFullPath(targetDirectory);

        if (Directory.Exists(installDirectory))
        {
            ToolchainVerificationResult existing = VerifyInstalled(lockPath, installDirectory, platform, timeout);
            if (existing.IsValid)
            {
                return existing;
            }
        }

        Directory.CreateDirectory(installDirectory);
        if (IsReparsePoint(installDirectory))
        {
            return new ToolchainVerificationResult(false, null, "local tool bundle directory is required");
        }

        byte[] archiveBytes;
        try
        {
            Uri sourceUri = new(approved.Source, UriKind.Absolute);
            archiveBytes = archiveFetcher is not null
                ? archiveFetcher(sourceUri)
                : DownloadArchive(sourceUri);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
        {
            return new ToolchainVerificationResult(false, null, $"archive download failed: {approved.Archive}");
        }

        if (archiveBytes.Length == 0 || archiveBytes.LongLength > MaximumArchiveBytes || !MatchesSha256(archiveBytes, approved.ArchiveSha256))
        {
            return new ToolchainVerificationResult(false, null, $"archive digest mismatch: {approved.Archive}");
        }

        byte[] executableBytes;
        try
        {
            executableBytes = ExtractVerifiedExecutableFromArchiveBytes(archiveBytes, approved);
        }
        catch (InvalidDataException exception)
        {
            return new ToolchainVerificationResult(false, null, exception.Message);
        }

        string destinationPath = Path.Combine(installDirectory, approved.Executable);
        string stagingPath = Path.Combine(installDirectory, $".{approved.Executable}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(stagingPath, executableBytes);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(stagingPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string? versionDiagnostic = VerifyReportedVersion(stagingPath, loaded.Lock.Version, timeout);
            if (versionDiagnostic is not null)
            {
                return new ToolchainVerificationResult(false, null, versionDiagnostic);
            }

            File.Move(stagingPath, destinationPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ToolchainVerificationResult(false, null, $"failed to stage verified executable: {approved.Executable}");
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }

        return VerifyInstalled(lockPath, installDirectory, platform, timeout);
    }

    public static byte[] ExtractVerifiedExecutableFromArchiveBytes(byte[] archiveBytes, ToolchainPlatformEntry approved)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);
        ArgumentNullException.ThrowIfNull(approved);

        if (archiveBytes.Length == 0 || archiveBytes.LongLength > MaximumArchiveBytes || !MatchesSha256(archiveBytes, approved.ArchiveSha256))
        {
            throw new InvalidDataException($"archive digest mismatch: {approved.Archive}");
        }

        byte[]? extracted = approved.Archive.EndsWith(".tar.gz", StringComparison.Ordinal)
            ? ExtractFromTarGz(archiveBytes, approved.Executable)
            : approved.Archive.EndsWith(".zip", StringComparison.Ordinal)
                ? ExtractFromZip(archiveBytes, approved.Executable)
                : throw new InvalidDataException($"unsupported archive format: {approved.Archive}");

        if (extracted is null || extracted.Length == 0 || extracted.LongLength > MaximumExecutableBytes)
        {
            throw new InvalidDataException($"required executable is missing in archive: {approved.Executable}");
        }

        if (!MatchesSha256(extracted, approved.ExecutableSha256))
        {
            throw new InvalidDataException($"executable digest mismatch: {approved.Executable}");
        }

        return extracted;
    }

    public static ProcessStartInfo CreateOfflineNoExecStartInfo(
        VerifiedGitCliffExecutable tool,
        string configPath,
        string workingDirectory,
        IEnumerable<string> additionalArguments)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        ArgumentNullException.ThrowIfNull(additionalArguments);

        var startInfo = new ProcessStartInfo(tool.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
            StandardOutputEncoding = StrictUtf8,
            StandardErrorEncoding = StrictUtf8,
        };

        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(configPath);
        startInfo.ArgumentList.Add("--offline");
        startInfo.ArgumentList.Add("--no-exec");
        foreach (string argument in additionalArguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment.Clear();
        foreach ((string name, string value) in ReleaseArtifactPolicy.CreateDeterministicEnvironment(workingDirectory))
        {
            startInfo.Environment[name] = value;
        }

        startInfo.Environment["GIT_CLIFF_OFFLINE"] = "1";
        return startInfo;
    }

    public static int RunEnsureCommand(
        string[] args,
        TextWriter output,
        string lockPath,
        string? defaultBundlePath,
        string platform,
        TimeSpan timeout)
    {
        string? targetDirectory = defaultBundlePath;
        for (int index = 1; index < args.Length; index++)
        {
            if (string.Equals(args[index], "--output", StringComparison.Ordinal) && index + 1 < args.Length)
            {
                targetDirectory = args[++index];
            }
            else
            {
                output.WriteLine("invalid_arguments: ensure-git-cliff accepts only optional --output <directory>");
                return Program.UsageError;
            }
        }

        ToolchainVerificationResult result = EnsureProvisioned(lockPath, targetDirectory, platform, timeout);
        if (!result.IsValid || result.Executable is null)
        {
            output.WriteLine($"untrusted_tool: {result.Diagnostic}");
            return result.IsMissingLock ? Program.ToolchainNotConfigured : Program.ToolchainRejected;
        }

        output.WriteLine($"trusted_tool: git-cliff {result.Executable.Version} ({result.Executable.Platform})");
        return Program.Success;
    }

    private static byte[] DownloadArchive(Uri sourceUri)
    {
        if (!string.Equals(sourceUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(sourceUri.UserInfo))
        {
            throw new InvalidOperationException("Archive source must be HTTPS without user info.");
        }

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };
        using var client = new HttpClient(handler) { Timeout = DefaultDownloadTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ISLAMU.ReleaseEngineering/1.0");

        using HttpResponseMessage response = client.GetAsync(sourceUri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is > MaximumArchiveBytes)
        {
            throw new InvalidOperationException("Archive exceeds maximum allowed size.");
        }

        using Stream networkStream = response.Content.ReadAsStream();
        return ReadBoundedBytes(networkStream, MaximumArchiveBytes);
    }

    private static byte[]? ExtractFromTarGz(byte[] archiveBytes, string expectedExecutable)
    {
        byte[]? found = null;
        try
        {
            using var memory = new MemoryStream(archiveBytes, writable: false);
            using var gzip = new GZipStream(memory, CompressionMode.Decompress);
            using var reader = new TarReader(gzip, leaveOpen: false);

            while (reader.GetNextEntry() is { } entry)
            {
                if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                {
                    throw new InvalidDataException($"unsafe archive link entry: {entry.Name}");
                }

                ValidateArchiveEntryPath(entry.Name);

                if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile))
                {
                    continue;
                }

                if (!IsMatchingExecutableEntry(entry.Name, expectedExecutable))
                {
                    continue;
                }

                if (found is not null)
                {
                    throw new InvalidDataException($"unsafe archive duplicate executable entry: {expectedExecutable}");
                }

                if (entry.DataStream is null)
                {
                    throw new InvalidDataException($"unsafe archive empty executable entry: {expectedExecutable}");
                }

                found = ReadBoundedBytes(entry.DataStream, MaximumExecutableBytes);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            throw new InvalidDataException("unsafe archive stream", exception);
        }

        return found;
    }

    private static byte[]? ExtractFromZip(byte[] archiveBytes, string expectedExecutable)
    {
        byte[]? found = null;
        try
        {
            using var memory = new MemoryStream(archiveBytes, writable: false);
            using var zip = new ZipArchive(memory, ZipArchiveMode.Read, leaveOpen: false);

            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                ValidateArchiveEntryPath(entry.FullName);

                int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                if (unixMode == 0xA000)
                {
                    throw new InvalidDataException($"unsafe archive symlink entry: {entry.FullName}");
                }

                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                if (!IsMatchingExecutableEntry(entry.FullName, expectedExecutable))
                {
                    continue;
                }

                if (found is not null)
                {
                    throw new InvalidDataException($"unsafe archive duplicate executable entry: {expectedExecutable}");
                }

                if (entry.Length <= 0 || entry.Length > MaximumExecutableBytes)
                {
                    throw new InvalidDataException($"unsafe archive executable size: {entry.FullName}");
                }

                using Stream stream = entry.Open();
                found = ReadBoundedBytes(stream, MaximumExecutableBytes);
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            throw new InvalidDataException("unsafe archive stream", exception);
        }

        return found;
    }

    private static void ValidateArchiveEntryPath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            entryName.Contains('\\', StringComparison.Ordinal) ||
            entryName.StartsWith('/') ||
            entryName.Contains(':', StringComparison.Ordinal) ||
            entryName.Any(char.IsControl))
        {
            throw new InvalidDataException($"unsafe archive entry path: {entryName}");
        }

        string trimmed = entryName.TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return;
        }

        string[] segments = trimmed.Split('/');
        foreach (string segment in segments)
        {
            if (segment is "" or "." or "..")
            {
                throw new InvalidDataException($"unsafe archive entry path: {entryName}");
            }
        }
    }

    private static bool IsMatchingExecutableEntry(string entryName, string expectedExecutable)
    {
        string[] segments = entryName.TrimEnd('/').Split('/');
        return segments.Length is 1 or 2 &&
            string.Equals(segments[^1], expectedExecutable, StringComparison.Ordinal);
    }

    private static byte[] ReadBoundedBytes(Stream stream, long maxBytes)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[16_384];
        long total = 0;
        while (true)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return output.ToArray();
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("unsafe archive entry exceeds maximum size");
            }

            output.Write(buffer, 0, read);
        }
    }

    private static string? VerifyReportedVersion(string executablePath, string expectedVersion, TimeSpan timeout)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executablePath)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = StrictUtf8,
                StandardErrorEncoding = StrictUtf8,
            },
        };
        process.StartInfo.ArgumentList.Add("--version");

        try
        {
            process.Start();
            using var timeoutSource = new CancellationTokenSource(timeout);
            Task<string> standardOutput = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
            Task<string> standardError = ReadBoundedAsync(process.StandardError, timeoutSource.Token);
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
            string versionOutput = standardOutput.GetAwaiter().GetResult().Trim();
            string errorOutput = standardError.GetAwaiter().GetResult().Trim();

            if (process.ExitCode != 0 || errorOutput.Length != 0)
            {
                return "version probe failed";
            }

            if (!string.Equals(versionOutput, $"git-cliff {expectedVersion}", StringComparison.Ordinal))
            {
                return $"version mismatch: expected git-cliff {expectedVersion}";
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return "version probe timed out or exceeded output limit";
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return "version probe failed";
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var result = new char[MaximumProcessOutputCharacters];
        int count = 0;
        while (count < result.Length)
        {
            int read = await reader.ReadAsync(result.AsMemory(count, result.Length - count), cancellationToken);
            if (read == 0)
            {
                return new string(result, 0, count);
            }

            count += read;
        }

        if (await reader.ReadAsync(new char[1], cancellationToken) != 0)
        {
            throw new OperationCanceledException("Process output exceeded the configured limit.");
        }

        return new string(result);
    }

    private static bool MatchesSha256(byte[] content, string expectedHex)
    {
        byte[] actual = SHA256.HashData(content);
        byte[] expected = Convert.FromHexString(expectedHex);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static bool IsSafeSingleFileName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Path.GetFileName(value) == value &&
        !value.Contains("..", StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsHttpsUriWithoutUserInfo(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsReparsePoint(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private sealed record RawToolchainLock(
        int SchemaVersion,
        string? Tool,
        string? Version,
        RawToolPlatform[]? Platforms);

    private sealed record RawToolPlatform(
        string? Platform,
        string? Executable,
        string? ExecutableSha256,
        string? Archive,
        string? ArchiveSha256,
        string? Source);
}
