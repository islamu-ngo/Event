using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Services.Ai;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AiAssistantClientServiceTests
{
    private readonly IAiAssistantClient _apiClient = Substitute.For<IAiAssistantClient>();
    private readonly ILogger<AiAssistantClientService> _logger = Substitute.For<ILogger<AiAssistantClientService>>();

    [Test]
    [Arguments("array")]
    [Arguments("null")]
    [Arguments("scalar")]
    [Arguments("oversized")]
    [Arguments("deep")]
    [Arguments("duplicate")]
    [Arguments("long-code")]
    [Arguments("typed-long-code")]
    [Arguments("typed-json-long-code")]
    [Arguments("valid")]
    public async Task RawProblemCodeRequiresBoundedUnambiguousObject(string shape)
    {
        string response = shape switch
        {
            "array" => "[]",
            "null" => "null",
            "scalar" => "1",
            "oversized" => "{\"code\":\"conversation_not_active\",\"padding\":\""
                + new string('x', 65_536) + "\"}",
            "deep" => "{\"code\":\"conversation_not_active\",\"nested\":"
                + new string('[', 17) + "0" + new string(']', 17) + "}",
            "duplicate" => "{\"code\":\"conversation_not_active\",\"code\":\"forbidden\"}",
            "long-code" => "{\"code\":\"" + new string('x', 129) + "\"}",
            "typed-long-code" or "typed-json-long-code" => "{}",
            _ => "{\"code\":\"conversation_not_active\"}"
        };
        var problem = new ProblemDetails();
        if (shape == "typed-long-code")
            problem.AdditionalProperties["code"] = new string('x', 129);
        if (shape == "typed-json-long-code")
            problem.AdditionalProperties["code"] =
                System.Text.Json.JsonSerializer.SerializeToElement(new string('x', 129));
        var canary = Guid.NewGuid().ToString("N");
        _apiClient.SendAiMessageAsync(Arg.Any<Guid>(), Arg.Any<SendAiMessageRequestDto>(), null,
                cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(canary, 409, response,
                new Dictionary<string, IEnumerable<string>>(), problem,
                new InvalidOperationException(canary)));
        var logs = new ConfirmationCollector();
        var service = new AiAssistantClientService(_apiClient, logs);

        var result = await service.SendMessageAsync(Guid.CreateVersion7(), canary);

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode)
            .IsEqualTo(shape == "valid" ? "conversation_not_active" : "conflict");
        await Assert.That(logs.Entries.Single().Exception).IsNull();
        await Assert.That(logs.Entries.Single().Message).DoesNotContain(canary);
    }

    [Test]
    public async Task OversizedFallbackSuccessBodyIsNotDeserialized()
    {
        _apiClient.SendAiMessageAsync(Arg.Any<Guid>(), Arg.Any<SendAiMessageRequestDto>(), null,
                cancellationToken: Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(Guid.NewGuid().ToString("N"), 200,
                "{\"success\":true,\"message\":\"" + new string('x', 65_536) + "\"}",
                new Dictionary<string, IEnumerable<string>>(), null));

        var result = await CreateService().SendMessageAsync(Guid.CreateVersion7(), "Valid input");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("api_error");
    }

    [Test]
    [Arguments("bootstrap")]
    [Arguments("list")]
    [Arguments("conversation")]
    [Arguments("run")]
    [Arguments("run-denied")]
    [Arguments("references")]
    [Arguments("create")]
    [Arguments("send")]
    [Arguments("cancel")]
    [Arguments("confirm")]
    [Arguments("reject")]
    public async Task ClientReadsAndMutationsExportNoPrivateExceptionOrResourceIdentity(string operation)
    {
        Guid conversationId = Guid.CreateVersion7();
        Guid runId = Guid.CreateVersion7();
        string canary = Guid.CreateVersion7().ToString("N");
        var failure = new ApiException(canary, operation == "run-denied" ? 401 : 503, canary,
            new Dictionary<string, IEnumerable<string>>(), new InvalidOperationException(canary));
        _apiClient.GetAiAssistantBootstrapAsync(cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.GetAiConversationsAsync(20, cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.GetAiConversationAsync(conversationId, cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.GetAiRunStatusAsync(conversationId, runId, cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.SearchAiReferencesAsync(canary, 10, null, null, Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.CreateAiConversationAsync(Arg.Any<CreateAiConversationRequestDto>(),
            cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.SendAiMessageAsync(conversationId, Arg.Any<SendAiMessageRequestDto>(), null,
            cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.CancelAiRunAsync(conversationId, runId, cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.ConfirmAiProposedActionAsync(conversationId, runId, null,
            cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        _apiClient.RejectAiProposedActionAsync(conversationId, runId,
            cancellationToken: Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        var logs = new ConfirmationCollector();
        var service = new AiAssistantClientService(_apiClient, logs);
        switch (operation)
        {
            case "bootstrap":
                await Assert.That(await service.GetBootstrapAsync()).IsNull();
                break;
            case "conversation":
                await Assert.That(await service.GetConversationAsync(conversationId)).IsNull();
                break;
            case "list":
                await Assert.That(await service.GetConversationCollectionAsync()).IsNull();
                break;
            case "run":
            case "run-denied":
                await service.GetRunStatusAsync(conversationId, runId);
                break;
            case "references":
                await Assert.That((await service.SearchReferencesAsync(canary)).Count).IsEqualTo(0);
                break;
            case "create":
                await Assert.That((await service.CreateConversationAsync(new CreateAiConversationRequestDto())).Success).IsFalse();
                break;
            case "send":
                await Assert.That((await service.SendMessageAsync(conversationId, canary)).Success).IsFalse();
                break;
            case "cancel":
                await Assert.That((await service.CancelRunAsync(conversationId, runId)).Success).IsFalse();
                break;
            case "confirm":
                await Assert.That((await service.ConfirmProposedActionAsync(conversationId, runId)).Success).IsFalse();
                break;
            case "reject":
                await Assert.That((await service.RejectProposedActionAsync(conversationId, runId)).Success).IsFalse();
                break;
        }
        var exported = logs.Entries.Single();
        await Assert.That(exported.Exception).IsNull();
        await Assert.That(exported.Message).DoesNotContain(canary);
        await Assert.That(exported.Message).DoesNotContain(conversationId.ToString());
        await Assert.That(exported.Message).DoesNotContain(runId.ToString());
    }

    private sealed class ConfirmationCollector : ILogger<AiAssistantClientService>
    {
        public List<(string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((formatter(state, exception), exception));
    }

    [Test]
    public async Task GetConversationCollectionAsync_WhenApiSucceeds_PreservesCollectionLinks()
    {
        var response = new HalCollectionResourceOfAiConversationSummaryDto
        {
            _links = new Dictionary<string, HalLink>
            {
                ["create"] = new() { Href = "/api/ai/assistant/conversations", Method = "POST" }
            },
            _embedded = new HalCollectionEmbeddedOfAiConversationSummaryDto
            {
                Items = [new HalResourceOfAiConversationSummaryDto { Id = Guid.CreateVersion7(), Title = "Planning" }]
            }
        };

        _apiClient.GetAiConversationsAsync(20, null, null, Arg.Any<CancellationToken>())
            .Returns(response);

        var service = CreateService();

        var collection = await service.GetConversationCollectionAsync(20);

        await Assert.That(collection?._embedded?.Items?.Count).IsEqualTo(1);
        await Assert.That(collection?._links).ContainsKey("create");
    }

    [Test]
    public async Task SearchReferencesAsync_WhenApiSucceeds_ReturnsHalReferenceItemsWithLinks()
    {
        var referenceId = Guid.CreateVersion7();
        var response = new HalCollectionResourceOfAiReferenceSearchResultDto
        {
            _embedded = new HalCollectionEmbeddedOfAiReferenceSearchResultDto
            {
                Items =
                [
                    HalLinkTestFactory.WithLinks(new HalResourceOfAiReferenceSearchResultDto
                    {
                        Kind = "Event",
                        ReferenceId = referenceId,
                        DisplayName = "Community Iftar"
        }, new HalLinkTestLink("event", $"/api/events/{referenceId}", "GET"))
                ]
            }
        };

        _apiClient.SearchAiReferencesAsync("iftar", 20, null, null, Arg.Any<CancellationToken>())
            .Returns(response);

        var service = CreateService();

        var references = await service.SearchReferencesAsync("iftar", 20);

        await Assert.That(references.Count).IsEqualTo(1);
        await Assert.That(references[0].ReferenceId).IsEqualTo(referenceId);
        await Assert.That(references[0]._links).ContainsKey("event");
    }

    [Test]
    public async Task SearchReferencesAsync_WhenApiFails_ReturnsEmptyList()
    {
        _apiClient.SearchAiReferencesAsync(Arg.Any<string?>(), Arg.Any<int?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException(429));

        var service = CreateService();

        var references = await service.SearchReferencesAsync("iftar", 10);

        await Assert.That(references).IsEmpty();
    }

    [Test]
    public async Task SendMessageAsync_PropagatesIdempotencyKeyInBodyAndHeader()
    {
        var conversationId = Guid.CreateVersion7();
        var images = new List<AiMessageImageInputDto>
        {
            new()
            {
                MediaType = "image/png",
                Data = "aW1hZ2U=",
                FileName = "diagram.png",
                SizeBytes = 5
            }
        };
        var references = new List<AiSelectedReferenceDto>
        {
            new()
            {
                Kind = "Event",
                ReferenceId = Guid.CreateVersion7(),
                DisplayName = "Community Iftar",
                Summary = "Public evening program"
            }
        };
        _apiClient.SendAiMessageAsync(
                conversationId,
                Arg.Any<SendAiMessageRequestDto>(),
                "send-key",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.CreateVersion7() });

        var service = CreateService();

        var result = await service.SendMessageAsync(
            conversationId,
            "Plan an iftar",
            idempotencyKey: "send-key",
            images: images,
            references: references);

        await Assert.That(result.Success).IsTrue();
        await _apiClient.Received(1).SendAiMessageAsync(
            conversationId,
            Arg.Is<SendAiMessageRequestDto>(request =>
                request.Content == "Plan an iftar"
                && request.ModelId == null
                && request.IdempotencyKey == "send-key"
                && request.Images != null
                && request.Images.Count == 1
                && request.Images.Single().MediaType == "image/png"
                && request.Images.Single().Data == "aW1hZ2U="
                && request.References != null
                && request.References.Count == 1
                && request.References.Single().Kind == "Event"
                && request.References.Single().DisplayName == "Community Iftar"),
            "send-key",
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessageAsync_WhenApiReturnsProblemDetails_UsesProblemDetailsMessage()
    {
        var conversationId = Guid.CreateVersion7();
        _apiClient.SendAiMessageAsync(
                conversationId,
                Arg.Any<SendAiMessageRequestDto>(),
                Arg.Any<string?>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(
                "Conflict",
                409,
                "{}",
                new Dictionary<string, IEnumerable<string>>(),
                new ProblemDetails
                {
                    Title = "AI conversation conflict",
                    Detail = "AI conversation is not ready for a new message.",
                    AdditionalProperties = { ["code"] = "conversation_not_active" }
                },
                null));

        var service = CreateService();

        var result = await service.SendMessageAsync(conversationId, "hello", idempotencyKey: "send-key");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("conversation_not_active");
        await Assert.That(result.Message).IsEqualTo("AI conversation is not ready for a new message.");
    }

    [Test]
    public async Task SendMessageAsync_WhenApiReturnsUnauthorized_ReturnsUnauthorizedFailureCode()
    {
        var conversationId = Guid.CreateVersion7();
        _apiClient.SendAiMessageAsync(
                conversationId,
                Arg.Any<SendAiMessageRequestDto>(),
                Arg.Any<string?>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException(401));

        var service = CreateService();

        var result = await service.SendMessageAsync(conversationId, "hello", idempotencyKey: "send-key");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("unauthorized");
    }

    [Test]
    public async Task SendMessageAsync_WhenApiReturnsLegacyOkCommandBody_TreatsAsSuccess()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        _apiClient.SendAiMessageAsync(
                conversationId,
                Arg.Any<SendAiMessageRequestDto>(),
                Arg.Any<string?>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException(
                "The HTTP status code of the response was not expected (200).",
                200,
                $$"""{"id":"{{runId}}","success":true,"message":"AI message sent.","errors":[]}""",
                new Dictionary<string, IEnumerable<string>>(),
                null));

        var service = CreateService();

        var result = await service.SendMessageAsync(conversationId, "hello", idempotencyKey: "send-key");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(runId);
        await Assert.That(result.Message).IsEqualTo("AI message sent.");
    }

    [Test]
    public async Task ConfirmProposedActionAsync_PropagatesIdempotencyKey()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        _apiClient.ConfirmAiProposedActionAsync(
                conversationId,
                proposedActionId,
                "confirm-key",
                null,
                null,
                Arg.Any<CancellationToken>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = eventId });

        var service = CreateService();

        var result = await service.ConfirmProposedActionAsync(conversationId, proposedActionId, "confirm-key");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Id).IsEqualTo(eventId);
        await _apiClient.Received(1).ConfirmAiProposedActionAsync(
            conversationId,
            proposedActionId,
            "confirm-key",
            null,
            null,
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConfirmProposedActionAsync_WhenApiReturnsProblemDetails_UsesProblemDetailsMessage()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        _apiClient.ConfirmAiProposedActionAsync(
                conversationId,
                proposedActionId,
                Arg.Any<string?>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(
                "Bad Request",
                400,
                """{"type":"https://httpstatuses.com/400","title":"AI assistant request failed","status":400,"detail":"AI event draft organization is not allowed for this mapping context.","code":"invalid_organization_scope"}""",
                new Dictionary<string, IEnumerable<string>>(),
                new ProblemDetails
                {
                    Title = "AI assistant request failed",
                    Detail = "AI event draft organization is not allowed for this mapping context.",
                    AdditionalProperties = { ["code"] = "invalid_organization_scope" }
                },
                null));

        var service = CreateService();

        var result = await service.ConfirmProposedActionAsync(conversationId, proposedActionId, "confirm-key");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("invalid_organization_scope");
        await Assert.That(result.Message).IsEqualTo("AI event draft organization is not allowed for this mapping context.");
    }

    [Test]
    public async Task ConfirmProposedActionAsync_WhenApiExceptionResponseIsBlank_UsesTypedProblemDetails()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        _apiClient.ConfirmAiProposedActionAsync(
                conversationId,
                proposedActionId,
                Arg.Any<string?>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(
                "Bad Request",
                400,
                string.Empty,
                new Dictionary<string, IEnumerable<string>>(),
                new ProblemDetails
                {
                    Title = "AI assistant request failed",
                    Detail = "Selected AI actor context is not allowed to create events for this user.",
                    AdditionalProperties = { ["code"] = "actor_context_not_allowed" }
                },
                null));

        var service = CreateService();

        var result = await service.ConfirmProposedActionAsync(conversationId, proposedActionId, "confirm-key");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("actor_context_not_allowed");
        await Assert.That(result.Message).IsEqualTo("Selected AI actor context is not allowed to create events for this user.");
    }

    [Test]
    public async Task RejectProposedActionAsync_WhenApiFails_ReturnsFailureResult()
    {
        _apiClient.RejectAiProposedActionAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(CreateApiException(409));

        var service = CreateService();

        var result = await service.RejectProposedActionAsync(Guid.CreateVersion7(), Guid.CreateVersion7());

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("api_error");
    }

    [Test]
    public async Task CreateConversationAsync_WhenForbidden_ReturnsProblemDetailsMessage()
    {
        _apiClient.CreateAiConversationAsync(
                Arg.Any<CreateAiConversationRequestDto>(),
                null,
                null,
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new ApiException<ProblemDetails>(
                "Forbidden",
                403,
                "{}",
                new Dictionary<string, IEnumerable<string>>(),
                new ProblemDetails { Title = "Forbidden", Detail = "Create conversations is not available." },
                null));

        var service = CreateService();

        var result = await service.CreateConversationAsync(new CreateAiConversationRequestDto { Title = "AI Assistant" });

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("forbidden");
        await Assert.That(result.Message).IsEqualTo("Create conversations is not available.");
    }

    private AiAssistantClientService CreateService() => new(_apiClient, _logger);

    private static ApiException CreateApiException(int statusCode) =>
        new("API error", statusCode, "{}", new Dictionary<string, IEnumerable<string>>(), null);
}
