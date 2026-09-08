using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Telemetry;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class MicrosoftExtensionsAiChatProviderTests
{
    [Test]
    public async Task SendAsync_MapsPayloadToChatClientAndResponseToProviderContract()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Assistant response"))
        {
            ResponseId = "resp_test",
            FinishReason = ChatFinishReason.Stop,
            Usage = new UsageDetails { InputTokenCount = 12, OutputTokenCount = 4, TotalTokenCount = 16 }
        });
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest("Plan a community dinner"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Assistant response");
        await Assert.That(result.Response.ProviderRequestId).IsEqualTo("resp_test");
        await Assert.That(result.Response.FinishReason).IsEqualTo("stop");
        await Assert.That(result.Response.Usage.InputTokens).IsEqualTo(12);
        await Assert.That(result.Response.Usage.OutputTokens).IsEqualTo(4);
        await Assert.That(result.Response.Usage.TotalTokens).IsEqualTo(16);
        await Assert.That(chatClient.Messages![0].Role).IsEqualTo(ChatRole.System);
        await Assert.That(chatClient.Messages[1].Role).IsEqualTo(ChatRole.User);
        await Assert.That(chatClient.Options!.ModelId).IsEqualTo("gpt-test");
        await Assert.That(chatClient.Options.MaxOutputTokens).IsEqualTo(1024);
        await Assert.That(chatClient.Options.Temperature).IsEqualTo(0.2f);
    }

    [Test]
    public async Task SendAsync_WhenToolCallReturned_ReturnsCreateEventDraftCandidate()
    {
        var message = new ChatMessage(ChatRole.Assistant, string.Empty)
        {
            Contents =
            [
                new FunctionCallContent(
                    "call_1",
                    "create_event_draft",
                    new Dictionary<string, object?> { ["title"] = "Community Dinner" })
            ]
        };
        var chatClient = new RecordingChatClient(new ChatResponse(message) { FinishReason = ChatFinishReason.ToolCalls });
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Create an event draft",
            toolProposalsEnabled: true,
            actionSchema: "{\"type\":\"object\",\"properties\":{\"title\":{\"type\":\"string\"}}}"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.ProposedActions.Count).IsEqualTo(1);
        await Assert.That(result.Response.ProposedActions[0].Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(result.Response.ProposedActions[0].PayloadJson).Contains("Community Dinner");
        await Assert.That(chatClient.Options!.Tools!.Count).IsEqualTo(1);
        await Assert.That(chatClient.Options.Tools[0].Name).IsEqualTo("create_event_draft");
        await Assert.That(chatClient.Options.ToolMode).IsEqualTo(ChatToolMode.Auto);
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputEnabled_SetsResponseFormatAndParsesAssistantMessage()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            "{\"message\":\"Structured assistant response\"}"))
        {
            FinishReason = ChatFinishReason.Stop
        });
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Summarize the plan",
            structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Structured assistant response");

        var responseFormat = (ChatResponseFormatJson)chatClient.Options!.ResponseFormat!;
        await Assert.That(responseFormat.SchemaName).IsEqualTo("assistant_message");
        await Assert.That(responseFormat.SchemaDescription).Contains("non-action assistant reply");
        await Assert.That(responseFormat.Schema!.Value.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(chatClient.Options.Tools).IsNull();
        await Assert.That(chatClient.Options.ToolMode).IsEqualTo(ChatToolMode.None);
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputDoesNotMatchSchema_ReturnsSafeFailure()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{\"summary\":\"wrong shape\"}")));
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Summarize the plan",
            structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_structured_output");
        await Assert.That(result.Error.Message).DoesNotContain("wrong shape");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputCombinesWithToolProposals_FailsBeforeProviderCall()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Create an event draft",
            toolProposalsEnabled: true,
            actionSchema: "{\"type\":\"object\"}",
            structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("structured_output_conflict");
        await Assert.That(chatClient.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenToolProposalsEnabled_UsesRegistryBackedJsonSchema()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Assistant response")));
        var provider = CreateProvider(chatClient);

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
        var functionTool = (AIFunctionDeclaration)chatClient.Options!.Tools![0];
        using var schemaDocument = JsonDocument.Parse(functionTool.JsonSchema.GetRawText());
        var schema = schemaDocument.RootElement;
        var requiredFields = schema.GetProperty("required")
            .EnumerateArray()
            .Select(field => field.GetString())
            .ToArray();

        await Assert.That(functionTool.Name).IsEqualTo("create_event_draft");
        await Assert.That(schema.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(requiredFields).Contains("title");
    }

    [Test]
    public async Task SendAsync_WhenContentFiltered_ReturnsStableSafeFailureCode()
    {
        var chatClient = new RecordingChatClient(new ChatResponse
        {
            FinishReason = ChatFinishReason.ContentFilter
        });
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest("Sensitive prompt"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("content_filtered");
        await Assert.That(result.Error.Message).DoesNotContain("Sensitive prompt");
    }

    [Test]
    public async Task SendAsync_WhenToolArgumentsMalformed_ReturnsSafeFailure()
    {
        var message = new ChatMessage(ChatRole.Assistant, string.Empty)
        {
            Contents = [new FunctionCallContent("call_1", "create_event_draft")]
        };
        var chatClient = new RecordingChatClient(new ChatResponse(message) { FinishReason = ChatFinishReason.ToolCalls });
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Create an event draft",
            toolProposalsEnabled: true,
            actionSchema: "{\"type\":\"object\"}"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_tool_arguments");
    }

    [Test]
    public async Task SendAsync_WhenToolResultMessageProvided_MapsToFunctionResultContent()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Tool result accepted")));
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Continue after the tool call",
            messages:
            [
                new AiChatMessage(AiMessageRole.User, "Create an event draft"),
                new AiChatMessage(AiMessageRole.Tool, "{\"draftId\":\"draft_1\"}", "call_1")
            ]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(chatClient.Messages![2].Role).IsEqualTo(ChatRole.Tool);
        await Assert.That(chatClient.Messages[2].AuthorName).IsNull();

        var functionResult = (FunctionResultContent)chatClient.Messages[2].Contents[0];
        await Assert.That(functionResult.Result).IsEqualTo("{\"draftId\":\"draft_1\"}");
    }

    [Test]
    public async Task SendAsync_WhenUserMessageHasImage_MapsToTextAndDataContent()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Image accepted")));
        var provider = CreateProvider(chatClient);
        const string imageData = "aW1hZ2UtYnl0ZXM=";

        var result = await provider.SendAsync(CreateRequest(
            "Describe this picture:",
            messages:
            [
                new AiChatMessage(
                    AiMessageRole.User,
                    "Describe this picture:",
                    images:
                    [
                        new AiChatImage("image/png", imageData)
                    ])
            ]));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(chatClient.Messages![1].Role).IsEqualTo(ChatRole.User);
        await Assert.That(chatClient.Messages[1].Contents.Count).IsEqualTo(2);

        var text = (TextContent)chatClient.Messages[1].Contents[0];
        var image = (DataContent)chatClient.Messages[1].Contents[1];
        await Assert.That(text.Text).IsEqualTo("Describe this picture:");
        await Assert.That(image.MediaType).IsEqualTo("image/png");
        await Assert.That(image.Uri).StartsWith("data:image/png;base64,");
    }

    [Test]
    public async Task SendAsync_WhenImageBackedBuildModeReturnsBlankTextWithoutToolCall_ReturnsRetryableEmptyResponseFailure()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
        var provider = CreateProvider(chatClient);
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
            messages:
            [
                new AiChatMessage(
                    AiMessageRole.User,
                    "Create event draft for this event poster",
                    images:
                    [
                        new AiChatImage("image/png", imageData)
                    ])
            ]));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_response");
        await Assert.That(result.Error.Message).Contains("empty text response");
        await Assert.That(chatClient.Options!.Tools![0].Name).IsEqualTo("create_event_draft");

        var image = (DataContent)chatClient.Messages![1].Contents[1];
        await Assert.That(image.MediaType).IsEqualTo("image/png");
    }

    [Test]
    public async Task SendAsync_WhenToolResultMessageMissingCallId_ReturnsSafeFailureBeforeProviderCall()
    {
        var chatClient = new RecordingChatClient(new ChatResponse(new ChatMessage(ChatRole.Assistant, "unused")));
        var provider = CreateProvider(chatClient);

        var result = await provider.SendAsync(CreateRequest(
            "Continue after the tool call",
            messages:
            [
                new AiChatMessage(AiMessageRole.User, "Create an event draft"),
                new AiChatMessage(AiMessageRole.Tool, "{\"draftId\":\"draft_1\"}")
            ]));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_tool_result");
        await Assert.That(chatClient.Calls).IsEqualTo(0);
    }

    private static MicrosoftExtensionsAiChatProvider CreateProvider(RecordingChatClient chatClient)
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));

        return new MicrosoftExtensionsAiChatProvider(
            chatClient,
            Options.Create(CreateSettings()),
            new BusinessMetrics(meterFactory));
    }

    private static AiProviderSettings CreateSettings() => new()
    {
        Enabled = true,
        Provider = AiProviderSettings.ProviderOpenAiCompatible,
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
        IReadOnlyList<AiChatMessage>? messages = null) => new(
            "gpt-test",
            messages ?? [new AiChatMessage(AiMessageRole.User, userMessage)],
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
            structuredOutputSchema);

    private sealed class RecordingChatClient : IChatClient
    {
        private readonly ChatResponse _response;

        public RecordingChatClient(ChatResponse response)
        {
            _response = response;
        }

        public IReadOnlyList<ChatMessage>? Messages { get; private set; }

        public ChatOptions? Options { get; private set; }

        public int Calls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Messages = messages.ToList();
            Options = options;
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
