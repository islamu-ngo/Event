using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ISLAMU.DependencySubmission;

public enum DependencyRelationship { Direct, Indirect }
public enum DependencyScope { Runtime, Development }
public enum LockDependencyKind { Direct, Transitive, CentralTransitive, Project }

/// <summary>Explicit protocol vocabulary, independent of CLR enum naming.</summary>
public static class WireTokens
{
    public static FrozenDictionary<DependencyRelationship, string> Relationships { get; } =
        new Dictionary<DependencyRelationship, string>
        {
            [DependencyRelationship.Direct] = "direct",
            [DependencyRelationship.Indirect] = "indirect"
        }.ToFrozenDictionary();

    public static FrozenDictionary<DependencyScope, string> Scopes { get; } =
        new Dictionary<DependencyScope, string>
        {
            [DependencyScope.Runtime] = "runtime",
            [DependencyScope.Development] = "development"
        }.ToFrozenDictionary();

    public static FrozenDictionary<string, LockDependencyKind> LockKinds { get; } =
        new Dictionary<string, LockDependencyKind>(StringComparer.Ordinal)
        {
            ["Direct"] = LockDependencyKind.Direct,
            ["Transitive"] = LockDependencyKind.Transitive,
            ["CentralTransitive"] = LockDependencyKind.CentralTransitive,
            ["Project"] = LockDependencyKind.Project
        }.ToFrozenDictionary(StringComparer.Ordinal);
}

public sealed record SnapshotDependency(
    [property: JsonPropertyName("package_url")] Uri PackageUrl,
    [property: JsonPropertyName("relationship")] DependencyRelationship Relationship,
    [property: JsonPropertyName("scope")] DependencyScope Scope,
    [property: JsonPropertyName("dependencies")] ImmutableArray<Uri> Dependencies);

public sealed record ManifestFile([property: JsonPropertyName("source_location")] string SourceLocation);
public sealed record ManifestMetadata([property: JsonPropertyName("target_framework")] string TargetFramework);
public sealed record SnapshotManifest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("file")] ManifestFile File,
    [property: JsonPropertyName("metadata")] ManifestMetadata Metadata,
    [property: JsonPropertyName("resolved")] ImmutableSortedDictionary<string, SnapshotDependency> Resolved);
public sealed record SnapshotJob(
    [property: JsonPropertyName("id"), JsonNumberHandling(JsonNumberHandling.WriteAsString)] long Id,
    [property: JsonPropertyName("correlator")] string Correlator,
    [property: JsonPropertyName("html_url")] Uri HtmlUrl);
public sealed record SnapshotDetector(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("url")] Uri Url);
public sealed record DependencySnapshot(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("sha")] string Sha,
    [property: JsonPropertyName("ref")] string Ref,
    [property: JsonPropertyName("job")] SnapshotJob Job,
    [property: JsonPropertyName("detector")] SnapshotDetector Detector,
    [property: JsonPropertyName("scanned")] DateTimeOffset Scanned,
    [property: JsonPropertyName("manifests")] ImmutableSortedDictionary<string, SnapshotManifest> Manifests);
public sealed record SnapshotContext(string Sha, string Ref, SnapshotJob Job, DateTimeOffset Scanned);
public sealed record LockInput(string Path, string Json);

public static class SnapshotJson
{
    private static JsonSerializerOptions Options { get; } = CreateOptions();
    public static byte[] Serialize(DependencySnapshot snapshot) => JsonSerializer.SerializeToUtf8Bytes(snapshot, Options);
    public static byte[] Serialize(ValidationReport report) => JsonSerializer.SerializeToUtf8Bytes(report, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = false };
        options.Converters.Add(new RelationshipConverter());
        options.Converters.Add(new ScopeConverter());
        options.Converters.Add(new ValidationFailureConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class RelationshipConverter : JsonConverter<DependencyRelationship>
    {
        public override DependencyRelationship Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var token = reader.GetString();
            foreach (var pair in WireTokens.Relationships)
                if (pair.Value == token) return pair.Key;
            throw new JsonException("Unknown relationship token.");
        }
        public override void Write(Utf8JsonWriter writer, DependencyRelationship value, JsonSerializerOptions options) =>
            writer.WriteStringValue(WireTokens.Relationships[value]);
    }

    private sealed class ScopeConverter : JsonConverter<DependencyScope>
    {
        public override DependencyScope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var token = reader.GetString();
            foreach (var pair in WireTokens.Scopes)
                if (pair.Value == token) return pair.Key;
            throw new JsonException("Unknown scope token.");
        }
        public override void Write(Utf8JsonWriter writer, DependencyScope value, JsonSerializerOptions options) =>
            writer.WriteStringValue(WireTokens.Scopes[value]);
    }

    private sealed class ValidationFailureConverter : JsonConverter<SnapshotValidationFailure>
    {
        public override SnapshotValidationFailure Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var token = reader.GetString();
            foreach (var pair in SnapshotValidationException.Codes)
                if (pair.Value == token) return pair.Key;
            throw new JsonException("Unknown validation failure token.");
        }
        public override void Write(Utf8JsonWriter writer, SnapshotValidationFailure value, JsonSerializerOptions options) =>
            writer.WriteStringValue(SnapshotValidationException.Codes[value]);
    }
}
