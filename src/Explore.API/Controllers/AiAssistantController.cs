using System.Text.Json;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.BackgroundServices;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Ai;
using Explore.Application.Features.AiAssistant.Requests.Commands;
using Explore.Application.Features.AiAssistant.Requests.Queries;
using Explore.Application.Features.TenantOnboarding.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Infrastructure.Ai;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/ai/assistant")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class AiAssistantController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";
    private const int DefaultReferenceLimit = 10;
    private const int MaxReferenceLimit = 20;

    private static readonly ApiValidationProblemDescriptor ModelDiscoveryValidationProblem = new(
        "aiAssistantModelDiscovery",
        "AI assistant model discovery validation failed",
        "AI assistant model discovery failed.");

    private static readonly ApiNotFoundProblemDescriptor ConversationNotFoundProblem = new(
        "AI conversation not found",
        "AI conversation not found.");

    private static readonly ApiNotFoundProblemDescriptor RunNotFoundProblem = new(
        "AI run not found",
        "AI run not found.");

    private readonly IMediator _mediator;
    private readonly IHateoasLinkGenerator _linkGenerator;
    private readonly IResourceAssembler<AiConversationDto, AiConversationSummaryDto> _conversationAssembler;
    private readonly IAiAssistantRunQueue _runQueue;
    private readonly ITenantContext _tenantContext;

    public AiAssistantController(
        IMediator mediator,
        IHateoasLinkGenerator linkGenerator,
        IResourceAssembler<AiConversationDto, AiConversationSummaryDto> conversationAssembler,
        IAiAssistantRunQueue runQueue,
        ITenantContext tenantContext)
    {
        _mediator = mediator;
        _linkGenerator = linkGenerator;
        _conversationAssembler = conversationAssembler;
        _runQueue = runQueue;
        _tenantContext = tenantContext;
    }

    [HttpGet("bootstrap", Name = RouteNames.GetAiAssistantBootstrap)]
    [EndpointSummary("Get AI assistant bootstrap")]
    [EndpointDescription("Returns authenticated AI assistant availability, model choices, feature flags, limits, and HAL links without exposing provider secrets.")]
    [ProducesResponseType(typeof(HalResource<AiAssistantBootstrapDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<AiAssistantBootstrapDto>>> GetBootstrap(CancellationToken cancellationToken = default)
    {
        var bootstrap = await _mediator.Send(new GetAiAssistantBootstrapQuery(), cancellationToken);
        var resource = new HalResource<AiAssistantBootstrapDto>(bootstrap);
        var selfPath = _linkGenerator.GeneratePath(RouteNames.GetAiAssistantBootstrap, null, HttpContext);

        if (!string.IsNullOrWhiteSpace(selfPath))
        {
            resource.WithLink(LinkRelations.Self, HalLink.Create(selfPath));
        }

        return Ok(resource);
    }

    [HttpPost("models", Name = RouteNames.GetAiAssistantModels)]
    [EndpointSummary("Discover AI assistant models")]
    [EndpointDescription("Returns OpenAI-compatible model IDs exposed by the supplied provider endpoint without exposing credentials.")]
    [ProducesResponseType(typeof(IReadOnlyList<AiAssistantModelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<AiAssistantModelDto>>> GetModels(
        [FromBody] AiAssistantModelDiscoveryRequestDto request,
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] AiProviderSettingsValidator settingsValidator,
        [FromServices] IOptions<AiProviderSettings> providerOptions,
        CancellationToken cancellationToken = default)
    {
        var status = await _mediator.Send(new GetTenantOnboardingStatusQuery(), cancellationToken);
        if (!status.IsCurrentUserTenantAdministrator && !status.IsCurrentUserPlatformAdministrator)
        {
            return this.ToForbiddenProblem(detail: "Tenant or platform administrator authority is required to discover AI assistant models.");
        }

        if (string.IsNullOrWhiteSpace(request.EndpointUrl))
        {
            return this.ToValidationProblem(
                ModelDiscoveryValidationProblem,
                "Enter an OpenAI-compatible endpoint URL before fetching models.");
        }

        var settings = new AiProviderSettings
        {
            Enabled = true,
            Provider = AiProviderSettings.ProviderOpenAiCompatible,
            EndpointUrl = request.EndpointUrl.Trim(),
            ApiKey = request.ApiKey?.Trim() ?? string.Empty,
            ModelId = "model-discovery",
            MaxInputTokens = providerOptions.Value.MaxInputTokens,
            MaxOutputTokens = providerOptions.Value.MaxOutputTokens,
            Temperature = providerOptions.Value.Temperature,
            TimeoutSeconds = providerOptions.Value.TimeoutSeconds,
            RetentionDays = providerOptions.Value.RetentionDays,
            DailyMessageLimit = providerOptions.Value.DailyMessageLimit,
            AllowLocalProviderEndpoints = providerOptions.Value.AllowLocalProviderEndpoints
        };

        var validation = settingsValidator.Validate(null, settings);
        if (!validation.Succeeded)
        {
            return this.ToValidationProblem(
                ModelDiscoveryValidationProblem,
                string.Join(" ", validation.Failures ?? []));
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 30)));

            var client = httpClientFactory.CreateClient(OpenAiCompatibleChatProvider.HttpClientName);
            var models = await OpenAiCompatibleChatProvider.DiscoverModelsAsync(
                client,
                settings.EndpointUrl,
                settings.ApiKey,
                timeout.Token);

            IReadOnlyList<AiAssistantModelDto> response = models
                .Select(model => new AiAssistantModelDto
                {
                    Id = model.Id,
                    DisplayName = model.DisplayName,
                    MaxInputTokens = model.MaxInputTokens,
                    MaxOutputTokens = model.MaxOutputTokens,
                    SupportsToolProposals = model.SupportsToolProposals,
                    SupportsStreaming = model.SupportsStreaming
                })
                .ToList();

            return Ok(response);
        }
        catch (UriFormatException)
        {
            return this.ToValidationProblem(
                ModelDiscoveryValidationProblem,
                "The endpoint URL must be an absolute HTTP or HTTPS URL.");
        }
        catch (JsonException)
        {
            return this.ToBadGatewayProblem(
                "Invalid model response",
                "The provider did not return a valid OpenAI-compatible models response.");
        }
        catch (HttpRequestException ex)
        {
            return this.ToBadGatewayProblem("Model discovery failed", ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return this.ToBadGatewayProblem(
                "Model discovery timed out",
                "The provider did not respond before the request timed out.");
        }
    }

    [HttpGet("conversations", Name = RouteNames.GetAiConversations)]
    [EndpointSummary("Get AI conversations")]
    [EndpointDescription("Returns the authenticated user's recent private AI assistant conversations with HAL navigation links.")]
    [ProducesResponseType(typeof(HalCollectionResource<AiConversationSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<AiConversationSummaryDto>>> GetConversations(
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var conversations = await _mediator.Send(new GetAiConversationListQuery { Limit = limit }, cancellationToken);
        var resource = await _conversationAssembler.ToCollectionResource(
            conversations,
            RouteNames.GetAiConversations,
            new { limit },
            HttpContext);

        return Ok(resource);
    }

    [HttpPost("conversations", Name = RouteNames.CreateAiConversation)]
    [EndpointSummary("Create AI conversation")]
    [EndpointDescription("Creates a private AI assistant conversation after tenant governance checks pass. The endpoint does not call a provider.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateConversation(
        [FromBody] CreateAiConversationRequestDto dto,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CreateAiConversationCommand { Conversation = dto }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToAiAssistantProblem(response);
        }

        return CreatedAtRoute(RouteNames.GetAiConversation, new { conversationId = response.Id }, response);
    }

    [HttpGet("conversations/{conversationId:guid}", Name = RouteNames.GetAiConversation)]
    [EndpointSummary("Get AI conversation detail")]
    [EndpointDescription("Returns owned AI assistant conversation history with safe message, run, reference, and proposed-action metadata.")]
    [ProducesResponseType(typeof(HalResource<AiConversationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<AiConversationDto>>> GetConversation(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _mediator.Send(new GetAiConversationDetailQuery { ConversationId = conversationId }, cancellationToken);

        if (conversation is null)
        {
            return this.ToNotFoundProblem(ConversationNotFoundProblem);
        }

        return Ok(await _conversationAssembler.ToResource(conversation, HttpContext));
    }

    [HttpGet("references", Name = RouteNames.SearchAiReferences)]
    [EndpointSummary("Search AI references")]
    [EndpointDescription("Searches lightweight tenant-visible event, actor, and organization references for AI assistant prompt context without returning full resource content.")]
    [ProducesResponseType(typeof(HalCollectionResource<AiReferenceSearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(RateLimitingExtensions.AiAssistantPolicy)]
    public async Task<ActionResult<HalCollectionResource<AiReferenceSearchResultDto>>> SearchReferences(
        [FromQuery] string searchTerm,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        int normalizedLimit = NormalizeReferenceLimit(limit);
        IReadOnlyList<AiReferenceSearchResultDto> references = await _mediator.Send(new SearchAiReferencesQuery
        {
            SearchTerm = searchTerm,
            Limit = normalizedLimit
        }, cancellationToken);

        var items = references.Select(CreateReferenceResource).ToList();
        var links = new Dictionary<string, HalLink>();
        AddLink(links, LinkRelations.Self, RouteNames.SearchAiReferences, new { searchTerm, limit = normalizedLimit });

        return Ok(HalCollectionResource<AiReferenceSearchResultDto>.Create(
            items,
            pageNumber: 1,
            pageSize: normalizedLimit,
            totalCount: references.Count,
            links));
    }

    [HttpPost("conversations/{conversationId:guid}/messages", Name = RouteNames.SendAiMessage)]
    [EndpointSummary("Send AI conversation message")]
    [EndpointDescription("Sends a bounded user message into an owned AI conversation using an Idempotency-Key header and guarded Application orchestration.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.AiAssistantPolicy)]
    [DisableRequestTimeout]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> SendMessage(
        Guid conversationId,
        [FromBody] SendAiMessageRequestDto dto,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            dto = dto with { IdempotencyKey = idempotencyKey };
        }

        var response = await _mediator.Send(new SendAiMessageCommand
        {
            ConversationId = conversationId,
            Message = dto
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToAiAssistantProblem(response);
        }

        var interactionMode = AiAssistantInteractionModes.Normalize(dto.Mode);

        await _runQueue.EnqueueAsync(
            new AiAssistantRunQueueItem(
                _tenantContext.TenantId,
                conversationId,
                response.Id,
                interactionMode),
            CancellationToken.None);

        return AcceptedAtRoute(RouteNames.GetAiRunStatus, new { conversationId, runId = response.Id }, response);
    }

    [HttpPost("conversations/{conversationId:guid}/proposed-actions/{proposedActionId:guid}/confirm", Name = RouteNames.ConfirmAiProposedAction)]
    [EndpointSummary("Confirm AI proposed action")]
    [EndpointDescription("Confirms one owned AI-proposed action and executes it through the governed Application tool executor path.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting(RateLimitingExtensions.AiAssistantPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ConfirmProposedAction(
        Guid conversationId,
        Guid proposedActionId,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        _ = conversationId;
        var response = await _mediator.Send(new ConfirmAiProposedActionCommand
        {
            ProposedActionId = proposedActionId,
            IdempotencyKey = idempotencyKey
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToAiAssistantProblem(response);
        }

        return Ok(response);
    }

    [HttpPost("conversations/{conversationId:guid}/proposed-actions/{proposedActionId:guid}/reject", Name = RouteNames.RejectAiProposedAction)]
    [EndpointSummary("Reject AI proposed action")]
    [EndpointDescription("Rejects one owned AI-proposed action without executing any tool side effects.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(RateLimitingExtensions.AiAssistantPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RejectProposedAction(
        Guid conversationId,
        Guid proposedActionId,
        CancellationToken cancellationToken = default)
    {
        _ = conversationId;
        var response = await _mediator.Send(new RejectAiProposedActionCommand
        {
            ProposedActionId = proposedActionId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToAiAssistantProblem(response);
        }

        return Ok(response);
    }

    [HttpGet("conversations/{conversationId:guid}/runs/{runId:guid}", Name = RouteNames.GetAiRunStatus)]
    [EndpointSummary("Get AI run status")]
    [EndpointDescription("Returns safe status metadata for one AI provider run in an owned conversation.")]
    [ProducesResponseType(typeof(HalResource<AiRunDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<AiRunDto>>> GetRunStatus(
        Guid conversationId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var run = await _mediator.Send(new GetAiRunStatusQuery
        {
            ConversationId = conversationId,
            RunId = runId
        }, cancellationToken);

        if (run is null)
        {
            return this.ToNotFoundProblem(RunNotFoundProblem);
        }

        var resource = new HalResource<AiRunDto>(run);
        AddResourceLink(resource, LinkRelations.Self, RouteNames.GetAiRunStatus, new { conversationId, runId });
        AddResourceLink(resource, LinkRelations.Up, RouteNames.GetAiConversation, new { conversationId });
        if (IsCancellableRun(run.Status))
        {
            AddResourceLink(resource, LinkRelations.CancelRun, RouteNames.CancelAiRun, new { conversationId, runId }, "POST");
        }

        return Ok(resource);
    }

    [HttpPost("conversations/{conversationId:guid}/runs/{runId:guid}/cancel", Name = RouteNames.CancelAiRun)]
    [EndpointSummary("Cancel AI run")]
    [EndpointDescription("Cancels one queued or in-progress AI provider run in an owned conversation without producing proposed actions.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(RateLimitingExtensions.AiAssistantPolicy)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CancelRun(
        Guid conversationId,
        Guid runId,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CancelAiRunCommand
        {
            ConversationId = conversationId,
            RunId = runId
        }, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToAiAssistantProblem(response);
        }

        return Ok(response);
    }

    private void AddResourceLink<T>(
        HalResource<T> resource,
        string rel,
        string routeName,
        object? routeValues,
        string? method = null)
        where T : class
    {
        var path = _linkGenerator.GeneratePath(routeName, routeValues, HttpContext);

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        resource.WithLink(rel, method is null ? HalLink.Create(path) : HalLink.CreateAction(path, method));
    }

    private HalResource<AiReferenceSearchResultDto> CreateReferenceResource(AiReferenceSearchResultDto reference)
    {
        var resource = new HalResource<AiReferenceSearchResultDto>(reference);
        switch (reference.Kind)
        {
            case "Event":
                AddResourceLink(resource, LinkRelations.Event, RouteNames.GetEventById, new { id = reference.ReferenceId });
                break;
            case "Actor":
                AddResourceLink(resource, LinkRelations.Actor, RouteNames.GetActorById, new { id = reference.ReferenceId });
                break;
            case "Organization":
                AddResourceLink(resource, LinkRelations.Organization, RouteNames.GetOrganizationById, new { id = reference.ReferenceId });
                break;
        }

        return resource;
    }

    private void AddLink(Dictionary<string, HalLink> links, string rel, string routeName, object routeValues, string? method = null)
    {
        var path = _linkGenerator.GeneratePath(routeName, routeValues, HttpContext);

        if (!string.IsNullOrWhiteSpace(path))
        {
            links[rel] = method is null ? HalLink.Create(path) : HalLink.CreateAction(path, method);
        }
    }

    private static int NormalizeReferenceLimit(int limit)
    {
        if (limit <= 0)
        {
            return DefaultReferenceLimit;
        }

        return Math.Min(limit, MaxReferenceLimit);
    }

    private static bool IsCancellableRun(string status) =>
        string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "InProgress", StringComparison.OrdinalIgnoreCase);

}
