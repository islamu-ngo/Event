namespace Explore.Application.Features.ConfigurationManifest.Application;

using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Explore.Application.Features.ConfigurationManifest.Catalog;
using Explore.Application.Features.ConfigurationManifest.Validation;
using Explore.Domain;

/// <summary>Non-secret, immutable post-commit instructions derived from an applied bootstrap receipt.</summary>
public sealed record ConfigurationManifestEffectPayload(
    int Version,
    DateTime NotificationTimeUtc,
    ImmutableArray<string> InstanceSettingKeys,
    ImmutableArray<ConfigurationManifestTenantEffect> Tenants)
{
    public const int CurrentVersion = 1;
    public const int MaximumPayloadByteCount = 2 * 1024 * 1024;

    private static readonly ConfigurationManifestEffectJsonContext Json = new(
        new JsonSerializerOptions(JsonSerializerOptions.Strict)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            MaxDepth = 8
        });

    public static string Create(ConfigurationManifestOperation operation,
        IReadOnlyCollection<ConfigurationManifestTenantResult> results)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(results);
        if (operation.Mode != ConfigurationManifestAuditMode.Bootstrap
            || operation.Status != ConfigurationManifestOperationStatus.Applied
            || operation.EffectOutboxId is not { } effectId || !IsAggregateId(effectId)
            || results.Count != operation.RequestedTenantCount
            || results.Count > ConfigurationManifestValidator.MaximumTenantCount
            || operation.FailedTenantCount != 0
            || results.Any(result => result is null || result.OperationId != operation.Id
                || result.CompletedAt != operation.CompletedAt)
            || results.Select(result => result.TenantId).Distinct().Count() != results.Count
            || results.Count(result => result.Status == ConfigurationManifestTenantResultStatus.Created) != operation.CreatedTenantCount
            || results.Count(result => result.Status == ConfigurationManifestTenantResultStatus.SkippedExisting) != operation.SkippedExistingTenantCount)
            throw new InvalidOperationException("Manifest effects require the complete original applied receipt.");

        var payload = new ConfigurationManifestEffectPayload(CurrentVersion, operation.StartedAt,
            [.. operation.InstanceChangedSettingKeyNames],
            [.. results.Where(result => result.Status == ConfigurationManifestTenantResultStatus.Created)
                .OrderBy(result => result.TenantId)
                .Select(result => new ConfigurationManifestTenantEffect(result.TenantId, [.. result.ChangedSettingKeyNames]))]);
        Validate(payload, operation.CompletedAt);
        string json = JsonSerializer.Serialize(payload, Json.ConfigurationManifestEffectPayload);
        RequireBounded(json);
        return json;
    }

    public static ConfigurationManifestEffectPayload Read(string? payload, DateTime completedAtUtc)
    {
        RequireBounded(payload);
        var instructions = JsonSerializer.Deserialize(payload!, Json.ConfigurationManifestEffectPayload)
            ?? throw new JsonException("Manifest effect instructions are required.");
        Validate(instructions, completedAtUtc);
        return instructions;
    }

    private static void RequireBounded(string? payload)
    {
        if (string.IsNullOrEmpty(payload) || payload.Length > MaximumPayloadByteCount
            || Encoding.UTF8.GetByteCount(payload) > MaximumPayloadByteCount)
            throw new JsonException("Manifest effect instructions exceed their bounded contract.");
    }

    private static void Validate(ConfigurationManifestEffectPayload payload, DateTime completedAtUtc)
    {
        if (payload.Version != CurrentVersion || payload.NotificationTimeUtc == default
            || payload.NotificationTimeUtc.Kind != DateTimeKind.Utc
            || completedAtUtc == default || completedAtUtc.Kind != DateTimeKind.Utc
            || payload.NotificationTimeUtc > completedAtUtc
            || payload.Tenants.IsDefault || payload.Tenants.Length > ConfigurationManifestValidator.MaximumTenantCount)
            throw new JsonException("Manifest effect version, time or audience is invalid.");
        ValidateKeys(payload.InstanceSettingKeys, instance: true);
        Guid previous = Guid.Empty;
        foreach (var tenant in payload.Tenants)
        {
            if (tenant is null || !IsAggregateId(tenant.TenantId) || previous.CompareTo(tenant.TenantId) >= 0)
                throw new JsonException("Manifest effect tenants must be unique canonical aggregate identities in order.");
            previous = tenant.TenantId;
            ValidateKeys(tenant.SettingKeys, instance: false);
        }
    }

    private static void ValidateKeys(ImmutableArray<string> keys, bool instance)
    {
        var catalog = instance ? ConfigurationManifestCatalog.InstanceSettings : ConfigurationManifestCatalog.TenantSettings;
        if (keys.IsDefault || keys.Length > catalog.Count)
            throw new JsonException("Manifest effect setting keys are invalid.");
        string? previous = null;
        foreach (string? key in keys)
        {
            if (key is null || !catalog.ContainsKey(key)
                || previous is not null && StringComparer.Ordinal.Compare(previous, key) >= 0)
                throw new JsonException("Manifest effect keys must be unique closed-catalog identifiers in order.");
            previous = key;
        }
    }

    private static bool IsAggregateId(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
}

public sealed record ConfigurationManifestTenantEffect(Guid TenantId, ImmutableArray<string> SettingKeys);

[JsonSerializable(typeof(ConfigurationManifestEffectPayload))]
internal sealed partial class ConfigurationManifestEffectJsonContext : JsonSerializerContext;
