using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Explore.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiEndpointAuthorityTests : AiProviderTestBase
{
    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    [Arguments(AiProviderSettings.ProviderAzureOpenAi)]
    public async Task TenantOverride_PrivateLiteralCannotInheritOperatorOptIn(int providerId)
    {
        var settings = Settings(providerId);
        await SelectTenantAsync(providerId, "https://127.0.0.1/v1");
        using var factory = new VettedClientFactory(settings, [IPAddress.Loopback]);
        var provider = CreateProvider(providerId, settings, factory);

        var result = await provider.SendAsync(Payload(providerId, "https://127.0.0.1/v1"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo(providerId == AiProviderSettings.ProviderAzureOpenAi
            ? "provider_configuration_changed" : "invalid_settings");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    [Arguments(AiProviderSettings.ProviderAzureOpenAi)]
    public async Task SelectedTransport_MixedDnsAnswersAreDeniedByActualAdapterTransport(int providerId)
    {
        var settings = Settings(providerId);
        bool azure = providerId == AiProviderSettings.ProviderAzureOpenAi;
        if (azure)
            settings.AllowLocalProviderEndpoints = false;
        else
            await SelectTenantAsync(providerId, "https://ai.tenant.test/v1");
        using var factory = new VettedClientFactory(settings,
            [IPAddress.Parse("8.8.8.8"), IPAddress.Loopback]);
        var provider = CreateProvider(providerId, settings, factory);

        var payload = Payload(providerId, "https://ai.tenant.test/v1");
        if (azure)
            payload = payload with { ProviderConfiguration = null };
        var result = await provider.SendAsync(payload);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_endpoint_denied");
        if (azure)
        {
            await Assert.That(factory.ClientNames.Single()).IsEqualTo(
                MicrosoftExtensionsAiChatProvider.HttpClientName + AiTransportAuthority.InstanceClientSuffix);
            await Assert.That(factory.ResolvedHosts.Single()).IsEqualTo("ai.operator.test");
        }
        else
        {
            await Assert.That(factory.ClientNames.Single()).DoesNotContain(AiTransportAuthority.InstanceClientSuffix);
            await Assert.That(factory.ResolvedHosts.Single()).IsEqualTo("ai.tenant.test");
        }
    }

    [Test]
    [Arguments("https://169.254.169.254/v1")]
    [Arguments("https://[fd00:ec2::254]/v1")]
    [Arguments("https://100.100.100.200/v1")]
    [Arguments("https://[::ffff:169.254.169.254]/v1")]
    [Arguments("https://metadata.google.internal/v1")]
    public async Task MetadataIsRejectedEvenForInstanceLocalOptIn(string endpoint)
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible);
        settings.EndpointUrl = endpoint;

        await Assert.That(new AiProviderSettingsValidator().Validate(null, settings).Succeeded).IsFalse();
    }

    [Test]
    [Arguments("https://[::ffff:127.0.0.1]/v1")]
    [Arguments("https://[fd12::1]/v1")]
    [Arguments("https://[fe80::1]/v1")]
    [Arguments("https://10.0.0.1/v1")]
    [Arguments("https://192.168.0.1/v1")]
    public async Task TenantPrivateAddressFamiliesAreDeniedWithoutClientConstruction(string endpoint)
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible);
        await SelectTenantAsync(settings.Provider, endpoint);
        using var factory = new VettedClientFactory(settings, [IPAddress.Loopback]);
        var provider = CreateProvider(settings.Provider, settings, factory);

        var result = await provider.SendAsync(Payload(settings.Provider, endpoint));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_settings");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InstanceTransport_RejectsOtherOriginBeforeResolvingOrSendingCredentials()
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible);
        int resolutions = 0;
        using var client = new HttpClient(AiTransportAuthority.CreateHandler(settings, true, (_, _) =>
        {
            resolutions++;
            return Task.FromResult<IPAddress[]>([IPAddress.Loopback]);
        }));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://other.operator.test/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Guid.NewGuid().ToString("N"));
        OutboundEndpointDeniedException? denied = null;
        try
        {
            using var response = await client.SendAsync(request);
        }
        catch (OutboundEndpointDeniedException exception)
        {
            denied = exception;
        }

        await Assert.That(denied?.FailureCategory).IsEqualTo("origin_mismatch");
        await Assert.That(resolutions).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ApprovedLocalInstance_UsesVettedAdapterAndDoesNotFollowRedirect(bool redirect)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible);
        settings.EndpointUrl = $"http://ai.operator.test:{port}/v1";
        string canary = Fixture.Credential;
        using var factory = new VettedClientFactory(settings, [IPAddress.Loopback]);
        var provider = (OpenAiCompatibleChatProvider)CreateProvider(settings.Provider, settings, factory);
        var accepted = listener.AcceptTcpClientAsync(timeout.Token);
        var sending = provider.ListAvailableModelsAsync(timeout.Token);
        using var peer = await accepted;
        await using var stream = peer.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var headers = new List<string>();
        while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line)
            headers.Add(line);
        string body = """{"data":[{"id":"local-model"}]}""";
        byte[] response = Encoding.ASCII.GetBytes(redirect
            ? $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{port}/other\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"
            : $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.ASCII.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
        await stream.WriteAsync(response, timeout.Token);
        await stream.FlushAsync(timeout.Token);
        var models = await sending;

        await Assert.That(models.Count).IsEqualTo(redirect ? 0 : 1);
        if (!redirect)
            await Assert.That(models.Single().Id).IsEqualTo("local-model");
        await Assert.That(factory.ClientNames.Single()).IsEqualTo(
            OpenAiCompatibleChatProvider.HttpClientName + AiTransportAuthority.InstanceClientSuffix);
        await Assert.That(headers).Contains($"Host: ai.operator.test:{port}");
        await Assert.That(headers).Contains($"Authorization: Bearer {canary}");
        await Assert.That(listener.Pending()).IsFalse();
    }

    [Test]
    public async Task InstanceMixedDns_MetadataAnswerDeniesEntireAnswerSetDespiteLocalOptIn()
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible);
        using var factory = new VettedClientFactory(settings,
            [IPAddress.Loopback, IPAddress.Parse("169.254.169.254")]);
        var provider = (OpenAiCompatibleChatProvider)CreateProvider(settings.Provider, settings, factory);

        var models = await provider.ListAvailableModelsAsync();

        await Assert.That(models.Count).IsEqualTo(0);
        await Assert.That(factory.ResolvedHosts.Single()).IsEqualTo("ai.operator.test");
    }

    [Test]
    public async Task InvalidTenantProviderDoesNotFallBackToInstanceStrategy()
    {
        var settings = Settings(AiProviderSettings.ProviderOpenAiCompatible);
        using var factory = new VettedClientFactory(settings, [IPAddress.Loopback]);
        var strategy = new OpenAiCompatibleProviderStrategy(
            (OpenAiCompatibleChatProvider)CreateProvider(settings.Provider, settings, factory));
        var resolver = new AiProviderStrategyResolver([strategy],
            Substitute.For<ILogger<AiProviderStrategyResolver>>());
        var provider = new RuntimeAiChatProvider(
            Options.Create(settings), new AiProviderSettingsValidator(), resolver);

        var result = await provider.SendAsync(Payload(999, settings.EndpointUrl));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_not_configured");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
    }

    [Test]
    public async Task TenantAzureWithoutKeyCannotBorrowInstanceDefaultCredential()
    {
        var settings = Settings(AiProviderSettings.ProviderAzureOpenAi);
        settings.CredentialScope = AiCredentialScope.None;
        settings.AzureCredentialMode = AiProviderSettings.AzureCredentialModeDefaultAzureCredential;
        await Fixture.SelectTenantAsync(AiProviderSettings.ProviderOpenAiCompatible,
            "https://ai.tenant.test", AiCredentialScope.None, "deployment");
        using var factory = new VettedClientFactory(settings, [IPAddress.Loopback]);
        var provider = CreateProvider(settings.Provider, settings, factory);
        var request = Payload(settings.Provider, "https://ai.tenant.test") with
        {
            ProviderConfiguration = new AiChatProviderConfiguration(
                settings.Provider, "https://ai.tenant.test", AiCredentialScope.None, "deployment")
        };

        var result = await provider.SendAsync(request);

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_configuration_changed");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
    }

    [Test]
    public async Task InstanceAzureReference_UsesActualSdkTransportAndOnlyAzureCredential()
    {
        var settings = Settings(AiProviderSettings.ProviderAzureOpenAi);
        string azureKey = Fixture.Credential;
        string? sentKey = null;
        Uri? target = null;
        using var client = new HttpClient(new RecordingSdkHandler(request =>
        {
            sentKey = request.Headers.GetValues("api-key").Single();
            target = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"id":"completion-a","object":"chat.completion","created":1,"model":"deployment",
                     "choices":[{"index":0,"message":{"role":"assistant","content":"Instance response"},"finish_reason":"stop"}],
                     "usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}
                    """, Encoding.UTF8, "application/json")
            };
        }));
        var factory = new RecordingClientFactory(client);
        var provider = CreateProvider(settings.Provider, settings, factory);
        var payload = Payload(settings.Provider, settings.EndpointUrl) with { ProviderConfiguration = null };

        var result = await provider.SendAsync(payload);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Instance response");
        await Assert.That(sentKey).IsEqualTo(azureKey);
        await Assert.That(target!.Host).IsEqualTo("ai.operator.test");
        await Assert.That(factory.ClientName).IsEqualTo(
            MicrosoftExtensionsAiChatProvider.HttpClientName + AiTransportAuthority.InstanceClientSuffix);
    }

    [Test]
    public async Task PostedTenantAzureReferenceCannotBorrowInstanceAzureCredential()
    {
        var settings = Settings(AiProviderSettings.ProviderAzureOpenAi);
        await Fixture.SelectTenantAsync(AiProviderSettings.ProviderOpenAiCompatible,
            "https://ai.tenant.test", AiCredentialScope.Tenant, "deployment");
        using var factory = new VettedClientFactory(settings, [IPAddress.Loopback]);
        var provider = CreateProvider(settings.Provider, settings, factory);

        var result = await provider.SendAsync(Payload(settings.Provider, "https://ai.tenant.test"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_configuration_changed");
        await Assert.That(factory.ClientNames.Count).IsEqualTo(0);
        await Assert.That(factory.ResolvedHosts.Count).IsEqualTo(0);
    }

    private static AiProviderSettings Settings(int provider) => new()
    {
        Enabled = true,
        Provider = provider,
        EndpointUrl = "https://ai.operator.test/v1",
        ModelId = "deployment",
        CredentialScope = AiCredentialScope.Instance,
        AzureCredentialMode = AiProviderSettings.AzureCredentialModeApiKey,
        AllowLocalProviderEndpoints = true
    };

    private static AiChatPayload Payload(int provider, string endpoint) => new(
        "deployment", [new AiChatMessage(AiMessageRole.User, "Hello")], null,
        new AiChatOptions(8000, 1024, 0.2m, 5, false, false),
        ProviderConfiguration: new AiChatProviderConfiguration(
            provider, endpoint, AiCredentialScope.Tenant, "deployment"));

    private IAiChatProvider CreateProvider(
        int provider, AiProviderSettings settings, IHttpClientFactory factory)
    {
        return Fixture.Provider(provider, Fixture.Configuration(settings), factory);
    }

    private async Task SelectTenantAsync(int provider, string endpoint)
    {
        int ordinaryProvider = provider == AiProviderSettings.ProviderAzureOpenAi
            ? AiProviderSettings.ProviderOpenAiCompatible : provider;
        await Fixture.SelectTenantAsync(ordinaryProvider, endpoint, AiCredentialScope.Tenant, "deployment");
        Fixture.BindProvider(ordinaryProvider, AiCredentialScope.Tenant, Fixture.Credential);
    }

    private sealed class VettedClientFactory(AiProviderSettings settings, IPAddress[] answers)
        : IHttpClientFactory, IDisposable
    {
        private readonly List<HttpClient> _clients = [];
        public List<string> ClientNames { get; } = [];
        public List<string> ResolvedHosts { get; } = [];

        public HttpClient CreateClient(string name)
        {
            ClientNames.Add(name);
            var client = new HttpClient(AiTransportAuthority.CreateHandler(settings,
                name.EndsWith(AiTransportAuthority.InstanceClientSuffix, StringComparison.Ordinal), (host, _) =>
                {
                    ResolvedHosts.Add(host);
                    return Task.FromResult(answers);
                }));
            _clients.Add(client);
            return client;
        }

        public void Dispose()
        {
            foreach (var client in _clients)
                client.Dispose();
        }
    }

    private sealed class RecordingClientFactory(HttpClient client) : IHttpClientFactory
    {
        public string? ClientName { get; private set; }
        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return client;
        }
    }

    private sealed class RecordingSdkHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
