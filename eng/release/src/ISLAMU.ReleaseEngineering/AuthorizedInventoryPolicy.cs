using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record AuthorizedSourceDocument(string Path, string Kind);

public sealed record AuthorizedInventoryEntry(
    string Version, string Line, string ReleaseDate, string TagObjectId, string TargetOid,
    string SourceDirectory, string EvidencePath, string EvidenceSha256,
    bool DisclosureAuthorized, string AuthorizationEvidenceSha256,
    IReadOnlyList<AuthorizedSourceDocument> SourceDocuments);

public sealed record AcceptedReleaseIdentity(string Version, string TagObjectId, string EvidenceSha256);

public sealed record AuthorizedRelease(
    string Version, DateOnly ReleaseDate, string Line, string TagObjectId, string TargetOid,
    string EvidencePath, string SummarySha256, string ContextSha256, string EvidenceSha256,
    string NotesSha256, string Summary, ReleaseContext Context, ReleaseDescriptor Descriptor,
    IReadOnlyList<PublicChangeFragment> Fragments);

public sealed record AuthorizedInventoryResult(
    bool IsValid, IReadOnlyList<AuthorizedRelease> Releases, IReadOnlyList<string> Diagnostics);

internal static class PublicationInventoryVerificationBudget
{
    public const int MaximumEntries = 1_024;
    public static readonly TimeSpan PerEntryTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan OverallTimeout = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PromotedProcessTimeout = TimeSpan.FromMinutes(6);
}

/// <summary>
/// The final-lane adapter must verify an authorized final-lane SSH signature over the complete
/// original inventory UTF-8 bytes. It must require ordinal equality between inventorySha256 and
/// lowercase SHA-256 of those independently authenticated bytes, without parsing/reserializing,
/// trimming, or normalizing them. The signed bytes attest the complete retained all-line set and
/// final verification and disclosure approval for every entry's pinned tag, target and evidence.
/// In particular, disclosureAuthorized and authorizationEvidenceSha256 are covered by that signature;
/// a valid signature over only tags, final evidence, or an earlier subset does not satisfy this contract.
/// The detached signature is outside the inventory. AuthorizationEvidenceSha256 refers to the
/// upstream disclosure-approval record, never this inventory or its signature, so there is no circular
/// hash. This consumer relies on the authenticated whole-inventory disclosure attestation; it does not
/// independently retrieve that approval record. The adapter must reject missing final-lane authority.
/// Computing a digest of publisher-supplied input, or trusting its producer/completeSet booleans,
/// does not implement this contract. No production authority is inferred from release-evidence.v1:
/// that format attests tag verification, not completeness or permission to disclose.
/// </summary>
public interface IFinalLaneInventoryAuthority
{
    bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries, CancellationToken cancellationToken = default);
}

