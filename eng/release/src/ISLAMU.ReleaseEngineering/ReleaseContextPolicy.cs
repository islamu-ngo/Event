using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public enum VersionBump
{
    None = 0,
    Patch = 1,
    Minor = 2,
    Major = 3,
}

public sealed record ReleaseCommit(string Oid, string Message);

public sealed record ReleaseContextValidationResult(
    bool IsValid,
    ReleaseContext? Context,
    string? Json,
    IReadOnlyList<string> Diagnostics);

public sealed record ReleaseContext(
    int SchemaVersion,
    ReleaseContextRelease Release,
    IReadOnlyList<ReleaseContextChange> Changes,
    ReleaseContextEvidence Evidence);

public sealed record ReleaseContextRelease(
    string Version,
    string Line,
    string ReleaseDate,
    string BaseStableTag,
    string PreviousPublishedTag,
    string MinimumBump,
    string Channel,
    bool AdvancesMain);

public sealed record ReleaseContextChange(
    string DisplayId,
    string Oid,
    string? ChangeId,
    string Type,
    string Scope,
    string Title,
    string Summary,
    bool Breaking,
    bool Backport,
    string? BackportOf);

public sealed record ReleaseContextEvidence(
    string BaseStableOid,
    string PreviousPublishedOid,
    IReadOnlyList<ReleaseContextObject> Objects);

public sealed record ReleaseContextObject(string DisplayId, string Oid);

