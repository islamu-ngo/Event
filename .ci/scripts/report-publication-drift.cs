#:property RestorePackagesWithLockFile=false
#pragma warning disable CA1050

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// A forge release body is mutable, unsigned, and editable by any maintainer or by the forge
// itself, so "published bodies match canonical notes" is unenforceable by construction. This
// tool therefore reports; it never repairs, and drift never invalidates the release. Canonical
// truth stays the signed tag plus release-notes.md committed at preparation commit B.

const int MaximumProjections = 64;
const int MaximumBodyBytes = 4 * 1024 * 1024;

try
{
    Options options = ParseOptions(args);
    ReleaseIdentity identity = ReadReleaseIdentity(options.ReleaseDirectory);
    IReadOnlyList<ProjectionResult> results = Evaluate(identity, ReadProjections(options.ProjectionsPath));
    WriteReport(options.OutputDirectory, identity, results);

    foreach (ProjectionResult result in results)
    {
        Console.WriteLine($"{result.ProviderId}: {result.Status}");
    }

    bool drift = results.Any(result => result.Status == "drift");
    Console.WriteLine(drift
        ? "publication_drift_reported: release remains valid; the signed tag is canonical"
        : "publication_drift_none: every projection matches the canonical notes hash");
    return drift && options.FailOnDrift ? 3 : 0;
}
catch (DriftException exception)
{
    Console.Error.WriteLine(exception.Code);
    return 1;
}

static Options ParseOptions(string[] arguments)
{
    string? releaseDirectory = null;
    string? projections = null;
    string? output = null;
    var failOnDrift = false;
    for (var index = 0; index < arguments.Length; index++)
    {
        switch (arguments[index])
        {
            case "--release-directory" when index + 1 < arguments.Length:
                releaseDirectory = arguments[++index];
                break;
            case "--projections" when index + 1 < arguments.Length:
                projections = arguments[++index];
                break;
            case "--output" when index + 1 < arguments.Length:
                output = arguments[++index];
                break;
            case "--fail-on-drift":
                failOnDrift = true;
                break;
            default:
                throw new DriftException("drift_usage_invalid");
        }
    }

    if (releaseDirectory is null || projections is null || output is null) throw new DriftException("drift_usage_invalid");
    if (!Directory.Exists(releaseDirectory)) throw new DriftException("drift_release_directory_missing");
    if (!File.Exists(projections)) throw new DriftException("drift_projections_missing");
    return new Options(Path.GetFullPath(releaseDirectory), Path.GetFullPath(projections), Path.GetFullPath(output), failOnDrift);
}

static ReleaseIdentity ReadReleaseIdentity(string releaseDirectory)
{
    string evidencePath = Path.Combine(releaseDirectory, "release-evidence.v1.json");
    string notesPath = Path.Combine(releaseDirectory, "release-notes.md");
    if (!File.Exists(evidencePath) || !File.Exists(notesPath)) throw new DriftException("drift_release_evidence_missing");

    byte[] notes = ReadBounded(notesPath);
    string notesSha256 = Sha256(notes);
    using JsonDocument document = JsonDocument.Parse(ReadBounded(evidencePath), new JsonDocumentOptions { MaxDepth = 16 });
    JsonElement root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || RequiredString(root, "schemaVersion") != "release-evidence.v1")
    {
        throw new DriftException("drift_release_evidence_invalid");
    }

    string version = RequiredString(root, "version");
    string tagName = RequiredString(root, "tagName");
    string declaredNotesSha256 = RequiredString(root, "releaseNotesSha256");

    // The evidence manifest is the authority on which bytes were released. If the working copy of
    // release-notes.md no longer matches it, the local checkout is the problem, not the forge.
    if (!string.Equals(declaredNotesSha256, notesSha256, StringComparison.Ordinal)) throw new DriftException("drift_canonical_notes_mismatch");

    return new ReleaseIdentity(version, tagName, $"refs/tags/{tagName}", notesSha256, RequiredString(root, "tagObjectId"));
}

