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
    private static class Diagnostics
    {
        public const string ReleaseInputInvalid = "enricher_release_input_invalid";
        public const string AuthorityContextInvalid = "enricher_authority_context_invalid";
        public const string NativeContextInvalid = "enricher_native_context_invalid";
        public const string FragmentRestricted = "enricher_fragment_embargo_or_restricted";
        public const string SummaryRestricted = "enricher_summary_restricted";
        public const string VersionMismatch = "enricher_version_mismatch";
        public const string RangeMismatch = "enricher_range_mismatch";
        public const string PreviousVersionMismatch = "enricher_previous_version_mismatch";
        public const string DuplicateNativeCommit = "enricher_duplicate_native_commit";
        public const string VisibleCommitSetMismatch = "enricher_visible_commit_set_mismatch";
        public const string BreakingFlagMismatch = "enricher_breaking_flag_mismatch";
        public const string CommitTextRestricted = "enricher_commit_text_restricted";
        public const string OmittedCommitSetMismatch = "enricher_omitted_commit_set_mismatch";
        public const string ImageManifestInvalid = "enricher_image_manifest_invalid";
    }

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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static GitCliffContextEnrichmentResult Enrich(GitCliffContextEnrichmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? preconditionError = ValidatePreconditions(request, out string sanitizedSummary);
        if (preconditionError is not null)
        {
            return Invalid(preconditionError);
        }

        if (!TryParseSingleNativeRelease(request.RawNativeContextJson, out JsonDocument? document, out JsonElement nativeRelease, out JsonElement nativeCommitsEl))
        {
            return Invalid(Diagnostics.NativeContextInvalid);
        }

        using (document!)
        {
            ReleaseContext authority = request.AuthorityContext.Context!;
            HashSet<string> rangeSet = request.RangeOids.ToHashSet(StringComparer.Ordinal);
            string? metadataError = ValidateNativeReleaseMetadata(nativeRelease, authority, request.RangeOids, rangeSet);
            if (metadataError is not null)
            {
                return Invalid(metadataError);
            }

            string? commitsError = TryProjectCommits(nativeCommitsEl, authority, request.Input.Fragments, out Dictionary<string, AllowlistedNativeCommit> nativeCommitsById);
            if (commitsError is not null)
            {
                return Invalid(commitsError);
            }

            if (!VerifyOmittedRangePartition(request.OmittedRangeOids, nativeCommitsById, rangeSet))
            {
                return Invalid(Diagnostics.OmittedCommitSetMismatch);
            }

            IReadOnlyList<EnrichedImageReference>? validatedImages = ValidateImages(request.ImageReferences, out bool imagesValid);
            if (!imagesValid)
            {
                return Invalid(Diagnostics.ImageManifestInvalid);
            }

            AllowlistedNativeRelease projected = BuildProjectedRelease(request, authority, sanitizedSummary, nativeCommitsById, validatedImages);
            AllowlistedNativeRelease[] releases = [projected];
            string json = JsonSerializer.Serialize(releases, JsonOptions) + "\n";
            return new GitCliffContextEnrichmentResult(true, releases, json, null);
        }
    }

    private static string? ValidatePreconditions(GitCliffContextEnrichmentRequest request, out string sanitizedSummary)
    {
        sanitizedSummary = string.Empty;
        if (request.Input is null || !request.Input.IsValid || request.Input.Descriptor is null)
        {
            return Diagnostics.ReleaseInputInvalid;
        }

        if (request.AuthorityContext is null || !request.AuthorityContext.IsValid || request.AuthorityContext.Context is null)
        {
            return Diagnostics.AuthorityContextInvalid;
        }

        if (string.IsNullOrWhiteSpace(request.RawNativeContextJson))
        {
            return Diagnostics.NativeContextInvalid;
        }

        if (!ValidateFragmentsArePublicSafe(request.Input.Fragments))
        {
            return Diagnostics.FragmentRestricted;
        }

        return TrySanitizeText(request.MaintainerSummary, allowMultiline: true, out sanitizedSummary)
            ? null
            : Diagnostics.SummaryRestricted;
    }

    private static bool TryParseSingleNativeRelease(
        string rawJson,
        out JsonDocument? document,
        out JsonElement nativeRelease,
        out JsonElement nativeCommitsEl)
    {
        document = null;
        nativeRelease = default;
        nativeCommitsEl = default;

        try
        {
            document = JsonDocument.Parse(rawJson, new JsonDocumentOptions { MaxDepth = 64 });
        }
        catch (JsonException)
        {
            return false;
        }

        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() != 1 ||
            document.RootElement[0].ValueKind != JsonValueKind.Object ||
            !document.RootElement[0].TryGetProperty("commits", out nativeCommitsEl) ||
            nativeCommitsEl.ValueKind != JsonValueKind.Array)
        {
            document.Dispose();
            document = null;
            return false;
        }

        nativeRelease = document.RootElement[0];
        return true;
    }

    private static string? ValidateNativeReleaseMetadata(
        JsonElement nativeRelease,
        ReleaseContext authority,
        IReadOnlyList<string> rangeOids,
        HashSet<string> rangeSet)
    {
        if (!TryGetString(nativeRelease, "version", out string? nativeVersion) ||
            string.IsNullOrWhiteSpace(nativeVersion) ||
            !VersionsMatch(nativeVersion, authority.Release.Version))
        {
            return Diagnostics.VersionMismatch;
        }

        if (rangeSet.Count != rangeOids.Count || !IsNativeRangeWithinAuthority(nativeRelease, rangeSet))
        {
            return Diagnostics.RangeMismatch;
        }

        return IsPreviousVersionConsistent(nativeRelease, authority)
            ? null
            : Diagnostics.PreviousVersionMismatch;
    }

    private static bool IsNativeRangeWithinAuthority(JsonElement nativeRelease, HashSet<string> rangeSet)
    {
        if (TryGetString(nativeRelease, "commit_id", out string? nativeCommitId) &&
            !string.IsNullOrEmpty(nativeCommitId) &&
            !rangeSet.Contains(nativeCommitId))
        {
            return false;
        }

        if (nativeRelease.TryGetProperty("commit_range", out JsonElement nativeRangeEl) &&
            nativeRangeEl.ValueKind == JsonValueKind.Object)
        {
            if (TryGetString(nativeRangeEl, "from", out string? rangeFrom) && !string.IsNullOrEmpty(rangeFrom) && !rangeSet.Contains(rangeFrom))
            {
                return false;
            }

            if (TryGetString(nativeRangeEl, "to", out string? rangeTo) && !string.IsNullOrEmpty(rangeTo) && !rangeSet.Contains(rangeTo))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPreviousVersionConsistent(JsonElement nativeRelease, ReleaseContext authority)
    {
        if (!nativeRelease.TryGetProperty("previous", out JsonElement nativePrevEl) ||
            nativePrevEl.ValueKind != JsonValueKind.Object ||
            !TryGetString(nativePrevEl, "version", out string? nativePrevVersion) ||
            string.IsNullOrEmpty(nativePrevVersion) ||
            ReleaseInputPolicy.IsBaselineRef(authority.Release.PreviousPublishedTag))
        {
            return true;
        }

        return VersionsMatch(nativePrevVersion, authority.Release.PreviousPublishedTag) ||
            VersionsMatch(nativePrevVersion, authority.Release.BaseStableTag);
    }

    private static string? TryProjectCommits(
        JsonElement nativeCommitsEl,
        ReleaseContext authority,
        IReadOnlyList<PublicChangeFragment> fragments,
        out Dictionary<string, AllowlistedNativeCommit> nativeCommitsById)
    {
        Dictionary<string, ReleaseContextChange> authorityChangesByOid = authority.Changes
            .ToDictionary(c => c.Oid, StringComparer.Ordinal);
        Dictionary<string, PublicChangeFragment> fragmentsByChangeId = fragments
            .ToDictionary(f => f.ChangeId, StringComparer.Ordinal);
        nativeCommitsById = new Dictionary<string, AllowlistedNativeCommit>(StringComparer.Ordinal);

        foreach (JsonElement nativeCommit in nativeCommitsEl.EnumerateArray())
        {
            string? commitError = TryProjectSingleCommit(nativeCommit, authorityChangesByOid, fragmentsByChangeId, nativeCommitsById);
            if (commitError is not null)
            {
                return commitError;
            }
        }

        return nativeCommitsById.Count == authorityChangesByOid.Count
            ? null
            : Diagnostics.VisibleCommitSetMismatch;
    }

    private static string? TryProjectSingleCommit(
        JsonElement nativeCommit,
        Dictionary<string, ReleaseContextChange> authorityChangesByOid,
        Dictionary<string, PublicChangeFragment> fragmentsByChangeId,
        Dictionary<string, AllowlistedNativeCommit> nativeCommitsById)
    {
        if (nativeCommit.ValueKind != JsonValueKind.Object ||
            !TryGetString(nativeCommit, "id", out string? id) ||
            string.IsNullOrWhiteSpace(id))
        {
            return Diagnostics.NativeContextInvalid;
        }

        if (nativeCommitsById.ContainsKey(id))
        {
            return Diagnostics.DuplicateNativeCommit;
        }

        if (!authorityChangesByOid.TryGetValue(id, out ReleaseContextChange? authorityChange))
        {
            return Diagnostics.VisibleCommitSetMismatch;
        }

        if (!TryGetString(nativeCommit, "group", out string? group) || string.IsNullOrWhiteSpace(group) ||
            !TryGetString(nativeCommit, "message", out string? message) || string.IsNullOrWhiteSpace(message))
        {
            return Diagnostics.NativeContextInvalid;
        }

        bool nativeBreaking = ReadBoolProperty(nativeCommit, "breaking", defaultValue: false);
        if (nativeBreaking != authorityChange.Breaking)
        {
            return Diagnostics.BreakingFlagMismatch;
        }

        if (!TrySanitizeText(message, allowMultiline: false, out string safeMessage) ||
            !TryReadSafeBreakingDescription(nativeCommit, nativeBreaking, out string? breakingDescription))
        {
            return Diagnostics.CommitTextRestricted;
        }

        AllowlistedCommitExtra commitExtra = BuildCommitExtra(authorityChange, fragmentsByChangeId);
        string? scope = TryGetString(nativeCommit, "scope", out string? nativeScope) && !string.IsNullOrWhiteSpace(nativeScope)
            ? nativeScope
            : authorityChange.Scope;

        nativeCommitsById[id] = new AllowlistedNativeCommit(
            Id: id,
            Group: group,
            Scope: scope,
            Message: safeMessage,
            Breaking: nativeBreaking,
            BreakingDescription: breakingDescription,
            Conventional: ReadBoolProperty(nativeCommit, "conventional", defaultValue: true),
            MergeCommit: ReadBoolProperty(nativeCommit, "merge_commit", defaultValue: false),
            Extra: commitExtra);
        return null;
    }

    private static bool TryReadSafeBreakingDescription(JsonElement nativeCommit, bool isBreaking, out string? breakingDescription)
    {
        breakingDescription = null;
        if (!isBreaking ||
            !TryGetString(nativeCommit, "breaking_description", out string? rawBreaking) ||
            string.IsNullOrWhiteSpace(rawBreaking))
        {
            return true;
        }

        if (!TrySanitizeText(rawBreaking, allowMultiline: true, out string safeBreaking))
        {
            return false;
        }

        breakingDescription = safeBreaking;
        return true;
    }

    private static AllowlistedCommitExtra BuildCommitExtra(
        ReleaseContextChange authorityChange,
        Dictionary<string, PublicChangeFragment> fragmentsByChangeId)
    {
        PublicChangeFragment? linkedFragment = authorityChange.ChangeId is not null &&
            fragmentsByChangeId.TryGetValue(authorityChange.ChangeId, out PublicChangeFragment? found)
            ? found
            : null;
        AllowlistedImpactEntry[]? commitImpacts = linkedFragment is not null
            ? BuildFragmentImpactEntries(linkedFragment)
            : null;

        return new AllowlistedCommitExtra(
            DisplayId: authorityChange.DisplayId,
            ChangeId: authorityChange.ChangeId,
            Title: linkedFragment is not null ? authorityChange.Title : null,
            Summary: linkedFragment is not null ? authorityChange.Summary : null,
            Backport: authorityChange.Backport,
            BackportOf: authorityChange.BackportOf,
            Impacts: commitImpacts is { Length: > 0 } ? commitImpacts : null);
    }

    private static bool VerifyOmittedRangePartition(
        IReadOnlyList<string> omittedRangeOids,
        Dictionary<string, AllowlistedNativeCommit> nativeCommitsById,
        HashSet<string> rangeSet)
    {
        HashSet<string> omittedSet = omittedRangeOids.ToHashSet(StringComparer.Ordinal);
        var combinedSet = new HashSet<string>(omittedSet, StringComparer.Ordinal);
        combinedSet.UnionWith(nativeCommitsById.Keys);
        return omittedSet.Count == omittedRangeOids.Count &&
            !omittedSet.Overlaps(nativeCommitsById.Keys) &&
            omittedSet.Count + nativeCommitsById.Count == rangeSet.Count &&
            combinedSet.SetEquals(rangeSet);
    }

    private static AllowlistedNativeRelease BuildProjectedRelease(
        GitCliffContextEnrichmentRequest request,
        ReleaseContext authority,
        string sanitizedSummary,
        Dictionary<string, AllowlistedNativeCommit> nativeCommitsById,
        IReadOnlyList<EnrichedImageReference>? validatedImages)
    {
        var rangeIndex = new Dictionary<string, int>(request.RangeOids.Count, StringComparer.Ordinal);
        for (int index = 0; index < request.RangeOids.Count; index++)
        {
            rangeIndex[request.RangeOids[index]] = index;
        }

        AllowlistedNativeCommit[] orderedCommits = nativeCommitsById.Values
            .OrderBy(c => rangeIndex.TryGetValue(c.Id, out int idx) ? idx : int.MaxValue)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, string> displayIdByOid = authority.Evidence.Objects
            .ToDictionary(o => o.Oid, o => o.DisplayId, StringComparer.Ordinal);
        string[] rangeDisplayIds = request.RangeOids
            .Select(oid => displayIdByOid.TryGetValue(oid, out string? displayId) ? displayId : oid[..Math.Min(12, oid.Length)])
            .ToArray();

        var orderedDispositions = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach ((string key, string value) in request.Input.Descriptor!.ImpactDispositions)
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
            Version: ReleaseInputPolicy.IsBaselineRef(authority.Release.PreviousPublishedTag) ? null : authority.Release.PreviousPublishedTag,
            CommitId: authority.Evidence.PreviousPublishedOid);

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

        return new AllowlistedNativeRelease(
            Version: $"v{authority.Release.Version}",
            CommitId: request.RangeOids.Count > 0 ? request.RangeOids[^1] : null,
            CommitRange: commitRange,
            Previous: previousRelease,
            Commits: orderedCommits,
            Extra: extra);
    }

    private static bool ValidateFragmentsArePublicSafe(IReadOnlyList<PublicChangeFragment> fragments) =>
        fragments.All(IsFragmentPublicSafe);

    private static bool IsFragmentPublicSafe(PublicChangeFragment fragment)
    {
        if (!TrySanitizeText(fragment.Title, allowMultiline: false, out _) ||
            !TrySanitizeText(fragment.Summary, allowMultiline: true, out _))
        {
            return false;
        }

        return fragment.Impacts.Values.All(impact =>
            !string.Equals(impact.PublicDisclosure, "embargoed", StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(impact.Detail) || TrySanitizeText(impact.Detail, allowMultiline: true, out _)));
    }

    private static AllowlistedImpactEntry[] BuildFragmentImpactEntries(PublicChangeFragment fragment) =>
        ImpactCategoryOrder
            .Where(category =>
                fragment.Impacts.TryGetValue(category, out FragmentImpact? impact) &&
                !string.Equals(impact.Disposition, "not-applicable", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(impact.Detail))
            .Select(category =>
            {
                FragmentImpact impact = fragment.Impacts[category];
                return new AllowlistedImpactEntry(
                    Category: category,
                    ChangeId: fragment.ChangeId,
                    Disposition: impact.Disposition,
                    Detail: impact.Detail!.Trim(),
                    Reference: impact.Reference);
            })
            .ToArray();

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
        if ((!allowMultiline && candidate.Contains('\n', StringComparison.Ordinal)) ||
            IdentityOrProviderPattern.IsMatch(candidate) ||
            candidate.Contains("restricted-details", StringComparison.OrdinalIgnoreCase) ||
            candidate.Split('\n').Any(line => !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool ReadBoolProperty(JsonElement obj, string property, bool defaultValue)
    {
        if (!obj.TryGetProperty(property, out JsonElement element))
        {
            return defaultValue;
        }

        return defaultValue
            ? element.ValueKind != JsonValueKind.False
            : element.ValueKind == JsonValueKind.True;
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
