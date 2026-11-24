using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Executes a SHA-256 and version verified <c>git-cliff</c> binary with strict timeouts,
/// bounded output capture, offline <c>--no-exec</c> enforcement, and fixed symbolic diagnostics
/// that never leak raw context, commit messages, or stderr payloads.
/// </summary>
public sealed class GitCliffProcessExecutor : IGitCliffExecutor
{
    private const int DefaultMaximumStdoutBytes = 16 * 1024 * 1024;
    private const int DefaultMaximumStderrBytes = 256 * 1024;
    private const int MaximumBumpStdoutBytes = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly string[] RemoteProviders = ["github", "gitlab", "gitea", "bitbucket", "azure_devops"];
    private static readonly Regex SemVerPattern = new(
        "^(?:0|[1-9][0-9]{0,8})\\.(?:0|[1-9][0-9]{0,8})\\.(?:0|[1-9][0-9]{0,8})(?:-(?:alpha|beta|rc)\\.[1-9][0-9]{0,5})?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private readonly int maximumStdoutBytes;
    private readonly int maximumStderrBytes;

    public GitCliffProcessExecutor(
        int maximumStdoutBytes = DefaultMaximumStdoutBytes,
        int maximumStderrBytes = DefaultMaximumStderrBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStdoutBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumStderrBytes);
        this.maximumStdoutBytes = maximumStdoutBytes;
        this.maximumStderrBytes = maximumStderrBytes;
    }

    public static GitCliffProcessExecutor Default { get; } = new();

