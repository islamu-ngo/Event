using System.Diagnostics;
using System.Text;

namespace ISLAMU.ReleaseEngineering;

public static class PrepareCommand
{
    private const string UnexpectedGeneratedFileDiagnostic = "prepare_generated_file_unexpected";
    private const string RangeMovedDiagnostic = "prepare_release_range_moved";
    private const string GitRevParseCommand = "rev-parse";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int Run(
        string[] args,
        TextWriter output,
        string repositoryRoot,
        string platform,
        TimeSpan timeout,
        IGitCliffExecutor? gitCliffExecutor = null,
        IGitObjectReader? gitObjectReader = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (!TryParseArgs(args, out string? releaseDirectoryArg, out string? imageManifestPath))
        {
            output.WriteLine("invalid_arguments: prepare requires <release-directory> [--image-manifest <path>]");
            return Program.UsageError;
        }

        try
        {
            return ExecutePreparedRelease(
                output,
                repositoryRoot,
                releaseDirectoryArg!,
                imageManifestPath,
                platform,
                timeout,
                gitCliffExecutor ?? GitCliffProcessExecutor.Default,
                gitObjectReader);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException)
        {
            return Reject(output, "prepare_input_invalid");
        }
    }

    private static int ExecutePreparedRelease(
        TextWriter output,
        string repositoryRoot,
        string releaseDirectoryArg,
        string? imageManifestPath,
        string platform,
        TimeSpan timeout,
        IGitCliffExecutor executor,
        IGitObjectReader? gitObjectReader)
    {
        string? layoutDiagnostic = ResolveAndLoadInputs(
            repositoryRoot,
            releaseDirectoryArg,
            out string root,
            out string releaseDirectory,
            out string releaseYaml,
            out byte[] summaryBytes,
            out ReleaseDescriptor? descriptor);
        if (layoutDiagnostic is not null)
        {
            return Reject(output, layoutDiagnostic);
        }

        string headOid = RunGit(root, GitRevParseCommand, "HEAD^{commit}").Trim();
        string? pinnedDiagnostic = ValidatePinnedObjects(root, descriptor!, timeout, out GitTagVerificationResult? verifiedBaseline);
        if (pinnedDiagnostic is not null)
        {
            return Reject(output, pinnedDiagnostic);
        }

        string? imageDiagnostic = LoadOptionalImageManifest(root, imageManifestPath, descriptor!.Version, headOid, out IReadOnlyList<EnrichedImageReference>? verifiedImages);
        if (imageDiagnostic is not null)
        {
            return Reject(output, imageDiagnostic);
        }

        string? rangeDiagnostic = LoadRangeAndFragments(
            root,
            descriptor,
            timeout,
            out IReadOnlyList<ReleaseCommit> commits,
            out ReleasePolicy policy,
            out string[] rangeOids,
            out string[] omittedRangeOids,
            out string[] fragments);
        if (rangeDiagnostic is not null)
        {
            return Reject(output, rangeDiagnostic);
        }

        GitCliffToolConfiguration toolConfig = BuildToolConfiguration(root, platform, timeout);
        string commitRange = $"{descriptor.PreviousPublishedTag}..HEAD";
        string? bumpDiagnostic = RequestOptionalBumpEvidence(executor, toolConfig, root, descriptor, commitRange, out string? suggestedVersion);
        if (bumpDiagnostic is not null)
        {
            return Reject(output, bumpDiagnostic);
        }

        ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(releaseYaml, fragments, []);
        ReleaseContextValidationResult context = ReleaseContextPolicy.Build(
            input,
            commits,
            policy,
            gitCliffSuggestedVersion: suggestedVersion,
            verifiedBaselineRef: verifiedBaseline?.TagName,
            verifiedBaselineOid: verifiedBaseline?.TargetCommitOid);
        if (!context.IsValid || context.Json is null)
        {
            string contextDiagnostic = context.Diagnostics.Contains("context_git_cliff_bump_disagreement", StringComparer.Ordinal)
                ? "context_git_cliff_bump_disagreement"
                : "prepare_context_invalid";
            return Reject(output, contextDiagnostic);
        }

        string? diffDiagnostic = ExecuteContractDiff(root, descriptor, headOid, timeout, gitObjectReader, out ReleaseContractDiffResult? contractDiff);
        if (diffDiagnostic is not null)
        {
            return Reject(output, diffDiagnostic);
        }

        return FinalizeAndWriteArtifacts(
            output,
            new FinalizationInputs(
                root,
                releaseDirectory,
                input,
                context,
                summaryBytes,
                rangeOids,
                omittedRangeOids,
                toolConfig,
                executor,
                commitRange,
                contractDiff!,
                verifiedImages));
    }

