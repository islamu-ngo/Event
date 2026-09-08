#:property RestorePackagesWithLockFile=false

using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

const int MaximumArtifactCount = 4_096;
const int MaximumArtifactPathBytes = 4_096;
const int MaximumArtifactPathDepth = 32;
const long MaximumArtifactBytes = 1_073_741_824;
const long MaximumArtifactTreeBytes = 8_589_934_592;

var rootDirectory = args.Length > 0 ? args[0] : "artifacts/container";
var outputPath = args.Length > 1 ? args[1] : Path.Combine(rootDirectory, "checksums.sha256");

if (!Directory.Exists(rootDirectory))
{
    Console.Error.WriteLine($"Artifact directory does not exist: {rootDirectory}");
    return 1;
}

var rootFullPath = Path.GetFullPath(rootDirectory);
var outputFullPath = Path.GetFullPath(outputPath);

try
{
    if ((File.GetAttributes(rootFullPath) & FileAttributes.ReparsePoint) != 0)
    {
        Console.Error.WriteLine("artifact_checksums_path_alias");
        return 1;
    }
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
{
    Console.Error.WriteLine("artifact_checksums_path_invalid");
    return 1;
}

if (StringComparer.Ordinal.Equals(rootFullPath, outputFullPath)
    || IsInsideDirectory(rootFullPath, outputFullPath))
{
    Console.Error.WriteLine("artifact_checksums_output_path_invalid");
    return 1;
}

IReadOnlyList<string> files;

try
{
    files = EnumerateFiles(rootFullPath, outputFullPath);
}
catch (InvalidOperationException exception) when (exception.Message.StartsWith("artifact_checksums_", StringComparison.Ordinal))
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or PathTooLongException or NotSupportedException)
{
    Console.Error.WriteLine("artifact_checksums_path_invalid");
    return 1;
}

if (files.Count == 0)
{
    Console.Error.WriteLine($"No artifact files found under: {rootDirectory}");
    return 1;
}

var lines = new List<string>(files.Count);
foreach (var file in files)
{
    using var stream = File.OpenRead(file);
    var hash = SHA256.HashData(stream);
    var relativePath = ToManifestPath(rootFullPath, file);
    lines.Add($"{Convert.ToHexString(hash).ToLowerInvariant()}  {relativePath}");
}

string outputDirectory = Path.GetDirectoryName(outputFullPath) ?? rootFullPath;
Directory.CreateDirectory(outputDirectory);
string temporaryPath = Path.Combine(outputDirectory, $".{Path.GetFileName(outputFullPath)}.tmp-{Guid.NewGuid():N}");
try
{
    File.WriteAllText(temporaryPath, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
    File.Move(temporaryPath, outputFullPath, overwrite: true);
}
finally
{
    if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
}
Console.WriteLine($"Wrote {lines.Count} checksum entries to {outputPath}.");

return 0;

static string ToManifestPath(string rootFullPath, string fileFullPath)
{
    return Path.GetRelativePath(rootFullPath, fileFullPath)
        .Replace(Path.DirectorySeparatorChar, '/')
        .Replace(Path.AltDirectorySeparatorChar, '/');
}

static bool IsInsideDirectory(string path, string directory)
{
    string relativePath = Path.GetRelativePath(directory, path);
    return !relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath);
}

