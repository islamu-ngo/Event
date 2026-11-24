using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record GitCliffRenderRequest(
    string ToolchainLockPath,
    string ToolBundlePath,
    string ConfigPath,
    byte[] NormalizedContext,
    string Platform,
    string IsolationRoot,
    TimeSpan Timeout);

public sealed record GitCliffRenderResult(bool IsValid, byte[]? Markdown, string? Diagnostic);

/// <summary>
/// Temporary Phase 1 renderer decoupled from retired trusted-bundle ceremony.
/// Replaced in Phase 5 by native <c>git-cliff</c> context export, allowlisted enrichment, and three-persona rendering.
/// </summary>
public static class GitCliffRenderer
{
    private const int MaximumDiagnosticCharacters = 4096;
    private const int MaximumOutputCharacters = ReleaseArtifactPolicy.MaximumDocumentUtf8Bytes;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex UrlPattern = new(@"(?:https?://|mailto:|www\.)\S+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex EmailOrHandlePattern = new(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}|(?<![\w@])@[A-Za-z0-9][A-Za-z0-9-]{0,38}(?![\w-])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex RawHtmlPattern = new(@"<\s*(?:!|/?[A-Za-z])[^>]*>", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions ContextOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly JsonSerializerOptions RendererContextOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static GitCliffRenderResult Render(GitCliffRenderRequest request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.ToolchainLockPath) ||
            string.IsNullOrWhiteSpace(request.ToolBundlePath) ||
            string.IsNullOrWhiteSpace(request.ConfigPath) ||
            !Path.IsPathFullyQualified(request.IsolationRoot) ||
            request.Timeout <= TimeSpan.Zero)
        {
            return Invalid("renderer_request_invalid");
        }

        if (!TryReadConfig(request.ConfigPath, out byte[]? configBytes))
        {
            return Invalid("renderer_config_invalid");
        }

        if (!TryReadNormalizedContext(request.NormalizedContext, out ReleaseContext? context))
        {
            return Invalid("renderer_context_not_normalized");
        }

        ToolchainVerificationResult verification = GitCliffToolchainStore.VerifyInstalled(
            request.ToolchainLockPath,
            request.ToolBundlePath,
            request.Platform,
            request.Timeout);
        if (!verification.IsValid || verification.Executable is null)
        {
            return Invalid("renderer_tool_untrusted");
        }