static IReadOnlyList<Projection> ReadProjections(string projectionsPath)
{
    using JsonDocument document = JsonDocument.Parse(ReadBounded(projectionsPath), new JsonDocumentOptions { MaxDepth = 16 });
    JsonElement root = document.RootElement;
    if (root.ValueKind != JsonValueKind.Object || RequiredString(root, "schemaVersion") != "release-publication-projection.v1")
    {
        throw new DriftException("drift_projection_schema_invalid");
    }

    if (!root.TryGetProperty("projections", out JsonElement array) || array.ValueKind != JsonValueKind.Array) throw new DriftException("drift_projection_schema_invalid");
    if (array.GetArrayLength() > MaximumProjections) throw new DriftException("drift_projection_count_exceeded");

    var results = new List<Projection>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (JsonElement item in array.EnumerateArray())
    {
        if (item.ValueKind != JsonValueKind.Object) throw new DriftException("drift_projection_schema_invalid");
        string providerId = RequiredString(item, "providerId");
        if (!ProviderIdPattern().IsMatch(providerId) || !seen.Add(providerId)) throw new DriftException("drift_projection_provider_invalid");

        string state = RequiredString(item, "state");
        if (state is not ("published" or "unavailable" or "unsupported")) throw new DriftException("drift_projection_state_invalid");

        string? bodySha256 = OptionalString(item, "publishedBodySha256");
        string? bodyText = OptionalString(item, "publishedBody");
        if (bodySha256 is not null && !Sha256Pattern().IsMatch(bodySha256)) throw new DriftException("drift_projection_digest_invalid");

        results.Add(new Projection(
            providerId,
            state,
            OptionalString(item, "declaredCanonicalNotesSha256"),
            OptionalString(item, "declaredTagRef"),
            bodySha256,
            bodyText,
            ReadStringArrayOrEmpty(item, "assets"),
            OptionalString(item, "operatorEvidenceReference")));
    }

    return results;
}

static IReadOnlyList<ProjectionResult> Evaluate(ReleaseIdentity identity, IReadOnlyList<Projection> projections)
{
    var results = new List<ProjectionResult>();
    foreach (Projection projection in projections.OrderBy(item => item.ProviderId, StringComparer.Ordinal))
    {
        results.Add(EvaluateProjection(identity, projection));
    }

    return results;
}

static ProjectionResult EvaluateProjection(ReleaseIdentity identity, Projection projection)
{
    // A provider that has no release API, or a forge that is simply down, degrades to a recorded
    // no-op backed by operator evidence. It is not a failed release: the tag already closed it.
    if (projection.State is "unavailable" or "unsupported")
    {
        return projection.OperatorEvidenceReference is null or ""
            ? new ProjectionResult(projection.ProviderId, "drift", ["recorded_no_op_missing_operator_evidence"])
            : new ProjectionResult(projection.ProviderId, "recorded-no-op", []);
    }

    var findings = new List<string>();
    if (!string.Equals(projection.DeclaredCanonicalNotesSha256, identity.NotesSha256, StringComparison.Ordinal))
    {
        findings.Add("declared_canonical_notes_sha256_mismatch");
    }

    if (!string.Equals(projection.DeclaredTagRef, identity.TagRef, StringComparison.Ordinal))
    {
        findings.Add("declared_tag_reference_mismatch");
    }

    foreach (string asset in RequiredAssets())
    {
        if (!projection.Assets.Contains(asset, StringComparer.Ordinal)) findings.Add($"required_asset_missing:{asset}");
    }

    string? observedBodySha256 = projection.PublishedBody is null ? projection.PublishedBodySha256 : Sha256(Encoding.UTF8.GetBytes(projection.PublishedBody));
    if (observedBodySha256 is null)
    {
        findings.Add("published_body_not_supplied");
    }
    else if (projection.PublishedBody is not null)
    {
        // The page is a projection, not a copy, so it is not required to equal the canonical bytes.
        // What it must do is carry the canonical hash and the tag reference verbatim, so any reader
        // can check the page against the repository without trusting the forge.
        if (!projection.PublishedBody.Contains(identity.NotesSha256, StringComparison.Ordinal)) findings.Add("published_body_missing_canonical_notes_sha256");
        if (!projection.PublishedBody.Contains(identity.TagRef, StringComparison.Ordinal)) findings.Add("published_body_missing_tag_reference");
    }

    return new ProjectionResult(projection.ProviderId, findings.Count == 0 ? "in-sync" : "drift", findings);
}

