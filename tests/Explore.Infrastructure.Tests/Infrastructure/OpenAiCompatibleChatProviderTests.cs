using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class OpenAiCompatibleChatProviderTests
{
    [Test]
    public async Task ListAvailableModels_WhenModelsEndpointResponds_ReturnsDiscoveredModels()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "data": [
                { "id": "gpt-z" },
                { "id": "gpt-a" }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var models = await provider.ListAvailableModelsAsync();

        await Assert.That(models.Select(model => model.Id).ToArray()).IsEquivalentTo(["gpt-a", "gpt-z"]);
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://ai.example.test/v1/models"));
        await Assert.That(handler.Calls).IsEqualTo(1);
    }

    [Test]
    public async Task ListAvailableModels_WhenModelsEndpointFails_ReturnsConfiguredModel()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("{}", statusCode: HttpStatusCode.NotFound));
        var provider = CreateProvider(handler, CreateSettings());

        var models = await provider.ListAvailableModelsAsync();

        await Assert.That(models.Count).IsEqualTo(1);
        await Assert.That(models[0].Id).IsEqualTo("gpt-test");
        await Assert.That(models[0].SupportsToolProposals).IsTrue();
        await Assert.That(models[0].SupportsStreaming).IsFalse();
    }

    [Test]
    public async Task DiscoverModelsAsync_WhenEndpointIsChatCompletionsUrl_UsesModelsEndpointAndBearerToken()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "data": [
                { "id": "gpt-z" },
                { "id": "gpt-a" },
                { "id": "" }
              ]
            }
            """));
        using var client = new HttpClient(handler);

        var models = await OpenAiCompatibleChatProvider.DiscoverModelsAsync(
            client,
            "https://ai.example.test/v1/chat/completions",
            "secret-key");

        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://ai.example.test/v1/models"));
        await Assert.That(handler.Authorization?.Scheme).IsEqualTo("Bearer");
        await Assert.That(handler.Authorization?.Parameter).IsEqualTo("secret-key");
        await Assert.That(models.Select(model => model.Id).ToArray()).IsEquivalentTo(["gpt-a", "gpt-z"]);
    }

    [Test]
    public async Task SendAsync_PostsChatCompletionRequestWithBearerTokenAndSafeBody()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "Assistant response" },
                  "finish_reason": "stop"
                }
              ],
              "usage": { "prompt_tokens": 12, "completion_tokens": 4, "total_tokens": 16 }
            }
            """, response => response.Headers.Add("x-request-id", "req_test")));
        var provider = CreateProvider(handler, CreateSettings(apiKey: "secret-key"));

        var result = await provider.SendAsync(CreateRequest("Plan a community dinner"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://ai.example.test/v1/chat/completions"));
        await Assert.That(handler.Authorization?.Scheme).IsEqualTo("Bearer");
        await Assert.That(handler.Authorization?.Parameter).IsEqualTo("secret-key");
        await Assert.That(handler.RequestBody).DoesNotContain("secret-key");
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Assistant response");
        await Assert.That(result.Response.ProviderRequestId).IsEqualTo("req_test");
        await Assert.That(result.Response.Usage.TotalTokens).IsEqualTo(16);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("gpt-test");
        await Assert.That(root.GetProperty("stream").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("messages").GetArrayLength()).IsEqualTo(2);
        await Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString()).IsEqualTo("system");
        await Assert.That(root.GetProperty("messages")[1].GetProperty("role").GetString()).IsEqualTo("user");
    }

    [Test]
    public async Task SendAsync_WhenUserMessageHasImage_PostsOpenAiImageUrlContentParts()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "This is an event flyer." },
                  "finish_reason": "stop"
                }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());
        const string imageData = "aW1hZ2UtYnl0ZXM=";

        var result = await provider.SendAsync(CreateRequest(
            "Describe this picture:",
            images:
            [
                new AiChatImage("image/jpeg", imageData)
            ]));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var message = document.RootElement.GetProperty("messages")[1];
        var content = message.GetProperty("content");
        await Assert.That(message.GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(content.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(content[0].GetProperty("text").GetString()).IsEqualTo("Describe this picture:");

        var imagePart = content[1];
        await Assert.That(imagePart.GetProperty("type").GetString()).IsEqualTo("image_url");
        await Assert.That(imagePart.GetProperty("image_url").GetProperty("url").GetString())
            .IsEqualTo($"data:image/jpeg;base64,{imageData}");
    }

    [Test]
    public async Task SendAsync_WhenLocalEndpointHasImageAndToolProposal_DisablesTemplateThinking()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": {
                    "content": "",
                    "tool_calls": [
                      {
                        "type": "function",
                        "function": {
                          "name": "create_event_draft",
                          "arguments": "{\"title\":\"Community Dinner\"}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ]
            }
            """));
        var settings = CreateSettings();
        settings.EndpointUrl = "http://127.0.0.1:1337/v1";
        settings.AllowLocalProviderEndpoints = true;
        var provider = CreateProvider(handler, settings);
        const string imageData = "aW1hZ2UtYnl0ZXM=";

        var result = await provider.SendAsync(CreateRequest(
            "Create event draft for this event poster",
            toolProposalsEnabled: true,
            actionSchema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title"],
                  "properties": { "title": { "type": "string" } }
                }
                """,
            images:
            [
                new AiChatImage("image/png", imageData)
            ]));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString())
            .IsEqualTo("create_event_draft");
        await Assert.That(root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("type").GetString())
            .IsEqualTo("image_url");
    }

    [Test]
    public async Task SendAsync_WhenExternalEndpointIsUsed_DoesNotSendTemplateThinkingOverride()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "Assistant response" },
                  "finish_reason": "stop"
                }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        await Assert.That(document.RootElement.TryGetProperty("chat_template_kwargs", out _)).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenImageBackedBuildModeReturnsBlankTextWithoutToolCall_ReturnsRetryableEmptyResponseFailure()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "" },
                  "finish_reason": "stop"
                }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());
        const string imageData = "aW1hZ2UtYnl0ZXM=";

        var result = await provider.SendAsync(CreateRequest(
            "Create event draft for this event poster",
            toolProposalsEnabled: true,
            actionSchema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title"],
                  "properties": { "title": { "type": "string" } }
                }
                """,
            images:
            [
                new AiChatImage("image/png", imageData)
            ]));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_response");
        await Assert.That(result.Error.Message).Contains("empty text response");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString())
            .IsEqualTo("create_event_draft");
        var content = root.GetProperty("messages")[1].GetProperty("content");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("image_url");
    }

    [Test]
    public async Task SendAsync_WhenProviderReturnsToolCall_ReturnsCreateEventDraftCandidate()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": {
                    "content": "",
                    "tool_calls": [
                      {
                        "type": "function",
                        "function": {
                          "name": "create_event_draft",
                          "arguments": "{\"title\":\"Community Dinner\"}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ],
              "usage": { "prompt_tokens": 20, "completion_tokens": 10, "total_tokens": 30 }
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest(
            "Create an event draft",
            toolProposalsEnabled: true,
            actionSchema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title"],
                  "properties": { "title": { "type": "string" } }
                }
                """));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.ProposedActions.Count).IsEqualTo(1);
        await Assert.That(result.Response.ProposedActions[0].Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(result.Response.ProposedActions[0].PayloadJson).Contains("Community Dinner");
        await Assert.That(result.Response.FinishReason).IsEqualTo("tool_calls");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var tool = document.RootElement.GetProperty("tools")[0];
        await Assert.That(tool.GetProperty("function").GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(tool.GetProperty("function").GetProperty("strict").GetBoolean()).IsTrue();
        await Assert.That(tool.GetProperty("function").GetProperty("parameters").GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(tool.GetProperty("function").GetProperty("parameters").GetProperty("required")[0].GetString()).IsEqualTo("title");
        await Assert.That(document.RootElement.GetProperty("tool_choice").GetString()).IsEqualTo("auto");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputEnabled_SendsResponseFormatAndParsesAssistantMessage()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "{\"message\":\"Structured assistant response\"}" },
                  "finish_reason": "stop"
                }
              ],
              "usage": { "prompt_tokens": 12, "completion_tokens": 4, "total_tokens": 16 }
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest(
            "Summarize the plan",
            structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Structured assistant response");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var responseFormat = document.RootElement.GetProperty("response_format");
        await Assert.That(responseFormat.GetProperty("type").GetString()).IsEqualTo("json_schema");
        await Assert.That(responseFormat.GetProperty("json_schema").GetProperty("name").GetString()).IsEqualTo("assistant_message");
        await Assert.That(responseFormat.GetProperty("json_schema").GetProperty("strict").GetBoolean()).IsTrue();
        await Assert.That(responseFormat.GetProperty("json_schema").GetProperty("schema").GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(document.RootElement.TryGetProperty("tools", out _)).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputDoesNotMatchSchema_ReturnsSafeFailure()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "{\"summary\":\"wrong shape\"}" },
                  "finish_reason": "stop"
                }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest(
            "Summarize the plan",
            structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_structured_output");
        await Assert.That(result.Error.Message).DoesNotContain("wrong shape");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputCombinesWithToolProposals_FailsBeforeHttpCall()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest(
            "Create an event draft",
            toolProposalsEnabled: true,
            actionSchema: "{\"type\":\"object\"}",
            structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("structured_output_conflict");
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenProviderReturnsHttpFailure_ReturnsSafeTransientError()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse(
            "{\"error\":{\"message\":\"secret prompt leaked\"}}",
            statusCode: HttpStatusCode.TooManyRequests));
        var provider = CreateProvider(handler, CreateSettings(apiKey: "secret-key"));

        var result = await provider.SendAsync(CreateRequest("Sensitive prompt body"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("http_429");
        await Assert.That(result.Error.IsTransient).IsTrue();
        await Assert.That(result.Error.Message).DoesNotContain("secret");
        await Assert.That(result.Error.Message).DoesNotContain("Sensitive prompt body");
    }

    [Test]
    public async Task SendAsync_WhenProviderReturnsContentFilterFinishReason_ReturnsStableSafeFailureCode()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "choices": [
                {
                  "message": { "content": "" },
                  "finish_reason": "content_filter"
                }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest("Sensitive prompt body"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("content_filtered");
        await Assert.That(result.Error.Message).DoesNotContain("Sensitive prompt body");
    }

    [Test]
    public async Task SendAsync_WhenProviderNotConfigured_DoesNotCallHttp()
    {
        var handler = new RecordingMessageHandler(_ => throw new InvalidOperationException("HTTP should not be called."));
        var provider = CreateProvider(handler, new AiProviderSettings());

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_not_configured");
        await Assert.That(handler.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenHttpClientTimesOut_ReturnsTransientTimeoutFailure()
    {
        var handler = new RecordingMessageHandler(_ => throw new TaskCanceledException("simulated timeout"));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest("Hello"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("provider_timeout");
        await Assert.That(result.Error.IsTransient).IsTrue();
    }

    [Test]
    public async Task SendAsync_WhenRuntimeProviderConfigurationIsUsed_UsesRequestTimeoutInsteadOfStaticDefault()
    {
        var handler = new DelayedSuccessMessageHandler(TimeSpan.FromMilliseconds(1100));
        var settings = CreateSettings();
        settings.TimeoutSeconds = 1;
        var provider = CreateProvider(handler, settings);

        var result = await provider.SendAsync(CreateRequest(
            "Hello",
            timeoutSeconds: 2,
            providerConfiguration: new AiChatProviderConfiguration(
                AiProviderSettings.ProviderOpenAiCompatible,
                "https://tenant-ai.example.test/v1",
                string.Empty,
                "tenant-model")));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.Calls).IsEqualTo(1);
    }

    private static OpenAiCompatibleChatProvider CreateProvider(
        HttpMessageHandler handler,
        AiProviderSettings settings)
    {
        var client = new HttpClient(handler);
        var factory = new StaticHttpClientFactory(client);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new OpenAiCompatibleChatProvider(
            factory,
            Options.Create(settings),
            new AiProviderSettingsValidator(),
            new BusinessMetrics(meterFactory));
    }

    private static AiProviderSettings CreateSettings(string apiKey = "test-key") => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderOpenAiCompatible,
        EndpointUrl = "https://ai.example.test/v1",
        ApiKey = apiKey,
        ModelId = "gpt-test",
        MaxInputTokens = 8000,
        MaxOutputTokens = 1024,
        TimeoutSeconds = 30
    };

    private static AiChatPayload CreateRequest(
        string userMessage,
        bool toolProposalsEnabled = false,
        string? actionSchema = null,
        bool structuredOutputEnabled = false,
        AiStructuredOutputSchema? structuredOutputSchema = null,
        int timeoutSeconds = 30,
        AiChatProviderConfiguration? providerConfiguration = null,
        IReadOnlyList<AiChatImage>? images = null) => new(
            "gpt-test",
            [new AiChatMessage(AiMessageRole.User, userMessage, images: images)],
            "You are a safe assistant.",
            new AiChatOptions(
                8000,
                1024,
                0.2m,
                timeoutSeconds,
                toolProposalsEnabled,
                StreamingEnabled: false,
                StructuredOutputEnabled: structuredOutputEnabled),
            actionSchema is null
                ? null
                : new AiStructuredActionSchema([AiProposedActionKind.CreateEventDraft], actionSchema),
            structuredOutputSchema,
            ProviderConfiguration: providerConfiguration);

    private static HttpResponseMessage JsonResponse(
        string json,
        Action<HttpResponseMessage>? configure = null,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        configure?.Invoke(response);
        return response;
    }

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
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _responseFactory(request);
        }
    }

    private sealed class DelayedSuccessMessageHandler(TimeSpan delay) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(delay, cancellationToken);
            return JsonResponse("""
                {
                  "choices": [
                    {
                      "message": { "content": "Delayed assistant response" },
                      "finish_reason": "stop"
                    }
                  ]
                }
                """);
        }
    }
}
