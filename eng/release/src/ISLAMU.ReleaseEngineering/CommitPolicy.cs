using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ISLAMU.ReleaseEngineering;

public enum ReleaseVisibility
{
    Visible,
    Omitted,
    Skipped,
}

public enum ScopeKind
{
    Unknown,
    Public,
    Engineering,
}

public sealed record CommitPolicyResult(
    bool IsValid,
    ReleaseVisibility ReleaseVisibility,
    ScopeKind ScopeKind,
    bool IsBreaking,
    string? Type,
    string? Scope,
    string? Description,
    string? SkipReason,
    string? ChangeId,
    IReadOnlyList<string> Diagnostics);

public sealed class ReleasePolicy
{
    private static readonly Regex HeaderPattern = new(
        "^(?<type>[a-z]+)(?<typeBang>!)?\\((?<scope>[a-z][a-z0-9-]*)\\)(?<scopeBang>!)?: (?<description>[^\\r\\n]+)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    private readonly HashSet<string> releaseVisibleTypes;
    private readonly HashSet<string> internalTypes;
    private readonly HashSet<string> publicScopes;
    private readonly HashSet<string> engineeringScopes;
    private readonly int maximumCommitMessageBytes;
    private readonly string skipTrailer;
    private readonly string skipValue;
    private readonly string skipReasonTrailer;
    private readonly string breakingFooter;
    private const string ChangeIdTrailer = "Change-Id";
    private const int MaximumSubjectUtf8Bytes = 256;
    private const int MaximumTrailerValueUtf8Bytes = 2_048;

    private ReleasePolicy(ReleasePolicyYaml policy, ScopeRegistryYaml scopes)
    {
        maximumCommitMessageBytes = policy.MaximumCommitMessageBytes;
        releaseVisibleTypes = ToSet(policy.ReleaseVisibleTypes);
        internalTypes = ToSet(policy.InternalTypes);
        publicScopes = ToSet(scopes.PublicScopes);
        engineeringScopes = ToSet(scopes.EngineeringScopes);
        skipTrailer = Required(policy.SkipTrailer, nameof(policy.SkipTrailer));
        skipValue = Required(policy.SkipValue, nameof(policy.SkipValue));
        skipReasonTrailer = Required(policy.SkipReasonTrailer, nameof(policy.SkipReasonTrailer));
        breakingFooter = Required(policy.RequiredBreakingSignals?.Footer, nameof(policy.RequiredBreakingSignals.Footer));
    }

    public static ReleasePolicy LoadFromRepositoryRoot(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        string policyPath = Path.Combine(repositoryRoot, "eng", "release", "policy", "release-policy.yaml");
        string scopePath = Path.Combine(repositoryRoot, "eng", "release", "policy", "scope-registry.yaml");
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        ReleasePolicyYaml policy = ReadYaml<ReleasePolicyYaml>(deserializer, policyPath);
        ScopeRegistryYaml scopes = ReadYaml<ScopeRegistryYaml>(deserializer, scopePath);
        if (policy.SchemaVersion != 1 || scopes.SchemaVersion != 1 || policy.MaximumCommitMessageBytes <= 0)
        {
            throw new InvalidOperationException("Release policy files are malformed.");
        }

        return new ReleasePolicy(policy, scopes);
    }

    public CommitPolicyResult EvaluateCommit(string? commitMessage)
    {
        var diagnostics = new List<string>();
        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            return Invalid(["empty_commit_message"]);
        }

        if (System.Text.Encoding.UTF8.GetByteCount(commitMessage) > maximumCommitMessageBytes)
        {
            return Invalid(["commit_message_too_long"]);
        }

        string normalized = commitMessage.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        string[] lines = normalized.Split('\n');
        Match header = HeaderPattern.Match(lines[0]);
        if (!header.Success)
        {
            return Invalid(["malformed_header"]);
        }

        string type = header.Groups["type"].Value;
        string scope = header.Groups["scope"].Value;
        string description = header.Groups["description"].Value;
        bool hasBang = header.Groups["typeBang"].Success || header.Groups["scopeBang"].Success;
        string[] footerLines = ReadFooterBlock(lines);
        bool hasBreakingFooter = HasDedicatedFooter(
            lines,
            breakingFooter,
            out string? breakingText);
        TrailerState trailers = ReadTrailers(footerLines);
        (string? changeId, bool invalidChangeId) = ReadChangeId(lines);

        if (System.Text.Encoding.UTF8.GetByteCount(description) > MaximumSubjectUtf8Bytes)
        {
            diagnostics.Add("commit_subject_too_long");
        }

        ValidateTrailerValueSizes(footerLines, diagnostics);

        if (!releaseVisibleTypes.Contains(type) && !internalTypes.Contains(type))
        {
            diagnostics.Add("unknown_type");
        }

        ScopeKind scopeKind = ScopeKind.Unknown;
        if (publicScopes.Contains(scope))
        {
            scopeKind = ScopeKind.Public;
        }
        else if (engineeringScopes.Contains(scope))
        {
            scopeKind = ScopeKind.Engineering;
        }
        else
        {
            diagnostics.Add("unknown_scope");
        }

        if (hasBang != hasBreakingFooter || (hasBreakingFooter && string.IsNullOrWhiteSpace(breakingText)))
        {
            diagnostics.Add("breaking_change_requires_bang_and_footer");
        }

        bool skipRequested = trailers.ChangelogSkip;
        if (trailers.InvalidChangelog)
        {
            diagnostics.Add("invalid_changelog_trailer");
        }

        if (skipRequested && string.IsNullOrWhiteSpace(trailers.SkipReason))
        {
            diagnostics.Add("changelog_skip_requires_reason");
        }

        if (!skipRequested && !string.IsNullOrWhiteSpace(trailers.SkipReason))
        {
            diagnostics.Add("changelog_reason_without_skip");
        }

        if (invalidChangeId)
        {
            diagnostics.Add("invalid_change_id_trailer");
        }

        bool isBreaking = hasBang && hasBreakingFooter && !string.IsNullOrWhiteSpace(breakingText);
        if (isBreaking && skipRequested)
        {
            diagnostics.Add("breaking_change_cannot_be_skipped");
        }

        ReleaseVisibility visibility = Classify(type, scopeKind, skipRequested, isBreaking);
        return new CommitPolicyResult(
            diagnostics.Count == 0,
            visibility,
            scopeKind,
            isBreaking,
            type,
            scope,
            description,
            trailers.SkipReason,
            changeId,
            diagnostics);
    }

