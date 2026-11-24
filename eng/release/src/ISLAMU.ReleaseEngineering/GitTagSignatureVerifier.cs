using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record GitTagVerificationRequest(
    string RepositoryRoot,
    string TagName,
    string AllowedSignersPath,
    string? ExpectedTargetOid = null,
    string? ExpectedEventSha = null,
    string? ExpectedSignerPrincipal = null,
    TimeSpan? Timeout = null);

public sealed record GitTagVerificationResult(
    bool IsValid,
    string? TagName,
    string? TagObjectId,
    string? TargetCommitOid,
    string? SignerPrincipal,
    string? Diagnostic);

/// <summary>
/// Verifies annotated SSH-signed Git release and baseline tags directly against an explicit allowed-signers file,
/// and reads bounded or root-inclusive commit ranges from Git history.
/// </summary>
public static class GitTagSignatureVerifier
{
    public const string AllowedSignersEnvironmentVariable = "ISLAMU_RELEASE_ALLOWED_SIGNERS";

    private const int MaximumGitOutputCharacters = 1_048_576;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(30);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ReleaseTagPattern = new("^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)(?:-(?:alpha|beta|rc)\\.(?:[1-9][0-9]*))?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex BaselineTagPattern = new("^changelog-baseline-[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GoodSignaturePattern = new("Good \\\"git\\\" signature for (?<principal>[^\\s]+)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string ResolveDefaultAllowedSignersPath(string repositoryRoot)
    {
        string? overridePath = Environment.GetEnvironmentVariable(AllowedSignersEnvironmentVariable);
        return !string.IsNullOrWhiteSpace(overridePath)
            ? Path.GetFullPath(overridePath)
            : Path.Combine(Path.GetFullPath(repositoryRoot), "eng", "release", "trust", "allowed-signers");
    }

    public static GitTagVerificationResult Verify(GitTagVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        TimeSpan timeout = request.Timeout ?? DefaultTimeout;
        if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
        {
            return Invalid("tag_timeout_invalid");
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryRoot) ||
            !Directory.Exists(request.RepositoryRoot) ||
            IsReparsePoint(request.RepositoryRoot))
        {
            return Invalid("tag_repository_invalid");
        }

        if (string.IsNullOrWhiteSpace(request.TagName) ||
            (!ReleaseTagPattern.IsMatch(request.TagName) && !BaselineTagPattern.IsMatch(request.TagName)))
        {
            return Invalid("tag_name_malformed");
        }

        if (request.ExpectedTargetOid is not null && !FullOidPattern.IsMatch(request.ExpectedTargetOid))
        {
            return Invalid("tag_expected_target_invalid");
        }

        if (request.ExpectedEventSha is not null && !FullOidPattern.IsMatch(request.ExpectedEventSha))
        {
            return Invalid("tag_expected_event_sha_invalid");
        }

        string allowedSignersPath;
        try
        {
            allowedSignersPath = Path.GetFullPath(request.AllowedSignersPath);
            if (!File.Exists(allowedSignersPath) || IsReparsePoint(allowedSignersPath))
            {
                return Invalid("tag_allowed_signers_missing");
            }

            bool hasActiveSigner = File.ReadLines(allowedSignersPath, StrictUtf8)
                .Select(line => line.Trim())
                .Any(line => line.Length > 0 && !line.StartsWith('#'));
            if (!hasActiveSigner)
            {
                return Invalid("tag_allowed_signers_empty");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            return Invalid("tag_allowed_signers_invalid");
        }

        string repoRoot = Path.GetFullPath(request.RepositoryRoot);
        string tagRef = $"refs/tags/{request.TagName}";

        if (!TryRunGit(repoRoot, timeout, out string objectType, out _, "cat-file", "-t", tagRef))
        {
            return Invalid("tag_missing");
        }

        if (!string.Equals(objectType.Trim(), "tag", StringComparison.Ordinal))
        {
            return Invalid("tag_not_annotated");
        }

        if (!TryRunGit(repoRoot, timeout, out string tagObjectIdRaw, out _, "rev-parse", "--verify", "--end-of-options", $"{tagRef}^{{object}}") ||
            !TryRunGit(repoRoot, timeout, out string targetCommitOidRaw, out _, "rev-parse", "--verify", "--end-of-options", $"{tagRef}^{{commit}}") ||
            !TryRunGit(repoRoot, timeout, out string tagObjectBody, out _, "cat-file", "-p", tagRef))
        {
            return Invalid("tag_object_unreadable");
        }

        string tagObjectId = tagObjectIdRaw.Trim();
        string targetCommitOid = targetCommitOidRaw.Trim();
        if (!FullOidPattern.IsMatch(tagObjectId) || !FullOidPattern.IsMatch(targetCommitOid))
        {
            return Invalid("tag_object_invalid");
        }

        if (!TryParseTagHeader(tagObjectBody, out string headerObjectOid, out string headerType, out string headerTagName))
        {
            return Invalid("tag_object_malformed");
        }

        if (!string.Equals(headerType, "commit", StringComparison.Ordinal) ||
            !string.Equals(headerObjectOid, targetCommitOid, StringComparison.Ordinal))
        {
            return Invalid("tag_target_mismatch");
        }

        if (!string.Equals(headerTagName, request.TagName, StringComparison.Ordinal))
        {
            return Invalid("tag_name_mismatch");
        }

        if (request.ExpectedTargetOid is not null &&
            !string.Equals(targetCommitOid, request.ExpectedTargetOid, StringComparison.Ordinal))
        {
            return Invalid("tag_target_mismatch");
        }

        if (request.ExpectedEventSha is not null &&
            !string.Equals(targetCommitOid, request.ExpectedEventSha, StringComparison.Ordinal))
        {
            return Invalid("tag_event_sha_mismatch");
        }

        if (!tagObjectBody.Contains("-----BEGIN SSH SIGNATURE-----", StringComparison.Ordinal))
        {
            return Invalid("tag_not_signed");
        }

        bool verified = TryRunGit(
            repoRoot,
            timeout,
            out string verifyStdout,
            out string verifyStderr,
            "-c", "gpg.format=ssh",
            "-c", $"gpg.ssh.allowedSignersFile={allowedSignersPath}",
            "verify-tag",
            tagRef);

        if (!verified)
        {
            return Invalid("tag_signature_invalid");
        }

        string combined = verifyStdout + "\n" + verifyStderr;
        Match principalMatch = GoodSignaturePattern.Match(combined);
        if (!principalMatch.Success)
        {
            return Invalid("tag_signature_invalid");
        }

        string principal = principalMatch.Groups["principal"].Value;
        if (request.ExpectedSignerPrincipal is not null &&
            !string.Equals(principal, request.ExpectedSignerPrincipal, StringComparison.Ordinal))
        {
            return Invalid("tag_signer_unauthorized");
        }

        return new GitTagVerificationResult(
            true,
            request.TagName,
            tagObjectId,
            targetCommitOid,
            principal,
            null);
    }

    public static IReadOnlyList<ReleaseCommit> ReadCommitRange(
        string repositoryRoot,
        string? previousExclusiveRef,
        string endRef,
        TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(endRef);

        string revisionSpec = string.IsNullOrWhiteSpace(previousExclusiveRef)
            ? endRef
            : $"{previousExclusiveRef}..{endRef}";

        if (!TryRunGit(
            Path.GetFullPath(repositoryRoot),
            timeout ?? DefaultTimeout,
            out string raw,
            out _,
            "log",
            "--reverse",
            "--format=%H%x00%B%x1e",
            revisionSpec))
        {
            throw new IOException($"Failed to read Git commit range '{revisionSpec}'.");
        }

        return raw.Split('\u001e', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.TrimStart('\n'))
            .Where(entry => entry.Length != 0)
            .Select(entry =>
            {
                int separator = entry.IndexOf('\0', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    throw new IOException("Malformed Git log entry.");
                }

                return new ReleaseCommit(entry[..separator], entry[(separator + 1)..].TrimEnd('\n'));
            })
            .ToArray();
    }

    public static int RunCli(string[] args, TextWriter output, string defaultRepositoryRoot, TimeSpan timeout)
    {
        string repositoryRoot = defaultRepositoryRoot;
        string? tagName = null;
        string? expectedTarget = null;
        string? expectedEventSha = null;
        string? allowedSigners = null;
        string? expectedSigner = null;

        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--repository" when index + 1 < args.Length:
                    repositoryRoot = args[++index];
                    break;
                case "--tag" when index + 1 < args.Length:
                    tagName = args[++index];
                    break;
                case "--target" when index + 1 < args.Length:
                    expectedTarget = args[++index];
                    break;
                case "--event-sha" when index + 1 < args.Length:
                    expectedEventSha = args[++index];
                    break;
                case "--allowed-signers" when index + 1 < args.Length:
                    allowedSigners = args[++index];
                    break;
                case "--signer" when index + 1 < args.Length:
                    expectedSigner = args[++index];
                    break;
                default:
                    output.WriteLine("invalid_arguments: verify-release-tag requires --tag <tag-name> [--target <oid>] [--event-sha <oid>] [--allowed-signers <path>] [--signer <principal>] [--repository <path>]");
                    return Program.UsageError;
            }
        }

        if (string.IsNullOrWhiteSpace(tagName))
        {
            output.WriteLine("invalid_arguments: verify-release-tag requires --tag <tag-name>");
            return Program.UsageError;
        }

        string resolvedAllowedSigners = allowedSigners ?? ResolveDefaultAllowedSignersPath(repositoryRoot);
        GitTagVerificationResult result = Verify(new GitTagVerificationRequest(
            repositoryRoot,
            tagName,
            resolvedAllowedSigners,
            expectedTarget,
            expectedEventSha,
            expectedSigner,
            timeout));

        if (!result.IsValid)
        {
            output.WriteLine($"verify_release_tag_failed: {result.Diagnostic}");
            return Program.ToolchainRejected;
        }

        output.WriteLine($"verified_release_tag: {result.TagName} target={result.TargetCommitOid} tag_object={result.TagObjectId} signer={result.SignerPrincipal}");
        return Program.Success;
    }

    private static bool TryParseTagHeader(
        string body,
        out string objectOid,
        out string targetType,
        out string tagName)
    {
        objectOid = string.Empty;
        targetType = string.Empty;
        tagName = string.Empty;

        string normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (string line in normalized.Split('\n'))
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("object ", StringComparison.Ordinal))
            {
                objectOid = line["object ".Length..].Trim();
            }
            else if (line.StartsWith("type ", StringComparison.Ordinal))
            {
                targetType = line["type ".Length..].Trim();
            }
            else if (line.StartsWith("tag ", StringComparison.Ordinal))
            {
                tagName = line["tag ".Length..].Trim();
            }
        }

