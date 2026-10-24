using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Event.Testing;

/// <summary>Validates the existing private parent-run marker and selected Unix Docker endpoint.</summary>
internal sealed record AssuranceOwnershipContext
{
    private AssuranceOwnershipContext(string owner, Uri dockerEndpoint, string root)
    {
        Owner = owner;
        DockerEndpoint = dockerEndpoint;
        Root = root;
    }

    internal string Owner { get; }
    internal Uri DockerEndpoint { get; }
    internal string Root { get; }

    internal static async Task<string?> ValidateAssuranceOwnershipAsync(string? runId, string? root, string temporaryRoot)
    {
        if (runId is null && root is null) return null;
        try
        {
            if (!OperatingSystem.IsLinux() || runId is null || runId.Length != 32 ||
                runId.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')) ||
                string.IsNullOrEmpty(root) || !Path.IsPathFullyQualified(root) ||
                !Path.IsPathFullyQualified(temporaryRoot))
                throw new InvalidOperationException("test_resource_ownership_invalid");
            string temporary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(temporaryRoot));
            if (root != Path.GetFullPath(root) || root != Path.Combine(temporary, "islamu-assurance", runId))
                throw new InvalidOperationException("test_resource_ownership_invalid");
            for (DirectoryInfo? component = new(root); component is not null; component = component.Parent)
                if (!component.Exists || component.LinkTarget is not null ||
                    (component.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("test_resource_ownership_invalid");

            const UnixFileMode ownerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            const UnixFileMode ownerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            if (File.GetUnixFileMode(root) != ownerDirectory || File.GetUnixFileMode(temporary) != ownerDirectory)
                throw new InvalidOperationException("test_resource_ownership_invalid");
            string markerPath = Path.Combine(root, ".islamu-test-owner");
            var marker = new FileInfo(markerPath);
            if (!marker.Exists || marker.LinkTarget is not null || marker.Length != 32 ||
                (marker.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
                File.GetUnixFileMode(markerPath) != ownerFile || !await IsPrivateRegularMarkerAsync(markerPath))
                throw new InvalidOperationException("test_resource_ownership_invalid");
            await using var input = new FileStream(markerPath, new FileStreamOptions
            {
                Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.Asynchronous
            });
            if (input.Length != 32) throw new InvalidOperationException("test_resource_ownership_invalid");
            byte[] bytes = new byte[32];
            await input.ReadExactlyAsync(bytes);
            if (input.Length != 32 || !bytes.AsSpan().SequenceEqual(Encoding.ASCII.GetBytes(runId)))
                throw new InvalidOperationException("test_resource_ownership_invalid");
            return runId;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
            InvalidOperationException or Win32Exception or TimeoutException or OperationCanceledException or NotSupportedException)
        {
            throw new InvalidOperationException("test_resource_ownership_invalid");
        }
    }

    internal static async Task<AssuranceOwnershipContext?> ValidateAssuranceContextAsync(
        string? runId, string? root, string temporaryRoot, string? dockerHost)
    {
        string? owner = await ValidateAssuranceOwnershipAsync(runId, root, temporaryRoot);
        if (owner is null) return null;
        try
        {
            if (!Uri.TryCreate(dockerHost, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "unix" ||
                endpoint.Host.Length != 0 || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 ||
                endpoint.Fragment.Length != 0 || !Path.IsPathFullyQualified(endpoint.LocalPath) ||
                dockerHost != "unix://" + Path.GetFullPath(endpoint.LocalPath))
                throw new InvalidOperationException("test_resource_ownership_invalid");
            return new(owner, endpoint, root!);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or
            NotSupportedException or UriFormatException)
        {
            throw new InvalidOperationException("test_resource_ownership_invalid");
        }
    }

    private static async Task<bool> IsPrivateRegularMarkerAsync(string markerPath)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/usr/bin/stat")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            }
        };
        process.StartInfo.Environment.Clear();
        process.StartInfo.Environment["LC_ALL"] = "C";
        foreach (string argument in new[] { "--format=%f:%h:%s", "--", markerPath })
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        using var drain = new CancellationTokenSource();
        Task<string?> stdout = ReadMarkerOutputAsync(process.StandardOutput, 128, drain.Token);
        Task<string?> stderr = ReadMarkerOutputAsync(process.StandardError, 512, drain.Token);
        try
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await process.WaitForExitAsync(deadline.Token);
            string?[] streams = await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(1));
            string? text = streams[0];
            if (process.ExitCode != 0 || streams[1] != string.Empty || text is null ||
                !text.EndsWith('\n') || text[..^1].Contains('\n')) return false;
            string[] fields = text[..^1].Split(':');
            return fields.Length == 3 && fields[1] == "1" && fields[2] == "32" &&
                uint.TryParse(fields[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint mode) &&
                (mode & 0xf000) == 0x8000;
        }
        finally
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                await drain.CancelAsync();
                try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(1)); }
                catch (OperationCanceledException) { }
            }
        }
    }

    private static async Task<string?> ReadMarkerOutputAsync(StreamReader reader, int maximum, CancellationToken cancellationToken)
    {
        char[] buffer = new char[maximum + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) return new string(buffer, 0, length);
            length += read;
        }
        return null;
    }
}
