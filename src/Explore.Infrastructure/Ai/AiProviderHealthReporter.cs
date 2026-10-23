using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

public sealed class AiProviderHealthReporter(
    AiProviderSettingsValidator validator,
    IAiProviderStrategyResolver resolver,
    AiProviderConfigurationResolver configuration)
{
    public async Task<AiProviderHealth> CheckAsync(AiProviderSettings settings, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object> data = new(StringComparer.Ordinal)
        {
            ["enabled"] = settings.Enabled,
            ["provider"] = settings.Provider,
            ["endpointConfigured"] = !string.IsNullOrWhiteSpace(settings.EndpointUrl),
            ["credentialConfigured"] = false,
            ["modelConfigured"] = !string.IsNullOrWhiteSpace(settings.ModelId),
            ["toolProposalsEnabled"] = settings.ToolProposalsEnabled,
            ["streamingEnabled"] = settings.StreamingEnabled,
            ["timeoutSeconds"] = settings.TimeoutSeconds,
            ["localProviderEndpointsAllowed"] = settings.AllowLocalProviderEndpoints
        };

        var validation = validator.Validate(null, settings);
        if (!validation.Succeeded)
        {
            data["reason"] = "invalid_settings";
            return new AiProviderHealth(
                Enabled: settings.Enabled,
                Healthy: false,
                Status: "invalid_settings",
                Description: "AI provider settings are invalid.",
                Data: data);
        }

        if (!settings.Enabled)
        {
            data["reason"] = "disabled";
            return new AiProviderHealth(
                Enabled: false,
                Healthy: true,
                Status: "healthy_disabled",
                Description: "AI provider integration is intentionally disabled.",
                Data: data);
        }

        var strategy = resolver.Resolve(settings.Provider);
        if (strategy is null)
        {
            data["reason"] = "provider_not_configured";
            return new AiProviderHealth(
                Enabled: true,
                Healthy: false,
                Status: "provider_not_configured",
                Description: "AI provider integration is enabled but no runnable provider is configured.",
                Data: data);
        }
        var resolved = await configuration.ResolveInstanceAsync(settings, cancellationToken);
        if (resolved.Configuration is null)
        {
            data["reason"] = resolved.FailureCode!;
            return new AiProviderHealth(true, false, resolved.FailureCode!,
                "AI provider configuration or credential is unavailable.", data);
        }
        data["credentialConfigured"] = !string.IsNullOrEmpty(resolved.Configuration.ApiKey);

        return strategy.CheckHealth(data);
    }

}
