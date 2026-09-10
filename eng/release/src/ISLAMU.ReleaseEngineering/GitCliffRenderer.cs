using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record GitCliffRenderRequest(
    VerifiedTrustedBundle TrustedBundle,
    byte[] CanonicalContext,
    string Platform,
    string IsolationRoot,
    TimeSpan Timeout);

public sealed record GitCliffRenderResult(bool IsValid, byte[]? Markdown, string? Diagnostic);

internal static class PresentationConfigGrammar
{
    private static readonly Regex TemplateVariable = new(@"{{\s*([^}]+?)\s*}}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TemplateBlock = new(@"{%\s*([^%]+?)\s*%}", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly HashSet<string> AllowedVariables = new(StringComparer.Ordinal)
    {
        "version",
        "commit.group",
        "commit.scope",
        "commit.message",
        "commit.id",
    };

    public static bool IsValid(string config)
    {
        string[] lines = config.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int index = SkipTrivia(lines, 0);
        if (index >= lines.Length || lines[index] != "[changelog]") return false;
        index = SkipTrivia(lines, index + 1);
        if (index >= lines.Length || lines[index] != "body = \"\"\"") return false;
        index++;

        bool sawLoopStart = false;
        bool sawLoopEnd = false;
        while (index < lines.Length && lines[index] != "\"\"\"")
        {
            if (!IsAllowedTemplateLine(lines[index], ref sawLoopStart, ref sawLoopEnd)) return false;
            index++;
        }

        if (index >= lines.Length || !sawLoopStart || !sawLoopEnd) return false;
        index = SkipTrivia(lines, index + 1);
        if (index >= lines.Length || lines[index] != "trim = true") return false;
        index = SkipTrivia(lines, index + 1);
        if (index >= lines.Length || lines[index] != "render_always = true") return false;
        index = SkipTrivia(lines, index + 1);
        return index >= lines.Length;
    }

    private static int SkipTrivia(string[] lines, int index)
    {
        while (index < lines.Length && (lines[index].Length == 0 || lines[index].StartsWith('#')))
        {
            index++;
        }

        return index;
    }

    private static bool IsAllowedTemplateLine(string line, ref bool sawLoopStart, ref bool sawLoopEnd)
    {
        if (line.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("https://", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("exec", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("processor", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("provider", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("remote", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("parser", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("bump", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("tag", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("range", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (Match match in TemplateVariable.Matches(line))
        {
            if (!AllowedVariables.Contains(match.Groups[1].Value.Trim())) return false;
        }

        foreach (Match match in TemplateBlock.Matches(line))
        {
            string block = match.Groups[1].Value.Trim();
            if (block == "for commit in commits")
            {
                sawLoopStart = true;
            }
            else if (block == "endfor")
            {
                sawLoopEnd = true;
            }
            else
            {
                return false;
            }
        }

        return true;
    }
}

public static class GitCliffRenderer
{
    private const int MaximumDiagnosticCharacters = 4096;
    private const int MaximumOutputCharacters = CanonicalArtifactPolicy.MaximumDocumentUtf8Bytes;
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
            request.TrustedBundle is null ||
            !TryResolveTrustedRoot(request.TrustedBundle.Root, out string? trustedRoot) ||
            !string.Equals(trustedRoot, request.TrustedBundle.Root, PathComparison) ||
            !IsTrustedChildPath(request.TrustedBundle.ToolchainLockPath, trustedRoot) ||
            !IsTrustedChildPath(request.TrustedBundle.ConfigPath, trustedRoot) ||
            !Path.IsPathFullyQualified(request.IsolationRoot) ||
            request.Timeout <= TimeSpan.Zero)
        {
            return Invalid("renderer_request_invalid");
        }

        if (!TryReadTrustedConfig(request, out byte[]? configBytes, out string? configDiagnostic))
        {
            return Invalid(configDiagnostic!);
        }

        if (!TryReadCanonicalContext(request.CanonicalContext, out ReleaseContext? context))
        {
            return Invalid("renderer_context_not_canonical");
        }

        using var verificationOutput = new StringWriter();
        int verification = Program.Run(
            ["verify-tools"],
            verificationOutput,
            request.TrustedBundle.ToolchainLockPath,
            request.TrustedBundle.Root,
            request.Platform,
            request.Timeout);
        if (verification != Program.Success)
        {
            return Invalid("renderer_tool_untrusted");
        }

        if (!TryResolveVerifiedExecutable(request, out string? executableName, out byte[]? executableBytes))
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
            string executablePath = Path.Combine(runDirectory, executableName!);
            File.WriteAllBytes(contextPath, CreateRendererContext(context!));
            File.WriteAllBytes(configPath, configBytes!);
            File.WriteAllBytes(executablePath, executableBytes!);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(executablePath, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }

            process = CreateProcess(executablePath, configPath, contextPath, runDirectory);
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
                CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeText(output);
                if (!canonical.IsValid || !outputBytes.AsSpan().SequenceEqual(canonical.Bytes))
                {
                    result = Invalid("renderer_output_not_canonical");
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
            catch (IOException)
            {
                cleanupFailed = true;
            }
            catch (UnauthorizedAccessException)
            {
                cleanupFailed = true;
            }
        }

        return cleanupFailed ? Invalid("renderer_cleanup_failed") : result;
    }

    private static bool TryReadTrustedConfig(GitCliffRenderRequest request, out byte[]? configBytes, out string? diagnostic)
    {
        configBytes = null;
        diagnostic = null;
        if (!IsSafeRegularFile(request.TrustedBundle.ConfigPath) || request.TrustedBundle.ConfigDigest.Length != 64)
        {
            diagnostic = "renderer_config_invalid";
            return false;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(request.TrustedBundle.ConfigPath);
            byte[] expected = Convert.FromHexString(request.TrustedBundle.ConfigDigest);
            byte[] actual = SHA256.HashData(bytes);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                diagnostic = "renderer_config_digest_mismatch";
                return false;
            }

            if (!PresentationConfigGrammar.IsValid(StrictUtf8.GetString(bytes)))
            {
                diagnostic = "renderer_config_not_presentation_only";
                return false;
            }

            configBytes = bytes;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or DecoderFallbackException)
        {
            diagnostic = "renderer_config_invalid";
            return false;
        }
    }

    private static bool TryReadCanonicalContext(byte[] bytes, out ReleaseContext? context)
    {
        context = null;
        if (bytes.Length == 0 || bytes.Length > CanonicalArtifactPolicy.MaximumDocumentUtf8Bytes || bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble))
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

    private static bool TryResolveVerifiedExecutable(GitCliffRenderRequest request, out string? executableName, out byte[]? executableBytes)
    {
        executableName = null;
        executableBytes = null;
        try
        {
            if (!IsSafeRegularFile(request.TrustedBundle.ToolchainLockPath))
            {
                return false;
            }

            byte[] lockBytes = File.ReadAllBytes(request.TrustedBundle.ToolchainLockPath);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(lockBytes)), request.TrustedBundle.ToolchainLockDigest, StringComparison.Ordinal))
            {
                return false;
            }

            using JsonDocument document = JsonDocument.Parse(lockBytes);
            JsonElement[] platforms = document.RootElement.GetProperty("platforms").EnumerateArray()
                .Where(item => item.GetProperty("platform").GetString() == request.Platform)
                .Take(2)
                .ToArray();
            if (platforms.Length != 1)
            {
                return false;
            }

            string executable = platforms[0].GetProperty("executable").GetString() ?? string.Empty;
            string digest = platforms[0].GetProperty("executableSha256").GetString() ?? string.Empty;
            if (Path.GetFileName(executable) != executable || digest.Length != 64)
            {
                return false;
            }

            string executablePath = Path.Combine(request.TrustedBundle.Root, executable);
            if (!IsSafeRegularFile(executablePath))
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(executablePath);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), digest, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            executableName = executable;
            executableBytes = bytes;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static byte[] CreateRendererContext(ReleaseContext context)
    {
        RendererCommit[] commits = context.Changes
            .GroupBy(change => change.Breaking
                ? (Order: 0, Heading: "\u26a0\ufe0f Breaking Changes")
                : change.Type switch
                {
                    "feat" => (Order: 1, Heading: "\U0001f680 Features"),
                    "fix" => (Order: 2, Heading: "\U0001f41b Bug Fixes"),
                    "perf" => (Order: 3, Heading: "\u26a1 Performance"),
                    _ => (Order: 4, Heading: "\U0001f527 Other Improvements"),
                })
            .OrderBy(group => group.Key.Order)
            .SelectMany(group => group.Select((change, index) => new RendererCommit(
                change.DisplayId,
                change.Title,
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
                change.Title))).ToArray();
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

    private static bool TryResolveTrustedRoot(string path, out string? trustedRoot)
    {
        trustedRoot = null;
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            trustedRoot = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsTrustedChildPath(string path, string? trustedRoot)
    {
        if (trustedRoot is null || !Path.IsPathFullyQualified(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            string relative = Path.GetRelativePath(trustedRoot, fullPath);
            return relative.Length != 0 &&
                relative != "." &&
                !relative.StartsWith("..", StringComparison.Ordinal) &&
                !Path.IsPathFullyQualified(relative);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsSafeRegularFile(string path)
    {
        try
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            {
                return true;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("/usr/bin/stat")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            process.StartInfo.ArgumentList.Add(OperatingSystem.IsLinux() ? "-c" : "-f");
            process.StartInfo.ArgumentList.Add(OperatingSystem.IsLinux() ? "%h" : "%l");
            process.StartInfo.ArgumentList.Add(path);
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            return process.WaitForExit(TimeSpan.FromSeconds(5)) &&
                process.ExitCode == 0 &&
                int.TryParse(output.Trim(), out int count) &&
                count == 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Process CreateProcess(string executablePath, string configPath, string contextPath, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo(executablePath)
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
        startInfo.ArgumentList.Add("--from-context");
        startInfo.ArgumentList.Add(contextPath);
        startInfo.ArgumentList.Add("--offline");
        startInfo.ArgumentList.Add("--no-exec");
        startInfo.Environment.Clear();
        foreach ((string name, string value) in CanonicalArtifactPolicy.CreateDeterministicEnvironment(workingDirectory))
        {
            startInfo.Environment.Add(name, value);
        }
        startInfo.Environment.Add("GIT_CEILING_DIRECTORIES", workingDirectory);

        return new Process { StartInfo = startInfo };
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
        CanonicalArtifactPolicy.EscapeUntrustedMarkdown(value).IsValid;

    private static bool IsSafeMarkdown(string value) =>
        !UrlPattern.IsMatch(value) &&
        !EmailOrHandlePattern.IsMatch(value) &&
        !RawHtmlPattern.IsMatch(value) &&
        value.Split('\n', StringSplitOptions.RemoveEmptyEntries).All(line => CanonicalArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid);

    private static GitCliffRenderResult Invalid(string diagnostic) => new(false, null, diagnostic);
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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