    private sealed record FinalizationInputs(
        string Root,
        string ReleaseDirectory,
        ReleaseInputValidationResult Input,
        ReleaseContextValidationResult Context,
        byte[] SummaryBytes,
        string[] RangeOids,
        string[] OmittedRangeOids,
        GitCliffToolConfiguration ToolConfig,
        IGitCliffExecutor Executor,
        string CommitRange,
        ReleaseContractDiffResult ContractDiff,
        IReadOnlyList<EnrichedImageReference>? VerifiedImages);

    private static string? ResolveAndLoadInputs(
        string repositoryRoot,
        string releaseDirectoryArg,
        out string root,
        out string releaseDirectory,
        out string releaseYaml,
        out byte[] summaryBytes,
        out ReleaseDescriptor? descriptor)
    {
        releaseDirectory = string.Empty;
        releaseYaml = string.Empty;
        summaryBytes = [];
        descriptor = null;

        root = ResolveDirectory(repositoryRoot);
        if (!string.Equals(RunGit(root, GitRevParseCommand, "--show-toplevel").Trim(), root, PathComparison))
        {
            return "prepare_repository_root_invalid";
        }

        releaseDirectory = ResolveChild(root, releaseDirectoryArg, mustExist: true);
        string expectedReleaseParent = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases");
        if (!string.Equals(Path.GetDirectoryName(releaseDirectory), expectedReleaseParent, PathComparison))
        {
            return "prepare_release_path_invalid";
        }

        releaseYaml = ReadNormalizedText(ResolveChild(releaseDirectory, "release.yaml", mustExist: true));
        summaryBytes = File.ReadAllBytes(ResolveChild(releaseDirectory, "summary.md", mustExist: true));
        if (!IsValidSummary(summaryBytes))
        {
            return "prepare_summary_restricted";
        }

        ReleaseInputValidationResult descriptorOnly = ReleaseInputPolicy.Validate(releaseYaml, [], []);
        if (!descriptorOnly.IsValid || descriptorOnly.Descriptor is null)
        {
            return "prepare_release_input_invalid";
        }

        descriptor = descriptorOnly.Descriptor;
        return null;
    }

    private static string? LoadOptionalImageManifest(
        string root,
        string? imageManifestPath,
        string expectedVersion,
        string headOid,
        out IReadOnlyList<EnrichedImageReference>? verifiedImages)
    {
        verifiedImages = null;
        if (imageManifestPath is null)
        {
            return null;
        }

        string resolvedManifest = Path.IsPathFullyQualified(imageManifestPath)
            ? imageManifestPath
            : Path.Combine(root, imageManifestPath);
        ReleaseImageManifestLoadResult manifestResult = ReleaseImageManifest.LoadFromFile(
            resolvedManifest,
            expectedVersion,
            expectedCommitOid: headOid);
        if (!manifestResult.IsValid || manifestResult.Manifest is null)
        {
            return manifestResult.Diagnostic ?? "image_manifest_invalid";
        }

        verifiedImages = manifestResult.Manifest.Images;
        return null;
    }