static IReadOnlyList<string> EnumerateFiles(string rootFullPath, string outputFullPath)
{
    var files = new List<string>();
    var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stack = new Stack<(string Directory, int Depth)>();
    stack.Push((rootFullPath, 0));
    long totalBytes = 0;

    while (stack.Count > 0)
    {
        (string directory, int depth) = stack.Pop();
        if (depth > MaximumArtifactPathDepth) throw new InvalidOperationException("artifact_checksums_path_invalid");
        foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
        {
            string fullPath = Path.GetFullPath(entry);
            if (StringComparer.Ordinal.Equals(fullPath, outputFullPath)) continue;
            string relativePath = ToManifestPath(rootFullPath, fullPath);
            ValidateRelativePath(rootFullPath, fullPath, relativePath, aliases);
            FileAttributes attributes = File.GetAttributes(fullPath);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("artifact_checksums_path_alias");
            if ((attributes & FileAttributes.Directory) != 0)
            {
                stack.Push((fullPath, depth + 1));
                continue;
            }

            if (HasUnsafeLinkCount(fullPath)) throw new InvalidOperationException("artifact_checksums_path_alias");
            long length = new FileInfo(fullPath).Length;
            if (length > MaximumArtifactBytes) throw new InvalidOperationException("artifact_checksums_size_invalid");
            totalBytes = checked(totalBytes + length);
            if (totalBytes > MaximumArtifactTreeBytes) throw new InvalidOperationException("artifact_checksums_tree_size_invalid");
            files.Add(fullPath);
            if (files.Count > MaximumArtifactCount) throw new InvalidOperationException("artifact_checksums_count_invalid");
        }
    }

    return files.OrderBy(path => ToManifestPath(rootFullPath, path), StringComparer.Ordinal).ToList();
}

static void ValidateRelativePath(string rootFullPath, string fullPath, string relativePath, HashSet<string> aliases)
{
    string rootPrefix = Path.TrimEndingDirectorySeparator(rootFullPath) + Path.DirectorySeparatorChar;
    if (!fullPath.StartsWith(rootPrefix, StringComparison.Ordinal)
        || relativePath.Length == 0
        || relativePath.StartsWith("../", StringComparison.Ordinal)
        || Path.IsPathRooted(relativePath))
    {
        throw new InvalidOperationException("artifact_checksums_path_invalid");
    }

    string[] segments = relativePath.Split('/');
    if (segments.Length > MaximumArtifactPathDepth
        || segments.Any(segment => segment.Length == 0 || segment is "." or "..")
        || !relativePath.IsNormalized(NormalizationForm.FormC)
        || Encoding.UTF8.GetByteCount(relativePath) > MaximumArtifactPathBytes)
    {
        throw new InvalidOperationException("artifact_checksums_path_invalid");
    }

    if (!aliases.Add(relativePath.Normalize(NormalizationForm.FormC))) throw new InvalidOperationException("artifact_checksums_path_alias");
}

static bool HasUnsafeLinkCount(string path)
{
    if (OperatingSystem.IsWindows()) return WindowsFileLinkCount.HasUnsafe(path);
    if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return true;
    string stat = OperatingSystem.IsLinux() ? "/usr/bin/stat" : "/usr/bin/stat";
    if (!File.Exists(stat)) return true;
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo(stat)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        },
    };
    if (OperatingSystem.IsLinux())
    {
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("%h");
    }
    else
    {
        process.StartInfo.ArgumentList.Add("-f");
        process.StartInfo.ArgumentList.Add("%l");
    }

    process.StartInfo.ArgumentList.Add(path);
    try
    {
        if (!process.Start()) return true;
        string output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(5)))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            return true;
        }

        return process.ExitCode != 0 || !int.TryParse(output.Trim(), CultureInfo.InvariantCulture, out int count) || count != 1;
    }
    catch (Exception exception) when (exception is IOException or InvalidOperationException or Win32Exception)
    {
        return true;
    }
}

static class WindowsFileLinkCount
{
    public static bool HasUnsafe(string path)
    {
        const uint fileShareReadWriteDelete = 0x00000007;
        const uint openExisting = 3;
        const uint fileAttributeNormal = 0x00000080;
        try
        {
            using SafeFileHandle handle = CreateFileW(path, 0, fileShareReadWriteDelete, IntPtr.Zero, openExisting, fileAttributeNormal, IntPtr.Zero);
            if (handle.IsInvalid) return true;
            return !GetFileInformationByHandle(handle, out ByHandleFileInformation information) || information.NumberOfLinks != 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle fileHandle, out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }
}
