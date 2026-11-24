using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record EnrichedImageReference(
    string ImageName,
    string Digest,
    IReadOnlyList<string> CanonicalReferences);

public sealed record GitCliffContextEnrichmentRequest(
    string RawNativeContextJson,
    ReleaseInputValidationResult Input,
    ReleaseContextValidationResult AuthorityContext,
    IReadOnlyList<string> RangeOids,
    IReadOnlyList<string> OmittedRangeOids,
    string MaintainerSummary,
    ReleaseContractDiffArtifact? ContractDiff,
    IReadOnlyList<EnrichedImageReference>? ImageReferences);

public sealed record GitCliffContextEnrichmentResult(
    bool IsValid,
    IReadOnlyList<AllowlistedNativeRelease>? Releases,
    string? Json,
    string? Diagnostic);

public sealed record AllowlistedNativeRelease(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("commit_id")] string? CommitId,
    [property: JsonPropertyName("commit_range")] AllowlistedCommitRange? CommitRange,
    [property: JsonPropertyName("previous")] AllowlistedPreviousRelease? Previous,
    [property: JsonPropertyName("commits")] IReadOnlyList<AllowlistedNativeCommit> Commits,
    [property: JsonPropertyName("extra")] AllowlistedReleaseExtra Extra);

public sealed record AllowlistedCommitRange(
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("to")] string To);

public sealed record AllowlistedPreviousRelease(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("commit_id")] string? CommitId);

public sealed record AllowlistedNativeCommit(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("group")] string Group,
    [property: JsonPropertyName("scope")] string? Scope,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("breaking")] bool Breaking,
    [property: JsonPropertyName("breaking_description")] string? BreakingDescription,
    [property: JsonPropertyName("conventional")] bool Conventional,
    [property: JsonPropertyName("merge_commit")] bool MergeCommit,
    [property: JsonPropertyName("extra")] AllowlistedCommitExtra Extra);

public sealed record AllowlistedCommitExtra(
    [property: JsonPropertyName("displayId")] string DisplayId,
    [property: JsonPropertyName("changeId")] string? ChangeId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("backport")] bool Backport,
    [property: JsonPropertyName("backportOf")] string? BackportOf,
    [property: JsonPropertyName("impacts")] IReadOnlyList<AllowlistedImpactEntry>? Impacts);

public sealed record AllowlistedImpactEntry(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("changeId")] string ChangeId,
    [property: JsonPropertyName("disposition")] string Disposition,
    [property: JsonPropertyName("detail")] string Detail,
    [property: JsonPropertyName("reference")] string Reference);

public sealed record AllowlistedReleaseExtra(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("line")] string Line,
    [property: JsonPropertyName("releaseDate")] string ReleaseDate,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("minimumBump")] string MinimumBump,
    [property: JsonPropertyName("baseStableTag")] string BaseStableTag,
    [property: JsonPropertyName("previousPublishedTag")] string PreviousPublishedTag,
    [property: JsonPropertyName("advancesMain")] bool AdvancesMain,
    [property: JsonPropertyName("maintainerSummary")] string MaintainerSummary,
    [property: JsonPropertyName("rangeDisplayIds")] IReadOnlyList<string> RangeDisplayIds,
    [property: JsonPropertyName("impactDispositions")] IReadOnlyDictionary<string, string> ImpactDispositions,
    [property: JsonPropertyName("impactEntries")] IReadOnlyList<AllowlistedImpactEntry> ImpactEntries,
    [property: JsonPropertyName("contractDiff")] ReleaseContractDiffArtifact? ContractDiff,
    [property: JsonPropertyName("images")] IReadOnlyList<EnrichedImageReference>? Images);

/// <summary>
/// Reconstructs an allowlisted, privacy-safe native <c>git-cliff</c> context array
/// (<c>release-rendering-context.v1.json</c>) from transient Stage 1 JSON, correlating every
/// release, range, visible commit, and omitted commit against C# <see cref="ReleaseContextPolicy"/>
/// authority. Never copies unknown or identity-bearing native fields.
/// </summary>
public static class GitCliffContextEnricher
{
    private static readonly string[] ImpactCategoryOrder =
    [
        "breaking",
        "security",
        "migration",
        "configuration",
        "openapi",
        "operator",
    ];

