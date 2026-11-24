using System.Diagnostics;
using System.Text;

namespace ISLAMU.ReleaseEngineering;

public static class PrepareCommand
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int Run(string[] args, TextWriter output, string repositoryRoot, string platform, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (args.Length != 2)
        {
            output.WriteLine("invalid_arguments: prepare requires release directory");
            return Program.UsageError;
        }

        try
        {
            string root = ResolveDirectory(repositoryRoot);
            if (!string.Equals(RunGit(root, "rev-parse", "--show-toplevel").Trim(), root, PathComparison))
            {
                return Reject(output, "prepare_repository_root_invalid");
            }

            string releaseDirectory = ResolveChild(root, args[1], mustExist: true);
            string expectedReleaseParent = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases");
            if (!string.Equals(Path.GetDirectoryName(releaseDirectory), expectedReleaseParent, PathComparison))
            {
                return Reject(output, "prepare_release_path_invalid");
            }

            string releasePath = ResolveChild(releaseDirectory, "release.yaml", mustExist: true);
            string summaryPath = ResolveChild(releaseDirectory, "summary.md", mustExist: true);
            string releaseYaml = ReadNormalizedText(releasePath);
            byte[] summaryBytes = File.ReadAllBytes(summaryPath);
            if (!IsValidSummary(summaryBytes))
            {
                return Reject(output, "prepare_summary_restricted");
            }

            ReleaseInputValidationResult descriptorOnly = ReleaseInputPolicy.Validate(releaseYaml, [], []);
            if (!descriptorOnly.IsValid || descriptorOnly.Descriptor is null)
            {
                return Reject(output, "prepare_release_input_invalid");
            }

            ReleaseDescriptor descriptor = descriptorOnly.Descriptor;
            string? pinnedObjectsDiagnostic = ValidatePinnedObjects(root, descriptor, timeout, out GitTagVerificationResult? verifiedBaseline);
            if (pinnedObjectsDiagnostic is not null)
            {
                return Reject(output, pinnedObjectsDiagnostic);
            }

            IReadOnlyList<ReleaseCommit> commits = GitTagSignatureVerifier.ReadCommitRange(root, descriptor.PreviousPublishedTag, "HEAD", timeout);
            string[] rangeOids = commits.Select(commit => commit.Oid).ToArray();
            ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
            ChangeIdRenameLoadResult renameResult = ChangeIdRenamePolicy.Load(root);
            if (!renameResult.IsValid)
            {
                return Reject(output, renameResult.Diagnostics[0]);
            }

            string[] linkedChangeIds = commits
                .Select(commit => ChangeIdRenamePolicy.Evaluate(commit, policy, renameResult.Renames).ChangeId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string fragmentDirectory = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "changes");
            string[] fragments = Directory.Exists(fragmentDirectory)
                ? Directory.EnumerateFiles(fragmentDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
                    .Where(path => linkedChangeIds.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .Select(ReadNormalizedText)
                    .ToArray()
                : [];
            if (fragments.Length != linkedChangeIds.Length)
            {
                return Reject(output, "prepare_fragment_missing");
            }

            ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(releaseYaml, fragments, []);
            ReleaseContextValidationResult context = ReleaseContextPolicy.Build(
                input,
                commits,
                policy,
                verifiedBaselineRef: verifiedBaseline?.TagName,
                verifiedBaselineOid: verifiedBaseline?.TargetCommitOid,
                changeIdRenames: renameResult.Renames);
            if (!context.IsValid || context.Json is null)
            {
                return Reject(output, "prepare_context_invalid");
            }

            string contextJson = context.Json;
            string lockPath = Path.Combine(root, "eng", "release", "toolchain.lock.json");
            string configPath = Path.Combine(root, "eng", "release", "cliff.toml");
            string toolDirectory = GitCliffToolchainStore.ResolveConfiguredDirectory(
                lockPath,
                Environment.GetEnvironmentVariable(GitCliffToolchainStore.DefaultBundleEnvironmentVariable),
                platform);

            var renderRequest = new GitCliffRenderRequest(
                lockPath,
                toolDirectory,
                configPath,
                StrictUtf8.GetBytes(contextJson),
                platform,
                Path.Combine(Path.GetTempPath(), "islamu-release-renderer"),
                timeout);
            ReleasePreparationResult result = ReleasePreparation.Prepare(new ReleasePreparationRequest(
                releaseDirectory,
                input,
                context,
                summaryBytes,
                rangeOids,
                renderRequest));
            if (!result.IsValid)
            {
                return Reject(output, result.Diagnostic!);
            }

            string contextPath = Path.Combine(releaseDirectory, "release-context.v1.json");
            byte[] contextBytes = StrictUtf8.GetBytes(contextJson);
            if (File.Exists(contextPath))
            {
                if (!File.ReadAllBytes(contextPath).AsSpan().SequenceEqual(contextBytes))
                {
                    return Reject(output, "prepare_generated_file_unexpected");
                }
            }
            else
            {
                WriteAtomic(contextPath, contextBytes);
            }

            output.Write(result.CommitMessage);
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            return Reject(output, "prepare_input_invalid");
        }
    }

    private static string? ValidatePinnedObjects(
        string repositoryRoot,
        ReleaseDescriptor descriptor,
        TimeSpan timeout,
        out GitTagVerificationResult? verifiedBaseline)
    {
        verifiedBaseline = null;
        if (ReleaseInputPolicy.IsBaselineRef(descriptor.BaseStableTag) || ReleaseInputPolicy.IsBaselineRef(descriptor.PreviousPublishedTag))
        {
            if (descriptor.BaseStableTag != descriptor.PreviousPublishedTag ||
                descriptor.ReleaseRange.BaseRef != descriptor.BaseStableTag ||
                descriptor.ReleaseRange.PreviousRef != descriptor.PreviousPublishedTag ||
                descriptor.ReleaseRange.BaseOid != descriptor.ReleaseRange.PreviousOid)
            {
                return "prepare_release_range_moved";
            }

            string allowedSigners = GitTagSignatureVerifier.ResolveDefaultAllowedSignersPath(repositoryRoot);
            GitTagVerificationResult baselineResult = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
                repositoryRoot,
                descriptor.BaseStableTag,
                allowedSigners,
                ExpectedTargetOid: descriptor.ReleaseRange.BaseOid,
                Timeout: timeout));
            if (!baselineResult.IsValid)
            {
                return baselineResult.Diagnostic ?? "prepare_baseline_tag_invalid";
            }

            verifiedBaseline = baselineResult;
            GitReleaseValidationResult git = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
                repositoryRoot,
                descriptor.Line,
                descriptor.Version,
                $"refs/tags/{descriptor.BaseStableTag}",
                $"refs/tags/{descriptor.PreviousPublishedTag}",
                RunGit(repositoryRoot, "rev-parse", "HEAD^{commit}").Trim()));
            return git.IsValid ? null : git.Diagnostics.Count == 0 ? "prepare_release_range_moved" : git.Diagnostics[0];
        }

        string baseOid = RunGit(repositoryRoot, "rev-parse", $"{descriptor.BaseStableTag}^{{commit}}").Trim();
        string previousOid = RunGit(repositoryRoot, "rev-parse", $"{descriptor.PreviousPublishedTag}^{{commit}}").Trim();
        string headOid = RunGit(repositoryRoot, "rev-parse", "HEAD^{commit}").Trim();

        return string.Equals(descriptor.ReleaseRange.BaseOid, baseOid, StringComparison.Ordinal) &&
            string.Equals(descriptor.ReleaseRange.PreviousOid, previousOid, StringComparison.Ordinal) &&
            !string.Equals(headOid, previousOid, StringComparison.Ordinal)
            ? null
            : "prepare_release_range_moved";
    }

    private static bool IsValidSummary(byte[] bytes)
    {
        try
        {
            string decoded = StrictUtf8.GetString(bytes);
            ArtifactPolicyResult normalized = ReleaseArtifactPolicy.NormalizeText(decoded);
            string summary = decoded.TrimEnd('\n');
            return normalized.IsValid && normalized.Bytes is not null && bytes.AsSpan().SequenceEqual(normalized.Bytes) &&
                !string.IsNullOrWhiteSpace(summary) &&
                !summary.Contains("generated-region", StringComparison.OrdinalIgnoreCase) &&
                !summary.Contains("restricted-details", StringComparison.OrdinalIgnoreCase) &&
                !summary.Split('\n').Any(line => line.TrimStart().StartsWith('#') || !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static string RunGit(string repositoryRoot, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(ReleaseToolPaths.Git)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = repositoryRoot,
            },
        };
        string nullDevice = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = nullDevice;
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"core.hooksPath={nullDevice}");
        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(5)) || process.ExitCode != 0 || standardError.Length != 0)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new IOException();
        }

        return standardOutput;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        string temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string ReadNormalizedText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = StrictUtf8.GetString(bytes);
        ArtifactPolicyResult normalized = ReleaseArtifactPolicy.NormalizeText(text);
        if (!normalized.IsValid || normalized.Bytes is null || !bytes.AsSpan().SequenceEqual(normalized.Bytes))
        {
            throw new IOException();
        }

        return text;
    }

    private static string ResolveDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Path.IsPathFullyQualified(fullPath) || !Directory.Exists(fullPath) || IsLink(fullPath))
        {
            throw new IOException();
        }

        return fullPath;
    }

    private static string ResolveChild(string root, string path, bool mustExist)
    {
        string fullPath = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(root, path));
        string relative = Path.GetRelativePath(root, fullPath);
        if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relative) ||
            (mustExist && !File.Exists(fullPath) && !Directory.Exists(fullPath)) || HasLinkInPath(root, fullPath))
        {
            throw new IOException();
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsLink(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool HasLinkInPath(string root, string path)
    {
        string current = root;
        foreach (string segment in Path.GetRelativePath(root, path).Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsLink(current))
            {
                return true;
            }
        }

        return false;
    }

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine($"prepare_failed: {diagnostic}");
        return Program.ToolchainRejected;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
