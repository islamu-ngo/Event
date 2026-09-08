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

public sealed class OpenAiResponsesChatProviderTests
{
    [Test]
    public async Task ListAvailableModels_WhenModelsEndpointResponds_ReturnsDiscoveredModels()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "data": [
                { "id": "gpt-5.4-mini" },
                { "id": "gpt-5.4" }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var models = await provider.ListAvailableModelsAsync();

        await Assert.That(models.Select(model => model.Id).ToArray()).IsEquivalentTo(["gpt-5.4", "gpt-5.4-mini"]);
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://api.openai.com/v1/models"));
        await Assert.That(handler.Authorization?.Scheme).IsEqualTo("Bearer");
        await Assert.That(handler.Authorization?.Parameter).IsEqualTo("secret-key");
    }

    [Test]
    public async Task SendAsync_PostsResponsesRequestWithBearerTokenStoreAndSafeBody()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "Assistant response" }
                  ]
                }
              ],
              "usage": { "input_tokens": 12, "output_tokens": 4, "total_tokens": 16 }
            }
            """, response => response.Headers.Add("x-request-id", "req_test")));
        var provider = CreateProvider(handler, CreateSettings(apiKey: "secret-key"));

        var result = await provider.SendAsync(CreateRequest("write a haiku about ai"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://api.openai.com/v1/responses"));
        await Assert.That(handler.Authorization?.Scheme).IsEqualTo("Bearer");
        await Assert.That(handler.Authorization?.Parameter).IsEqualTo("secret-key");
        await Assert.That(handler.RequestBody).DoesNotContain("secret-key");
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Assistant response");
        await Assert.That(result.Response.ProviderRequestId).IsEqualTo("req_test");
        await Assert.That(result.Response.Usage.TotalTokens).IsEqualTo(16);

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("gpt-5.4-mini");
        await Assert.That(root.GetProperty("store").GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty("stream").GetBoolean()).IsFalse();
        await Assert.That(root.GetProperty("max_output_tokens").GetInt32()).IsEqualTo(1024);
        await Assert.That(root.GetProperty("temperature").GetDecimal()).IsEqualTo(0.2m);
        await Assert.That(root.GetProperty("input").GetString()).Contains("System:");
        await Assert.That(root.GetProperty("input").GetString()).Contains("write a haiku about ai");
    }

    [Test]
    public async Task SendAsync_WhenUserMessageHasImage_PostsResponsesInputImageContent()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "This is an event flyer." }
                  ]
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
                new AiChatImage("image/png", imageData)
            ]));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var input = document.RootElement.GetProperty("input");
        await Assert.That(input.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(input[0].GetProperty("role").GetString()).IsEqualTo("system");
        await Assert.That(input[1].GetProperty("role").GetString()).IsEqualTo("user");

        var content = input[1].GetProperty("content");
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("input_text");
        await Assert.That(content[0].GetProperty("text").GetString()).IsEqualTo("Describe this picture:");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("input_image");
        await Assert.That(content[1].GetProperty("image_url").GetString())
            .IsEqualTo($"data:image/png;base64,{imageData}");
        await Assert.That(content[1].GetProperty("detail").GetString()).IsEqualTo("auto");
    }

    [Test]
    public async Task SendAsync_WhenImageBackedBuildModeReturnsBlankTextWithoutFunctionCall_ReturnsRetryableEmptyResponseFailure()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "" }
                  ]
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
        await Assert.That(root.GetProperty("tools")[0].GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        var content = root.GetProperty("input")[1].GetProperty("content");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("input_image");
    }

    [Test]
    public async Task SendAsync_WhenProviderReturnsFunctionCall_ReturnsCreateEventDraftCandidate()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "function_call",
                  "name": "create_event_draft",
                  "arguments": "{\"title\":\"Community Dinner\"}"
                }
              ],
              "usage": { "input_tokens": 20, "output_tokens": 10, "total_tokens": 30 }
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
        await Assert.That(result.Response.FinishReason).IsEqualTo("completed");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var tool = document.RootElement.GetProperty("tools")[0];
        await Assert.That(tool.GetProperty("type").GetString()).IsEqualTo("function");
        await Assert.That(tool.GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(tool.GetProperty("strict").GetBoolean()).IsTrue();
        await Assert.That(tool.GetProperty("parameters").GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(document.RootElement.GetProperty("tool_choice").GetString()).IsEqualTo("auto");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputEnabled_SendsTextFormatAndParsesAssistantMessage()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "{\"message\":\"Structured assistant response\"}" }
                  ]
                }
              ],
              "usage": { "input_tokens": 12, "output_tokens": 4, "total_tokens": 16 }
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
        var format = document.RootElement.GetProperty("text").GetProperty("format");
        await Assert.That(format.GetProperty("type").GetString()).IsEqualTo("json_schema");
        await Assert.That(format.GetProperty("name").GetString()).IsEqualTo("assistant_message");
        await Assert.That(format.GetProperty("strict").GetBoolean()).IsTrue();
        await Assert.That(format.GetProperty("schema").GetProperty("additionalProperties").GetBoolean()).IsFalse();
    }

    [Test]
    public async Task SendAsync_WhenRuntimeProviderConfigurationIsUsed_UsesResponsesEndpointAndSelectedModel()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "resp_test",
              "status": "completed",
              "output": [
                {
                  "type": "message",
                  "content": [
                    { "type": "output_text", "text": "Tenant OpenAI response" }
                  ]
                }
              ]
            }
            """));
        var provider = CreateProvider(handler, CreateSettings(modelId: "static-model"));

        var result = await provider.SendAsync(CreateRequest(
            "Hello",
            providerConfiguration: new AiChatProviderConfiguration(
                AiProviderSettings.ProviderOpenAi,
                string.Empty,
                "tenant-key",
                "tenant-model")));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://api.openai.com/v1/responses"));
        await Assert.That(handler.Authorization?.Parameter).IsEqualTo("tenant-key");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        await Assert.That(document.RootElement.GetProperty("model").GetString()).IsEqualTo("gpt-5.4-mini");
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

    private static OpenAiResponsesChatProvider CreateProvider(
        HttpMessageHandler handler,
        AiProviderSettings settings)
    {
        var client = new HttpClient(handler);
        var factory = new StaticHttpClientFactory(client);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new OpenAiResponsesChatProvider(
            factory,
            Options.Create(settings),
            new AiProviderSettingsValidator(),
            new BusinessMetrics(meterFactory));
    }

    private static AiProviderSettings CreateSettings(
        string apiKey = "secret-key",
        string modelId = "gpt-5.4-mini") => new()
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAi,
            ApiKey = apiKey,
            ModelId = modelId,
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
        AiChatProviderConfiguration? providerConfiguration = null,
        IReadOnlyList<AiChatImage>? images = null) => new(
            "gpt-5.4-mini",
            [new AiChatMessage(AiMessageRole.User, userMessage, images: images)],
            "You are a safe assistant.",
            new AiChatOptions(
                8000,
                1024,
                0.2m,
                30,
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

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
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
            return responseFactory(request);
        }
    }
}