        string runDirectory = Path.Combine(request.IsolationRoot, $"git-cliff-{Guid.NewGuid():N}");
        Process? process = null;
        GitCliffRenderResult result;
        bool cleanupFailed = false;
        try
        {
            Directory.CreateDirectory(runDirectory);
            string contextPath = Path.Combine(runDirectory, "git-cliff-context.json");
            string configPath = Path.Combine(runDirectory, "cliff.toml");
            File.WriteAllBytes(contextPath, CreateRendererContext(context!));
            File.WriteAllBytes(configPath, configBytes!);

            ProcessStartInfo startInfo = GitCliffToolchainStore.CreateOfflineNoExecStartInfo(
                verification.Executable,
                configPath,
                runDirectory,
                ["--from-context", contextPath]);
            startInfo.Environment["GIT_CEILING_DIRECTORIES"] = runDirectory;

            process = new Process { StartInfo = startInfo };
            process.Start();
            using var timeoutSource = new CancellationTokenSource(request.Timeout);
            Task<string> standardOutput = ReadBoundedAsync(process.StandardOutput, MaximumOutputCharacters, timeoutSource.Token);
            Task<string> standardError = ReadBoundedAsync(process.StandardError, MaximumDiagnosticCharacters, timeoutSource.Token);
            Task.WhenAll(process.WaitForExitAsync(timeoutSource.Token), standardOutput, standardError).GetAwaiter().GetResult();
            string output = standardOutput.Result;
            string error = standardError.Result;

            if (process.ExitCode != 0 || error.Length != 0)
            {
                result = Invalid("renderer_process_failed");
            }
            else
            {
                byte[] outputBytes = StrictUtf8.GetBytes(output);
                ArtifactPolicyResult normalized = ReleaseArtifactPolicy.NormalizeText(output);
                if (!normalized.IsValid || !outputBytes.AsSpan().SequenceEqual(normalized.Bytes))
                {
                    result = Invalid("renderer_output_not_normalized");
                }
                else if (!IsSafeMarkdown(output))
                {
                    result = Invalid("renderer_output_restricted");
                }
                else
                {
                    result = new GitCliffRenderResult(true, outputBytes, null);
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (process is { HasExited: false })
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            result = Invalid("renderer_process_limit_exceeded");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception or DecoderFallbackException or JsonException)
        {
            result = Invalid("renderer_process_failed");
        }
        finally
        {
            try
            {
                if (process is { HasExited: false })
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                cleanupFailed = true;
            }

            process?.Dispose();
            try
            {
                if (Directory.Exists(runDirectory))
                {
                    Directory.Delete(runDirectory, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                cleanupFailed = true;
            }
        }

        return cleanupFailed ? Invalid("renderer_cleanup_failed") : result;
    }

    private static bool TryReadConfig(string configPath, out byte[]? configBytes)
    {
        configBytes = null;
        try
        {
            if (!File.Exists(configPath) || (File.GetAttributes(configPath) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(configPath);
            if (bytes.Length == 0 || bytes.Length > ReleaseArtifactPolicy.MaximumDocumentUtf8Bytes)
            {
                return false;
            }

            _ = StrictUtf8.GetString(bytes);
            configBytes = bytes;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryReadNormalizedContext(byte[] bytes, out ReleaseContext? context)
    {
        context = null;
        if (bytes.Length == 0 || bytes.Length > ReleaseArtifactPolicy.MaximumDocumentUtf8Bytes || bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
        {
            return false;
        }

        try
        {
            string json = StrictUtf8.GetString(bytes);
            context = JsonSerializer.Deserialize<ReleaseContext>(json, ContextOptions);
            if (context is null || context.SchemaVersion != 1 || context.Changes is null || context.Evidence?.Objects is null)
            {
                return false;
            }

            byte[] expected = StrictUtf8.GetBytes(JsonSerializer.Serialize(context, ContextOptions) + "\n");
            return bytes.AsSpan().SequenceEqual(expected) &&
                context.Changes.All(change => IsSafeContextText(change.Title) && IsSafeContextText(change.Summary));
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static byte[] CreateRendererContext(ReleaseContext context)
    {
        RendererCommit[] commits = context.Changes
            .GroupBy(change => ReleaseChangePresentation.Category(change) switch
            {
                0 => (Order: 0, Heading: "\u26a0\ufe0f Breaking Changes"),
                1 => (Order: 1, Heading: "\U0001f680 Features"),
                2 => (Order: 2, Heading: "\U0001f41b Bug Fixes"),
                3 => (Order: 3, Heading: "\u26a1 Performance"),
                _ => (Order: 4, Heading: "\U0001f527 Other Improvements"),
            })
            .OrderBy(group => group.Key.Order)
            .SelectMany(group => group.Select((change, index) => new RendererCommit(
                change.DisplayId,
                ReleaseArtifactPolicy.EscapeUntrustedMarkdown(change.Title).Text!,
                null,
                [],
                index == 0 ? $"\n### {group.Key.Heading}\n\n" : string.Empty,
                null,
                change.Breaking,
                change.Scope,
                [],
                RendererSignature.Empty,
                RendererSignature.Empty,
                false,
                false,
                RendererCommitStatistics.Empty,
                null,
                ProviderCommit.Empty,
                ProviderCommit.Empty,
                ProviderCommit.Empty,
                ProviderCommit.Empty,
                ProviderCommit.Empty,
                ReleaseArtifactPolicy.EscapeUntrustedMarkdown(change.Title).Text!))).ToArray();
        string commitId = context.Changes.Count == 0 ? context.Evidence.PreviousPublishedOid : context.Changes[^1].Oid;
        var release = new RendererRelease(
            context.Release.Version,
            null,
            commits,
            commitId,
            null,
            null,
            null,
            new RendererCommitRange(context.Evidence.BaseStableOid, commitId),
            new Dictionary<string, string>(),
            new RendererReleaseStatistics(commits.Length, 0, []),
            null,
            null,
            ProviderContributors.Empty,
            ProviderContributors.Empty,
            ProviderContributors.Empty,
            ProviderContributors.Empty,
            ProviderContributors.Empty);
        return JsonSerializer.SerializeToUtf8Bytes(new[] { release }, RendererContextOptions);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximumCharacters, CancellationToken cancellationToken)
    {
        var result = new char[maximumCharacters];
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
            throw new OperationCanceledException();
        }

        return new string(result);
    }

    private static bool IsSafeContextText(string value) =>
        !UrlPattern.IsMatch(value) &&
        !EmailOrHandlePattern.IsMatch(value) &&
        !RawHtmlPattern.IsMatch(value) &&
        ReleaseArtifactPolicy.EscapeUntrustedMarkdown(value).IsValid;

    private static bool IsSafeMarkdown(string value) =>
        !UrlPattern.IsMatch(value) &&
        !EmailOrHandlePattern.IsMatch(value) &&
        !RawHtmlPattern.IsMatch(value) &&
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line => ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid);

    private static GitCliffRenderResult Invalid(string diagnostic) => new(false, null, diagnostic);

    private sealed record RendererRelease(
        string Version,
        string? Message,
        RendererCommit[] Commits,
        string CommitId,
        long? Timestamp,
        string? Previous,
        string? Repository,
        RendererCommitRange CommitRange,
        Dictionary<string, string> SubmoduleCommits,
        RendererReleaseStatistics Statistics,
        Dictionary<string, string>? Extra,
        string? BumpType,
        ProviderContributors Github,
        ProviderContributors Gitlab,
        ProviderContributors Gitea,
        ProviderContributors Bitbucket,
        ProviderContributors AzureDevops);

    private sealed record RendererCommit(
        string Id,
        string Message,
        string? Body,
        string[] Footers,
        string Group,
        string? BreakingDescription,
        bool Breaking,
        string Scope,
        string[] Links,
        RendererSignature Author,
        RendererSignature Committer,
        bool Conventional,
        bool MergeCommit,
        RendererCommitStatistics Statistics,
        Dictionary<string, string>? Extra,
        ProviderCommit Github,
        ProviderCommit Gitlab,
        ProviderCommit Gitea,
        ProviderCommit Bitbucket,
        ProviderCommit AzureDevops,
        string RawMessage);

    private sealed record RendererSignature(string Name, string Email, long Timestamp)
    {
        public static RendererSignature Empty { get; } = new(string.Empty, string.Empty, 0);
    }

    private sealed record RendererCommitStatistics(int FilesChanged, int Additions, int Deletions)
    {
        public static RendererCommitStatistics Empty { get; } = new(0, 0, 0);
    }

    private sealed record RendererCommitRange(string From, string To);
    private sealed record RendererReleaseStatistics(int CommitCount, int ConventionalCommitCount, string[] Links);

    private sealed record ProviderCommit(string? Username, string? PrTitle, int? PrNumber, string[] PrLabels, bool IsFirstTime)
    {
        public static ProviderCommit Empty { get; } = new(null, null, null, [], false);
    }

    private sealed record ProviderContributors(string[] Contributors)
    {
        public static ProviderContributors Empty { get; } = new([]);
    }
}