public static class ReleaseContextPolicy
{
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex IdentityOrProviderPattern = new("(?:[\\p{L}\\p{N}._%+-]+@[\\p{L}\\p{N}.-]+\\.[\\p{L}]{2,}|https?://(?:www\\.)?(?:github\\.com|gitlab\\.com|codeberg\\.org|bitbucket\\.org)/\\S+|(?<![\\w@])@[A-Za-z0-9][A-Za-z0-9-]{0,38}(?![\\w-])|\\b(?:workflow|pipeline|job|run)[ _-]?id\\s*[:=]\\s*[0-9]{3,}\\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static ReleaseContextValidationResult Build(
        ReleaseInputValidationResult input,
        IEnumerable<ReleaseCommit> commits,
        ReleasePolicy policy,
        string? gitCliffSuggestedVersion = null,
        string? verifiedBaselineRef = null,
        string? verifiedBaselineOid = null,
        IEnumerable<ChangeIdRename>? changeIdRenames = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(commits);
        ArgumentNullException.ThrowIfNull(policy);

        var diagnostics = new List<string>(input.Diagnostics);
        ReleaseDescriptor? descriptor = input.Descriptor;
        if (descriptor is null)
        {
            diagnostics.Add("context_missing_release_descriptor");
            return Invalid(diagnostics);
        }

        List<ReleaseCommit> commitList = commits.ToList();
        List<CommitPolicyResult> evaluatedCommits = EvaluateCommits(
            commitList,
            policy,
            changeIdRenames,
            diagnostics);
        if (!SemanticVersion.TryParse(descriptor.Version, out SemanticVersion selected))
        {
            diagnostics.Add("context_malformed_version");
            return Invalid(diagnostics);
        }

        bool firstReleaseBaseline = ReleaseInputPolicy.IsBaselineRef(descriptor.BaseStableTag) || ReleaseInputPolicy.IsBaselineRef(descriptor.PreviousPublishedTag);
        SemanticVersion baseStable;
        SemanticVersion previousPublished;
        if (firstReleaseBaseline)
        {
            ValidateFirstReleaseBaseline(descriptor, verifiedBaselineRef, verifiedBaselineOid, diagnostics);
            baseStable = new SemanticVersion(0, 0, 0, null, null);
            previousPublished = baseStable;
        }
        else
        {
            if (!SemanticVersion.TryParseTag(descriptor.BaseStableTag, out baseStable) || baseStable.IsPrerelease)
            {
                diagnostics.Add("context_malformed_base_stable_tag");
                return Invalid(diagnostics);
            }

            if (!SemanticVersion.TryParseTag(descriptor.PreviousPublishedTag, out previousPublished))
            {
                diagnostics.Add("context_malformed_previous_published_tag");
                return Invalid(diagnostics);
            }
        }

        ValidateVersionLine(selected, descriptor.Line, diagnostics);
        ValidatePrereleasePolicy(selected, baseStable, previousPublished, diagnostics);
        VersionBump minimumBump = ComputeMinimumBump(evaluatedCommits, input.Fragments, baseStable.Major);
        ValidateSelectedBump(selected, baseStable, minimumBump, diagnostics);
        if (!string.IsNullOrWhiteSpace(gitCliffSuggestedVersion) && !string.Equals(gitCliffSuggestedVersion, descriptor.Version, StringComparison.Ordinal))
        {
            diagnostics.Add("context_git_cliff_bump_disagreement");
        }

        List<ReleaseContextChange> changes = BuildChanges(commitList, evaluatedCommits, input.Fragments, diagnostics);
        ValidateCanonicalText(changes, diagnostics);
        Dictionary<string, string> displayIds = CreateDisplayIds(AllEvidenceOids(descriptor, changes), diagnostics);
        if (diagnostics.Count != 0)
        {
            return Invalid(diagnostics);
        }

        changes = changes
            .Select(change => change with { DisplayId = displayIds[change.Oid] })
            .OrderBy(change => change.ChangeId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(change => change.DisplayId, StringComparer.Ordinal)
            .ToArray()
            .ToList();

        var context = new ReleaseContext(
            1,
            new ReleaseContextRelease(
                descriptor.Version,
                descriptor.Line,
                descriptor.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                descriptor.BaseStableTag,
                descriptor.PreviousPublishedTag,
                minimumBump.ToString().ToLowerInvariant(),
                selected.Channel,
                !selected.IsPrerelease),
            changes,
            new ReleaseContextEvidence(
                descriptor.ReleaseRange.BaseOid,
                descriptor.ReleaseRange.PreviousOid,
                displayIds
                    .OrderBy(pair => pair.Value, StringComparer.Ordinal)
                    .Select(pair => new ReleaseContextObject(pair.Value, pair.Key))
                    .ToArray()));

        string json = JsonSerializer.Serialize(context, JsonOptions) + "\n";
        return new ReleaseContextValidationResult(true, context, json, []);
    }

    private static void ValidateFirstReleaseBaseline(
        ReleaseDescriptor descriptor,
        string? verifiedBaselineRef,
        string? verifiedBaselineOid,
        List<string> diagnostics)
    {
        if (!string.Equals(descriptor.BaseStableTag, descriptor.PreviousPublishedTag, StringComparison.Ordinal) ||
            !string.Equals(descriptor.ReleaseRange.BaseRef, descriptor.BaseStableTag, StringComparison.Ordinal) ||
            !string.Equals(descriptor.ReleaseRange.PreviousRef, descriptor.PreviousPublishedTag, StringComparison.Ordinal) ||
            !string.Equals(descriptor.ReleaseRange.BaseOid, descriptor.ReleaseRange.PreviousOid, StringComparison.Ordinal))
        {
            diagnostics.Add("context_baseline_range_mismatch");
        }

        if (verifiedBaselineRef is null || verifiedBaselineOid is null)
        {
            diagnostics.Add("context_baseline_evidence_required");
            return;
        }

        if (!string.Equals(verifiedBaselineRef, descriptor.BaseStableTag, StringComparison.Ordinal) ||
            !string.Equals(verifiedBaselineOid, descriptor.ReleaseRange.BaseOid, StringComparison.Ordinal))
        {
            diagnostics.Add("context_baseline_evidence_mismatch");
        }
    }

    private static List<CommitPolicyResult> EvaluateCommits(
        List<ReleaseCommit> commits,
        ReleasePolicy policy,
        IEnumerable<ChangeIdRename>? changeIdRenames,
        List<string> diagnostics)
    {
        var results = new List<CommitPolicyResult>(commits.Count);
        foreach (ReleaseCommit commit in commits)
        {
            if (!FullOidPattern.IsMatch(commit.Oid))
            {
                diagnostics.Add($"context_malformed_full_oid:{commit.Oid}");
            }

            CommitPolicyResult result = ChangeIdRenamePolicy.Evaluate(
                commit,
                policy,
                changeIdRenames,
                diagnostics);
            if (!result.IsValid)
            {
                diagnostics.AddRange(result.Diagnostics.Select(diagnostic => $"context_commit_invalid:{commit.Oid}:{diagnostic}"));
            }

            results.Add(result);
        }

        return results;
    }

    private static void ValidateVersionLine(SemanticVersion selected, string line, List<string> diagnostics)
    {
        string expectedLine = $"v{selected.Major}.{selected.Minor}";
        if (!string.Equals(line, expectedLine, StringComparison.Ordinal))
        {
            diagnostics.Add("context_release_line_version_mismatch");
        }
    }

    private static void ValidatePrereleasePolicy(SemanticVersion selected, SemanticVersion baseStable, SemanticVersion previousPublished, List<string> diagnostics)
    {
        if (selected.IsPrerelease)
        {
            if (previousPublished.IsPrerelease && !previousPublished.SameCore(selected))
            {
                diagnostics.Add("context_prerelease_previous_version_mismatch");
                return;
            }

            if (!previousPublished.IsPrerelease && !previousPublished.SameCore(baseStable))
            {
                diagnostics.Add("context_prerelease_base_version_mismatch");
                return;
            }

            if (selected.PrereleaseNumber == 1)
            {
                if (!IsValidStageStart(selected.PrereleaseStage!, previousPublished))
                {
                    diagnostics.Add("context_prerelease_stage_progression_invalid");
                }
            }
            else if (!previousPublished.IsPrerelease || !string.Equals(previousPublished.PrereleaseStage, selected.PrereleaseStage, StringComparison.Ordinal) || previousPublished.PrereleaseNumber != selected.PrereleaseNumber - 1)
            {
                diagnostics.Add("context_prerelease_counter_not_contiguous");
            }

            return;
        }

        if (previousPublished.IsPrerelease && !selected.SameCore(previousPublished))
        {
            diagnostics.Add("context_stable_promotion_base_mismatch");
        }
    }

    private static bool IsValidStageStart(string stage, SemanticVersion previous) => stage switch
    {
        "alpha" => !previous.IsPrerelease,
        "beta" => previous is { PrereleaseStage: "alpha" },
        "rc" => previous is { PrereleaseStage: "beta" },
        _ => false,
    };

    private static VersionBump ComputeMinimumBump(IEnumerable<CommitPolicyResult> commits, IEnumerable<PublicChangeFragment> fragments, int baseMajor)
    {
        VersionBump bump = VersionBump.None;
        foreach (CommitPolicyResult commit in commits.Where(commit => commit.IsValid && commit.ReleaseVisibility == ReleaseVisibility.Visible))
        {
            bump = Max(bump, BumpFor(commit.Type, commit.IsBreaking, baseMajor));
        }

        foreach (PublicChangeFragment fragment in fragments)
        {
            VersionBump fragmentBump = fragment.BackportOf is null
                ? BumpFor(fragment.Type, IsBreakingFragment(fragment), baseMajor)
                : VersionBump.Patch;
            bump = Max(bump, fragmentBump);
        }

        return bump;
    }

    private static void ValidateSelectedBump(SemanticVersion selected, SemanticVersion baseStable, VersionBump minimumBump, List<string> diagnostics)
    {
        SemanticVersion minimum = minimumBump switch
        {
            VersionBump.Major => new SemanticVersion(baseStable.Major + 1, 0, 0, null, null),
            VersionBump.Minor => new SemanticVersion(baseStable.Major, baseStable.Minor + 1, 0, null, null),
            VersionBump.Patch => new SemanticVersion(baseStable.Major, baseStable.Minor, baseStable.Patch + 1, null, null),
            _ => baseStable,
        };

        if (selected.CompareCoreTo(minimum) < 0)
        {
            diagnostics.Add("context_selected_version_below_minimum_bump");
        }
    }

    private static VersionBump BumpFor(string? type, bool breaking, int baseMajor)
    {
        if (breaking)
        {
            return baseMajor == 0 ? VersionBump.Minor : VersionBump.Major;
        }

        return type switch
        {
            "feat" => VersionBump.Minor,
            "fix" or "perf" or "revert" or "docs" => VersionBump.Patch,
            _ => VersionBump.None,
        };
    }

    private static VersionBump Max(VersionBump left, VersionBump right) => left > right ? left : right;

    private static List<ReleaseContextChange> BuildChanges(
        List<ReleaseCommit> commits,
        List<CommitPolicyResult> evaluatedCommits,
        IEnumerable<PublicChangeFragment> fragments,
        List<string> diagnostics)
    {
        PublicChangeFragment[] fragmentList = fragments.ToArray();
        Dictionary<string, List<int>> commitIndexesByChangeId = evaluatedCommits
            .Select((commit, index) => new { Commit = commit, Index = index })
            .Where(item => item.Commit.IsValid && item.Commit.ReleaseVisibility == ReleaseVisibility.Visible && !string.IsNullOrWhiteSpace(item.Commit.ChangeId))
            .GroupBy(item => item.Commit.ChangeId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Index).ToList(), StringComparer.Ordinal);
        var fragmentByChangeId = new Dictionary<string, PublicChangeFragment>(StringComparer.Ordinal);
        foreach (PublicChangeFragment fragment in fragmentList)
        {
            if (!commitIndexesByChangeId.TryGetValue(fragment.ChangeId, out List<int>? matchingIndexes))
            {
                diagnostics.Add($"context_fragment_missing_commit_link:{fragment.ChangeId}");
                continue;
            }

            if (matchingIndexes.Count != 1)
            {
                diagnostics.Add($"context_fragment_duplicate_commit_link:{fragment.ChangeId}");
                continue;
            }

            fragmentByChangeId[fragment.ChangeId] = fragment;
        }

        var changes = new List<ReleaseContextChange>();
        for (int index = 0; index < commits.Count; index++)
        {
            CommitPolicyResult result = evaluatedCommits[index];
            if (!result.IsValid || result.ReleaseVisibility != ReleaseVisibility.Visible)
            {
                continue;
            }

            if (result.ChangeId is not null && fragmentByChangeId.TryGetValue(result.ChangeId, out PublicChangeFragment? fragment))
            {
                bool backport = fragment.BackportOf is not null;
                changes.Add(new ReleaseContextChange(
                    string.Empty,
                    commits[index].Oid,
                    fragment.ChangeId,
                    fragment.Type,
                    fragment.Scope,
                    backport ? $"Backport: {fragment.Title}" : fragment.Title,
                    fragment.Summary,
                    IsBreakingFragment(fragment),
                    backport,
                    fragment.BackportOf));
                continue;
            }

            changes.Add(new ReleaseContextChange(
                string.Empty,
                commits[index].Oid,
                null,
                result.Type!,
                result.Scope!,
                result.Description!,
                result.Description!,
                result.IsBreaking,
                false,
                null));
        }

        return changes;
    }

