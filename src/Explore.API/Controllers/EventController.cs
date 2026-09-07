using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.API.Services.Calendar;
using Explore.Application.DTOs.Event;
using Explore.Application.DTOs.EventProgram;
using Explore.Application.DTOs.EventSession;
using Explore.Application.DTOs.PublicExperience;
using Explore.Application.Features.EventPrograms.Requests.Queries;
using Explore.Application.Features.Events.Moderation;
using Explore.Application.Features.Events.Requests.Commands;
using Explore.Application.Features.Events.Requests.Queries;
using Explore.Application.Features.EventSessions.Requests.Queries;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Specifications.Events;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace Explore.API.Controllers;

/// <summary>
/// Event management API endpoints.
/// All responses include HATEOAS links by default.
/// Send "Prefer: return=minimal" header to strip links.
/// </summary>
[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class EventController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor FilterValidationProblem = new(
        "eventFilter",
        "Event filter validation failed",
        "Event filtering failed.");

    private static readonly ApiNotFoundProblemDescriptor EventNotFoundProblem = new(
        "Event not found",
        "Event not found.");

    private readonly IMediator _mediator;
    private readonly IResourceAssembler<EventDto, EventListDto> _resourceAssembler;
    private readonly IResourceAssembler<EventDiscoveryItemDto> _eventDiscoveryResourceAssembler;

    public EventController(
        IMediator mediator,
        IResourceAssembler<EventDto, EventListDto> resourceAssembler,
        IResourceAssembler<EventDiscoveryItemDto> eventDiscoveryResourceAssembler)
    {
        _mediator = mediator;
        _resourceAssembler = resourceAssembler;
        _eventDiscoveryResourceAssembler = eventDiscoveryResourceAssembler;
    }

    /// <summary>
    /// Get all events with pagination and optional filtering.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet(Name = RouteNames.GetEvents)]
    [EndpointSummary("Get all Events")]
    [EndpointDescription("Get a paginated, filterable list of all Events (Conference, Webinar, Workshop...). " +
        "Default page size is 20, max is 100. " +
        "Supports filtering by category, tag, format, madhab, language, date range, and free-text search. " +
        "Supports module-conditional aspect filters: Islamic (genderMode, quranRecitation, referencePrayer, islamicLanguage) " +
        "and Tech (skillLevel, codingCompetition, hackathon, requiresLaptop, techStack). " +
        "Aspect filters are silently ignored when the corresponding module is not enabled for the tenant. " +
        "Supports custom-property projection filters and text search gated behind the tenant feature flag " +
        "'custom_properties.projection_discovery_enabled' — silently ignored when disabled. " +
        "Supports sorting by date, title, views, or createdAt. " +
        "When tenant governance enables ATProto Events, the result also includes tenant-visible community events " +
        "within a bounded 1,000-item merge window, de-duplicated against locally-owned ATProto records. " +
        "Response includes HATEOAS navigation links (first, prev, next, last) and safe source affordances. " +
        "Send 'Prefer: return=minimal' header to strip links.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventDiscoveryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [OutputCache(PolicyName = "EventDiscovery")]
    public async Task<ActionResult<HalCollectionResource<EventDiscoveryItemDto>>> GetAll(
        [FromQuery] EventFilterRequest filter,
        CancellationToken cancellationToken = default)
    {
        if (Request.Query.Keys.Any(static key =>
                string.Equals(key, "locationIds", StringComparison.OrdinalIgnoreCase)))
        {
            return this.ToValidationProblem(
                FilterValidationProblem,
                "The locationIds filter is not available on public event discovery.");
        }

        var result = await _mediator.Send(new GetPublicEventDiscoveryRequest(new GetEventListRequest
        {
            PageNumber = filter.PageNumber,
            PageSize = filter.PageSize,
            SearchTerm = filter.SearchTerm,
            ActorId = filter.ActorId,
            OrganizationId = filter.OrganizationId,
            GroupId = filter.GroupId,
            CategoryId = filter.CategoryId,
            IncludedCategoryIds = filter.IncludedCategoryIds,
            ExcludedCategoryIds = filter.ExcludedCategoryIds,
            CategoryInclusionMode = ParseTagFilterMode(filter.CategoryInclusionMode, TagFilterMode.And),
            CategoryExclusionMode = ParseTagFilterMode(filter.CategoryExclusionMode, TagFilterMode.Or),
            IncludedTagIds = filter.IncludedTagIds,
            ExcludedTagIds = filter.ExcludedTagIds,
            InclusionMode = ParseTagFilterMode(filter.InclusionMode, TagFilterMode.And),
            ExclusionMode = ParseTagFilterMode(filter.ExclusionMode, TagFilterMode.Or),
            FormatIds = filter.FormatIds,
            MadhabIds = filter.MadhabIds,
            RegistrationModeIds = filter.RegistrationModeIds,
            LanguageIds = filter.LanguageIds,
            DateFrom = filter.DateFrom,
            DateTo = filter.DateTo,
            EventTypeIds = filter.EventTypeIds,
            AudienceGenderIds = filter.AudienceGenderIds,
            AudienceAgeIds = filter.AudienceAgeIds,
            EventStatusIds = filter.EventStatusIds,
            GenderModeIds = filter.GenderModeIds,
            IncludesQuranRecitation = filter.IncludesQuranRecitation,
            ReferencePrayerIds = filter.ReferencePrayerIds,
            IslamicPrimaryLanguageIds = filter.IslamicPrimaryLanguageIds,
            HasIslamicAspect = filter.HasIslamicAspect,
            SkillLevelId = filter.SkillLevelId,
            IsCodingCompetition = filter.IsCodingCompetition,
            IsHackathon = filter.IsHackathon,
            RequiresLaptop = filter.RequiresLaptop,
            TechStackTag = filter.TechStackTag,
            HasTechAspect = filter.HasTechAspect,
            SortBy = filter.SortBy,
            SortDescending = filter.SortDescending,
            View = ParseTemporalView(filter.View),
            CustomPropertyFilters = filter.CustomPropertyFilters,
            CustomPropertySearchTerm = filter.CustomPropertySearchTerm
        }), cancellationToken);

        var halResource = await _eventDiscoveryResourceAssembler.ToCollectionResource(
            result,
            RouteNames.GetEvents,
            additionalRouteValues: new
            {
                filter.ActorId,
                filter.OrganizationId,
                filter.GroupId
            },
            HttpContext);

        return Ok(halResource);
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.GlobalPolicy)]
    [HttpGet("federated/{atprotoRecordId:guid}/source", Name = RouteNames.GetAtprotoEventSource)]
    [EndpointSummary("Open Federated Event Source")]
    [EndpointDescription("Redirects to the current tenant-visible HTTPS source for a federated event after rechecking ATProto Events governance.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAtprotoEventSource(
        Guid atprotoRecordId,
        CancellationToken cancellationToken = default)
    {
        string? sourceUrl = await _mediator.Send(
            new GetAtprotoEventSourceQuery(atprotoRecordId),
            cancellationToken);
        return sourceUrl is null
            ? this.ToNotFoundProblem(EventNotFoundProblem)
            : Redirect(sourceUrl);
    }

    /// <summary>
    /// Get events for the current user's organizations.
    /// </summary>
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("my", Name = RouteNames.GetMyEvents)]
    [EndpointSummary("Get My Events")]
    [EndpointDescription("Get a paginated list of events created by the current user's organizations. " +
        "Default page size is 20, max is 100.")]
    [ProducesResponseType(typeof(HalCollectionResource<EventListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HalCollectionResource<EventListDto>>> GetMyEvents(
        [FromQuery] PaginationQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserId?.ToString();

        if (string.IsNullOrEmpty(userId))
        {
            return this.ToAuthenticationRequiredProblem();
        }

        var result = await _mediator.Send(new GetMyEventsRequest
        {
            UserId = userId,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        }, cancellationToken);

        var halResource = await _resourceAssembler.ToCollectionResource(
            result!,
            RouteNames.GetMyEvents,
            additionalRouteValues: null,
            HttpContext);

        return Ok(halResource);
    }






    /// <summary>
    /// Get event details by ID.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("{id:guid}", Name = RouteNames.GetEventById)]
    [EndpointSummary("Get Event Details")]
    [EndpointDescription("Get full details of an event including actor information, sessions, and related resources. " +
        "Response includes links to related resources (sessions, categories, tags).")]
    [ProducesResponseType(typeof(HalResource<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var @event = await _mediator.Send(new GetEventDetailsRequest { Id = id }, cancellationToken);
        if (@event == null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        var halResource = await _resourceAssembler.ToResource(@event, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Get public event details by slug-code URL token.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("public/{slugCode}", Name = RouteNames.GetEventByPublicCode)]
    [EndpointSummary("Get Event Details By Public Code")]
    [EndpointDescription("Get full public event details from a clean slug-code URL token.")]
    [ProducesResponseType(typeof(HalResource<EventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<HalResource<EventDto>>> GetByPublicCode(string slugCode, CancellationToken cancellationToken = default)
    {
        var @event = await _mediator.Send(new GetPublicEventDetailsRequest { SlugCode = slugCode }, cancellationToken);
        if (@event == null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        var halResource = await _resourceAssembler.ToResource(@event, HttpContext);
        return Ok(halResource);
    }

    /// <summary>
    /// Render the canonical Open Graph image for a public event.
    /// </summary>
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [EnableRateLimiting(RateLimitingExtensions.EventOpenGraphImagePolicy)]
    [HttpGet("public/{slugCode}/og-image", Name = RouteNames.GetEventOpenGraphImage)]
    [EndpointSummary("Get Public Event Open Graph Image")]
    [EndpointDescription("Returns a deterministic 1200x630 PNG for an eligible public event, or the generic event-not-found response when the event is not publicly renderable.")]
    [Produces("image/png")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> GetOpenGraphImage(string slugCode, CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPublicEventOpenGraphImageRequest { SlugCode = slugCode },
            cancellationToken);
        if (result is null)
            return this.ToNotFoundProblem(EventNotFoundProblem);

        var entityTag = EntityTagHeaderValue.Parse(result.ETag);
        Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
        Response.Headers.Vary = "Host, X-Tenant-Slug";

        var fileResult = File(result.PngBytes.ToArray(), "image/png");
        fileResult.EntityTag = entityTag;
        return fileResult;
    }

















    private static TagFilterMode ParseTagFilterMode(string? value, TagFilterMode defaultValue) =>
        value?.ToLowerInvariant() switch
        {
            "and" => TagFilterMode.And,
            "or" => TagFilterMode.Or,
            _ => defaultValue
        };

    private static TemporalView? ParseTemporalView(string? value) =>
        Enum.TryParse<TemporalView>(value, ignoreCase: true, out var view) ? view : null;







}
