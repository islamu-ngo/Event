using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Projects signed local release records under <c>docs/internal/releases/</c> into
/// <c>docs/public/changelog/README.md</c> and its atomic <c>publication-manifest.v1.json</c>.
/// </summary>
public static class SyncPublicChangelogCommand
{
    private static class Diagnostics
    {
        public const string PathInvalid = "changelog_path_invalid";
        public const string ReleaseFilesMissing = "changelog_release_files_missing";
        public const string ReleasesMissing = "changelog_releases_missing";
        public const string ProjectionInvalid = "changelog_projection_invalid";
        public const string TransactionInvalid = "changelog_transaction_invalid";
        public const string AcceptedProjectionDrift = "changelog_accepted_projection_drift";
        public const string AcceptedManifestMissing = "changelog_accepted_manifest_missing";
        public const string AcceptedHistoryOmitted = "changelog_accepted_history_omitted";
        public const string ManifestInvalid = "changelog_manifest_invalid";
        public const string TransactionGenerationMismatch = "changelog_transaction_generation_mismatch";
        public const string TransactionIncomplete = "changelog_transaction_incomplete";
    }

    private const int MaximumInputBytes = 1_048_576;
    private const string ManifestSchemaVersion = "publication-manifest.v1";
    private const string TransactionSchemaVersion = "publication-transaction.v1";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record ParsedOptions(Uri PublicationBase, string? AllowedSignersPath, string? SealedArtifactsRoot, bool Check);

    private sealed record ChangelogPaths(
        string Root,
        string AllowedSignersPath,
        string? SealedArtifactsRoot,
        string PagePath,
        string ManifestPath,
        string TransactionPath);

    private sealed record LoadedGeneratedArtifacts(
        byte[] ContextBytes,
        byte[] NotesBytes,
        IReadOnlyList<EnrichedImageReference>? Images,
        bool NotesInSignedTagTree,
        string? ManifestSha256);

    private sealed record SignedReleaseLookup(
        string RepositoryRoot,
        string AllowedSignersPath,
        string? SealedArtifactsRoot,
        string ReleaseDir,
        string VersionDir,
        string TargetCommitOid);

    private sealed record LoadedPublicationState(
        byte[] PreviousPage,
        byte[] PreviousManifest,
        PublicationTransaction? Transaction,
        AcceptedPublicationManifest? Accepted);

    public static int Run(string[] args, TextWriter output, string repositoryRoot) =>
        Run(args, output, repositoryRoot, replacementCompleted: null);

    internal static int Run(
        string[] args,
        TextWriter output,
        string repositoryRoot,
        Action<string>? replacementCompleted)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        if (!TryParseArguments(args, output, out ParsedOptions? options))
        {
            return Program.UsageError;
        }

        try
        {
            return ExecuteSync(options!, output, repositoryRoot, replacementCompleted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException or DecoderFallbackException)
        {
            return Reject(output, "changelog_input_invalid");
        }
    }

    private sealed class OptionAccumulator
    {
        public Uri? PublicationBase { get; set; }
        public string? AllowedSignersPath { get; set; }
        public string? SealedArtifactsRoot { get; set; }
        public bool Check { get; set; }
    }

    private static bool TryParseArguments(string[] args, TextWriter output, out ParsedOptions? parsed)
    {
        parsed = null;
        var options = new OptionAccumulator();
        var reader = new CliArgumentReader(args);
        while (reader.TryReadNext(out string token))
        {
            if (!TryApplyOption(token, ref reader, options, output))
            {
                return false;
            }
        }

        if (args.Length < 2 || args[0] != "sync-public-changelog" || options.PublicationBase is null)
        {
            output.WriteLine("changelog_required_arguments_missing");
            return false;
        }

        parsed = new ParsedOptions(options.PublicationBase, options.AllowedSignersPath, options.SealedArtifactsRoot, options.Check);
        return true;
    }

