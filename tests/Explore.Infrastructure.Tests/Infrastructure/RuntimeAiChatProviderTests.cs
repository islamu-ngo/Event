using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class RuntimeAiChatProviderTests
{
    [Test]
    public async Task SendAsync_WhenDisabled_FailsWithoutCallingHttp()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var provider = CreateProvider(new AiProviderSettings(), handler);

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_disabled");
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenFakeProviderConfigured_UsesDeterministicFakeProvider()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var provider = CreateProvider(new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderFake
        }, handler);

        var result = await provider.SendAsync(CreateRequest("Plan a community dinner"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).Contains("Plan a community dinner");
        await Assert.That(result.Response.ProviderRequestId).IsEqualTo("fake-provider");
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task ListAvailableModels_WhenFakeProviderConfigured_ReturnsFakeModel()
    {
        var provider = CreateProvider(new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderFake
        });

        var models = await provider.ListAvailableModelsAsync();

        await Assert.That(models.Count).IsEqualTo(1);
        await Assert.That(models[0].Id).IsEqualTo(FakeAiChatProvider.ModelId);
    }

    [Test]
    public async Task SendAsync_WhenOpenAiCompatibleConfigured_DelegatesToOpenAiAdapter()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                { "message": { "content": "OpenAI-compatible response" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 8, "completion_tokens": 4, "total_tokens": 12 }
            }
            """));
        var provider = CreateProvider(CreateOpenAiSettings(), handler);

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("OpenAI-compatible response");
        await Assert.That(handler.Calls).IsEqualTo(1);
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://ai.example.test/v1/chat/completions"));
    }

    [Test]
    public async Task ListAvailableModels_WhenOpenAiCompatibleConfigured_ReturnsDiscoveredModels()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "data": [
                { "id": "gpt-z" },
                { "id": "gpt-a" }
              ]
            }
            """));
        var provider = CreateProvider(CreateOpenAiSettings(), handler);

        var models = await provider.ListAvailableModelsAsync();

        await Assert.That(models.Select(model => model.Id).ToArray()).IsEquivalentTo(["gpt-a", "gpt-z"]);
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://ai.example.test/v1/models"));
        await Assert.That(handler.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_WhenOpenAiConfigured_DelegatesToOpenAiResponsesAdapter()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "OpenAI response" }
                  ]
                }
              ],
              "usage": { "input_tokens": 8, "output_tokens": 4, "total_tokens": 12 }
            }
            """));
        var provider = CreateProvider(CreateOpenAiResponsesSettings(), handler);

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("OpenAI response");
        await Assert.That(handler.Calls).IsEqualTo(1);
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://api.openai.com/v1/responses"));
    }

    [Test]
    public async Task SendAsync_WhenAnthropicConfigured_DelegatesToAnthropicMessagesAdapter()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "text", "text": "Anthropic response" }
              ],
              "model": "claude-test",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 8, "output_tokens": 4 }
            }
            """));
        var provider = CreateProvider(CreateAnthropicSettings(), handler);

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Anthropic response");
        await Assert.That(handler.Calls).IsEqualTo(1);
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://api.anthropic.com/v1/messages"));
    }

    [Test]
    public async Task SendAsync_WhenConfiguredEndpointIsUnsafe_FailsBeforeHttpCall()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var provider = CreateProvider(new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = "http://localhost:11434/v1",
            ApiKey = "test-key",
            ModelId = "gpt-test"
        }, handler);

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_settings");
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenAzureOpenAiConfigured_DelegatesToMicrosoftExtensionsAdapter()
    {
        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAzureOpenAi,
            EndpointUrl = "https://ai.example.openai.azure.com/",
            ApiKey = "test-key",
            ModelId = "gpt-test"
        };
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "SDK response"))
        {
            FinishReason = ChatFinishReason.Stop
        });
        var sdkProvider = CreateMicrosoftExtensionsProvider(settings, chatClient);
        var provider = CreateProvider(settings, microsoftExtensionsProvider: sdkProvider);

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("SDK response");
        await Assert.That(chatClient.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task SendAsync_WhenSdkProviderConfiguredButAdapterMissing_FailsClosed()
    {
        var provider = CreateProvider(new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAzureOpenAi,
            EndpointUrl = "https://ai.example.openai.azure.com/",
            ApiKey = "test-key",
            ModelId = "gpt-test"
        });

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_not_configured");
    }

    private static RuntimeAiChatProvider CreateProvider(
        AiProviderSettings settings,
        RecordingMessageHandler? handler = null,
        MicrosoftExtensionsAiChatProvider? microsoftExtensionsProvider = null)
    {
        handler ??= new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var client = new HttpClient(handler);
        var factory = new StaticHttpClientFactory(client);
        var options = Options.Create(settings);
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns("Testing");
        var validator = new AiProviderSettingsValidator(environment);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        var fakeProvider = new FakeAiChatProvider();
        var openAiResponsesProvider = new OpenAiResponsesChatProvider(
            factory, options, validator, new BusinessMetrics(meterFactory));
        var openAiProvider = new OpenAiCompatibleChatProvider(
            factory, options, validator, new BusinessMetrics(meterFactory));
        var officialAnthropicProvider = new AnthropicChatProvider(
            factory, options, validator, new BusinessMetrics(meterFactory));
        var anthropicProvider = new AnthropicCompatibleChatProvider(
            factory, options, validator, new BusinessMetrics(meterFactory));

        var strategies = new List<IAiProviderStrategy>
        {
            new FakeAiProviderStrategy(fakeProvider),
            new OpenAiResponsesProviderStrategy(openAiResponsesProvider),
            new OpenAiCompatibleProviderStrategy(openAiProvider),
            new AnthropicProviderStrategy(officialAnthropicProvider),
            new AnthropicCompatibleProviderStrategy(anthropicProvider),
        };

        if (microsoftExtensionsProvider is not null)
            strategies.Add(new MicrosoftExtensionsProviderStrategy(microsoftExtensionsProvider));

        var resolver = new AiProviderStrategyResolver(
            strategies, Substitute.For<ILogger<AiProviderStrategyResolver>>());

        return new RuntimeAiChatProvider(options, validator, resolver);
    }

    private static MicrosoftExtensionsAiChatProvider CreateMicrosoftExtensionsProvider(
        AiProviderSettings settings,
        RecordingChatClient chatClient)
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new MicrosoftExtensionsAiChatProvider(
            chatClient,
            Options.Create(settings),
            new BusinessMetrics(meterFactory));
    }

    private static AiProviderSettings CreateOpenAiResponsesSettings() => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderOpenAi,
        ApiKey = "test-key",
        ModelId = "gpt-test",
        MaxInputTokens = 8000,
        MaxOutputTokens = 1024,
        TimeoutSeconds = 30
    };

    private static AiProviderSettings CreateOpenAiSettings() => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderOpenAiCompatible,
        EndpointUrl = "https://ai.example.test/v1",
        ApiKey = "test-key",
        ModelId = "gpt-test",
        MaxInputTokens = 8000,
        MaxOutputTokens = 1024,
        TimeoutSeconds = 30
    };

    private static AiProviderSettings CreateAnthropicSettings() => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderAnthropic,
        ApiKey = "test-key",
        ModelId = "claude-test",
        MaxInputTokens = 8000,
        MaxOutputTokens = 1024,
        TimeoutSeconds = 30
    };

    private static AiChatPayload CreateRequest(string userMessage) => new(
        "gpt-test",
        [new AiChatMessage(AiMessageRole.User, userMessage)],
        "You are a safe assistant.",
        new AiChatOptions(8000, 1024, 0.2m, 30, ToolProposalsEnabled: false, StreamingEnabled: false));

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StaticHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public StaticHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class RecordingMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            return Task.FromResult(_responseFactory(request));
        }
    }

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly ChatResponse _response;

        public RecordingChatClient(ChatResponse response)
        {
            _response = response;
        }

        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_response);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
