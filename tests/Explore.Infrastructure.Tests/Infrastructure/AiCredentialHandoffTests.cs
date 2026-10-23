using System.Net;
using System.Text;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Ai;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiCredentialHandoffTests : AiProviderTestBase
{
    private const string Endpoint = "https://selected.provider.test/v1";

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi, SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey, "")]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible, SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey, "")]
    [Arguments(AiProviderSettings.ProviderAnthropic, SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey, "")]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible, SecretDefinitionRegistry.Keys.Ai.AnthropicCompatibleApiKey, "")]
    public async Task TenantSelectionHandsOnlyExactSelectedCredentialToNativeHttp(
        int providerId, string key, string qualifier)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.Tenant);
        string instance = Fixture.Bind(key, SecretScope.Instance, null, "");
        string otherTenant = Fixture.Bind(key, SecretScope.Tenant, Guid.CreateVersion7(), qualifier);
        string officialKey = providerId switch
        {
            AiProviderSettings.ProviderOpenAiCompatible => SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey,
            AiProviderSettings.ProviderAnthropicCompatible => SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey,
            _ => key
        };
        string official = Fixture.Bind(officialKey, SecretScope.Tenant, Fixture.Database.TenantId, "");
        string selected = Fixture.Bind(key, SecretScope.Tenant, Fixture.Database.TenantId, qualifier);
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var configuration = Fixture.Configuration(Settings(providerId), bindCredential: false);
        var provider = Fixture.Provider(providerId, configuration, factory);

        var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.Tenant));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("selected response");
        await Assert.That(transport.Credential).IsEqualTo(selected);
        await Assert.That(transport.Credential).IsNotEqualTo(instance);
        await Assert.That(transport.Credential).IsNotEqualTo(otherTenant);
        if (providerId is AiProviderSettings.ProviderOpenAiCompatible or AiProviderSettings.ProviderAnthropicCompatible)
            await Assert.That(transport.Credential).IsNotEqualTo(official);
        await Assert.That(transport.Body).DoesNotContain(selected);
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(factory.ClientNames.Single()).DoesNotContain(AiTransportAuthority.InstanceClientSuffix);
        await Assert.That(transport.RequestUri!.Host).IsEqualTo("selected.provider.test");
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi, SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey, "")]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible, SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey, "")]
    [Arguments(AiProviderSettings.ProviderAnthropic, SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey, "")]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible, SecretDefinitionRegistry.Keys.Ai.AnthropicCompatibleApiKey, "")]
    [Arguments(AiProviderSettings.ProviderAzureOpenAi, SecretDefinitionRegistry.Keys.Ai.AzureApiKey, "")]
    public async Task InstanceSelectionHandsExactProviderReferenceToNativeHttpOrAzureSdk(
        int providerId, string key, string qualifier)
    {
        string selected = Fixture.Bind(key, SecretScope.Instance, null, qualifier);
        string tenant = Fixture.Bind(key, SecretScope.Tenant, Fixture.Database.TenantId, qualifier);
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var configuration = Fixture.Configuration(Settings(providerId), bindCredential: false);
        var provider = Fixture.Provider(providerId, configuration, factory);

        var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.Instance) with { ProviderConfiguration = null });

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(transport.Credential).IsEqualTo(selected);
        await Assert.That(transport.Credential).IsNotEqualTo(tenant);
        await Assert.That(transport.Body).DoesNotContain(selected);
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(factory.ClientNames.Single()).EndsWith(AiTransportAuthority.InstanceClientSuffix);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    public async Task SelectedCredentialFailuresNeverDispatchOrBorrowAnAvailableFallback(int providerId)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.Tenant);
        var (key, qualifier) = Reference(providerId);
        Fixture.Bind(key, SecretScope.Instance, null, qualifier);
        if (qualifier.Length > 0)
            Fixture.Bind(key, SecretScope.Tenant, Fixture.Database.TenantId, "");
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var provider = Fixture.Provider(providerId,
            Fixture.Configuration(Settings(providerId), bindCredential: false), factory);
        var outcomes = new (SecretResolutionResult Result, string Code)[]
        {
            (SecretResolutionResult.Unconfigured, "credential_unconfigured"),
            (SecretResolutionResult.Unavailable, "credential_unavailable"),
            (SecretResolutionResult.Unauthorized, "credential_unauthorized"),
            (SecretResolutionResult.Invalid, "credential_invalid")
        };

        foreach (var (resolution, failure) in outcomes)
        {
            Fixture.SetSecret(key, SecretScope.Tenant, Fixture.Database.TenantId, qualifier, resolution);
            var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.Tenant));

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Error!.Code).IsEqualTo(failure);
            await Assert.That(result.Error.IsTransient).IsFalse();
            await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
            await Assert.That(transport.Calls).IsEqualTo(0);
        }
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    public async Task ResolvedCredentialWithWrongKeyOrOwnershipNeverReachesNativeTransport(int providerId)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.Tenant);
        var (key, qualifier) = Reference(providerId);
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var provider = Fixture.Provider(providerId,
            Fixture.Configuration(Settings(providerId), bindCredential: false), factory);
        var returnedMetadata = new (string Key, SecretScope Scope, Guid? ScopeId, string Value)[]
        {
            (SecretDefinitionRegistry.Keys.Ai.AzureApiKey, SecretScope.Tenant, Fixture.Database.TenantId, Guid.CreateVersion7().ToString("N")),
            (key, SecretScope.Instance, null, Guid.CreateVersion7().ToString("N")),
            (key, SecretScope.Tenant, Guid.CreateVersion7(), Guid.CreateVersion7().ToString("N")),
            (key, SecretScope.Tenant, Fixture.Database.TenantId, " ")
        };

        foreach (var metadata in returnedMetadata)
        {
            Fixture.SetSecret(key, SecretScope.Tenant, Fixture.Database.TenantId, qualifier,
                SecretResolutionResult.Resolved(new ResolvedSecret(metadata.Key, metadata.Value,
                    SecretSourceType.EnvironmentVariable, metadata.Scope, metadata.ScopeId, DateTimeOffset.UtcNow)));
            var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.Tenant));

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Error!.Code).IsEqualTo("credential_invalid");
            await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
            await Assert.That(transport.Calls).IsEqualTo(0);
            await Assert.That(result.Error.Message).DoesNotContain(metadata.Value.Trim().Length > 0 ? metadata.Value : Fixture.Credential);
        }
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    public async Task ExplicitCompatibleNoneDispatchesUnauthenticatedWithoutOfficialOrInstanceBorrowing(int providerId)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.None);
        var (key, qualifier) = Reference(providerId);
        Fixture.Bind(key, SecretScope.Instance, null, "");
        Fixture.Bind(key, SecretScope.Instance, null, qualifier);
        Fixture.Bind(key, SecretScope.Tenant, Fixture.Database.TenantId, "");
        Fixture.Bind(key, SecretScope.Tenant, Fixture.Database.TenantId, qualifier);
        Fixture.RejectSecretReads();
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var provider = Fixture.Provider(providerId,
            Fixture.Configuration(Settings(providerId), bindCredential: false), factory);

        var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.None));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(transport.Credential).IsNull();
        await Assert.That(transport.AuthorizationPresent).IsFalse();
        await Assert.That(transport.ApiKeyPresent).IsFalse();
        await Assert.That(transport.Calls).IsEqualTo(1);
        await Assert.That(factory.ClientNames.Single()).DoesNotContain(AiTransportAuthority.InstanceClientSuffix);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    public async Task TenantOwnedTransportCannotSelectAnAvailableInstanceReference(int providerId)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.Instance);
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var provider = Fixture.Provider(providerId, Fixture.Configuration(Settings(providerId)), factory);

        var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.Instance));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("credential_scope_invalid");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    public async Task OfficialProviderCannotUseExplicitNoneEvenWithInstanceCredentialAvailable(int providerId)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.None);
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var provider = Fixture.Provider(providerId, Fixture.Configuration(Settings(providerId)), factory);

        var result = await provider.SendAsync(Payload(providerId, AiCredentialScope.None));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("credential_scope_invalid");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    public async Task FreshPolicyRejectsStaleProviderEndpointOrScopeBeforeCredentialHandoff(int providerId)
    {
        await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.Tenant);
        Fixture.BindProvider(providerId, AiCredentialScope.Tenant, Fixture.Credential);
        var transport = new RecordingTransport(providerId);
        var factory = new ClientFactory(Fixture.CreateClient(transport));
        var provider = Fixture.Provider(providerId,
            Fixture.Configuration(Settings(providerId), bindCredential: false), factory);
        var payload = Payload(providerId, AiCredentialScope.Tenant);
        var changes = new (string Key, string Value)[]
        {
            (GovernanceSettingKeys.AiAssistant.Provider, "none"),
            (GovernanceSettingKeys.AiAssistant.EndpointUrl, "https://changed.provider.test/v1"),
            (GovernanceSettingKeys.AiAssistant.CredentialScope, "none")
        };

        foreach (var change in changes)
        {
            await Fixture.SelectTenantAsync(providerId, Endpoint, AiCredentialScope.Tenant);
            await Fixture.Database.SetTenantAsync(change.Key, change.Value);

            var result = await provider.SendAsync(payload);

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Error!.Code).IsEqualTo("provider_configuration_changed");
            await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
            await Assert.That(transport.Calls).IsEqualTo(0);
        }
    }

    private static (string Key, string Qualifier) Reference(int providerId) => providerId switch
    {
        AiProviderSettings.ProviderOpenAi => (SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey, ""),
        AiProviderSettings.ProviderOpenAiCompatible => (SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey, ""),
        AiProviderSettings.ProviderAnthropic => (SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey, ""),
        AiProviderSettings.ProviderAnthropicCompatible => (SecretDefinitionRegistry.Keys.Ai.AnthropicCompatibleApiKey, ""),
        _ => throw new ArgumentOutOfRangeException(nameof(providerId))
    };

    private static AiProviderSettings Settings(int providerId) => new()
    {
        Enabled = true,
        Provider = providerId,
        EndpointUrl = Endpoint,
        ModelId = "model",
        CredentialScope = AiCredentialScope.Instance,
        AzureCredentialMode = AiProviderSettings.AzureCredentialModeApiKey
    };

    private static AiChatPayload Payload(int providerId, AiCredentialScope scope) => new(
        "model", [new AiChatMessage(AiMessageRole.User, "Hello")], null,
        new AiChatOptions(8000, 1024, 0.2m, 30, false, false),
        ProviderConfiguration: new(providerId, Endpoint, scope, "model"));

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public List<string> ClientNames { get; } = [];
        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            return client;
        }
    }

    private sealed class RecordingTransport(int providerId) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Credential { get; private set; }
        public bool AuthorizationPresent { get; private set; }
        public bool ApiKeyPresent { get; private set; }
        public Uri? RequestUri { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            AuthorizationPresent = request.Headers.Authorization is not null;
            ApiKeyPresent = request.Headers.Contains("x-api-key") || request.Headers.Contains("api-key");
            Credential = request.Headers.Authorization?.Parameter
                ?? (request.Headers.TryGetValues("x-api-key", out var anthropic) ? anthropic.Single() : null)
                ?? (request.Headers.TryGetValues("api-key", out var azure) ? azure.Single() : null);
            string response = providerId switch
            {
                AiProviderSettings.ProviderOpenAi =>
                    """{"id":"response","status":"completed","output":[{"type":"message","content":[{"type":"output_text","text":"selected response"}]}]}""",
                AiProviderSettings.ProviderAnthropic or AiProviderSettings.ProviderAnthropicCompatible =>
                    """{"content":[{"type":"text","text":"selected response"}],"stop_reason":"end_turn"}""",
                AiProviderSettings.ProviderAzureOpenAi =>
                    """{"id":"completion","object":"chat.completion","created":1,"model":"model","choices":[{"index":0,"message":{"role":"assistant","content":"selected response"},"finish_reason":"stop"}]}""",
                _ => """{"choices":[{"message":{"content":"selected response"},"finish_reason":"stop"}]}"""
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