    private static bool TryApplyOption(
        string token,
        ref CliArgumentReader reader,
        OptionAccumulator options,
        TextWriter output)
    {
        switch (token)
        {
            case "--check" when !options.Check:
                options.Check = true;
                return true;
            case "--allowed-signers" when reader.TryReadOptionValue(options.AllowedSignersPath, out string signers):
                options.AllowedSignersPath = signers;
                return true;
            case "--sealed-artifacts-root" when reader.TryReadOptionValue(options.SealedArtifactsRoot, out string sealedRoot):
                options.SealedArtifactsRoot = sealedRoot;
                return true;
            case "--publication-base" when reader.TryReadOptionValue(options.PublicationBase?.ToString(), out string baseText):
                if (!Uri.TryCreate(baseText, UriKind.Absolute, out Uri? parsedBase))
                {
                    output.WriteLine("changelog_invalid_publication_base");
                    return false;
                }

                options.PublicationBase = parsedBase;
                return true;
            default:
                output.WriteLine("changelog_invalid_arguments");
                return false;
        }
    }

    private static int ExecuteSync(
        ParsedOptions options,
        TextWriter output,
        string repositoryRoot,
        Action<string>? replacementCompleted)
    {
        if (!TryResolveSafePaths(repositoryRoot, options.AllowedSignersPath, options.SealedArtifactsRoot, out ChangelogPaths paths))
        {
            return Reject(output, Diagnostics.PathInvalid);
        }

        string? stateDiagnostic = TryLoadExistingState(paths, out LoadedPublicationState? state);
        if (stateDiagnostic is not null)
        {
            return Reject(output, stateDiagnostic);
        }

        if (!TryLoadSignedReleases(paths, out List<PublicChangelogRelease> releases, out string? loadDiagnostic))
        {
            return Reject(output, loadDiagnostic ?? "changelog_releases_invalid");
        }

        if (state!.Accepted is not null && !PreservesAcceptedHistory(state.Accepted, releases))
        {
            return Reject(output, Diagnostics.AcceptedHistoryOmitted);
        }

        string? buildDiagnostic = TryGenerateProjectionAndManifest(
            releases,
            options.PublicationBase,
            state,
            out byte[] nextPage,
            out byte[] nextManifest,
            out string? warning);
        if (buildDiagnostic is not null)
        {
            return Reject(output, buildDiagnostic);
        }

        bool same = state.PreviousPage.AsSpan().SequenceEqual(nextPage) &&
            state.PreviousManifest.AsSpan().SequenceEqual(nextManifest);

        if (options.Check)
        {
            return CompleteCheckMode(output, state.Transaction, same);
        }

        if (!same || state.Transaction is not null)
        {
            CommitTransaction(paths, state, nextPage, nextManifest, replacementCompleted);
        }

        if (warning is not null)
        {
            output.WriteLine(warning);
        }

        output.WriteLine(same ? "changelog_unchanged" : "changelog_generated");
        return Program.Success;
    }

    private static bool TryResolveSafePaths(
        string repositoryRoot,
        string? allowedSignersPath,
        string? sealedArtifactsRoot,
        out ChangelogPaths paths)
    {
        string root = Path.GetFullPath(repositoryRoot);
        string resolvedSigners = allowedSignersPath is not null
            ? Path.GetFullPath(allowedSignersPath)
            : GitTagSignatureVerifier.ResolveDefaultAllowedSignersPath(root);
        string? resolvedSealedRoot = sealedArtifactsRoot is not null
            ? Path.GetFullPath(sealedArtifactsRoot)
            : null;
        string changelogDir = Path.Combine(root, "docs", "public", "changelog");
        paths = new ChangelogPaths(
            root,
            resolvedSigners,
            resolvedSealedRoot,
            Path.Combine(changelogDir, "README.md"),
            Path.Combine(changelogDir, "publication-manifest.v1.json"),
            Path.Combine(changelogDir, ".publication-transaction.v1.json"));

        return IsSafePath(paths.PagePath) &&
            IsSafePath(paths.ManifestPath) &&
            IsSafePath(paths.TransactionPath) &&
            IsSafePath(paths.AllowedSignersPath) &&
            (paths.SealedArtifactsRoot is null || IsSafePath(paths.SealedArtifactsRoot));
    }

