using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Domain.ValueObjects;

namespace Explore.Application.Services;

public sealed class EventResourceGovernancePolicyReader(
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings) : IEventResourceGovernancePolicyReader
{
    private static readonly string[] Keys = EventResourceSettingDefinitions.All.Select(value => value.Key)
        .Concat([
            GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
            GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
            GovernanceSettingKeys.TenantDelegation.LockStorage,
            GovernanceSettingKeys.Deployment.Mode
        ]).ToArray();

    public async Task<EventResourceGovernancePolicy?> ReadAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) return null;
        var systems = new Dictionary<string, SystemSetting>(StringComparer.Ordinal);
        foreach (string key in Keys)
        {
            var row = await systemSettings.GetByKey(key, cancellationToken);
            if (row is not null) systems.Add(key, row);
        }
        var tenants = (await tenantSettings.GetByTenantAndKeys(tenantId, Keys, cancellationToken))
            .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        try
        {
            string Value(string key, bool includeTenant) =>
                HierarchicalSettingMerge.Resolve(key, systems, includeTenant ? tenants : null)?.Value
                    ?? throw new JsonException("Required policy definition is unavailable.");
            long Positive(string key, bool includeTenant)
            {
                long value = JsonSerializer.Deserialize<long>(Value(key, includeTenant));
                return value > 0 ? value : throw new JsonException("Storage limits must be positive.");
            }
            string? mode = JsonSerializer.Deserialize<string>(Value(GovernanceSettingKeys.Deployment.Mode, false));
            if (!Enum.TryParse<DeploymentMode>(mode, ignoreCase: true, out var deployment) || !Enum.IsDefined(deployment))
                return null;
            bool delegated = deployment == DeploymentMode.SingleTenant
                || !JsonSerializer.Deserialize<bool>(Value(GovernanceSettingKeys.TenantDelegation.LockStorage, false));
            long storageCeiling = Math.Min(Positive(GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, false),
                Positive(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, delegated));
            var instanceValues = EventResourceSettingDefinitions.All.ToDictionary(
                definition => definition.Key, definition => Value(definition.Key, false), StringComparer.Ordinal);
            var tenantValues = EventResourceSettingDefinitions.All.ToDictionary(
                definition => definition.Key, definition => Value(definition.Key, true), StringComparer.Ordinal);
            return EventResourceGovernancePolicyValues.Parse(instanceValues, storageCeiling)
                .Intersect(EventResourceGovernancePolicyValues.Parse(tenantValues, storageCeiling));
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
