// ABOUTME: Loads exact-commit Change-Id corrections without mutating immutable Git history.
// ABOUTME: Applies a replacement only when the bound commit still carries the recorded old footer.

using System.Text;

namespace ISLAMU.ReleaseEngineering;

public sealed record ChangeIdRename(
    string CommitOid,
    string OldChangeId,
    string NewChangeId,
    string Reason);

public sealed record ChangeIdRenameLoadResult(
    bool IsValid,
    IReadOnlyList<ChangeIdRename> Renames,
    IReadOnlyList<string> CanonicalDocuments,
    IReadOnlyList<string> Diagnostics);

public static class ChangeIdRenamePolicy
{
    private const int MaximumRenameFiles = 1_024;
    private const int MaximumRenameBytes = 16_384;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ChangeIdRenameLoadResult Load(string repositoryRoot)
    {
        string directory = Path.Join(Path.GetFullPath(repositoryRoot), "docs", "internal", "releases", "change-id-renames");
        if (!Directory.Exists(directory))
        {
            return new ChangeIdRenameLoadResult(true, [], [], []);
        }

        string[] files = Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Take(MaximumRenameFiles + 1)
            .ToArray();
        if (files.Length > MaximumRenameFiles)
        {
            return Invalid("change_id_rename_collection_too_large");
        }

        var renames = new List<ChangeIdRename>(files.Length);
        var documents = new List<string>(files.Length);
        var diagnostics = new List<string>();
        foreach (string file in files)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                if (bytes.Length > MaximumRenameBytes)
                {
                    diagnostics.Add($"change_id_rename_too_large:{Path.GetFileName(file)}");
                    continue;
                }

                string text = StrictUtf8.GetString(bytes);
                CanonicalArtifactResult canonical = CanonicalArtifactPolicy.CanonicalizeText(text);
                if (!canonical.IsValid || canonical.Bytes is null || !bytes.AsSpan().SequenceEqual(canonical.Bytes))
                {
                    diagnostics.Add($"change_id_rename_not_canonical:{Path.GetFileName(file)}");
                    continue;
                }

                Dictionary<string, string> fields = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => !line.StartsWith('#'))
                    .Select(line => line.Split(':', 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.Ordinal);
                string[] expected = ["Schema-Version", "Commit-Oid", "Old-Change-Id", "New-Change-Id", "Reason"];
                if (fields.Count != expected.Length || expected.Any(key => !fields.ContainsKey(key)) ||
                    !string.Equals(fields["Schema-Version"], "change-id-rename.v1", StringComparison.Ordinal) ||
                    !IsFullOid(fields["Commit-Oid"]) ||
                    !string.Equals(Path.GetFileNameWithoutExtension(file), fields["Commit-Oid"], StringComparison.Ordinal) ||
                    !ChangeIdPolicy.IsValid(fields["Old-Change-Id"]) ||
                    !ChangeIdPolicy.IsGenerated(fields["New-Change-Id"]) ||
                    string.Equals(fields["Old-Change-Id"], fields["New-Change-Id"], StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(fields["Reason"]))
                {
                    diagnostics.Add($"change_id_rename_invalid:{Path.GetFileName(file)}");
                    continue;
                }

                renames.Add(new ChangeIdRename(
                    fields["Commit-Oid"],
                    fields["Old-Change-Id"],
                    fields["New-Change-Id"],
                    fields["Reason"]));
                documents.Add(text);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException or ArgumentException)
            {
                diagnostics.Add($"change_id_rename_invalid:{Path.GetFileName(file)}");
            }
        }

        foreach (IGrouping<string, ChangeIdRename> duplicate in renames
            .GroupBy(rename => rename.CommitOid, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add($"change_id_rename_duplicate_commit:{duplicate.Key}");
        }

        foreach (IGrouping<string, ChangeIdRename> duplicate in renames
            .GroupBy(rename => rename.NewChangeId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add($"change_id_rename_duplicate_target:{duplicate.Key}");
        }

        return new ChangeIdRenameLoadResult(
            diagnostics.Count == 0,
            renames,
            documents,
            diagnostics.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
    }

    public static CommitPolicyResult Evaluate(
        ReleaseCommit commit,
        ReleasePolicy policy,
        IEnumerable<ChangeIdRename>? renames,
        ICollection<string>? diagnostics = null)
    {
        CommitPolicyResult result = policy.EvaluateCommit(commit.Message);
        ChangeIdRename[] matches = (renames ?? [])
            .Where(rename => string.Equals(rename.CommitOid, commit.Oid, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length == 0)
        {
            return result;
        }

        if (matches.Length != 1 ||
            !result.IsValid ||
            !string.Equals(result.ChangeId, matches[0].OldChangeId, StringComparison.Ordinal))
        {
            diagnostics?.Add($"context_change_id_rename_mismatch:{commit.Oid}");
            return result;
        }

        return result with { ChangeId = matches[0].NewChangeId };
    }

    public static string Serialize(ChangeIdRename rename) =>
        "# ABOUTME: Commit-bound correction for a colliding immutable Change-Id footer.\n" +
        "# ABOUTME: Maps only the exact recorded Git object to its replacement public fragment.\n" +
        "Schema-Version: change-id-rename.v1\n" +
        $"Commit-Oid: {rename.CommitOid}\n" +
        $"Old-Change-Id: {rename.OldChangeId}\n" +
        $"New-Change-Id: {rename.NewChangeId}\n" +
        $"Reason: {rename.Reason}\n";

    private static bool IsFullOid(string value) =>
        value.Length is 40 or 64 &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static ChangeIdRenameLoadResult Invalid(string diagnostic) =>
        new(false, [], [], [diagnostic]);
}
