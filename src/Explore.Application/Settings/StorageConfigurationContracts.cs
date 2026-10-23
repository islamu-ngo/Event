using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Settings;

public interface IStorageConfigurationAuthority
{
    Task<IStorageConfigurationSource> CaptureAsync(object command, Guid? tenantId, CancellationToken token);
    void RequireIssued(IStorageConfigurationSource source);
}

/// <summary>Privately issued original request custody; it is not a transferable administrator grant.</summary>
public interface IStorageConfigurationSource
{
    object OriginalCommand { get; }
    Guid Actor { get; }
    Guid? TenantId { get; }
    Guid AmbientTenant { get; }
    HumanCommitSubject Subject { get; }
    HumanAuthenticationLifetime Lifetime { get; }
    Task<bool> ObserveProviderAsync(StorageConfigurationState state, CancellationToken token);
    Task RequireExternalSourcesAsync(CancellationToken token);
    Task RequireSourcesAsync(CancellationToken token);
    void RequireTime();
}

public interface IStorageConfigurationWriter
{
    void RequireIdle();
    Task<StorageConfigurationState> ObserveAsync(IStorageConfigurationSource source, IUnitOfWork owner, CancellationToken token);
    DateTime RepresentTimestamp(DateTime timestamp);
    Task RequireUnchangedAsync(StorageConfigurationState original, CancellationToken token);
    Task ExecuteAsync(StorageSettingsMutation mutation, CancellationToken token);
    Task RequireAppliedAsync(StorageSettingsMutation mutation, CancellationToken token);
}

/// <summary>
/// Complete persisted setting state. Tenant rows have no type/description/category/order
/// columns, represented by null rather than invented metadata.
/// </summary>
public sealed record StorageConfigurationRow(Guid? TenantId, string Key, Guid Id, string Value,
    SettingValueType? ValueType, bool IsLocked, string? AllowedValues, string? Description,
    string? Category, int? DisplayOrder, DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    public static StorageConfigurationRow From(SystemSetting row) => new(null, row.SettingKey, row.Id,
        row.Value, row.ValueType, row.IsLocked, row.AllowedValues, row.Description, row.Category,
        row.DisplayOrder, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
    public static StorageConfigurationRow From(TenantSetting row) => new(row.TenantId, row.SettingKey,
        row.Id, row.Value, null, row.IsLocked, null, null, null, null,
        row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
}

/// <summary>Includes explicit absence for every key and all relevant binding rows, not just winners.</summary>
public sealed record StorageConfigurationState(
    ImmutableDictionary<string, StorageConfigurationRow?> System,
    ImmutableDictionary<string, StorageConfigurationRow?> Tenant, string Bindings, long Revision)
{
    public ResolvedSetting? Resolve(string key, bool tenant) => StorageConfigurationProjection.Resolve(this, key, tenant);
    public bool TenantOverridesAllowed => StorageConfigurationProjection.TenantAllowed(this);
    public static ImmutableArray<string> Keys { get; } =
    [
        GovernanceSettingKeys.Storage.Provider, GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
        GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
        GovernanceSettingKeys.TenantDelegation.LockStorage, GovernanceSettingKeys.Storage.RouteMatrix,
        GovernanceSettingKeys.Storage.Endpoint, GovernanceSettingKeys.Storage.PublicEndpoint,
        GovernanceSettingKeys.Storage.BucketName, GovernanceSettingKeys.Storage.Region,
        GovernanceSettingKeys.Storage.ForcePathStyle, GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes,
        GovernanceSettingKeys.Deployment.Mode
    ];

    public bool Same(StorageConfigurationState other) => Revision == other.Revision
        && Keys.All(key => System[key] == other.System[key] && Tenant[key] == other.Tenant[key])
        && JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(Bindings),
            JsonSerializer.Deserialize<JsonElement>(other.Bindings));

    public void RequireShape(Guid ambientTenant)
    {
        if (System.Count != Keys.Length || Tenant.Count != Keys.Length || Revision < 0
            || Keys.Any(key => !System.ContainsKey(key) || !Tenant.ContainsKey(key)
                || System[key] is { } system && (system.Key != key || system.TenantId is not null)
                || Tenant[key] is { } tenant && (tenant.Key != key || tenant.TenantId != ambientTenant)))
            throw new UnauthorizedAccessException("Storage configuration requires complete original sources.");
    }
}