    private static void ValidateTrailerValueSizes(string[] footerLines, List<string> diagnostics)
    {
        foreach (string line in footerLines)
        {
            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string name = line[..separator];
            string value = line[(separator + 1)..].Trim();
            if (System.Text.Encoding.UTF8.GetByteCount(value) > MaximumTrailerValueUtf8Bytes)
            {
                diagnostics.Add($"commit_trailer_value_too_long:{name}");
            }
        }
    }

    private ReleaseVisibility Classify(string type, ScopeKind scopeKind, bool skipRequested, bool isBreaking)
    {
        if (skipRequested)
        {
            return ReleaseVisibility.Skipped;
        }

        if (isBreaking)
        {
            return ReleaseVisibility.Visible;
        }

        return scopeKind == ScopeKind.Public && releaseVisibleTypes.Contains(type)
            ? ReleaseVisibility.Visible
            : ReleaseVisibility.Omitted;
    }

    private TrailerState ReadTrailers(string[] footerLines)
    {
        bool changelogSkip = false;
        bool invalidChangelog = false;
        string? skipReason = null;
        foreach (string line in footerLines)
        {
            int separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            string name = line[..separator];
            string value = line[(separator + 1)..].Trim();
            if (string.Equals(name, skipTrailer, StringComparison.Ordinal))
            {
                if (changelogSkip || !string.Equals(value, skipValue, StringComparison.Ordinal))
                {
                    invalidChangelog = true;
                }

                changelogSkip = true;
            }
            else if (string.Equals(name, skipReasonTrailer, StringComparison.Ordinal))
            {
                if (skipReason is not null)
                {
                    invalidChangelog = true;
                }

                skipReason = value;
            }
        }

        return new TrailerState(changelogSkip, skipReason, invalidChangelog);
    }

