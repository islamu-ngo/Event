using Explore.Domain.Ai;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;

namespace Event.Domain.UnitTests.Ai;

public sealed class AiCredentialReferenceTests
{
    [Test]
    [Arguments("openai", "", SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey)]
    [Arguments("anthropic", "", SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey)]
    [Arguments("openai-compatible", "", SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey)]
    [Arguments("anthropic-compatible", "", SecretDefinitionRegistry.Keys.Ai.AnthropicCompatibleApiKey)]
    public async Task TenantSelectionBindsExactProviderScopeAndQualifier(string provider, string qualifier, string key)
    {
        Guid tenant = Guid.CreateVersion7();
        bool accepted = AiCredentialReference.TryCreate(provider, AiCredentialScope.Tenant, tenant, true, out var reference);

        await Assert.That(accepted).IsTrue();
        await Assert.That(reference!.SettingKey).IsEqualTo(key);
        await Assert.That(reference.Scope).IsEqualTo(SecretScope.Tenant);
        await Assert.That(reference.ScopeId).IsEqualTo(tenant);
        await Assert.That(reference.Qualifier).IsEqualTo(qualifier);
    }

    [Test]
    [Arguments("openai")]
    [Arguments("anthropic")]
    [Arguments("openai-compatible")]
    [Arguments("anthropic-compatible")]
    public async Task TenantOwnedTransportCannotBorrowInstanceCredential(string provider)
    {
        bool accepted = AiCredentialReference.TryCreate(provider, AiCredentialScope.Instance, Guid.CreateVersion7(), true, out var reference);

        await Assert.That(accepted).IsFalse();
        await Assert.That(reference).IsNull();
    }

    [Test]
    [Arguments("openai", false)]
    [Arguments("anthropic", false)]
    [Arguments("openai-compatible", true)]
    [Arguments("anthropic-compatible", true)]
    [Arguments("fake", true)]
    [Arguments("unrecognized", false)]
    public async Task OnlyExplicitCredentialFreeProvidersAcceptNone(string provider, bool expected)
    {
        bool accepted = AiCredentialReference.TryCreate(provider, AiCredentialScope.None, Guid.CreateVersion7(), true, out var reference);

        await Assert.That(accepted).IsEqualTo(expected);
        await Assert.That(reference).IsNull();
    }

    [Test]
    public async Task InheritedTransportAllowsInstanceCredentialWithTenantModelOverrides()
    {
        bool accepted = AiCredentialReference.TryCreate("openai", AiCredentialScope.Instance, Guid.CreateVersion7(), false, out var reference);

        await Assert.That(accepted).IsTrue();
        await Assert.That(reference!.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(reference.ScopeId).IsNull();
        await Assert.That(reference.Qualifier).IsEqualTo("");
    }

    [Test]
    public async Task TenantCredentialRequiresAnAdmittedTenant()
    {
        bool accepted = AiCredentialReference.TryCreate("openai", AiCredentialScope.Tenant, Guid.Empty, true, out var reference);

        await Assert.That(accepted).IsFalse();
        await Assert.That(reference).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AzureApiKeySelectionRemainsInstanceOnly(bool tenant)
    {
        bool accepted = AiCredentialReference.TryCreate("azure-openai",
            tenant ? AiCredentialScope.Tenant : AiCredentialScope.Instance,
            Guid.CreateVersion7(), tenant, out var reference);

        await Assert.That(accepted).IsEqualTo(!tenant);
        if (tenant)
            await Assert.That(reference).IsNull();
        else
            await Assert.That(reference!.SettingKey).IsEqualTo(SecretDefinitionRegistry.Keys.Ai.AzureApiKey);
    }
}
