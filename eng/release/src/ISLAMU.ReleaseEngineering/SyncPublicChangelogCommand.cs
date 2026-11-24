using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.ReleaseEngineering;

/// <summary>
/// Projects signed local release records under <c>docs/internal/releases/</c> into
/// <c>docs/public/changelog/README.md</c> and its atomic <c>publication-manifest.v1.json</c>.
/// </summary>
public static class SyncPublicChangelogCommand
{
    private const int MaximumInputBytes = 1_048_576;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static int Run(string[] args, TextWriter output, string repositoryRoot) =>
        Run(args, output, repositoryRoot, replacementCompleted: null);

    internal static int Run(
        string[] args,
        TextWriter output,
        string repositoryRoot,
        Action<string>? replacementCompleted)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);

        Uri? publicationBase = null;
        string? allowedSignersPath = null;
        bool check = false;
        for (int index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--check" when !check:
                    check = true;
                    break;
                case "--allowed-signers" when allowedSignersPath is null && index + 1 < args.Length:
                    allowedSignersPath = args[++index];
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

        if (args.Length < 2 || args[0] != "sync-public-changelog" || publicationBase is null)
        {
            output.WriteLine("changelog_required_arguments_missing");
            return Program.UsageError;
        }

        try
        {
            string root = Path.GetFullPath(repositoryRoot);
            string resolvedAllowedSigners = allowedSignersPath is not null
                ? Path.GetFullPath(allowedSignersPath)
                : GitTagSignatureVerifier.ResolveDefaultAllowedSignersPath(root);
            string pagePath = Path.Combine(root, "docs", "public", "changelog", "README.md");
            string manifestPath = Path.Combine(root, "docs", "public", "changelog", "publication-manifest.v1.json");
            string transactionPath = Path.Combine(root, "docs", "public", "changelog", ".publication-transaction.v1.json");
            if (!IsSafePath(pagePath) || !IsSafePath(manifestPath) || !IsSafePath(transactionPath) ||
                !IsSafePath(resolvedAllowedSigners))
            {
                return Reject(output, "changelog_path_invalid");
            }

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
                (previousPage.Length != 0 &&
                 !previousPage.AsSpan().SequenceEqual(Utf8.GetBytes(
                     $"{PublicChangelogPolicy.StartMarker}\n{PublicChangelogPolicy.EndMarker}\n"))))
            {
                return Reject(output, "changelog_accepted_manifest_missing");
            }

            if (!TryLoadSignedReleases(root, resolvedAllowedSigners, out List<PublicChangelogRelease> releases, out string? loadDiagnostic))
            {
                return Reject(output, loadDiagnostic ?? "changelog_releases_invalid");
            }

            if (accepted is not null)
            {
                Dictionary<string, PublicChangelogRelease> byVersion = releases.ToDictionary(release => release.Descriptor.Version, StringComparer.Ordinal);
                foreach (AcceptedPublicationRelease prior in accepted.Releases)
                {
                    if (!byVersion.TryGetValue(prior.Version, out PublicChangelogRelease? current) ||
                        !string.Equals(current.TagObjectId, prior.TagObjectId, StringComparison.Ordinal) ||
                        !string.Equals(current.NotesSha256, prior.NotesSha256, StringComparison.Ordinal))
                    {
                        return Reject(output, "changelog_accepted_history_omitted");
                    }
                }
            }

            PublicChangelogResult generated = PublicChangelogPolicy.Generate(releases, publicationBase);
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

            AcceptedPublicationRelease[] releaseRecords = releases
                .OrderBy(release => release.Descriptor.Version, StringComparer.Ordinal)
                .Select(release => new AcceptedPublicationRelease(
                    release.Descriptor.Version,
                    release.TagObjectId,
                    release.NotesSha256))
                .ToArray();
            string inputSetDigest = Digest(JsonSerializer.SerializeToUtf8Bytes(releaseRecords, JsonOptions));
            var manifest = new AcceptedPublicationManifest(
                "publication-manifest.v1",
                inputSetDigest,
                Digest(merged.Page!),
                generated.HighestStableVersion,
                releaseRecords);
            ArtifactPolicyResult manifestJson = ReleaseArtifactPolicy.NormalizeJson(JsonSerializer.Serialize(manifest, JsonOptions));
            if (!manifestJson.IsValid || manifestJson.Bytes is null || merged.Page!.Length > MaximumInputBytes)
            {
                return Reject(output, "changelog_manifest_invalid");
            }

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
                if (transaction is not null)
                {
                    return Reject(output, "changelog_transaction_incomplete");
                }

                output.WriteLine(same ? "changelog_current" : "changelog_stale");
                return same ? Program.Success : Program.ToolchainRejected;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(pagePath)!);
            if (!same || transaction is not null)
            {
                transaction ??= new PublicationTransaction(
                    "publication-transaction.v1",
                    previousPage,
                    previousManifest,
                    merged.Page!,
                    manifestJson.Bytes);
                if (!File.Exists(transactionPath))
                {
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
                    Restore(manifestPath, transaction.OldManifest);
                    Restore(pagePath, transaction.OldPage);
                    if (File.Exists(transactionPath))
                    {
                        File.Delete(transactionPath);
                    }

                    throw;
                }
            }

            if (generated.Warning is not null)
            {
                output.WriteLine(generated.Warning);
            }

            output.WriteLine(same ? "changelog_unchanged" : "changelog_generated");
            return Program.Success;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or JsonException or InvalidOperationException or DecoderFallbackException)
        {
            return Reject(output, "changelog_input_invalid");
        }
    }

    private static bool TryLoadSignedReleases(
        string repositoryRoot,
        string allowedSignersPath,
        out List<PublicChangelogRelease> releases,
        out string? diagnostic)
    {
        releases = [];
        diagnostic = null;

        string releasesRoot = Path.Combine(repositoryRoot, "docs", "internal", "releases");
        if (!Directory.Exists(releasesRoot))
        {
            diagnostic = "changelog_releases_missing";
            return false;
        }

        string fragmentsRoot = Path.Combine(releasesRoot, "changes");
        string[] candidateDirs = Directory.EnumerateDirectories(releasesRoot, "*", SearchOption.TopDirectoryOnly)
            .Where(dir => File.Exists(Path.Combine(dir, "release.yaml")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (candidateDirs.Length == 0)
        {
            diagnostic = "changelog_releases_missing";
            return false;
        }

        foreach (string releaseDir in candidateDirs)
        {
            string releasePath = Path.Combine(releaseDir, "release.yaml");
            string summaryPath = Path.Combine(releaseDir, "summary.md");
            string contextPath = Path.Combine(releaseDir, "release-context.v1.json");
            string notesPath = Path.Combine(releaseDir, "release-notes.md");
            if (!File.Exists(summaryPath) || !File.Exists(contextPath) || !File.Exists(notesPath))
            {
                diagnostic = "changelog_release_files_missing";
                return false;
            }

            string releaseYaml = Utf8.GetString(ReadBounded(releasePath));
            string summaryText = Utf8.GetString(ReadBounded(summaryPath)).TrimEnd('\n');
            byte[] contextBytes = ReadBounded(contextPath);
            byte[] notesBytes = ReadBounded(notesPath);
            ArtifactPolicyResult notesPolicy = ReleaseArtifactPolicy.ValidateRenderedReleaseNotes(Utf8.GetString(notesBytes));
            if (!notesPolicy.IsValid)
            {
                diagnostic = notesPolicy.Diagnostics.Count == 0 ? "changelog_notes_invalid" : notesPolicy.Diagnostics[0];
                return false;
            }

            ReleaseContext? context = JsonSerializer.Deserialize<ReleaseContext>(contextBytes, JsonOptions);
            if (context is null)
            {
                diagnostic = "changelog_context_invalid";
                return false;
            }

            string[] linkedIds = context.Changes
                .Select(change => change.ChangeId)
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var fragmentYamls = new List<string>(linkedIds.Length);
            foreach (string id in linkedIds)
            {
                string fragmentPath = Path.Combine(fragmentsRoot, id + ".yaml");
                if (!File.Exists(fragmentPath))
                {
                    diagnostic = $"changelog_fragment_missing:{id}";
                    return false;
                }

                fragmentYamls.Add(Utf8.GetString(ReadBounded(fragmentPath)));
            }

            ReleaseInputValidationResult validated = ReleaseInputPolicy.Validate(releaseYaml, fragmentYamls, []);
            if (!validated.IsValid || validated.Descriptor is null)
            {
                diagnostic = validated.Diagnostics.Count == 0 ? "changelog_release_input_invalid" : validated.Diagnostics[0];
                return false;
            }

            GitTagVerificationResult tagVerification = GitTagSignatureVerifier.Verify(new GitTagVerificationRequest(
                repositoryRoot,
                $"v{validated.Descriptor.Version}",
                allowedSignersPath));
            if (!tagVerification.IsValid || tagVerification.TagObjectId is null || tagVerification.TargetCommitOid is null)
            {
                diagnostic = tagVerification.Diagnostic ?? "changelog_tag_invalid";
                return false;
            }

            releases.Add(new PublicChangelogRelease(
                validated.Descriptor,
                context,
                validated.Fragments,
                summaryText,
                Digest(notesBytes),
                tagVerification.TagObjectId,
                tagVerification.TargetCommitOid));
        }

        return true;
    }

    private static bool IsSafePath(string path)
    {
        for (string? part = path; part is not null; part = Path.GetDirectoryName(part))
        {
            if ((File.Exists(part) || Directory.Exists(part)) &&
                (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsTransactionMember(byte[] current, byte[] old, byte[] next) =>
        current.AsSpan().SequenceEqual(old) || current.AsSpan().SequenceEqual(next);

    private static void Restore(string path, byte[] content)
    {
        if (content.Length == 0)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        else
        {
            WriteAtomic(path, content);
        }
    }

    private static byte[] ReadBounded(string path, int maximumBytes = MaximumInputBytes)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > maximumBytes)
        {
            throw new IOException("changelog_input_too_large");
        }

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
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static int Reject(TextWriter output, string diagnostic)
    {
        output.WriteLine(diagnostic);
        return Program.ToolchainRejected;
    }

    private sealed record AcceptedPublicationManifest(
        string SchemaVersion,
        string InputSetSha256,
        string ProjectionSha256,
        string? HighestStableVersion,
        IReadOnlyList<AcceptedPublicationRelease> Releases);

    private sealed record AcceptedPublicationRelease(string Version, string TagObjectId, string NotesSha256);

    private sealed record PublicationTransaction(
        string SchemaVersion,
        byte[] OldPage,
        byte[] OldManifest,
        byte[] NewPage,
        byte[] NewManifest);
}
