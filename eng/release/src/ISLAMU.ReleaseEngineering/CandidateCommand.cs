// ABOUTME: Verifies exact preparation commit B and emits deterministic pre-tag candidate evidence.
// ABOUTME: Recomputes release context and notes from local Git plus the promoted trusted bundle.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public static class CandidateCommand
{
    private const int MaximumGitOutputCharacters = 65_536;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public static int Run(string[] args, TextWriter output, string repositoryRoot, string platform, TimeSpan timeout)
    {
        if (args.Length != 3)
        {
            output.WriteLine("invalid_arguments: verify-candidate requires release directory and candidate oid");
            return Program.UsageError;
        }

        try
        {
            string root = ResolveDirectory(repositoryRoot);
            if (!string.Equals(RunGit(root, "rev-parse", "--show-toplevel").Trim(), root, PathComparison))
            {
                return Reject(output, "candidate_repository_root_invalid");
            }

            string releaseDirectory = ResolveChild(root, args[1], mustExist: true);
            string expectedReleaseParent = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases");
            if (!string.Equals(Path.GetDirectoryName(releaseDirectory), expectedReleaseParent, PathComparison))
            {
                return Reject(output, "candidate_release_path_invalid");
            }

            string candidateOid = args[2];
            if (!FullOidPattern.IsMatch(candidateOid))
            {
                return Reject(output, "candidate_oid_not_full");
            }

            TrustedBundleResult trusted = VerifyBundle(root);
            if (!trusted.IsValid || trusted.Bundle is null)
            {
                return Reject(output, "candidate_trusted_bundle_invalid");
            }

            BundleDigests bundleDigests = ReadBundleDigests(trusted.Bundle.Root);

            string releasePath = ResolveChild(releaseDirectory, "release.yaml", mustExist: true);
            string summaryPath = ResolveChild(releaseDirectory, "summary.md", mustExist: true);
            string contextPath = ResolveChild(releaseDirectory, "release-context.v1.json", mustExist: true);
            string notesPath = ResolveChild(releaseDirectory, "release-notes.md", mustExist: true);
            string manifestPath = ResolveChild(releaseDirectory, "release-candidate.v1.json", mustExist: false);
            string releaseYaml = ReadCanonicalText(releasePath);
            byte[] summaryBytes = File.ReadAllBytes(summaryPath);
            byte[] committedContext = File.ReadAllBytes(contextPath);
            byte[] committedNotes = File.ReadAllBytes(notesPath);
            if (IsDirty(root, releasePath) || IsDirty(root, summaryPath) || IsDirty(root, contextPath) || IsDirty(root, notesPath))
            {
                return Reject(output, "candidate_generated_artifacts_dirty");
            }

            string policyPath = ResolveChild(root, "eng/release/policy/release-policy.yaml", mustExist: true);
            if (!string.Equals(Sha256(File.ReadAllBytes(policyPath)), bundleDigests.PolicySha256, StringComparison.Ordinal))
            {
                return Reject(output, "candidate_policy_digest_mismatch");
            }

            ReleaseInputValidationResult descriptorOnly = ReleaseInputPolicy.Validate(releaseYaml, [], []);
            if (!descriptorOnly.IsValid || descriptorOnly.Descriptor is null)
            {
                return Reject(output, "candidate_release_input_invalid");
            }

            ReleaseDescriptor descriptor = descriptorOnly.Descriptor;
            GitReleaseValidationResult git = GitRepositoryValidator.Validate(new GitReleaseValidationRequest(
                root,
                descriptor.Line,
                descriptor.Version,
                $"refs/tags/{descriptor.BaseStableTag}",
                $"refs/tags/{descriptor.PreviousPublishedTag}",
                candidateOid),
                timeout);
            if (!git.IsValid || git.Identity is null)
            {
                return Reject(output, FirstDiagnostic(git.Diagnostics));
            }


            if (!IsExactCommittedFile(root, candidateOid, releasePath) ||
                !IsExactCommittedFile(root, candidateOid, summaryPath) ||
                !IsExactCommittedFile(root, candidateOid, contextPath) ||
                !IsExactCommittedFile(root, candidateOid, notesPath))
            {
                return Reject(output, "candidate_committed_artifact_mismatch");
            }

            ReleaseCommit[] commitsThroughB = ReadGitRange(root, descriptor.PreviousPublishedTag, candidateOid);
            if (commitsThroughB.Length < 2 || !string.Equals(commitsThroughB[^1].Oid, candidateOid, StringComparison.Ordinal))
            {
                return Reject(output, "candidate_range_not_terminal_b");
            }

            string candidateParentOid = RunGit(root, "rev-parse", "--verify", $"{candidateOid}^").Trim();
            if (!FullOidPattern.IsMatch(candidateParentOid) || !string.Equals(candidateParentOid, commitsThroughB[^2].Oid, StringComparison.Ordinal))
            {
                return Reject(output, "candidate_parent_mismatch");
            }

            ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
            CommitPolicyResult terminal = policy.EvaluateCommit(commitsThroughB[^1].Message);
            if (!terminal.IsValid || terminal.ReleaseVisibility != ReleaseVisibility.Skipped || !string.Equals(terminal.SkipReason, "release metadata commit", StringComparison.Ordinal) || !HasExactTerminalSkip(commitsThroughB[^1].Message))
            {
                return Reject(output, "candidate_terminal_commit_not_release_metadata_skip");
            }

            ChangeIdRenameLoadResult renameResult = ChangeIdRenamePolicy.Load(root);
            if (!renameResult.IsValid)
            {
                return Reject(output, renameResult.Diagnostics[0]);
            }

            string[] linkedChangeIds = commitsThroughB
                .Select(commit => ChangeIdRenamePolicy.Evaluate(commit, policy, renameResult.Renames).ChangeId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string[] fragments = ReadLinkedFragments(root, linkedChangeIds);
            if (fragments.Length != linkedChangeIds.Length)
            {
                return Reject(output, "candidate_fragment_missing");
            }

            HashSet<string> rangeCommitOids = commitsThroughB
                .Select(commit => commit.Oid)
                .ToHashSet(StringComparer.Ordinal);
            string[] releaseSourceDocuments =
            [
                .. fragments,
                .. renameResult.Renames
                    .Zip(renameResult.CanonicalDocuments)
                    .Where(item => rangeCommitOids.Contains(item.First.CommitOid))
                    .OrderBy(item => item.First.CommitOid, StringComparer.Ordinal)
                    .Select(item => item.Second),
            ];
            VerifiedBaseline? baseline = TryReadBaseline(root, descriptor);
            ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(releaseYaml, fragments, []);
            ReleaseContextValidationResult context = ReleaseContextPolicy.Build(
                input,
                commitsThroughB,
                policy,
                verifiedBaselineRef: baseline?.Ref,
                verifiedBaselineOid: baseline?.TargetOid,
                changeIdRenames: renameResult.Renames);
            if (!context.IsValid || context.Context is null || context.Json is null)
            {
                return Reject(output, "candidate_context_invalid");
            }

            byte[] recomputedContext = StrictUtf8.GetBytes(context.Json);
            if (!committedContext.AsSpan().SequenceEqual(recomputedContext))
            {
                return Reject(output, "candidate_release_context_mismatch");
            }

            if (!AllContextOidsMatchGitFormat(context.Context, git.Identity.OidLength))
            {
                return Reject(output, "candidate_object_format_mismatch");
            }

            string[] renderedRange = ContextRangeOids(context.Context);
            byte[] recomputedNotes = ComposeNotesInTemporaryDirectory(releaseDirectory, input, context, summaryBytes, renderedRange, trusted.Bundle, platform, timeout);
            if (!committedNotes.AsSpan().SequenceEqual(recomputedNotes))
            {
                return Reject(output, "candidate_release_notes_mismatch");
            }

            byte[] manifest = BuildManifest(
                git.Identity,
                descriptor,
                candidateParentOid,
                commitsThroughB.Select(commit => commit.Oid).ToArray(),
                trusted.Bundle,
                bundleDigests,
                releaseYaml,
                releaseSourceDocuments,
                summaryBytes,
                committedContext,
                committedNotes);

            // The manifest is derived entirely from immutable objects, but the *refs* naming the base
            // and previous tags could be recreated while this command runs. Re-resolving them around
            // the write keeps the published manifest bound to the objects that were actually measured,
            // without reintroducing any dependency on a mutable branch head.
            if (!ObjectAnchorsUnchanged(root, git.Identity))
            {
                return Reject(output, "candidate_object_anchors_moved");
            }

            bool manifestCreated = false;
            if (File.Exists(manifestPath))
            {
                if (!File.ReadAllBytes(manifestPath).AsSpan().SequenceEqual(manifest))
                {
                    return Reject(output, "candidate_manifest_stale");
                }
            }
            else
            {
                WriteAtomic(manifestPath, manifest);
                manifestCreated = true;
            }

            if (!ObjectAnchorsUnchanged(root, git.Identity))
            {
                if (manifestCreated && File.Exists(manifestPath)) File.Delete(manifestPath);
                return Reject(output, "candidate_object_anchors_moved");
            }

            output.WriteLine($"release_candidate_verified: {Path.GetRelativePath(root, manifestPath).Replace(Path.DirectorySeparatorChar, '/')}");
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or DecoderFallbackException or JsonException or InvalidOperationException)
        {
            return Reject(output, "candidate_input_invalid");
        }
    }

    private static byte[] ComposeNotesInTemporaryDirectory(
        string releaseDirectory,
        ReleaseInputValidationResult input,
        ReleaseContextValidationResult context,
        byte[] summaryBytes,
        IReadOnlyList<string> renderedRange,
        VerifiedTrustedBundle trusted,
        string platform,
        TimeSpan timeout)
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), $"islamu-candidate-render-{Guid.NewGuid():N}");
        string releaseLeaf = Path.GetFileName(releaseDirectory);
        if (string.IsNullOrWhiteSpace(releaseLeaf) || Path.IsPathRooted(releaseLeaf) || releaseLeaf.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) || releaseLeaf.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid release directory leaf name.", nameof(releaseDirectory));
        }
        string temporaryRelease = Path.Join(temporaryRoot, "docs", "internal", "releases", releaseLeaf);
        try
        {
            Directory.CreateDirectory(temporaryRelease);
            File.Copy(Path.Combine(releaseDirectory, "release.yaml"), Path.Combine(temporaryRelease, "release.yaml"));
            File.WriteAllBytes(Path.Combine(temporaryRelease, "summary.md"), summaryBytes);
            ReleasePreparationResult prepared = ReleasePreparation.Prepare(new ReleasePreparationRequest(
                temporaryRelease,
                input,
                context,
                summaryBytes,
                renderedRange,
                new GitCliffRenderRequest(
                    trusted,
                    StrictUtf8.GetBytes(context.Json!),
                    platform,
                    Path.Combine(temporaryRoot, "renderer"),
                    timeout)));
            if (!prepared.IsValid || prepared.Notes is null)
            {
                throw new InvalidOperationException(prepared.Diagnostic);
            }

            return prepared.Notes;
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static byte[] BuildManifest(
        GitRepositoryObjectIdentity git,
        ReleaseDescriptor descriptor,
        string candidateParentOid,
        IReadOnlyList<string> rangeOids,
        VerifiedTrustedBundle trusted,
        BundleDigests bundleDigests,
        string releaseYaml,
        IReadOnlyList<string> releaseSourceDocuments,
        byte[] summaryBytes,
        byte[] contextBytes,
        byte[] notesBytes)
    {
        string json = JsonSerializer.Serialize(new
        {
            schemaVersion = "release-candidate.v1",
            objectFormat = git.ObjectFormat,
            oidLength = git.OidLength,
            version = descriptor.Version,
            line = descriptor.Line,
            releaseDate = descriptor.ReleaseDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            candidateOid = git.CandidateOid,
            candidateParentOid,
            expectedIntegrationOldOid = candidateParentOid,
            expectedIntegrationNewOid = git.CandidateOid,
            rangeBaseOid = descriptor.ReleaseRange.BaseOid,
            rangePreviousOid = descriptor.ReleaseRange.PreviousOid,
            rangeOids,
            baseStableTag = descriptor.BaseStableTag,
            baseStableRef = $"refs/tags/{descriptor.BaseStableTag}",
            baseStableOid = git.BaseStableCommitOid,
            previousPublishedTag = descriptor.PreviousPublishedTag,
            previousPublishedRef = $"refs/tags/{descriptor.PreviousPublishedTag}",
            previousPublishedOid = git.PreviousPublishedCommitOid,
            trustedBundleManifestSha256 = trusted.ManifestDigest,
            trustedBundlePolicySha256 = bundleDigests.PolicySha256,
            trustedBundleConfigSha256 = trusted.ConfigDigest,
            trustedBundleTrustSha256 = bundleDigests.TrustSha256,
            trustedBundleToolchainSha256 = trusted.ToolchainLockDigest,
            trustedBundleGitCliffSha256 = bundleDigests.GitCliffSha256,
            releaseDescriptorSha256 = Sha256(StrictUtf8.GetBytes(releaseYaml)),
            releaseFragmentsSha256 = Sha256(StrictUtf8.GetBytes(string.Join("\n---\n", releaseSourceDocuments))),
            releaseSummarySha256 = Sha256(summaryBytes),
            releaseContextSha256 = Sha256(contextBytes),
            releaseNotesSha256 = Sha256(notesBytes),
        }, JsonOptions);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(json);
        if (!canonical.IsValid || canonical.Bytes is null)
        {
            throw new InvalidOperationException("candidate_manifest_not_canonical");
        }

        return canonical.Bytes;
    }

    private static BundleDigests ReadBundleDigests(string bundleRoot)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(bundleRoot, "trusted-bundle.manifest.json")));
        Dictionary<string, string> files = document.RootElement.GetProperty("files").EnumerateArray()
            .ToDictionary(item => item.GetProperty("path").GetString()!, item => item.GetProperty("sha256").GetString()!, StringComparer.Ordinal);
        string gitCliff = files.TryGetValue("git-cliff", out string? linux) ? linux : files.TryGetValue("git-cliff.exe", out string? windows) ? windows : string.Empty;
        if (gitCliff.Length == 0 ||
            !files.TryGetValue("policy/release-policy.yaml", out string? policy) ||
            !files.TryGetValue("trust/release-signing-policy.yaml", out string? trust))
        {
            throw new InvalidOperationException("candidate_trusted_bundle_tool_missing");
        }

        return new BundleDigests(policy, trust, gitCliff);
    }

    private static string[] ReadLinkedFragments(string root, IReadOnlyList<string> linkedChangeIds)
    {
        string fragmentDirectory = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "changes");
        return Directory.Exists(fragmentDirectory)
            ? Directory.EnumerateFiles(fragmentDirectory, "*.yaml", SearchOption.TopDirectoryOnly)
                .Where(path => linkedChangeIds.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal))
                .Order(StringComparer.Ordinal)
                .Select(ReadCanonicalText)
                .ToArray()
            : [];
    }

    private static VerifiedBaseline? TryReadBaseline(string repositoryRoot, ReleaseDescriptor descriptor) =>
        ReleaseInputPolicy.IsBaselineRef(descriptor.BaseStableTag) && BaselineEvidencePolicy.TryRead(repositoryRoot, descriptor.BaseStableTag, out VerifiedBaseline baseline)
            ? baseline
            : null;

    private static ReleaseCommit[] ReadGitRange(string repositoryRoot, string previousPublishedTag, string candidateOid)
    {
        string raw = RunGit(repositoryRoot, "log", "--reverse", "--format=%H%x00%B%x1e", $"{previousPublishedTag}..{candidateOid}");
        return raw.Split('\u001e', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.TrimStart('\n'))
            .Where(entry => entry.Length != 0)
            .Select(entry =>
            {
                int separator = entry.IndexOf('\0');
                if (separator <= 0) throw new IOException();
                return new ReleaseCommit(entry[..separator], entry[(separator + 1)..].TrimEnd('\n'));
            })
            .ToArray();
    }

    private static string[] ContextRangeOids(ReleaseContext context)
    {
        HashSet<string> currentChangeOids = context.Changes.Select(change => change.Oid).ToHashSet(StringComparer.Ordinal);
        HashSet<string> backportOriginalOnly = context.Changes
            .Select(change => change.BackportOf)
            .OfType<string>()
            .Where(oid => !currentChangeOids.Contains(oid))
            .ToHashSet(StringComparer.Ordinal);
        return context.Evidence.Objects.Select(value => value.Oid).Where(oid =>
            !string.Equals(oid, context.Evidence.BaseStableOid, StringComparison.Ordinal) &&
            !string.Equals(oid, context.Evidence.PreviousPublishedOid, StringComparison.Ordinal) &&
            !backportOriginalOnly.Contains(oid)).ToArray();
    }

    private static bool HasExactTerminalSkip(string message)
    {
        string[] lines = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        int last = lines.Length - 1;
        while (last >= 0 && lines[last].Length == 0) last--;
        return last >= 1 &&
            string.Equals(lines[last - 1], "Changelog: skip", StringComparison.Ordinal) &&
            string.Equals(lines[last], "Changelog-Reason: release metadata commit", StringComparison.Ordinal);
    }

    private static bool AllContextOidsMatchGitFormat(ReleaseContext context, int oidLength) =>
        context.Evidence.Objects.All(item => item.Oid.Length == oidLength) &&
        context.Changes.All(change => change.Oid.Length == oidLength && (change.BackportOf is null || change.BackportOf.Length == oidLength));

    private static bool IsDirty(string repositoryRoot, string path)
    {
        string relative = Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        return RunGit(repositoryRoot, "status", "--porcelain", "--", relative).Length != 0;
    }

    private static bool IsExactCommittedFile(string repositoryRoot, string candidateOid, string path)
    {
        string relative = Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
        if (!TryRunGit(repositoryRoot, out string committed, "rev-parse", "--verify", $"{candidateOid}:{relative}"))
        {
            return false;
        }

        string observed = RunGit(repositoryRoot, "hash-object", "--", relative).Trim();
        return FullOidPattern.IsMatch(committed.Trim()) && string.Equals(committed.Trim(), observed, StringComparison.Ordinal);
    }

    /// <summary>
    /// Confirms the annotated tags that bound the release range still resolve to the objects the
    /// manifest recorded. Tag refs are immutable by policy but deletable in practice, so this closes
    /// the read-then-write window that the removed branch-head re-check used to cover.
    /// </summary>
    private static bool ObjectAnchorsUnchanged(string repositoryRoot, GitRepositoryObjectIdentity identity) =>
        AnchorMatches(repositoryRoot, $"refs/tags/{identity.BaseStableTag}^{{object}}", identity.BaseStableTagObjectOid) &&
        AnchorMatches(repositoryRoot, $"refs/tags/{identity.BaseStableTag}^{{commit}}", identity.BaseStableCommitOid) &&
        AnchorMatches(repositoryRoot, $"refs/tags/{identity.PreviousPublishedTag}^{{object}}", identity.PreviousPublishedTagObjectOid) &&
        AnchorMatches(repositoryRoot, $"refs/tags/{identity.PreviousPublishedTag}^{{commit}}", identity.PreviousPublishedCommitOid) &&
        AnchorMatches(repositoryRoot, $"{identity.CandidateOid}^{{commit}}", identity.CandidateOid);

    private static bool AnchorMatches(string repositoryRoot, string reference, string expectedOid) =>
        TryRunGit(repositoryRoot, out string observed, "rev-parse", "--verify", "--end-of-options", reference) &&
        string.Equals(observed.Trim(), expectedOid, StringComparison.Ordinal);

    private static bool TryRunGit(string repositoryRoot, out string output, params string[] arguments)
    {
        try
        {
            output = RunGit(repositoryRoot, arguments);
            return true;
        }
        catch (IOException)
        {
            output = string.Empty;
            return false;
        }
    }

    private static TrustedBundleResult VerifyBundle(string candidateRoot)
    {
        string Required(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
        return TrustedBundlePolicy.Verify(new TrustedBundleVerificationRequest(
            Required("ISLAMU_RELEASE_TRUSTED_BUNDLE"),
            candidateRoot,
            new PromotionAuthorityInput(
                Required("ISLAMU_RELEASE_PROMOTION_RECEIPT"),
                Required("ISLAMU_RELEASE_PROMOTION_SIGNATURE"),
                Required("ISLAMU_RELEASE_PROMOTION_PRINCIPAL")),
            Required("ISLAMU_RELEASE_BUNDLE_ID"),
            Required("ISLAMU_RELEASE_BUNDLE_VERSION"),
            Required("ISLAMU_RELEASE_POLICY_VERSION"),
            Required("ISLAMU_RELEASE_CONFIG_VERSION"),
            Required("ISLAMU_RELEASE_TRUST_VERSION"))
        {
            ExpectedManifestDigest = Required("ISLAMU_RELEASE_MANIFEST_SHA256"),
        });
    }

    private static string RunGit(string repositoryRoot, params string[] arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo("git")
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
        process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            Task<string> standardOutput = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            Task<string> standardError = ReadBoundedAsync(process.StandardError, timeout.Token);
            Task.WhenAll(process.WaitForExitAsync(timeout.Token), standardOutput, standardError).GetAwaiter().GetResult();
            if (process.ExitCode != 0 || standardError.Result.Length != 0)
            {
                throw new IOException();
            }

            return standardOutput.Result;
        }
        catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new IOException("candidate_git_failed", exception);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumGitOutputCharacters];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
            if (read == 0) return new string(buffer, 0, count);
            count += read;
        }

        if (await reader.ReadAsync(new char[1], cancellationToken) != 0) throw new OperationCanceledException();
        return new string(buffer);
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

    private static string ReadCanonicalText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = StrictUtf8.GetString(bytes);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeText(text);
        if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes))
        {
            throw new IOException();
        }

        return text;
    }

    private static string ResolveDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) || IsLink(full)) throw new IOException();
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveChild(string root, string child, bool mustExist)
    {
        string full = Path.GetFullPath(Path.Combine(root, child));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)) throw new IOException();
        if (mustExist && (!File.Exists(full) && !Directory.Exists(full))) throw new IOException();
        if (IsLink(full)) throw new IOException();
        return full;
    }

    private static bool IsLink(string path) => File.Exists(path) || Directory.Exists(path)
        ? (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
        : false;

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string FirstDiagnostic(IReadOnlyList<string> diagnostics) => diagnostics.Count == 0 ? "candidate_git_invalid" : diagnostics[0];
    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine($"verify_candidate_failed: {diagnostic}");
        return Program.ToolchainRejected;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private sealed record BundleDigests(string PolicySha256, string TrustSha256, string GitCliffSha256);
}
