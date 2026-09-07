using Explore.Application.Contracts.Infrastructure.Ai;

public sealed class AiProviderSettings
{
    public const string SectionName = "AiProvider";

    public const int ProviderNone = AiProviderDefaults.ProviderIdNone;
    public const int ProviderFake = AiProviderDefaults.ProviderIdFake;
    public const int ProviderOpenAi = AiProviderDefaults.ProviderIdOpenAi;
    public const int ProviderOpenAiCompatible = AiProviderDefaults.ProviderIdOpenAiCompatible;
    public const int ProviderAnthropic = AiProviderDefaults.ProviderIdAnthropic;
    public const int ProviderAnthropicCompatible = AiProviderDefaults.ProviderIdAnthropicCompatible;
    public const int ProviderAzureOpenAi = AiProviderDefaults.ProviderIdAzureOpenAi;
    public const string AzureCredentialModeApiKey = "api-key";
    public const string AzureCredentialModeDefaultAzureCredential = "default-azure-credential";

    public bool Enabled { get; set; }
    public int Provider { get; set; } = ProviderNone;
    public string EndpointUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string AzureCredentialMode { get; set; } = AzureCredentialModeApiKey;
    public string AzureTenantId { get; set; } = string.Empty;
    public int MaxInputTokens { get; set; } = 8000;
    public int MaxOutputTokens { get; set; } = 1024;
    public decimal Temperature { get; set; } = 0.2m;
    public int TimeoutSeconds { get; set; } = AiProviderDefaults.DefaultTimeoutSeconds;
    public int RetentionDays { get; set; } = 30;
    public int DailyMessageLimit { get; set; } = 50;
    public bool ToolProposalsEnabled { get; set; } = true;
    public bool StreamingEnabled { get; set; }
    public bool AllowLocalProviderEndpoints { get; set; }
}
