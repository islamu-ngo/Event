using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Resources;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventTemplateSync;
using Explore.Application.Features.EventTemplateSync.Commands.ApplyEventTemplateSync;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateDiff;
using Explore.Application.Features.EventTemplateSync.Queries.GetEventTemplateSyncHistory;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class EventTemplateSyncController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor DiffValidationProblem = new(
        "eventTemplateSync",
        "Event template sync validation failed",
        "Event template diff computation failed.");

    private readonly IMediator _mediator;
    private readonly IHateoasAuthorizationEvaluator _authorizationEvaluator;
    private readonly IHateoasLinkGenerator _linkGenerator;
    private readonly ILinkPolicy<EventTemplateSyncResource> _syncLinkPolicy;
    private readonly ITenantContext _tenantContext;

    public EventTemplateSyncController(
        IMediator mediator,
        IHateoasAuthorizationEvaluator authorizationEvaluator,
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventTemplateSyncResource> syncLinkPolicy,
        ITenantContext tenantContext)
    {
        _mediator = mediator;
        _authorizationEvaluator = authorizationEvaluator;
        _linkGenerator = linkGenerator;
        _syncLinkPolicy = syncLinkPolicy;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Compute the event-template diff for a requested target template version.
    /// </summary>
    [HttpGet("{eventId:guid}/template-sync/diff", Name = RouteNames.GetEventTemplateSyncDiff)]
    [ProducesResponseType(typeof(HalResource<TemplateDiffDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<TemplateDiffDto>>> GetDiff(
        Guid eventId,
        [FromQuery(Name = "templateVersion")] int templateVersion,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetEventTemplateDiffQuery(eventId, templateVersion),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, DiffValidationProblem);
        }

        if (response.Id is null)
        {
            return this.ToValidationProblem(DiffValidationProblem, "Event template diff was not returned.");
        }

        var diff = response.Id;
        var hasChanges =
            diff.AddedDefinitions.Count > 0 ||
            diff.ModifiedDefinitions.Count > 0 ||
            diff.RetiredDefinitions.Count > 0 ||
            diff.AddedOptions.Count > 0 ||
            diff.ModifiedOptions.Count > 0 ||
            diff.RetiredOptions.Count > 0;

        var resource = new EventTemplateSyncResource(_tenantContext.TenantId, eventId, diff.TargetTemplateVersion, hasChanges);
        var halResource = new HalResource<TemplateDiffDto>(diff);

        var linkDefinitions = _syncLinkPolicy.GetLinks(resource, User).ToList();
        var allowedLinks = await _authorizationEvaluator.AreLinksAllowedAsync(linkDefinitions, User, HttpContext);

        for (var i = 0; i < linkDefinitions.Count; i++)
        {
            if (!allowedLinks[i])
                continue;

            var linkDef = linkDefinitions[i];
            var halLink = _linkGenerator.GenerateLink(linkDef, HttpContext);
            if (halLink is not null)
            {
                halResource.WithLink(linkDef.Rel, halLink);
            }
        }
        return Ok(halResource);
    }

    /// <summary>
    /// Apply an operator-selected event-template sync plan.
    /// </summary>
    [HttpPost("{eventId:guid}/template-sync/apply", Name = RouteNames.ApplyEventTemplateSync)]
    [RequestTimeout(RequestTimeoutExtensions.ComplexPolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(TemplateSyncOutcomeDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<TemplateSyncOutcomeDto>>> Apply(
        Guid eventId,
        [FromBody] EventTemplateSyncApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new ApplyEventTemplateSyncCommand(eventId, request.Plan, request.BaseProvenanceVersion),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Get paged event-template sync audit history.
    /// </summary>
    [HttpGet("{eventId:guid}/template-sync/history", Name = RouteNames.GetEventTemplateSyncHistory)]
    [ProducesResponseType(typeof(PaginatedResult<EventTemplateSyncHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PaginatedResult<EventTemplateSyncHistoryItemDto>>> GetHistory(
        Guid eventId,
        [FromQuery] TemplateSyncHistoryQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetEventTemplateSyncHistoryQuery(eventId, query.Page, query.PageSize),
            cancellationToken);

        return Ok(response);
    }
}