    private static bool IsBreakingFragment(PublicChangeFragment fragment) =>
        fragment.Impacts.TryGetValue("breaking", out FragmentImpact? breaking) &&
        (!string.Equals(breaking.Disposition, "not-applicable", StringComparison.Ordinal) || !string.IsNullOrWhiteSpace(breaking.Detail));

    private static void ValidateCanonicalText(IEnumerable<ReleaseContextChange> changes, List<string> diagnostics)
    {
        foreach (ReleaseContextChange change in changes)
        {
            if (IdentityOrProviderPattern.IsMatch(change.Title) || IdentityOrProviderPattern.IsMatch(change.Summary))
            {
                diagnostics.Add($"context_identity_or_provider_data:{change.Oid}");
            }
        }
    }

    private static IEnumerable<string> AllEvidenceOids(ReleaseDescriptor descriptor, IEnumerable<ReleaseContextChange> changes)
    {
        yield return descriptor.ReleaseRange.BaseOid;
        yield return descriptor.ReleaseRange.PreviousOid;
        foreach (ReleaseContextChange change in changes)
        {
            yield return change.Oid;
            if (change.BackportOf is not null)
            {
                yield return change.BackportOf;
            }
        }
    }

    private static Dictionary<string, string> CreateDisplayIds(IEnumerable<string> oids, List<string> diagnostics)
    {
        string[] distinctOids = oids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string oid in distinctOids)
        {
            if (!FullOidPattern.IsMatch(oid))
            {
                diagnostics.Add($"context_malformed_full_oid:{oid}");
                continue;
            }

            int length = 12;
            while (length <= oid.Length && distinctOids.Any(other => !string.Equals(other, oid, StringComparison.Ordinal) && other.StartsWith(oid[..length], StringComparison.Ordinal)))
            {
                length++;
            }

            if (distinctOids.Any(other => !string.Equals(other, oid, StringComparison.Ordinal) && (other.StartsWith(oid, StringComparison.Ordinal) || oid.StartsWith(other, StringComparison.Ordinal))))
            {
                diagnostics.Add($"context_display_id_collision:{oid}");
                continue;
            }

            if (length > oid.Length)
            {
                diagnostics.Add($"context_display_id_collision:{oid}");
            }
            else
            {
                result[oid] = oid[..length];
            }
        }

