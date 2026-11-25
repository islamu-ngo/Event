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

    private sealed record TagObjectMetadata(string TagObjectId, string TargetCommitOid, string TagObjectBody);

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
        string? requestError = ValidateRequest(request, timeout, out string allowedSignersPath);
        if (requestError is not null)
        {
            return Invalid(requestError);
        }

        string repoRoot = Path.GetFullPath(request.RepositoryRoot);
        string tagRef = $"refs/tags/{request.TagName}";

        string? tagError = ReadAndValidateTagObject(repoRoot, tagRef, request, timeout, out TagObjectMetadata? tagMetadata);
        if (tagError is not null)
        {
            return Invalid(tagError);
        }

        string? signatureError = VerifySshSignature(
            repoRoot,
            tagRef,
            allowedSignersPath,
            request.ExpectedSignerPrincipal,
            timeout,
            out string principal);
        if (signatureError is not null)
        {
            return Invalid(signatureError);
        }

        return new GitTagVerificationResult(
            true,
            request.TagName,
            tagMetadata!.TagObjectId,
            tagMetadata.TargetCommitOid,
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
            .Select(ParseCommitEntry)
            .ToArray();
    }

    public static int RunCli(string[] args, TextWriter output, string defaultRepositoryRoot, TimeSpan timeout)
    {
        if (!TryParseCliArguments(args, defaultRepositoryRoot, output, out GitTagVerificationRequest? request))
        {
            return Program.UsageError;
        }

        GitTagVerificationResult result = Verify(request! with { Timeout = timeout });
        if (!result.IsValid)
        {
            output.WriteLine($"verify_release_tag_failed: {result.Diagnostic}");
            return Program.ToolchainRejected;
        }

        output.WriteLine($"verified_release_tag: {result.TagName} target={result.TargetCommitOid} tag_object={result.TagObjectId} signer={result.SignerPrincipal}");
        return Program.Success;
    }

    private static string? ValidateRequest(GitTagVerificationRequest request, TimeSpan timeout, out string allowedSignersPath)
    {
        allowedSignersPath = string.Empty;
        if (timeout <= TimeSpan.Zero || timeout > MaximumTimeout)
        {
            return "tag_timeout_invalid";
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryRoot) ||
            !Directory.Exists(request.RepositoryRoot) ||
            IsReparsePoint(request.RepositoryRoot))
        {
            return "tag_repository_invalid";
        }

        if (string.IsNullOrWhiteSpace(request.TagName) ||
            (!ReleaseTagPattern.IsMatch(request.TagName) && !BaselineTagPattern.IsMatch(request.TagName)))
        {
            return "tag_name_malformed";
        }

        if (request.ExpectedTargetOid is not null && !FullOidPattern.IsMatch(request.ExpectedTargetOid))
        {
            return "tag_expected_target_invalid";
        }

        if (request.ExpectedEventSha is not null && !FullOidPattern.IsMatch(request.ExpectedEventSha))
        {
            return "tag_expected_event_sha_invalid";
        }

        return ValidateAllowedSignersFile(request.AllowedSignersPath, out allowedSignersPath);
    }

    private static string? ValidateAllowedSignersFile(string path, out string allowedSignersPath)
    {
        allowedSignersPath = string.Empty;
        try
        {
            allowedSignersPath = Path.GetFullPath(path);
            if (!File.Exists(allowedSignersPath) || IsReparsePoint(allowedSignersPath))
            {
                return "tag_allowed_signers_missing";
            }

            bool hasActiveSigner = File.ReadLines(allowedSignersPath, StrictUtf8)
                .Select(line => line.Trim())
                .Any(line => line.Length > 0 && !line.StartsWith('#'));
            return hasActiveSigner ? null : "tag_allowed_signers_empty";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            return "tag_allowed_signers_invalid";
        }
    }

    private static string? ReadAndValidateTagObject(
        string repoRoot,
        string tagRef,
        GitTagVerificationRequest request,
        TimeSpan timeout,
        out TagObjectMetadata? metadata)
    {
        metadata = null;
        if (!TryRunGit(repoRoot, timeout, out string objectType, out _, "cat-file", "-t", tagRef))
        {
            return "tag_missing";
        }

        if (!string.Equals(objectType.Trim(), "tag", StringComparison.Ordinal))
        {
            return "tag_not_annotated";
        }

        if (!TryRunGit(repoRoot, timeout, out string tagObjectIdRaw, out _, "rev-parse", "--verify", "--end-of-options", $"{tagRef}^{{object}}") ||
            !TryRunGit(repoRoot, timeout, out string targetCommitOidRaw, out _, "rev-parse", "--verify", "--end-of-options", $"{tagRef}^{{commit}}") ||
            !TryRunGit(repoRoot, timeout, out string tagObjectBody, out _, "cat-file", "-p", tagRef))
        {
            return "tag_object_unreadable";
        }

        string tagObjectId = tagObjectIdRaw.Trim();
        string targetCommitOid = targetCommitOidRaw.Trim();
        if (!FullOidPattern.IsMatch(tagObjectId) || !FullOidPattern.IsMatch(targetCommitOid))
        {
            return "tag_object_invalid";
        }

        string? headerError = ValidateTagHeaderBindings(tagObjectBody, targetCommitOid, request);
        if (headerError is not null)
        {
            return headerError;
        }

        metadata = new TagObjectMetadata(tagObjectId, targetCommitOid, tagObjectBody);
        return null;
    }

    private static string? ValidateTagHeaderBindings(string tagObjectBody, string targetCommitOid, GitTagVerificationRequest request)
    {
        if (!TryParseTagHeader(tagObjectBody, out string headerObjectOid, out string headerType, out string headerTagName))
        {
            return "tag_object_malformed";
        }

        if (!string.Equals(headerType, "commit", StringComparison.Ordinal) ||
            !string.Equals(headerObjectOid, targetCommitOid, StringComparison.Ordinal) ||
            (request.ExpectedTargetOid is not null && !string.Equals(targetCommitOid, request.ExpectedTargetOid, StringComparison.Ordinal)))
        {
            return "tag_target_mismatch";
        }

        if (!string.Equals(headerTagName, request.TagName, StringComparison.Ordinal))
        {
            return "tag_name_mismatch";
        }

        if (request.ExpectedEventSha is not null &&
            !string.Equals(targetCommitOid, request.ExpectedEventSha, StringComparison.Ordinal))
        {
            return "tag_event_sha_mismatch";
        }

        return tagObjectBody.Contains("-----BEGIN SSH SIGNATURE-----", StringComparison.Ordinal)
            ? null
            : "tag_not_signed";
    }

    private static string? VerifySshSignature(
        string repoRoot,
        string tagRef,
        string allowedSignersPath,
        string? expectedSignerPrincipal,
        TimeSpan timeout,
        out string principal)
    {
        principal = string.Empty;
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
            return "tag_signature_invalid";
        }

        Match principalMatch = GoodSignaturePattern.Match(verifyStdout + "\n" + verifyStderr);
        if (!principalMatch.Success)
        {
            return "tag_signature_invalid";
        }

        principal = principalMatch.Groups["principal"].Value;
        return expectedSignerPrincipal is not null && !string.Equals(principal, expectedSignerPrincipal, StringComparison.Ordinal)
            ? "tag_signer_unauthorized"
            : null;
    }

    private static bool TryParseCliArguments(
        string[] args,
        string defaultRepositoryRoot,
        TextWriter output,
        out GitTagVerificationRequest? request)
    {
        request = null;
        string repositoryRoot = defaultRepositoryRoot;
        string? tagName = null;
        string? expectedTarget = null;
        string? expectedEventSha = null;
        string? allowedSigners = null;
        string? expectedSigner = null;

        var reader = new CliArgumentReader(args);
        while (reader.TryReadNext(out string token))
        {
            if (token == "--repository" && reader.TryReadNext(out string repo))
            {
                repositoryRoot = repo;
            }
            else if (token == "--tag" && reader.TryReadNext(out string tag))
            {
                tagName = tag;
            }
            else if (token == "--target" && reader.TryReadNext(out string target))
            {
                expectedTarget = target;
            }
            else if (token == "--event-sha" && reader.TryReadNext(out string sha))
            {
                expectedEventSha = sha;
            }
            else if (token == "--allowed-signers" && reader.TryReadNext(out string signers))
            {
                allowedSigners = signers;
            }
            else if (token == "--signer" && reader.TryReadNext(out string signer))
            {
                expectedSigner = signer;
            }
            else
            {
                output.WriteLine("invalid_arguments: verify-release-tag requires --tag <tag-name> [--target <oid>] [--event-sha <oid>] [--allowed-signers <path>] [--signer <principal>] [--repository <path>]");
                return false;
            }
        }

        if (string.IsNullOrWhiteSpace(tagName))
        {
            output.WriteLine("invalid_arguments: verify-release-tag requires --tag <tag-name>");
            return false;
        }

        request = new GitTagVerificationRequest(
            repositoryRoot,
            tagName,
            allowedSigners ?? ResolveDefaultAllowedSignersPath(repositoryRoot),
            expectedTarget,
            expectedEventSha,
            expectedSigner);
        return true;
    }

    private static ReleaseCommit ParseCommitEntry(string entry)
    {
        int separator = entry.IndexOf('\0', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new IOException("Malformed Git log entry.");
        }

        return new ReleaseCommit(entry[..separator], entry[(separator + 1)..].TrimEnd('\n'));
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

        foreach (string line in body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
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