    private static string? TryLoadExistingState(ChangelogPaths paths, out LoadedPublicationState? state)
    {
        state = null;
        byte[] previousPage = File.Exists(paths.PagePath) ? ReadBounded(paths.PagePath) : [];
        byte[] previousManifest = File.Exists(paths.ManifestPath) ? ReadBounded(paths.ManifestPath) : [];
        PublicationTransaction? transaction = null;

        if (File.Exists(paths.TransactionPath))
        {
            transaction = TryLoadValidTransaction(paths.TransactionPath, previousPage, previousManifest);
            if (transaction is null)
            {
                return Diagnostics.TransactionInvalid;
            }

            previousPage = transaction.OldPage;
            previousManifest = transaction.OldManifest;
        }

        string? manifestDiagnostic = TryValidateAcceptedManifest(previousPage, previousManifest, out AcceptedPublicationManifest? accepted);
        if (manifestDiagnostic is not null)
        {
            return manifestDiagnostic;
        }

        state = new LoadedPublicationState(previousPage, previousManifest, transaction, accepted);
        return null;
    }

    private static PublicationTransaction? TryLoadValidTransaction(string transactionPath, byte[] currentPage, byte[] currentManifest)
    {
        byte[] transactionBytes = ReadBounded(transactionPath, MaximumInputBytes * 6);
        PublicationTransaction? transaction = JsonSerializer.Deserialize<PublicationTransaction>(transactionBytes, JsonOptions);
        if (transaction is null ||
            transaction.SchemaVersion != TransactionSchemaVersion ||
            transaction.OldPage is null || transaction.OldManifest is null ||
            transaction.NewPage is null || transaction.NewManifest is null)
        {
            return null;
        }

        bool validSizes = new[] { transaction.OldPage, transaction.OldManifest, transaction.NewPage, transaction.NewManifest }
            .All(bytes => bytes.Length <= MaximumInputBytes);
        bool validEncoding = JsonSerializer.SerializeToUtf8Bytes(transaction, JsonOptions).AsSpan().SequenceEqual(transactionBytes);
        bool validMembers = IsTransactionMember(currentPage, transaction.OldPage, transaction.NewPage) &&
            IsTransactionMember(currentManifest, transaction.OldManifest, transaction.NewManifest);

        return validSizes && validEncoding && validMembers ? transaction : null;
    }

    private static string? TryValidateAcceptedManifest(
        byte[] previousPage,
        byte[] previousManifest,
        out AcceptedPublicationManifest? accepted)
    {
        accepted = previousManifest.Length == 0
            ? null
            : JsonSerializer.Deserialize<AcceptedPublicationManifest>(previousManifest, JsonOptions);

        if (accepted is not null)
        {
            ArtifactPolicyResult canonical = ReleaseArtifactPolicy.NormalizeJson(Utf8.GetString(previousManifest));
            bool isCanonical = canonical.IsValid && canonical.Bytes is not null &&
                canonical.Bytes.AsSpan().SequenceEqual(previousManifest);
            if (accepted.SchemaVersion != ManifestSchemaVersion ||
                accepted.Releases is null ||
                accepted.ProjectionSha256 != Digest(previousPage) ||
                !isCanonical)
            {
                return Diagnostics.AcceptedProjectionDrift;
            }

            return null;
        }

        if (previousManifest.Length != 0 || !PublicChangelogPolicy.IsEmptyBootstrapPage(previousPage))
        {
            return Diagnostics.AcceptedManifestMissing;
        }

        return null;
    }

    private static bool PreservesAcceptedHistory(AcceptedPublicationManifest accepted, List<PublicChangelogRelease> releases)
    {
        Dictionary<string, PublicChangelogRelease> byVersion = releases.ToDictionary(release => release.Descriptor.Version, StringComparer.Ordinal);
        return accepted.Releases.All(prior =>
            byVersion.TryGetValue(prior.Version, out PublicChangelogRelease? current) &&
            string.Equals(current.TagObjectId, prior.TagObjectId, StringComparison.Ordinal) &&
            string.Equals(current.NotesSha256, prior.NotesSha256, StringComparison.Ordinal));
    }

