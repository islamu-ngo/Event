// ABOUTME: Provides Change-Id allocation, fragment creation, hook preflight, and commit-bound repair commands.
// ABOUTME: Prevents target collisions before commit or merge while preserving immutable Git provenance.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ISLAMU.ReleaseEngineering;

public static class ChangeWorkflowCommand
{
    private const int MaximumGitOutputCharacters = 1_048_576;
    private const string HookMarker = "# ISLAMU_RELEASE_CHANGE_HOOK";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

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
                "rename-change" => Rename(args, output, root, timeout),
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

    private static int Allocate(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, ["--target"]);
        string target = options.Get("--target") ?? "develop";
        ResolveCommit(root, target, timeout);
        string id = AllocateUnused(root, timeout);
        output.WriteLine($"change_id_allocated: {id}");
        return Program.Success;
    }

    private static int Create(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(
            args,
            1,
            ["--target", "--type", "--scope", "--title", "--summary", "--group"]);
        string target = options.Get("--target") ?? "develop";
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

        string id = AllocateUnused(root, timeout);
        string directory = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "changes");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, id + ".yaml");
        WriteAtomic(path, StrictUtf8.GetBytes(Fragment(id, title, type, scope, summary, group)));
        output.WriteLine($"change_created: id={id} path={Relative(root, path)}");
        output.WriteLine($"commit_footer: Change-Id: {id}");
        return Program.Success;
    }

    private static int PreflightCommit(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException("preflight_commit_message_path_required");
        }

        Options options = ParseOptions(args, 2, ["--target"]);
        string target = options.Get("--target") ?? "develop";
        ResolveCommit(root, target, timeout);
        string messagePath = Path.GetFullPath(args[1]);
        if (!File.Exists(messagePath) || IsLink(messagePath))
        {
            throw new ChangeWorkflowException("preflight_commit_message_invalid");
        }

        string message = StrictUtf8.GetString(File.ReadAllBytes(messagePath));
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

        ChangeIdRenameLoadResult loaded = LoadRenames(root);
        HashSet<string> reachable = ReadCommits(root, "--all", timeout)
            .Select(commit => ChangeIdRenamePolicy.Evaluate(commit, policy, loaded.Renames).ChangeId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        if (reachable.Contains(proposed.ChangeId))
        {
            throw new ChangeWorkflowException($"change_id_already_reachable:{proposed.ChangeId}");
        }

        ValidateIndexedFragment(root, proposed.ChangeId, timeout);
        output.WriteLine($"change_commit_verified: change-id={proposed.ChangeId} target={target}");
        return Program.Success;
    }

    private static int PreflightStaged(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, ["--target"]);
        string target = options.Get("--target") ?? "develop";
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
        ChangeIdRenameLoadResult loaded = LoadRenames(root);
        HashSet<string> reachable = ReadCommits(root, "--all", timeout)
            .Select(commit => ChangeIdRenamePolicy.Evaluate(commit, policy, loaded.Renames).ChangeId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        foreach (string relative in staged)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            string id = Path.GetFileNameWithoutExtension(path);
            if (!ChangeIdPolicy.IsValid(id) || !string.Equals(ReadFragmentId(path), id, StringComparison.Ordinal))
            {
                throw new ChangeWorkflowException($"staged_fragment_invalid:{relative}");
            }

            bool existedAtHead = TryRunGit(root, timeout, out _, "cat-file", "-e", $"HEAD:{relative}");
            if (!existedAtHead && reachable.Contains(id))
            {
                throw new ChangeWorkflowException($"change_id_already_reachable:{id}");
            }
        }

        output.WriteLine($"change_staged_verified: fragments={staged.Length} target={target}");
        return Program.Success;
    }

    private static int PreflightRange(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, ["--target", "--head"]);
        string target = options.Get("--target") ?? "develop";
        string head = options.Get("--head") ?? "HEAD";
        ResolveCommit(root, target, timeout);
        ResolveCommit(root, head, timeout);
        EnsureReleaseSourcesClean(root, timeout);
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
        ChangeIdRenameLoadResult loaded = LoadRenames(root);

        ReleaseCommit[] targetCommits = ReadCommits(root, target, timeout);
        ReleaseCommit[] rangeCommits = ReadCommits(root, $"{target}..{head}", timeout, reverse: true);
        foreach (ReleaseCommit commit in rangeCommits)
        {
            CommitPolicyResult evaluated = ChangeIdRenamePolicy.Evaluate(
                commit,
                policy,
                loaded.Renames);
            if (!evaluated.IsValid)
            {
                throw new ChangeWorkflowException(
                    $"change_commit_policy_invalid:{commit.Oid}:{evaluated.Diagnostics[0]}");
            }
        }

        Dictionary<string, string[]> targetOwners = EffectiveOwners(targetCommits, policy, loaded.Renames);
        Dictionary<string, string[]> rangeOwners = EffectiveOwners(rangeCommits, policy, loaded.Renames);

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

        output.WriteLine($"change_range_verified: target={target} head={head} commits={rangeCommits.Length} change-ids={rangeOwners.Count}");
        return Program.Success;
    }

    private static int Rename(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(
            args,
            1,
            ["--commit", "--from", "--to", "--reason"]);
        string commitOid = options.Require("--commit");
        string oldId = options.Require("--from");
        string newId = options.Get("--to") ?? AllocateUnused(root, timeout);
        string reason = ValidateSingleLine(options.Require("--reason"), "change_rename_reason_invalid");
        if (!IsFullOid(commitOid) || !ChangeIdPolicy.IsValid(oldId) || !ChangeIdPolicy.IsGenerated(newId) ||
            string.Equals(oldId, newId, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException("change_rename_arguments_invalid");
        }

        string observedCommit = ResolveCommit(root, commitOid, timeout);
        if (!string.Equals(observedCommit, commitOid, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException("change_rename_commit_mismatch");
        }

        string message = RunGit(root, timeout, "show", "-s", "--format=%B", commitOid).TrimEnd('\n');
        CommitPolicyResult commit = ReleasePolicy.LoadFromRepositoryRoot(root).EvaluateCommit(message);
        if (!commit.IsValid || !string.Equals(commit.ChangeId, oldId, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException("change_rename_old_footer_mismatch");
        }

        HashSet<string> used = CollectCommittedIds(root, timeout);
        if (used.Contains(newId))
        {
            throw new ChangeWorkflowException($"change_rename_target_used:{newId}");
        }

        string fragments = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "changes");
        string oldFragment = Path.Combine(fragments, oldId + ".yaml");
        string newFragment = Path.Combine(fragments, newId + ".yaml");
        if (!File.Exists(newFragment))
        {
            if (!File.Exists(oldFragment))
            {
                throw new ChangeWorkflowException("change_rename_fragment_missing");
            }

            string source = StrictUtf8.GetString(File.ReadAllBytes(oldFragment));
            string replaced = source.Replace(
                $"Change-Id: {oldId}\n",
                $"Change-Id: {newId}\n",
                StringComparison.Ordinal);
            if (string.Equals(source, replaced, StringComparison.Ordinal))
            {
                throw new ChangeWorkflowException("change_rename_fragment_invalid");
            }

            WriteAtomic(newFragment, StrictUtf8.GetBytes(replaced));
        }
        else
        {
            ValidateFragment(root, newId);
        }

        var rename = new ChangeIdRename(commitOid, oldId, newId, reason);
        string directory = Path.Join(Path.GetFullPath(root), "docs", "internal", "releases", "change-id-renames");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, commitOid + ".yaml");
        WriteAtomic(path, StrictUtf8.GetBytes(ChangeIdRenamePolicy.Serialize(rename)));
        output.WriteLine($"change_renamed: commit={commitOid} from={oldId} to={newId} record={Relative(root, path)}");
        return Program.Success;
    }

    private static int InstallHooks(string[] args, TextWriter output, string root, TimeSpan timeout)
    {
        Options options = ParseOptions(args, 1, ["--target"]);
        string target = ValidateToken(options.Get("--target") ?? "develop", "change_hook_target_invalid");
        ResolveCommit(root, target, timeout);
        string gitDirectory = RunGit(
            root,
            timeout,
            "rev-parse",
            "--path-format=absolute",
            "--git-common-dir").Trim();
        string hooks = Path.Combine(gitDirectory, "hooks");
        Directory.CreateDirectory(hooks);
        string project = Path.Combine(root, "eng", "release", "src", "ISLAMU.ReleaseEngineering", "ISLAMU.ReleaseEngineering.csproj");
        InstallManagedHook(
            Path.Combine(hooks, "pre-commit"),
            project,
            $"preflight-staged --target {ShellQuote(target)}");
        InstallManagedHook(
            Path.Combine(hooks, "commit-msg"),
            project,
            $"preflight-commit \"$1\" --target {ShellQuote(target)}");
        output.WriteLine($"change_hooks_installed: target={target} hooks={Relative(root, hooks)}");
        return Program.Success;
    }

    private static Dictionary<string, string[]> EffectiveOwners(
        IEnumerable<ReleaseCommit> commits,
        ReleasePolicy policy,
        IReadOnlyList<ChangeIdRename> renames)
    {
        return commits
            .Select(commit => new
            {
                Commit = commit,
                Result = ChangeIdRenamePolicy.Evaluate(commit, policy, renames),
            })
            .Where(item => item.Result.IsValid && item.Result.ChangeId is not null)
            .GroupBy(item => item.Result.ChangeId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Commit.Oid).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
    }

    private static ChangeIdRenameLoadResult LoadRenames(string root)
    {
        ChangeIdRenameLoadResult loaded = ChangeIdRenamePolicy.Load(root);
        if (!loaded.IsValid)
        {
            throw new ChangeWorkflowException(loaded.Diagnostics[0]);
        }

        return loaded;
    }

    private static string AllocateUnused(string root, TimeSpan timeout)
    {
        HashSet<string> used = CollectUsedIds(root, timeout);
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

    private static HashSet<string> CollectUsedIds(string root, TimeSpan timeout)
    {
        HashSet<string> result = CollectCommittedIds(root, timeout);

        string fragments = Path.Combine(root, "docs", "internal", "releases", "changes");
        if (Directory.Exists(fragments))
        {
            foreach (string file in Directory.EnumerateFiles(fragments, "*.yaml", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (ChangeIdPolicy.IsValid(id))
                {
                    result.Add(id);
                }
            }
        }

        ChangeIdRenameLoadResult loaded = ChangeIdRenamePolicy.Load(root);
        if (!loaded.IsValid)
        {
            throw new ChangeWorkflowException(loaded.Diagnostics[0]);
        }

        foreach (ChangeIdRename rename in loaded.Renames)
        {
            result.Add(rename.OldChangeId);
            result.Add(rename.NewChangeId);
        }

        return result;
    }

    private static HashSet<string> CollectCommittedIds(string root, TimeSpan timeout)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        ReleasePolicy policy = ReleasePolicy.LoadFromRepositoryRoot(root);
        foreach (ReleaseCommit commit in ReadCommits(root, "--all", timeout))
        {
            CommitPolicyResult evaluated = policy.EvaluateCommit(commit.Message);
            if (evaluated.ChangeId is not null)
            {
                result.Add(evaluated.ChangeId);
            }
        }

        ChangeIdRenameLoadResult loaded = ChangeIdRenamePolicy.Load(root);
        if (!loaded.IsValid)
        {
            throw new ChangeWorkflowException(loaded.Diagnostics[0]);
        }

        foreach (ChangeIdRename rename in loaded.Renames)
        {
            result.Add(rename.NewChangeId);
        }

        return result;
    }

    private static void ValidateFragment(string root, string id)
    {
        string path = Path.Combine(root, "docs", "internal", "releases", "changes", id + ".yaml");
        if (!File.Exists(path) || IsLink(path) || !string.Equals(ReadFragmentId(path), id, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException($"change_fragment_missing_or_mismatched:{id}");
        }
    }

    private static void ValidateIndexedFragment(
        string root,
        string id,
        TimeSpan timeout)
    {
        string relative = $"docs/internal/releases/changes/{id}.yaml";
        if (!TryRunGit(root, timeout, out string text, "show", $":{relative}") ||
            !string.Equals(ParseFragmentId(text), id, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException(
                $"change_fragment_not_staged_or_mismatched:{id}");
        }
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
                $"{revision}:{relative}") ||
            !string.Equals(ParseFragmentId(text), id, StringComparison.Ordinal))
        {
            throw new ChangeWorkflowException(
                $"change_fragment_missing_or_mismatched:{id}");
        }
    }

    private static void EnsureReleaseSourcesClean(string root, TimeSpan timeout)
    {
        string[] paths =
        [
            "docs/internal/releases/changes",
            "docs/internal/releases/change-id-renames",
        ];
        if (!TryRunGit(
                root,
                timeout,
                out _,
                "diff",
                "--quiet",
                "--",
                paths[0],
                paths[1]) ||
            !TryRunGit(
                root,
                timeout,
                out _,
                "diff",
                "--cached",
                "--quiet",
                "--",
                paths[0],
                paths[1]))
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
            paths[0],
            paths[1]);
        if (!string.IsNullOrWhiteSpace(untracked))
        {
            throw new ChangeWorkflowException("change_release_sources_not_committed");
        }
    }

    private static string ReadFragmentId(string path)
    {
        return ParseFragmentId(StrictUtf8.GetString(File.ReadAllBytes(path)));
    }

    private static string ParseFragmentId(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("Change-Id:", StringComparison.Ordinal))
            {
                return line["Change-Id:".Length..].Trim();
            }
        }

        return string.Empty;
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
                int separator = entry.IndexOf('\0');
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

        string observed = RunGit(root, timeout, "rev-parse", "--show-toplevel").Trim();
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

        string oid = RunGit(root, timeout, "rev-parse", "--verify", $"{revision}^{{commit}}").Trim();
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
        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add($"core.hooksPath={NullDevice}");
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
            "# ABOUTME: Public change fragment generated with its collision-resistant commit footer.\n" +
            "# ABOUTME: Records release impact defaults for review before the owning commit is shared.\n" +
            $"Change-Id: {id}\n" +
            $"Title: {YamlQuote(title)}\n" +
            $"Type: {type}\n" +
            $"Scope: {scope}\n" +
            $"Summary: {YamlQuote(summary)}\n" +
            groupLine +
            "Supersedes: []\n" +
            "Impacts:\n" +
            "  Breaking:\n" +
            "    Reference: docs/API_CHANGELOG.md\n" +
            "    Disposition: not-applicable\n" +
            "  Security:\n" +
            "    Reference: docs/SECURITY_OVERVIEW.md\n" +
            "    Disposition: not-applicable\n" +
            "  Migration:\n" +
            "    Reference: docs/RELEASE_RUNBOOK.md\n" +
            "    Disposition: not-applicable\n" +
            "  Configuration:\n" +
            "    Reference: docs/CONFIGURATION.md\n" +
            "    Disposition: not-applicable\n" +
            "  OpenAPI:\n" +
            "    Reference: docs/API_CHANGELOG.md\n" +
            "    Disposition: not-applicable\n" +
            "  Operator:\n" +
            "    Reference: docs/RELEASE_CHECKLIST.md\n" +
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
            value.Contains('\r') ||
            value.Contains('\n') ||
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
