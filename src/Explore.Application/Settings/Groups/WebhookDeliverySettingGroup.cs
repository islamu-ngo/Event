using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Constants;

namespace Explore.Application.Settings.Groups;

public sealed class WebhookDeliverySettingGroup : ISettingGroup
{
    public int MaxConcurrentDeliveries { get; private set; } = 16;
    public int MaxConcurrentDeliveriesPerTenant { get; private set; } = 4;
    public int MaxConcurrentDeliveriesPerEndpoint { get; private set; } = 1;
    public int MaxItemsPerTenantPerClaimCycle { get; private set; } = 10;
    public int MaxAttempts { get; private set; } = 8;
    public int EndpointTimeoutSeconds { get; private set; } = 15;
    public int AutoPauseThreshold { get; private set; } = 5;

    public static IEnumerable<string> SettingKeys =>
    [
        GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries,
        GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerTenant,
        GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerEndpoint,
        GovernanceSettingKeys.WebhookDelivery.MaxItemsPerTenantPerClaimCycle,
        GovernanceSettingKeys.WebhookDelivery.MaxAttempts,
        GovernanceSettingKeys.WebhookDelivery.EndpointTimeoutSeconds,
        GovernanceSettingKeys.WebhookDelivery.AutoPauseThreshold
    ];

    public void Populate(IReadOnlyDictionary<string, ResolvedSetting> settings)
    {
        MaxConcurrentDeliveries = GetInt(
            settings,
            GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveries,
            16,
            1,
            256);
        MaxConcurrentDeliveriesPerTenant = GetInt(
            settings,
            GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerTenant,
            4,
            1,
            MaxConcurrentDeliveries);
        MaxConcurrentDeliveriesPerEndpoint = GetInt(
            settings,
            GovernanceSettingKeys.WebhookDelivery.MaxConcurrentDeliveriesPerEndpoint,
            1,
            1,
            MaxConcurrentDeliveriesPerTenant);
        MaxItemsPerTenantPerClaimCycle = GetInt(
            settings,
            GovernanceSettingKeys.WebhookDelivery.MaxItemsPerTenantPerClaimCycle,
            10,
            1,
            1000);
        MaxAttempts = GetInt(settings, GovernanceSettingKeys.WebhookDelivery.MaxAttempts, 8, 1, 20);
        EndpointTimeoutSeconds = GetInt(
            settings,
            GovernanceSettingKeys.WebhookDelivery.EndpointTimeoutSeconds,
            15,
            1,
            60);
        AutoPauseThreshold = GetInt(
            settings,
            GovernanceSettingKeys.WebhookDelivery.AutoPauseThreshold,
            5,
            1,
            1000);
    }

    private static int GetInt(
        IReadOnlyDictionary<string, ResolvedSetting> settings,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        var value = settings.TryGetValue(key, out var setting)
            ? SettingValueSerializer.Deserialize(setting.Value, defaultValue)
            : defaultValue;
        return Math.Clamp(value, minimum, maximum);
    }
}
