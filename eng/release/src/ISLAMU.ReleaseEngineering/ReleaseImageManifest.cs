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
/// digest-only builds across one or more configured OCI registries (exact 4 approved images,
/// tagless canonical <c>registry/repository@sha256:&lt;digest&gt;</c> references with identical
/// registry coverage across every image).
/// </summary>
public static class ReleaseImageManifest
{
    private static class Diagnostics
    {
        public const string Invalid = "image_manifest_invalid";
        public const string VersionMismatch = "image_manifest_version_mismatch";
        public const string CommitMismatch = "image_manifest_commit_mismatch";
        public const string IncompleteImages = "image_manifest_incomplete_images";
        public const string ReferenceInvalid = "image_manifest_reference_invalid";
    }

    private const int MaximumManifestBytes = 256 * 1024;
    private const int MaximumRegistriesPerImage = 16;
    private const int ExpectedSchemaVersion = 1;
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
        "^(?<registry>[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+(?::[0-9]{1,5})?)/(?<repository>[a-z0-9]+(?:[._-][a-z0-9]+)*(?:/[a-z0-9]+(?:[._-][a-z0-9]+)*)*)@(?<digest>sha256:[0-9a-f]{64})$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromMilliseconds(100));

    public static ReleaseImageManifestLoadResult LoadFromFile(
        string manifestPath,
        string expectedVersion,
        string? expectedCommitOid = null,
        IReadOnlyCollection<string>? requiredRegistries = null)
    {
        if (!TryResolveExistingManifestPath(manifestPath, out string fullPath))
        {
            return Invalid(Diagnostics.Invalid);
        }

        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length <= 0 || info.Length > MaximumManifestBytes)
            {
                return Invalid(Diagnostics.Invalid);
            }