    private static readonly Regex IdentityOrProviderPattern = new(
        "(?:[\\p{L}\\p{N}._%+-]+@[\\p{L}\\p{N}.-]+\\.[\\p{L}]{2,}|https?://(?:www\\.)?(?:github\\.com|gitlab\\.com|codeberg\\.org|bitbucket\\.org)/\\S+|(?<![\\w@])@[A-Za-z0-9][A-Za-z0-9-]{0,38}(?![\\w-])|\\b(?:workflow|pipeline|job|run)[ _-]?id\\s*[:=]\\s*[0-9]{3,}\\b)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static GitCliffContextEnrichmentResult Enrich(GitCliffContextEnrichmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Input is null || !request.Input.IsValid || request.Input.Descriptor is null)
        {
            return Invalid("enricher_release_input_invalid");
        }

        if (request.AuthorityContext is null || !request.AuthorityContext.IsValid || request.AuthorityContext.Context is null)
        {
            return Invalid("enricher_authority_context_invalid");
        }

        if (string.IsNullOrWhiteSpace(request.RawNativeContextJson))
        {
            return Invalid("enricher_native_context_invalid");
        }

        ReleaseDescriptor descriptor = request.Input.Descriptor;
        ReleaseContext authority = request.AuthorityContext.Context;

        if (!ValidateFragmentsArePublicSafe(request.Input.Fragments))
        {
            return Invalid("enricher_fragment_embargo_or_restricted");
        }

        if (!TrySanitizeText(request.MaintainerSummary, allowMultiline: true, out string sanitizedSummary))
        {
            return Invalid("enricher_summary_restricted");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(request.RawNativeContextJson, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return Invalid("enricher_native_context_invalid");
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() != 1)
            {
                return Invalid("enricher_native_context_invalid");
            }

            JsonElement nativeRelease = doc.RootElement[0];
            if (nativeRelease.ValueKind != JsonValueKind.Object)
            {
                return Invalid("enricher_native_context_invalid");
            }

            if (!TryGetString(nativeRelease, "version", out string? nativeVersion) || string.IsNullOrWhiteSpace(nativeVersion))
            {
                return Invalid("enricher_version_mismatch");
            }

            string normalizedNativeVersion = nativeVersion.StartsWith('v') ? nativeVersion[1..] : nativeVersion;
            if (!string.Equals(normalizedNativeVersion, authority.Release.Version, StringComparison.Ordinal))
            {
                return Invalid("enricher_version_mismatch");
            }

            if (!nativeRelease.TryGetProperty("commits", out JsonElement nativeCommitsEl) ||
                nativeCommitsEl.ValueKind != JsonValueKind.Array)
            {
                return Invalid("enricher_native_context_invalid");
            }

            HashSet<string> rangeSet = request.RangeOids.ToHashSet(StringComparer.Ordinal);
            if (rangeSet.Count != request.RangeOids.Count)
            {
                return Invalid("enricher_range_mismatch");
            }

            if (TryGetString(nativeRelease, "commit_id", out string? nativeCommitId) &&
                !string.IsNullOrEmpty(nativeCommitId) &&
                !rangeSet.Contains(nativeCommitId))
            {
                return Invalid("enricher_range_mismatch");
            }

            if (nativeRelease.TryGetProperty("commit_range", out JsonElement nativeRangeEl) &&
                nativeRangeEl.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(nativeRangeEl, "from", out string? rangeFrom) && !string.IsNullOrEmpty(rangeFrom) && !rangeSet.Contains(rangeFrom))
                {
                    return Invalid("enricher_range_mismatch");
                }

                if (TryGetString(nativeRangeEl, "to", out string? rangeTo) && !string.IsNullOrEmpty(rangeTo) && !rangeSet.Contains(rangeTo))
                {
                    return Invalid("enricher_range_mismatch");
                }
            }

            if (nativeRelease.TryGetProperty("previous", out JsonElement nativePrevEl) &&
                nativePrevEl.ValueKind == JsonValueKind.Object &&
                TryGetString(nativePrevEl, "version", out string? nativePrevVersion) &&
                !string.IsNullOrEmpty(nativePrevVersion) &&
                !ReleaseInputPolicy.IsBaselineRef(authority.Release.PreviousPublishedTag))
            {
                string expectedPrev = authority.Release.PreviousPublishedTag;
                string expectedBase = authority.Release.BaseStableTag;
                if (!VersionsMatch(nativePrevVersion, expectedPrev) && !VersionsMatch(nativePrevVersion, expectedBase))
                {
                    return Invalid("enricher_previous_version_mismatch");
                }
            }

            // Correlate visible commits independently with C# authority.
            Dictionary<string, ReleaseContextChange> authorityChangesByOid = authority.Changes
                .ToDictionary(c => c.Oid, StringComparer.Ordinal);
            Dictionary<string, PublicChangeFragment> fragmentsByChangeId = request.Input.Fragments
                .ToDictionary(f => f.ChangeId, StringComparer.Ordinal);
            Dictionary<string, string> displayIdByOid = authority.Evidence.Objects
                .ToDictionary(o => o.Oid, o => o.DisplayId, StringComparer.Ordinal);

            var nativeCommitsById = new Dictionary<string, AllowlistedNativeCommit>(StringComparer.Ordinal);
            foreach (JsonElement nativeCommit in nativeCommitsEl.EnumerateArray())
            {
                if (nativeCommit.ValueKind != JsonValueKind.Object ||
                    !TryGetString(nativeCommit, "id", out string? id) ||
                    string.IsNullOrWhiteSpace(id))
                {
                    return Invalid("enricher_native_context_invalid");
                }

                if (nativeCommitsById.ContainsKey(id))
                {
                    return Invalid("enricher_duplicate_native_commit");
                }

                if (!authorityChangesByOid.TryGetValue(id, out ReleaseContextChange? authorityChange))
                {
                    return Invalid("enricher_visible_commit_set_mismatch");
                }

                if (!TryGetString(nativeCommit, "group", out string? group) || string.IsNullOrWhiteSpace(group))
                {
                    return Invalid("enricher_native_context_invalid");
                }

                if (!TryGetString(nativeCommit, "message", out string? message) || string.IsNullOrWhiteSpace(message))
                {
                    return Invalid("enricher_native_context_invalid");
                }

                bool nativeBreaking = nativeCommit.TryGetProperty("breaking", out JsonElement breakingEl) &&
                    breakingEl.ValueKind == JsonValueKind.True;
                if (nativeBreaking != authorityChange.Breaking)
                {
                    return Invalid("enricher_breaking_flag_mismatch");
                }

                bool conventional = !nativeCommit.TryGetProperty("conventional", out JsonElement convEl) ||
                    convEl.ValueKind != JsonValueKind.False;
                bool mergeCommit = nativeCommit.TryGetProperty("merge_commit", out JsonElement mergeEl) &&
                    mergeEl.ValueKind == JsonValueKind.True;

                string? scope = TryGetString(nativeCommit, "scope", out string? nativeScope) && !string.IsNullOrWhiteSpace(nativeScope)
                    ? nativeScope
                    : authorityChange.Scope;

                string? breakingDescription = null;
                if (nativeBreaking &&
                    TryGetString(nativeCommit, "breaking_description", out string? rawBreaking) &&
                    !string.IsNullOrWhiteSpace(rawBreaking))
                {
                    if (!TrySanitizeText(rawBreaking, allowMultiline: true, out string safeBreaking))
                    {
                        return Invalid("enricher_commit_text_restricted");
                    }

                    breakingDescription = safeBreaking;
                }

                if (!TrySanitizeText(message, allowMultiline: false, out string safeMessage))
                {
                    return Invalid("enricher_commit_text_restricted");
                }

                PublicChangeFragment? linkedFragment = authorityChange.ChangeId is not null &&
                    fragmentsByChangeId.TryGetValue(authorityChange.ChangeId, out PublicChangeFragment? f)
                    ? f
                    : null;
                AllowlistedImpactEntry[]? commitImpacts = linkedFragment is not null
                    ? BuildFragmentImpactEntries(linkedFragment)
                    : null;

                var commitExtra = new AllowlistedCommitExtra(
                    DisplayId: authorityChange.DisplayId,
                    ChangeId: authorityChange.ChangeId,
                    Title: linkedFragment is not null ? authorityChange.Title : null,
                    Summary: linkedFragment is not null ? authorityChange.Summary : null,
                    Backport: authorityChange.Backport,
                    BackportOf: authorityChange.BackportOf,
                    Impacts: commitImpacts is { Length: > 0 } ? commitImpacts : null);

                nativeCommitsById[id] = new AllowlistedNativeCommit(
                    Id: id,
                    Group: group,
                    Scope: scope,
                    Message: safeMessage,
                    Breaking: nativeBreaking,
                    BreakingDescription: breakingDescription,
                    Conventional: conventional,
                    MergeCommit: mergeCommit,
                    Extra: commitExtra);
            }

            if (nativeCommitsById.Count != authorityChangesByOid.Count)
            {
                return Invalid("enricher_visible_commit_set_mismatch");
            }

            // Correlate omitted/skipped commits independently against bounded range.
            HashSet<string> omittedSet = request.OmittedRangeOids.ToHashSet(StringComparer.Ordinal);
            var combinedSet = new HashSet<string>(omittedSet, StringComparer.Ordinal);
            combinedSet.UnionWith(nativeCommitsById.Keys);
            if (omittedSet.Count != request.OmittedRangeOids.Count ||
                omittedSet.Overlaps(nativeCommitsById.Keys) ||
                omittedSet.Count + nativeCommitsById.Count != rangeSet.Count ||
                !combinedSet.SetEquals(rangeSet))
            {
                return Invalid("enricher_omitted_commit_set_mismatch");
            }

            // Deterministic ordering by chronological RangeOids index, then OID.
            var rangeIndex = new Dictionary<string, int>(request.RangeOids.Count, StringComparer.Ordinal);
            for (int index = 0; index < request.RangeOids.Count; index++)
            {
                rangeIndex[request.RangeOids[index]] = index;
            }

            AllowlistedNativeCommit[] orderedCommits = nativeCommitsById.Values
                .OrderBy(c => rangeIndex.TryGetValue(c.Id, out int idx) ? idx : int.MaxValue)
                .ThenBy(c => c.Id, StringComparer.Ordinal)
                .ToArray();

            string[] rangeDisplayIds = request.RangeOids
                .Select(oid => displayIdByOid.TryGetValue(oid, out string? displayId) ? displayId : oid[..Math.Min(12, oid.Length)])
                .ToArray();

            var orderedDispositions = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach ((string key, string value) in descriptor.ImpactDispositions)
            {
                orderedDispositions[key] = value;
            }

            AllowlistedImpactEntry[] allImpactEntries = request.Input.Fragments
                .SelectMany(BuildFragmentImpactEntries)
                .OrderBy(entry => Array.IndexOf(ImpactCategoryOrder, entry.Category))
                .ThenBy(entry => entry.ChangeId, StringComparer.Ordinal)
                .ThenBy(entry => entry.Reference, StringComparer.Ordinal)
                .ToArray();

            AllowlistedCommitRange? commitRange = request.RangeOids.Count > 0
                ? new AllowlistedCommitRange(request.RangeOids[0], request.RangeOids[^1])
                : null;
            AllowlistedPreviousRelease previousRelease = new(
                Version: ReleaseInputPolicy.IsBaselineRef(authority.Release.PreviousPublishedTag)
                    ? null
                    : authority.Release.PreviousPublishedTag,
                CommitId: authority.Evidence.PreviousPublishedOid);

            IReadOnlyList<EnrichedImageReference>? validatedImages = ValidateImages(request.ImageReferences, out bool imagesValid);
            if (!imagesValid)
            {
                return Invalid("enricher_image_manifest_invalid");
            }

            var extra = new AllowlistedReleaseExtra(
                SchemaVersion: 1,
                Version: authority.Release.Version,
                Line: authority.Release.Line,
                ReleaseDate: authority.Release.ReleaseDate,
                Channel: authority.Release.Channel,
                MinimumBump: authority.Release.MinimumBump,
                BaseStableTag: authority.Release.BaseStableTag,
                PreviousPublishedTag: authority.Release.PreviousPublishedTag,
                AdvancesMain: authority.Release.AdvancesMain,
                MaintainerSummary: sanitizedSummary,
                RangeDisplayIds: rangeDisplayIds,
                ImpactDispositions: orderedDispositions,
                ImpactEntries: allImpactEntries,
                ContractDiff: request.ContractDiff,
                Images: validatedImages);

            var projectedRelease = new AllowlistedNativeRelease(
                Version: $"v{authority.Release.Version}",
                CommitId: request.RangeOids.Count > 0 ? request.RangeOids[^1] : null,
                CommitRange: commitRange,
                Previous: previousRelease,
                Commits: orderedCommits,
                Extra: extra);

            AllowlistedNativeRelease[] releases = [projectedRelease];
            string json = JsonSerializer.Serialize(releases, JsonOptions) + "\n";
            return new GitCliffContextEnrichmentResult(true, releases, json, null);
        }
    }

    private static bool ValidateFragmentsArePublicSafe(IReadOnlyList<PublicChangeFragment> fragments)
    {
        foreach (PublicChangeFragment fragment in fragments)
        {
            if (!TrySanitizeText(fragment.Title, allowMultiline: false, out _) ||
                !TrySanitizeText(fragment.Summary, allowMultiline: true, out _))
            {
                return false;
            }

            foreach ((_, FragmentImpact impact) in fragment.Impacts)
            {
                if (string.Equals(impact.PublicDisclosure, "embargoed", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(impact.Detail) &&
                    !TrySanitizeText(impact.Detail, allowMultiline: true, out _))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static AllowlistedImpactEntry[] BuildFragmentImpactEntries(PublicChangeFragment fragment)
    {
        var entries = new List<AllowlistedImpactEntry>();
        foreach (string category in ImpactCategoryOrder)
        {
            if (!fragment.Impacts.TryGetValue(category, out FragmentImpact? impact) ||
                string.Equals(impact.Disposition, "not-applicable", StringComparison.Ordinal))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(impact.Detail))
            {
                continue;
            }

            entries.Add(new AllowlistedImpactEntry(
                Category: category,
                ChangeId: fragment.ChangeId,
                Disposition: impact.Disposition,
                Detail: impact.Detail.Trim(),
                Reference: impact.Reference));
        }

        return entries.ToArray();
    }

    private static IReadOnlyList<EnrichedImageReference>? ValidateImages(
        IReadOnlyList<EnrichedImageReference>? images,
        out bool isValid)
    {
        isValid = true;
        if (images is null || images.Count == 0)
        {
            return null;
        }

        var normalized = new List<EnrichedImageReference>(images.Count);
        foreach (EnrichedImageReference image in images.OrderBy(i => i.ImageName, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(image.ImageName) ||
                string.IsNullOrWhiteSpace(image.Digest) ||
                !image.Digest.StartsWith("sha256:", StringComparison.Ordinal) ||
                image.CanonicalReferences is null ||
                image.CanonicalReferences.Count == 0 ||
                image.CanonicalReferences.Any(r => string.IsNullOrWhiteSpace(r) || !r.Contains("@sha256:", StringComparison.Ordinal)))
            {
                isValid = false;
                return null;
            }

            normalized.Add(new EnrichedImageReference(
                image.ImageName,
                image.Digest,
                image.CanonicalReferences.Order(StringComparer.Ordinal).ToArray()));
        }

        return normalized;
    }

    private static bool TrySanitizeText(string? text, bool allowMultiline, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string candidate = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (!allowMultiline && candidate.Contains('\n', StringComparison.Ordinal))
        {
            return false;
        }

        if (IdentityOrProviderPattern.IsMatch(candidate) ||
            candidate.Contains("restricted-details", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string line in candidate.Split('\n'))
        {
            if (!ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid)
            {
                return false;
            }
        }

        normalized = candidate;
        return true;
    }

    private static bool VersionsMatch(string left, string right)
    {
        string l = left.StartsWith('v') ? left[1..] : left;
        string r = right.StartsWith('v') ? right[1..] : right;
        return string.Equals(l, r, StringComparison.Ordinal);
    }

    private static bool TryGetString(JsonElement obj, string property, out string? value)
    {
        if (obj.TryGetProperty(property, out JsonElement element) && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static GitCliffContextEnrichmentResult Invalid(string diagnostic) =>
        new(false, null, null, diagnostic);
}
