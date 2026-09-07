namespace Explore.Domain.Settings.Definitions;

using Explore.Domain.Constants;

public static class AiAssistantSettingDefinitions
{
    private static readonly string[] AllowedProviders = ["none", "fake", "openai", "openai-compatible", "anthropic", "anthropic-compatible"];
    private const string DefaultTimeoutSeconds = "120";

    public static readonly SettingDefinition Enabled = new(
        Key: GovernanceSettingKeys.AiAssistant.Enabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "AiAssistant",
        Description: "Enable AI assistant in the application shell",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition Provider = new(
        Key: GovernanceSettingKeys.AiAssistant.Provider,
        ValueType: SettingValueType.String,
        DefaultValue: "\"none\"",
        Category: "AiAssistant",
        Description: "Selected AI provider. Use OpenAI or Anthropic for official APIs, fake for deterministic tests, and compatible providers for configured endpoints.",
        MaxScope: SettingScope.Tenant,
        AllowedValues: AllowedProviders);

    public static readonly SettingDefinition EndpointUrl = new(
        Key: GovernanceSettingKeys.AiAssistant.EndpointUrl,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "AiAssistant",
        Description: "Optional AI provider endpoint URL (for self-hosted or compatible APIs)",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ApiKey = new(
        Key: GovernanceSettingKeys.AiAssistant.ApiKey,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "AiAssistant",
        Description: "AI provider API key",
        MaxScope: SettingScope.Tenant,
        IsSensitive: true);

    public static readonly SettingDefinition ModelId = new(
        Key: GovernanceSettingKeys.AiAssistant.ModelId,
        ValueType: SettingValueType.String,
        DefaultValue: "\"\"",
        Category: "AiAssistant",
        Description: "Default model identifier used for assistant requests",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition AllowedModelIds = new(
        Key: GovernanceSettingKeys.AiAssistant.AllowedModelIds,
        ValueType: SettingValueType.Json,
        DefaultValue: "[]",
        Category: "AiAssistant",
        Description: "Model identifiers that may be selected in the AI assistant model picker",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxInputTokens = new(
        Key: GovernanceSettingKeys.AiAssistant.MaxInputTokens,
        ValueType: SettingValueType.Integer,
        DefaultValue: "8000",
        Category: "AiAssistant",
        Description: "Maximum prompt/context tokens allowed per assistant request",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxOutputTokens = new(
        Key: GovernanceSettingKeys.AiAssistant.MaxOutputTokens,
        ValueType: SettingValueType.Integer,
        DefaultValue: "1024",
        Category: "AiAssistant",
        Description: "Maximum generated tokens allowed per assistant response",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition Temperature = new(
        Key: GovernanceSettingKeys.AiAssistant.Temperature,
        ValueType: SettingValueType.Decimal,
        DefaultValue: "0.2",
        Category: "AiAssistant",
        Description: "Default model temperature for assistant responses",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition TimeoutSeconds = new(
        Key: GovernanceSettingKeys.AiAssistant.TimeoutSeconds,
        ValueType: SettingValueType.Integer,
        DefaultValue: DefaultTimeoutSeconds,
        Category: "AiAssistant",
        Description: "Maximum provider call duration in seconds",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition RetentionDays = new(
        Key: GovernanceSettingKeys.AiAssistant.RetentionDays,
        ValueType: SettingValueType.Integer,
        DefaultValue: "30",
        Category: "AiAssistant",
        Description: "Default retention period for persisted AI conversation history in days",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DailyMessageLimit = new(
        Key: GovernanceSettingKeys.AiAssistant.DailyMessageLimit,
        ValueType: SettingValueType.Integer,
        DefaultValue: "50",
        Category: "AiAssistant",
        Description: "Per-user daily assistant message limit before provider calls are rejected",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition DailyTenantMessageLimit = new(
        Key: GovernanceSettingKeys.AiAssistant.DailyTenantMessageLimit,
        ValueType: SettingValueType.Integer,
        DefaultValue: "1000",
        Category: "AiAssistant",
        Description: "Per-tenant daily assistant user-message limit before provider calls are rejected",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ConcurrentRunLimit = new(
        Key: GovernanceSettingKeys.AiAssistant.ConcurrentRunLimit,
        ValueType: SettingValueType.Integer,
        DefaultValue: "1",
        Category: "AiAssistant",
        Description: "Maximum concurrent AI assistant runs allowed per user",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition SelectedReferenceLimit = new(
        Key: GovernanceSettingKeys.AiAssistant.SelectedReferenceLimit,
        ValueType: SettingValueType.Integer,
        DefaultValue: "8",
        Category: "AiAssistant",
        Description: "Maximum selected references that can be packed into an assistant request",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition ToolProposalsEnabled = new(
        Key: GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "true",
        Category: "AiAssistant",
        Description: "Allow model output to create persisted proposed actions that still require user confirmation",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition StreamingEnabled = new(
        Key: GovernanceSettingKeys.AiAssistant.StreamingEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "AiAssistant",
        Description: "Enable streaming assistant responses after streaming transport hardening is complete",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition AllowAnonymousAccess = new(
        Key: GovernanceSettingKeys.AiAssistant.AllowAnonymousAccess,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "AiAssistant",
        Description: "Allow unauthenticated visitors to open the AI assistant from the application shell",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition MaxAiContextSensitivity = new(
        Key: GovernanceSettingKeys.AiAssistant.MaxAiContextSensitivity,
        ValueType: SettingValueType.Integer,
        DefaultValue: "1",
        Category: "AiAssistant",
        Description: "Maximum AI context sensitivity level allowed",
        MaxScope: SettingScope.Tenant);

    public static readonly SettingDefinition PiiDisclosureEnabled = new(
        Key: GovernanceSettingKeys.AiAssistant.PiiDisclosureEnabled,
        ValueType: SettingValueType.Boolean,
        DefaultValue: "false",
        Category: "AiAssistant",
        Description: "Allow the assistant context pipeline to disclose PII fields when explicit consent and policy checks allow it",
        MaxScope: SettingScope.Tenant);

    public static IReadOnlyList<SettingDefinition> All =>
    [
        Enabled, Provider, EndpointUrl, ApiKey, ModelId, AllowedModelIds, MaxInputTokens, MaxOutputTokens,
        Temperature, TimeoutSeconds, RetentionDays, DailyMessageLimit, DailyTenantMessageLimit,
        ConcurrentRunLimit, SelectedReferenceLimit,
        ToolProposalsEnabled, StreamingEnabled, AllowAnonymousAccess, MaxAiContextSensitivity, PiiDisclosureEnabled
    ];
}
