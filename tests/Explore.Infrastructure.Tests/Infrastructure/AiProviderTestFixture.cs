using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Ai;
using Explore.Tests.Shared.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Extensions;

namespace Explore.Infrastructure.Tests.Infrastructure;

public abstract class AiProviderTestBase
{
    internal AiProviderTestFixture Fixture { get; private set; } = null!;

    [Before(Test)]
    public async Task InitializeAiSettings() => Fixture = await AiProviderTestFixture.CreateAsync();

    [After(Test)]
    public async Task DisposeAiSettings() => await Fixture.DisposeAsync();
}

internal sealed class AiProviderTestFixture : IAsyncDisposable
{
    private readonly List<HttpClient> _clients = [];
    private readonly Meter _meter = new(BusinessMetrics.MeterName);
    private readonly Dictionary<(string Key, SecretScope Scope, Guid? ScopeId, string Qualifier), SecretResolutionResult> _secrets = [];

    private AiProviderTestFixture(SmtpSettingsDatabase database)
    {
        Database = database;
        var meters = Substitute.For<IMeterFactory>();
        meters.Create(Arg.Any<MeterOptions>()).Returns(_meter);
        Metrics = new BusinessMetrics(meters);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        Validator = new AiProviderSettingsValidator(environment);
        Database.Secrets.Configure().ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => Lookup(call.ArgAt<string>(0), SecretScope.Instance, call.ArgAt<Guid?>(1), ""));
        Database.Secrets.Configure().ResolveQualifiedAsync(Arg.Any<string>(), Arg.Any<SecretScope>(),
                Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Lookup(call.ArgAt<string>(0), call.ArgAt<SecretScope>(1),
                call.ArgAt<Guid?>(2), call.ArgAt<string>(3)));
    }

    public SmtpSettingsDatabase Database { get; }
    public BusinessMetrics Metrics { get; }
    public AiProviderSettingsValidator Validator { get; }
    public string Credential { get; } = Guid.CreateVersion7().ToString("N");

    public static async Task<AiProviderTestFixture> CreateAsync() =>
        new(await SmtpSettingsDatabase.CreateAsync());

    private SecretResolutionResult Lookup(string key, SecretScope scope, Guid? scopeId, string qualifier) =>
        _secrets.GetValueOrDefault((key, scope, scopeId, qualifier), SecretResolutionResult.Unconfigured);

    public void SetSecret(string key, SecretScope scope, Guid? scopeId, string qualifier, SecretResolutionResult result) =>
        _secrets[(key, scope, scopeId, qualifier)] = result;

    public void RejectSecretReads()
    {
        Database.RejectSecretReads();
        Database.Secrets.Configure().ResolveQualifiedAsync(Arg.Any<string>(), Arg.Any<SecretScope>(),
                Arg.Any<Guid?>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<SecretResolutionResult>(_ => throw new InvalidOperationException("Secret provider must not be called."));
    }

    public string Bind(string key, SecretScope scope, Guid? scopeId, string qualifier, string? value = null)
    {
        value ??= Guid.CreateVersion7().ToString("N");
        SetSecret(key, scope, scopeId, qualifier, SecretResolutionResult.Resolved(new ResolvedSecret(
            key, value, SecretSourceType.EnvironmentVariable, scope, scopeId, DateTimeOffset.UtcNow)));
        return value;
    }

    public AiProviderConfigurationResolver Configuration(AiProviderSettings settings, bool bindCredential = true)
    {
        if (bindCredential && settings.CredentialScope != AiCredentialScope.None)
            BindProvider(settings.Provider, settings.CredentialScope, Credential);
        return new(Options.Create(settings), Database.Settings, Database, Database.Secrets, Validator);
    }

    public void BindProvider(int provider, AiCredentialScope credentialScope, string value)
    {
        var (key, qualifier) = provider switch
        {
            AiProviderSettings.ProviderOpenAi => (SecretDefinitionRegistry.Keys.Ai.OpenAiApiKey, ""),
            AiProviderSettings.ProviderOpenAiCompatible => (SecretDefinitionRegistry.Keys.Ai.OpenAiCompatibleApiKey, ""),
            AiProviderSettings.ProviderAnthropic => (SecretDefinitionRegistry.Keys.Ai.AnthropicApiKey, ""),
            AiProviderSettings.ProviderAnthropicCompatible => (SecretDefinitionRegistry.Keys.Ai.AnthropicCompatibleApiKey, ""),
            AiProviderSettings.ProviderAzureOpenAi => (SecretDefinitionRegistry.Keys.Ai.AzureApiKey, ""),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        var scope = credentialScope == AiCredentialScope.Tenant ? SecretScope.Tenant : SecretScope.Instance;
        Bind(key, scope, scope == SecretScope.Tenant ? Database.TenantId : null, qualifier, value);
    }

    public async Task SelectTenantAsync(int provider, string endpoint, AiCredentialScope scope, string model = "model")
    {
        await Database.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockAiAssistant, false);
        await Database.SetTenantAsync(GovernanceSettingKeys.AiAssistant.Enabled, true);
        await Database.SetTenantAsync(GovernanceSettingKeys.AiAssistant.Provider, AiProviderDefaults.ProviderIdToLabel(provider));
        await Database.SetTenantAsync(GovernanceSettingKeys.AiAssistant.EndpointUrl, endpoint);
        await Database.SetTenantAsync(GovernanceSettingKeys.AiAssistant.CredentialScope, scope.ToString().ToLowerInvariant());
        await Database.SetTenantAsync(GovernanceSettingKeys.AiAssistant.ModelId, model);
    }

    public HttpClient CreateClient(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        _clients.Add(client);
        return client;
    }

    public IAiChatProvider Provider(int provider, AiProviderConfigurationResolver configuration, IHttpClientFactory factory,
        AiResponseLimits? limits = null) => provider switch
        {
            AiProviderSettings.ProviderOpenAi => new OpenAiResponsesChatProvider(factory, configuration, Validator, Metrics, Options.Create(limits ?? new())),
            AiProviderSettings.ProviderOpenAiCompatible => new OpenAiCompatibleChatProvider(factory, configuration, Validator, Metrics, Options.Create(limits ?? new())),
            AiProviderSettings.ProviderAnthropic => new AnthropicChatProvider(factory, configuration, Validator, Metrics, Options.Create(limits ?? new())),
            AiProviderSettings.ProviderAnthropicCompatible => new AnthropicCompatibleChatProvider(factory, configuration, Validator, Metrics, Options.Create(limits ?? new())),
            AiProviderSettings.ProviderAzureOpenAi => new MicrosoftExtensionsAiChatProvider(configuration, Metrics, factory),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

    public AiProviderHealthReporter HealthReporter(AiProviderSettings settings, bool bindCredential = true)
    {
        var configuration = Configuration(settings, bindCredential);
        var factory = new NoNetworkClientFactory();
        var strategies = new IAiProviderStrategy[]
        {
            new FakeAiProviderStrategy(new FakeAiChatProvider()),
            new OpenAiResponsesProviderStrategy((OpenAiResponsesChatProvider)Provider(AiProviderSettings.ProviderOpenAi, configuration, factory)),
            new OpenAiCompatibleProviderStrategy((OpenAiCompatibleChatProvider)Provider(AiProviderSettings.ProviderOpenAiCompatible, configuration, factory)),
            new AnthropicProviderStrategy((AnthropicChatProvider)Provider(AiProviderSettings.ProviderAnthropic, configuration, factory)),
            new AnthropicCompatibleProviderStrategy((AnthropicCompatibleChatProvider)Provider(AiProviderSettings.ProviderAnthropicCompatible, configuration, factory)),
            new MicrosoftExtensionsProviderStrategy((MicrosoftExtensionsAiChatProvider)Provider(AiProviderSettings.ProviderAzureOpenAi, configuration, factory))
        };
        return new(Validator, new AiProviderStrategyResolver(strategies,
            NullLogger<AiProviderStrategyResolver>.Instance), configuration);
    }

    private sealed class NoNetworkClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Health must not dispatch network probes.");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
            client.Dispose();
        Metrics.Dispose();
        await Database.DisposeAsync();
    }
}
