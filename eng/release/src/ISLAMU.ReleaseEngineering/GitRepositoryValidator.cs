using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Attestation inputs. Every member names an object that cannot change once written: a full candidate
/// object ID, annotated tag refs under <c>refs/tags/</c>, and the version-line label the release claims.
/// A branch ref is deliberately absent — a release must stay verifiable after the branch that carried
/// its commits has moved on, been deleted, or never existed in the consumer's clone.
/// </summary>
public sealed record GitReleaseValidationRequest(
    string RepositoryPath,
    string ReleaseLine,
    string SelectedVersion,
    string BaseStableRef,
    string PreviousPublishedRef,
    string CandidateOid,
    IReadOnlyDictionary<string, string>? ExpectedTagObjectOids = null);

public sealed record GitReleaseValidationResult(
    bool IsValid,
    GitRepositoryObjectIdentity? Identity,
    IReadOnlyList<string> Diagnostics);

public sealed record GitRepositoryObjectIdentity(
    string ObjectFormat,
    int OidLength,
    string BaseStableTag,
    string BaseStableCommitOid,
    string BaseStableTagObjectOid,
    string PreviousPublishedTag,
    string PreviousPublishedCommitOid,
    string PreviousPublishedTagObjectOid,
    string CandidateOid);

public static class GitRepositoryValidator
{
    private const int MaximumGitOutput = 16_384;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly Regex LinePattern = new("^v(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));
    private static readonly Regex VersionPattern = new("^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)(?:-(?<stage>alpha|beta|rc)\\.(?<number>[1-9][0-9]*))?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));
    private static readonly Regex BaselineRefPattern = new("^changelog-baseline-[0-9]{4}-[0-9]{2}-[0-9]{2}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static GitReleaseValidationResult Validate(GitReleaseValidationRequest request) => Validate(request, DefaultTimeout);

    public static GitReleaseValidationResult Validate(GitReleaseValidationRequest request, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(request);
        var diagnostics = new List<string>();
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
        {
            diagnostics.Add("git_timeout_invalid");
            return Invalid(diagnostics);
        }

        if (string.IsNullOrWhiteSpace(request.RepositoryPath) || !Directory.Exists(request.RepositoryPath))
        {
            diagnostics.Add("git_repository_path_missing");
            return Invalid(diagnostics);
        }

        Git git = new(request.RepositoryPath, timeout);
        if (!git.TryRun(out _, "rev-parse", "--git-dir"))
        {
            diagnostics.Add("git_repository_invalid");
            return Invalid(diagnostics);
        }

        string objectFormat = git.RunScalar(diagnostics, "git_object_format_unavailable", "rev-parse", "--show-object-format");
        string observedOid = git.RunScalar(diagnostics, "git_object_format_unavailable", "rev-parse", "--verify", "HEAD^{object}");
        int oidLength = IsObservedOid(observedOid) ? observedOid.Length : 0;
        if (oidLength == 0)
        {
            diagnostics.Add("git_unknown_object_format");
        }

        bool promisorRepository = ValidateRepositoryState(git, diagnostics);
        bool selectedValid = ParseSelected(request, diagnostics, out SemanticVersion selected);

        string candidateOid = ResolveCandidate(git, request.CandidateOid, oidLength, diagnostics);
        GitResolvedTag? baseStable = ResolveAnnotatedTag(git, request.BaseStableRef, oidLength, diagnostics);
        GitResolvedTag? previousPublished = ResolveAnnotatedTag(git, request.PreviousPublishedRef, oidLength, diagnostics);

        ValidateExpectedTagObject(request, baseStable, oidLength, diagnostics);
        ValidateExpectedTagObject(request, previousPublished, oidLength, diagnostics);
        bool baseline = IsBaselineTagRef(request.BaseStableRef) || IsBaselineTagRef(request.PreviousPublishedRef);
        ValidateDescriptorVersions(request, selectedValid ? selected : default, baseStable, previousPublished, diagnostics);

        if (candidateOid.Length != 0 && previousPublished is not null)
        {
            if (!git.IsSuccess("merge-base", "--is-ancestor", previousPublished.CommitOid, candidateOid))
            {
                diagnostics.Add("git_previous_not_ancestor");
            }
            else
            {
                if (!git.IsEmpty("rev-list", "--merges", $"{previousPublished.CommitOid}..{candidateOid}"))
                {
                    diagnostics.Add("git_non_linear_candidate");
                }
            }
        }

        if (candidateOid.Length != 0 && baseStable is not null && !git.IsSuccess("merge-base", "--is-ancestor", baseStable.CommitOid, candidateOid))
        {
            diagnostics.Add("git_base_not_ancestor");
            diagnostics.Add($"git_wrong_line_tag:{baseStable.Name}");
        }

        if (candidateOid.Length != 0 && baseline)
        {
            ValidateNoReachableStableTags(git, request, selectedValid ? selected : null, candidateOid, oidLength, diagnostics);
        }

        if (candidateOid.Length != 0 && selectedValid && previousPublished is not null && previousPublished.Version is not null)
        {
            ValidateAmbientTags(git, request, selected, previousPublished.Version.Value, candidateOid, oidLength, diagnostics);
        }

        if (promisorRepository && diagnostics.Any(diagnostic => diagnostic.StartsWith("git_missing_object:", StringComparison.Ordinal)))
        {
            diagnostics.Add("git_partial_clone_objects_missing");
        }

        if (diagnostics.Count != 0 || baseStable is null || previousPublished is null)
        {
            return Invalid(diagnostics);
        }

        var identity = new GitRepositoryObjectIdentity(
            objectFormat,
            oidLength,
            baseStable.Name,
            baseStable.CommitOid,
            baseStable.TagObjectOid,
            previousPublished.Name,
            previousPublished.CommitOid,
            previousPublished.TagObjectOid,
            candidateOid);
        return new GitReleaseValidationResult(true, identity, []);
    }

    private static bool ValidateRepositoryState(Git git, List<string> diagnostics)
    {
        if (string.Equals(git.RunScalar(diagnostics, "git_shallow_probe_failed", "rev-parse", "--is-shallow-repository"), "true", StringComparison.Ordinal))
        {
            diagnostics.Add("git_shallow_repository");
        }

        if (!git.IsEmpty("for-each-ref", "--format=%(refname)", "refs/replace"))
        {
            diagnostics.Add("git_replace_refs_present");
        }

        string graftsPath = git.ResolvePath(git.RunScalar(diagnostics, "git_directory_unavailable", "rev-parse", "--git-path", "info/grafts"));
        if (graftsPath.Length != 0 && File.Exists(graftsPath) && new FileInfo(graftsPath).Length != 0)
        {
            diagnostics.Add("git_grafts_present");
        }

        return !git.IsEmpty("config", "--get-regexp", "^remote\\..*\\.promisor$") ||
            git.RunScalar([], string.Empty, "config", "--get", "extensions.partialClone").Length != 0;
    }

    private static bool ParseSelected(GitReleaseValidationRequest request, List<string> diagnostics, out SemanticVersion selected)
    {
        if (!SemanticVersion.TryParse(request.SelectedVersion, out selected))
        {
            diagnostics.Add("git_selected_version_malformed");
            return false;
        }

        Match line = LinePattern.Match(request.ReleaseLine ?? string.Empty);
        if (!line.Success)
        {
            diagnostics.Add("git_release_line_malformed");
            return false;
        }

        if (!string.Equals(line.Groups["major"].Value, selected.Major.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) ||
            !string.Equals(line.Groups["minor"].Value, selected.Minor.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
        {
            diagnostics.Add("git_selected_version_line_mismatch");
        }

        return true;
    }

    private static void ValidateDescriptorVersions(
        GitReleaseValidationRequest request,
        SemanticVersion selected,
        GitResolvedTag? baseStable,
        GitResolvedTag? previousPublished,
        List<string> diagnostics)
    {
        bool baseline = IsBaselineTagRef(request.BaseStableRef) || IsBaselineTagRef(request.PreviousPublishedRef);
        if (baseline)
        {
            if (!IsBaselineTagRef(request.BaseStableRef) || !IsBaselineTagRef(request.PreviousPublishedRef) || request.BaseStableRef != request.PreviousPublishedRef)
            {
                diagnostics.Add("git_baseline_range_mismatch");
            }
            return;
        }

        if (!TryParseTagRef(request.BaseStableRef, out _, out SemanticVersion parsedBase) || parsedBase.IsPrerelease)
        {
            diagnostics.Add("git_base_stable_tag_malformed");
        }

        if (!TryParseTagRef(request.PreviousPublishedRef, out _, out SemanticVersion parsedPrevious))
        {
            diagnostics.Add("git_previous_published_tag_malformed");
        }

        if (baseStable?.Version is not null && !baseStable.Version.Value.Equals(parsedBase))
        {
            diagnostics.Add("git_base_stable_tag_malformed");
        }

        if (previousPublished?.Version is not null && !previousPublished.Version.Value.Equals(parsedPrevious))
        {
            diagnostics.Add("git_previous_published_tag_malformed");
        }

        if (previousPublished?.Version is not null && previousPublished.Version.Value.CompareTo(selected) >= 0)
        {
            diagnostics.Add("git_previous_tag_not_before_selected");
        }
    }

    private static void ValidateExpectedTagObject(GitReleaseValidationRequest request, GitResolvedTag? tag, int oidLength, List<string> diagnostics)
    {
        if (tag is null || request.ExpectedTagObjectOids?.TryGetValue(tag.Name, out string? expectedOid) != true || expectedOid is null)
        {
            return;
        }

        if (!IsFullOid(expectedOid, oidLength) || !string.Equals(expectedOid, tag.TagObjectOid, StringComparison.Ordinal))
        {
            diagnostics.Add($"git_tag_object_mismatch:{tag.Name}");
        }
    }

    /// <summary>
    /// A baseline lower bound is legal only before any governed stable SemVer release exists.
    /// The release currently being closed is explicitly not such a release: once its own tag is
    /// created, <c>verify-tag</c> re-runs this validation, and counting that tag would make the
    /// first governed release impossible to close and impossible to re-verify afterwards. The tag
    /// is skipped only when it names the selected version *and* points at the candidate commit, so
    /// a same-named tag on any other commit still fails closed.
    /// </summary>
    private static void ValidateNoReachableStableTags(Git git, GitReleaseValidationRequest request, SemanticVersion? selected, string candidateOid, int oidLength, List<string> diagnostics)
    {
        string output = git.RunScalar(diagnostics, "git_tag_scan_failed", "for-each-ref", "--format=%(refname)", "refs/tags");
        foreach (string tagRef in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(tagRef, request.BaseStableRef, StringComparison.Ordinal) ||
                string.Equals(tagRef, request.PreviousPublishedRef, StringComparison.Ordinal) ||
                !TryParseTagRef(tagRef, out string tag, out SemanticVersion version) ||
                version.IsPrerelease)
            {
                continue;
            }

            GitResolvedTag? resolved = ResolveAnnotatedTag(git, tagRef, oidLength, diagnostics);
            if (resolved is null || !git.IsSuccess("merge-base", "--is-ancestor", resolved.CommitOid, candidateOid))
            {
                continue;
            }

            if (selected is not null && version.CompareTo(selected.Value) == 0 && string.Equals(resolved.CommitOid, candidateOid, StringComparison.Ordinal))
            {
                continue;
            }

            diagnostics.Add($"git_baseline_stable_tag_exists:{tag}");
        }
    }

    private static GitResolvedTag? ResolveAnnotatedTag(Git git, string tagRef, int oidLength, List<string> diagnostics)
    {
        if (!TryParseTagRef(tagRef, out string tag, out SemanticVersion version) && !TryParseBaselineTagRef(tagRef, out tag))
        {
            diagnostics.Add($"git_malformed_tag_ref:{tagRef}");
            return null;
        }

        string type = git.RunScalar(diagnostics, $"git_missing_object:{tag}", "cat-file", "-t", tagRef);
        if (type.Length == 0)
        {
            return null;
        }

        if (!string.Equals(type, "tag", StringComparison.Ordinal))
        {
            diagnostics.Add($"git_lightweight_tag:{tag}");
            return null;
        }

        string tagObjectOid = ResolveObject(git, tagRef, oidLength, $"tag:{tag}", diagnostics);
        string commitOid = ResolveTagCommit(git, tagRef, oidLength, $"tag_target:{tag}", diagnostics);
        SemanticVersion? resolvedVersion = TryParseTagRef(tagRef, out _, out SemanticVersion parsed) ? parsed : null;
        return tagObjectOid.Length == 0 || commitOid.Length == 0 ? null : new GitResolvedTag(tag, resolvedVersion, commitOid, tagObjectOid);
    }

    private static void ValidateAmbientTags(
        Git git,
        GitReleaseValidationRequest request,
        SemanticVersion selected,
        SemanticVersion previousPublished,
        string candidateOid,
        int oidLength,
        List<string> diagnostics)
    {
        string output = git.RunScalar(diagnostics, "git_tag_scan_failed", "for-each-ref", "--format=%(refname)", "refs/tags");
        foreach (string tagRef in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(tagRef, request.BaseStableRef, StringComparison.Ordinal) ||
                string.Equals(tagRef, request.PreviousPublishedRef, StringComparison.Ordinal) ||
                !TryParseTagRef(tagRef, out string tag, out SemanticVersion version) ||
                version.Major != selected.Major ||
                version.Minor != selected.Minor ||
                version.CompareTo(previousPublished) <= 0 ||
                version.CompareTo(selected) >= 0)
            {
                continue;
            }

            GitResolvedTag? resolved = ResolveAnnotatedTag(git, tagRef, oidLength, diagnostics);
            if (resolved is null)
            {
                continue;
            }

            diagnostics.Add(git.IsSuccess("merge-base", "--is-ancestor", resolved.CommitOid, candidateOid)
                ? $"git_unexpected_newer_tag:{tag}"
                : $"git_wrong_line_tag:{tag}");
        }
    }

    private static bool TryParseTagRef(string tagRef, out string tag, out SemanticVersion version)
    {
        const string prefix = "refs/tags/";
        if (!tagRef.StartsWith(prefix, StringComparison.Ordinal))
        {
            tag = string.Empty;
            version = default;
            return false;
        }

        tag = tagRef[prefix.Length..];
        return SemanticVersion.TryParseTag(tag, out version);
    }

    private static bool TryParseBaselineTagRef(string tagRef, out string tag)
    {
        const string prefix = "refs/tags/";
        tag = tagRef.StartsWith(prefix, StringComparison.Ordinal) ? tagRef[prefix.Length..] : string.Empty;
        return BaselineRefPattern.IsMatch(tag);
    }

    private static bool IsBaselineTagRef(string tagRef) => TryParseBaselineTagRef(tagRef, out _);

    private static string ResolveCandidate(Git git, string candidateOid, int oidLength, List<string> diagnostics)
    {
        if (!IsFullOid(candidateOid, oidLength))
        {
            diagnostics.Add("git_object_id_not_full:candidate");
            return string.Empty;
        }

        return ResolveObject(git, $"{candidateOid}^{{commit}}", oidLength, "candidate", diagnostics);
    }

    private static string ResolveTagCommit(Git git, string tagRef, int oidLength, string label, List<string> diagnostics) =>
        ResolveObject(git, $"{tagRef}^{{commit}}", oidLength, label, diagnostics);

    private static string ResolveObject(Git git, string reference, int oidLength, string label, List<string> diagnostics)
    {
        string oid = git.RunScalar(diagnostics, $"git_missing_object:{label}", "rev-parse", "--verify", "--end-of-options", reference);
        if (oid.Length == 0)
        {
            return string.Empty;
        }

        if (!IsFullOid(oid, oidLength) || !git.IsSuccess("cat-file", "-e", $"{oid}^{{object}}"))
        {
            diagnostics.Add($"git_missing_object:{label}");
            return string.Empty;
        }

        return oid;
    }

    private static bool IsObservedOid(string value) => value.Length is >= 32 and <= 128 && value.Length % 2 == 0 && value.All(IsLowerHex);

    private static bool IsFullOid(string value, int length) => length != 0 && value.Length == length && value.All(IsLowerHex);

    private static bool IsLowerHex(char character) => character is >= '0' and <= '9' or >= 'a' and <= 'f';

    private static GitReleaseValidationResult Invalid(IReadOnlyList<string> diagnostics) => new(false, null, diagnostics.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());

    private sealed record GitResolvedTag(string Name, SemanticVersion? Version, string CommitOid, string TagObjectOid);

    private readonly struct SemanticVersion : IComparable<SemanticVersion>
    {
        private SemanticVersion(int major, int minor, int patch, string? stage, int number)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Stage = stage;
            Number = number;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string? Stage { get; }
        public int Number { get; }
        public bool IsPrerelease => Stage is not null;

        public static bool TryParseTag(string value, out SemanticVersion version)
        {
            if (value.StartsWith('v'))
            {
                return TryParse(value[1..], out version);
            }

            version = default;
            return false;
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            Match match = VersionPattern.Match(value ?? string.Empty);
            if (!match.Success ||
                !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
                !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
                !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
            {
                version = default;
                return false;
            }

            string? stage = match.Groups["stage"].Success ? match.Groups["stage"].Value : null;
            int number = match.Groups["number"].Success ? int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) : 0;
            version = new SemanticVersion(major, minor, patch, stage, number);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            int core = Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);
            if (core != 0)
            {
                return core;
            }

            if (Stage is null || other.Stage is null)
            {
                return Stage is null && other.Stage is null ? 0 : Stage is null ? 1 : -1;
            }

            int stage = StageRank(Stage).CompareTo(StageRank(other.Stage));
            return stage != 0 ? stage : Number.CompareTo(other.Number);
        }

        private static int StageRank(string value) => value switch
        {
            "alpha" => 0,
            "beta" => 1,
            "rc" => 2,
            _ => -1,
        };
    }

    private sealed class Git(string repositoryPath, TimeSpan timeout)
    {
        public bool IsSuccess(params string[] args) => TryRun(out _, args);

        public bool IsEmpty(params string[] args) => RunScalar([], string.Empty, args).Length == 0;

        public string ResolvePath(string path) => string.IsNullOrWhiteSpace(path) ? string.Empty : Path.IsPathRooted(path) ? path : Path.Combine(repositoryPath, path);

        public string RunScalar(List<string> diagnostics, string diagnostic, params string[] args)
        {
            if (TryRun(out string output, args))
            {
                return output.Trim();
            }

            if (!string.IsNullOrEmpty(diagnostic))
            {
                diagnostics.Add(diagnostic);
            }

            return string.Empty;
        }

        public bool TryRun(out string output, params string[] args)
        {
            output = string.Empty;
            string isolationDirectory = Path.Combine(Path.GetTempPath(), $"islamu-release-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(isolationDirectory);
            IReadOnlyDictionary<string, string> deterministicEnvironment = CanonicalArtifactPolicy.CreateDeterministicEnvironment(isolationDirectory);
            File.WriteAllText(deterministicEnvironment["GIT_CONFIG_GLOBAL"], string.Empty);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };
            foreach ((string key, string value) in deterministicEnvironment)
            {
                process.StartInfo.Environment[key] = value;
            }

            process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
            foreach (string variable in RepositoryEnvironmentVariables)
            {
                process.StartInfo.Environment.Remove(variable);
            }
            process.StartInfo.ArgumentList.Add("--no-replace-objects");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={NullDevice}");
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(repositoryPath);
            foreach (string arg in args)
            {
                process.StartInfo.ArgumentList.Add(arg);
            }

            try
            {
                process.Start();
                using var timeoutSource = new CancellationTokenSource(timeout);
                Task<string> stdout = ReadBoundedAsync(process.StandardOutput, timeoutSource.Token);
                Task<string> stderr = ReadBoundedAsync(process.StandardError, timeoutSource.Token);
                process.WaitForExitAsync(timeoutSource.Token).GetAwaiter().GetResult();
                output = stdout.GetAwaiter().GetResult();
                stderr.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                return false;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                return false;
            }
            finally
            {
                if (Directory.Exists(isolationDirectory))
                {
                    Directory.Delete(isolationDirectory, recursive: true);
                }
            }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
        {
            var buffer = new char[MaximumGitOutput];
            int count = 0;
            while (count < buffer.Length)
            {
                int read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
                if (read == 0)
                {
                    return new string(buffer, 0, count);
                }

                count += read;
            }

            if (await reader.ReadAsync(new char[1], cancellationToken) != 0)
            {
                throw new OperationCanceledException();
            }

            return new string(buffer);
        }

        private static string NullDevice => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

        private static readonly string[] RepositoryEnvironmentVariables =
        [
            "GIT_ALTERNATE_OBJECT_DIRECTORIES",
            "GIT_CEILING_DIRECTORIES",
            "GIT_COMMON_DIR",
            "GIT_CONFIG_COUNT",
            "GIT_CONFIG_KEY_0",
            "GIT_CONFIG_PARAMETERS",
            "GIT_CONFIG_SYSTEM",
            "GIT_CONFIG_VALUE_0",
            "GIT_DIR",
            "GIT_DISCOVERY_ACROSS_FILESYSTEM",
            "GIT_INDEX_FILE",
            "GIT_NAMESPACE",
            "GIT_OBJECT_DIRECTORY",
            "GIT_SHALLOW_FILE",
            "GIT_WORK_TREE",
        ];
    }
}
