using System.Net;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Explore.Infrastructure.Http;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AiBoundedConsumptionTests : AiProviderTestBase
{
    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible, false)]
    [Arguments(AiProviderSettings.ProviderOpenAi, false)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible, false)]
    [Arguments(AiProviderSettings.ProviderAnthropic, false)]
    [Arguments(AiProviderSettings.ProviderAzureOpenAi, false)]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible, true)]
    [Arguments(AiProviderSettings.ProviderOpenAi, true)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible, true)]
    [Arguments(AiProviderSettings.ProviderAnthropic, true)]
    [Arguments(AiProviderSettings.ProviderAzureOpenAi, true)]
    public async Task Completion_UnknownLengthStopsAtOverflowAndDisposesBeforeMapping(int providerId, bool configurationOverride)
    {
        using var body = new ObservedStream(Encoding.UTF8.GetBytes(new string(' ', 1024)));
        var limits = new AiResponseLimits { MaximumChatBytes = 128 };
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body)
        });
        using var client = new HttpClient(providerId == AiProviderSettings.ProviderAzureOpenAi
            ? new AiResponseBudgetHandler(limits) { InnerHandler = handler }
            : handler);
        // Azure is instance-only and absent from the ordinary assistant group.
        // Reject its posted tenant selection, then exercise the SDK budget through
        // its admitted instance reference. Ordinary adapters use tenant policy.
        bool tenant = configurationOverride && providerId != AiProviderSettings.ProviderAzureOpenAi;
        var scope = tenant ? AiCredentialScope.Tenant : AiCredentialScope.Instance;
        if (tenant)
            await Fixture.SelectTenantAsync(providerId, "https://provider.example/v1", scope);
        else if (configurationOverride)
            await Fixture.SelectTenantAsync(AiProviderSettings.ProviderOpenAiCompatible,
                "https://provider.example/v1", AiCredentialScope.Tenant);
        var provider = CreateProvider(providerId, client, limits, scope);

        if (configurationOverride && !tenant)
        {
            var denied = await provider.SendAsync(Payload(new AiChatProviderConfiguration(
                providerId, "https://provider.example/v1", AiCredentialScope.Tenant, "model")));
            await Assert.That(denied.Error!.Code).IsEqualTo("provider_configuration_changed");
            await Assert.That(body.BytesRead).IsEqualTo(0);
        }
        var result = await provider.SendAsync(Payload(tenant
            ? new AiChatProviderConfiguration(providerId, "https://provider.example/v1", scope, "model")
            : null));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("response_too_large");
        await Assert.That(result.Error.IsTransient).IsFalse();
        await Assert.That(body.BytesRead).IsEqualTo(129);
        await Assert.That(body.Disposed).IsTrue();
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    public async Task DirectAdapter_RejectsUnknownScalarWithoutRelyingOnNamedClientHandler(int providerId)
    {
        using var body = new ObservedStream(Encoding.UTF8.GetBytes(
            "{\"unknown\":\"" + new string('s', 65) + "\"}"));
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body)
        });
        using var client = new HttpClient(handler);
        var result = await CreateProvider(providerId, client,
            new AiResponseLimits { MaximumScalarBytes = 64 }).SendAsync(Payload());

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("response_scalar_limit");
        await Assert.That(result.Error.Message).DoesNotContain(new string('s', 65));
        await Assert.That(body.Disposed).IsTrue();
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    public async Task Discovery_KnownOversizeRejectsBeforeReadWithoutSuccessfulCatalogueFallback(int providerId)
    {
        using var body = new ObservedStream(Encoding.UTF8.GetBytes("{\"data\":[]}"));
        using var handler = new ResponseHandler(() =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = 1025;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler);
        var provider = (IAiModelCatalog)CreateProvider(providerId, client,
            new AiResponseLimits { MaximumDiscoveryBytes = 1024 });

        await Assert.That(async () => await provider.ListAvailableModelsAsync())
            .Throws<ProviderResponseRejectedException>();
        await Assert.That(body.BytesRead).IsEqualTo(0);
        await Assert.That(body.Disposed).IsTrue();
    }

    [Test]
    [Arguments(AiModelDiscoveryScope.Tenant)]
    [Arguments(AiModelDiscoveryScope.Instance)]
    public async Task DiscoveryGateway_ReturnsSecurityFailureRatherThanSuccessfulEmptyModels(AiModelDiscoveryScope scope)
    {
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[{\"id\":\"" + new string('m', 257) + "\"}]}")
        });
        using var client = new HttpClient(handler);
        var gateway = new AiModelDiscoveryGateway(new ClientFactory(client),
            new AiProviderConfigurationResolver(Options.Create(new AiProviderSettings
            {
                Enabled = true,
                Provider = AiProviderSettings.ProviderOpenAiCompatible,
                EndpointUrl = "https://provider.example/v1",
                CredentialScope = AiCredentialScope.None,
                ModelId = "model"
            }), Substitute.For<IHierarchicalSettingsResolver>(), Substitute.For<ITenantContext>(),
                Substitute.For<ISecretResolver>(), new AiProviderSettingsValidator()),
            Options.Create(new AiResponseLimits()));

        var result = await gateway.DiscoverAsync(new AiAssistantModelDiscoveryRequestDto
        {
            Scope = scope,
            EndpointUrl = "https://provider.example/v1",
            CredentialScope = AiCredentialScope.None
        }, CancellationToken.None);

        await Assert.That(result.FailureCode).IsEqualTo("response_identifier_limit");
        await Assert.That(result.Models.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("{\"choices\":[{},{},{}]}", "response_choice_limit")]
    [Arguments("{\"choices\":[{\"message\":{\"tool_calls\":[{},{}]}},{\"message\":{\"tool_calls\":[{}]}}]}", "response_tool_call_limit")]
    [Arguments("{\"content\":[{\"type\":\"tool_use\",\"input\":{\"x\":\"123456789\"}}]}", "response_tool_argument_limit")]
    [Arguments("{\"output\":[{\"type\":\"function_call\",\"arguments\":\"123456789\"}]}", "response_tool_argument_limit")]
    public async Task SemanticLimits_RejectBeforeMaterialization(string json, string category)
    {
        var limits = new AiResponseLimits
        {
            MaximumChoices = 2,
            MaximumToolCalls = 2,
            MaximumToolArgumentBytes = 8
        };
        using var content = new StringContent(json);
        ProviderResponseRejectedException? rejection = null;
        try
        {
            await AiResponseReader.ReadAsync(content, limits, discovery: false);
        }
        catch (ProviderResponseRejectedException exception)
        {
            rejection = exception;
        }
        await Assert.That(rejection).IsNotNull();
        await Assert.That(rejection!.Category).IsEqualTo(category);
    }

    [Test]
    public async Task SemanticLimits_CountEscapedCaseInsensitivePropertyNames()
    {
        using var content = new StringContent("{\"Ch\\u006fices\":[{},{}]}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(content,
                new AiResponseLimits { MaximumChoices = 1 }, discovery: false))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    public async Task SemanticLimits_CountUnknownObjectMembers()
    {
        using var content = new StringContent("{\"unknown\":{\"a\":1,\"b\":2,\"c\":3}}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(content,
                new AiResponseLimits { MaximumCollectionItems = 2 }, discovery: false))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    public async Task SemanticLimits_CountEscapedCaseInsensitiveToolTypesAcrossContent()
    {
        using var content = new StringContent("{\"content\":[{\"type\":\"TOOL_\\u0055SE\"},{\"type\":\"tool_use\"}]}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(content,
                new AiResponseLimits { MaximumToolCalls = 1 }, discovery: false))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    [Arguments("{\"choices\":[{\"message\":{\"tool_calls\":[{\"type\":\"function_call\"}]}}]}")]
    [Arguments("{\"content\":[{\"type\":\"tool_use\",\"input\":{\"type\":\"tool_use\",\"choices\":[1,2]}}]}")]
    public async Task ToolCallLimit_DoesNotDoubleCountTypeOrArgumentBusinessValues(string json)
    {
        using var content = new StringContent(json);
        var bytes = await AiResponseReader.ReadAsync(content,
            new AiResponseLimits { MaximumChoices = 1, MaximumToolCalls = 1 }, discovery: false);
        await Assert.That(bytes.Length).IsEqualTo(Encoding.UTF8.GetByteCount(json));
    }

    [Test]
    [Arguments("gzip")]
    [Arguments("br")]
    [Arguments("deflate")]
    public async Task CompressedResponses_AreRejectedBeforeBodyRead(string encoding)
    {
        using var body = new ObservedStream(Encoding.UTF8.GetBytes("{}"));
        using var handler = new ResponseHandler(() =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentEncoding.Add(encoding);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var client = new HttpClient(handler);
        var result = await CreateProvider(AiProviderSettings.ProviderOpenAiCompatible, client, new()).SendAsync(Payload());

        await Assert.That(result.Error!.Code).IsEqualTo("response_encoding_denied");
        await Assert.That(body.BytesRead).IsEqualTo(0);
        await Assert.That(body.Disposed).IsTrue();
    }

    [Test]
    public async Task DishonestLength_CannotBypassActualStreamCeiling()
    {
        using var body = new ObservedStream(Encoding.UTF8.GetBytes(new string(' ', 1024)));
        using var handler = new ResponseHandler(() =>
        {
            var content = new StreamContent(body);
            content.Headers.ContentLength = 2;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            response.Headers.TransferEncodingChunked = true;
            return response;
        });
        using var client = new HttpClient(handler);
        var result = await CreateProvider(AiProviderSettings.ProviderOpenAiCompatible, client,
            new AiResponseLimits { MaximumChatBytes = 128 }).SendAsync(Payload());

        await Assert.That(result.Error!.Code).IsEqualTo("response_too_large");
        await Assert.That(body.BytesRead).IsEqualTo(129);
    }

    [Test]
    public async Task DeepUnknownField_IsRejectedBeforeMapping()
    {
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"unknown\":" + new string('[', 33) + "0" + new string(']', 33) + "}")
        });
        using var client = new HttpClient(handler);
        var result = await CreateProvider(AiProviderSettings.ProviderOpenAiCompatible, client, new()).SendAsync(Payload());
        await Assert.That(result.Error!.Code).IsEqualTo("response_depth_limit");
    }

    [Test]
    public async Task DefaultScalarCeiling_ExactBoundarySucceedsAndOverflowRejects()
    {
        using var exact = new StringContent("{\"unknown\":\"" + new string('s', 256 * 1024) + "\"}");
        var accepted = await AiResponseReader.ReadAsync(exact, new(), discovery: false);
        await Assert.That(accepted.Length > 256 * 1024).IsTrue();

        using var overflow = new StringContent("{\"unknown\":\"" + new string('s', 256 * 1024 + 1) + "\"}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(overflow, new(), discovery: false))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    [Arguments("choices", 16)]
    [Arguments("tool_calls", 128)]
    [Arguments("unknown", 256)]
    public async Task DefaultCollectionCeilings_ExactBoundarySucceedsAndOverflowRejects(string field, int maximum)
    {
        string items = string.Join(',', Enumerable.Repeat("{}", maximum));
        using var exact = new StringContent("{\"" + field + "\":[" + items + "]}");
        await AiResponseReader.ReadAsync(exact, new(), discovery: false);
        using var overflow = new StringContent("{\"" + field + "\":[" + items + ",{}]}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(overflow, new(), discovery: false))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    public async Task DefaultToolArgumentCeiling_ExactBoundarySucceedsAndOverflowRejects()
    {
        using var exact = new StringContent("{\"arguments\":\"" + new string('a', 65536) + "\"}");
        await AiResponseReader.ReadAsync(exact, new(), discovery: false);
        using var overflow = new StringContent("{\"arguments\":\"" + new string('a', 65537) + "\"}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(overflow, new(), discovery: false))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    public async Task EscapedIdentifiers_AreLimitedByDecodedCharactersBeforeStringAllocation()
    {
        string identifier = string.Concat(Enumerable.Repeat("\\u006d", 256));
        using var exact = new StringContent("{\"data\":[{\"id\":\"" + identifier + "\"}]}");
        await AiResponseReader.ReadAsync(exact, new(), discovery: true);
        using var overflow = new StringContent("{\"data\":[{\"id\":\"" + identifier + "\\u006d\"}]}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(overflow, new(), discovery: true))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    public async Task Discovery_ModelCountIncludesDuplicateDataCollections()
    {
        using var content = new StringContent("{\"data\":[{\"id\":\"a\"},{\"id\":\"b\"}],\"data\":[{\"id\":\"c\"}]}");
        await Assert.That(async () => await AiResponseReader.ReadAsync(content,
                new AiResponseLimits { MaximumCollectionItems = 2 }, discovery: true))
            .Throws<ProviderResponseRejectedException>();
    }

    [Test]
    public async Task InvalidBudget_FailsBeforeReadingOrAllocatingProviderContent()
    {
        using var body = new ObservedStream(Encoding.UTF8.GetBytes("{}"));
        using var content = new StreamContent(body);
        await Assert.That(async () => await AiResponseReader.ReadAsync(content,
                new AiResponseLimits { MaximumChatBytes = int.MaxValue }, discovery: false))
            .Throws<ProviderResponseRejectedException>();
        await Assert.That(body.BytesRead).IsEqualTo(0);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    public async Task Anthropic_BoundedNativeTransportPreservesCustomHeadersAndEndpoint(int providerId)
    {
        string? key = null;
        string? version = null;
        Uri? uri = null;
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"content\":[{\"type\":\"text\",\"text\":\"bounded\"}],\"stop_reason\":\"end_turn\"}")
        }, request =>
        {
            uri = request.RequestUri;
            key = request.Headers.GetValues("x-api-key").Single();
            version = request.Headers.GetValues("anthropic-version").Single();
        });
        using var client = new HttpClient(handler);
        var result = await CreateProvider(providerId, client, new()).SendAsync(Payload());
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("bounded");
        await Assert.That(key).IsEqualTo(Fixture.Credential);
        await Assert.That(version).IsEqualTo("2023-06-01");
        await Assert.That(uri!.AbsolutePath).IsEqualTo("/v1/messages");
    }

    [Test]
    public async Task Discovery_ExactIdentifierAndByteLimitsSucceed()
    {
        string identifier = new('m', 256);
        string json = "{\"data\":[{\"id\":\"" + identifier + "\"}]}";
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });
        using var client = new HttpClient(handler);
        var models = await OpenAiCompatibleChatProvider.DiscoverModelsAsync(
            client, "https://provider.example/v1", null, CancellationToken.None,
            new AiResponseLimits { MaximumDiscoveryBytes = Encoding.UTF8.GetByteCount(json) });

        await Assert.That(models.Count).IsEqualTo(1);
        await Assert.That(models[0].Id).IsEqualTo(identifier);
    }

    [Test]
    public async Task AzureSdk_ConsumesOnlyValidatedBytesAndPreservesApiKeyHeader()
    {
        string? observedApiKey = null;
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"id\":\"completion\",\"object\":\"chat.completion\",\"created\":1,\"model\":\"model\"," +
                "\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"bounded\"},\"finish_reason\":\"stop\"}]}")
        }, request => observedApiKey = request.Headers.GetValues("api-key").Single());
        using var client = new HttpClient(new AiResponseBudgetHandler(new AiResponseLimits()) { InnerHandler = handler });
        var result = await CreateProvider(AiProviderSettings.ProviderAzureOpenAi, client, new()).SendAsync(Payload());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("bounded");
        await Assert.That(observedApiKey).IsEqualTo(Fixture.Credential);
    }

    [Test]
    [Arguments(408)]
    [Arguments(429)]
    [Arguments(503)]
    public async Task AzureSdk_TransientFailureDispatchesOnceAndDisposesUnreadBody(int statusCode)
    {
        int dispatches = 0;
        using var body = new ObservedStream(Encoding.UTF8.GetBytes(new string('x', 4096)));
        using var handler = new ResponseHandler(() => new HttpResponseMessage((HttpStatusCode)statusCode)
        {
            Content = new StreamContent(body)
        }, _ => dispatches++);
        using var client = new HttpClient(new AiResponseBudgetHandler(new()) { InnerHandler = handler });
        var result = await CreateProvider(AiProviderSettings.ProviderAzureOpenAi, client, new()).SendAsync(Payload());

        await Assert.That(result.Error!.Code).IsEqualTo($"http_{statusCode}");
        await Assert.That(result.Error.IsTransient).IsTrue();
        await Assert.That(dispatches).IsEqualTo(1);
        await Assert.That(body.BytesRead).IsEqualTo(0);
        await Assert.That(body.Disposed).IsTrue();
    }

    [Test]
    public async Task SdkAdapter_UnknownProfileIsRejectedBeforeClientInvocation()
    {
        using var handler = new ResponseHandler(() => throw new InvalidOperationException("HTTP must not be dispatched."));
        using var client = new HttpClient(handler);
        var factory = new ClientFactory(client);
        var provider = new MicrosoftExtensionsAiChatProvider(
            Fixture.Configuration(Settings(AiProviderSettings.ProviderOpenAiCompatible)),
            Fixture.Metrics, factory);
        var result = await provider.SendAsync(Payload());

        await Assert.That(result.Error!.Code).IsEqualTo("provider_transport_unsupported");
        await Assert.That(result.Error.IsTransient).IsFalse();
        await Assert.That((await provider.ListAvailableModelsAsync()).Count).IsEqualTo(0);
        await Assert.That(factory.ClientsCreated).IsEqualTo(0);
    }

    [Test]
    [Arguments(AiProviderSettings.ProviderOpenAiCompatible)]
    [Arguments(AiProviderSettings.ProviderOpenAi)]
    [Arguments(AiProviderSettings.ProviderAnthropicCompatible)]
    [Arguments(AiProviderSettings.ProviderAnthropic)]
    [Arguments(AiProviderSettings.ProviderAzureOpenAi)]
    public async Task Completion_CallerCancellationDuringReadPropagatesAndClosesStream(int providerId)
    {
        using var body = new ObservedStream([], block: true);
        using var handler = new ResponseHandler(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body)
        });
        using var client = new HttpClient(providerId == AiProviderSettings.ProviderAzureOpenAi
            ? new AiResponseBudgetHandler(new()) { InnerHandler = handler }
            : handler);
        using var cancellation = new CancellationTokenSource();
        var operation = CreateProvider(providerId, client, new())
            .SendAsync(Payload(), cancellation.Token);
        await body.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.That(async () => await operation.WaitAsync(TimeSpan.FromSeconds(5))).Throws<OperationCanceledException>();
        await Assert.That(body.Disposed).IsTrue();
    }

    private static AiProviderSettings Settings(int providerId, AiCredentialScope scope = AiCredentialScope.Instance) => new()
    {
        Enabled = true,
        Provider = providerId,
        EndpointUrl = "https://provider.example/v1",
        CredentialScope = scope,
        ModelId = "model"
    };

    private IAiChatProvider CreateProvider(int providerId, HttpClient client, AiResponseLimits limits,
        AiCredentialScope scope = AiCredentialScope.Instance)
    {
        var configuration = Fixture.Configuration(Settings(providerId, scope));
        var factory = new ClientFactory(client);
        return Fixture.Provider(providerId, configuration, factory, limits);
    }

    private static AiChatPayload Payload(AiChatProviderConfiguration? configuration = null) =>
        new("model", [new AiChatMessage(AiMessageRole.User, "Hello")],
            string.Empty, new AiChatOptions(8000, 1024, 0.2m, 30, false, false), ProviderConfiguration: configuration);

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public int ClientsCreated { get; private set; }
        public HttpClient CreateClient(string name)
        {
            ClientsCreated++;
            return client;
        }
    }

    private sealed class ResponseHandler(Func<HttpResponseMessage> response, Action<HttpRequestMessage>? observe = null)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            observe?.Invoke(request);
            return Task.FromResult(response());
        }
    }

    private sealed class ObservedStream(byte[] bytes, bool block = false) : Stream
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int BytesRead { get; private set; }
        public bool Disposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            if (block)
            {
                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => cancelled.TrySetCanceled(cancellationToken));
                await cancelled.Task;
            }
            int count = Math.Min(buffer.Length, bytes.Length - BytesRead);
            bytes.AsMemory(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