        return result;
    }

    private static ReleaseContextValidationResult Invalid(IReadOnlyList<string> diagnostics) => new(false, null, null, diagnostics);

    private sealed record SemanticVersion(int Major, int Minor, int Patch, string? PrereleaseStage, int? PrereleaseNumber)
    {
        private static readonly Regex Pattern = new("^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:-(?<stage>alpha|beta|rc)\\.(?<number>[1-9][0-9]*))?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));

        public bool IsPrerelease => PrereleaseStage is not null;
        public string Channel => PrereleaseStage ?? "stable";

        public static bool TryParseTag(string value, out SemanticVersion version)
        {
            if (value.StartsWith('v'))
            {
                return TryParse(value[1..], out version);
            }

            version = default!;
            return false;
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            Match match = Pattern.Match(value);
            if (!match.Success)
            {
                version = default!;
                return false;
            }

            if (!int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch) ||
                (match.Groups["number"].Success && !int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            {
                version = default!;
                return false;
            }

            int? prereleaseNumber = match.Groups["number"].Success
                ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture)
                : null;

            version = new SemanticVersion(
                major,
                minor,
                patch,
                match.Groups["stage"].Success ? match.Groups["stage"].Value : null,
                prereleaseNumber);
            return true;
        }

        public bool SameCore(SemanticVersion other) => Major == other.Major && Minor == other.Minor && Patch == other.Patch;

        public int CompareCoreTo(SemanticVersion other)
        {
            int major = Major.CompareTo(other.Major);
            if (major != 0)
            {
                return major;
            }

            int minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }
    }
}
