// ABOUTME: Verifies stable-main release topology and emits provider-neutral protected-ref actions.
// ABOUTME: Uses local Git object/ref checks only and never mutates, fetches, pushes, or executes candidates.

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public static class MainCommand
{
    private const int MaximumGitOutputCharacters = 16_384;
    private const int MaximumEvidenceBytes = 1_048_576;
    private const int MaximumReleaseDirectories = 1_024;
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex StableVersionPattern = new("^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));

    public static int Run(string[] args, TextWriter output, string repositoryRoot, TimeSpan timeout)
    {
        if (args.Length != 4)
        {
            output.WriteLine("invalid_arguments: verify-main requires release directory, expected old origin/main oid, and tag object oid");
            return Program.UsageError;
        }

        try
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30)) return Reject(output, "release_main_timeout_invalid");
            string root = ResolveDirectory(repositoryRoot);
            if (!ValidateRepositoryState(root, timeout)) return Reject(output, "release_main_repository_state_invalid");
            if (!FullOidPattern.IsMatch(args[2]) || !FullOidPattern.IsMatch(args[3])) return Reject(output, "release_main_oid_not_full");

            string releaseDirectory = ResolveChild(root, args[1], mustExist: true);
            string releasesRoot = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases");
            if (!string.Equals(Path.GetDirectoryName(releaseDirectory), releasesRoot, PathComparison)) return Reject(output, "release_main_path_invalid");

            string expectedOld = args[2];
            string expectedTagObject = args[3];
            MainEvidence evidence = ReadEvidence(Path.Combine(releaseDirectory, "release-evidence.v1.json"));
            if (IsPrerelease(evidence.Version)) return Reject(output, "release_main_prerelease_no_move");
            if (!TryParseStableVersion(evidence.Version, out StableVersion selected)) return Reject(output, "release_main_version_invalid");
            if (!string.Equals(evidence.Line, selected.Line, StringComparison.Ordinal) ||
                !string.Equals(evidence.TagName, $"v{evidence.Version}", StringComparison.Ordinal) ||
                !FullOidPattern.IsMatch(evidence.ReleaseContextSha256) ||
                !FullOidPattern.IsMatch(evidence.TargetOid) ||
                !FullOidPattern.IsMatch(evidence.CandidateOid) ||
                !FullOidPattern.IsMatch(evidence.TagObjectId))
            {
                return Reject(output, "release_main_evidence_invalid");
            }

            string contextPath = Path.Combine(releaseDirectory, "release-context.v1.json");
            if (!File.Exists(contextPath)) return Reject(output, "release_main_forward_port_evidence_invalid");
            if (!string.Equals(Sha256(ReadFileBounded(contextPath)), evidence.ReleaseContextSha256, StringComparison.Ordinal)) return Reject(output, "release_main_evidence_invalid");

            if (!string.Equals(evidence.TargetOid, evidence.CandidateOid, StringComparison.Ordinal)) return Reject(output, "release_main_evidence_target_mismatch");
            if (!string.Equals(evidence.TagObjectId, expectedTagObject, StringComparison.Ordinal)) return Reject(output, "release_main_tag_object_mismatch");
            if (!ValidateTag(root, evidence, timeout)) return Reject(output, "release_main_tag_object_mismatch");
            if (!ObjectExists(root, expectedOld, timeout)) return Reject(output, "release_main_expected_old_missing");
            if (!ObservedMainEquals(root, expectedOld, timeout)) return Reject(output, "release_main_cas_mismatch");

            bool newestStable = IsNewestStable(root, releasesRoot, selected, expectedOld, timeout);
            string action;
            string instruction;
            if (!newestStable)
            {
                string? forwardPortFailure = ValidateForwardPort(root, releaseDirectory, evidence, expectedOld, timeout);
                if (forwardPortFailure is not null) return Reject(output, forwardPortFailure);
                action = "no-main-move";
                instruction = "publish-release-without-main-update";
            }
            else if (string.Equals(expectedOld, evidence.TargetOid, StringComparison.Ordinal))
            {
                action = "already-at-target";
                instruction = "no-op-main-already-at-release";
            }
            else
            {
                if (!IsAncestor(root, expectedOld, evidence.TargetOid, timeout)) return Reject(output, "release_main_non_fast_forward");
                action = "move-main";
                instruction = "update-main-fast-forward";
            }

            if (!ObservedMainEquals(root, expectedOld, timeout)) return Reject(output, "release_main_cas_mismatch");
            if (!ValidateTag(root, evidence, timeout)) return Reject(output, "release_main_tag_object_mismatch");
            output.WriteLine($"release_main_verified: action={action} old={expectedOld} new={evidence.TargetOid} tag={evidence.TagName} instruction={instruction}");
            return Program.Success;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("release_main_", StringComparison.Ordinal))
        {
            return Reject(output, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            return Reject(output, "release_main_input_invalid");
        }
    }

    private static MainEvidence ReadEvidence(string path)
    {
        byte[] bytes = ReadFileBounded(path);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(System.Text.Encoding.UTF8.GetString(bytes));
        if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes)) throw new InvalidOperationException("release_main_evidence_invalid");
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (!TryString(root, "schemaVersion", out string schema) || schema != "release-evidence.v1" ||
            !TryString(root, "version", out string version) ||
            !TryString(root, "line", out string line) ||
            !TryString(root, "tagName", out string tagName) ||
            !TryString(root, "tagObjectId", out string tagObjectId) ||
            !TryString(root, "targetOid", out string targetOid) ||
            !TryString(root, "candidateOid", out string candidateOid))
        {
            throw new InvalidOperationException("release_main_evidence_invalid");
        }

        if (!TryString(root, "releaseContextSha256", out string releaseContextSha256)) throw new InvalidOperationException("release_main_evidence_invalid");
        return new MainEvidence(version, line, tagName, tagObjectId, targetOid, candidateOid, releaseContextSha256);
    }

    private static bool IsNewestStable(string root, string releasesRoot, StableVersion selected, string expectedOld, TimeSpan timeout)
    {
        string[] directories = Directory.EnumerateDirectories(releasesRoot).Take(MaximumReleaseDirectories + 1).ToArray();
        if (directories.Length > MaximumReleaseDirectories) throw new InvalidOperationException("release_main_release_count_exceeded");
        foreach (string directory in directories)
        {
            string name = Path.GetFileName(directory);
            if (!TryParseStableVersion(name, out StableVersion candidate) || candidate.CompareTo(selected) <= 0) continue;
            try
            {
                MainEvidence evidence = ReadEvidence(Path.Combine(directory, "release-evidence.v1.json"));
                if (!string.Equals(evidence.Version, name, StringComparison.Ordinal) ||
                    !string.Equals(evidence.Line, candidate.Line, StringComparison.Ordinal) ||
                    !string.Equals(evidence.TagName, $"v{name}", StringComparison.Ordinal) ||
                    !string.Equals(Sha256(ReadFileBounded(Path.Combine(directory, "release-context.v1.json"))), evidence.ReleaseContextSha256, StringComparison.Ordinal) ||
                    !FullOidPattern.IsMatch(evidence.TargetOid) ||
                    !string.Equals(evidence.TargetOid, evidence.CandidateOid, StringComparison.Ordinal) ||
                    !FullOidPattern.IsMatch(evidence.TagObjectId) ||
                    !ValidateTag(root, evidence, timeout))
                {
                    continue;
                }

                if (string.Equals(evidence.TargetOid, expectedOld, StringComparison.Ordinal)) return false;

                string relative = Path.GetRelativePath(root, directory).Replace(Path.DirectorySeparatorChar, '/');
                using var ignored = new StringWriter(CultureInfo.InvariantCulture);
                if (TagCommand.Run(["verify-tag", relative, evidence.TargetOid, evidence.TagObjectId], ignored, root, string.Empty, timeout) == Program.Success)
                {
                    return false;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
            {
            }
        }

        return true;
    }

    private static string? ValidateForwardPort(string root, string releaseDirectory, MainEvidence evidence, string expectedOld, TimeSpan timeout)
    {
        // The release being forward-ported is identified by its own signed tag, never by the branch
        // that happened to carry its commits. `Line` is a version-line label here, not a ref.
        if (!string.Equals(RunGit(root, timeout, "rev-parse", "--verify", $"refs/tags/{evidence.TagName}^{{commit}}").Trim(), evidence.TargetOid, StringComparison.Ordinal))
        {
            return "release_main_release_tag_target_mismatch";
        }

        try
        {
            byte[] bytes = ReadFileBounded(Path.Combine(releaseDirectory, "release-context.v1.json"));
            CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(System.Text.Encoding.UTF8.GetString(bytes));
            if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes)) return "release_main_forward_port_evidence_invalid";
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement rootElement = document.RootElement;
            if (!rootElement.TryGetProperty("schemaVersion", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number || schema.GetInt32() != 1 ||
                !rootElement.TryGetProperty("changes", out JsonElement changes) || changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
            {
                return "release_main_forward_port_evidence_invalid";
            }

            foreach (JsonElement change in changes.EnumerateArray())
            {
                if (!TryString(change, "changeId", out string changeId) || !ChangeIdPolicy.IsValid(changeId) ||
                    !TryString(change, "oid", out string oid) || !FullOidPattern.IsMatch(oid) ||
                    !change.TryGetProperty("backport", out JsonElement backport) || backport.ValueKind is not JsonValueKind.True ||
                    !TryString(change, "backportOf", out string backportOf) || !FullOidPattern.IsMatch(backportOf))
                {
                    return "release_main_forward_port_evidence_invalid";
                }

                if (!IsAncestor(root, oid, evidence.TargetOid, timeout)) return $"release_main_backport_not_on_release:{changeId}";
                if (!IsAncestor(root, backportOf, expectedOld, timeout)) return $"release_main_forward_port_not_on_main:{changeId}";
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            return "release_main_forward_port_evidence_invalid";
        }
    }

    private static bool TryParseStableVersion(string value, out StableVersion version)
    {
        Match match = StableVersionPattern.Match(value ?? string.Empty);
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int major) ||
            !int.TryParse(match.Groups["minor"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minor) ||
            !int.TryParse(match.Groups["patch"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
        {
            version = default;
            return false;
        }

        version = new StableVersion(major, minor, patch);
        return true;
    }

    private static bool ValidateTag(string root, MainEvidence evidence, TimeSpan timeout)
    {
        string type = RunGit(root, timeout, "cat-file", "-t", evidence.TagObjectId).Trim();
        string tagObject = RunGit(root, timeout, "rev-parse", "--verify", $"refs/tags/{evidence.TagName}^{{object}}").Trim();
        string target = RunGit(root, timeout, "rev-parse", "--verify", $"{evidence.TagObjectId}^{{commit}}").Trim();
        return string.Equals(type, "tag", StringComparison.Ordinal) &&
            string.Equals(tagObject, evidence.TagObjectId, StringComparison.Ordinal) &&
            string.Equals(target, evidence.TargetOid, StringComparison.Ordinal);
    }

    private static bool IsPrerelease(string version) => version.Contains('-', StringComparison.Ordinal) || version.Contains('+', StringComparison.Ordinal);
    private static bool ValidateRepositoryState(string root, TimeSpan timeout)
    {
        if (!string.Equals(RunGit(root, timeout, "rev-parse", "--is-shallow-repository").Trim(), "false", StringComparison.Ordinal)) return false;
        if (RunGit(root, timeout, "for-each-ref", "--format=%(refname)", "refs/replace").Length != 0) return false;
        string gitDirectory = RunGit(root, timeout, "rev-parse", "--absolute-git-dir").Trim();
        if (File.Exists(Path.Combine(gitDirectory, "info", "grafts")) && new FileInfo(Path.Combine(gitDirectory, "info", "grafts")).Length != 0) return false;
        bool promisor = TryRunGit(root, timeout, out _, "config", "--get-regexp", "^remote\\..*\\.promisor$") || TryRunGit(root, timeout, out _, "config", "--get", "extensions.partialClone");
        return !promisor;
    }

    private static bool ObjectExists(string root, string oid, TimeSpan timeout) => TryRunGit(root, timeout, out _, "cat-file", "-e", $"{oid}^{{commit}}");
    private static bool IsAncestor(string root, string oldOid, string newOid, TimeSpan timeout) => TryRunGit(root, timeout, out _, "merge-base", "--is-ancestor", oldOid, newOid);
    private static bool ObservedMainEquals(string root, string expectedOld, TimeSpan timeout) => TryRunGit(root, timeout, out string value, "rev-parse", "--verify", "refs/remotes/origin/main^{commit}") && string.Equals(value.Trim(), expectedOld, StringComparison.Ordinal);

    private static string ResolveDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) || IsLink(full)) throw new DirectoryNotFoundException(full);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveChild(string root, string relativePath, bool mustExist)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("absolute paths are not accepted", nameof(relativePath));
        string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) || IsLink(full)) throw new ArgumentException("path escapes repository root", nameof(relativePath));
        if (mustExist && !Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static string RunGit(string root, TimeSpan timeout, params string[] args)
    {
        if (!TryRunGit(root, timeout, out string result, args)) throw new InvalidOperationException("release_main_git_failed");
        return result;
    }

    private static bool TryRunGit(string root, TimeSpan timeout, out string output, params string[] args)
    {
        output = string.Empty;
        string isolationDirectory = Path.Combine(Path.GetTempPath(), $"islamu-main-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolationDirectory);
        try
        {
            IReadOnlyDictionary<string, string> environment = CanonicalArtifactPolicy.CreateDeterministicEnvironment(isolationDirectory);
            File.WriteAllText(environment["GIT_CONFIG_GLOBAL"], string.Empty);
            bool useProcessGroup = !OperatingSystem.IsWindows();
            using var process = new Process { StartInfo = new ProcessStartInfo(useProcessGroup ? "setsid" : "git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            foreach ((string key, string value) in environment) process.StartInfo.Environment[key] = value;
            process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
            foreach (string variable in RepositoryEnvironmentVariables) process.StartInfo.Environment.Remove(variable);
            if (useProcessGroup) process.StartInfo.ArgumentList.Add("git");
            process.StartInfo.ArgumentList.Add("--no-replace-objects");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={NullDevice}");
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(root);
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);
            try
            {
                process.Start();
                using var cancellation = new CancellationTokenSource(timeout);
                Task<string> stdout = ReadBoundedAsync(process.StandardOutput, cancellation.Token);
                Task<string> stderr = ReadBoundedAsync(process.StandardError, cancellation.Token);
                process.WaitForExitAsync(cancellation.Token).GetAwaiter().GetResult();
                output = stdout.GetAwaiter().GetResult();
                stderr.GetAwaiter().GetResult();
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                KillGitProcess(process, useProcessGroup);
                return false;
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or Win32Exception)
            {
                KillGitProcess(process, useProcessGroup);
                return false;
            }
        }
        finally
        {
            if (Directory.Exists(isolationDirectory)) Directory.Delete(isolationDirectory, recursive: true);
        }
    }

    private static void KillGitProcess(Process process, bool processGroup)
    {
        try
        {
            if (processGroup)
            {
                SendSignalToProcessGroup(process.Id, "-TERM");
                Thread.Sleep(50);
                SendSignalToProcessGroup(process.Id, "-KILL");
            }
            else if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
        }

        try
        {
            if (!process.WaitForExit(500) && !process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
        }
    }

    private static void SendSignalToProcessGroup(int processId, string signal)
    {
        try
        {
            using var kill = new Process { StartInfo = new ProcessStartInfo("kill") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            kill.StartInfo.ArgumentList.Add(signal);
            kill.StartInfo.ArgumentList.Add($"-{processId}");
            kill.Start();
            kill.WaitForExit(500);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or IOException)
        {
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumGitOutputCharacters];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(count, buffer.Length - count), cancellationToken);
            if (read == 0) return new string(buffer, 0, count);
            count += read;
        }

        if (await reader.ReadAsync(new char[1], cancellationToken) != 0) throw new OperationCanceledException();
        return new string(buffer);
    }

    private static byte[] ReadFileBounded(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
        if (stream.Length > MaximumEvidenceBytes) throw new InvalidOperationException("release_main_evidence_invalid");
        var bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        return element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String && (value = property.GetString() ?? string.Empty).Length != 0;
    }

    private static bool IsLink(string path) => File.Exists(path) || Directory.Exists(path) ? (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 : false;
    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static int Reject(TextWriter output, string reason) { output.WriteLine($"verify_main_failed: {reason}"); return Program.ToolchainRejected; }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static string NullDevice => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
    private static readonly string[] RepositoryEnvironmentVariables = ["GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_CEILING_DIRECTORIES", "GIT_COMMON_DIR", "GIT_CONFIG_COUNT", "GIT_CONFIG_KEY_0", "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_VALUE_0", "GIT_DIR", "GIT_DISCOVERY_ACROSS_FILESYSTEM", "GIT_INDEX_FILE", "GIT_NAMESPACE", "GIT_OBJECT_DIRECTORY", "GIT_SHALLOW_FILE", "GIT_WORK_TREE"];

    private sealed record MainEvidence(string Version, string Line, string TagName, string TagObjectId, string TargetOid, string CandidateOid, string ReleaseContextSha256);
    private readonly record struct StableVersion(int Major, int Minor, int Patch) : IComparable<StableVersion>
    {
        public string Line => $"v{Major}.{Minor}";
        public int CompareTo(StableVersion other) => Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);
    }
}
