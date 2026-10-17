using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Infrastructure.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Ai;

public sealed class AiProviderSettingsValidator : IValidateOptions<AiProviderSettings>
{
    internal bool FakeProviderAllowed { get; }

    private static readonly HashSet<int> SupportedProviders =
    [
        AiProviderSettings.ProviderNone,
        AiProviderSettings.ProviderFake,
        AiProviderSettings.ProviderOpenAi,
        AiProviderSettings.ProviderOpenAiCompatible,
        AiProviderSettings.ProviderAnthropic,
        AiProviderSettings.ProviderAnthropicCompatible,
        AiProviderSettings.ProviderAzureOpenAi
    ];

    private static readonly HashSet<string> SupportedAzureCredentialModes = new(StringComparer.OrdinalIgnoreCase)
    {
        AiProviderSettings.AzureCredentialModeApiKey,
        AiProviderSettings.AzureCredentialModeDefaultAzureCredential
    };

    public AiProviderSettingsValidator(IHostEnvironment? environment = null)
    {
        FakeProviderAllowed = environment?.IsDevelopment() == true
            || environment?.IsEnvironment("Testing") == true;
    }

    public ValidateOptionsResult Validate(string? name, AiProviderSettings options)
    {
        List<string> failures = [];

        if (!SupportedProviders.Contains(options.Provider))
        {
            failures.Add("AiProvider:Provider must be a valid provider ID (1-7).");
        }

        if (options.Provider == AiProviderSettings.ProviderFake && !FakeProviderAllowed)
        {
            failures.Add("AiProvider:Provider fake is supported only in Development or Testing environments.");
        }

        if (!string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            ValidateEndpointSafety(options, failures);
        }

        if (options.Enabled && options.Provider == AiProviderSettings.ProviderOpenAi)
        {
            ValidateOpenAiSettings(options, failures);
        }

        if (options.Enabled && options.Provider == AiProviderSettings.ProviderOpenAiCompatible)
        {
            ValidateOpenAiCompatibleSettings(options, failures);
        }

        if (options.Enabled && options.Provider == AiProviderSettings.ProviderAnthropic)
        {
            ValidateAnthropicSettings(options, failures);
        }

        if (options.Enabled && options.Provider == AiProviderSettings.ProviderAnthropicCompatible)
        {
            ValidateAnthropicCompatibleSettings(options, failures);
        }

        if (options.Enabled && options.Provider == AiProviderSettings.ProviderAzureOpenAi)
        {
            ValidateAzureOpenAiSettings(options, failures);
        }

        ValidateLimits(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateOpenAiSettings(AiProviderSettings options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add("AiProvider:ModelId is required for openai providers.");
        }
    }

    private static void ValidateAzureOpenAiSettings(AiProviderSettings options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            failures.Add("AiProvider:EndpointUrl is required for azure-openai providers.");
        }
        else if (Uri.TryCreate(options.EndpointUrl, UriKind.Absolute, out var endpoint)
            && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add("AiProvider:EndpointUrl must use HTTPS for azure-openai providers.");
        }

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add("AiProvider:ModelId is required for azure-openai providers.");
        }

        if (!SupportedAzureCredentialModes.Contains(options.AzureCredentialMode))
        {
            failures.Add("AiProvider:AzureCredentialMode must be api-key or default-azure-credential.");
        }

    }

    private static void ValidateOpenAiCompatibleSettings(AiProviderSettings options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add("AiProvider:ModelId is required for openai-compatible providers.");
        }

        if (string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            failures.Add("AiProvider:EndpointUrl is required for openai-compatible providers.");
        }
    }

    private static void ValidateAnthropicCompatibleSettings(AiProviderSettings options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add("AiProvider:ModelId is required for anthropic-compatible providers.");
        }

        if (string.IsNullOrWhiteSpace(options.EndpointUrl))
        {
            failures.Add("AiProvider:EndpointUrl is required for anthropic-compatible providers.");
        }
    }

    private static void ValidateAnthropicSettings(AiProviderSettings options, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            failures.Add("AiProvider:ModelId is required for anthropic providers.");
        }
    }

    private static void ValidateEndpointSafety(AiProviderSettings options, List<string> failures)
    {
        if (!Uri.TryCreate(options.EndpointUrl, UriKind.Absolute, out var endpoint)
            || (endpoint.Scheme != Uri.UriSchemeHttps && endpoint.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add("AiProvider:EndpointUrl must be an absolute HTTP or HTTPS URL.");
            return;
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            failures.Add("AiProvider:EndpointUrl must not contain embedded credentials.");
        }

        if (!string.IsNullOrEmpty(endpoint.Query) || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            failures.Add("AiProvider:EndpointUrl must not contain query strings or fragments.");
        }

        try
        {
            var policy = new OutboundEndpointPolicy(blockPrivateNetworks: !options.AllowLocalProviderEndpoints);
            policy.RequireEndpoint(endpoint);
            if (System.Net.IPAddress.TryParse(endpoint.IdnHost.Trim('[', ']'), out var address))
                policy.RequireAllowed(endpoint, [address]);
            else if (!options.AllowLocalProviderEndpoints && endpoint.IsLoopback)
                failures.Add("AiProvider:EndpointUrl must not target local, loopback, link-local, or private network hosts unless local provider endpoints are explicitly allowed.");
        }
        catch (OutboundEndpointDeniedException)
        {
            failures.Add("AiProvider:EndpointUrl is not permitted by the outbound endpoint policy.");
        }
    }

    private static void ValidateLimits(AiProviderSettings options, List<string> failures)
    {
        if (options.MaxInputTokens <= 0)
        {
            failures.Add("AiProvider:MaxInputTokens must be greater than zero.");
        }

        if (options.MaxOutputTokens <= 0)
        {
            failures.Add("AiProvider:MaxOutputTokens must be greater than zero.");
        }

        if (options.Temperature < 0m || options.Temperature > 2m)
        {
            failures.Add("AiProvider:Temperature must be between 0 and 2.");
        }

        if (options.TimeoutSeconds <= 0 || options.TimeoutSeconds > AiProviderDefaults.MaxTimeoutSeconds)
        {
            failures.Add($"AiProvider:TimeoutSeconds must be between 1 and {AiProviderDefaults.MaxTimeoutSeconds}.");
        }

        if (options.RetentionDays < 0)
        {
            failures.Add("AiProvider:RetentionDays must be zero or greater.");
        }

        if (options.DailyMessageLimit <= 0)
        {
            failures.Add("AiProvider:DailyMessageLimit must be greater than zero.");
        }
    }

}
