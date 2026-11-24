using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Allocates optional high-impact public change identifiers and checks commit policy and fragment
/// provenance against bounded target ranges and local Git preflight hooks.
/// </summary>
public static class ChangeWorkflowCommand
{
    private const int MaximumGitOutputCharacters = 4_194_304;
    private const string TargetOption = "--target";
    private const string DefaultTarget = "develop";
    private const string GitRevisionCommand = "rev-parse";
    private const string HookMarker = "# ISLAMU_RELEASE_CHANGE_HOOK";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Dispatches a change workflow against the resolved Git repository and reports policy
    /// rejection diagnostics as release-engine exit codes within the supplied Git timeout.
    /// </summary>
    public static int Run(string[] args, TextWriter output, string repositoryRoot, TimeSpan timeout)
    {
        if (args.Length == 0)
        {
            output.WriteLine("invalid_arguments: change workflow command is required");
            return Program.UsageError;
        }

        try
        {
            if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromSeconds(30))
            {
                return Reject(output, "change_workflow_timeout_invalid");
            }

            string root = ResolveRepositoryRoot(repositoryRoot, timeout);
            return args[0] switch
            {
                "allocate-change-id" => Allocate(args, output, root, timeout),
                "create-change" => Create(args, output, root, timeout),
                "preflight-commit" => PreflightCommit(args, output, root, timeout),
                "preflight-staged" => PreflightStaged(args, output, root, timeout),
                "preflight-range" => PreflightRange(args, output, root, timeout),
                "install-change-hooks" => InstallHooks(args, output, root, timeout),
                _ => Program.UsageError,
            };
        }
        catch (ChangeWorkflowException exception)
        {
            return Reject(output, exception.Diagnostic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or DecoderFallbackException)
        {
            return Reject(output, "change_workflow_input_invalid");
        }
    }

