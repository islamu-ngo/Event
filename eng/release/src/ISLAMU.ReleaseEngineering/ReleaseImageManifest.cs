using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ISLAMU.ReleaseEngineering;

public sealed record ReleaseImageManifestDocument(
    int SchemaVersion,
    string Version,
    string SourceCommitOid,
    IReadOnlyList<EnrichedImageReference> Images);

public sealed record ReleaseImageManifestLoadResult(
    bool IsValid,
    ReleaseImageManifestDocument? Manifest,
    string? Diagnostic);

/// <summary>
/// Strictly parses and validates <c>release-images.v1.json</c> produced after multi-arch OCI
/// digest-only builds across both ATCR and GHCR (exact 4 approved images, 8 tagless canonical
/// <c>registry/repository@sha256:&lt;digest&gt;</c> references).
/// </summary>
public static class ReleaseImageManifest
{
    private const int MaximumManifestBytes = 256 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> RequiredImageNames = new(StringComparer.Ordinal)
    {
        "api",
        "blazor",
        "standalone",
        "migration",
    };

    private static readonly Regex DigestPattern = new(
        "^sha256:[0-9a-f]{64}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex CanonicalReferencePattern = new(
        "^(?<registry>atcr\\.io|ghcr\\.io)/(?<repository>[a-z0-9][a-z0-9._/-]*[a-z0-9])@(?<digest>sha256:[0-9a-f]{64})$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    public static ReleaseImageManifestLoadResult LoadFromFile(
        string manifestPath,
        string expectedVersion,
        string? expectedCommitOid = null)
    {
        if (string.IsNullOrWhiteSpace(manifestPath) || manifestPath.Any(char.IsControl))
        {
            return Invalid("image_manifest_invalid");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(manifestPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Invalid("image_manifest_invalid");
        }

        if (!File.Exists(fullPath) || HasReparsePoint(fullPath))
        {
            return Invalid("image_manifest_invalid");
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length <= 0 || info.Length > MaximumManifestBytes)
            {
                return Invalid("image_manifest_invalid");
            }

            string json = StrictUtf8.GetString(File.ReadAllBytes(fullPath));
            return Parse(json, expectedVersion, expectedCommitOid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Invalid("image_manifest_invalid");
        }
    }

    public static ReleaseImageManifestLoadResult Parse(
        string json,
        string expectedVersion,
        string? expectedCommitOid = null)
    {
        if (string.IsNullOrWhiteSpace(json) || StrictUtf8.GetByteCount(json) > MaximumManifestBytes)
        {
            return Invalid("image_manifest_invalid");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException)
        {
            return Invalid("image_manifest_invalid");
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("image_manifest_invalid");
            }

            if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersionEl) ||
                schemaVersionEl.ValueKind != JsonValueKind.Number ||
                !schemaVersionEl.TryGetInt32(out int schemaVersion) ||
                schemaVersion != 1)
            {
                return Invalid("image_manifest_invalid");
            }

            if (!root.TryGetProperty("version", out JsonElement versionEl) ||
                versionEl.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(versionEl.GetString()))
            {
                return Invalid("image_manifest_invalid");
            }

            string version = versionEl.GetString()!;
            if (!string.Equals(version, expectedVersion, StringComparison.Ordinal))
            {
                return Invalid("image_manifest_version_mismatch");
            }

            if (!root.TryGetProperty("sourceCommitOid", out JsonElement commitEl) ||
                commitEl.ValueKind != JsonValueKind.String ||
                !GitProcessObjectReader.IsFullOid(commitEl.GetString()))
            {
                return Invalid("image_manifest_invalid");
            }

            string sourceCommitOid = commitEl.GetString()!;
            if (!string.IsNullOrWhiteSpace(expectedCommitOid) &&
                !string.Equals(sourceCommitOid, expectedCommitOid, StringComparison.Ordinal))
            {
                return Invalid("image_manifest_commit_mismatch");
            }

            if (!root.TryGetProperty("images", out JsonElement imagesEl) ||
                imagesEl.ValueKind != JsonValueKind.Array)
            {
                return Invalid("image_manifest_invalid");
            }

            var seenImages = new HashSet<string>(StringComparer.Ordinal);
            var parsedImages = new List<EnrichedImageReference>(RequiredImageNames.Count);

            foreach (JsonElement imageEl in imagesEl.EnumerateArray())
            {
                if (imageEl.ValueKind != JsonValueKind.Object ||
                    !imageEl.TryGetProperty("name", out JsonElement nameEl) || nameEl.ValueKind != JsonValueKind.String ||
                    !imageEl.TryGetProperty("digest", out JsonElement digestEl) || digestEl.ValueKind != JsonValueKind.String ||
                    !imageEl.TryGetProperty("references", out JsonElement refsEl) || refsEl.ValueKind != JsonValueKind.Array)
                {
                    return Invalid("image_manifest_invalid");
                }

                string name = nameEl.GetString()!;
                string digest = digestEl.GetString()!;
                if (!RequiredImageNames.Contains(name) || !seenImages.Add(name))
                {
                    return Invalid("image_manifest_incomplete_images");
                }

                if (!DigestPattern.IsMatch(digest))
                {
                    return Invalid("image_manifest_reference_invalid");
                }

                var registriesSeen = new HashSet<string>(StringComparer.Ordinal);
                var canonicalRefs = new List<string>(2);
                foreach (JsonElement refEl in refsEl.EnumerateArray())
                {
                    if (refEl.ValueKind != JsonValueKind.String)
                    {
                        return Invalid("image_manifest_reference_invalid");
                    }

                    string reference = refEl.GetString()!;
                    Match match = CanonicalReferencePattern.Match(reference);
                    if (!match.Success ||
                        !string.Equals(match.Groups["digest"].Value, digest, StringComparison.Ordinal) ||
                        !registriesSeen.Add(match.Groups["registry"].Value))
                    {
                        return Invalid("image_manifest_reference_invalid");
                    }

                    canonicalRefs.Add(reference);
                }

                if (canonicalRefs.Count != 2 || !registriesSeen.Contains("atcr.io") || !registriesSeen.Contains("ghcr.io"))
                {
                    return Invalid("image_manifest_reference_invalid");
                }

                parsedImages.Add(new EnrichedImageReference(
                    name,
                    digest,
                    canonicalRefs.Order(StringComparer.Ordinal).ToArray()));
            }

            if (seenImages.Count != RequiredImageNames.Count || !seenImages.SetEquals(RequiredImageNames))
            {
                return Invalid("image_manifest_incomplete_images");
            }

            EnrichedImageReference[] ordered = parsedImages
                .OrderBy(i => i.ImageName, StringComparer.Ordinal)
                .ToArray();

            return new ReleaseImageManifestLoadResult(
                true,
                new ReleaseImageManifestDocument(schemaVersion, version, sourceCommitOid, ordered),
                null);
        }
    }

    private static bool HasReparsePoint(string fullPath)
    {
        string? current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            string? parent = Path.GetDirectoryName(current);
            if (string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return false;
    }

    private static ReleaseImageManifestLoadResult Invalid(string diagnostic) =>
        new(false, null, diagnostic);
}