    private static string? LoadRangeAndFragments(
        string root,
        ReleaseDescriptor descriptor,
        TimeSpan timeout,
        out IReadOnlyList<ReleaseCommit> commits,
        out ReleasePolicy policy,
        out string[] rangeOids,
        out string[] omittedRangeOids,
        out string[] fragments)
    {
        commits = GitTagSignatureVerifier.ReadCommitRange(root, descriptor.PreviousPublishedTag, "HEAD", timeout);
        rangeOids = commits.Select(commit => commit.Oid).ToArray();
        ReleasePolicy loadedPolicy = ReleasePolicy.LoadFromRepositoryRoot(root);
        policy = loadedPolicy;

        (ReleaseCommit Commit, CommitPolicyResult Result)[] evaluatedCommits = commits
            .Select(commit => (commit, loadedPolicy.EvaluateCommit(commit.Message)))
            .ToArray();

        omittedRangeOids = evaluatedCommits
            .Where(item => item.Result.IsValid && item.Result.ReleaseVisibility != ReleaseVisibility.Visible)
            .Select(item => item.Commit.Oid)
            .ToArray();

        string[] linkedChangeIds = evaluatedCommits
            .Select(item => item.Result.ChangeId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string fragmentDirectory = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "changes");
        fragments = Directory.Exists(fragmentDirectory)
            ? Directory.EnumerateFiles(fragmentDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
                .Where(path => linkedChangeIds.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(ReadNormalizedText)
                .ToArray()
            : [];

        return fragments.Length == linkedChangeIds.Length ? null : "prepare_fragment_missing";
    }

    private static GitCliffToolConfiguration BuildToolConfiguration(string root, string platform, TimeSpan timeout)
    {
        string lockPath = Path.Combine(root, "eng", "release", "toolchain.lock.json");
        string configPath = Path.Combine(root, "eng", "release", "cliff.toml");
        string toolDirectory = GitCliffToolchainStore.ResolveConfiguredDirectory(
            lockPath,
            Environment.GetEnvironmentVariable(GitCliffToolchainStore.DefaultBundleEnvironmentVariable),
            platform);
        return new GitCliffToolConfiguration(lockPath, toolDirectory, configPath, platform, timeout);
    }

    private static string? RequestOptionalBumpEvidence(
        IGitCliffExecutor executor,
        GitCliffToolConfiguration toolConfig,
        string root,
        ReleaseDescriptor descriptor,
        string commitRange,
        out string? suggestedVersion)
    {
        suggestedVersion = null;
        if (descriptor.Version.Contains('-', StringComparison.Ordinal))
        {
            return null;
        }

        GitCliffBumpVersionResult bumpResult = executor.SuggestBumpedVersion(
            new GitCliffBumpVersionRequest(toolConfig, root, commitRange));
        if (!bumpResult.IsValid || string.IsNullOrWhiteSpace(bumpResult.SuggestedVersion))
        {
            return bumpResult.Diagnostic ?? "git_cliff_execution_failed";
        }

        suggestedVersion = bumpResult.SuggestedVersion;
        return null;
    }

    private static string? ExecuteContractDiff(
        string root,
        ReleaseDescriptor descriptor,
        string headOid,
        TimeSpan timeout,
        IGitObjectReader? gitObjectReader,
        out ReleaseContractDiffResult? contractDiff)
    {
        TimeSpan diffTimeout = timeout < TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : timeout;
        IGitObjectReader objectReader = gitObjectReader ?? new GitProcessObjectReader(root, diffTimeout);
        contractDiff = ReleaseContractDiffPipeline.Default.Execute(objectReader, descriptor.ReleaseRange.PreviousOid, headOid);
        return !contractDiff.IsValid || contractDiff.Artifact is null || contractDiff.Json is null
            ? contractDiff.Diagnostic ?? "prepare_contract_diff_failed"
            : null;
    }

    private static int FinalizeAndWriteArtifacts(TextWriter output, FinalizationInputs inputs)
    {
        byte[] contextBytes = StrictUtf8.GetBytes(inputs.Context.Json!);
        byte[] contractDiffBytes = StrictUtf8.GetBytes(inputs.ContractDiff.Json!);
        string contextPath = Path.Combine(inputs.ReleaseDirectory, "release-context.v1.json");
        string contractDiffPath = Path.Combine(inputs.ReleaseDirectory, "contract-diff.v1.json");

        if (HasUnexpectedExistingContent(contextPath, contextBytes) ||
            HasUnexpectedExistingContent(contractDiffPath, contractDiffBytes))
        {
            return Reject(output, UnexpectedGeneratedFileDiagnostic);
        }

        ReleasePreparationResult result = ReleasePreparation.Prepare(new ReleasePreparationRequest(
            inputs.ReleaseDirectory,
            inputs.Root,
            inputs.Input,
            inputs.Context,
            inputs.SummaryBytes,
            inputs.RangeOids,
            inputs.OmittedRangeOids,
            inputs.ToolConfig,
            inputs.Executor,
            inputs.CommitRange,
            inputs.ContractDiff.Artifact,
            inputs.VerifiedImages));
        if (!result.IsValid)
        {
            return Reject(output, result.Diagnostic!);
        }

        WriteIfMissing(contextPath, contextBytes);
        WriteIfMissing(contractDiffPath, contractDiffBytes);
        output.Write(result.CommitMessage);
        return Program.Success;
    }

    private static bool HasUnexpectedExistingContent(string path, byte[] expectedBytes) =>
        File.Exists(path) && !File.ReadAllBytes(path).AsSpan().SequenceEqual(expectedBytes);

    private static void WriteIfMissing(string path, byte[] bytes)
    {
        if (!File.Exists(path))
        {
            WriteAtomic(path, bytes);
        }
    }

    private static bool TryParseArgs(string[] args, out string? releaseDirectory, out string? imageManifestPath)
    {
        releaseDirectory = null;
        imageManifestPath = null;
        if (args.Length is not (2 or 4))
        {
            return false;
        }

        releaseDirectory = args[1];
        if (string.IsNullOrWhiteSpace(releaseDirectory) || releaseDirectory.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        if (args.Length == 4)
        {
            if (!string.Equals(args[2], "--image-manifest", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(args[3]))
            {
                return false;
            }

            imageManifestPath = args[3];
        }

        return true;
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
            return ValidateSignedBaselineTag(repositoryRoot, descriptor, timeout, out verifiedBaseline);
        }

        string baseOid = RunGit(repositoryRoot, GitRevParseCommand, $"{descriptor.BaseStableTag}^{{commit}}").Trim();
        string previousOid = RunGit(repositoryRoot, GitRevParseCommand, $"{descriptor.PreviousPublishedTag}^{{commit}}").Trim();
        string headOid = RunGit(repositoryRoot, GitRevParseCommand, "HEAD^{commit}").Trim();

        return string.Equals(descriptor.ReleaseRange.BaseOid, baseOid, StringComparison.Ordinal) &&
            string.Equals(descriptor.ReleaseRange.PreviousOid, previousOid, StringComparison.Ordinal) &&
            !string.Equals(headOid, previousOid, StringComparison.Ordinal)
            ? null
            : RangeMovedDiagnostic;
    }

    private static string? ValidateSignedBaselineTag(
        string repositoryRoot,
        ReleaseDescriptor descriptor,
        TimeSpan timeout,
        out GitTagVerificationResult? verifiedBaseline)
    {
        verifiedBaseline = null;
        if (descriptor.BaseStableTag != descriptor.PreviousPublishedTag ||
            descriptor.ReleaseRange.BaseRef != descriptor.BaseStableTag ||
            descriptor.ReleaseRange.PreviousRef != descriptor.PreviousPublishedTag ||
            descriptor.ReleaseRange.BaseOid != descriptor.ReleaseRange.PreviousOid)
        {
            return RangeMovedDiagnostic;
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
            RunGit(repositoryRoot, GitRevParseCommand, "HEAD^{commit}").Trim()));
        if (git.IsValid)
        {
            return null;
        }

        return git.Diagnostics.Count == 0 ? RangeMovedDiagnostic : git.Diagnostics[0];
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
