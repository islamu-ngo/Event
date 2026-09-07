// ABOUTME: Plans an idempotent maintenance-line branch sourced only from a verified signed stable release tag.
// ABOUTME: Emits the exact operator command and compare-and-swap IDs without creating, moving, or deleting any ref.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Maintenance lines are lazy and disposable: nothing is provisioned at release time, and a branch
/// is opened only when a real backport needs somewhere to accumulate commits before the next patch
/// tag. This command is the planner for that mutating step, so — unlike attestation — it is allowed
/// to observe <c>refs/heads/*</c>. It observes only; the operator performs the ref change.
///
/// The source is never supplied by the caller. It is derived from the release tag this command
/// re-verifies, which structurally rules out opening a line from <c>develop</c>, <c>main</c>, or an
/// arbitrary commit: such a branch would carry commits that were never in the release.
/// </summary>
public static class MaintenanceLineCommand
{
    private const int MaximumEvidenceBytes = 1_048_576;
    private static readonly Regex StableVersionPattern = new("^(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromMilliseconds(100));
    private static readonly Regex FullOidPattern = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static int Run(string[] args, TextWriter output, string repositoryRoot, string platform, TimeSpan timeout)
    {
        if (args.Length != 3)
        {
            output.WriteLine("invalid_arguments: open-maintenance-line requires release directory and tag object oid");
            return Program.UsageError;
        }

        try
        {
            string root = ResolveDirectory(repositoryRoot);
            string releaseDirectory = ResolveChild(root, args[1]);
            if (!string.Equals(Path.GetDirectoryName(releaseDirectory), Path.Join(Path.GetFullPath(root), "docs", "internal", "releases"), PathComparison))
            {
                return Reject(output, "maintenance_line_path_invalid");
            }

            string tagObjectId = args[2];
            if (!FullOidPattern.IsMatch(tagObjectId)) return Reject(output, "maintenance_line_oid_not_full");

            MaintenanceEvidence evidence = ReadEvidence(Path.Combine(releaseDirectory, "release-evidence.v1.json"));
            if (!StableVersionPattern.IsMatch(evidence.Version)) return Reject(output, "maintenance_line_prerelease_not_supported");
            if (!string.Equals(evidence.TagObjectId, tagObjectId, StringComparison.Ordinal)) return Reject(output, "maintenance_line_tag_object_mismatch");

            // Re-verify the tag through the promoted bundle rather than trusting the evidence file
            // that sits beside it. A maintenance line inherits the release's whole trust chain.
            string relativeReleaseDirectory = Path.GetRelativePath(root, releaseDirectory).Replace(Path.DirectorySeparatorChar, '/');
            using var verification = new StringWriter(CultureInfo.InvariantCulture);
            if (TagCommand.Run(["verify-tag", relativeReleaseDirectory, evidence.TargetOid, tagObjectId], verification, root, platform, timeout) != Program.Success)
            {
                return Reject(output, "maintenance_line_tag_unverified");
            }

            string branchRef;
            try
            {
                branchRef = ReleaseRefNamespacePolicy.MaintenanceBranchRefForLine(evidence.Line);
            }
            catch (ArgumentException)
            {
                return Reject(output, "maintenance_line_label_malformed");
            }

            RefNamespaceDecision namespaceDecision = ReleaseRefNamespacePolicy.EvaluateBranchCreation(branchRef);
            if (!namespaceDecision.IsAllowed || !ReleaseRefNamespacePolicy.IsMaintenanceBranchRef(branchRef))
            {
                return Reject(output, "maintenance_line_reserved_namespace");
            }

            string branchName = branchRef["refs/heads/".Length..];
            string command = $"git switch -c {branchName} {evidence.TagName}";

            // Observing the branch head here is a mutation precondition, not release identity.
            if (!TryRunGit(root, timeout, out string observed, "rev-parse", "--verify", "--end-of-options", $"{branchRef}^{{commit}}"))
            {
                output.WriteLine($"maintenance_line_verified: action=create-maintenance-line branch={branchRef} source-tag={evidence.TagName} expected-old=none expected-new={evidence.TargetOid} instruction={command}");
                return Program.Success;
            }

            string branchOid = observed.Trim();
            if (!FullOidPattern.IsMatch(branchOid)) return Reject(output, "maintenance_line_repository_state_invalid");

            // An existing branch is acceptable only if the released commit is actually on it. A line
            // cut from develop or main fails here, which is exactly the unsound case Decision 11
            // rejects: it would ship unreviewed integration work under a patch version.
            if (!TryRunGit(root, timeout, out _, "merge-base", "--is-ancestor", evidence.TargetOid, branchOid))
            {
                return Reject(output, "maintenance_line_source_not_release_tag");
            }

            // Re-running is a no-op and never force-updates, so the command is safe to repeat.
            output.WriteLine($"maintenance_line_verified: action=already-open branch={branchRef} source-tag={evidence.TagName} expected-old={branchOid} expected-new={branchOid} instruction=no-op-maintenance-line-already-open");
            return Program.Success;
        }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("maintenance_line_", StringComparison.Ordinal))
        {
            return Reject(output, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return Reject(output, "maintenance_line_input_invalid");
        }
    }

    private static MaintenanceEvidence ReadEvidence(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumEvidenceBytes) throw new InvalidOperationException("maintenance_line_evidence_invalid");
        byte[] bytes = File.ReadAllBytes(path);
        CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeJson(Encoding.UTF8.GetString(bytes));
        if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes)) throw new InvalidOperationException("maintenance_line_evidence_invalid");

        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !TryString(root, "schemaVersion", out string schema) || schema != "release-evidence.v1" ||
            !TryString(root, "version", out string version) ||
            !TryString(root, "line", out string line) ||
            !TryString(root, "tagName", out string tagName) ||
            !TryString(root, "tagObjectId", out string tagObjectId) ||
            !TryString(root, "targetOid", out string targetOid) ||
            !FullOidPattern.IsMatch(tagObjectId) ||
            !FullOidPattern.IsMatch(targetOid))
        {
            throw new InvalidOperationException("maintenance_line_evidence_invalid");
        }

        return new MaintenanceEvidence(version, line, tagName, tagObjectId, targetOid);
    }

    private static bool TryString(JsonElement element, string key, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(key, out JsonElement property) || property.ValueKind != JsonValueKind.String) return false;
        value = property.GetString() ?? string.Empty;
        return value.Length != 0;
    }

    private static bool TryRunGit(string root, TimeSpan timeout, out string output, params string[] args)
    {
        output = string.Empty;
        string isolationDirectory = Path.Combine(Path.GetTempPath(), $"islamu-maintenance-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolationDirectory);
        try
        {
            IReadOnlyDictionary<string, string> environment = CanonicalArtifactPolicy.CreateDeterministicEnvironment(isolationDirectory);
            File.WriteAllText(environment["GIT_CONFIG_GLOBAL"], string.Empty);
            using var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo("git")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
            };
            foreach ((string key, string value) in environment) process.StartInfo.Environment[key] = value;
            process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
            process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
            process.StartInfo.ArgumentList.Add("--no-replace-objects");
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={NullDevice}");
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(root);
            foreach (string arg in args) process.StartInfo.ArgumentList.Add(arg);

            process.Start();
            string standardOutput = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(timeout))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            output = standardOutput;
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
        finally
        {
            if (Directory.Exists(isolationDirectory)) Directory.Delete(isolationDirectory, recursive: true);
        }
    }

    private static string ResolveDirectory(string path)
    {
        string full = Path.GetFullPath(path);
        if (!Directory.Exists(full) || IsLink(full)) throw new DirectoryNotFoundException(full);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string ResolveChild(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath)) throw new ArgumentException("absolute paths are not accepted", nameof(relativePath));
        string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison) || IsLink(full)) throw new ArgumentException("path escapes repository root", nameof(relativePath));
        if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full);
        return full;
    }

    private static bool IsLink(string path) => (File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine($"open_maintenance_line_failed: {diagnostic}");
        return Program.ToolchainRejected;
    }

    private static string NullDevice => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record MaintenanceEvidence(string Version, string Line, string TagName, string TagObjectId, string TargetOid);
}
