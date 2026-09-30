using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// An offline projection of final-lane authorized releases. This command neither grants publication
/// authority nor writes a protected ref; the final lane authenticates the complete inventory first.
/// </summary>
public static class SyncPublicChangelogCommand
{
    private const int MaximumInputBytes = 1_048_576;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static int Run(string[] args, TextWriter output, string repositoryRoot, IFinalLaneInventoryAuthority? authority) =>
        Run(args, output, repositoryRoot, authority, null);

    internal static int Run(string[] args, TextWriter output, string repositoryRoot, IFinalLaneInventoryAuthority? authority,
        Action<string>? replacementCompleted)
    {
        string? inventoryPath = null;
        string? evidenceRoot = null;
        Uri? publicationBase = null;
        bool check = false;
        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--check" when !check:
                    check = true;
                    break;
                case "--inventory" when inventoryPath is null && index + 1 < args.Length:
                    inventoryPath = args[++index];
                    break;
                case "--retained-evidence" when evidenceRoot is null && index + 1 < args.Length:
                    evidenceRoot = args[++index];
                    break;
                case "--publication-base" when publicationBase is null && index + 1 < args.Length:
                    if (!Uri.TryCreate(args[++index], UriKind.Absolute, out publicationBase))
                    {
                        output.WriteLine("changelog_invalid_publication_base");
                        return Program.UsageError;
                    }
                    break;
                default:
                    output.WriteLine("changelog_invalid_arguments");
                    return Program.UsageError;
            }
        }

        if (args.Length < 2 || args[0] != "sync-public-changelog" ||
            inventoryPath is null || evidenceRoot is null || publicationBase is null)
        {
            output.WriteLine("changelog_required_arguments_missing");
            return Program.UsageError;
        }

        try
        {
            string root = Path.GetFullPath(repositoryRoot);
            string pagePath = Path.Combine(root, "docs", "public", "changelog", "README.md");
            string manifestPath = Path.Combine(root, "docs", "public", "changelog", "publication-manifest.v1.json");
            string transactionPath = Path.Combine(root, "docs", "public", "changelog", ".publication-transaction.v1.json");
            if (!IsSafePath(pagePath) || !IsSafePath(manifestPath) || !IsSafePath(transactionPath) ||
                !IsSafePath(Path.GetFullPath(inventoryPath)) ||
                !IsSafePath(Path.GetFullPath(evidenceRoot)))
            {
                return Reject(output, "changelog_path_invalid");
            }

            byte[] inventoryBytes = ReadBounded(inventoryPath);
            byte[] previousPage = File.Exists(pagePath) ? ReadBounded(pagePath) : [];
            byte[] previousManifest = File.Exists(manifestPath) ? ReadBounded(manifestPath) : [];
            PublicationTransaction? transaction = null;
            if (File.Exists(transactionPath))
            {
                byte[] transactionBytes = ReadBounded(transactionPath, MaximumInputBytes * 6);
                transaction = JsonSerializer.Deserialize<PublicationTransaction>(transactionBytes, JsonOptions);
                if (transaction is null || transaction.SchemaVersion != "publication-transaction.v1" ||
                    transaction.OldPage is null || transaction.OldManifest is null ||
                    transaction.NewPage is null || transaction.NewManifest is null ||
                    new[] { transaction.OldPage, transaction.OldManifest, transaction.NewPage, transaction.NewManifest }
                        .Any(bytes => bytes.Length > MaximumInputBytes) ||
                    !JsonSerializer.SerializeToUtf8Bytes(transaction, JsonOptions).AsSpan().SequenceEqual(transactionBytes) ||
                    !IsTransactionMember(previousPage, transaction.OldPage, transaction.NewPage) ||
                    !IsTransactionMember(previousManifest, transaction.OldManifest, transaction.NewManifest))
                {
                    return Reject(output, "changelog_transaction_invalid");
                }

                previousPage = transaction.OldPage;
                previousManifest = transaction.OldManifest;
            }
            AcceptedPublicationManifest? accepted = previousManifest.Length == 0
                ? null
                : JsonSerializer.Deserialize<AcceptedPublicationManifest>(previousManifest, JsonOptions);
            ArtifactPolicyResult acceptedCanonical = previousManifest.Length == 0
                ? new ArtifactPolicyResult(true, [], [])
                : ReleaseArtifactPolicy.NormalizeJson(Utf8.GetString(previousManifest));
            if (accepted is not null)
            {
                if (accepted.SchemaVersion != "publication-manifest.v1" ||
                    accepted.Releases is null ||
                    accepted.ProjectionSha256 != Digest(previousPage) ||
                    !acceptedCanonical.IsValid || acceptedCanonical.Bytes is null ||
                    !acceptedCanonical.Bytes.AsSpan().SequenceEqual(previousManifest))
                {
                    return Reject(output, "changelog_accepted_projection_drift");
                }
            }
            else if (previousManifest.Length != 0 ||
                previousPage.Length != 0 &&
                !previousPage.AsSpan().SequenceEqual(Utf8.GetBytes(
                    $"{PublicChangelogPolicy.StartMarker}\n{PublicChangelogPolicy.EndMarker}\n")))
            {
                return Reject(output, "changelog_accepted_manifest_missing");
            }

            AuthorizedInventoryResult inventory = AuthorizedInventoryPolicy.Verify(
                root, Utf8.GetString(inventoryBytes), evidenceRoot, authority,
                accepted?.Releases.Select(release => new AcceptedReleaseIdentity(
                    release.Version, release.TagObjectId, release.EvidenceSha256)).ToArray() ?? [],
                PublicationInventoryVerificationBudget.OverallTimeout);
            if (!inventory.IsValid)
            {
                return Reject(output, inventory.Diagnostics.Count == 0
                    ? "changelog_inventory_invalid" : inventory.Diagnostics[0]);
            }

            PublicChangelogRelease[] entries = inventory.Releases.Select(release => new PublicChangelogRelease(
                release.Descriptor, release.Context, release.Fragments, release.Summary, release.NotesSha256,
                release.TagObjectId, release.TargetOid)).ToArray();
            PublicChangelogResult generated = PublicChangelogPolicy.Generate(entries, publicationBase);
            if (!generated.IsValid)
            {
                return Reject(output, generated.Diagnostic ?? "changelog_projection_invalid");
            }

            PublicChangelogResult merged = PublicChangelogPolicy.ReplaceRegion(
                previousPage, generated.Page!, bootstrap: accepted is null);
            if (!merged.IsValid)
            {
                return Reject(output, merged.Diagnostic ?? "changelog_projection_invalid");
            }

            var manifest = new AcceptedPublicationManifest(
                "publication-manifest.v1",
                Digest(inventoryBytes),
                Digest(merged.Page!),
                generated.HighestStableVersion,
                inventory.Releases.OrderBy(release => release.Version, StringComparer.Ordinal)
                    .Select(release => new AcceptedPublicationRelease(
                        release.Version, release.TagObjectId, release.EvidenceSha256)).ToArray());
            ArtifactPolicyResult manifestJson = ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(manifest, JsonOptions));
            if (!manifestJson.IsValid || manifestJson.Bytes is null || merged.Page!.Length > MaximumInputBytes)
            {
                return Reject(output, "changelog_manifest_invalid");
            }

            // A journal is not publication authority. Recovery is permitted only when the
            // supplied, authenticated inventory reproduces the exact pending generation.
            if (transaction is not null &&
                (!transaction.NewPage.AsSpan().SequenceEqual(merged.Page) ||
                 !transaction.NewManifest.AsSpan().SequenceEqual(manifestJson.Bytes)))
            {
                return Reject(output, "changelog_transaction_generation_mismatch");
            }

            bool same = previousPage.AsSpan().SequenceEqual(merged.Page) &&
                previousManifest.AsSpan().SequenceEqual(manifestJson.Bytes);
            if (check)
            {
                if (transaction is not null) return Reject(output, "changelog_transaction_incomplete");
                output.WriteLine(same ? "changelog_current" : "changelog_stale");
                return same ? Program.Success : Program.ToolchainRejected;
            }

            if (!same || transaction is not null)
            {
                transaction ??= new PublicationTransaction("publication-transaction.v1",
                    previousPage, previousManifest, merged.Page!, manifestJson.Bytes);
                if (!File.Exists(transactionPath))
                {
                    // Journal members are whole documents, not artifact-policy text fields.
                    WriteAtomic(transactionPath, JsonSerializer.SerializeToUtf8Bytes(transaction, JsonOptions));
                }
                try
                {
                    WriteAtomic(manifestPath, transaction.NewManifest);
                    replacementCompleted?.Invoke("manifest");
                    WriteAtomic(pagePath, transaction.NewPage);
                    replacementCompleted?.Invoke("page");
                    File.Delete(transactionPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Keep the journal if rollback itself fails, so a later authenticated
                    // invocation can recover either atomic old/new member combination.
                    Restore(manifestPath, transaction.OldManifest);
                    Restore(pagePath, transaction.OldPage);
                    File.Delete(transactionPath);
                    throw;
                }
            }

            if (generated.Warning is not null) output.WriteLine(generated.Warning);
            output.WriteLine(same ? "changelog_unchanged" : "changelog_generated");
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException)
        {
            return Reject(output, "changelog_input_invalid");
        }
    }

    private static bool IsSafePath(string path)
    {
        for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
        {
            if ((File.Exists(part) || Directory.Exists(part)) &&
                (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0) return false;
        }
        return true;
    }

    private static bool IsTransactionMember(byte[] current, byte[] old, byte[] next) =>
        current.AsSpan().SequenceEqual(old) || current.AsSpan().SequenceEqual(next);

    private static void Restore(string path, byte[] content)
    {
        if (content.Length == 0) File.Delete(path);
        else WriteAtomic(path, content);
    }

    private static byte[] ReadBounded(string path, int maximumBytes = MaximumInputBytes)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximumBytes) throw new IOException("changelog_input_too_large");
        using var memory = new MemoryStream();
        file.CopyTo(memory);
        return memory.ToArray();
    }

    private static void WriteAtomic(string path, byte[] content)
    {
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(content);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine(diagnostic);
        return Program.ToolchainRejected;
    }

    private sealed record AcceptedPublicationManifest(
        string SchemaVersion, string InputSetSha256, string ProjectionSha256,
        string? HighestStableVersion, IReadOnlyList<AcceptedPublicationRelease> Releases);

    private sealed record AcceptedPublicationRelease(string Version, string TagObjectId, string EvidenceSha256);

    private sealed record PublicationTransaction(
        string SchemaVersion, byte[] OldPage, byte[] OldManifest, byte[] NewPage, byte[] NewManifest);
}
