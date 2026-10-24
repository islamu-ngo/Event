using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Domain;

namespace Explore.Application.Services.Moderation;

/// <summary>
/// Immutable approved execution contract. Exact membership is necessary for measured conformance;
/// parsing these bytes supplies neither independent approval nor evidence that any case ran.
/// </summary>
public sealed record ModerationMeasurementCatalogue(
    int SchemaVersion, string ConsumerId, string RuntimeVersion, string ProviderCode,
    string ProviderVersion, string ConfigurationDigest, string ProductArtifactDigest,
    ImmutableArray<ModerationMeasurementCatalogueJob> Jobs)
{
    public const int MaximumBytes = 1024 * 1024;
    public static ImmutableArray<string> Bundles { get; } =
        ["Event.Domain.UnitTests", "Event.API.IntegrationTests", "Event.Persistence.IntegrationTests"];

    [JsonIgnore]
    public ImmutableArray<string> CaseIds => Jobs.SelectMany(job => job.Cases)
        .Select(item => item.CaseId).Order(StringComparer.Ordinal).ToImmutableArray();

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
        MaxDepth = 12
    };

    public static ModerationMeasurementCatalogue Parse(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumBytes) throw Invalid();
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 12 });
        Unique(document.RootElement);
        var value = JsonSerializer.Deserialize<ModerationMeasurementCatalogue>(bytes.Span, Options);
        if (value is null || value.SchemaVersion != 1 || !ModerationConsumerInstallation.IsConsumerId(value.ConsumerId)
            || !Identity(value.RuntimeVersion, 64) || !Identity(value.ProviderVersion, 256)
            || value.ProviderCode is not ("PostgreSql" or "Sqlite" or "SqlServer" or "MariaDb" or "MySql")
            || !IsDigest(value.ConfigurationDigest) || !IsDigest(value.ProductArtifactDigest)
            || value.Jobs.IsDefault || value.Jobs.Length != Bundles.Length)
            throw Invalid();
        var allCases = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Bundles.Length; index++)
        {
            var job = value.Jobs[index];
            if (job is null || job.Bundle != Bundles[index] || !Identity(job.TreeFilter, 8192)
                || job.TreeFilter.Length < 2 || !job.TreeFilter.StartsWith('/')
                || job.Cases.IsDefaultOrEmpty || job.Cases.Length > 256)
                throw Invalid();
            var nodes = new HashSet<string>(StringComparer.Ordinal);
            var journalIds = new HashSet<string>(StringComparer.Ordinal);
            var terminals = new HashSet<(string, string, string)>();
            foreach (var test in job.Cases)
            {
                if (test is null || !Identity(test.CaseId, 256)
                    || !test.CaseId.All(c => c is >= '!' and <= '~') || !allCases.Add(test.CaseId)
                    || !Identity(test.JournalTestId, 4096) || !journalIds.Add(test.JournalTestId)
                    || !Identity(test.NodeUid, 4096) || !nodes.Add(test.NodeUid)
                    || !Identity(test.ClassName, 1024) || !Identity(test.MethodName, 1024)
                    || !Identity(test.DisplayName, 4096) || !Identity(test.MtpClassName, 1024)
                    || !Identity(test.MtpMethodName, 1024) || !Identity(test.MtpTestName, 4096)
                    || !terminals.Add((test.MtpClassName, test.MtpMethodName, test.MtpTestName)))
                    throw Invalid();
            }
        }
        if (allCases.Count > 256) throw Invalid();
        return value;
    }

    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool Identity(string? value, int limit) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= limit && !value.Any(char.IsControl);

    private static void Unique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Invalid();
                Unique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) Unique(item);
    }

    private static JsonException Invalid() => new("The native measurement catalogue contract is invalid.");
}

public sealed record ModerationMeasurementCatalogueJob(
    string Bundle, string TreeFilter, ImmutableArray<ModerationMeasurementCatalogueCase> Cases);

public sealed record ModerationMeasurementCatalogueCase(
    string CaseId, string JournalTestId, string NodeUid, string ClassName, string MethodName,
    string DisplayName, string MtpClassName, string MtpMethodName, string MtpTestName);