static void WriteReport(string outputDirectory, ReleaseIdentity identity, IReadOnlyList<ProjectionResult> results)
{
    Directory.CreateDirectory(outputDirectory);

    // Reflection-based serialization is disabled repository-wide for file-based apps, and the
    // report is a stable machine-read contract, so it is written explicitly.
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", "publication-drift-report.v1");
        writer.WriteStartObject("canonical");
        writer.WriteString("version", identity.Version);
        writer.WriteString("tagName", identity.TagName);
        writer.WriteString("tagRef", identity.TagRef);
        writer.WriteString("tagObjectId", identity.TagObjectId);
        writer.WriteString("releaseNotesSha256", identity.NotesSha256);
        writer.WriteEndObject();
        writer.WriteBoolean("autoRepair", false);
        writer.WriteBoolean("releaseInvalidated", false);
        writer.WriteStartArray("projections");
        foreach (ProjectionResult result in results)
        {
            writer.WriteStartObject();
            writer.WriteString("providerId", result.ProviderId);
            writer.WriteString("status", result.Status);
            writer.WriteStartArray("findings");
            foreach (string finding in result.Findings) writer.WriteStringValue(finding);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    string json = Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n");
    File.WriteAllText(Path.Combine(outputDirectory, "publication-drift-report.v1.json"), json + "\n", new UTF8Encoding(false));

    var markdown = new StringBuilder();
    markdown.Append(CultureInfo.InvariantCulture, $"# Publication Drift Report — {identity.TagName}\n\n");
    markdown.Append(CultureInfo.InvariantCulture, $"Canonical notes SHA-256: `{identity.NotesSha256}`\n\n");
    markdown.Append(CultureInfo.InvariantCulture, $"Tag reference: `{identity.TagRef}` (tag object `{identity.TagObjectId}`)\n\n");
    markdown.Append("Published pages are a noncanonical projection. Drift is reported, never repaired, and never invalidates the release.\n\n");
    markdown.Append("| Provider | Status | Findings |\n|---|---|---|\n");
    foreach (ProjectionResult result in results)
    {
        string findings = result.Findings.Count == 0 ? "none" : string.Join("; ", result.Findings);
        markdown.Append(CultureInfo.InvariantCulture, $"| {result.ProviderId} | {result.Status} | {findings} |\n");
    }

    File.WriteAllText(Path.Combine(outputDirectory, "publication-drift-report.md"), markdown.ToString(), new UTF8Encoding(false));
}

static IReadOnlyList<string> RequiredAssets() => ["release-evidence.v1.json", "artifacts.sha256", "container-image-digests.json", "sbom.spdx.json"];

static byte[] ReadBounded(string path)
{
    var info = new FileInfo(path);
    if (!info.Exists || info.Length > MaximumBodyBytes) throw new DriftException("drift_input_too_large");
    return File.ReadAllBytes(path);
}

static string RequiredString(JsonElement element, string key) =>
    element.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
        ? text
        : throw new DriftException("drift_projection_schema_invalid");

static string? OptionalString(JsonElement element, string key) =>
    element.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

static IReadOnlyList<string> ReadStringArrayOrEmpty(JsonElement element, string key)
{
    if (!element.TryGetProperty(key, out JsonElement array)) return [];
    if (array.ValueKind != JsonValueKind.Array) throw new DriftException("drift_projection_schema_invalid");
    return array.EnumerateArray()
        .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : throw new DriftException("drift_projection_schema_invalid"))
        .ToArray();
}

static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

static Regex ProviderIdPattern() => Patterns.ProviderId();
static Regex Sha256Pattern() => Patterns.Sha256();

internal sealed record Options(string ReleaseDirectory, string ProjectionsPath, string OutputDirectory, bool FailOnDrift);
internal sealed record ReleaseIdentity(string Version, string TagName, string TagRef, string NotesSha256, string TagObjectId);
internal sealed record Projection(
    string ProviderId,
    string State,
    string? DeclaredCanonicalNotesSha256,
    string? DeclaredTagRef,
    string? PublishedBodySha256,
    string? PublishedBody,
    IReadOnlyList<string> Assets,
    string? OperatorEvidenceReference);
internal sealed record ProjectionResult(string ProviderId, string Status, IReadOnlyList<string> Findings);

internal sealed class DriftException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

internal static partial class Patterns
{
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{1,63}$", RegexOptions.CultureInvariant, 100)]
    public static partial Regex ProviderId();

    [GeneratedRegex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant, 100)]
    public static partial Regex Sha256();
}
