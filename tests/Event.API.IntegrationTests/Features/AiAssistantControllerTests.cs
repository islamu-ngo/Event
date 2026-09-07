namespace Event.Api.IntegrationTests.Features;

using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.BackgroundServices;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Ai;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Serialization;
using Explore.Infrastructure.Ai;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NSubstitute;

public sealed class AiAssistantControllerTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IHateoasLinkGenerator _linkGenerator = Substitute.For<IHateoasLinkGenerator>();
    private readonly IResourceAssembler<AiConversationDto, AiConversationSummaryDto> _conversationAssembler =
        Substitute.For<IResourceAssembler<AiConversationDto, AiConversationSummaryDto>>();
    private readonly IAiAssistantRunQueue _runQueue = Substitute.For<IAiAssistantRunQueue>();
    private readonly ITenantContext _tenantContext = Substitute.For<ITenantContext>();

    [Test]
    public async Task AiRoutes_AreAuthenticatedAndUseStableRouteNames()
    {
        await Assert.That(typeof(AiAssistantController).GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(typeof(AiAssistantController).GetCustomAttribute<EndpointClassificationAttribute>()?.Class)
            .IsEqualTo(EndpointClass.Authenticated);
        await Assert.That(typeof(AiAssistantController).GetCustomAttribute<ApiVersionAttribute>()?.Versions.Single().ToString())
            .IsEqualTo("0.1");

        AssertRoute(nameof(AiAssistantController.GetBootstrap), typeof(HttpGetAttribute), "bootstrap", RouteNames.GetAiAssistantBootstrap);
        AssertRoute(nameof(AiAssistantController.GetConversations), typeof(HttpGetAttribute), "conversations", RouteNames.GetAiConversations);
        AssertRoute(nameof(AiAssistantController.CreateConversation), typeof(HttpPostAttribute), "conversations", RouteNames.CreateAiConversation);
        AssertRoute(nameof(AiAssistantController.GetConversation), typeof(HttpGetAttribute), "conversations/{conversationId:guid}", RouteNames.GetAiConversation);
        AssertRoute(nameof(AiAssistantController.SearchReferences), typeof(HttpGetAttribute), "references", RouteNames.SearchAiReferences);
        AssertRoute(nameof(AiAssistantController.SendMessage), typeof(HttpPostAttribute), "conversations/{conversationId:guid}/messages", RouteNames.SendAiMessage);
        AssertRoute(nameof(AiAssistantController.ConfirmProposedAction), typeof(HttpPostAttribute), "conversations/{conversationId:guid}/proposed-actions/{proposedActionId:guid}/confirm", RouteNames.ConfirmAiProposedAction);
        AssertRoute(nameof(AiAssistantController.RejectProposedAction), typeof(HttpPostAttribute), "conversations/{conversationId:guid}/proposed-actions/{proposedActionId:guid}/reject", RouteNames.RejectAiProposedAction);
        AssertRoute(nameof(AiAssistantController.GetRunStatus), typeof(HttpGetAttribute), "conversations/{conversationId:guid}/runs/{runId:guid}", RouteNames.GetAiRunStatus);
        AssertRoute(nameof(AiAssistantController.CancelRun), typeof(HttpPostAttribute), "conversations/{conversationId:guid}/runs/{runId:guid}/cancel", RouteNames.CancelAiRun);
    }

    [Test]
    public async Task GetConversations_DispatchesQueryAndUsesConversationAssembler()
    {
        var conversation = CreateSummary(Guid.CreateVersion7(), "Active");
        var conversations = new[] { conversation };
        var expected = HalCollectionResource<AiConversationSummaryDto>.Create(
            [new HalResource<AiConversationSummaryDto>(conversation)],
            pageNumber: 1,
            pageSize: 20,
            totalCount: 1,
            links: new Dictionary<string, HalLink>());
        _mediator.Send(Arg.Any<GetAiConversationListQuery>(), Arg.Any<CancellationToken>())
            .Returns(conversations);
        _conversationAssembler.ToCollectionResource(
                Arg.Any<IEnumerable<AiConversationSummaryDto>>(),
                RouteNames.GetAiConversations,
                Arg.Any<object>(),
                Arg.Any<HttpContext>())
            .Returns(expected);
        var controller = CreateController();

        var actionResult = await controller.GetConversations(limit: 25, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(expected);
        await _mediator.Received(1).Send(
            Arg.Is<GetAiConversationListQuery>(query => query.Limit == 25),
            Arg.Any<CancellationToken>());
        await _conversationAssembler.Received(1).ToCollectionResource(
            Arg.Is<IEnumerable<AiConversationSummaryDto>>(items => items.Single() == conversation),
            RouteNames.GetAiConversations,
            Arg.Any<object>(),
            Arg.Any<HttpContext>());
    }

    [Test]
    public async Task SearchReferences_DispatchesQueryAndReturnsHalCollectionWithKindLinks()
    {
        var eventId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var eventReference = new AiReferenceSearchResultDto(
            "Event",
            eventId,
            "Community Iftar",
            "Public meal",
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            "Published",
            "Public",
            "Local");
        var actorReference = new AiReferenceSearchResultDto(
            "Actor",
            actorId,
            "Amina Speaker",
            null,
            null,
            null,
            null,
            null,
            null);
        var organizationReference = new AiReferenceSearchResultDto(
            "Organization",
            organizationId,
            "Community Center",
            null,
            null,
            null,
            null,
            null,
            null);
        _mediator.Send(Arg.Any<SearchAiReferencesQuery>(), Arg.Any<CancellationToken>())
            .Returns([eventReference, actorReference, organizationReference]);
        _linkGenerator.GeneratePath(RouteNames.SearchAiReferences, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns("/api/ai/assistant/references?searchTerm=iftar&limit=20");
        _linkGenerator.GeneratePath(RouteNames.GetEventById, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/Event/{eventId}");
        _linkGenerator.GeneratePath(RouteNames.GetActorById, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/Actor/{actorId}");
        _linkGenerator.GeneratePath(RouteNames.GetOrganizationById, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/Organization/{organizationId}");
        var controller = CreateController();

        var actionResult = await controller.SearchReferences("iftar", 999, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var resource = ok!.Value as HalCollectionResource<AiReferenceSearchResultDto>;
        await Assert.That(resource).IsNotNull();
        await Assert.That(resource!.PageSize).IsEqualTo(20);
        await Assert.That(resource.TotalCount).IsEqualTo(3);
        await Assert.That(resource.Links[LinkRelations.Self].Href).Contains("references");
        var eventItem = resource.Embedded.Items.Single(item => item.Data.Kind == "Event");
        await Assert.That(eventItem.Data).IsEqualTo(eventReference);
        await Assert.That(eventItem.Links[LinkRelations.Event].Href).Contains(eventId.ToString());
        var actorItem = resource.Embedded.Items.Single(item => item.Data.Kind == "Actor");
        await Assert.That(actorItem.Links[LinkRelations.Actor].Href).Contains(actorId.ToString());
        var organizationItem = resource.Embedded.Items.Single(item => item.Data.Kind == "Organization");
        await Assert.That(organizationItem.Links[LinkRelations.Organization].Href).Contains(organizationId.ToString());
        await _mediator.Received(1).Send(
            Arg.Is<SearchAiReferencesQuery>(query => query.SearchTerm == "iftar" && query.Limit == 20),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SearchReferences_UsesAiAssistantRateLimitPolicy()
    {
        var attribute = typeof(AiAssistantController).GetMethod(nameof(AiAssistantController.SearchReferences))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.PolicyName).IsEqualTo(RateLimitingExtensions.AiAssistantPolicy);
    }

    [Test]
    public async Task GetModels_WhenLocalEndpointIsAllowed_AppendsModelsPathAndOmitsBearerWithoutApiKey()
    {
        var handler = new RecordingModelDiscoveryHandler(_ => JsonResponse("""
            {
              "data": [
                { "id": "Gemma-4-E2B-Uncensored-HauhauCS-Aggressive-Q8_K_P" }
              ]
            }
            """));
        var httpClientFactory = new StaticHttpClientFactory(new HttpClient(handler));
        _mediator.Send(Arg.Any<GetTenantOnboardingStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(new TenantOnboardingStatusDto
            {
                IsAuthenticated = true,
                IsCurrentUserTenantAdministrator = true
            });
        var controller = CreateController();

        var actionResult = await controller.GetModels(
            new AiAssistantModelDiscoveryRequestDto
            {
                EndpointUrl = "http://127.0.0.1:1337/v1",
                ApiKey = string.Empty
            },
            httpClientFactory,
            new AiProviderSettingsValidator(),
            Options.Create(new AiProviderSettings
            {
                AllowLocalProviderEndpoints = true,
                MaxInputTokens = 8000,
                MaxOutputTokens = 1024,
                Temperature = 0.2m,
                TimeoutSeconds = 30,
                RetentionDays = 30,
                DailyMessageLimit = 50
            }),
            CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var models = ok!.Value as IReadOnlyList<AiAssistantModelDto>;
        await Assert.That(models).IsNotNull();
        await Assert.That(models!.Single().Id).IsEqualTo("Gemma-4-E2B-Uncensored-HauhauCS-Aggressive-Q8_K_P");
        await Assert.That(handler.RequestUri).IsEqualTo(new Uri("http://127.0.0.1:1337/v1/models"));
        await Assert.That(handler.Authorization).IsNull();
    }

    [Test]
    public async Task SendMessage_PropagatesIdempotencyHeaderAndReturnsAcceptedRunLocation()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var dto = new SendAiMessageRequestDto
        {
            Content = "Plan this event.",
            IdempotencyKey = "body-key"
        };
        var response = Success(runId);
        _mediator.Send(Arg.Any<SendAiMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.SendMessage(conversationId, dto, "header-key", CancellationToken.None);

        var accepted = actionResult.Result as AcceptedAtRouteResult;
        await Assert.That(accepted).IsNotNull();
        await Assert.That(accepted!.RouteName).IsEqualTo(RouteNames.GetAiRunStatus);
        await Assert.That(accepted.Value).IsEqualTo(response);
        await Assert.That(RouteValue<Guid>(accepted.RouteValues, "conversationId")).IsEqualTo(conversationId);
        await Assert.That(RouteValue<Guid>(accepted.RouteValues, "runId")).IsEqualTo(runId);
        await _mediator.Received(1).Send(
            Arg.Is<SendAiMessageCommand>(command =>
                command.ConversationId == conversationId &&
                command.Message.Content == dto.Content &&
                command.Message.Mode == dto.Mode &&
                command.Message.IdempotencyKey == "header-key"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SendMessage_UsesAiAssistantRateLimitPolicy()
    {
        var method = typeof(AiAssistantController).GetMethod(nameof(AiAssistantController.SendMessage))!;
        var attribute = method.GetCustomAttribute<EnableRateLimitingAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.PolicyName).IsEqualTo(RateLimitingExtensions.AiAssistantPolicy);
    }

    [Test]
    public async Task ConfirmProposedAction_PropagatesIdempotencyHeaderAndReturnsOk()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var response = Success(eventId);
        _mediator.Send(Arg.Any<ConfirmAiProposedActionCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.ConfirmProposedAction(
            conversationId,
            proposedActionId,
            "confirm-key",
            CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
        await _mediator.Received(1).Send(
            Arg.Is<ConfirmAiProposedActionCommand>(command =>
                command.ProposedActionId == proposedActionId &&
                command.IdempotencyKey == "confirm-key"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ConfirmProposedAction_WhenRejectedStateConflict_ReturnsSafeProblemDetails()
    {
        _mediator.Send(Arg.Any<ConfirmAiProposedActionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Failure("AI proposed action was already rejected.", "proposed_action_rejected"));
        var controller = CreateController();

        var actionResult = await controller.ConfirmProposedAction(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "confirm-key",
            CancellationToken.None);

        var result = actionResult.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");
        var problem = result.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("proposed_action_rejected");
        await Assert.That(problem.Detail).DoesNotContain("payload");
        await Assert.That(problem.Detail).DoesNotContain("prompt");
    }

    [Test]
    public async Task ConfirmProposedAction_WhenToolFailureIsBadRequest_ReturnsUnderlyingFailureDetail()
    {
        const string underlyingError = "Selected AI actor context is not allowed to create events.";
        _mediator.Send(Arg.Any<ConfirmAiProposedActionCommand>(), Arg.Any<CancellationToken>())
            .Returns(BaseCommandResponse.Failure(
                "actor_context_not_allowed",
                "AI proposed action confirmation failed.",
                [underlyingError],
                Guid.Empty));
        var controller = CreateController();

        var actionResult = await controller.ConfirmProposedAction(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "confirm-key",
            CancellationToken.None);

        var result = actionResult.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(StatusCodes.Status400BadRequest);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");
        var problem = result.Value as ValidationProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("actor_context_not_allowed");
        await Assert.That(problem.Detail).IsEqualTo(underlyingError);
        await Assert.That(problem.Errors["aiAssistant"]).Contains(underlyingError);
    }

    [Test]
    public async Task RejectProposedAction_DispatchesCommandAndReturnsOk()
    {
        var conversationId = Guid.CreateVersion7();
        var proposedActionId = Guid.CreateVersion7();
        var response = Success(proposedActionId);
        _mediator.Send(Arg.Any<RejectAiProposedActionCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.RejectProposedAction(conversationId, proposedActionId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
        await _mediator.Received(1).Send(
            Arg.Is<RejectAiProposedActionCommand>(command => command.ProposedActionId == proposedActionId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ProposedActionEndpoints_UseAiAssistantRateLimitPolicy()
    {
        var confirm = typeof(AiAssistantController).GetMethod(nameof(AiAssistantController.ConfirmProposedAction))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        var reject = typeof(AiAssistantController).GetMethod(nameof(AiAssistantController.RejectProposedAction))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        await Assert.That(confirm).IsNotNull();
        await Assert.That(confirm!.PolicyName).IsEqualTo(RateLimitingExtensions.AiAssistantPolicy);
        await Assert.That(reject).IsNotNull();
        await Assert.That(reject!.PolicyName).IsEqualTo(RateLimitingExtensions.AiAssistantPolicy);
    }

    [Test]
    public async Task CancelRun_DispatchesCommandAndReturnsOk()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        var response = Success(runId);
        _mediator.Send(Arg.Any<CancelAiRunCommand>(), Arg.Any<CancellationToken>())
            .Returns(response);
        var controller = CreateController();

        var actionResult = await controller.CancelRun(conversationId, runId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        await Assert.That(ok!.Value).IsEqualTo(response);
        await _mediator.Received(1).Send(
            Arg.Is<CancelAiRunCommand>(command => command.ConversationId == conversationId && command.RunId == runId),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CancelRun_WhenRunIsComplete_ReturnsSafeConflictProblemDetails()
    {
        _mediator.Send(Arg.Any<CancelAiRunCommand>(), Arg.Any<CancellationToken>())
            .Returns(Failure("Completed AI runs cannot be cancelled.", "run_not_cancellable"));
        var controller = CreateController();

        var actionResult = await controller.CancelRun(Guid.CreateVersion7(), Guid.CreateVersion7(), CancellationToken.None);

        var result = actionResult.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(StatusCodes.Status409Conflict);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");
        var problem = result.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Extensions["code"]).IsEqualTo("run_not_cancellable");
        await Assert.That(problem.Detail).DoesNotContain("payload");
        await Assert.That(problem.Detail).DoesNotContain("prompt");
    }

    [Test]
    public async Task GetRunStatus_WhenRunIsCancellable_AddsCancelLink()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<GetAiRunStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AiRunDto
            {
                Id = runId,
                Status = "InProgress",
                Provider = "fake",
                ModelId = "fake-ai-assistant-v1",
                QueuedAt = DateTime.UtcNow
            });
        _linkGenerator.GeneratePath(RouteNames.GetAiRunStatus, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}/runs/{runId}");
        _linkGenerator.GeneratePath(RouteNames.GetAiConversation, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}");
        _linkGenerator.GeneratePath(RouteNames.CancelAiRun, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}/runs/{runId}/cancel");
        var controller = CreateController();

        var actionResult = await controller.GetRunStatus(conversationId, runId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var resource = ok!.Value as HalResource<AiRunDto>;
        await Assert.That(resource).IsNotNull();
        await Assert.That(resource!.Links[LinkRelations.CancelRun].Href).Contains("/cancel");
        await Assert.That(resource.Links[LinkRelations.CancelRun].Method).IsEqualTo("POST");
    }

    [Test]
    public async Task GetRunStatus_WhenRunIsTerminal_DoesNotAddCancelLink()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<GetAiRunStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AiRunDto
            {
                Id = runId,
                Status = "Succeeded",
                Provider = "fake",
                ModelId = "fake-ai-assistant-v1",
                QueuedAt = DateTime.UtcNow
            });
        _linkGenerator.GeneratePath(RouteNames.GetAiRunStatus, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}/runs/{runId}");
        _linkGenerator.GeneratePath(RouteNames.GetAiConversation, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}");
        var controller = CreateController();

        var actionResult = await controller.GetRunStatus(conversationId, runId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var resource = ok!.Value as HalResource<AiRunDto>;
        await Assert.That(resource).IsNotNull();
        await Assert.That(resource!.Links.ContainsKey(LinkRelations.CancelRun)).IsFalse();
    }

    [Test]
    public async Task CancelRun_UsesAiAssistantRateLimitPolicy()
    {
        var attribute = typeof(AiAssistantController).GetMethod(nameof(AiAssistantController.CancelRun))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        await Assert.That(attribute).IsNotNull();
        await Assert.That(attribute!.PolicyName).IsEqualTo(RateLimitingExtensions.AiAssistantPolicy);
    }

    [Test]
    public async Task SendMessage_WhenProviderNotReady_ReturnsSafeProblemDetails()
    {
        var conversationId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<SendAiMessageCommand>(), Arg.Any<CancellationToken>())
            .Returns(Failure("AI provider is not ready.", "provider_not_ready"));
        var controller = CreateController();

        var actionResult = await controller.SendMessage(
            conversationId,
            new SendAiMessageRequestDto { Content = "hello", IdempotencyKey = "key" },
            null,
            CancellationToken.None);

        var result = actionResult.Result as ObjectResult;
        await Assert.That(result).IsNotNull();
        await Assert.That(result!.StatusCode).IsEqualTo(StatusCodes.Status503ServiceUnavailable);
        await Assert.That(result.ContentTypes).Contains("application/problem+json");
        var problem = result.Value as ProblemDetails;
        await Assert.That(problem).IsNotNull();
        await Assert.That(problem!.Title).IsEqualTo("AI provider unavailable");
        await Assert.That(problem.Extensions["code"]).IsEqualTo("provider_not_ready");
        await Assert.That(problem.Detail).DoesNotContain("sk-");
        await Assert.That(problem.Detail).DoesNotContain("prompt");
    }

    [Test]
    public async Task GetRunStatus_ReturnsHalResourceWithSelfAndConversationLinks()
    {
        var conversationId = Guid.CreateVersion7();
        var runId = Guid.CreateVersion7();
        _mediator.Send(Arg.Any<GetAiRunStatusQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AiRunDto
            {
                Id = runId,
                Status = "Succeeded",
                Provider = "fake",
                ModelId = "fake-ai-assistant-v1",
                QueuedAt = DateTime.UtcNow
            });
        _linkGenerator.GeneratePath(RouteNames.GetAiRunStatus, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}/runs/{runId}");
        _linkGenerator.GeneratePath(RouteNames.GetAiConversation, Arg.Any<object>(), Arg.Any<HttpContext>())
            .Returns($"/api/ai/assistant/conversations/{conversationId}");
        var controller = CreateController();

        var actionResult = await controller.GetRunStatus(conversationId, runId, CancellationToken.None);

        var ok = actionResult.Result as OkObjectResult;
        await Assert.That(ok).IsNotNull();
        var resource = ok!.Value as HalResource<AiRunDto>;
        await Assert.That(resource).IsNotNull();
        await Assert.That(resource!.Links[LinkRelations.Self].Href).Contains($"/runs/{runId}");
        await Assert.That(resource.Links[LinkRelations.Up].Href).Contains($"/conversations/{conversationId}");
    }

    [Test]
    public async Task AiConversationHalResource_SerializesLinksWithSourceGeneratedContext()
    {
        var conversationId = Guid.CreateVersion7();
        var resource = new HalResource<AiConversationDto>(CreateDetail(conversationId, "Active") with
        {
            ProposedActions =
            [
                new AiProposedActionDto
                {
                    Id = Guid.CreateVersion7(),
                    Kind = "CreateEventDraft",
                    Status = "Proposed",
                    CreatedAt = DateTime.UtcNow,
                    Links = new Dictionary<string, HalLink>
                    {
                        [LinkRelations.ConfirmAction] = HalLink.CreateAction($"/api/ai/assistant/conversations/{conversationId}/proposed-actions/action/confirm", "POST")
                    }
                }
            ]
        });
        resource.WithLink(LinkRelations.Self, HalLink.Create($"/api/ai/assistant/conversations/{conversationId}"));
        resource.WithLink(LinkRelations.SendMessage, HalLink.CreateAction($"/api/ai/assistant/conversations/{conversationId}/messages", "POST"));

        var json = JsonSerializer.Serialize(resource, ExploreJsonContext.Default.Options);

        await Assert.That(json).Contains("_links");
        await Assert.That(json).Contains("send-message");
        await Assert.That(json).Contains("confirm-action");
        await Assert.That(json).Contains("POST");
        await Assert.That(json).Contains(conversationId.ToString());
    }

    private AiAssistantController CreateController()
    {
        var httpContext = new DefaultHttpContext
        {
            TraceIdentifier = "trace-ai-test"
        };
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.CreateVersion7().ToString())], "test"));
        httpContext.Items["CorrelationId"] = "correlation-ai-test";

        _tenantContext.TenantId.Returns(Guid.CreateVersion7());

        return new AiAssistantController(_mediator, _linkGenerator, _conversationAssembler, _runQueue, _tenantContext)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static AiConversationSummaryDto CreateSummary(Guid id, string status) =>
        new()
        {
            Id = id,
            TenantId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

    private static AiConversationDto CreateDetail(Guid id, string status) =>
        new()
        {
            Id = id,
            TenantId = Guid.CreateVersion7(),
            UserId = Guid.CreateVersion7(),
            Status = status,
            CreatedAt = DateTime.UtcNow
        };

    private static BaseCommandResponse<Guid> Success(Guid id) =>
        BaseCommandResponse.Success(id, "OK");

    private static BaseCommandResponse<Guid> Failure(string message, string failureCode) =>
        BaseCommandResponse.Failure(failureCode, message, [message], Guid.Empty);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static T RouteValue<T>(object? routeValues, string key)
    {
        if (routeValues is RouteValueDictionary dictionary && dictionary.TryGetValue(key, out var dictionaryValue))
        {
            return (T)dictionaryValue!;
        }

        var value = routeValues?.GetType().GetProperty(key)?.GetValue(routeValues);
        return value is null ? default! : (T)value;
    }

    private static void AssertRoute(string methodName, Type attributeType, string template, string routeName)
    {
        var method = typeof(AiAssistantController).GetMethod(methodName)!;
        var attribute = method.GetCustomAttributes<HttpMethodAttribute>()
            .Single(candidate => candidate.GetType() == attributeType);

        Assert.That(attribute.Template).IsEqualTo(template);
        Assert.That(attribute.Name).IsEqualTo(routeName);
    }

    private sealed class StaticHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingModelDiscoveryHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return Task.FromResult(responseFactory(request));
        }
    }
}
