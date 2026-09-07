using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace ISLAMU.ReleaseEngineering;

public sealed record ReleaseInputValidationResult(
    bool IsValid,
    ReleaseDescriptor? Descriptor,
    IReadOnlyList<PublicChangeFragment> Fragments,
    IReadOnlyList<string> Diagnostics);

public sealed record ReleaseDescriptor(
    string Version,
    string Line,
    DateOnly ReleaseDate,
    string BaseStableTag,
    string PreviousPublishedTag,
    ReleaseRangeReference ReleaseRange,
    IReadOnlyList<string> Compatibility,
    IReadOnlyDictionary<string, string> ImpactDispositions);

public sealed record ReleaseRangeReference(
    string BaseRef,
    string BaseOid,
    string PreviousRef,
    string PreviousOid);

public sealed record PublicChangeFragment(
    string ChangeId,
    string Title,
    string Type,
    string Scope,
    string Summary,
    string? Group,
    string? BackportOf,
    IReadOnlyList<string> Supersedes,
    IReadOnlyDictionary<string, FragmentImpact> Impacts,
    string CanonicalSnapshot);

public sealed record FragmentImpact(
    string Reference,
    string Disposition,
    string? PublicDisclosure,
    string? Detail);

public sealed record RefNamespaceDecision(bool IsAllowed, string? Diagnostic);

