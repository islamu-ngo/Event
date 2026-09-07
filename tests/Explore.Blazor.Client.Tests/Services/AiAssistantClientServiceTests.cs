using Explore.Blazor.Client.Contracts.Services.Ai;
using Explore.Blazor.Client.Services.Ai;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AiAssistantClientServiceTests
{
    private readonly IAiAssistantClient _apiClient = Substitute.For<IAiAssistantClient>();
    private readonly ILogger<AiAssistantClientService> _logger = Substitute.For<ILogger<AiAssistantClientService>>();

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
