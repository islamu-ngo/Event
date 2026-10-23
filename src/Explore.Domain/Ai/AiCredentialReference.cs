using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Explore.Domain.Ai;

public enum AiCredentialScope
{
    None,
    Instance,
    Tenant
}

/// <summary>
/// A provider-bound credential reference. Tenant-owned transports never inherit
/// instance credentials; only an explicit credential-free selection yields no reference.
/// </summary>
public sealed record AiCredentialReference
{
    public const string RetiredSettingKey = "ai_assistant.api_key";

    public static void RejectRetiredSettingMutation(string key)
    {
        if (key == RetiredSettingKey)
            throw new InvalidOperationException("ai_credential_setting_retired");
    }

    private AiCredentialReference(string settingKey, SecretScope scope, Guid? scopeId, string qualifier)
    {
        SettingKey = settingKey;
        Scope = scope;
        ScopeId = scopeId;
        Qualifier = qualifier;
    }

    public string SettingKey { get; }
    public SecretScope Scope { get; }
    public Guid? ScopeId { get; }
    public string Qualifier { get; }

    public static bool TryCreate(
        string provider,
        AiCredentialScope credentialScope,
        Guid tenantId,
        bool tenantOwnedTransport,
        out AiCredentialReference? reference)
    {
        reference = null;
        string? key = provider switch
        {
            "openai" => SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey,
            "anthropic" => SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey,
            "openai-compatible" => SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey,
            "anthropic-compatible" => SecretDefinitionRegistry.Keys.Ai.AnthropicCompatibleApiKey,
            "azure-openai" => SecretDefinitionRegistry.Keys.Ai.AzureApiKey,
            _ => null
        };
        if (credentialScope == AiCredentialScope.None)
            return provider is "fake" or "openai-compatible" or "anthropic-compatible";
        if (key is null || credentialScope is not (AiCredentialScope.Instance or AiCredentialScope.Tenant)
            || provider == "azure-openai" && credentialScope != AiCredentialScope.Instance
            || credentialScope == AiCredentialScope.Instance && tenantOwnedTransport
            || credentialScope == AiCredentialScope.Tenant && tenantId == Guid.Empty)
            return false;
        reference = new AiCredentialReference(key,
            credentialScope == AiCredentialScope.Tenant ? SecretScope.Tenant : SecretScope.Instance,
            credentialScope == AiCredentialScope.Tenant ? tenantId : null, "");
        return true;
    }
}
