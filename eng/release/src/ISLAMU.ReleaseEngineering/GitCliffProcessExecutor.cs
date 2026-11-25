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
    private static class Diagnostics
    {
        public const string InputInvalid = "git_cliff_input_invalid";
        public const string InvalidContextJson = "git_cliff_invalid_context_json";
        public const string InvalidBumpVersion = "git_cliff_invalid_bump_version";
        public const string ConfigInvalid = "git_cliff_config_invalid";
        public const string WorkingDirectoryInvalid = "git_cliff_working_directory_invalid";
        public const string ToolUntrusted = "git_cliff_tool_untrusted";
        public const string ExecutionFailed = "git_cliff_execution_failed";
        public const string OutputLimitExceeded = "git_cliff_output_limit_exceeded";
        public const string ExitNonZero = "git_cliff_exit_nonzero";
        public const string Timeout = "git_cliff_timeout";
    }

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
        if (string.IsNullOrWhiteSpace(request.Tag) || request.Tag.Any(char.IsControl) || !IsValidOptionalRange(request.CommitRange))
        {
            return new GitCliffContextExportResult(false, null, Diagnostics.InputInvalid);
        }

        var arguments = new List<string> { "--context", "--tag", request.Tag };
        if (request.CommitRange is not null)
        {
            arguments.Add("--");
            arguments.Add(request.CommitRange);
        }

        ProcessOutcome outcome = Execute(request.Tool, request.RepositoryRoot, arguments, stdinBytes: null, maximumStdoutBytes);
        if (!outcome.IsValid || outcome.Stdout is null)
        {
            return new GitCliffContextExportResult(false, null, outcome.Diagnostic);
        }

        return IsJsonArray(outcome.Stdout)
            ? new GitCliffContextExportResult(true, outcome.Stdout, null)
            : new GitCliffContextExportResult(false, null, Diagnostics.InvalidContextJson);
    }

    public GitCliffRenderFromContextResult RenderFromContext(GitCliffRenderFromContextRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        byte[]? wirePayload = string.IsNullOrWhiteSpace(request.RenderingContextJson)
            ? null
            : TryBuildWireContextBytes(request.RenderingContextJson);
        if (wirePayload is null)
        {
            return new GitCliffRenderFromContextResult(false, null, Diagnostics.InputInvalid);
        }

        ProcessOutcome outcome = Execute(request.Tool, request.WorkingDirectory, ["--from-context", "-"], wirePayload, maximumStdoutBytes);
        return outcome.IsValid && outcome.Stdout is not null
            ? new GitCliffRenderFromContextResult(true, outcome.Stdout, null)
            : new GitCliffRenderFromContextResult(false, null, outcome.Diagnostic);
    }

    public GitCliffBumpVersionResult SuggestBumpedVersion(GitCliffBumpVersionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidOptionalRange(request.CommitRange))
        {
            return new GitCliffBumpVersionResult(false, null, null, Diagnostics.InputInvalid);
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
            Math.Min(maximumStdoutBytes, MaximumBumpStdoutBytes));
        if (!outcome.IsValid || outcome.Stdout is null)
        {
            return new GitCliffBumpVersionResult(false, null, null, outcome.Diagnostic);
        }

        string raw = outcome.Stdout.Trim();
        string normalized = raw.StartsWith('v') ? raw[1..] : raw;
        if (raw.Length == 0 ||
            raw.Contains('\n', StringComparison.Ordinal) ||
            raw.Contains('\r', StringComparison.Ordinal) ||
            !SemVerPattern.IsMatch(normalized))
        {
            return new GitCliffBumpVersionResult(false, null, null, Diagnostics.InvalidBumpVersion);
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
        string? setupDiagnostic = PrepareVerifiedStartInfo(
            tool,
            workingDirectory,
            additionalArguments,
            stdinBytes is not null,
            out ProcessStartInfo? startInfo);
        if (setupDiagnostic is not null)
        {
            return new ProcessOutcome(false, null, setupDiagnostic);
        }

        return RunProcessBounded(startInfo!, tool.Timeout, stdinBytes, stdoutLimitBytes);
    }

    private static string? PrepareVerifiedStartInfo(
        GitCliffToolConfiguration tool,
        string workingDirectory,
        IReadOnlyList<string> additionalArguments,
        bool redirectStdin,
        out ProcessStartInfo? startInfo)
    {
        startInfo = null;
        if (tool is null || tool.Timeout <= TimeSpan.Zero)
        {
            return Diagnostics.InputInvalid;
        }

        if (string.IsNullOrWhiteSpace(tool.ConfigPath) || !File.Exists(tool.ConfigPath) || IsReparsePoint(tool.ConfigPath))
        {
            return Diagnostics.ConfigInvalid;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory) || IsReparsePoint(workingDirectory))
        {
            return Diagnostics.WorkingDirectoryInvalid;
        }

        ToolchainVerificationResult verification = GitCliffToolchainStore.VerifyInstalled(
            tool.LockPath,
            tool.ToolDirectory,
            tool.Platform,
            tool.Timeout);
        if (!verification.IsValid || verification.Executable is null)
        {
            return Diagnostics.ToolUntrusted;
        }

        startInfo = GitCliffToolchainStore.CreateOfflineNoExecStartInfo(
            verification.Executable,
            Path.GetFullPath(tool.ConfigPath),
            Path.GetFullPath(workingDirectory),
            additionalArguments);
        if (redirectStdin)
        {
            startInfo.RedirectStandardInput = true;
            startInfo.StandardInputEncoding = StrictUtf8;
        }

        return null;
    }

    private ProcessOutcome RunProcessBounded(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        byte[]? stdinBytes,
        int stdoutLimitBytes)
    {
        using var process = new Process { StartInfo = startInfo };
        if (!TryStartProcess(process))
        {
            return new ProcessOutcome(false, null, Diagnostics.ExecutionFailed);
        }

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var overflowSource = new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token, overflowSource.Token);

        Task<BoundedReadResult> stdoutTask = ReadBoundedUtf8Async(process.StandardOutput.BaseStream, stdoutLimitBytes, overflowSource, linkedSource.Token);
        Task<BoundedReadResult> stderrTask = ReadBoundedUtf8Async(process.StandardError.BaseStream, maximumStderrBytes, overflowSource, linkedSource.Token);

        try
        {
            if (stdinBytes is not null)
            {
                WriteStdinAsync(process.StandardInput.BaseStream, stdinBytes, linkedSource.Token).GetAwaiter().GetResult();
            }

            process.WaitForExitAsync(linkedSource.Token).GetAwaiter().GetResult();
            return EvaluateProcessResults(process.ExitCode, stdoutTask.GetAwaiter().GetResult(), stderrTask.GetAwaiter().GetResult(), overflowSource.IsCancellationRequested);
        }
        catch (OperationCanceledException)
        {
            KillSafely(process);
            return new ProcessOutcome(false, null, overflowSource.IsCancellationRequested ? Diagnostics.OutputLimitExceeded : Diagnostics.Timeout);
        }
        catch (IOException)
        {
            KillSafely(process);
            return new ProcessOutcome(false, null, Diagnostics.ExecutionFailed);
        }
        finally
        {
            if (!process.HasExited)
            {
                KillSafely(process);
            }
        }
    }

    private static bool TryStartProcess(Process process)
    {
        try
        {
            return process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
        {
            return false;
        }
    }

    private static ProcessOutcome EvaluateProcessResults(
        int exitCode,
        BoundedReadResult stdout,
        BoundedReadResult stderr,
        bool overflowed)
    {
        if (stdout.LimitExceeded || stderr.LimitExceeded || overflowed)
        {
            return new ProcessOutcome(false, null, Diagnostics.OutputLimitExceeded);
        }

        if (stdout.InvalidEncoding || stderr.InvalidEncoding)
        {
            return new ProcessOutcome(false, null, Diagnostics.ExecutionFailed);
        }

        return exitCode == 0
            ? new ProcessOutcome(true, stdout.Text, null)
            : new ProcessOutcome(false, null, Diagnostics.ExitNonZero);
    }

    private static bool IsValidOptionalRange(string? commitRange) =>
        commitRange is null || (!string.IsNullOrWhiteSpace(commitRange) && !commitRange.Any(char.IsControl));

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

                await bufferStream.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
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

        foreach (JsonObject commit in commits.OfType<JsonObject>())
        {
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
