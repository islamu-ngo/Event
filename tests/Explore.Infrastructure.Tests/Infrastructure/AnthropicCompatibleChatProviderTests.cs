using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class AnthropicCompatibleChatProviderTests
{
    [Test]
    public async Task SendAsync_PostsMessagesRequestWithNativeToolsAndToolResults()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                {
                  "type": "tool_use",
                  "id": "call_2",
                  "name": "create_event_draft",
                  "input": { "title": "Community Dinner" }
                }
              ],
              "model": "claude-test",
              "stop_reason": "tool_use",
              "usage": { "input_tokens": 12, "output_tokens": 4 }
            }
            """, response => response.Headers.Add("x-request-id", "req_test")));
        var provider = CreateProvider(handler, CreateSettings(apiKey: "secret-key"));

        var result = await provider.SendAsync(CreateRequest(
            toolProposalsEnabled: true,
            actionSchema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title"],
                  "properties": { "title": { "type": "string" } }
                }
                """,
            messages:
            [
                new AiChatMessage(AiMessageRole.User, "Create an event draft"),
                new AiChatMessage(AiMessageRole.Tool, "{\"draftId\":\"draft_1\"}", "call_1")
            ]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("https://ai.example.test/v1/messages"));
        await Assert.That(handler.RequestHeaders!.GetValues("x-api-key").Single()).IsEqualTo("secret-key");
        await Assert.That(handler.RequestHeaders.GetValues("anthropic-version").Single()).IsEqualTo("2023-06-01");
        await Assert.That(handler.RequestBody).DoesNotContain("secret-key");
        await Assert.That(result.Response!.ProviderRequestId).IsEqualTo("req_test");
        await Assert.That(result.Response.ProposedActions.Count).IsEqualTo(1);
        await Assert.That(result.Response.ProposedActions[0].PayloadJson).Contains("Community Dinner");
        await Assert.That(result.Response.AssistantMessage).IsEqualTo("Event draft proposed.");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("model").GetString()).IsEqualTo("claude-test");
        await Assert.That(root.GetProperty("system").GetString()).IsEqualTo("You are a safe assistant.");
        await Assert.That(root.GetProperty("tools")[0].GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(root.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("additionalProperties").GetBoolean()).IsFalse();

        var toolMessage = root.GetProperty("messages")[1];
        var toolResult = toolMessage.GetProperty("content")[0];
        await Assert.That(toolMessage.GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(toolResult.GetProperty("type").GetString()).IsEqualTo("tool_result");
        await Assert.That(toolResult.GetProperty("tool_use_id").GetString()).IsEqualTo("call_1");
        await Assert.That(toolResult.GetProperty("content").GetString()).IsEqualTo("{\"draftId\":\"draft_1\"}");
    }

    [Test]
    public async Task SendAsync_WhenUserMessageHasImage_PostsAnthropicBase64ImageBlock()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "text", "text": "This is a C# code image." }
              ],
              "model": "claude-test",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 20, "output_tokens": 8 }
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());
        const string imageData = "aW1hZ2UtYnl0ZXM=";

        var result = await provider.SendAsync(CreateRequest(messages:
        [
            new AiChatMessage(
                AiMessageRole.User,
                "Describe this picture:",
                images:
                [
                    new AiChatImage("image/jpeg", imageData)
                ])
        ]));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var content = document.RootElement.GetProperty("messages")[0].GetProperty("content");
        await Assert.That(content.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(content[0].GetProperty("text").GetString()).IsEqualTo("Describe this picture:");

        var imageBlock = content[1];
        await Assert.That(imageBlock.GetProperty("type").GetString()).IsEqualTo("image");
        var source = imageBlock.GetProperty("source");
        await Assert.That(source.GetProperty("type").GetString()).IsEqualTo("base64");
        await Assert.That(source.GetProperty("media_type").GetString()).IsEqualTo("image/jpeg");
        await Assert.That(source.GetProperty("data").GetString()).IsEqualTo(imageData);
        await Assert.That(source.GetProperty("data").GetString()).DoesNotContain("data:image");
    }

    [Test]
    public async Task SendAsync_WhenImageBackedBuildModeReturnsTextlessToolUse_MapsEventDraftProposal()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                {
                  "type": "tool_use",
                  "id": "call_1",
                  "name": "create_event_draft",
                  "input": { "title": "Poster Draft" }
                }
              ],
              "model": "claude-test",
              "stop_reason": "tool_use",
              "usage": { "input_tokens": 24, "output_tokens": 10 }
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest(
            toolProposalsEnabled: true,
            actionSchema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title"],
                  "properties": { "title": { "type": "string" } }
                }
                """,
            messages:
            [
                new AiChatMessage(
                    AiMessageRole.User,
                    "Create an event draft from this poster:",
                    images:
                    [
                        new AiChatImage("image/png", "aW1hZ2UtYnl0ZXM=")
                    ])
            ]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Event draft proposed.");
        await Assert.That(result.Response.ProposedActions.Count).IsEqualTo(1);
        await Assert.That(result.Response.ProposedActions[0].PayloadJson).Contains("Poster Draft");

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("tools")[0].GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        var content = root.GetProperty("messages")[0].GetProperty("content");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("image");
    }

    [Test]
    public async Task SendAsync_WhenLocalEndpointHasImageAndToolProposal_DisablesTemplateThinking()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                {
                  "type": "tool_use",
                  "id": "call_1",
                  "name": "create_event_draft",
                  "input": { "title": "Poster Draft" }
                }
              ],
              "model": "claude-test",
              "stop_reason": "tool_use",
              "usage": { "input_tokens": 24, "output_tokens": 10 }
            }
            """));
        var settings = CreateSettings();
        settings.EndpointUrl = "http://127.0.0.1:1337/v1/messages";
        settings.AllowLocalProviderEndpoints = true;
        var provider = CreateProvider(handler, settings);

        var result = await provider.SendAsync(CreateRequest(
            toolProposalsEnabled: true,
            actionSchema: """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["title"],
                  "properties": { "title": { "type": "string" } }
                }
                """,
            messages:
            [
                new AiChatMessage(
                    AiMessageRole.User,
                    "Create an event draft from this poster:",
                    images:
                    [
                        new AiChatImage("image/png", "aW1hZ2UtYnl0ZXM=")
                    ])
            ]));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("chat_template_kwargs").GetProperty("enable_thinking").GetBoolean())
            .IsFalse();
        await Assert.That(root.GetProperty("tools")[0].GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(root.GetProperty("messages")[0].GetProperty("content")[1].GetProperty("type").GetString())
            .IsEqualTo("image");
    }

    [Test]
    public async Task SendAsync_WhenExternalEndpointIsUsed_DoesNotSendTemplateThinkingOverride()
    {
        var handler = new RecordingMessageHandler(_ => JsonResponse("""
            {
              "id": "msg_test",
              "type": "message",
              "role": "assistant",
              "content": [
                { "type": "text", "text": "Assistant response" }
              ],
              "model": "claude-test",
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 12, "output_tokens": 4 }
            }
            """));
        var provider = CreateProvider(handler, CreateSettings());

        var result = await provider.SendAsync(CreateRequest(messages:
        [
            new AiChatMessage(AiMessageRole.User, "Hello")
        ]));

        await Assert.That(result.Succeeded).IsTrue();

        using var document = JsonDocument.Parse(handler.RequestBody!);
        await Assert.That(document.RootElement.TryGetProperty("chat_template_kwargs", out _)).IsFalse();
    }

    private static AnthropicCompatibleChatProvider CreateProvider(
        HttpMessageHandler handler,
        AiProviderSettings settings)
    {
        var client = new HttpClient(handler);
        var factory = new StaticHttpClientFactory(client);
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new AnthropicCompatibleChatProvider(
            factory,
            Options.Create(settings),
            new AiProviderSettingsValidator(),
            new BusinessMetrics(meterFactory));
    }

    private static AiProviderSettings CreateSettings(string apiKey = "test-key") => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderAnthropicCompatible,
        EndpointUrl = "https://ai.example.test/v1/messages",
        ApiKey = apiKey,
        ModelId = "claude-test",
        MaxInputTokens = 8000,
        MaxOutputTokens = 1024,
        TimeoutSeconds = 30
    };

    private static AiChatPayload CreateRequest(
        bool toolProposalsEnabled = false,
        string? actionSchema = null,
        IReadOnlyList<AiChatMessage>? messages = null) => new(
            "claude-test",
            messages ?? [new AiChatMessage(AiMessageRole.User, "Plan a community dinner")],
            "You are a safe assistant.",
            new AiChatOptions(
                8000,
                1024,
                0.2m,
                30,
                toolProposalsEnabled,
                StreamingEnabled: false,
                StructuredOutputEnabled: false),
            actionSchema is null
                ? null
                : new AiStructuredActionSchema([AiProposedActionKind.CreateEventDraft], actionSchema));

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
        public Uri? RequestUri { get; private set; }
        public System.Net.Http.Headers.HttpRequestHeaders? RequestHeaders { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestHeaders = request.Headers;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
