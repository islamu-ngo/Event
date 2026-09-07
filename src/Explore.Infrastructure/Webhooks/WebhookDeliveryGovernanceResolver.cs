using System.Globalization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Webhooks;

public sealed class WebhookDeliveryGovernanceResolver(
    IHierarchicalSettingsResolver settingsResolver,
    IOptions<WebhookDeliveryProcessorSettings> processorSettings,
    IOptionsMonitor<WebhookOptions> webhookOptions) : IWebhookDeliveryGovernanceResolver
{
    private readonly WebhookDeliveryProcessorSettings _processorSettings = processorSettings.Value;

    public async Task<WebhookDeliveryGovernancePolicy> ResolveAsync(
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var governed = await settingsResolver.ResolveGroupAsync<WebhookDeliverySettingGroup>(
            new SettingContext(TenantId: tenantId),
            cancellationToken);
        var local = webhookOptions.CurrentValue.Local;

        var globalLimit = Math.Min(
            governed.MaxConcurrentDeliveries,
            _processorSettings.MaxConcurrentDeliveries);
        var tenantLimit = Math.Min(
            Math.Min(governed.MaxConcurrentDeliveriesPerTenant, _processorSettings.MaxConcurrentDeliveriesPerTenant),
            globalLimit);
        var endpointLimit = Math.Min(
            Math.Min(governed.MaxConcurrentDeliveriesPerEndpoint, _processorSettings.MaxConcurrentDeliveriesPerEndpoint),
            tenantLimit);
        var itemsPerCycle = Math.Min(
            governed.MaxItemsPerTenantPerClaimCycle,
            _processorSettings.MaxItemsPerTenantPerClaimCycle);
        var maxAttempts = Math.Min(governed.MaxAttempts, local.MaxAttempts);
        var endpointTimeoutSeconds = Math.Min(governed.EndpointTimeoutSeconds, local.TimeoutSeconds);

        var resolutionVersion = string.Create(
            CultureInfo.InvariantCulture,
            $"webhook-delivery-v1:g{globalLimit}:t{tenantLimit}:e{endpointLimit}:c{itemsPerCycle}:a{maxAttempts}:o{endpointTimeoutSeconds}:p{governed.AutoPauseThreshold}");

        return new WebhookDeliveryGovernancePolicy(
            globalLimit,
            tenantLimit,
            endpointLimit,
            itemsPerCycle,
            maxAttempts,
            endpointTimeoutSeconds,
            governed.AutoPauseThreshold,
            resolutionVersion);
    }
}