    private static string? TryGenerateProjectionAndManifest(
        List<PublicChangelogRelease> releases,
        Uri publicationBase,
        LoadedPublicationState state,
        out byte[] nextPage,
        out byte[] nextManifest,
        out string? warning)
    {
        nextPage = [];
        nextManifest = [];
        warning = null;

        PublicChangelogResult generated = PublicChangelogPolicy.Generate(releases, publicationBase);
        if (!generated.IsValid || generated.Page is null)
        {
            return generated.Diagnostic ?? Diagnostics.ProjectionInvalid;
        }

        PublicChangelogResult merged = PublicChangelogPolicy.ReplaceRegion(
            state.PreviousPage,
            generated.Page,
            bootstrap: state.Accepted is null);
        if (!merged.IsValid || merged.Page is null)
        {
            return merged.Diagnostic ?? Diagnostics.ProjectionInvalid;
        }

        AcceptedPublicationRelease[] releaseRecords = releases
            .OrderBy(release => release.Descriptor.Version, StringComparer.Ordinal)
            .Select(release => new AcceptedPublicationRelease(release.Descriptor.Version, release.TagObjectId, release.NotesSha256))
            .ToArray();
        var manifest = new AcceptedPublicationManifest(
            ManifestSchemaVersion,
            Digest(JsonSerializer.SerializeToUtf8Bytes(releaseRecords, JsonOptions)),
            Digest(merged.Page),
            generated.HighestStableVersion,
            releaseRecords);
        ArtifactPolicyResult manifestJson = ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(manifest, JsonOptions));
        if (!manifestJson.IsValid || manifestJson.Bytes is null || merged.Page.Length > MaximumInputBytes)
        {
            return Diagnostics.ManifestInvalid;
        }

        if (state.Transaction is not null &&
            (!state.Transaction.NewPage.AsSpan().SequenceEqual(merged.Page) ||
             !state.Transaction.NewManifest.AsSpan().SequenceEqual(manifestJson.Bytes)))
        {
            return Diagnostics.TransactionGenerationMismatch;
        }