/// <summary>
/// Governs which Git ref names the release model is allowed to create.
/// Version tags own the <c>v*</c> glob outright: a branch named <c>v0.1</c> beside tag <c>v0.1.0</c>
/// would let a bare name resolve to either object depending on Git's disambiguation order, so branch
/// creation in that namespace is refused by policy instead of being disambiguated after the fact.
/// Maintenance lines therefore use <c>release/&lt;major&gt;.&lt;minor&gt;</c> and are opened on demand
/// from a verified signed stable tag, never provisioned eagerly at release time.
/// </summary>
public static class ReleaseRefNamespacePolicy
{
    private const string BranchPrefix = "refs/heads/";
    private static readonly Regex MaintenanceBranchPattern = new("^release/(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ReservedBranchPattern = new("^v.*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex LineLabelPattern = new("^v(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>Reserved glob recorded in provider protected-ref settings; no branch may match it.</summary>
    public const string ReservedBranchGlob = "refs/heads/v*";

    public static bool IsReservedBranchRef(string branchRef) =>
        branchRef is not null &&
        branchRef.StartsWith(BranchPrefix, StringComparison.Ordinal) &&
        ReservedBranchPattern.IsMatch(branchRef[BranchPrefix.Length..]);

    public static bool IsMaintenanceBranchRef(string branchRef) =>
        branchRef is not null &&
        branchRef.StartsWith(BranchPrefix, StringComparison.Ordinal) &&
        MaintenanceBranchPattern.IsMatch(branchRef[BranchPrefix.Length..]);

    /// <summary>Maps a version line label such as <c>v1.2</c> onto its maintenance branch ref.</summary>
    public static string MaintenanceBranchRefForLine(string line) =>
        line is not null && line.StartsWith('v') && LineLabelPattern.IsMatch(line)
            ? $"{BranchPrefix}release/{line[1..]}"
            : throw new ArgumentException("release_line_label_malformed", nameof(line));

    public static RefNamespaceDecision EvaluateBranchCreation(string branchRef)
    {
        if (branchRef is null || !branchRef.StartsWith(BranchPrefix, StringComparison.Ordinal))
        {
            return new RefNamespaceDecision(false, "ref_namespace_branch_ref_malformed");
        }

        return IsReservedBranchRef(branchRef)
            ? new RefNamespaceDecision(false, "ref_namespace_version_tag_glob_reserved")
            : new RefNamespaceDecision(true, null);
    }
}

public static class ReleaseInputPolicy
{
    private const int MaximumYamlBytes = 65_536;
    private const int MaximumFragments = 1_024;
    private static readonly Regex VersionPattern = new("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-(?:alpha|beta|rc)\\.[0-9]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex LinePattern = new("^v[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex TagPattern = new("^v[0-9]+\\.[0-9]+\\.[0-9]+(?:-(?:alpha|beta|rc)\\.[0-9]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex BaselineRefPattern = new("^changelog-baseline-[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex GroupPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex SecretPattern = new("(?:-----BEGIN [A-Z0-9 ]*PRIVATE KEY-----|\\bBearer\\s+[A-Za-z0-9._~+/=-]{16,}|\\b(?:gh[pousr]_|github_pat_|glpat-|xox[baprs]-)[A-Za-z0-9_-]{8,}|\\b(?:password|passwd|client_secret|api[_-]?key|access[_-]?token|refresh[_-]?token)\\s*[:=]\\s*\\S+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly Regex ProviderIdentityPattern = new("(?:https?://(?:www\\.)?(?:github\\.com|gitlab\\.com|codeberg\\.org|bitbucket\\.org)/\\S+|(?<![\\w@])@[A-Za-z0-9][A-Za-z0-9-]{0,38}(?![\\w-])|\\b(?:workflow|pipeline|job|run)[ _-]?id\\s*[:=]\\s*[0-9]{3,}\\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly string[] RequiredImpacts = ["breaking", "security", "migration", "configuration", "openapi", "operator"];
    private static readonly string[] ReleaseDescriptorKeys = ["Version", "Line", "Release-Date", "Base-Stable-Tag", "Previous-Published-Tag", "Release-Range", "Compatibility", "Impact-Dispositions"];
    private static readonly string[] FragmentKeys = ["Change-Id", "Title", "Type", "Scope", "Summary", "Group", "Backport-Of", "Supersedes", "Impacts"];
    private static readonly string[] FragmentImpactKeys = ["Reference", "Disposition", "Public-Disclosure", "Detail"];
    private static readonly string[] ReleaseRangeKeys = ["Base-Ref", "Base-Oid", "Previous-Ref", "Previous-Oid"];
    private static readonly HashSet<string> AllowedDispositions = new(StringComparer.Ordinal)
    {
        "accepted",
        "coordinated",
        "documented",
        "mitigated",
        "not-applicable",
        "planned",
    };

    public static ReleaseInputValidationResult Validate(
        string releaseYaml,
        IEnumerable<string> fragmentYamls,
        IEnumerable<string> priorSnapshotYamls)
    {
        var diagnostics = new List<string>();
        ReleaseDescriptor? descriptor = ParseDescriptor(releaseYaml, diagnostics);
        List<PublicChangeFragment> fragments = ParseFragments(fragmentYamls, diagnostics);
        ValidateDuplicateIds(fragments, diagnostics);
        ValidateGroups(fragments, diagnostics);
        ValidateSupersedes(fragments, diagnostics);
        ValidateSnapshot(fragments, priorSnapshotYamls, diagnostics);

        return new ReleaseInputValidationResult(diagnostics.Count == 0, descriptor, fragments, diagnostics);
    }

    private static ReleaseDescriptor? ParseDescriptor(string yaml, List<string> diagnostics)
    {
        YamlMappingNode? root = ParseRoot(yaml, "release", diagnostics);
        if (root is null)
        {
            return null;
        }

        RequireKnownKeys(root, "release", null, ReleaseDescriptorKeys, diagnostics);
        ScanReleaseText(root, diagnostics);
        string version = Scalar(root, "Version", "release", diagnostics);
        string line = Scalar(root, "Line", "release", diagnostics);
        string dateText = Scalar(root, "Release-Date", "release", diagnostics);
        string baseStableTag = Scalar(root, "Base-Stable-Tag", "release", diagnostics);
        string previousPublishedTag = Scalar(root, "Previous-Published-Tag", "release", diagnostics);
        ReleaseRangeReference releaseRange = ValidateReleaseRange(root, baseStableTag, previousPublishedTag, diagnostics);
        string[] compatibility = Sequence(root, "Compatibility", "release", diagnostics);
        Dictionary<string, string> dispositions = StringMap(root, "Impact-Dispositions", "release", diagnostics);

        if (!VersionPattern.IsMatch(version))
        {
            diagnostics.Add("release_malformed_version");
        }

        if (!LinePattern.IsMatch(line))
        {
            diagnostics.Add("release_malformed_line");
        }

        if (VersionPattern.IsMatch(version) && LinePattern.IsMatch(line))
        {
            string[] parts = version.Split('.', '-');
            if (!string.Equals(line, $"v{parts[0]}.{parts[1]}", StringComparison.Ordinal))
            {
                diagnostics.Add("release_line_version_mismatch");
            }
        }

        if (!DateOnly.TryParseExact(dateText, "yyyy-MM-dd", out DateOnly releaseDate))
        {
            diagnostics.Add("release_malformed_date");
        }

        ValidateTagOrBaseline(baseStableTag, "Base-Stable-Tag", diagnostics);
        ValidateTagOrBaseline(previousPublishedTag, "Previous-Published-Tag", diagnostics);
        ValidateBaselineRange(baseStableTag, previousPublishedTag, releaseRange, diagnostics);
        if (compatibility.Length == 0)
        {
            diagnostics.Add("release_missing_compatibility_reference");
        }

        foreach (string impact in RequiredImpacts)
        {
            if (!dispositions.TryGetValue(impact, out string? disposition) || string.IsNullOrWhiteSpace(disposition))
            {
                diagnostics.Add($"release_missing_impact_disposition:{impact}");
            }
            else if (!AllowedDispositions.Contains(disposition))
            {
                diagnostics.Add($"release_unknown_impact_disposition:{impact}");
            }
        }

        return new ReleaseDescriptor(version, line, releaseDate, baseStableTag, previousPublishedTag, releaseRange, compatibility, dispositions);
    }

    private static List<PublicChangeFragment> ParseFragments(IEnumerable<string> yamls, List<string> diagnostics)
    {
        var fragments = new List<PublicChangeFragment>();
        if (yamls is null)
        {
            diagnostics.Add("fragment_collection_null");
            return fragments;
        }

        foreach (string yaml in yamls)
        {
            if (fragments.Count >= MaximumFragments)
            {
                diagnostics.Add("fragment_collection_too_large");
                break;
            }

            YamlMappingNode? root = ParseRoot(yaml, "fragment", diagnostics);
            if (root is null)
            {
                ScanRawFragmentText(yaml, diagnostics);
                continue;
            }

            string changeId = Scalar(root, "Change-Id", "fragment", diagnostics);
            string diagnosticId = string.IsNullOrWhiteSpace(changeId) ? "unknown" : changeId;
            RequireKnownKeys(root, "fragment", diagnosticId, FragmentKeys, diagnostics);

            string title = Scalar(root, "Title", $"fragment:{diagnosticId}", diagnostics);
            string type = Scalar(root, "Type", $"fragment:{diagnosticId}", diagnostics);
            string scope = Scalar(root, "Scope", $"fragment:{diagnosticId}", diagnostics);
            string summary = Scalar(root, "Summary", $"fragment:{diagnosticId}", diagnostics);
            string? group = OptionalScalar(root, "Group");
            string? backportOf = OptionalScalar(root, "Backport-Of");
            string[] supersedes = root.Children.ContainsKey(new YamlScalarNode("Supersedes"))
                ? Sequence(root, "Supersedes", $"fragment:{diagnosticId}", diagnostics)
                : [];
            Dictionary<string, FragmentImpact> impacts = Impacts(root, diagnosticId, diagnostics);

            if (!ChangeIdPolicy.IsValid(changeId))
            {
                diagnostics.Add($"fragment_malformed_change_id:{diagnosticId}");
            }

            if (group is not null && !GroupPattern.IsMatch(group))
            {
                diagnostics.Add($"fragment_malformed_group:{diagnosticId}");
            }

            if (backportOf is not null && !FullOidPattern.IsMatch(backportOf))
            {
                diagnostics.Add($"fragment_malformed_full_oid:{diagnosticId}:Backport-Of");
            }

            foreach (string superseded in supersedes)
            {
                if (!ChangeIdPolicy.IsValid(superseded))
                {
                    diagnostics.Add($"fragment_malformed_supersedes:{diagnosticId}");
                    break;
                }
            }

            foreach (string impact in RequiredImpacts)
            {
                if (!impacts.TryGetValue(impact, out FragmentImpact? value))
                {
                    diagnostics.Add($"fragment_missing_impact:{diagnosticId}:{impact}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(value.Reference))
                {
                    diagnostics.Add($"fragment_missing_impact_reference:{diagnosticId}:{impact}");
                }

                if (string.IsNullOrWhiteSpace(value.Disposition))
                {
                    diagnostics.Add($"fragment_missing_impact_disposition:{diagnosticId}:{impact}");
                }
                else if (!AllowedDispositions.Contains(value.Disposition))
                {
                    diagnostics.Add($"fragment_unknown_impact_disposition:{diagnosticId}:{impact}");
                }
            }

            if (impacts.TryGetValue("security", out FragmentImpact? security) && string.Equals(security.PublicDisclosure, "embargoed", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add($"fragment_embargo_not_public:{diagnosticId}");
            }

            if (ContainsRestrictedMarker(root))
            {
                diagnostics.Add($"fragment_restricted_detail_marker:{diagnosticId}");
            }

            ScanPublicText(root, diagnosticId, diagnostics);

            fragments.Add(new PublicChangeFragment(changeId, title, type, scope, summary, group, backportOf, supersedes, impacts, NormalizeSnapshot(yaml)));
        }

        return fragments;
    }

    private static Dictionary<string, FragmentImpact> Impacts(YamlMappingNode root, string changeId, List<string> diagnostics)
    {
        if (!TryMapping(root, "Impacts", out YamlMappingNode? impacts))
        {
            diagnostics.Add($"fragment_missing_impacts:{changeId}");
            return new Dictionary<string, FragmentImpact>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, FragmentImpact>(StringComparer.Ordinal);
        foreach (KeyValuePair<YamlNode, YamlNode> child in impacts!.Children)
        {
            string impactKey = Key(child.Key);
            string normalizedImpact = impactKey.ToLowerInvariant();
            if (!RequiredImpacts.Contains(normalizedImpact, StringComparer.Ordinal))
            {
                diagnostics.Add($"fragment_unknown_impact:{changeId}:{impactKey}");
                continue;
            }

            if (child.Value is not YamlMappingNode impactNode)
            {
                diagnostics.Add($"fragment_malformed_impact:{changeId}:{normalizedImpact}");
                continue;
            }

            RequireKnownKeys(impactNode, "fragment-impact", $"{changeId}:{normalizedImpact}", FragmentImpactKeys, diagnostics);
            result[normalizedImpact] = new FragmentImpact(
                OptionalScalar(impactNode, "Reference") ?? string.Empty,
                OptionalScalar(impactNode, "Disposition") ?? string.Empty,
                OptionalScalar(impactNode, "Public-Disclosure"),
                OptionalScalar(impactNode, "Detail"));
        }

        return result;
    }

    private static void ValidateDuplicateIds(IReadOnlyList<PublicChangeFragment> fragments, List<string> diagnostics)
    {
        foreach (string duplicate in fragments.GroupBy(fragment => fragment.ChangeId, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).Order(StringComparer.Ordinal))
        {
            diagnostics.Add($"fragment_duplicate_change_id:{duplicate}");
        }
    }

    private static void ValidateGroups(IReadOnlyList<PublicChangeFragment> fragments, List<string> diagnostics)
    {
        foreach (IGrouping<string?, PublicChangeFragment> group in fragments.Where(fragment => !string.IsNullOrWhiteSpace(fragment.Group)).GroupBy(fragment => fragment.Group, StringComparer.Ordinal))
        {
            if (group.Select(fragment => fragment.Scope).Distinct(StringComparer.Ordinal).Skip(1).Any())
            {
                diagnostics.Add($"fragment_incompatible_group:{group.Key}");
            }
        }
    }

    private static void ValidateSnapshot(IReadOnlyList<PublicChangeFragment> currentFragments, IEnumerable<string> priorSnapshotYamls, List<string> diagnostics)
    {
        if (priorSnapshotYamls is null)
        {
            diagnostics.Add("snapshot_collection_null");
            return;
        }

        Dictionary<string, PublicChangeFragment> current = currentFragments
            .GroupBy(fragment => fragment.ChangeId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        List<PublicChangeFragment> prior = ParseFragments(priorSnapshotYamls, diagnostics);

        foreach (PublicChangeFragment priorFragment in prior)
        {
            if (!current.TryGetValue(priorFragment.ChangeId, out PublicChangeFragment? currentFragment))
            {
                diagnostics.Add($"fragment_deleted:{priorFragment.ChangeId}");
            }
            else if (!string.Equals(currentFragment.CanonicalSnapshot, priorFragment.CanonicalSnapshot, StringComparison.Ordinal))
            {
                diagnostics.Add($"fragment_mutated:{priorFragment.ChangeId}");
            }
        }
    }

    private static ReleaseRangeReference ValidateReleaseRange(YamlMappingNode root, string baseStableTag, string previousPublishedTag, List<string> diagnostics)
    {
        if (!TryMapping(root, "Release-Range", out YamlMappingNode? range))
        {
            diagnostics.Add("release_missing_key:Release-Range");
            return new ReleaseRangeReference(string.Empty, string.Empty, string.Empty, string.Empty);
        }

        RequireKnownKeys(range!, "release-range", null, ReleaseRangeKeys, diagnostics);
        string baseRef = Scalar(range!, "Base-Ref", "release-range", diagnostics);
        string baseOid = Scalar(range!, "Base-Oid", "release-range", diagnostics);
        string previousRef = Scalar(range!, "Previous-Ref", "release-range", diagnostics);
        string previousOid = Scalar(range!, "Previous-Oid", "release-range", diagnostics);

        if (!IsTagOrBaseline(baseRef))
        {
            diagnostics.Add(BaselineLike(baseRef) ? "release_malformed_baseline_ref:Base-Ref" : "release_malformed_range_ref:Base-Ref");
        }

        if (!IsTagOrBaseline(previousRef))
        {
            diagnostics.Add(BaselineLike(previousRef) ? "release_malformed_baseline_ref:Previous-Ref" : "release_malformed_range_ref:Previous-Ref");
        }

        if (!FullOidPattern.IsMatch(baseOid))
        {
            diagnostics.Add("release_malformed_full_oid:Release-Range:Base-Oid");
        }

        if (!FullOidPattern.IsMatch(previousOid))
        {
            diagnostics.Add("release_malformed_full_oid:Release-Range:Previous-Oid");
        }

        if (!string.Equals(baseRef, baseStableTag, StringComparison.Ordinal))
        {
            diagnostics.Add("release_range_base_mismatch");
        }

        if (!string.Equals(previousRef, previousPublishedTag, StringComparison.Ordinal))
        {
            diagnostics.Add("release_range_previous_mismatch");
        }

        return new ReleaseRangeReference(baseRef, baseOid, previousRef, previousOid);
    }

    private static void ValidateBaselineRange(string baseStableTag, string previousPublishedTag, ReleaseRangeReference releaseRange, List<string> diagnostics)
    {
        bool anyBaseline = IsBaselineRef(baseStableTag) ||
            IsBaselineRef(previousPublishedTag) ||
            IsBaselineRef(releaseRange.BaseRef) ||
            IsBaselineRef(releaseRange.PreviousRef);
        if (!anyBaseline)
        {
            return;
        }

        if (!IsBaselineRef(baseStableTag) ||
            !IsBaselineRef(previousPublishedTag) ||
            !IsBaselineRef(releaseRange.BaseRef) ||
            !IsBaselineRef(releaseRange.PreviousRef) ||
            !string.Equals(baseStableTag, previousPublishedTag, StringComparison.Ordinal) ||
            !string.Equals(releaseRange.BaseRef, baseStableTag, StringComparison.Ordinal) ||
            !string.Equals(releaseRange.PreviousRef, previousPublishedTag, StringComparison.Ordinal) ||
            !string.Equals(releaseRange.BaseOid, releaseRange.PreviousOid, StringComparison.Ordinal))
        {
            diagnostics.Add("release_baseline_range_mismatch");
        }
    }

    private static void ValidateSupersedes(IReadOnlyList<PublicChangeFragment> fragments, List<string> diagnostics)
    {
        HashSet<string> ids = fragments.Select(fragment => fragment.ChangeId).ToHashSet(StringComparer.Ordinal);
        foreach (PublicChangeFragment fragment in fragments)
        {
            foreach (string superseded in fragment.Supersedes)
            {
                if (string.Equals(fragment.ChangeId, superseded, StringComparison.Ordinal))
                {
                    diagnostics.Add($"fragment_self_supersedes:{fragment.ChangeId}");
                }
                else if (!ids.Contains(superseded))
                {
                    diagnostics.Add($"fragment_dangling_supersedes:{fragment.ChangeId}:{superseded}");
                }
            }
        }

        Dictionary<string, string[]> edges = fragments
            .GroupBy(fragment => fragment.ChangeId, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single().Supersedes.Where(id => !string.Equals(id, group.Key, StringComparison.Ordinal)).ToArray(),
                StringComparer.Ordinal);
        foreach (string id in ids.Order(StringComparer.Ordinal))
        {
            if (HasSupersedesCycle(id, id, edges, []))
            {
                diagnostics.Add($"fragment_supersedes_cycle:{id}");
                return;
            }
        }
    }

    private static bool HasSupersedesCycle(string start, string current, IReadOnlyDictionary<string, string[]> edges, HashSet<string> visited)
    {
        if (!edges.TryGetValue(current, out string[]? next))
        {
            return false;
        }

        foreach (string id in next)
        {
            if (string.Equals(id, start, StringComparison.Ordinal))
            {
                return true;
            }

            if (visited.Add(id) && HasSupersedesCycle(start, id, edges, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static YamlMappingNode? ParseRoot(string yaml, string label, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            diagnostics.Add($"{label}_empty_yaml");
            return null;
        }

        if (System.Text.Encoding.UTF8.GetByteCount(yaml) > MaximumYamlBytes)
        {
            diagnostics.Add($"{label}_yaml_too_large");
            return null;
        }

        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                diagnostics.Add($"{label}_malformed_yaml");
                return null;
            }

            return root;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            diagnostics.Add($"{label}_malformed_yaml");
            return null;
        }
    }

    private static void RequireKnownKeys(YamlMappingNode node, string label, string? id, IReadOnlyCollection<string> allowed, List<string> diagnostics)
    {
        foreach (YamlNode key in node.Children.Keys)
        {
            string keyText = Key(key);
            if (!allowed.Contains(keyText))
            {
                diagnostics.Add(id is null ? $"{label}_unknown_key:{keyText}" : $"{label}_unknown_key:{id}:{keyText}");
            }
        }
    }

    private static string Scalar(YamlMappingNode node, string key, string label, List<string> diagnostics)
    {
        string? value = OptionalScalar(node, key);
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add($"{label}_missing_key:{key}");
            return string.Empty;
        }

        return value;
    }

    private static string? OptionalScalar(YamlMappingNode node, string key)
    {
        return node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlScalarNode scalar
            ? scalar.Value
            : null;
    }

    private static string[] Sequence(YamlMappingNode node, string key, string label, List<string> diagnostics)
    {
        if (!node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value))
        {
            diagnostics.Add($"{label}_missing_key:{key}");
            return [];
        }

        if (value is not YamlSequenceNode sequence)
        {
            diagnostics.Add($"{label}_malformed_sequence:{key}");
            return [];
        }

        return sequence.Children.OfType<YamlScalarNode>().Select(child => child.Value ?? string.Empty).ToArray();
    }

    private static Dictionary<string, string> StringMap(YamlMappingNode node, string key, string label, List<string> diagnostics)
    {
        if (!TryMapping(node, key, out YamlMappingNode? map))
        {
            diagnostics.Add($"{label}_missing_key:{key}");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return map!.Children.ToDictionary(child => Key(child.Key), child => child.Value is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty, StringComparer.Ordinal);
    }

    private static bool TryMapping(YamlMappingNode node, string key, out YamlMappingNode? map)
    {
        map = node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) ? value as YamlMappingNode : null;
        return map is not null;
    }

    private static void ValidateTagOrBaseline(string value, string key, List<string> diagnostics)
    {
        if (IsTagOrBaseline(value))
        {
            if (string.Equals(value, "v0.0.0", StringComparison.Ordinal))
            {
                diagnostics.Add("release_fake_semver_baseline");
            }

            return;
        }

        if (BaselineLike(value))
        {
            diagnostics.Add($"release_malformed_baseline_ref:{key}");
        }
        else
        {
            diagnostics.Add($"release_malformed_tag:{key}");
        }
    }

    internal static bool IsBaselineRef(string value) => BaselineRefPattern.IsMatch(value);
    private static bool IsTagOrBaseline(string value) => TagPattern.IsMatch(value) || BaselineRefPattern.IsMatch(value);
    private static bool BaselineLike(string value) => value.StartsWith("changelog-baseline-", StringComparison.Ordinal);

    private static bool ContainsRestrictedMarker(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            string value = NormalizeForMarkerScan(scalar.Value ?? string.Empty);
            return value.Contains("restricted", StringComparison.Ordinal) || value.Contains("restricted-detail", StringComparison.Ordinal);
        }

        if (node is YamlMappingNode mapping)
        {
            return mapping.Children.Any(child => string.Equals(Key(child.Key), "Restricted-Details", StringComparison.OrdinalIgnoreCase) || ContainsRestrictedMarker(child.Value));
        }

        return node is YamlSequenceNode sequence && sequence.Children.Any(ContainsRestrictedMarker);
    }

    private static void ScanPublicText(YamlNode node, string changeId, List<string> diagnostics)
    {
        bool secretFound = false;
        bool providerFound = false;
        bool ambiguousFound = false;
        foreach (string value in Scalars(node))
        {
            if (!secretFound && ContainsSecretMaterial(value))
            {
                diagnostics.Add($"fragment_secret_material:{changeId}");
                secretFound = true;
            }

            if (!providerFound && ContainsProviderIdentity(value))
            {
                diagnostics.Add($"fragment_provider_identity:{changeId}");
                providerFound = true;
            }

            if (!ambiguousFound && ContainsAmbiguousUnicode(value))
            {
                diagnostics.Add($"fragment_ambiguous_unicode:{changeId}");
                ambiguousFound = true;
            }
        }
    }

    private static void ScanReleaseText(YamlNode node, List<string> diagnostics)
    {
        IReadOnlyList<string> values = Scalars(node).ToArray();
        if (values.Any(ContainsSecretMaterial))
        {
            diagnostics.Add("release_secret_material");
        }

        if (values.Any(ContainsProviderIdentity))
        {
            diagnostics.Add("release_provider_identity");
        }

        if (values.Any(ContainsAmbiguousUnicode))
        {
            diagnostics.Add("release_ambiguous_unicode");
        }
    }

    private static void ScanRawFragmentText(string yaml, List<string> diagnostics)
    {
        string changeId = ExtractChangeId(yaml);
        if (ContainsSecretMaterial(yaml))
        {
            diagnostics.Add($"fragment_secret_material:{changeId}");
        }

        if (ContainsProviderIdentity(yaml))
        {
            diagnostics.Add($"fragment_provider_identity:{changeId}");
        }

        if (ContainsAmbiguousUnicode(yaml))
        {
            diagnostics.Add($"fragment_ambiguous_unicode:{changeId}");
        }
    }

    private static string ExtractChangeId(string yaml)
    {
        foreach (string line in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            const string prefix = "Change-Id:";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                string value = line[prefix.Length..].Trim();
                return string.IsNullOrWhiteSpace(value) ? "unknown" : value;
            }
        }

        return "unknown";
    }

    private static IEnumerable<string> Scalars(YamlNode node)
    {
        if (node is YamlScalarNode scalar)
        {
            yield return scalar.Value ?? string.Empty;
        }
        else if (node is YamlMappingNode mapping)
        {
            foreach (KeyValuePair<YamlNode, YamlNode> child in mapping.Children)
            {
                foreach (string value in Scalars(child.Value))
                {
                    yield return value;
                }
            }
        }
        else if (node is YamlSequenceNode sequence)
        {
            foreach (YamlNode child in sequence.Children)
            {
                foreach (string value in Scalars(child))
                {
                    yield return value;
                }
            }
        }
    }

    private static bool ContainsSecretMaterial(string value) => SecretPattern.IsMatch(value);

    private static bool ContainsProviderIdentity(string value) => ProviderIdentityPattern.IsMatch(value);

    private static bool ContainsAmbiguousUnicode(string value) => value.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format && rune.Value is not '\r' and not '\n' and not '\t');

    private static string NormalizeForMarkerScan(string value) => value.Replace('\u0455', 's').ToLowerInvariant();

    private static string NormalizeSnapshot(string yaml) => yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd() + "\n";
    private static string Key(YamlNode node) => node is YamlScalarNode scalar ? scalar.Value ?? string.Empty : string.Empty;
}
