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

    private const string ToolName = "git-cliff";
    private const string CacheNamespace = "islamu-event";
    private const string CacheToolsDirectory = "tools";
    private const int MaximumProcessOutputCharacters = 4_096;
    private const long MaximumArchiveBytes = 64L * 1024 * 1024;
    private const long MaximumExecutableBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromSeconds(60);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static class Diagnostics
    {
        public const string LockNotConfigured = "toolchain lock is not configured";
        public const string LockMalformed = "toolchain lock is malformed";
        public const string BundleDirectoryRequired = "local tool bundle directory is required";
        public const string UnsafeArchiveStream = "unsafe archive stream";
        public const string VersionProbeFailed = "version probe failed";
        public const string VersionProbeTimedOut = "version probe timed out or exceeded output limit";
    }

    public static string GetCurrentPlatform()
    {
        string os = ResolveCurrentOperatingSystem();
        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "unsupported",
        };
        return $"{os}-{arch}";
    }

    private static string ResolveCurrentOperatingSystem()
    {
        if (OperatingSystem.IsLinux())
        {
            return "linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "osx";
        }

        if (OperatingSystem.IsWindows())
        {
            return "windows";
        }

        return "unsupported";
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

        string cacheRoot = ResolveDefaultCacheRoot();
        return Path.Combine(cacheRoot, ToolName, version, platform);
    }

    private static string ResolveDefaultCacheRoot()
    {
        string? explicitCache = Environment.GetEnvironmentVariable(DefaultCacheEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitCache))
        {
            return Path.GetFullPath(explicitCache);
        }

        string? xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrWhiteSpace(xdgCache))
        {
            return Path.Combine(Path.GetFullPath(xdgCache), CacheNamespace, CacheToolsDirectory);
        }

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return !string.IsNullOrWhiteSpace(home)
            ? Path.Combine(home, ".cache", CacheNamespace, CacheToolsDirectory)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CacheNamespace, CacheToolsDirectory);
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
            return new ToolchainLockLoadResult(false, null, Diagnostics.LockNotConfigured, IsMissingLock: true);
        }

        if (IsReparsePoint(lockPath))
        {
            return new ToolchainLockLoadResult(false, null, Diagnostics.LockMalformed);
        }

        RawToolchainLock? raw;
        try
        {
            raw = JsonSerializer.Deserialize<RawToolchainLock>(File.ReadAllText(lockPath, StrictUtf8), JsonOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DecoderFallbackException)
        {
            return new ToolchainLockLoadResult(false, null, Diagnostics.LockMalformed);
        }

        if (raw is not { SchemaVersion: 1, Tool: ToolName, Version: { } version, Platforms: { Length: > 0 } rawPlatforms } ||
            string.IsNullOrWhiteSpace(version))
        {
            return new ToolchainLockLoadResult(false, null, Diagnostics.LockMalformed);
        }

        List<ToolchainPlatformEntry>? entries = TryParsePlatformEntries(rawPlatforms);
        if (entries is null)
        {
            return new ToolchainLockLoadResult(false, null, Diagnostics.LockMalformed);
        }

        return new ToolchainLockLoadResult(
            true,
            new ToolchainLockDocument(raw.SchemaVersion, ToolName, version, entries),
            null);
    }

    private static List<ToolchainPlatformEntry>? TryParsePlatformEntries(RawToolPlatform[] rawPlatforms)
    {
        var entries = new List<ToolchainPlatformEntry>(rawPlatforms.Length);
        var seenPlatforms = new HashSet<string>(StringComparer.Ordinal);
        foreach (RawToolPlatform candidate in rawPlatforms)
        {
            if (string.IsNullOrWhiteSpace(candidate.Platform) ||
                !seenPlatforms.Add(candidate.Platform) ||
                !IsSafeSingleFileName(candidate.Executable) ||
                !IsSha256Hex(candidate.ExecutableSha256) ||
                !IsSafeSingleFileName(candidate.Archive) ||
                !IsSha256Hex(candidate.ArchiveSha256) ||
                !IsHttpsUriWithoutUserInfo(candidate.Source))
            {
                return null;
            }

            entries.Add(new ToolchainPlatformEntry(
                candidate.Platform,
                candidate.Executable!,
                candidate.ExecutableSha256!.ToLowerInvariant(),
                candidate.Archive!,
                candidate.ArchiveSha256!.ToLowerInvariant(),
                candidate.Source!));
        }

        return entries;
    }

    public static ToolchainVerificationResult VerifyInstalled(
        string lockPath,
        string? bundleDirectory,
        string platform,
        TimeSpan timeout)
    {
        if (!TryResolveApprovedPlatform(lockPath, platform, out ToolchainLockDocument? lockDocument, out ToolchainPlatformEntry? approved, out ToolchainVerificationResult? failure))
        {
            return failure!;
        }

        if (string.IsNullOrWhiteSpace(bundleDirectory) || !Directory.Exists(bundleDirectory) || IsReparsePoint(bundleDirectory))
        {
            return new ToolchainVerificationResult(false, null, Diagnostics.BundleDirectoryRequired);
        }

        string executablePath = Path.Combine(Path.GetFullPath(bundleDirectory), approved!.Executable);
        if (!TryReadVerifiedExecutable(executablePath, approved, out string? readDiagnostic))
        {
            return new ToolchainVerificationResult(false, null, readDiagnostic);
        }

        string? versionDiagnostic = VerifyReportedVersion(executablePath, lockDocument!.Version, timeout);
        if (versionDiagnostic is not null)
        {
            return new ToolchainVerificationResult(false, null, versionDiagnostic);
        }

        return new ToolchainVerificationResult(
            true,
            new VerifiedGitCliffExecutable(lockDocument.Version, platform, executablePath, approved.ExecutableSha256),
            null);
    }

    public static ToolchainVerificationResult EnsureProvisioned(
        string lockPath,
        string? targetDirectory,
        string platform,
        TimeSpan timeout,
        Func<Uri, byte[]>? archiveFetcher = null)
    {
        if (!TryResolveApprovedPlatform(lockPath, platform, out ToolchainLockDocument? lockDocument, out ToolchainPlatformEntry? approved, out ToolchainVerificationResult? failure))
        {
            return failure!;
        }

        string installDirectory = string.IsNullOrWhiteSpace(targetDirectory)
            ? GetDefaultStoreDirectory(lockDocument!.Version, platform)
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
            return new ToolchainVerificationResult(false, null, Diagnostics.BundleDirectoryRequired);
        }

        if (!TryAcquireVerifiedExecutable(approved!, archiveFetcher, out byte[]? executableBytes, out string? acquisitionDiagnostic))
        {
            return new ToolchainVerificationResult(false, null, acquisitionDiagnostic);
        }

        string? stageDiagnostic = StageAndCommitExecutable(installDirectory, approved!, lockDocument!.Version, executableBytes!, timeout);
        if (stageDiagnostic is not null)
        {
            return new ToolchainVerificationResult(false, null, stageDiagnostic);
        }

        return VerifyInstalled(lockPath, installDirectory, platform, timeout);
    }

    private static bool TryResolveApprovedPlatform(
        string lockPath,
        string platform,
        out ToolchainLockDocument? lockDocument,
        out ToolchainPlatformEntry? approved,
        out ToolchainVerificationResult? failure)
    {
        lockDocument = null;
        approved = null;
        failure = null;

        ToolchainLockLoadResult loaded = LoadLock(lockPath);
        if (!loaded.IsValid || loaded.Lock is null)
        {
            failure = new ToolchainVerificationResult(false, null, loaded.Diagnostic, loaded.IsMissingLock);
            return false;
        }

        ToolchainPlatformEntry? candidate = loaded.Lock.Platforms
            .SingleOrDefault(entry => string.Equals(entry.Platform, platform, StringComparison.Ordinal));
        if (candidate is null)
        {
            failure = new ToolchainVerificationResult(false, null, $"platform is not approved: {platform}");
            return false;
        }

        lockDocument = loaded.Lock;
        approved = candidate;
        return true;
    }

    private static bool TryReadVerifiedExecutable(string executablePath, ToolchainPlatformEntry approved, out string? diagnostic)
    {
        diagnostic = null;
        if (!File.Exists(executablePath) || IsReparsePoint(executablePath))
        {
            diagnostic = $"required executable is missing: {approved.Executable}";
            return false;
        }

        byte[] executableBytes;
        try
        {
            var info = new FileInfo(executablePath);
            if (info.Length <= 0 || info.Length > MaximumExecutableBytes)
            {
                diagnostic = $"executable digest mismatch: {approved.Executable}";
                return false;
            }

            executableBytes = File.ReadAllBytes(executablePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostic = $"required executable is missing: {approved.Executable}";
            return false;
        }

        if (!MatchesSha256(executableBytes, approved.ExecutableSha256))
        {
            diagnostic = $"executable digest mismatch: {approved.Executable}";
            return false;
        }

        return true;
    }

    private static bool TryAcquireVerifiedExecutable(
        ToolchainPlatformEntry approved,
        Func<Uri, byte[]>? archiveFetcher,
        out byte[]? executableBytes,
        out string? diagnostic)
    {
        executableBytes = null;
        diagnostic = null;

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
            diagnostic = $"archive download failed: {approved.Archive}";
            return false;
        }

        if (archiveBytes.Length == 0 || archiveBytes.LongLength > MaximumArchiveBytes || !MatchesSha256(archiveBytes, approved.ArchiveSha256))
        {
            diagnostic = $"archive digest mismatch: {approved.Archive}";
            return false;
        }

        try
        {
            executableBytes = ExtractVerifiedExecutableFromArchiveBytes(archiveBytes, approved);
            return true;
        }
        catch (InvalidDataException exception)
        {
            diagnostic = exception.Message;
            return false;
        }
    }

    private static string? StageAndCommitExecutable(
        string installDirectory,
        ToolchainPlatformEntry approved,
        string expectedVersion,
        byte[] executableBytes,
        TimeSpan timeout)
    {
        string destinationPath = Path.Combine(installDirectory, approved.Executable);
        string stagingPath = Path.Combine(installDirectory, $".{approved.Executable}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(stagingPath, executableBytes);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(stagingPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            string? versionDiagnostic = VerifyReportedVersion(stagingPath, expectedVersion, timeout);
            if (versionDiagnostic is not null)
            {
                return versionDiagnostic;
            }

            File.Move(stagingPath, destinationPath, overwrite: true);
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"failed to stage verified executable: {approved.Executable}";
        }
        finally
        {
            if (File.Exists(stagingPath))
            {
                File.Delete(stagingPath);
            }
        }
    }

    public static byte[] ExtractVerifiedExecutableFromArchiveBytes(byte[] archiveBytes, ToolchainPlatformEntry approved)
    {
        ArgumentNullException.ThrowIfNull(archiveBytes);
        ArgumentNullException.ThrowIfNull(approved);

        if (archiveBytes.Length == 0 || archiveBytes.LongLength > MaximumArchiveBytes || !MatchesSha256(archiveBytes, approved.ArchiveSha256))
        {
            throw new InvalidDataException($"archive digest mismatch: {approved.Archive}");
        }

        byte[]? extracted = ExtractArchivePayload(archiveBytes, approved);
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

    private static byte[]? ExtractArchivePayload(byte[] archiveBytes, ToolchainPlatformEntry approved)
    {
        if (approved.Archive.EndsWith(".tar.gz", StringComparison.Ordinal))
        {
            return ExtractFromTarGz(archiveBytes, approved.Executable);
        }

        if (approved.Archive.EndsWith(".zip", StringComparison.Ordinal))
        {
            return ExtractFromZip(archiveBytes, approved.Executable);
        }

        throw new InvalidDataException($"unsupported archive format: {approved.Archive}");
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
        var reader = new CliArgumentReader(args, startIndex: 1);
        while (reader.TryReadNext(out string option))
        {
            if (string.Equals(option, "--output", StringComparison.Ordinal) &&
                reader.TryReadNext(out string value))
            {
                targetDirectory = value;
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

        output.WriteLine($"trusted_tool: {ToolName} {result.Executable.Version} ({result.Executable.Platform})");
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
                byte[]? extracted = ReadTarExecutableEntry(entry, expectedExecutable, found is not null);
                if (extracted is not null)
                {
                    found = extracted;
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            throw new InvalidDataException(Diagnostics.UnsafeArchiveStream, exception);
        }

        return found;
    }

    private static byte[]? ReadTarExecutableEntry(TarEntry entry, string expectedExecutable, bool alreadyFound)
    {
        if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
        {
            throw new InvalidDataException($"unsafe archive link entry: {entry.Name}");
        }

        ValidateArchiveEntryPath(entry.Name);

        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
            !IsMatchingExecutableEntry(entry.Name, expectedExecutable))
        {
            return null;
        }

        if (alreadyFound)
        {
            throw new InvalidDataException($"unsafe archive duplicate executable entry: {expectedExecutable}");
        }

        if (entry.DataStream is null)
        {
            throw new InvalidDataException($"unsafe archive empty executable entry: {expectedExecutable}");
        }

        return ReadBoundedBytes(entry.DataStream, MaximumExecutableBytes);
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
                byte[]? extracted = ReadZipExecutableEntry(entry, expectedExecutable, found is not null);
                if (extracted is not null)
                {
                    found = extracted;
                }
            }
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or EndOfStreamException)
        {
            throw new InvalidDataException(Diagnostics.UnsafeArchiveStream, exception);
        }

        return found;
    }

    private static byte[]? ReadZipExecutableEntry(ZipArchiveEntry entry, string expectedExecutable, bool alreadyFound)
    {
        ValidateArchiveEntryPath(entry.FullName);

        int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
        if (unixMode == 0xA000)
        {
            throw new InvalidDataException($"unsafe archive symlink entry: {entry.FullName}");
        }

        if (entry.FullName.EndsWith('/') || !IsMatchingExecutableEntry(entry.FullName, expectedExecutable))
        {
            return null;
        }

        if (alreadyFound)
        {
            throw new InvalidDataException($"unsafe archive duplicate executable entry: {expectedExecutable}");
        }

        if (entry.Length <= 0 || entry.Length > MaximumExecutableBytes)
        {
            throw new InvalidDataException($"unsafe archive executable size: {entry.FullName}");
        }

        using Stream stream = entry.Open();
        return ReadBoundedBytes(stream, MaximumExecutableBytes);
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
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException($"unsafe archive entry path: {entryName}");
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
                return Diagnostics.VersionProbeFailed;
            }

            if (!string.Equals(versionOutput, $"{ToolName} {expectedVersion}", StringComparison.Ordinal))
            {
                return $"version mismatch: expected {ToolName} {expectedVersion}";
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return Diagnostics.VersionProbeTimedOut;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return Diagnostics.VersionProbeFailed;
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
