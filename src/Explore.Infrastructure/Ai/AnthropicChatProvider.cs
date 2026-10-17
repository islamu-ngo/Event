using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;

namespace Explore.Infrastructure.Ai;

public sealed class AnthropicChatProvider : AnthropicCompatibleChatProvider
{
    public new const string HttpClientName = "AnthropicAiClient";

    private const string DefaultEndpointUrl = "https://api.anthropic.com/v1";

    public AnthropicChatProvider(
        IHttpClientFactory httpClientFactory,
        AiProviderConfigurationResolver configuration,
        AiProviderSettingsValidator validator,
        BusinessMetrics metrics)
        : base(
            httpClientFactory,
            configuration,
            validator,
            metrics,
            AiProviderSettings.ProviderAnthropic,
            AiProviderDefaults.ProviderAnthropic,
            HttpClientName,
            DefaultEndpointUrl,
            "Anthropic provider is not enabled or configured.")
    {
    }
}