        return FullOidPattern.IsMatch(objectOid) && targetType.Length > 0 && tagName.Length > 0;
    }

    private static bool TryRunGit(
        string repositoryRoot,
        TimeSpan timeout,
        out string stdout,
        out string stderr,
        params string[] arguments)
    {
        stdout = string.Empty;
        stderr = string.Empty;

        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ReleaseToolPaths.Git)
            {
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = StrictUtf8,
                StandardErrorEncoding = StrictUtf8,
            },
        };

        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        process.StartInfo.Environment["LC_ALL"] = "C";
        process.StartInfo.Environment["TZ"] = "UTC";

        process.StartInfo.ArgumentList.Add("--no-replace-objects");
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            using var timeoutSource = new CancellationTokenSource(timeout);
            Task<string> outTask = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
            Task<string> errTask = ReadBoundedAsync(process.StandardError, timeoutSource.Token);
            process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
            stdout = outTask.GetAwaiter().GetResult();
            stderr = errTask.GetAwaiter().GetResult();
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumGitOutputCharacters];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
            if (read == 0)
            {
                return new string(buffer, 0, count);
            }

            count += read;
        }

        if (await reader.ReadAsync(new char[1], cancellationToken) != 0)
        {
            throw new OperationCanceledException("Git output exceeded maximum allowed length.");
        }

        return new string(buffer);
    }

    private static bool IsReparsePoint(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static GitTagVerificationResult Invalid(string diagnostic) =>
        new(false, null, null, null, null, diagnostic);
}
