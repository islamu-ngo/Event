using System.Globalization;
using System.Text;

namespace ISLAMU.ReleaseEngineering;

public sealed record ReleasePreparationRequest(
    string ReleaseDirectory,
    string RepositoryRoot,
    ReleaseInputValidationResult Input,
    ReleaseContextValidationResult Context,
    byte[] Summary,
    IReadOnlyList<string> RangeOids,
    IReadOnlyList<string> OmittedRangeOids,
    GitCliffToolConfiguration Tool,
    IGitCliffExecutor Executor,
    string? NativeCommitRange,
    ReleaseContractDiffArtifact? ContractDiff = null,
    IReadOnlyList<EnrichedImageReference>? ImageReferences = null);

public sealed record ReleasePreparationResult(
    bool IsValid,
    string? Diagnostic,
    string? CommitMessage,
    byte[]? Notes,
    byte[]? RenderingContextBytes = null);

/// <summary>
/// Orchestrates the three-stage release-note preparation pipeline:
/// Stage 1 offline <c>git-cliff --context</c> export (transient),
/// Stage 2 allowlisted C# authority correlation and enrichment (<c>release-rendering-context.v1.json</c>),
/// and Stage 3 offline <c>git-cliff --from-context -</c> rendering guarded by <see cref="ReleaseArtifactPolicy"/>.
/// </summary>
public static class ReleasePreparation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] HighImpacts = ["breaking", "security", "migration", "configuration", "openapi", "operator"];

    public static ReleasePreparationResult Prepare(ReleasePreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryValidate(request, out ReleaseDescriptor? descriptor, out string? summary, out string? diagnostic))
        {
            return Invalid(diagnostic!);
        }

        // Stage 1: transient native git-cliff --context export (never persisted or logged).
        GitCliffContextExportResult exported = request.Executor.ExportContext(new GitCliffContextExportRequest(
            request.Tool,
            request.RepositoryRoot,
            $"v{descriptor!.Version}",
            request.NativeCommitRange));
        if (!exported.IsValid || string.IsNullOrWhiteSpace(exported.RawContextJson))
        {
            return Invalid(exported.Diagnostic ?? "prepare_renderer_failed");
        }

        // Stage 2: allowlist reconstruction, C# authority correlation, and curated extra enrichment.
        GitCliffContextEnrichmentResult enriched = GitCliffContextEnricher.Enrich(new GitCliffContextEnrichmentRequest(
            exported.RawContextJson,
            request.Input,
            request.Context,
            request.RangeOids,
            request.OmittedRangeOids,
            summary!,
            request.ContractDiff,
            request.ImageReferences));
        if (!enriched.IsValid || string.IsNullOrWhiteSpace(enriched.Json))
        {
            return Invalid(enriched.Diagnostic ?? "prepare_context_enrichment_failed");
        }

        // Stage 3: offline --from-context - Tera rendering through verified git-cliff.
        GitCliffRenderFromContextResult rendered = request.Executor.RenderFromContext(new GitCliffRenderFromContextRequest(
            request.Tool,
            request.ReleaseDirectory,
            enriched.Json));
        if (!rendered.IsValid || string.IsNullOrWhiteSpace(rendered.Markdown))
        {
            return Invalid("prepare_renderer_failed");
        }

        // Post-render policy gate (identities, secrets, HTML, embargo, UTF-8 LF normalization).
        ArtifactPolicyResult validatedNotes = ReleaseArtifactPolicy.ValidateRenderedReleaseNotes(rendered.Markdown);
        if (!validatedNotes.IsValid || validatedNotes.Bytes is null)
        {
            string notesDiagnostic = validatedNotes.Diagnostics.Count == 0
                ? "prepare_notes_not_normalized"
                : validatedNotes.Diagnostics[0];
            return Invalid(notesDiagnostic);
        }

        string normalizedMarkdown = StrictUtf8.GetString(validatedNotes.Bytes);
        string expectedHeading = $"# Release {descriptor.Version}\n";
        if (!normalizedMarkdown.StartsWith(expectedHeading, StringComparison.Ordinal))
        {
            return Invalid("prepare_renderer_version_mismatch");
        }

        byte[] renderingContextBytes = StrictUtf8.GetBytes(enriched.Json);
        string renderingContextPath = Path.Combine(request.ReleaseDirectory, "release-rendering-context.v1.json");
        string notesPath = Path.Combine(request.ReleaseDirectory, "release-notes.md");

        try
        {
            if (File.Exists(renderingContextPath) &&
                !File.ReadAllBytes(renderingContextPath).AsSpan().SequenceEqual(renderingContextBytes))
            {
                return Invalid("prepare_generated_file_unexpected");
            }

            if (File.Exists(notesPath) &&
                !File.ReadAllBytes(notesPath).AsSpan().SequenceEqual(validatedNotes.Bytes))
            {
                return Invalid("prepare_generated_file_unexpected");
            }

            if (!File.Exists(renderingContextPath))
            {
                WriteAtomic(renderingContextPath, renderingContextBytes);
            }

            if (!File.Exists(notesPath))
            {
                WriteAtomic(notesPath, validatedNotes.Bytes);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("prepare_write_failed");
        }

        return Valid(descriptor.Version, validatedNotes.Bytes, renderingContextBytes);
    }

    private static bool TryValidate(
        ReleasePreparationRequest request,
        out ReleaseDescriptor? descriptor,
        out string? summary,
        out string? diagnostic)
    {
        descriptor = request.Input.Descriptor;
        summary = null;
        diagnostic = null;
        if (!request.Input.IsValid || descriptor is null)
        {
            diagnostic = "prepare_release_input_invalid";
            return false;
        }

        if (!request.Context.IsValid || request.Context.Context is null || request.Context.Json is null)
        {
            diagnostic = "prepare_context_invalid";
            return false;
        }

        ReleaseContextRelease release = request.Context.Context.Release;
        if (!string.Equals(release.Version, descriptor.Version, StringComparison.Ordinal) ||
            !string.Equals(release.Line, descriptor.Line, StringComparison.Ordinal) ||
            !string.Equals(release.ReleaseDate, descriptor.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            !string.Equals(release.BaseStableTag, descriptor.BaseStableTag, StringComparison.Ordinal) ||
            !string.Equals(release.PreviousPublishedTag, descriptor.PreviousPublishedTag, StringComparison.Ordinal) ||
            !string.Equals(request.Context.Context.Evidence.BaseStableOid, descriptor.ReleaseRange.BaseOid, StringComparison.Ordinal) ||
            !string.Equals(request.Context.Context.Evidence.PreviousPublishedOid, descriptor.ReleaseRange.PreviousOid, StringComparison.Ordinal))
        {
            diagnostic = "prepare_context_release_mismatch";
            return false;
        }

        foreach (PublicChangeFragment fragment in request.Input.Fragments)
        {
            ReleaseContextChange[] matches = request.Context.Context.Changes.Where(change => change.ChangeId == fragment.ChangeId).Take(2).ToArray();
            if (matches.Length != 1 ||
                !string.Equals(matches[0].Type, fragment.Type, StringComparison.Ordinal) ||
                !string.Equals(matches[0].Scope, fragment.Scope, StringComparison.Ordinal) ||
                !string.Equals(matches[0].Summary, fragment.Summary, StringComparison.Ordinal))
            {
                diagnostic = $"prepare_fragment_context_mismatch:{fragment.ChangeId}";
                return false;
            }
        }

        if (!Path.IsPathFullyQualified(request.ReleaseDirectory) ||
            !Directory.Exists(request.ReleaseDirectory) ||
            !string.Equals(Path.GetFileName(request.ReleaseDirectory), descriptor.Version, StringComparison.Ordinal) ||
            IsLink(request.ReleaseDirectory) ||
            IsLink(Path.Combine(request.ReleaseDirectory, "release.yaml")) ||
            IsLink(Path.Combine(request.ReleaseDirectory, "summary.md")) ||
            IsLink(Path.Combine(request.ReleaseDirectory, "release-notes.md")) ||
            IsLink(Path.Combine(request.ReleaseDirectory, "release-rendering-context.v1.json")))
        {
            diagnostic = "prepare_release_path_invalid";
            return false;
        }

        HashSet<string> rangeSet = request.RangeOids.ToHashSet(StringComparer.Ordinal);
        HashSet<string> omittedSet = request.OmittedRangeOids.ToHashSet(StringComparer.Ordinal);
        HashSet<string> currentChangeOids = request.Context.Context.Changes.Select(change => change.Oid).ToHashSet(StringComparer.Ordinal);
        if (request.RangeOids.Count == 0 || request.RangeOids.Count > ReleaseArtifactPolicy.MaximumCollectionItems ||
            rangeSet.Count != request.RangeOids.Count ||
            omittedSet.Count != request.OmittedRangeOids.Count ||
            !omittedSet.IsSubsetOf(rangeSet) ||
            omittedSet.Overlaps(currentChangeOids))
        {
            diagnostic = "prepare_range_context_mismatch";
            return false;
        }

        string[] visibleRangeOids = request.RangeOids.Where(oid => !omittedSet.Contains(oid)).ToArray();
        HashSet<string> expectedEvidence = visibleRangeOids.ToHashSet(StringComparer.Ordinal);
        expectedEvidence.Add(request.Context.Context.Evidence.BaseStableOid);
        expectedEvidence.Add(request.Context.Context.Evidence.PreviousPublishedOid);
        foreach (string originalOid in request.Context.Context.Changes.Select(change => change.BackportOf).OfType<string>())
        {
            expectedEvidence.Add(originalOid);
        }

        string[] actualEvidence = request.Context.Context.Evidence.Objects.Select(value => value.Oid).ToArray();
        HashSet<string> backportOriginalOnly = request.Context.Context.Changes
            .Select(change => change.BackportOf)
            .OfType<string>()
            .Where(oid => !currentChangeOids.Contains(oid))
            .ToHashSet(StringComparer.Ordinal);
        string[] normalizedRange = actualEvidence.Where(oid =>
            !string.Equals(oid, request.Context.Context.Evidence.BaseStableOid, StringComparison.Ordinal) &&
            !string.Equals(oid, request.Context.Context.Evidence.PreviousPublishedOid, StringComparison.Ordinal) &&
            !backportOriginalOnly.Contains(oid)).ToArray();
        if (request.RangeOids.Select(oid => oid.Length).Distinct().Count() != 1 ||
            request.RangeOids.Any(oid => oid.Length is not (40 or 64) || oid.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) ||
            request.Context.Context.Changes.Any(change => !rangeSet.Contains(change.Oid)) ||
            actualEvidence.Distinct(StringComparer.Ordinal).Count() != actualEvidence.Length ||
            !expectedEvidence.SetEquals(actualEvidence) ||
            !visibleRangeOids.Order(StringComparer.Ordinal).SequenceEqual(normalizedRange, StringComparer.Ordinal))
        {
            diagnostic = "prepare_range_context_mismatch";
            return false;
        }

        foreach (string impact in HighImpacts)
        {
            if (!descriptor.ImpactDispositions.TryGetValue(impact, out string? disposition))
            {
                diagnostic = $"prepare_impact_missing:{impact}";
                return false;
            }

            bool applicable = !string.Equals(disposition, "not-applicable", StringComparison.Ordinal);
            bool drifted = request.Input.Fragments.Any(fragment =>
                fragment.Impacts.TryGetValue(impact, out FragmentImpact? evidence) &&
                !string.Equals(evidence.Disposition, "not-applicable", StringComparison.Ordinal) &&
                !string.Equals(evidence.Disposition, disposition, StringComparison.Ordinal));
            PublicChangeFragment[] covered = applicable
                ? request.Input.Fragments.Where(fragment =>
                    fragment.Impacts.TryGetValue(impact, out FragmentImpact? evidence) &&
                    string.Equals(evidence.Disposition, disposition, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(evidence.Reference)).OrderBy(fragment => fragment.ChangeId, StringComparer.Ordinal).ToArray()
                : [];
            if (drifted || applicable != (covered.Length != 0))
            {
                diagnostic = $"prepare_impact_not_covered:{impact}";
                return false;
            }

            if (!applicable)
            {
                continue;
            }

            foreach (PublicChangeFragment fragment in covered)
            {
                FragmentImpact evidence = fragment.Impacts[impact];
                UntrustedTextResult safeDetail = ReleaseArtifactPolicy.EscapeUntrustedMarkdown(evidence.Detail ?? string.Empty);
                UntrustedTextResult safeReference = ReleaseArtifactPolicy.EscapeUntrustedMarkdown(evidence.Reference);
                if (string.IsNullOrWhiteSpace(evidence.Detail) || !safeDetail.IsValid || !safeReference.IsValid)
                {
                    diagnostic = $"prepare_impact_detail_invalid:{impact}:{fragment.ChangeId}";
                    return false;
                }
            }
        }

        try
        {
            string decoded = StrictUtf8.GetString(request.Summary);
            ArtifactPolicyResult normalized = ReleaseArtifactPolicy.NormalizeText(decoded);
            if (!normalized.IsValid || normalized.Bytes is null || !request.Summary.AsSpan().SequenceEqual(normalized.Bytes))
            {
                diagnostic = "prepare_summary_not_normalized";
                return false;
            }

            summary = decoded.TrimEnd('\n');
            if (string.IsNullOrWhiteSpace(summary) ||
                summary.Contains("generated-region", StringComparison.OrdinalIgnoreCase) ||
                summary.Contains("restricted-details", StringComparison.OrdinalIgnoreCase) ||
                summary.Split('\n').Any(line => line.TrimStart().StartsWith('#') || !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid))
            {
                diagnostic = "prepare_summary_restricted";
                return false;
            }
        }
        catch (DecoderFallbackException)
        {
            diagnostic = "prepare_summary_not_normalized";
            return false;
        }

        return true;
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
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsLink(string path) => File.Exists(path) || Directory.Exists(path)
        ? (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
        : false;

    private static ReleasePreparationResult Valid(string version, byte[] notes, byte[] renderingContextBytes) => new(
        true,
        null,
        $"docs(release): prepare {version}\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n",
        notes,
        renderingContextBytes);

    private static ReleasePreparationResult Invalid(string diagnostic) => new(false, diagnostic, null, null, null);
}
