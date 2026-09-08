using Explore.Application.Contracts.Infrastructure.Ai;

namespace Explore.Infrastructure.Ai;

public sealed class AiProviderHealthReporter(
    AiProviderSettingsValidator validator,
    IAiProviderStrategyResolver resolver)
{
    public AiProviderHealth Check(AiProviderSettings settings)
    {
        Dictionary<string, object> data = new(StringComparer.Ordinal)
        {
            ["enabled"] = settings.Enabled,
            ["provider"] = settings.Provider,
            ["endpointConfigured"] = !string.IsNullOrWhiteSpace(settings.EndpointUrl),
            ["apiKeyConfigured"] = !string.IsNullOrWhiteSpace(settings.ApiKey),
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
        if (strategy is not null)
            return strategy.CheckHealth(data);

        data["reason"] = "provider_not_configured";
        return new AiProviderHealth(
            Enabled: true,
            Healthy: false,
            Status: "provider_not_configured",
            Description: "AI provider integration is enabled but no runnable provider is configured.",
            Data: data);
    }

}
