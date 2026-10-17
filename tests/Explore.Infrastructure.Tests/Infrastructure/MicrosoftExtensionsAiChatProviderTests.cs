using System.Net;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Domain.Ai;
using Explore.Infrastructure.Ai;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class MicrosoftExtensionsAiChatProviderTests : AiProviderTestBase
{
    [Test]
    public async Task SendAsync_MapsPayloadToChatClientAndResponseToProviderContract()
    {
        var transport = new RecordingSdkTransport(Completion("Assistant response"));
        var provider = CreateProvider(transport);

        var result = await provider.SendAsync(CreateRequest("Plan a community dinner"));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Assistant response");
        await Assert.That(result.Response.ProviderRequestId).IsEqualTo("resp_test");
        await Assert.That(result.Response.FinishReason).IsEqualTo("stop");
        await Assert.That(result.Response.Usage.InputTokens).IsEqualTo(12);
        await Assert.That(result.Response.Usage.OutputTokens).IsEqualTo(4);
        await Assert.That(result.Response.Usage.TotalTokens).IsEqualTo(16);
        await Assert.That(transport.ApiKey).IsEqualTo(Fixture.Credential);
        await Assert.That(transport.RequestUri!.AbsolutePath).IsEqualTo("/openai/deployments/gpt-test/chat/completions");
        using var document = JsonDocument.Parse(transport.Body!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString()).IsEqualTo("system");
        await Assert.That(root.GetProperty("messages")[1].GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(root.GetProperty("max_completion_tokens").GetInt32()).IsEqualTo(1024);
        await Assert.That(root.GetProperty("temperature").GetDecimal()).IsEqualTo(0.2m);
        await Assert.That(transport.Body).DoesNotContain(Fixture.Credential);
    }

    [Test]
    public async Task SendAsync_WhenToolCallReturned_ReturnsCreateEventDraftCandidate()
    {
        var transport = new RecordingSdkTransport(Completion("", "tool_calls", """{"title":"Community Dinner"}"""));
        var provider = CreateProvider(transport);

        var result = await provider.SendAsync(CreateRequest("Create an event draft",
            toolProposalsEnabled: true, actionSchema: """{"type":"object","properties":{"title":{"type":"string"}}}"""));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.ProposedActions.Count).IsEqualTo(1);
        await Assert.That(result.Response.ProposedActions[0].Kind).IsEqualTo(AiProposedActionKind.CreateEventDraft);
        await Assert.That(result.Response.ProposedActions[0].PayloadJson).Contains("Community Dinner");
        using var document = JsonDocument.Parse(transport.Body!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("tools").GetArrayLength()).IsEqualTo(1);
        await Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(root.GetProperty("tool_choice").GetString()).IsEqualTo("auto");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputEnabled_SetsResponseFormatAndParsesAssistantMessage()
    {
        var transport = new RecordingSdkTransport(Completion("""{"message":"Structured assistant response"}"""));
        var provider = CreateProvider(transport);

        var result = await provider.SendAsync(CreateRequest("Summarize the plan", structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Response!.AssistantMessage).IsEqualTo("Structured assistant response");
        using var document = JsonDocument.Parse(transport.Body!);
        var root = document.RootElement;
        var format = root.GetProperty("response_format");
        await Assert.That(format.GetProperty("type").GetString()).IsEqualTo("json_schema");
        var schema = format.GetProperty("json_schema");
        await Assert.That(schema.GetProperty("name").GetString()).IsEqualTo("assistant_message");
        await Assert.That(schema.GetProperty("description").GetString()).Contains("non-action assistant reply");
        await Assert.That(schema.GetProperty("schema").GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(root.TryGetProperty("tools", out _)).IsFalse();
        if (root.TryGetProperty("tool_choice", out var choice))
            await Assert.That(choice.GetString()).IsEqualTo("none");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputDoesNotMatchSchema_ReturnsSafeFailure()
    {
        var transport = new RecordingSdkTransport(Completion("""{"summary":"wrong shape"}"""));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Summarize the plan", structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_structured_output");
        await Assert.That(result.Error.Message).DoesNotContain("wrong shape");
    }

    [Test]
    public async Task SendAsync_WhenStructuredOutputCombinesWithToolProposals_FailsBeforeProviderCall()
    {
        var transport = new RecordingSdkTransport(Completion("unused"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Create an event draft", toolProposalsEnabled: true,
            actionSchema: """{"type":"object"}""", structuredOutputEnabled: true,
            structuredOutputSchema: AiStructuredOutputSchemas.AssistantMessage));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("structured_output_conflict");
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    [Test]
    public async Task SendAsync_WhenToolProposalsEnabled_UsesRegistryBackedJsonSchema()
    {
        var transport = new RecordingSdkTransport(Completion("Assistant response"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Create an event draft", toolProposalsEnabled: true,
            actionSchema: """
                {"type":"object","additionalProperties":false,"required":["title"],
                 "properties":{"title":{"type":"string"}}}
                """));

        await Assert.That(result.Succeeded).IsTrue();
        using var document = JsonDocument.Parse(transport.Body!);
        var function = document.RootElement.GetProperty("tools")[0].GetProperty("function");
        var schema = function.GetProperty("parameters");
        await Assert.That(function.GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(schema.GetProperty("additionalProperties").GetBoolean()).IsFalse();
        await Assert.That(schema.GetProperty("required").EnumerateArray().Select(field => field.GetString()).ToArray()).Contains("title");
    }

    [Test]
    public async Task SendAsync_WhenContentFiltered_ReturnsStableSafeFailureCode()
    {
        var transport = new RecordingSdkTransport(Completion("", "content_filter"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Sensitive prompt"));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("content_filtered");
        await Assert.That(result.Error.Message).DoesNotContain("Sensitive prompt");
    }

    [Test]
    public async Task SendAsync_WhenToolArgumentsMalformed_ReturnsSafeFailure()
    {
        var transport = new RecordingSdkTransport(Completion("", "tool_calls", "{}"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Create an event draft", toolProposalsEnabled: true,
            actionSchema: """{"type":"object"}"""));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_tool_arguments");
    }

    [Test]
    public async Task SendAsync_WhenToolResultMessageProvided_MapsToFunctionResultContent()
    {
        var transport = new RecordingSdkTransport(Completion("Tool result accepted"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Continue after the tool call", messages:
        [
            new AiChatMessage(AiMessageRole.User, "Create an event draft"),
            new AiChatMessage(AiMessageRole.Tool, """{"draftId":"draft_1"}""", "call_1")
        ]));

        await Assert.That(result.Succeeded).IsTrue();
        using var document = JsonDocument.Parse(transport.Body!);
        var tool = document.RootElement.GetProperty("messages")[2];
        await Assert.That(tool.GetProperty("role").GetString()).IsEqualTo("tool");
        await Assert.That(tool.TryGetProperty("name", out _)).IsFalse();
        await Assert.That(tool.GetProperty("tool_call_id").GetString()).IsEqualTo("call_1");
        await Assert.That(tool.GetProperty("content").GetString()).IsEqualTo("""{"draftId":"draft_1"}""");
    }

    [Test]
    public async Task SendAsync_WhenUserMessageHasImage_MapsToTextAndDataContent()
    {
        var transport = new RecordingSdkTransport(Completion("Image accepted"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Describe this picture:", messages:
        [
            new AiChatMessage(AiMessageRole.User, "Describe this picture:",
                images: [new AiChatImage("image/png", "aW1hZ2UtYnl0ZXM=")])
        ]));

        await Assert.That(result.Succeeded).IsTrue();
        using var document = JsonDocument.Parse(transport.Body!);
        var message = document.RootElement.GetProperty("messages")[1];
        await Assert.That(message.GetProperty("role").GetString()).IsEqualTo("user");
        var content = message.GetProperty("content");
        await Assert.That(content.GetArrayLength()).IsEqualTo(2);
        await Assert.That(content[0].GetProperty("type").GetString()).IsEqualTo("text");
        await Assert.That(content[0].GetProperty("text").GetString()).IsEqualTo("Describe this picture:");
        await Assert.That(content[1].GetProperty("type").GetString()).IsEqualTo("image_url");
        await Assert.That(content[1].GetProperty("image_url").GetProperty("url").GetString()).IsEqualTo("data:image/png;base64,aW1hZ2UtYnl0ZXM=");
    }

    [Test]
    public async Task SendAsync_WhenImageBackedBuildModeReturnsBlankTextWithoutToolCall_ReturnsRetryableEmptyResponseFailure()
    {
        var transport = new RecordingSdkTransport(Completion(""));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Create event draft for this event poster",
            toolProposalsEnabled: true, actionSchema: """
                {"type":"object","additionalProperties":false,"required":["title"],
                 "properties":{"title":{"type":"string"}}}
                """, messages:
        [
            new AiChatMessage(AiMessageRole.User, "Create event draft for this event poster",
                images: [new AiChatImage("image/png", "aW1hZ2UtYnl0ZXM=")])
        ]));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_response");
        await Assert.That(result.Error.Message).Contains("empty text response");
        using var document = JsonDocument.Parse(transport.Body!);
        var root = document.RootElement;
        await Assert.That(root.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString()).IsEqualTo("create_event_draft");
        await Assert.That(root.GetProperty("messages")[1].GetProperty("content")[1].GetProperty("image_url").GetProperty("url").GetString())
            .StartsWith("data:image/png;base64,");
    }

    [Test]
    public async Task SendAsync_WhenToolResultMessageMissingCallId_ReturnsSafeFailureBeforeProviderCall()
    {
        var transport = new RecordingSdkTransport(Completion("unused"));
        var provider = CreateProvider(transport);
        var result = await provider.SendAsync(CreateRequest("Continue after the tool call", messages:
        [
            new AiChatMessage(AiMessageRole.User, "Create an event draft"),
            new AiChatMessage(AiMessageRole.Tool, """{"draftId":"draft_1"}""")
        ]));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Error!.Code).IsEqualTo("invalid_tool_result");
        await Assert.That(transport.Calls).IsEqualTo(0);
    }

    private MicrosoftExtensionsAiChatProvider CreateProvider(RecordingSdkTransport transport)
    {
        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderAzureOpenAi,
            EndpointUrl = "https://ai.example.openai.azure.com",
            CredentialScope = AiCredentialScope.Instance,
            AzureCredentialMode = AiProviderSettings.AzureCredentialModeApiKey,
            ModelId = "gpt-test"
        };
        return new(Fixture.Configuration(settings), Fixture.Metrics,
            new ClientFactory(Fixture.CreateClient(transport)));
    }

    private static string Completion(string content, string finishReason = "stop", string? toolArguments = null) =>
        JsonSerializer.Serialize(new
        {
            id = "resp_test",
            @object = "chat.completion",
            created = 1,
            model = "gpt-test",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant", content,
                        tool_calls = toolArguments is null ? null : new[]
                        {
                            new { id = "call_1", type = "function", function = new { name = "create_event_draft", arguments = toolArguments } }
                        }
                    },
                    finish_reason = finishReason
                }
            },
            usage = new { prompt_tokens = 12, completion_tokens = 4, total_tokens = 16 }
        });

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
            new AiChatOptions(8000, 1024, 0.2m, 30, toolProposalsEnabled,
                StreamingEnabled: false, StructuredOutputEnabled: structuredOutputEnabled),
            actionSchema is null ? null : new AiStructuredActionSchema([AiProposedActionKind.CreateEventDraft], actionSchema),
            structuredOutputSchema);

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingSdkTransport(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? ApiKey { get; private set; }
        public Uri? RequestUri { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            RequestUri = request.RequestUri;
            ApiKey = request.Headers.GetValues("api-key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