    public GitCliffContextExportResult ExportContext(GitCliffContextExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Tag) || request.Tag.Any(char.IsControl))
        {
            return new GitCliffContextExportResult(false, null, "git_cliff_input_invalid");
        }

        if (request.CommitRange is not null && (string.IsNullOrWhiteSpace(request.CommitRange) || request.CommitRange.Any(char.IsControl)))
        {
            return new GitCliffContextExportResult(false, null, "git_cliff_input_invalid");
        }

        var arguments = new List<string> { "--context", "--tag", request.Tag };
        if (request.CommitRange is not null)
        {
            arguments.Add("--");
            arguments.Add(request.CommitRange);
        }

        ProcessOutcome outcome = Execute(
            request.Tool,
            request.RepositoryRoot,
            arguments,
            stdinBytes: null,
            stdoutLimitBytes: maximumStdoutBytes);
        if (!outcome.IsValid || outcome.Stdout is null)
        {
            return new GitCliffContextExportResult(false, null, outcome.Diagnostic);
        }

        if (!IsJsonArray(outcome.Stdout))
        {
            return new GitCliffContextExportResult(false, null, "git_cliff_invalid_context_json");
        }

        return new GitCliffContextExportResult(true, outcome.Stdout, null);
    }

    public GitCliffRenderFromContextResult RenderFromContext(GitCliffRenderFromContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RenderingContextJson))
        {
            return new GitCliffRenderFromContextResult(false, null, "git_cliff_input_invalid");
        }

        byte[]? wirePayload = TryBuildWireContextBytes(request.RenderingContextJson);
        if (wirePayload is null)
        {
            return new GitCliffRenderFromContextResult(false, null, "git_cliff_input_invalid");
        }

        ProcessOutcome outcome = Execute(
            request.Tool,
            request.WorkingDirectory,
            ["--from-context", "-"],
            stdinBytes: wirePayload,
            stdoutLimitBytes: maximumStdoutBytes);
        if (!outcome.IsValid || outcome.Stdout is null)
        {
            return new GitCliffRenderFromContextResult(false, null, outcome.Diagnostic);
        }

        return new GitCliffRenderFromContextResult(true, outcome.Stdout, null);
    }

    public GitCliffBumpVersionResult SuggestBumpedVersion(GitCliffBumpVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CommitRange is not null && (string.IsNullOrWhiteSpace(request.CommitRange) || request.CommitRange.Any(char.IsControl)))
        {
            return new GitCliffBumpVersionResult(false, null, null, "git_cliff_input_invalid");
        }

        var arguments = new List<string> { "--bumped-version" };
        if (request.CommitRange is not null)
        {
            arguments.Add("--");
            arguments.Add(request.CommitRange);
        }

        ProcessOutcome outcome = Execute(
            request.Tool,
            request.RepositoryRoot,
            arguments,
            stdinBytes: null,
            stdoutLimitBytes: Math.Min(maximumStdoutBytes, MaximumBumpStdoutBytes));
        if (!outcome.IsValid || outcome.Stdout is null)
        {
            return new GitCliffBumpVersionResult(false, null, null, outcome.Diagnostic);
        }

        string raw = outcome.Stdout.Trim();
        if (raw.Length == 0 || raw.Contains('\n', StringComparison.Ordinal) || raw.Contains('\r', StringComparison.Ordinal))
        {
            return new GitCliffBumpVersionResult(false, null, null, "git_cliff_invalid_bump_version");
        }

        string normalized = raw.StartsWith('v') ? raw[1..] : raw;
        if (!SemVerPattern.IsMatch(normalized))
        {
            return new GitCliffBumpVersionResult(false, null, null, "git_cliff_invalid_bump_version");
        }

        return new GitCliffBumpVersionResult(true, normalized, raw, null);
    }

    private ProcessOutcome Execute(
        GitCliffToolConfiguration tool,
        string workingDirectory,
        IReadOnlyList<string> additionalArguments,
        byte[]? stdinBytes,
        int stdoutLimitBytes)
    {
        if (tool is null || tool.Timeout <= TimeSpan.Zero)
        {
            return new ProcessOutcome(false, null, "git_cliff_input_invalid");
        }

        if (string.IsNullOrWhiteSpace(tool.ConfigPath) || !File.Exists(tool.ConfigPath) || IsReparsePoint(tool.ConfigPath))
        {
            return new ProcessOutcome(false, null, "git_cliff_config_invalid");
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory) || IsReparsePoint(workingDirectory))
        {
            return new ProcessOutcome(false, null, "git_cliff_working_directory_invalid");
        }

        ToolchainVerificationResult verification = GitCliffToolchainStore.VerifyInstalled(
            tool.LockPath,
            tool.ToolDirectory,
            tool.Platform,
            tool.Timeout);
        if (!verification.IsValid || verification.Executable is null)
        {
            return new ProcessOutcome(false, null, "git_cliff_tool_untrusted");
        }

        ProcessStartInfo startInfo = GitCliffToolchainStore.CreateOfflineNoExecStartInfo(
            verification.Executable,
            Path.GetFullPath(tool.ConfigPath),
            Path.GetFullPath(workingDirectory),
            additionalArguments);
        if (stdinBytes is not null)
        {
            startInfo.RedirectStandardInput = true;
            startInfo.StandardInputEncoding = StrictUtf8;
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ProcessOutcome(false, null, "git_cliff_execution_failed");
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
        {
            return new ProcessOutcome(false, null, "git_cliff_execution_failed");
        }

        using var timeoutSource = new CancellationTokenSource(tool.Timeout);
        using var overflowSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, overflowSource.Token);

        Task<BoundedReadResult> stdoutTask = ReadBoundedUtf8Async(
            process.StandardOutput.BaseStream,
            stdoutLimitBytes,
            overflowSource,
            linkedSource.Token);
        Task<BoundedReadResult> stderrTask = ReadBoundedUtf8Async(
            process.StandardError.BaseStream,
            maximumStderrBytes,
            overflowSource,
            linkedSource.Token);

        try
        {
            if (stdinBytes is not null)
            {
                WriteStdinAsync(process.StandardInput.BaseStream, stdinBytes, linkedSource.Token).GetAwaiter().GetResult();
            }

            process.WaitForExitAsync(linkedSource.Token).GetAwaiter().GetResult();
            BoundedReadResult stdout = stdoutTask.GetAwaiter().GetResult();
            BoundedReadResult stderr = stderrTask.GetAwaiter().GetResult();

            if (stdout.LimitExceeded || stderr.LimitExceeded || overflowSource.IsCancellationRequested)
            {
                return new ProcessOutcome(false, null, "git_cliff_output_limit_exceeded");
            }

            if (stdout.InvalidEncoding || stderr.InvalidEncoding)
            {
                return new ProcessOutcome(false, null, "git_cliff_execution_failed");
            }

            if (process.ExitCode != 0)
            {
                return new ProcessOutcome(false, null, "git_cliff_exit_nonzero");
            }

            return new ProcessOutcome(true, stdout.Text, null);
        }
        catch (OperationCanceledException)
        {
            KillSafely(process);
            return overflowSource.IsCancellationRequested
                ? new ProcessOutcome(false, null, "git_cliff_output_limit_exceeded")
                : new ProcessOutcome(false, null, "git_cliff_timeout");
        }
        catch (IOException)
        {
            KillSafely(process);
            return new ProcessOutcome(false, null, "git_cliff_execution_failed");
        }
        finally
        {
            if (!process.HasExited)
            {
                KillSafely(process);
            }
        }
    }

    private static async Task WriteStdinAsync(Stream stdin, byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            await stdin.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Child process may have already exited with a non-zero exit code before draining stdin.
        }
        finally
        {
            stdin.Close();
        }
    }

    private static async Task<BoundedReadResult> ReadBoundedUtf8Async(
        Stream stream,
        int maxBytes,
        CancellationTokenSource overflowSource,
        CancellationToken cancellationToken)
    {
        using var bufferStream = new MemoryStream();
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
                    return new BoundedReadResult(null, LimitExceeded: true, InvalidEncoding: false);
                }

                bufferStream.Write(chunk, 0, read);
            }
        }
        catch (OperationCanceledException) when (overflowSource.IsCancellationRequested)
        {
            return new BoundedReadResult(null, LimitExceeded: true, InvalidEncoding: false);
        }

        try
        {
            string decoded = StrictUtf8.GetString(bufferStream.GetBuffer(), 0, (int)bufferStream.Length);
            return new BoundedReadResult(decoded, LimitExceeded: false, InvalidEncoding: false);
        }
        catch (DecoderFallbackException)
        {
            return new BoundedReadResult(null, LimitExceeded: false, InvalidEncoding: true);
        }
    }

    /// <summary>
    /// Hydrates only neutral empty serde defaults required by <c>git-cliff</c> 2.14.2 <c>--from-context -</c>
    /// in memory before writing to stdin, so persisted allowlisted <c>release-rendering-context.v1.json</c>
    /// artifacts never carry empty identity, link, or remote stubs.
    /// </summary>
    private static byte[]? TryBuildWireContextBytes(string renderingContextJson)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(renderingContextJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed is not JsonArray releases)
        {
            return null;
        }

        foreach (JsonNode? releaseNode in releases)
        {
            if (releaseNode is not JsonObject release)
            {
                return null;
            }

            HydrateReleaseWireDefaults(release);
        }

        return StrictUtf8.GetBytes(releases.ToJsonString());
    }

    private static void HydrateReleaseWireDefaults(JsonObject release)
    {
        release["submodule_commits"] ??= new JsonObject();
        foreach (string provider in RemoteProviders)
        {
            release[provider] ??= new JsonObject { ["contributors"] = new JsonArray() };
        }

        if (release["previous"] is JsonObject previous)
        {
            previous["commits"] ??= new JsonArray();
            HydrateReleaseWireDefaults(previous);
        }

        if (release["commits"] is not JsonArray commits)
        {
            return;
        }

        foreach (JsonNode? commitNode in commits)
        {
            if (commitNode is not JsonObject commit)
            {
                continue;
            }

            commit["links"] ??= new JsonArray();
            commit["author"] ??= CreateNeutralSignature();
            commit["committer"] ??= CreateNeutralSignature();
            foreach (string provider in RemoteProviders)
            {
                commit[provider] ??= new JsonObject
                {
                    ["pr_labels"] = new JsonArray(),
                    ["is_first_time"] = false,
                };
            }
        }
    }

    private static JsonObject CreateNeutralSignature() => new()
    {
        ["name"] = null,
        ["email"] = null,
        ["timestamp"] = 0,
    };

    private static bool IsJsonArray(string content)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(content);
            return doc.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

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

    private sealed record ProcessOutcome(bool IsValid, string? Stdout, string? Diagnostic);

    private sealed record BoundedReadResult(string? Text, bool LimitExceeded, bool InvalidEncoding);
}