public static class AuthorizedInventoryPolicy
{
    private const int MaximumBytes = 1_048_576;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly Regex Oid = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Digest = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    /// <summary>
    /// Reads presentation inputs from pinned commit B, never from checkout directories or line
    /// branches. Retained evidence paths are relative to a separately supplied artifact root.
    /// Accepted history must come from the protected acceptance record, not the proposal.
    /// Null authority always fails closed; callers must supply an independently authenticated
    /// final-lane adapter before production publication can proceed.
    /// </summary>
    public static AuthorizedInventoryResult Verify(
        string repositoryRoot, string inventoryJson, string retainedEvidenceRoot,
        IFinalLaneInventoryAuthority? authority, IReadOnlyList<AcceptedReleaseIdentity> acceptedHistory,
        TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero) return Invalid("inventory_request_invalid");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        cancellationToken = deadline.Token;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] inventoryBytes = Utf8.GetBytes(inventoryJson);
            if (inventoryBytes.Length > MaximumBytes) return Invalid("inventory_too_large");
            if (timeout <= TimeSpan.Zero || acceptedHistory is null ||
                acceptedHistory.Count > PublicationInventoryVerificationBudget.MaximumEntries)
                return Invalid("inventory_request_invalid");
            ArtifactPolicyResult normalized = ReleaseArtifactPolicy.NormalizeJson(inventoryJson);
            if (!normalized.IsValid) return Invalid("inventory_json_invalid");
            Inventory? inventory = JsonSerializer.Deserialize<Inventory>(inventoryJson, JsonOptions);
            if (inventory is null || inventory.SchemaVersion != "authorized-inventory.v1" ||
                inventory.Producer != "final-lane" || !inventory.CompleteSet || inventory.Entries is null)
                return Invalid("inventory_incomplete");
            AuthorizedInventoryEntry[] entries = inventory.Entries;
            if (entries.Length == 0) return Invalid("inventory_empty");
            if (entries.Length > PublicationInventoryVerificationBudget.MaximumEntries)
                return Invalid("inventory_too_many_entries");
            var versions = new HashSet<string>(StringComparer.Ordinal);
            foreach (AuthorizedInventoryEntry entry in entries)
            {
                if (entry is null || string.IsNullOrEmpty(entry.Version)) return Invalid("inventory_entry_invalid");
                if (!versions.Add(entry.Version)) return Invalid("inventory_duplicate_version");
                if (!IsOid(entry.TagObjectId) || !IsOid(entry.TargetOid) || entry.TargetOid.Length != entry.TagObjectId.Length)
                    return Invalid("inventory_object_id_invalid");
                if (!entry.DisclosureAuthorized || !IsDigest(entry.AuthorizationEvidenceSha256))
                    return Invalid("inventory_disclosure_missing");
                if (!IsDigest(entry.EvidenceSha256)) return Invalid("inventory_evidence_hash_invalid");
                if (entry.SourceDocuments is null ||
                    entry.SourceDocuments.Count > PublicationInventoryVerificationBudget.MaximumEntries)
                    return Invalid("inventory_source_documents_invalid");
            }
            foreach (AcceptedReleaseIdentity accepted in acceptedHistory)
            {
                AuthorizedInventoryEntry? entry = entries.SingleOrDefault(item => item.Version == accepted.Version);
                if (entry is null) return Invalid("inventory_accepted_history_omitted");
                if (entry.TagObjectId != accepted.TagObjectId || entry.EvidenceSha256 != accepted.EvidenceSha256)
                    return Invalid("inventory_accepted_history_replaced");
            }
            if (authority is null) return Invalid("inventory_final_lane_authority_missing");
            if (!authority.VerifyCompleteInventory(Sha256(inventoryBytes), entries, cancellationToken))
                return Invalid("inventory_final_lane_authority_invalid");
            cancellationToken.ThrowIfCancellationRequested();

            string repository = Path.GetFullPath(repositoryRoot);
            string evidenceRoot = Path.GetFullPath(retainedEvidenceRoot);
            var releases = new List<AuthorizedRelease>();
            foreach (AuthorizedInventoryEntry entry in entries)
            {
                using var entryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                entryDeadline.CancelAfter(PublicationInventoryVerificationBudget.PerEntryTimeout);
                CancellationToken entryCancellationToken = entryDeadline.Token;
                string tagRef = $"refs/tags/v{entry.Version}";
                if (GitText(repository, entryCancellationToken, "cat-file", "-t", entry.TagObjectId).Trim() != "tag")
                    return Invalid("inventory_tag_not_annotated");
                if (GitText(repository, entryCancellationToken, "rev-parse", "--verify", tagRef).Trim() != entry.TagObjectId)
                    return Invalid("inventory_tag_ref_mismatch");
                string tag = GitText(repository, entryCancellationToken, "cat-file", "-p", entry.TagObjectId);
                string header = tag.Split("\n\n", 2, StringSplitOptions.None)[0];
                if (!header.StartsWith($"object {entry.TargetOid}\ntype commit\n", StringComparison.Ordinal) ||
                    !header.Split('\n').Contains($"tag v{entry.Version}", StringComparer.Ordinal) ||
                    GitText(repository, entryCancellationToken, "rev-parse", "--verify", $"{entry.TagObjectId}^{{commit}}").Trim() != entry.TargetOid)
                    return Invalid("inventory_tag_target_mismatch");

                string evidencePath = ResolveEvidencePath(evidenceRoot, entry.EvidencePath);
                if (!File.Exists(evidencePath)) return Invalid("inventory_evidence_missing");
                using var stream = new FileStream(evidencePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > MaximumBytes) return Invalid("inventory_evidence_too_large");
                byte[] evidenceBytes = ReadBounded(stream, entryCancellationToken).GetAwaiter().GetResult();
                if (Sha256(evidenceBytes) != entry.EvidenceSha256) return Invalid("inventory_evidence_hash_mismatch");
                ArtifactPolicyResult evidenceNormalized = ReleaseArtifactPolicy.NormalizeJson(Utf8.GetString(evidenceBytes));
                if (!evidenceNormalized.IsValid) return Invalid("inventory_evidence_invalid");
                using JsonDocument evidence = JsonDocument.Parse(evidenceBytes);
                JsonElement fields = evidence.RootElement;
                if (Text(fields, "schemaVersion") != "release-evidence.v1" ||
                    Text(fields, "version") != entry.Version || Text(fields, "line") != entry.Line ||
                    Text(fields, "releaseDate") != entry.ReleaseDate || Text(fields, "tagName") != $"v{entry.Version}" ||
                    Text(fields, "tagObjectId") != entry.TagObjectId || Text(fields, "targetOid") != entry.TargetOid ||
                    Text(fields, "candidateOid") != entry.TargetOid)
                    return Invalid("inventory_evidence_identity_mismatch");

                ValidateRelativePath(entry.SourceDirectory);
                byte[] descriptorBytes = ReadInput("release.yaml", "releaseDescriptorSha256", normalize: true);
                byte[] summaryBytes = ReadInput("summary.md", "releaseSummarySha256");
                byte[] contextBytes = ReadInput("release-context.v1.json", "releaseContextSha256");
                _ = ReadInput("release-notes.md", "releaseNotesSha256");
                ArtifactPolicyResult contextNormalized = ReleaseArtifactPolicy.NormalizeJson(Utf8.GetString(contextBytes));
                if (!contextNormalized.IsValid)
                    return Invalid("inventory_context_invalid");
                ReleaseContext? context = JsonSerializer.Deserialize<ReleaseContext>(contextBytes, JsonOptions);
                if (context is null || context.SchemaVersion != 1 || context.Release is null ||
                    context.Changes is null || context.Evidence is null || context.Evidence.Objects is null ||
                    context.Changes.Count > PublicationInventoryVerificationBudget.MaximumEntries ||
                    context.Release.Version != entry.Version || context.Release.Line != entry.Line ||
                    context.Release.ReleaseDate != entry.ReleaseDate)
                    return Invalid("inventory_context_invalid");

                var fragments = new List<string>();
                var sourceTexts = new List<string>();
                var sourcePaths = new HashSet<string>(StringComparer.Ordinal);
                foreach (AuthorizedSourceDocument source in entry.SourceDocuments)
                {
                    if (source is null || (source.Kind != "fragment" && source.Kind != "change-id-rename") || !sourcePaths.Add(source.Path))
                        return Invalid("inventory_source_documents_invalid");
                    string sourceText = Utf8.GetString(CommittedBlob(repository, entry.TargetOid, source.Path, entryCancellationToken));
                    ArtifactPolicyResult sourceNormalized = ReleaseArtifactPolicy.NormalizeText(sourceText);
                    if (!sourceNormalized.IsValid || sourceNormalized.Bytes is null) return Invalid("inventory_source_documents_invalid");
                    string normalizedText = Utf8.GetString(sourceNormalized.Bytes);
                    sourceTexts.Add(normalizedText);
                    if (source.Kind == "fragment") fragments.Add(normalizedText);
                }
                if (Sha256(Utf8.GetBytes(string.Join("\n---\n", sourceTexts))) != Text(fields, "releaseFragmentsSha256"))
                    return Invalid("inventory_fragment_hash_mismatch");
                // Match candidate verification's release-local fragment set. Prior snapshots enforce
                // immutability of the same IDs; they do not supply historical reference resolution.
                ReleaseInputValidationResult input = ReleaseInputPolicy.Validate(Utf8.GetString(descriptorBytes), fragments, []);
                if (!input.IsValid || input.Descriptor is null) return Invalid("inventory_descriptor_invalid");
                ReleaseDescriptor descriptor = input.Descriptor;
                if (descriptor.Version != entry.Version || descriptor.Line != entry.Line ||
                    descriptor.ReleaseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != entry.ReleaseDate ||
                    descriptor.BaseStableTag != context.Release.BaseStableTag || descriptor.PreviousPublishedTag != context.Release.PreviousPublishedTag ||
                    descriptor.ReleaseRange.BaseOid != context.Evidence.BaseStableOid ||
                    descriptor.ReleaseRange.PreviousOid != context.Evidence.PreviousPublishedOid)
                    return Invalid("inventory_presentation_identity_mismatch");
                string[] linkedIds = context.Changes.Select(change => change.ChangeId).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                if (!linkedIds.SequenceEqual(input.Fragments.Select(fragment => fragment.ChangeId).Order(StringComparer.Ordinal), StringComparer.Ordinal))
                    return Invalid("inventory_fragment_set_mismatch");
                foreach (ReleaseContextChange change in context.Changes)
                {
                    if (!IsOid(change.Oid) || string.IsNullOrEmpty(change.DisplayId) ||
                        !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(change.Title).IsValid ||
                        !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(change.Summary).IsValid ||
                        !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(change.Scope).IsValid)
                        return Invalid("inventory_context_invalid");
                }
                string summary = Utf8.GetString(summaryBytes);
                if (summary.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                    .Any(line => !ReleaseArtifactPolicy.EscapeUntrustedMarkdown(line).IsValid))
                    return Invalid("inventory_summary_invalid");
                releases.Add(new AuthorizedRelease(entry.Version, descriptor.ReleaseDate, entry.Line,
                    entry.TagObjectId, entry.TargetOid, entry.EvidencePath,
                    Text(fields, "releaseSummarySha256"), Text(fields, "releaseContextSha256"), entry.EvidenceSha256,
                    Text(fields, "releaseNotesSha256"), summary, context, descriptor, input.Fragments));

                byte[] ReadInput(string name, string hashField, bool normalize = false)
                {
                    byte[] bytes = CommittedBlob(repository, entry.TargetOid, $"{entry.SourceDirectory}/{name}", entryCancellationToken);
                    if (normalize)
                    {
                        ArtifactPolicyResult text = ReleaseArtifactPolicy.NormalizeText(Utf8.GetString(bytes));
                        if (!text.IsValid || text.Bytes is null) throw new InventoryException("inventory_committed_input_invalid");
                        bytes = text.Bytes;
                    }
                    if (!IsDigest(Text(fields, hashField)) || Sha256(bytes) != Text(fields, hashField))
                        throw new InventoryException("inventory_committed_input_hash_mismatch");
                    return bytes;
                }
            }
            // Ref identity can change while artifacts are read; do not return a partial or stale set.
            foreach (AuthorizedInventoryEntry entry in entries)
            {
                using var entryDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                entryDeadline.CancelAfter(PublicationInventoryVerificationBudget.PerEntryTimeout);
                if (GitText(repository, entryDeadline.Token, "rev-parse", "--verify", $"refs/tags/v{entry.Version}").Trim() != entry.TagObjectId)
                    return Invalid("inventory_tag_ref_mismatch");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new AuthorizedInventoryResult(true, releases.OrderBy(item => item.Version, StringComparer.Ordinal).ToArray(), []);
        }
        catch (InventoryException exception) { return Invalid(exception.Message); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or
            JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OperationCanceledException)
        {
            return Invalid("inventory_input_invalid");
        }
    }

    private static byte[] CommittedBlob(string repository, string oid, string path, CancellationToken cancellationToken)
    {
        ValidateRelativePath(path);
        string tree = GitText(repository, cancellationToken, "ls-tree", oid, "--", path);
        if (!tree.StartsWith("100644 blob ", StringComparison.Ordinal) && !tree.StartsWith("100755 blob ", StringComparison.Ordinal))
            throw new InventoryException("inventory_committed_input_invalid");
        return GitBytes(repository, cancellationToken, "cat-file", "blob", $"{oid}:{path}");
    }

    private static string GitText(string repository, CancellationToken cancellationToken, params string[] args) => Utf8.GetString(GitBytes(repository, cancellationToken, args));

    private static byte[] GitBytes(string repository, CancellationToken cancellationToken, params string[] args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(ReleaseToolPaths.Git)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string arg in args) info.ArgumentList.Add(arg);
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        info.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        using Process process = Process.Start(info) ?? throw new InventoryException("inventory_git_failed");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<byte[]> output = ReadOutput(process.StandardOutput.BaseStream);
        Task<byte[]> error = ReadOutput(process.StandardError.BaseStream);
        try
        {
            Task.WhenAll(process.WaitForExitAsync(cancellation.Token), output, error).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new InventoryException("inventory_git_failed");
            return output.Result;
        }
        finally
        {
            cancellation.Cancel();
            if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(); }
            try { Task.WhenAll(output, error).GetAwaiter().GetResult(); }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or InventoryException)
            {
                Trace.TraceWarning("inventory_process_cleanup_interrupted:{0}", exception.GetType().Name);
            }
        }

        async Task<byte[]> ReadOutput(Stream stream)
        {
            try { return await ReadBounded(stream, cancellation.Token); }
            catch { cancellation.Cancel(); throw; }
        }
    }

    private static async Task<byte[]> ReadBounded(Stream stream, CancellationToken cancellation)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[8_192];
        int count;
        while ((count = await stream.ReadAsync(buffer, cancellation)) != 0)
        {
            if (output.Length + count > MaximumBytes) throw new InventoryException("inventory_artifact_too_large");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private static string ResolveEvidencePath(string root, string relative)
    {
        ValidateRelativePath(relative);
        string full = Path.GetFullPath(relative, root);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InventoryException("inventory_path_invalid");
        }
        return full;
    }

    private static void ValidateRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 4_096 || Path.IsPathRooted(path) ||
            path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal) || path.Any(char.IsControl) ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InventoryException("inventory_path_invalid");
    }

    private static bool IsOid(string? value) => value is not null && Oid.IsMatch(value);
    private static bool IsDigest(string? value) => value is not null && Digest.IsMatch(value);
    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new InventoryException("inventory_evidence_invalid");
    private static AuthorizedInventoryResult Invalid(string diagnostic) => new(false, [], [diagnostic]);
    private sealed record Inventory(string SchemaVersion, string Producer, bool CompleteSet, AuthorizedInventoryEntry[] Entries);
    private sealed class InventoryException(string diagnostic) : Exception(diagnostic);
}
