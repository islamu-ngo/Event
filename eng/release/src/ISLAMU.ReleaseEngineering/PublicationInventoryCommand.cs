using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Prepares the complete retained set for human review and signing. An unsigned proposal is not
/// publication authority; no signature or accepted artifact is created or replaced by this command.
/// </summary>
public static class PublicationInventoryCommand
{
    private const int MaximumBytes = 1_048_576;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };

    public static int Run(string[] args, TextWriter output, string repositoryRoot)
    {
        string[] names = ["--inventory", "--retained-evidence", "--release-evidence",
            "--release-directory", "--disclosure-approved", "--output-directory"];
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        if (args.Length != 13 || args[0] != "prepare-publication-inventory")
            return Reject(output, "publication_inventory_invalid_arguments", Program.UsageError);
        for (int index = 1; index < args.Length; index += 2)
        {
            if (!names.Contains(args[index], StringComparer.Ordinal) ||
                string.IsNullOrWhiteSpace(args[index + 1]) ||
                !options.TryAdd(args[index], args[index + 1]))
                return Reject(output, "publication_inventory_invalid_arguments", Program.UsageError);
        }

        try
        {
            string repository = SafePath(repositoryRoot, repositoryRoot);
            string inventoryPath = SafePath(repository, options["--inventory"]);
            string evidenceRoot = SafePath(repository, options["--retained-evidence"]);
            string proposalRoot = SafePath(repository, options["--output-directory"]);
            string sourceDirectory = options["--release-directory"];
            string evidenceRelative = options["--release-evidence"];
            RelativePath(sourceDirectory);
            RelativePath(evidenceRelative);
            if (!Directory.Exists(evidenceRoot) || Directory.Exists(proposalRoot) || File.Exists(proposalRoot))
                return Reject(output, "publication_inventory_output_must_be_new");

            byte[] previousBytes = Read(inventoryPath);
            if (!ReleaseArtifactPolicy.NormalizeJson(Utf8.GetString(previousBytes)).IsValid)
                return Reject(output, "publication_inventory_input_invalid");
            Inventory? previous = JsonSerializer.Deserialize<Inventory>(previousBytes, JsonOptions);
            if (previous is null || previous.SchemaVersion != "authorized-inventory.v1" ||
                previous.Producer != "final-lane" || !previous.CompleteSet ||
                previous.Entries is null || previous.Entries.Length > 1_024)
                return Reject(output, "publication_inventory_input_invalid");
            if (previous.Entries.Any(entry => entry is null) ||
                previous.Entries.Select(entry => entry.Version).Distinct(StringComparer.Ordinal).Count() != previous.Entries.Length)
                return Reject(output, "inventory_duplicate_version");

            string finalPath = SafePath(evidenceRoot, evidenceRelative);
            if (!File.Exists(finalPath)) return Reject(output, "inventory_evidence_missing");
            byte[] finalBytes = Read(finalPath);
            using JsonDocument final = JsonDocument.Parse(finalBytes);
            JsonElement fields = final.RootElement;
            string version = Text(fields, "version");
            if (!SafeVersion(version) ||
                options["--disclosure-approved"] != version)
                return Reject(output, "publication_inventory_disclosure_approval_required");
            RelativePath(version);

            // The same independently promoted authority used by release verification is mandatory.
            // A caller-chosen bundle directory alone is not a trust root.
            string Required(string name) => Environment.GetEnvironmentVariable(name) ?? string.Empty;
            TrustedBundleResult trusted = TrustedBundlePolicy.Verify(new TrustedBundleVerificationRequest(
                Required("ISLAMU_RELEASE_TRUSTED_BUNDLE"), repository,
                new PromotionAuthorityInput(Required("ISLAMU_RELEASE_PROMOTION_RECEIPT"),
                    Required("ISLAMU_RELEASE_PROMOTION_SIGNATURE"), Required("ISLAMU_RELEASE_PROMOTION_PRINCIPAL")),
                Required("ISLAMU_RELEASE_BUNDLE_ID"), Required("ISLAMU_RELEASE_BUNDLE_VERSION"),
                Required("ISLAMU_RELEASE_POLICY_VERSION"), Required("ISLAMU_RELEASE_CONFIG_VERSION"),
                Required("ISLAMU_RELEASE_TRUST_VERSION"))
            { ExpectedManifestDigest = Required("ISLAMU_RELEASE_MANIFEST_SHA256") });
            if (!trusted.IsValid || trusted.Bundle is null)
                return Reject(output, "release_trusted_bundle_invalid");

            string tag = Text(fields, "tagObjectId");
            string target = Text(fields, "targetOid");
            if (!TagCommand.IsSignedFinalRelease(repository, trusted.Bundle.Root, finalPath, tag, version, target))
                return Reject(output, "publication_inventory_release_signature_invalid");
            byte[] approval = Canonical(new
            {
                schemaVersion = "publication-approval.v1", version, tagObjectId = tag,
                evidenceSha256 = Digest(finalBytes), disclosureAuthorized = true,
            });
            AuthorizedInventoryEntry? existing = previous.Entries.SingleOrDefault(item => item.Version == version);
            if (existing is not null)
                approval = Read(SafePath(evidenceRoot, $"publication-approvals/{version}.json"));
            var entry = new AuthorizedInventoryEntry(version, Text(fields, "line"),
                Text(fields, "releaseDate"), tag, target, sourceDirectory, evidenceRelative,
                Digest(finalBytes), true, Digest(approval), SourceDocuments(repository, target, sourceDirectory));
            if (existing is not null &&
                !Canonical(existing).AsSpan().SequenceEqual(Canonical(entry)))
                return Reject(output, "publication_inventory_retained_release_replaced");
            AuthorizedInventoryEntry[] union = previous.Entries.Where(item => item.Version != version)
                .Append(existing ?? entry).OrderBy(item => item.Version, StringComparer.Ordinal).ToArray();
            if (union.Length > 1_024) return Reject(output, "inventory_too_many_entries");
            var approvals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (AuthorizedInventoryEntry retained in union)
            {
                RelativePath(retained.Version);
                if (!SafeVersion(retained.Version))
                    return Reject(output, "publication_inventory_input_invalid");
                string retainedPath = SafePath(evidenceRoot, $"publication-approvals/{retained.Version}.json");
                byte[] receipt = File.Exists(retainedPath) ? Read(retainedPath) :
                    retained.Version == version ? approval : throw new IOException("approval_missing");
                using JsonDocument document = JsonDocument.Parse(receipt);
                JsonElement record = document.RootElement;
                if (receipt.Length > 16_384 ||
                    !ReleaseArtifactPolicy.NormalizeJson(Utf8.GetString(receipt)).IsValid ||
                    Digest(receipt) != retained.AuthorizationEvidenceSha256 ||
                    Text(record, "schemaVersion") != "publication-approval.v1" ||
                    Text(record, "version") != retained.Version ||
                    Text(record, "tagObjectId") != retained.TagObjectId ||
                    Text(record, "evidenceSha256") != retained.EvidenceSha256 ||
                    !record.GetProperty("disclosureAuthorized").GetBoolean())
                    return Reject(output, "publication_inventory_retained_approval_invalid");
                approvals.Add(retained.Version, receipt);
                RelativePath(retained.EvidencePath);
                if (!TagCommand.IsSignedFinalRelease(repository, trusted.Bundle.Root,
                    SafePath(evidenceRoot, retained.EvidencePath), retained.TagObjectId,
                    retained.Version, retained.TargetOid))
                    return Reject(output, "publication_inventory_release_signature_invalid");
            }
            string approvalDirectory = SafePath(evidenceRoot, "publication-approvals");
            if (Directory.Exists(approvalDirectory))
            {
                foreach (string receipt in Directory.EnumerateFileSystemEntries(approvalDirectory))
                {
                    SafePath(evidenceRoot, receipt);
                    if (!File.Exists(receipt) || Path.GetExtension(receipt) != ".json" ||
                        !approvals.ContainsKey(Path.GetFileNameWithoutExtension(receipt)))
                        return Reject(output, "publication_inventory_retained_release_omitted");
                }
            }
            byte[] proposed = Canonical(new Inventory("authorized-inventory.v1", "final-lane", true, union));
            AuthorizedInventoryResult verified = AuthorizedInventoryPolicy.Verify(repository, Utf8.GetString(proposed),
                evidenceRoot, new ProposalValidation(Digest(proposed), union), previous.Entries.Select(item =>
                    new AcceptedReleaseIdentity(item.Version, item.TagObjectId, item.EvidenceSha256)).ToArray(),
                TimeSpan.FromSeconds(30));
            if (!verified.IsValid) return Reject(output, verified.Diagnostics[0]);

            // Publish the unsigned directory as a unit. No existing directory or detached signature
            // may be overwritten, even on an idempotent repeat; callers choose a fresh proposal path.
            string parent = Path.GetDirectoryName(proposalRoot)!;
            if (!Directory.Exists(parent)) return Reject(output, "publication_inventory_output_parent_missing");
            string temporary = Path.Combine(parent, $".publication-inventory-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(Path.Combine(temporary, "publication-approvals"));
                File.WriteAllBytes(Path.Combine(temporary, "authorized-inventory.v1.json"), proposed);
                foreach ((string approvedVersion, byte[] receipt) in approvals)
                    File.WriteAllBytes(Path.Combine(temporary, "publication-approvals", approvedVersion + ".json"), receipt);
                Directory.Move(temporary, proposalRoot);
            }
            finally
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
            }
            output.WriteLine("publication_inventory_pending_signature");
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException or KeyNotFoundException or
            FormatException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            return Reject(output, "publication_inventory_input_invalid");
        }
    }

    private static AuthorizedSourceDocument[] SourceDocuments(string repository, string target, string sourceDirectory)
    {
        RelativePath(sourceDirectory);
        using JsonDocument context = JsonDocument.Parse(Git(repository, "show", $"{target}:{sourceDirectory}/release-context.v1.json"));
        JsonElement root = context.RootElement;
        var sources = root.GetProperty("changes").EnumerateArray()
            .Select(change => change.TryGetProperty("changeId", out JsonElement id) ? id.GetString() : null)
            .OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(id =>
            {
                if (!ChangeIdPolicy.IsValid(id)) throw new IOException("change_id_invalid");
                return new AuthorizedSourceDocument($"docs/internal/releases/changes/{id}.yaml", "fragment");
            }).ToList();
        string previous = root.GetProperty("evidence").GetProperty("previousPublishedOid").GetString()!;
        string[] commits = Git(repository, "rev-list", $"{previous}..{target}").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] renamePaths = Git(repository, "ls-tree", "-r", "--name-only", target, "--",
            "docs/internal/releases/change-id-renames").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        sources.AddRange(renamePaths.Where(path => commits.Contains(Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal))
            .Order(StringComparer.Ordinal).Select(path => new AuthorizedSourceDocument(path, "change-id-rename")));
        return sources.ToArray();
    }

    private static string Git(string repository, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = repository, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true,
            },
        };
        foreach (string argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.StartInfo.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        process.StartInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        process.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> stdout = ReadProcess(process.StandardOutput, timeout.Token);
        Task<string> stderr = ReadProcess(process.StandardError, timeout.Token);
        try
        {
            Task.WhenAll(process.WaitForExitAsync(timeout.Token), stdout, stderr).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new IOException("git_failed");
            return stdout.Result;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<string> ReadProcess(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        char[] buffer = new char[8_192];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (text.Length + count > MaximumBytes) throw new IOException("git_output_too_large");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }

    private static string SafePath(string root, string path)
    {
        if (path.Contains('\\', StringComparison.Ordinal) ||
            path.Split('/').Any(part => part is "." or ".."))
            throw new IOException("path_invalid");
        string full = Path.GetFullPath(path, root);
        for (string? current = full; current is not null; current = Path.GetDirectoryName(current))
        {
            if (new FileInfo(current).LinkTarget is not null ||
                (File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("path_invalid");
        }
        return full;
    }

    private static void RelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains(':', StringComparison.Ordinal) ||
            path.Contains('\\', StringComparison.Ordinal) || path.Split('/').Any(part => part is "" or "." or ".."))
            throw new IOException("path_invalid");
    }

    private static byte[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is <= 0 or > MaximumBytes) throw new IOException("input_size_invalid");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static byte[] Canonical<T>(T value)
    {
        ArtifactPolicyResult result = ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(value, JsonOptions));
        if (!result.IsValid || result.Bytes is null || result.Bytes.Length > MaximumBytes)
            throw new IOException("json_invalid");
        return result.Bytes;
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString()!;
    private static bool SafeVersion(string version) => version is { Length: > 0 and <= 64 } &&
        version.All(character => character is >= '0' and <= '9' or '.' or '-' or >= 'a' and <= 'z');
    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static int Reject(TextWriter output, string diagnostic, int code = Program.ToolchainRejected)
    {
        output.WriteLine(diagnostic);
        return code;
    }

    private sealed record Inventory(string SchemaVersion, string Producer, bool CompleteSet, AuthorizedInventoryEntry[] Entries);

    // Only validates a proposal after every tag and receipt has been checked above. This private
    // adapter never escapes to the publication command, which still requires the human signature.
    private sealed class ProposalValidation(string verifiedDigest, AuthorizedInventoryEntry[] verifiedEntries)
        : IFinalLaneInventoryAuthority
    {
        public bool VerifyCompleteInventory(string inventorySha256, IReadOnlyList<AuthorizedInventoryEntry> entries) =>
            inventorySha256 == verifiedDigest &&
            Canonical(entries).AsSpan().SequenceEqual(Canonical(verifiedEntries));
    }
}
