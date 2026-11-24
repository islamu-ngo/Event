using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Reads pinned Git commit trees and blob bytes directly from Git object storage with bounded
/// output sizes and timeouts, completely independent of the working tree.
/// </summary>
public sealed class GitProcessObjectReader : IGitObjectReader
{
    public const int DefaultMaximumBlobBytes = 32 * 1024 * 1024;
    private const int MaximumTreeListBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly string repositoryRoot;
    private readonly TimeSpan timeout;
    private readonly int maximumBlobBytes;

    public GitProcessObjectReader(
        string repositoryRoot,
        TimeSpan timeout,
        int maximumBlobBytes = DefaultMaximumBlobBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBlobBytes);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        this.timeout = timeout;
        this.maximumBlobBytes = maximumBlobBytes;
    }

    public static bool IsFullOid(string? value) =>
        value is not null && FullOidPattern.IsMatch(value);

    public bool CommitExists(string fullOid)
    {
        if (!IsFullOid(fullOid))
        {
            return false;
        }

        GitRunOutcome outcome = RunGitBytes(64, "cat-file", "-t", fullOid);
        return outcome.Succeeded &&
            outcome.Bytes is not null &&
            string.Equals(StrictUtf8.GetString(outcome.Bytes).Trim(), "commit", StringComparison.Ordinal);
    }

    public GitTreeListResult ListFiles(string fullOid, string? pathPrefix = null)
    {
        if (!IsFullOid(fullOid))
        {
            return new GitTreeListResult(false, [], "git_object_oid_invalid");
        }

        if (pathPrefix is not null && !IsSafeRepositoryPath(pathPrefix))
        {
            return new GitTreeListResult(false, [], "git_object_path_invalid");
        }

        var args = new List<string> { "ls-tree", "-r", "-z", "--name-only", fullOid };
        if (!string.IsNullOrWhiteSpace(pathPrefix))
        {
            args.Add("--");
            args.Add(pathPrefix.TrimEnd('/'));
        }

        GitRunOutcome outcome = RunGitBytes(MaximumTreeListBytes, args.ToArray());
        if (outcome.LimitExceeded)
        {
            return new GitTreeListResult(false, [], "git_object_tree_too_large");
        }

        if (!outcome.Succeeded || outcome.Bytes is null)
        {
            return new GitTreeListResult(false, [], "git_object_tree_read_failed");
        }

        string decoded = StrictUtf8.GetString(outcome.Bytes);
        string[] paths = decoded
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return new GitTreeListResult(true, paths, null);
    }

    public GitBlobReadResult ReadBlob(string fullOid, string repositoryRelativePath)
    {
        if (!IsFullOid(fullOid))
        {
            return new GitBlobReadResult(false, null, "git_object_oid_invalid");
        }

        if (!IsSafeRepositoryPath(repositoryRelativePath))
        {
            return new GitBlobReadResult(false, null, "git_object_path_invalid");
        }

        string spec = $"{fullOid}:{repositoryRelativePath}";
        GitRunOutcome sizeOutcome = RunGitBytes(64, "cat-file", "-s", spec);
        if (!sizeOutcome.Succeeded || sizeOutcome.Bytes is null)
        {
            return new GitBlobReadResult(false, null, null);
        }

        string sizeText = StrictUtf8.GetString(sizeOutcome.Bytes).Trim();
        if (!long.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out long size) || size < 0)
        {
            return new GitBlobReadResult(false, null, "git_object_blob_read_failed");
        }

        if (size > maximumBlobBytes)
        {
            return new GitBlobReadResult(true, null, "git_object_blob_too_large");
        }

        GitRunOutcome contentOutcome = RunGitBytes(maximumBlobBytes, "cat-file", "blob", spec);
        if (contentOutcome.LimitExceeded)
        {
            return new GitBlobReadResult(true, null, "git_object_blob_too_large");
        }

        if (!contentOutcome.Succeeded || contentOutcome.Bytes is null)
        {
            return new GitBlobReadResult(false, null, "git_object_blob_read_failed");
        }

        return new GitBlobReadResult(true, contentOutcome.Bytes, null);
    }

    private GitRunOutcome RunGitBytes(int maxStdoutBytes, params string[] arguments)
    {
        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        var startInfo = new ProcessStartInfo(ReleaseToolPaths.Git)
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        startInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        startInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        startInfo.ArgumentList.Add("--no-replace-objects");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new GitRunOutcome(false, null, LimitExceeded: false);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
        {
            return new GitRunOutcome(false, null, LimitExceeded: false);
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var overflowSource = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, overflowSource.Token);

        Task<StreamReadResult> stdoutTask = ReadBoundedBytesAsync(process.StandardOutput.BaseStream, maxStdoutBytes, overflowSource, linked.Token);
        Task<StreamReadResult> stderrTask = ReadBoundedBytesAsync(process.StandardError.BaseStream, 65_536, overflowSource, linked.Token);

        try
        {
            process.WaitForExitAsync(linked.Token).GetAwaiter().GetResult();
            StreamReadResult stdout = stdoutTask.GetAwaiter().GetResult();
            StreamReadResult stderr = stderrTask.GetAwaiter().GetResult();
            if (stdout.LimitExceeded || stderr.LimitExceeded || overflowSource.IsCancellationRequested)
            {
                return new GitRunOutcome(false, null, LimitExceeded: true);
            }

            return process.ExitCode == 0
                ? new GitRunOutcome(true, stdout.Bytes, LimitExceeded: false)
                : new GitRunOutcome(false, null, LimitExceeded: false);
        }
        catch (OperationCanceledException)
        {
            KillSafely(process);
            return new GitRunOutcome(false, null, LimitExceeded: overflowSource.IsCancellationRequested);
        }
        catch (IOException)
        {
            KillSafely(process);
            return new GitRunOutcome(false, null, LimitExceeded: false);
        }
        finally
        {
            if (!process.HasExited)
            {
                KillSafely(process);
            }
        }
    }

    private static async Task<StreamReadResult> ReadBoundedBytesAsync(
        Stream stream,
        int maxBytes,
        CancellationTokenSource overflowSource,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16_384];
        int total = 0;

        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    await overflowSource.CancelAsync().ConfigureAwait(false);
                    return new StreamReadResult(null, LimitExceeded: true);
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (OperationCanceledException) when (overflowSource.IsCancellationRequested)
        {
            return new StreamReadResult(null, LimitExceeded: true);
        }

        return new StreamReadResult(buffer.ToArray(), LimitExceeded: false);
    }

    private static bool IsSafeRepositoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.StartsWith('-') ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Any(char.IsControl))
        {
            return false;
        }

        foreach (string segment in path.Split('/'))
        {
            if (segment is "" or "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static void KillSafely(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // Process exited concurrently.
        }
    }

    private sealed record GitRunOutcome(bool Succeeded, byte[]? Bytes, bool LimitExceeded);

    private sealed record StreamReadResult(byte[]? Bytes, bool LimitExceeded);
}
