using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record PublicChangelogRelease(
    ReleaseDescriptor Descriptor,
    ReleaseContext Context,
    IReadOnlyList<PublicChangeFragment> Fragments,
    string Summary,
    string NotesSha256,
    string TagObjectId,
    string TargetOid);

public sealed record PublicChangelogResult(
    bool IsValid, byte[]? Page, string? Diagnostic, string? HighestStableVersion, string? Warning = null);

public static class PublicChangelogPolicy
{
    public const string StartMarker = "<!-- BEGIN GENERATED RELEASES -->";
    public const string EndMarker = "<!-- END GENERATED RELEASES -->";
    public const int MaximumPageBytes = 1_048_576;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex VersionPattern = new(
        @"^(?<major>0|[1-9]\d*)\.(?<minor>0|[1-9]\d*)\.(?<patch>0|[1-9]\d*)(?:-(?<stage>alpha|beta|rc)\.(?<number>0|[1-9]\d*))?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));

    public static PublicChangelogResult Generate(IReadOnlyList<PublicChangelogRelease> releases, Uri publicationBase)
    {
        if (releases is null || releases.Count == 0 || releases.Count > ReleaseArtifactPolicy.MaximumCollectionItems ||
            publicationBase is null || !publicationBase.IsAbsoluteUri || publicationBase.Scheme != Uri.UriSchemeHttps ||
            !publicationBase.AbsoluteUri.EndsWith('/') ||
            !string.IsNullOrEmpty(publicationBase.UserInfo) ||
            !string.IsNullOrEmpty(publicationBase.Fragment) || !string.IsNullOrEmpty(publicationBase.Query))
        {
            return Invalid("changelog_inputs_invalid");
        }

        var versions = new HashSet<string>(StringComparer.Ordinal);
        var sorted = new List<(PublicChangelogRelease Release, VersionKey Version)>(releases.Count);
        foreach (PublicChangelogRelease release in releases)
        {
            if (release.Descriptor is null || release.Context?.Release is null || release.Fragments is null ||
                !TryParseVersion(release.Descriptor.Version, out VersionKey version) ||
                !versions.Add(release.Descriptor.Version) ||
                release.Context.Release.Version != release.Descriptor.Version ||
                release.Context.Release.ReleaseDate != release.Descriptor.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ||
                release.Context.Release.Line != release.Descriptor.Line ||
                !IsHexOid(release.TagObjectId) || !IsHexOid(release.TargetOid) ||
                !IsSha256(release.NotesSha256))
            {
                return Invalid("changelog_release_identity_invalid");
            }

            sorted.Add((release, version));
        }

        string? highestStable = sorted.Where(item => !item.Version.IsPrerelease)
            .OrderByDescending(item => item.Version)
            .Select(item => item.Release.Descriptor.Version)
            .FirstOrDefault();
        var page = new StringBuilder(StartMarker).Append('\n');
        foreach ((PublicChangelogRelease release, VersionKey version) in sorted
            .OrderByDescending(item => item.Release.Descriptor.ReleaseDate)
            .ThenByDescending(item => item.Version)
            .ThenBy(item => item.Release.Descriptor.Version, StringComparer.Ordinal))
        {
            if (!TryBuildEntry(release, version, publicationBase, out string? entry))
            {
                return Invalid("changelog_release_content_invalid");
            }

            page.Append(entry).Append('\n');
            if (StrictUtf8.GetByteCount(page.ToString()) + StrictUtf8.GetByteCount(EndMarker) + 1 > MaximumPageBytes)
            {
                return Invalid("changelog_page_too_large");
            }
        }

        page.Append(EndMarker).Append('\n');
        byte[] bytes = StrictUtf8.GetBytes(page.ToString());
        return bytes.Length <= MaximumPageBytes
            ? new PublicChangelogResult(true, bytes, null, highestStable,
                bytes.Length >= MaximumPageBytes * 9 / 10 ? "changelog_page_near_limit" : null)
            : Invalid("changelog_page_too_large");
    }

    public static PublicChangelogResult ReplaceRegion(byte[] existing, byte[] generated, bool bootstrap)
    {
        if (existing is null || generated is null || existing.Length > MaximumPageBytes ||
            generated.Length > MaximumPageBytes)
        {
            return Invalid("changelog_page_too_large");
        }

        try
        {
            string current = StrictUtf8.GetString(existing);
            string replacement = StrictUtf8.GetString(generated);
            if (current.Contains('\r', StringComparison.Ordinal) || replacement.Contains('\r', StringComparison.Ordinal) ||
                !replacement.StartsWith(StartMarker + "\n", StringComparison.Ordinal) ||
                !replacement.EndsWith(EndMarker + "\n", StringComparison.Ordinal) ||
                replacement.IndexOf(StartMarker, StartMarker.Length, StringComparison.Ordinal) >= 0 ||
                replacement.LastIndexOf(EndMarker, StringComparison.Ordinal) != replacement.Length - EndMarker.Length - 1)
            {
                return Invalid("changelog_markers_invalid");
            }

            if (current.Length == 0 && bootstrap)
            {
                return new PublicChangelogResult(true, generated, null, null);
            }

            int begin = current.IndexOf(StartMarker, StringComparison.Ordinal);
            int end = current.IndexOf(EndMarker, StringComparison.Ordinal);
            if (begin < 0 || end <= begin ||
                current.IndexOf(StartMarker, begin + StartMarker.Length, StringComparison.Ordinal) >= 0 ||
                current.IndexOf(EndMarker, end + EndMarker.Length, StringComparison.Ordinal) >= 0 ||
                !current.AsSpan(end + EndMarker.Length).SequenceEqual("\n".AsSpan()))
            {
                return Invalid("changelog_markers_invalid");
            }

            string prefix = current[..begin];
            if (prefix.Length != 0 && (!prefix.StartsWith("---\n", StringComparison.Ordinal) ||
                !prefix.EndsWith("---\n", StringComparison.Ordinal) ||
                prefix.Split("---\n", StringSplitOptions.None).Length != 3))
            {
                return Invalid("changelog_outside_region_invalid");
            }

            byte[] result = StrictUtf8.GetBytes(prefix + replacement);
            return result.Length <= MaximumPageBytes
                ? new PublicChangelogResult(true, result, null, null)
                : Invalid("changelog_page_too_large");
        }
        catch (DecoderFallbackException)
        {
            return Invalid("changelog_utf8_invalid");
        }
    }

    private static bool TryBuildEntry(PublicChangelogRelease release, VersionKey version, Uri publicationBase, out string? text)
    {
        text = null;
        if (!TryEscape(release.Summary, out string? summary) || release.Context.Changes is null ||
            release.Context.Changes.Count > ReleaseArtifactPolicy.MaximumCollectionItems ||
            release.Fragments.GroupBy(fragment => fragment.ChangeId, StringComparer.Ordinal).Any(group => group.Count() != 1))
        {
            return false;
        }

        var fragments = release.Fragments.ToDictionary(fragment => fragment.ChangeId, StringComparer.Ordinal);
        var groups = new SortedDictionary<int, List<string>>();
        var upgrades = new List<string>();
        foreach (ReleaseContextChange change in release.Context.Changes)
        {
            PublicChangeFragment? fragment = null;
            if (change.ChangeId is not null && !fragments.TryGetValue(change.ChangeId, out fragment))
            {
                return false;
            }

            if (!TryEscape(change.Title, out string? title) || !TryEscape(change.Scope, out string? scope))
            {
                return false;
            }

            int category = ReleaseChangePresentation.Category(change);
            if (!groups.TryGetValue(category, out List<string>? lines))
            {
                lines = [];
                groups.Add(category, lines);
            }

            lines.Add($"- {scope}: {title}");
            foreach ((string impact, FragmentImpact evidence) in
                (fragment?.Impacts ?? new Dictionary<string, FragmentImpact>()).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (evidence.PublicDisclosure?.Equals("embargoed", StringComparison.OrdinalIgnoreCase) == true)
                {
                    return false;
                }

                if (evidence.Disposition == "not-applicable")
                {
                    continue;
                }

                if (!TryEscape(evidence.Detail ?? string.Empty, out string? detail) ||
                    string.IsNullOrWhiteSpace(detail) ||
                    !TryEvidenceLink(evidence.Reference, release.TargetOid, publicationBase, out string? reference))
                {
                    return false;
                }

                upgrades.Add($"- {impact}: {detail} ([Evidence]({reference}))");
            }

            if (change.Breaking && (fragment is null || !fragment.Impacts.Any(pair =>
                pair.Key is "breaking" or "migration" or "configuration" or "openapi" &&
                pair.Value.Disposition != "not-applicable" && !string.IsNullOrWhiteSpace(pair.Value.Detail))))
            {
                return false;
            }
        }

        var builder = new StringBuilder();
        string releaseVersion = release.Descriptor.Version;
        string tagName = $"v{releaseVersion}";
        string notes = new Uri(publicationBase,
            $"{release.TargetOid}/docs/internal/releases/{Uri.EscapeDataString(releaseVersion)}/release-notes.md").AbsoluteUri;
        builder.Append("<a id=\"").Append(tagName).Append("\"></a>\n")
            .Append("## ").Append(tagName).Append(" \u2014 ")
            .Append(release.Descriptor.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append("\n\n")
            .Append(version.IsPrerelease ? "**Pre-release**" : "**Stable**")
            .Append(" | Release line ").Append(release.Descriptor.Line).Append("\n\n")
            .Append(summary).Append("\n\n");

        if (upgrades.Count != 0)
        {
            builder.Append("### Upgrade actions\n\n").Append(string.Join('\n', upgrades.Distinct(StringComparer.Ordinal))).Append("\n\n");
        }

        foreach ((int index, List<string> lines) in groups)
        {
            builder.Append("### ").Append(ReleaseChangePresentation.Heading(index)).Append("\n\n")
                .Append(string.Join('\n', lines)).Append("\n\n");
        }

        builder.Append("### Verify this release\n\n")
            .Append("- Signed annotated tag: `").Append(tagName).Append("` (`").Append(release.TagObjectId).Append("`)\n")
            .Append("- Authoritative notes SHA-256: `").Append(release.NotesSha256).Append("`\n")
            .Append("- [Authoritative release notes](").Append(notes).Append(")\n");
        text = builder.ToString();
        return true;
    }

    private static bool TryEscape(string value, out string? text)
    {
        text = null;
        if (value is null) return false;
        var escaped = new List<string>();
        foreach (string line in value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            UntrustedTextResult result = ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line);
            if (!result.IsValid) return false;
            escaped.Add(result.Text!);
        }

        text = string.Join('\n', escaped).Trim();
        return text.Length != 0;
    }

    private static bool TryEvidenceLink(string reference, string targetOid, Uri publicationBase, out string? link)
    {
        link = null;
        if (string.IsNullOrWhiteSpace(reference) || reference.Any(char.IsControl) ||
            reference.Contains('\\', StringComparison.Ordinal))
        {
            return false;
        }

        if (Uri.TryCreate(reference, UriKind.Absolute, out Uri? absolute))
        {
            if (absolute.Scheme != Uri.UriSchemeHttps || absolute.UserInfo.Length != 0)
            {
                return false;
            }
            link = absolute.AbsoluteUri.Replace("(", "%28", StringComparison.Ordinal)
                .Replace(")", "%29", StringComparison.Ordinal);
            return true;
        }

        string[] parts = reference.Split('#', 2);
        string[] segments = parts[0].Split('/');
        if (segments.Any(segment => segment is "" or "." or "..") ||
            reference.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }
        link = new Uri(publicationBase,
            $"{targetOid}/{string.Join('/', segments.Select(Uri.EscapeDataString))}" +
            (parts.Length == 2 ? $"#{Uri.EscapeDataString(parts[1])}" : string.Empty)).AbsoluteUri;
        return true;
    }

    private static bool TryParseVersion(string value, out VersionKey version)
    {
        version = default;
        Match match = VersionPattern.Match(value ?? string.Empty);
        if (!match.Success || !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch) ||
            match.Groups["number"].Success && !int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            return false;
        }

        version = new VersionKey(major, minor, patch,
            match.Groups["stage"].Value switch { "alpha" => 0, "beta" => 1, "rc" => 2, _ => 3 },
            match.Groups["number"].Success ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : 0);
        return true;
    }

    private static bool IsHexOid(string value) =>
        value is { Length: 40 or 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSha256(string value) =>
        value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static PublicChangelogResult Invalid(string diagnostic) => new(false, null, diagnostic, null);

    private readonly record struct VersionKey(int Major, int Minor, int Patch, int Stage, int Number) : IComparable<VersionKey>
    {
        public bool IsPrerelease => Stage != 3;

        public int CompareTo(VersionKey other)
        {
            int core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core == 0) core = Stage.CompareTo(other.Stage);
            return core == 0 ? Number.CompareTo(other.Number) : core;
        }
    }
}