    /// <summary>Verifies the target ref and allocates an identifier absent from bounded repository provenance.</summary>
    private static int Allocate(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, [TargetOption]);
        string target = options.Get(TargetOption) ?? DefaultTarget;
        ResolveCommit(root, target, timeout);
        string id = AllocateUnused(root, target, timeout);
        output.WriteLine($"change_id_allocated: {id}");
        return Program.Success;
    }

    /// <summary>Validates public change metadata and atomically creates an optional high-impact fragment with its exact commit footer.</summary>
    private static int Create(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(
            args,
            1,
            [TargetOption, "--type", "--scope", "--title", "--summary", "--group"]);
        string target = options.Get(TargetOption) ?? DefaultTarget;
        ResolveCommit(root, target, timeout);
        string type = options.Require("--type");
        string scope = options.Require("--scope");
        string title = ValidateSingleLine(options.Require("--title"), "change_title_invalid");
        string summary = ValidateSingleLine(options.Require("--summary"), "change_summary_invalid");
        string? group = options.Get("--group");
        if (group is not null)
        {
            group = ValidateToken(group, "change_group_invalid");
        }

        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
        CommitPolicyResult commit = policy.EvaluateCommit($"{type}({scope}): {title}");
        if (!commit.IsValid)
        {
            throw new ChangeWorkflowException($"change_policy_invalid:{commit.Diagnostics[0]}");
        }

        string id = AllocateUnused(root, target, timeout);
        string directory = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "changes");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, id + ".yaml");
        string yaml = Fragment(id, title, type, scope, summary, group);
        ValidateFragmentContent(yaml, id);
        WriteAtomic(path, StrictUtf8.GetBytes(yaml));
        output.WriteLine($"change_created: id={id} path={Relative(root, path)}");
        output.WriteLine($"commit_footer: Change-Id: {id}");
        return Program.Success;
    }

    /// <summary>Rejects invalid commit policy, target identifier reuse, and invalid or unindexed optional fragments before commit.</summary>
    private static int PreflightCommit(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException("preflight_commit_message_path_required");
        }

        Options options = ParseOptions(args, 2, [TargetOption]);
        string target = options.Get(TargetOption) ?? DefaultTarget;
        ResolveCommit(root, target, timeout);
        string messagePath = Path.GetFullPath(args[1]);
        if (!File.Exists(messagePath) || IsLink(messagePath))
        {
            throw new ChangeWorkflowException("preflight_commit_message_invalid");
        }

        string rawMessage = StrictUtf8.GetString(File.ReadAllBytes(messagePath));
        string message = ReleasePolicy.StripGitComments(rawMessage);
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
        CommitPolicyResult proposed = policy.EvaluateCommit(message);
        if (!proposed.IsValid)
        {
            throw new ChangeWorkflowException($"commit_policy_invalid:{proposed.Diagnostics[0]}");
        }

        if (proposed.ChangeId is null)
        {
            output.WriteLine("change_commit_verified: change-id=none");
            return Program.Success;
        }

        HashSet<string> reachable = CollectBoundedCommittedIds(root, target, policy, timeout);
        if (reachable.Contains(proposed.ChangeId))
        {
            throw new ChangeWorkflowException($"change_id_already_reachable:{proposed.ChangeId}");
        }

        ValidateIndexedFragment(root, proposed.ChangeId, timeout);
        output.WriteLine($"change_commit_verified: change-id={proposed.ChangeId} target={target}");
        return Program.Success;
    }

    /// <summary>Rejects committed fragment mutation and validates staged optional fragments with full ReleaseInputPolicy.</summary>
    private static int PreflightStaged(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, [TargetOption]);
        string target = options.Get(TargetOption) ?? DefaultTarget;
        ResolveCommit(root, target, timeout);
        string[] staged = RunGit(
                root,
                timeout,
                "diff",
                "--cached",
                "--name-only",
                "--diff-filter=AM",
                "--",
                "docs/internal/releases/changes")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] modified = RunGit(
                root,
                timeout,
                "diff",
                "--cached",
                "--name-only",
                "--diff-filter=M",
                "--",
                "docs/internal/releases/changes")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (modified.Length != 0)
        {
            throw new ChangeWorkflowException($"staged_fragment_mutation:{modified[0]}");
        }

        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
        HashSet<string> reachable = CollectBoundedCommittedIds(root, target, policy, timeout);

        foreach (string relative in staged)
        {
            string id = Path.GetFileNameWithoutExtension(relative);
            if (!ChangeIdPolicy.IsValid(id) || !relative.EndsWith(".yaml", StringComparison.Ordinal))
            {
                throw new ChangeWorkflowException($"staged_fragment_invalid:{relative}");
            }

            if (!TryRunGit(root, timeout, out string stagedText, "show", $":{relative}"))
            {
                throw new ChangeWorkflowException($"staged_fragment_invalid:{relative}");
            }

            ValidateFragmentContent(stagedText, id);

            bool existedAtHead = TryRunGit(root, timeout, out _, "cat-file", "-e", $"HEAD:{relative}");
            if (!existedAtHead && reachable.Contains(id))
            {
                throw new ChangeWorkflowException($"change_id_already_reachable:{id}");
            }
        }

        output.WriteLine($"change_staged_verified: fragments={staged.Length} target={target}");
        return Program.Success;
    }

    /// <summary>Checks clean release inputs, commit policy, identifier uniqueness, and full fragment validity across the explicit target..head range.</summary>
    private static int PreflightRange(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, [TargetOption, "--head"]);
        string target = options.Get(TargetOption) ?? DefaultTarget;
        string head = options.Get("--head") ?? "HEAD";
        ResolveCommit(root, target, timeout);
        ResolveCommit(root, head, timeout);
        EnsureReleaseSourcesClean(root, timeout);
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);

        ReleaseCommit[] targetCommits = ReadCommits(root, target, timeout);
        ReleaseCommit[] rangeCommits = ReadCommits(root, $"{target}..{head}", timeout, reverse: true);
        foreach (ReleaseCommit commit in rangeCommits)
        {
            CommitPolicyResult evaluated = policy.EvaluateCommit(commit.Message);
            if (!evaluated.IsValid)
            {
                throw new ChangeWorkflowException(
                    $"change_commit_policy_invalid:{commit.Oid}:{evaluated.Diagnostics[0]}");
            }
        }

        Dictionary<string, string[]> targetOwners = EffectiveOwners(targetCommits, policy);
        Dictionary<string, string[]> rangeOwners = EffectiveOwners(rangeCommits, policy);

        foreach ((string id, string[] owners) in rangeOwners)
        {
            if (owners.Length > 1)
            {
                throw new ChangeWorkflowException($"change_id_range_duplicate:{id}");
            }

            if (targetOwners.ContainsKey(id))
            {
                throw new ChangeWorkflowException($"change_id_target_collision:{id}");
            }

            ValidateFragmentAtRevision(root, head, id, timeout);
        }

        // Also validate any optional fragment files added or modified in target..head even if not linked yet.
        string[] rangeFragments = RunGit(
                root,
                timeout,
                "diff",
                "--name-only",
                "--diff-filter=AM",
                $"{target}..{head}",
                "--",
                "docs/internal/releases/changes")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string relative in rangeFragments)
        {
            string id = Path.GetFileNameWithoutExtension(relative);
            if (!ChangeIdPolicy.IsValid(id) || !relative.EndsWith(".yaml", StringComparison.Ordinal))
            {
                throw new ChangeWorkflowException($"change_fragment_invalid:{relative}");
            }

            if (!rangeOwners.ContainsKey(id))
            {
                ValidateFragmentAtRevision(root, head, id, timeout);
            }
        }

        output.WriteLine($"change_range_verified: target={target} head={head} commits={rangeCommits.Length} change-ids={rangeOwners.Count}");
        return Program.Success;
    }

    /// <summary>Installs shared Git preflight hooks while preserving existing checks and refusing ambiguous overwrites.</summary>
    private static int InstallHooks(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, [TargetOption]);
        string target = ValidateToken(options.Get(TargetOption) ?? DefaultTarget, "change_hook_target_invalid");
        ResolveCommit(root, target, timeout);
        string hooks = RunGit(
            root,
            timeout,
            GitRevisionCommand,
            "--path-format=absolute",
            "--git-path",
            "hooks").Trim();
        Directory.CreateDirectory(hooks);
        string project = Path.Combine(root, "eng", "release", "src", "ISLAMU.ReleaseEngineering", "ISLAMU.ReleaseEngineering.csproj");
        InstallManagedHook(
            Path.Combine(hooks, "pre-commit"),
            project,
            $"preflight-staged {TargetOption} {ShellQuote(target)}");
        InstallManagedHook(
            Path.Combine(hooks, "commit-msg"),
            project,
            $"preflight-commit \"$1\" {TargetOption} {ShellQuote(target)}");
        output.WriteLine($"change_hooks_installed: target={target} hooks={Relative(root, hooks)}");
        return Program.Success;
    }

    private static Dictionary<string, string[]> EffectiveOwners(
        IEnumerable<ReleaseCommit> commits,
        ReleasePolicy policy)
    {
        return commits
            .Select(commit => new
            {
                Commit = commit,
                Result = policy.EvaluateCommit(commit.Message),
            })
            .Where(item => item.Result.IsValid && item.Result.ChangeId is not null)
            .GroupBy(item => item.Result.ChangeId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Commit.Oid).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static string AllocateUnused(string root, string target, TimeSpan timeout)
    {
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
        HashSet<string> used = CollectBoundedCommittedIds(root, target, policy, timeout);
        string fragments = Path.Combine(root, "docs", "internal", "releases", "changes");
        if (Directory.Exists(fragments))
        {
            foreach (string file in Directory.EnumerateFiles(fragments, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (ChangeIdPolicy.IsValid(id))
                {
                    used.Add(id);
                }
            }
        }

        for (int attempt = 0; attempt < 16; attempt++)
        {
            string candidate = ChangeIdPolicy.Create();
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new ChangeWorkflowException("change_id_allocation_exhausted");
    }

    private static HashSet<string> CollectBoundedCommittedIds(
        string root,
        string target,
        ReleasePolicy policy,
        TimeSpan timeout)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (ReleaseCommit commit in ReadCommits(root, target, timeout))
        {
            CommitPolicyResult evaluated = policy.EvaluateCommit(commit.Message);
            if (evaluated.ChangeId is not null)
            {
                result.Add(evaluated.ChangeId);
            }
        }

        if (TryRunGit(root, timeout, out string headOid, GitRevisionCommand, "--verify", "HEAD^{commit}"))
        {
            string targetOid = ResolveCommit(root, target, timeout);
            if (!string.Equals(headOid.Trim(), targetOid, StringComparison.Ordinal))
            {
                foreach (ReleaseCommit commit in ReadCommits(root, $"{target}..HEAD", timeout))
                {
                    CommitPolicyResult evaluated = policy.EvaluateCommit(commit.Message);
                    if (evaluated.ChangeId is not null)
                    {
                        result.Add(evaluated.ChangeId);
                    }
                }
            }
        }

        return result;
    }

    private static void ValidateIndexedFragment(
        string root,
        string id,
        TimeSpan timeout)
    {
        string relative = $"docs/internal/releases/changes/{id}.yaml";
        if (!TryRunGit(root, timeout, out string text, "show", $":{relative}"))
        {
            throw new ChangeWorkflowException(
                $"change_fragment_not_staged_or_mismatched:{id}");
        }

        ValidateFragmentContent(text, id);
    }

    private static void ValidateFragmentAtRevision(
        string root,
        string revision,
        string id,
        TimeSpan timeout)
    {
        string relative = $"docs/internal/releases/changes/{id}.yaml";
        if (!TryRunGit(
                root,
                timeout,
                out string text,
                "show",
                $"{revision}:{relative}"))
        {
            throw new ChangeWorkflowException(
                $"change_fragment_missing_or_mismatched:{id}");
        }

        ValidateFragmentContent(text, id);
    }

    private static void ValidateFragmentContent(string fragmentYaml, string expectedId)
    {
        ReleaseInputValidationResult validation = ReleaseInputPolicy.ValidateFragments([fragmentYaml]);
        if (!validation.IsValid)
        {
            string prioritized = validation.Diagnostics.FirstOrDefault(
                    diagnostic => diagnostic.StartsWith("fragment_embargo_not_public:", StringComparison.Ordinal) ||
                                  diagnostic.StartsWith("fragment_restricted_detail_marker:", StringComparison.Ordinal))
                ?? validation.Diagnostics[0];
            throw new ChangeWorkflowException(prioritized);
        }

        if (validation.Fragments.Count != 1 ||
            !string.Equals(validation.Fragments[0].ChangeId, expectedId, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException($"change_fragment_missing_or_mismatched:{expectedId}");
        }
    }

    private static void EnsureReleaseSourcesClean(string root, TimeSpan timeout)
    {
        const string changesPath = "docs/internal/releases/changes";
        if (!TryRunGit(root, timeout, out _, "diff", "--quiet", "--", changesPath) ||
            !TryRunGit(root, timeout, out _, "diff", "--cached", "--quiet", "--", changesPath))
        {
            throw new ChangeWorkflowException("change_release_sources_not_committed");
        }

        string untracked = RunGit(
            root,
            timeout,
            "ls-files",
            "--others",
            "--exclude-standard",
            "--",
            changesPath);
        if (!string.IsNullOrWhiteSpace(untracked))
        {
            throw new ChangeWorkflowException("change_release_sources_not_committed");
        }
    }

    private static ReleaseCommit[] ReadCommits(
        string root,
        string revision,
        TimeSpan timeout,
        bool reverse = false)
    {
        var arguments = new List<string> { "log" };
        if (reverse)
        {
            arguments.Add("--reverse");
        }

        arguments.Add("--format=%H%x00%B%x1e");
        arguments.Add(revision);
        string raw = RunGit(root, timeout, arguments.ToArray());
        return raw.Split('\u001e', StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.TrimStart('\n'))
            .Where(entry => entry.Length != 0)
            .Select(entry =>
            {
                int separator = entry.IndexOf('\0', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    throw new ChangeWorkflowException("change_git_log_invalid");
                }

                return new ReleaseCommit(
                    entry[..separator],
                    entry[(separator + 1)..].TrimEnd('\n'));
            })
            .ToArray();
    }

    private static string ResolveRepositoryRoot(string repositoryRoot, TimeSpan timeout)
    {
        string root = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(root) || IsLink(root))
        {
            throw new ChangeWorkflowException("change_repository_invalid");
        }

        string observed = RunGit(root, timeout, GitRevisionCommand, "--show-toplevel").Trim();
        if (!string.Equals(observed, root, PathComparison))
        {
            throw new ChangeWorkflowException("change_repository_root_invalid");
        }

        return root;
    }

    private static string ResolveCommit(string root, string revision, TimeSpan timeout)
    {
        if (revision.StartsWith('-'))
        {
            throw new ChangeWorkflowException("change_revision_invalid");
        }

        string oid = RunGit(root, timeout, GitRevisionCommand, "--verify", $"{revision}^{{commit}}").Trim();
        if (!IsFullOid(oid))
        {
            throw new ChangeWorkflowException($"change_revision_missing:{revision}");
        }

        return oid;
    }

    private static string RunGit(string root, TimeSpan timeout, params string[] arguments)
    {
        if (!TryRunGit(root, timeout, out string output, arguments))
        {
            throw new ChangeWorkflowException("change_git_failed");
        }

        return output;
    }

    private static bool TryRunGit(
        string root,
        TimeSpan timeout,
        out string output,
        params string[] arguments)
    {
        output = string.Empty;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        process.StartInfo.Environment["GIT_NO_LAZY_FETCH"] = "1";
        process.StartInfo.ArgumentList.Add("--no-replace-objects");
        // rev-parse cannot execute hooks and must observe the real hooksPath configuration.
        if (arguments[0] != GitRevisionCommand)
        {
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"core.hooksPath={NullDevice}");
        }

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

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
        catch (Exception exception) when (exception is OperationCanceledException or IOException or InvalidOperationException or Win32Exception)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            return false;
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var buffer = new char[MaximumGitOutputCharacters];
        int count = 0;
        while (count < buffer.Length)
        {
            int read = await reader.ReadAsync(
                buffer.AsMemory(count, buffer.Length - count),
                cancellationToken);
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

    /// <summary>
    /// Renders an optional high-impact fragment with all six required impact categories defaulted
    /// to <c>not-applicable</c> and pointing to repository-native <c>docs/internal/</c> anchors.
    /// </summary>
    private static string Fragment(
        string id,
        string title,
        string type,
        string scope,
        string summary,
        string? group)
    {
        string groupLine = group is null ? string.Empty : $"Group: {group}\n";
        return
            $"Change-Id: {id}\n" +
            $"Title: {YamlQuote(title)}\n" +
            $"Type: {type}\n" +
            $"Scope: {scope}\n" +
            $"Summary: {YamlQuote(summary)}\n" +
            groupLine +
            "Supersedes: []\n" +
            "Impacts:\n" +
            "  Breaking:\n" +
            "    Reference: docs/internal/API_CHANGELOG.md\n" +
            "    Disposition: not-applicable\n" +
            "  Security:\n" +
            "    Reference: docs/internal/SECURITY_OVERVIEW.md\n" +
            "    Disposition: not-applicable\n" +
            "  Migration:\n" +
            "    Reference: docs/internal/RELEASE_RUNBOOK.md\n" +
            "    Disposition: not-applicable\n" +
            "  Configuration:\n" +
            "    Reference: docs/internal/CONFIGURATION.md\n" +
            "    Disposition: not-applicable\n" +
            "  OpenAPI:\n" +
            "    Reference: docs/internal/API_CHANGELOG.md\n" +
            "    Disposition: not-applicable\n" +
            "  Operator:\n" +
            "    Reference: docs/internal/RELEASE_CHECKLIST.md\n" +
            "    Disposition: not-applicable\n";
    }

    private static string Hook(
        string project,
        string command,
        string? preservedHook) =>
        "#!/bin/sh\n" +
        HookMarker + "\n" +
        "set -eu\n" +
        (preservedHook is null
            ? string.Empty
            : $"{ShellQuote(preservedHook)} \"$@\"\n") +
        $"exec dotnet run --project {ShellQuote(project)} -- {command}\n";

    private static void InstallManagedHook(
        string path,
        string project,
        string command)
    {
        string backup = path + ".before-islamu-release";
        string? preserved = File.Exists(backup) ? backup : null;
        if (File.Exists(path))
        {
            string existing = File.ReadAllText(path);
            if (!existing.Contains(HookMarker, StringComparison.Ordinal))
            {
                if (preserved is not null)
                {
                    throw new ChangeWorkflowException(
                        $"existing_hook_not_managed:{Path.GetFileName(path)}");
                }

                File.Move(path, backup);
                preserved = backup;
            }
            else
            {
                string managed = Hook(project, command, preserved);
                if (string.Equals(existing, managed, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        string content = Hook(project, command, preserved);
        File.WriteAllText(path, content, StrictUtf8);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute);
        }
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            throw new ChangeWorkflowException($"change_output_exists:{Path.GetFileName(path)}");
        }

        string temporary = Path.Combine(
            Path.GetDirectoryName(path)!,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static Options ParseOptions(
        string[] args,
        int start,
        IReadOnlyCollection<string> allowed)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = start; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length ||
                !allowed.Contains(args[index]) ||
                values.ContainsKey(args[index]))
            {
                throw new ChangeWorkflowException("change_arguments_invalid");
            }

            values[args[index]] = args[index + 1];
        }

        return new Options(values);
    }

    private static string ValidateSingleLine(string value, string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 2_048 ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Any(char.IsControl))
        {
            throw new ChangeWorkflowException(diagnostic);
        }

        return value.Trim();
    }

    private static string ValidateToken(string value, string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            value.Any(character =>
                !char.IsAsciiLetterOrDigit(character) &&
                character is not '-' and not '_' and not '/' and not '.'))
        {
            throw new ChangeWorkflowException(diagnostic);
        }

        return value;
    }

    private static string YamlQuote(string value) =>
        "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static bool IsFullOid(string value) =>
        value.Length is 40 or 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsLink(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static int Reject(TextWriter output, string diagnostic)
    {
        string prefix = diagnostic.StartsWith("existing_hook_", StringComparison.Ordinal)
            ? "change_hooks_failed"
            : "change_preflight_failed";
        output.WriteLine($"{prefix}: {diagnostic}");
        return Program.ToolchainRejected;
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static string NullDevice => OperatingSystem.IsWindows() ? "NUL" : "/dev/null";

    private sealed class Options(IReadOnlyDictionary<string, string> values)
    {
        public string? Get(string name) =>
            values.TryGetValue(name, out string? value) ? value : null;

        public string Require(string name) =>
            Get(name) ?? throw new ChangeWorkflowException($"change_argument_required:{name}");
    }

    private sealed class ChangeWorkflowException(string diagnostic) : Exception
    {
        public string Diagnostic { get; } = diagnostic;
    }
}
