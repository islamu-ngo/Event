using System.Net;
using System.Net.Sockets;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Settings.Groups;

namespace Explore.Application.Features.AiAssistant;

internal static class AiAssistantAvailability
{
    public const string ModelNotAllowedFailureCode = "model_not_allowed";
    public const string ModelNotAllowedFailureMessage = "Selected AI model is not allowed by tenant policy.";

    public static string NormalizeProvider(string? provider)
        => string.IsNullOrWhiteSpace(provider)
            ? AiProviderDefaults.ProviderNone
            : provider.Trim().ToLowerInvariant();

    public static string? ResolveDisabledReason(AiAssistantSettingGroup settings)
    {
        var provider = NormalizeProvider(settings.Provider);

        if (!settings.Enabled)
            return "disabled";

        if (provider == AiProviderDefaults.ProviderNone)
            return "provider_not_configured";

        if (provider != AiProviderDefaults.ProviderFake
            && provider != AiProviderDefaults.ProviderOpenAi
            && provider != AiProviderDefaults.ProviderOpenAiCompatible
            && provider != AiProviderDefaults.ProviderAnthropic
            && provider != AiProviderDefaults.ProviderAnthropicCompatible)
            return "provider_unsupported";

        if (provider == AiProviderDefaults.ProviderOpenAi || provider == AiProviderDefaults.ProviderAnthropic)
        {
            if (!settings.HasApiKey)
                return "api_key_not_configured";

            if (!settings.HasModel)
                return "model_not_configured";
        }

        if (provider == AiProviderDefaults.ProviderOpenAiCompatible || provider == AiProviderDefaults.ProviderAnthropicCompatible)
        {
            if (string.IsNullOrWhiteSpace(settings.EndpointUrl))
                return "endpoint_not_configured";

            if (!settings.HasModel)
                return "model_not_configured";
        }

        return null;
    }

    public static string ResolveModelId(AiAssistantSettingGroup settings)
        => NormalizeProvider(settings.Provider) == AiProviderDefaults.ProviderFake
            ? AiProviderDefaults.FakeModelId
            : settings.ModelId?.Trim() ?? string.Empty;

    public static IReadOnlyList<string> ResolveAllowedModelIds(AiAssistantSettingGroup settings)
    {
        if (NormalizeProvider(settings.Provider) == AiProviderDefaults.ProviderFake)
        {
            return [AiProviderDefaults.FakeModelId];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelIds = new List<string>();

        AddModelId(settings.ModelId);
        foreach (var modelId in settings.AllowedModelIds)
        {
            AddModelId(modelId);
        }

        return modelIds;

        void AddModelId(string? modelId)
        {
            var trimmed = modelId?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || !seen.Add(trimmed))
            {
                return;
            }

            modelIds.Add(trimmed);
        }
    }

    public static bool IsModelAllowed(AiAssistantSettingGroup settings, string modelId)
        => ResolveAllowedModelIds(settings).Any(allowedModelId => string.Equals(
            allowedModelId,
            modelId,
            StringComparison.OrdinalIgnoreCase));

    public static int ResolveTimeoutSeconds(AiAssistantSettingGroup settings)
    {
        var configuredTimeout = settings.TimeoutSeconds <= 0
            ? AiProviderDefaults.DefaultTimeoutSeconds
            : settings.TimeoutSeconds;

        if (NormalizeProvider(settings.Provider) == AiProviderDefaults.ProviderOpenAi
            || NormalizeProvider(settings.Provider) == AiProviderDefaults.ProviderOpenAiCompatible
            || NormalizeProvider(settings.Provider) == AiProviderDefaults.ProviderAnthropic
            || NormalizeProvider(settings.Provider) == AiProviderDefaults.ProviderAnthropicCompatible)
        {
            configuredTimeout = Math.Max(configuredTimeout, AiProviderDefaults.DefaultTimeoutSeconds);
            if (IsLocalOrPrivateEndpoint(settings.EndpointUrl))
            {
                configuredTimeout = Math.Max(configuredTimeout, AiProviderDefaults.LocalProviderTimeoutSeconds);
            }
        }

        return Math.Clamp(configuredTimeout, 1, AiProviderDefaults.MaxTimeoutSeconds);
    }

    private static bool IsLocalOrPrivateEndpoint(string? endpointUrl)
    {
        if (string.IsNullOrWhiteSpace(endpointUrl)
            || !Uri.TryCreate(endpointUrl, UriKind.Absolute, out var endpoint))
        {
            return false;
        }

        if (endpoint.IsLoopback)
        {
            return true;
        }

        if (!IPAddress.TryParse(endpoint.Host, out var address))
        {
            return endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }

        var bytes = address.GetAddressBytes();
        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork =>
                bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254),
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal || address.IsIPv6SiteLocal,
            _ => false
        };
    }
}