    private static (string? ChangeId, bool Invalid) ReadChangeId(string[] lines)
    {
        string? changeId = null;
        bool invalid = false;
        string prefix = ChangeIdTrailer + ":";
        foreach (string line in lines.Skip(1))
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string value = line[prefix.Length..].Trim();
            if (changeId is not null || !ChangeIdPolicy.IsValid(value))
            {
                invalid = true;
            }

            changeId = value;
        }

        return (changeId, invalid);
    }

    private string[] ReadFooterBlock(string[] lines)
    {
        int lastLine = lines.Length - 1;
        while (lastLine > 0 && lines[lastLine].Length == 0)
        {
            lastLine--;
        }

        int lastBlankLine = Array.FindLastIndex(lines, lastLine, string.IsNullOrWhiteSpace);
        if (lastBlankLine < 1 || lastBlankLine == lastLine)
        {
            return [];
        }

        string[] footerLines = lines[(lastBlankLine + 1)..(lastLine + 1)];
        if (!TryReadTrailerHeader(footerLines[0], out _))
        {
            return [];
        }

        return footerLines;
    }

    private bool TryReadTrailerHeader(string line, out string name)
    {
        int separator = line.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            name = string.Empty;
            return false;
        }

        name = line[..separator];
        return string.Equals(name, breakingFooter, StringComparison.Ordinal) ||
            name.All(character => char.IsLetterOrDigit(character) || character == '-');
    }

    private static bool HasDedicatedFooter(
        string[] lines,
        string footerName,
        out string? value)
    {
        string prefix = footerName + ":";
        for (int index = 1; index < lines.Length;)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
            }

            if (index >= lines.Length)
            {
                break;
            }

            int blockStart = index;
            while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
            }

            if (lines[blockStart].StartsWith(prefix, StringComparison.Ordinal))
            {
                value = lines[blockStart][prefix.Length..].Trim();
                return true;
            }
        }

        value = null;
        return false;
    }

    private static CommitPolicyResult Invalid(IReadOnlyList<string> diagnostics) => new(
        false,
        ReleaseVisibility.Omitted,
        ScopeKind.Unknown,
        false,
        null,
        null,
        null,
        null,
        null,
        diagnostics);

    private static T ReadYaml<T>(IDeserializer deserializer, string path)
    {
        try
        {
            return deserializer.Deserialize<T>(File.ReadAllText(path))
                ?? throw new InvalidOperationException("Release policy file is empty.");
        }
        catch (Exception exception) when (exception is IOException or YamlDotNet.Core.YamlException)
        {
            throw new InvalidOperationException("Release policy files are malformed.", exception);
        }
    }

    private static HashSet<string> ToSet(IEnumerable<string>? values) => values is null
        ? throw new InvalidOperationException("Release policy files are malformed.")
        : new HashSet<string>(values, StringComparer.Ordinal);

    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Release policy value is missing: {name}.")
        : value;

    private sealed record TrailerState(bool ChangelogSkip, string? SkipReason, bool InvalidChangelog);
    private sealed class ReleasePolicyYaml
    {
        public int SchemaVersion { get; set; }
        public int MaximumCommitMessageBytes { get; set; }
        public string[]? ReleaseVisibleTypes { get; set; }
        public string[]? InternalTypes { get; set; }
        public RequiredBreakingSignalsYaml? RequiredBreakingSignals { get; set; }
        public string? SkipTrailer { get; set; }
        public string? SkipValue { get; set; }
        public string? SkipReasonTrailer { get; set; }
    }

    private sealed class RequiredBreakingSignalsYaml
    {
        public bool Bang { get; set; }
        public string? Footer { get; set; }
    }

    private sealed class ScopeRegistryYaml
    {
        public int SchemaVersion { get; set; }
        public string[]? PublicScopes { get; set; }
        public string[]? EngineeringScopes { get; set; }
    }
}
