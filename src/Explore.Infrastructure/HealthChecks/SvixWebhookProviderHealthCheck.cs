using Explore.Application.Contracts.Secrets;
using Explore.Application.Lookups;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.HealthChecks;

public sealed class SvixWebhookProviderHealthCheck(
    IOptionsMonitor<WebhookOptions> options,
    IOptions<WebhookProviderPublicationProcessorSettings> processorSettings,
    IServiceScopeFactory scopeFactory,
    BusinessMetrics metrics) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var currentOptions = options.CurrentValue;
        var svixSelected = !currentOptions.IsDisabled
            && (currentOptions.IsProvider(WebhookOptions.ProviderSvix)
                || currentOptions.IsProvider(WebhookOptions.ProviderComposite));
        var data = new Dictionary<string, object>
        {
            ["enabled"] = currentOptions.Enabled,
            ["provider"] = currentOptions.Provider,
            ["svixProviderSelected"] = svixSelected,
            ["baseUrlConfigured"] = !string.IsNullOrWhiteSpace(currentOptions.Svix.BaseUrl),
            ["environment"] = currentOptions.Svix.Environment,
            ["providerVersion"] = currentOptions.Svix.ProviderVersion,
            ["capabilityPolicyVersion"] = currentOptions.Svix.CapabilityPolicyVersion,
            ["appPortalEnabled"] = currentOptions.Svix.AppPortalEnabled,
            ["syncEventTypesOnStartup"] = currentOptions.Svix.SyncEventTypesOnStartup,
            ["processorEnabled"] = processorSettings.Value.Enabled,
            ["authTokenBindingConfigured"] = !string.IsNullOrWhiteSpace(currentOptions.Svix.AuthTokenSecretRef),
            ["operationalWebhookBindingConfigured"] = !string.IsNullOrWhiteSpace(currentOptions.Svix.OperationalWebhookSecretRef)
        };

        if (!svixSelected)
        {
            return Report(
                HealthCheckResult.Healthy(
                    "Svix webhook provider is not the selected outgoing provider.",
                    data),
                WebhookTelemetryOutcome.NotSelected);
        }

        if (!processorSettings.Value.Enabled)
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "Svix webhook provider publication processor is disabled.",
                    data: data),
                WebhookTelemetryOutcome.Disabled);
        }

        if (!SvixConformanceProfileRegistry.TryResolve(
                currentOptions.Svix.Environment,
                currentOptions.Svix.ProviderVersion,
                currentOptions.Svix.CapabilityPolicyVersion,
                !string.IsNullOrWhiteSpace(currentOptions.Svix.BaseUrl),
                out var profile))
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "The selected Svix deployment profile is absent from the conformance matrix.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        if (profile is null)
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "The selected Svix deployment profile could not be resolved.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        data["deploymentKind"] = profile.DeploymentKind.ToString();
        data["conformanceEvidenceRevision"] = profile.EvidenceRevision;
        data["conformanceExecutedTestCount"] = profile.ExecutedTestCount;
        data["exactMessageLookupSupported"] = profile.SupportsExactMessageLookup;
        data["providerCapabilityCount"] = CountIndividualCapabilities(profile.Capabilities);
        data["providerCapabilityCodes"] = ResolveCapabilityCodes(profile.Capabilities);
        if (!profile.IsVerified)
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "The selected Svix deployment profile has no executed conformance evidence.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        if (currentOptions.Svix.AppPortalEnabled &&
            !Supports(profile.Capabilities, WebhookProviderCapability.AppPortal))
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "The selected Svix profile does not prove the enabled App Portal capability.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        if (currentOptions.Svix.SyncEventTypesOnStartup &&
            !Supports(profile.Capabilities, WebhookProviderCapability.EventCatalog))
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "The selected Svix profile does not prove the enabled event catalog capability.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        var authTokenSettingKey = currentOptions.Svix.AuthTokenSecretRef?.Trim();
        if (string.IsNullOrWhiteSpace(authTokenSettingKey))
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "Svix webhook auth token binding is not configured.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var secretResolver = scope.ServiceProvider.GetRequiredService<ISecretResolver>();
        SecretResolutionResult authToken;
        try
        {
            authToken = await secretResolver.ResolveAsync(authTokenSettingKey, tenantId: null, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "Svix webhook auth token resolution failed.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }
        var authTokenResolved = authToken.IsResolved && !string.IsNullOrWhiteSpace(authToken.Value);
        data["authTokenResolved"] = authTokenResolved;
        data["authTokenState"] = authToken.Status.ToString();
        if (!authTokenResolved)
        {
            return Report(
                HealthCheckResult.Unhealthy(
                    "Svix webhook auth token could not be resolved.",
                    data: data),
                WebhookTelemetryOutcome.Unhealthy);
        }

        var operationalWebhookSettingKey = currentOptions.Svix.OperationalWebhookSecretRef?.Trim();
        if (!string.IsNullOrWhiteSpace(operationalWebhookSettingKey))
        {
            try
            {
                var operationalSecret = await secretResolver.ResolveAsync(
                    operationalWebhookSettingKey,
                    tenantId: null,
                    cancellationToken);
                data["operationalWebhookSecretResolved"] = operationalSecret.IsResolved;
                data["operationalWebhookSecretState"] = operationalSecret.Status.ToString();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                data["operationalWebhookSecretResolved"] = false;
                data["operationalWebhookSecretState"] = SecretResolutionStatus.Unavailable.ToString();
            }
        }

        return Report(
            HealthCheckResult.Healthy(
                "Svix webhook provider configuration is ready.",
                data),
            WebhookTelemetryOutcome.Healthy);
    }

    private HealthCheckResult Report(
        HealthCheckResult result,
        WebhookTelemetryOutcome outcome)
    {
        metrics.RecordWebhookProviderHealthCheck(WebhookTelemetryProvider.Svix, outcome);
        return result;
    }

    private static string[] ResolveCapabilityCodes(WebhookProviderCapability capabilities) =>
        Enum.GetValues<WebhookProviderCapability>()
            .Where(capability => capability != WebhookProviderCapability.None &&
                IsSingleFlag(capability) &&
                Supports(capabilities, capability))
            .Select(capability =>
                NormalizedLookupMetadata.WebhookProviderCapability((int)capability).Code)
            .ToArray();

    private static int CountIndividualCapabilities(WebhookProviderCapability capabilities) =>
        ResolveCapabilityCodes(capabilities).Length;

    private static bool Supports(
        WebhookProviderCapability available,
        WebhookProviderCapability required) =>
        (available & required) == required;

    private static bool IsSingleFlag(WebhookProviderCapability capability)
    {
        var value = (long)capability;
        return (value & (value - 1)) == 0;
    }
}