        nextPage = merged.Page;
        nextManifest = manifestJson.Bytes;
        warning = generated.Warning;
        return null;
    }

    private static int CompleteCheckMode(TextWriter output, PublicationTransaction? transaction, bool same)
    {
        if (transaction is not null)
        {
            return Reject(output, Diagnostics.TransactionIncomplete);
        }

        output.WriteLine(same ? "changelog_current" : "changelog_stale");
        return same ? Program.Success : Program.ToolchainRejected;
    }

    private static void CommitTransaction(
        ChangelogPaths paths,
        LoadedPublicationState state,
        byte[] nextPage,
        byte[] nextManifest,
        Action<string>? replacementCompleted)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.PagePath)!);
        PublicationTransaction transaction = state.Transaction ?? new PublicationTransaction(
            TransactionSchemaVersion,
            state.PreviousPage,
            state.PreviousManifest,
            nextPage,
            nextManifest);
        if (!File.Exists(paths.TransactionPath))
        {
            WriteAtomic(paths.TransactionPath, JsonSerializer.SerializeToUtf8Bytes(transaction, JsonOptions));
        }

        try
        {
            WriteAtomic(paths.ManifestPath, transaction.NewManifest);
            replacementCompleted?.Invoke("manifest");
            WriteAtomic(paths.PagePath, transaction.NewPage);
            replacementCompleted?.Invoke("page");
            File.Delete(paths.TransactionPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Restore(paths.ManifestPath, transaction.OldManifest);
            Restore(paths.PagePath, transaction.OldPage);
            if (File.Exists(paths.TransactionPath))
            {
                File.Delete(paths.TransactionPath);
            }

            throw;
        }
    }

    private static bool TryLoadSignedReleases(
        ChangelogPaths paths,
        out List<PublicChangelogRelease> releases,
        out string? diagnostic)
    {
        releases = [];
        diagnostic = null;

        string releasesRoot = Path.Combine(paths.Root, "docs", "internal", "releases");
        if (!Directory.Exists(releasesRoot))
        {
            diagnostic = Diagnostics.ReleasesMissing;
            return false;
        }

        string[] candidateDirs = Directory.EnumerateDirectories(releasesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(dir => File.Exists(Path.Combine(dir, "release.yaml")))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (candidateDirs.Length == 0)
        {
            diagnostic = Diagnostics.ReleasesMissing;
            return false;
        }

        var objectReader = new GitProcessObjectReader(paths.Root, TimeSpan.FromSeconds(10), MaximumInputBytes);
        foreach (string releaseDir in candidateDirs)
        {
            if (!TryLoadSingleSignedRelease(paths, releaseDir, objectReader, out PublicChangelogRelease? release, out diagnostic))
            {
                return false;
            }

            releases.Add(release!);
        }

        return true;
    }

    private static bool TryLoadSingleSignedRelease(
        ChangelogPaths paths,
        string releaseDir,
        IGitObjectReader objectReader,
        out PublicChangelogRelease? release,
        out string? diagnostic)
    {
        release = null;
        string versionDir = Path.GetFileName(releaseDir);
        if (!IsSafePath(releaseDir) || string.IsNullOrWhiteSpace(versionDir))
        {
            diagnostic = Diagnostics.PathInvalid;
            return false;
        }

        GitTagVerificationResult tagVerification = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
            paths.Root,
            $"v{versionDir}",
            paths.AllowedSignersPath));
        if (!tagVerification.IsValid || tagVerification.TagObjectId is null || tagVerification.TargetCommitOid is null)
        {
            diagnostic = tagVerification.Diagnostic ?? "changelog_tag_invalid";
            return false;
        }

        var lookup = new SignedReleaseLookup(paths.Root, paths.AllowedSignersPath, paths.SealedArtifactsRoot, releaseDir, versionDir, tagVerification.TargetCommitOid);
        string prefix = $"docs/internal/releases/{versionDir}";
        if (!TryReadSignedArtifact(objectReader, lookup.TargetCommitOid, lookup.RepositoryRoot, $"{prefix}/release.yaml", Diagnostics.ReleaseFilesMissing, out byte[] releaseBytes, out diagnostic) ||
            !TryReadSignedArtifact(objectReader, lookup.TargetCommitOid, lookup.RepositoryRoot, $"{prefix}/summary.md", Diagnostics.ReleaseFilesMissing, out byte[] summaryBytes, out diagnostic))
        {
            return false;
        }

        if (!TryLoadGeneratedArtifacts(objectReader, lookup, out LoadedGeneratedArtifacts? generated, out diagnostic))
        {
            return false;
        }

        ArtifactPolicyResult notesPolicy = ReleaseArtifactPolicy.ValidateRenderedReleaseNotes(Utf8.GetString(generated!.NotesBytes));
        if (!notesPolicy.IsValid)
        {
            diagnostic = notesPolicy.Diagnostics.Count == 0 ? "changelog_notes_invalid" : notesPolicy.Diagnostics[0];
            return false;
        }

        ReleaseContext? context = JsonSerializer.Deserialize<ReleaseContext>(generated.ContextBytes, JsonOptions);
        if (context is null)
        {
            diagnostic = "changelog_context_invalid";
            return false;
        }

        if (!TryLoadSignedFragments(objectReader, lookup.TargetCommitOid, lookup.RepositoryRoot, context, out List<string> fragmentYamls, out diagnostic))
        {
            return false;
        }

        ReleaseInputValidationResult validated = ReleaseInputPolicy.Validate(Utf8.GetString(releaseBytes), fragmentYamls, []);
        if (!validated.IsValid || validated.Descriptor is null)
        {
            diagnostic = validated.Diagnostics.Count == 0 ? "changelog_release_input_invalid" : validated.Diagnostics[0];
            return false;
        }

        if (!string.Equals(validated.Descriptor.Version, versionDir, StringComparison.Ordinal) ||
            !string.Equals(context.Release.Version, validated.Descriptor.Version, StringComparison.Ordinal))
        {
            diagnostic = "changelog_release_version_mismatch";
            return false;
        }

        if (!generated.NotesInSignedTagTree && !ContextMatchesDescriptor(context, validated.Descriptor))
        {
            diagnostic = "changelog_context_descriptor_mismatch";
            return false;
        }

        release = new PublicChangelogRelease(
            validated.Descriptor,
            context,
            validated.Fragments,
            Utf8.GetString(summaryBytes).TrimEnd('\n'),
            Digest(generated.NotesBytes),
            tagVerification.TagObjectId,
            tagVerification.TargetCommitOid,
            generated.Images,
            generated.NotesInSignedTagTree,
            generated.ManifestSha256);
        return true;
    }

    private static bool TryLoadGeneratedArtifacts(
        IGitObjectReader objectReader,
        SignedReleaseLookup lookup,
        out LoadedGeneratedArtifacts? loaded,
        out string? diagnostic)
    {
        loaded = null;
        string prefix = $"docs/internal/releases/{lookup.VersionDir}";
        string contextRelative = $"{prefix}/{ReleaseArtifactManifest.ReleaseContextFileName}";
        string notesRelative = $"{prefix}/{ReleaseArtifactManifest.ReleaseNotesFileName}";

        GitBlobReadResult taggedContext = objectReader.ReadBlob(lookup.TargetCommitOid, contextRelative);
        if (taggedContext.Diagnostic is not null)
        {
            diagnostic = taggedContext.Diagnostic;
            return false;
        }

        GitBlobReadResult taggedNotes = objectReader.ReadBlob(lookup.TargetCommitOid, notesRelative);
        if (taggedNotes.Diagnostic is not null)
        {
            diagnostic = taggedNotes.Diagnostic;
            return false;
        }

        if (taggedContext.Found && taggedNotes.Found)
        {
            if (!TryReadSignedArtifact(objectReader, lookup.TargetCommitOid, lookup.RepositoryRoot, contextRelative, Diagnostics.ReleaseFilesMissing, out byte[] contextBytes, out diagnostic) ||
                !TryReadSignedArtifact(objectReader, lookup.TargetCommitOid, lookup.RepositoryRoot, notesRelative, Diagnostics.ReleaseFilesMissing, out byte[] notesBytes, out diagnostic))
            {
                return false;
            }

            loaded = new LoadedGeneratedArtifacts(contextBytes, notesBytes, Images: null, NotesInSignedTagTree: true, ManifestSha256: null);
            return true;
        }

        if (taggedContext.Found != taggedNotes.Found)
        {
            diagnostic = $"changelog_signed_artifact_missing:{(taggedContext.Found ? notesRelative : contextRelative)}";
            return false;
        }

        string sealedDir = lookup.SealedArtifactsRoot is not null
            ? Path.Combine(lookup.SealedArtifactsRoot, lookup.VersionDir)
            : lookup.ReleaseDir;
        if (!IsSafePath(sealedDir))
        {
            diagnostic = Diagnostics.PathInvalid;
            return false;
        }

        ReleaseArtifactVerificationResult verified = ReleaseArtifactManifest.VerifySealedDirectory(
            sealedDir,
            lookup.VersionDir,
            $"v{lookup.VersionDir}",
            lookup.TargetCommitOid,
            allowedSignersPath: lookup.AllowedSignersPath);
        if (!verified.IsValid || verified.Manifest is null || verified.ContextBytes is null || verified.NotesBytes is null)
        {
            diagnostic = verified.Diagnostic ?? Diagnostics.ReleaseFilesMissing;
            return false;
        }

        loaded = new LoadedGeneratedArtifacts(
            verified.ContextBytes,
            verified.NotesBytes,
            verified.Manifest.Images,
            NotesInSignedTagTree: false,
            verified.ManifestSha256);
        diagnostic = null;
        return true;
    }

    private static bool ContextMatchesDescriptor(ReleaseContext context, ReleaseDescriptor descriptor)
    {
        string expectedDate = descriptor.ReleaseDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return string.Equals(context.Release.Line, descriptor.Line, StringComparison.Ordinal) &&
            string.Equals(context.Release.ReleaseDate, expectedDate, StringComparison.Ordinal) &&
            string.Equals(context.Release.BaseStableTag, descriptor.BaseStableTag, StringComparison.Ordinal) &&
            string.Equals(context.Release.PreviousPublishedTag, descriptor.PreviousPublishedTag, StringComparison.Ordinal) &&
            string.Equals(context.Evidence?.BaseStableOid, descriptor.ReleaseRange.BaseOid, StringComparison.Ordinal) &&
            string.Equals(context.Evidence?.PreviousPublishedOid, descriptor.ReleaseRange.PreviousOid, StringComparison.Ordinal);
    }

    private static bool TryLoadSignedFragments(
        IGitObjectReader objectReader,
        string targetCommitOid,
        string repositoryRoot,
        ReleaseContext context,
        out List<string> fragmentYamls,
        out string? diagnostic)
    {
        diagnostic = null;
        string[] linkedIds = context.Changes
            .Select(change => change.ChangeId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        fragmentYamls = new List<string>(linkedIds.Length);
        foreach (string id in linkedIds)
        {
            string relativePath = $"docs/internal/releases/changes/{id}.yaml";
            if (!TryReadSignedArtifact(objectReader, targetCommitOid, repositoryRoot, relativePath, $"changelog_fragment_missing:{id}", out byte[] fragmentBytes, out diagnostic))
            {
                return false;
            }

            fragmentYamls.Add(Utf8.GetString(fragmentBytes));
        }

        return true;
    }

    private static bool TryReadSignedArtifact(
        IGitObjectReader objectReader,
        string targetCommitOid,
        string repositoryRoot,
        string repoRelativePath,
        string missingLocalDiagnostic,
        out byte[] signedBytes,
        out string? diagnostic)
    {
        signedBytes = [];
        diagnostic = null;

        string localPath = Path.Combine(repositoryRoot, repoRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!IsSafePath(localPath))
        {
            diagnostic = Diagnostics.PathInvalid;
            return false;
        }

        if (!File.Exists(localPath))
        {
            diagnostic = missingLocalDiagnostic;
            return false;
        }

        byte[] workingTreeBytes = ReadBounded(localPath);
        GitBlobReadResult blob = objectReader.ReadBlob(targetCommitOid, repoRelativePath);
        if (blob.Diagnostic is not null)
        {
            diagnostic = blob.Diagnostic;
            return false;
        }

        if (!blob.Found || blob.Bytes is null)
        {
            diagnostic = $"changelog_signed_artifact_missing:{repoRelativePath}";
            return false;
        }

        if (!workingTreeBytes.AsSpan().SequenceEqual(blob.Bytes))
        {
            diagnostic = $"changelog_signed_artifact_mismatch:{repoRelativePath}";
            return false;
        }

        signedBytes = blob.Bytes;
        return true;
    }

    private static bool IsSafePath(string path)
    {
        for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
        {
            if ((File.Exists(part) || Directory.Exists(part)) &&
                (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTransactionMember(byte[] current, byte[] old, byte[] next) =>
        current.AsSpan().SequenceEqual(old) || current.AsSpan().SequenceEqual(next);

    private static void Restore(string path, byte[] content)
    {
        if (content.Length == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else
        {
            WriteAtomic(path, content);
        }
    }

    private static byte[] ReadBounded(string path, int maximumBytes = MaximumInputBytes)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximumBytes)
        {
            throw new IOException("changelog_input_too_large");
        }

        using var memory = new MemoryStream();
        file.CopyTo(memory);
        return memory.ToArray();
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(content);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine(diagnostic);
        return Program.ToolchainRejected;
    }

    private sealed record AcceptedPublicationManifest(
        string SchemaVersion,
        string InputSetSha256,
        string ProjectionSha256,
        string? HighestStableVersion,
        IReadOnlyList<AcceptedPublicationRelease> Releases);

    private sealed record AcceptedPublicationRelease(string Version, string TagObjectId, string NotesSha256);

    private sealed record PublicationTransaction(
        string SchemaVersion,
        byte[] OldPage,
        byte[] OldManifest,
        byte[] NewPage,
        byte[] NewManifest);
}