            string json = StrictUtf8.GetString(File.ReadAllBytes(fullPath));
            return Parse(json, expectedVersion, expectedCommitOid, requiredRegistries);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            return Invalid(Diagnostics.Invalid);
        }
    }

    public static ReleaseImageManifestLoadResult Parse(
        string json,
        string expectedVersion,
        string? expectedCommitOid = null,
        IReadOnlyCollection<string>? requiredRegistries = null)
    {
        if (string.IsNullOrWhiteSpace(json) || StrictUtf8.GetByteCount(json) > MaximumManifestBytes)
        {
            return Invalid(Diagnostics.Invalid);
        }

        if (!TryParseManifestJson(json, out JsonDocument? document) || document is null)
        {
            return Invalid(Diagnostics.Invalid);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            string? headerDiagnostic = TryReadHeader(root, expectedVersion, expectedCommitOid, out string version, out string sourceCommitOid, out JsonElement imagesEl);
            if (headerDiagnostic is not null)
            {
                return Invalid(headerDiagnostic);
            }

            string? imagesDiagnostic = TryParseImages(imagesEl, requiredRegistries, out EnrichedImageReference[] ordered);
            return imagesDiagnostic is not null
                ? Invalid(imagesDiagnostic)
                : new ReleaseImageManifestLoadResult(
                    true,
                    new ReleaseImageManifestDocument(ExpectedSchemaVersion, version, sourceCommitOid, ordered),
                    null);
        }
    }

    private static bool TryResolveExistingManifestPath(string manifestPath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(manifestPath) || manifestPath.Any(char.IsControl))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(manifestPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return File.Exists(fullPath) && !HasReparsePoint(fullPath);
    }

    private static bool TryParseManifestJson(string json, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
            document = null;
            return false;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static string? TryReadHeader(
        JsonElement root,
        string expectedVersion,
        string? expectedCommitOid,
        out string version,
        out string sourceCommitOid,
        out JsonElement imagesEl)
    {
        version = string.Empty;
        sourceCommitOid = string.Empty;
        imagesEl = default;

        if (!root.TryGetProperty("schemaVersion", out JsonElement schemaVersionEl) ||
            schemaVersionEl.ValueKind != JsonValueKind.Number ||
            !schemaVersionEl.TryGetInt32(out int schemaVersion) ||
            schemaVersion != ExpectedSchemaVersion)
        {
            return Diagnostics.Invalid;
        }

        if (!JsonElementCanonicalizer.TryGetNonEmptyString(root, "version", out string? parsedVersion))
        {
            return Diagnostics.Invalid;
        }

        if (!string.Equals(parsedVersion, expectedVersion, StringComparison.Ordinal))
        {
            return Diagnostics.VersionMismatch;
        }

        if (!JsonElementCanonicalizer.TryGetNonEmptyString(root, "sourceCommitOid", out string? parsedCommit) ||
            !GitProcessObjectReader.IsFullOid(parsedCommit))
        {
            return Diagnostics.Invalid;
        }

        if (!string.IsNullOrWhiteSpace(expectedCommitOid) &&
            !string.Equals(parsedCommit, expectedCommitOid, StringComparison.Ordinal))
        {
            return Diagnostics.CommitMismatch;
        }

        if (!root.TryGetProperty("images", out imagesEl) || imagesEl.ValueKind != JsonValueKind.Array)
        {
            return Diagnostics.Invalid;
        }

        version = parsedVersion;
        sourceCommitOid = parsedCommit;
        return null;
    }

    private static string? TryParseImages(
        JsonElement imagesEl,
        IReadOnlyCollection<string>? requiredRegistries,
        out EnrichedImageReference[] ordered)
    {
        ordered = [];
        var seenImages = new HashSet<string>(StringComparer.Ordinal);
        var parsedImages = new List<EnrichedImageReference>(RequiredImageNames.Count);
        HashSet<string>? expectedRegistrySet = null;

        foreach (JsonElement imageEl in imagesEl.EnumerateArray())
        {
            string? entryError = TryParseSingleImage(imageEl, seenImages, out EnrichedImageReference? parsed, out HashSet<string> imageRegistries);
            if (entryError is not null)
            {
                return entryError;
            }

            if (expectedRegistrySet is null)
            {
                expectedRegistrySet = imageRegistries;
            }
            else if (!expectedRegistrySet.SetEquals(imageRegistries))
            {
                return Diagnostics.ReferenceInvalid;
            }

            parsedImages.Add(parsed!);
        }

        if (!seenImages.SetEquals(RequiredImageNames))
        {
            return Diagnostics.IncompleteImages;
        }

        if (requiredRegistries is { Count: > 0 } &&
            (expectedRegistrySet is null || !requiredRegistries.All(expectedRegistrySet.Contains)))
        {
            return Diagnostics.ReferenceInvalid;
        }

        ordered = parsedImages.OrderBy(i => i.ImageName, StringComparer.Ordinal).ToArray();
        return null;
    }

    private static string? TryParseSingleImage(
        JsonElement imageEl,
        HashSet<string> seenImages,
        out EnrichedImageReference? parsed,
        out HashSet<string> registries)
    {
        parsed = null;
        registries = new HashSet<string>(StringComparer.Ordinal);
        if (imageEl.ValueKind != JsonValueKind.Object ||
            !JsonElementCanonicalizer.TryGetNonEmptyString(imageEl, "name", out string? name) ||
            !JsonElementCanonicalizer.TryGetNonEmptyString(imageEl, "digest", out string? digest) ||
            !imageEl.TryGetProperty("references", out JsonElement refsEl) ||
            refsEl.ValueKind != JsonValueKind.Array)
        {
            return Diagnostics.Invalid;
        }

        if (!RequiredImageNames.Contains(name) || !seenImages.Add(name))
        {
            return Diagnostics.IncompleteImages;
        }

        if (!DigestPattern.IsMatch(digest) ||
            !TryParseCanonicalReferences(refsEl, digest, out string[] canonicalRefs, out registries))
        {
            return Diagnostics.ReferenceInvalid;
        }

        parsed = new EnrichedImageReference(name, digest, canonicalRefs);
        return null;
    }

    private static bool TryParseCanonicalReferences(
        JsonElement refsEl,
        string expectedDigest,
        out string[] canonicalRefs,
        out HashSet<string> registriesSeen)
    {
        canonicalRefs = [];
        registriesSeen = new HashSet<string>(StringComparer.Ordinal);
        var collected = new List<string>();

        foreach (JsonElement refEl in refsEl.EnumerateArray())
        {
            if (refEl.ValueKind != JsonValueKind.String || collected.Count >= MaximumRegistriesPerImage)
            {
                return false;
            }

            string reference = refEl.GetString()!;
            Match match = CanonicalReferencePattern.Match(reference);
            if (!match.Success ||
                !string.Equals(match.Groups["digest"].Value, expectedDigest, StringComparison.Ordinal) ||
                !registriesSeen.Add(match.Groups["registry"].Value))
            {
                return false;
            }

            collected.Add(reference);
        }

        if (collected.Count == 0)
        {
            return false;
        }

        canonicalRefs = collected.Order(StringComparer.Ordinal).ToArray();
        return true;
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
