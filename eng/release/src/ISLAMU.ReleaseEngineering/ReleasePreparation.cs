using System.Globalization;
using System.Text;

namespace ISLAMU.ReleaseEngineering;

public sealed record ReleasePreparationRequest(
    string ReleaseDirectory,
    ReleaseInputValidationResult Input,
    ReleaseContextValidationResult Context,
    byte[] Summary,
    IReadOnlyList<string> RangeOids,
    GitCliffRenderRequest Renderer);

public sealed record ReleasePreparationResult(bool IsValid, string? Diagnostic, string? CommitMessage, byte[]? Notes);

public static class ReleasePreparation
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] HighImpacts = ["breaking", "security", "migration", "configuration", "openapi", "operator"];

    public static ReleasePreparationResult Prepare(ReleasePreparationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryValidate(request, out ReleaseDescriptor? descriptor, out string? summary, out string? impactSummary, out string? diagnostic))
        {
            return Invalid(diagnostic!);
        }

        GitCliffRenderResult rendered = GitCliffRenderer.Render(request.Renderer);
        if (!rendered.IsValid || rendered.Markdown is null)
        {
            return Invalid("prepare_renderer_failed");
        }

        string details;
        try
        {
            string markdown = StrictUtf8.GetString(rendered.Markdown);
            string expectedHeading = $"# Release {descriptor!.Version}\n";
            if (!markdown.StartsWith(expectedHeading, StringComparison.Ordinal))
            {
                return Invalid("prepare_renderer_version_mismatch");
            }

            details = markdown[expectedHeading.Length..].Trim();
        }
        catch (DecoderFallbackException)
        {
            return Invalid("prepare_renderer_failed");
        }

        string fullRange = string.Join('\n', request.RangeOids.Select(oid => $"- `{DisplayId(oid, request.RangeOids)}`"));
        var sections = new List<string> { $"# Release {descriptor!.Version}", $"## Maintainer Summary\n\n{summary}" };
        if (details.Length != 0)
        {
            sections.Add(impactSummary is null
                ? $"## Release-Visible Details\n\n{details}"
                : $"## Release-Visible Details\n\n{details}\n\n{impactSummary}");
        }
        else if (impactSummary is not null)
        {
            sections.Add($"## Release-Visible Details\n\n{impactSummary}");
        }

        sections.Add($"## Complete Commit Range\n\n{fullRange}");
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeText(string.Join("\n\n", sections));
        if (!canonical.IsValid || canonical.Bytes is null)
        {
            return Invalid("prepare_notes_not_canonical");
        }

        string notesPath = Path.Combine(request.ReleaseDirectory, "release-notes.md");
        try
        {
            if (File.Exists(notesPath))
            {
                return File.ReadAllBytes(notesPath).AsSpan().SequenceEqual(canonical.Bytes)
                    ? Valid(descriptor.Version, canonical.Bytes)
                    : Invalid("prepare_generated_file_unexpected");
            }

            string temporaryPath = Path.Combine(request.ReleaseDirectory, $".release-notes.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, canonical.Bytes);
                File.Move(temporaryPath, notesPath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid("prepare_write_failed");
        }

        return Valid(descriptor.Version, canonical.Bytes);
    }

    private static bool TryValidate(
        ReleasePreparationRequest request,
        out ReleaseDescriptor? descriptor,
        out string? summary,
        out string? impactSummary,
        out string? diagnostic)
    {
        descriptor = request.Input.Descriptor;
        summary = null;
        impactSummary = null;
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
            IsLink(Path.Combine(request.ReleaseDirectory, "release-notes.md")))
        {
            diagnostic = "prepare_release_path_invalid";
            return false;
        }

        HashSet<string> expectedEvidence = request.RangeOids.ToHashSet(StringComparer.Ordinal);
        expectedEvidence.Add(request.Context.Context.Evidence.BaseStableOid);
        expectedEvidence.Add(request.Context.Context.Evidence.PreviousPublishedOid);
        foreach (string originalOid in request.Context.Context.Changes.Select(change => change.BackportOf).OfType<string>())
        {
            expectedEvidence.Add(originalOid);
        }

        string[] actualEvidence = request.Context.Context.Evidence.Objects.Select(value => value.Oid).ToArray();
        HashSet<string> currentChangeOids = request.Context.Context.Changes.Select(change => change.Oid).ToHashSet(StringComparer.Ordinal);
        HashSet<string> backportOriginalOnly = request.Context.Context.Changes
            .Select(change => change.BackportOf)
            .OfType<string>()
            .Where(oid => !currentChangeOids.Contains(oid))
            .ToHashSet(StringComparer.Ordinal);
        string[] canonicalRange = actualEvidence.Where(oid =>
            !string.Equals(oid, request.Context.Context.Evidence.BaseStableOid, StringComparison.Ordinal) &&
            !string.Equals(oid, request.Context.Context.Evidence.PreviousPublishedOid, StringComparison.Ordinal) &&
            !backportOriginalOnly.Contains(oid)).ToArray();
        if (request.RangeOids.Count == 0 || request.RangeOids.Count > CanonicalArtifactPolicy.MaximumCollectionItems ||
            request.RangeOids.Distinct(StringComparer.Ordinal).Count() != request.RangeOids.Count ||
            request.RangeOids.Select(oid => oid.Length).Distinct().Count() != 1 ||
            request.RangeOids.Any(oid => oid.Length is not (40 or 64) || oid.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) ||
            request.Context.Context.Changes.Any(change => !request.RangeOids.Contains(change.Oid, StringComparer.Ordinal)) ||
            actualEvidence.Distinct(StringComparer.Ordinal).Count() != actualEvidence.Length ||
            !expectedEvidence.SetEquals(actualEvidence) ||
            !request.RangeOids.Order(StringComparer.Ordinal).SequenceEqual(canonicalRange, StringComparer.Ordinal))
        {
            diagnostic = "prepare_range_context_mismatch";
            return false;
        }

        var impactSections = new List<string>();
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

            var entries = new List<string>(covered.Length);
            foreach (PublicChangeFragment fragment in covered)
            {
                FragmentImpact evidence = fragment.Impacts[impact];
                CanonicalTextResult safeDetail = CanonicalArtifactPolicy.EscapeUntrustedMarkdown(evidence.Detail ?? string.Empty);
                CanonicalTextResult safeReference = CanonicalArtifactPolicy.EscapeUntrustedMarkdown(evidence.Reference);
                if (string.IsNullOrWhiteSpace(evidence.Detail) || !safeDetail.IsValid || !safeReference.IsValid)
                {
                    diagnostic = $"prepare_impact_detail_invalid:{impact}:{fragment.ChangeId}";
                    return false;
                }

                entries.Add($"- `{fragment.ChangeId}` - {disposition}: {safeDetail.Text} (Evidence: `{safeReference.Text}`)");
            }

            impactSections.Add($"#### {ImpactHeading(impact)}\n\n{string.Join('\n', entries)}");
        }

        if (impactSections.Count != 0)
        {
            impactSummary = $"### Impact Summary\n\n{string.Join("\n\n", impactSections)}";
        }

        try
        {
            string decoded = StrictUtf8.GetString(request.Summary);
            CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeText(decoded);
            if (!canonical.IsValid || canonical.Bytes is null || !request.Summary.AsSpan().SequenceEqual(canonical.Bytes))
            {
                diagnostic = "prepare_summary_not_canonical";
                return false;
            }

            summary = decoded.TrimEnd('\n');
            if (string.IsNullOrWhiteSpace(summary) ||
                summary.Contains("generated-region", StringComparison.OrdinalIgnoreCase) ||
                summary.Contains("restricted-details", StringComparison.OrdinalIgnoreCase) ||
                summary.Split('\n').Any(line => line.TrimStart().StartsWith('#') || !CanonicalArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid))
            {
                diagnostic = "prepare_summary_restricted";
                return false;
            }
        }
        catch (DecoderFallbackException)
        {
            diagnostic = "prepare_summary_not_canonical";
            return false;
        }

        return true;
    }

    private static bool IsLink(string path) => File.Exists(path) || Directory.Exists(path)
        ? (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
        : false;

    private static string DisplayId(string oid, IReadOnlyList<string> range)
    {
        int length = 12;
        while (range.Any(other => !string.Equals(other, oid, StringComparison.Ordinal) && other.StartsWith(oid[..length], StringComparison.Ordinal)))
        {
            length++;
        }

        return oid[..length];
    }

    private static string ImpactHeading(string impact) => impact switch
    {
        "openapi" => "OpenAPI",
        _ => char.ToUpperInvariant(impact[0]) + impact[1..],
    };

    private static ReleasePreparationResult Valid(string version, byte[] notes) => new(
        true,
        null,
            $"docs(release): prepare {version}\n\nChangelog: skip\nChangelog-Reason: release metadata commit\n",
        notes);

    private static ReleasePreparationResult Invalid(string diagnostic) => new(false, diagnostic, null, null);
}
