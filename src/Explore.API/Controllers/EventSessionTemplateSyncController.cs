using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Resources;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.EventSessionTemplateSync;
using Explore.Application.Features.EventSessionTemplateSync.Commands.ApplyEventSessionTemplateSync;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateDiff;
using Explore.Application.Features.EventSessionTemplateSync.Queries.GetEventSessionTemplateSyncHistory;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/event-sessions")]
[ApiController]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
public sealed class EventSessionTemplateSyncController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor DiffValidationProblem = new(
        "eventSessionTemplateSync",
        "Event session template sync validation failed",
        "Event session template diff computation failed.");

    private readonly IMediator _mediator;
    private readonly IHateoasAuthorizationEvaluator _authorizationEvaluator;
    private readonly IHateoasLinkGenerator _linkGenerator;
    private readonly ILinkPolicy<EventSessionTemplateSyncResource> _syncLinkPolicy;
    private readonly ITenantContext _tenantContext;

    public EventSessionTemplateSyncController(
        IMediator mediator,
        IHateoasAuthorizationEvaluator authorizationEvaluator,
        IHateoasLinkGenerator linkGenerator,
        ILinkPolicy<EventSessionTemplateSyncResource> syncLinkPolicy,
        ITenantContext tenantContext)
    {
        _mediator = mediator;
        _authorizationEvaluator = authorizationEvaluator;
        _linkGenerator = linkGenerator;
        _syncLinkPolicy = syncLinkPolicy;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Compute the event-session-template diff for a requested target template version.
    /// </summary>
    [HttpGet("{sessionId:guid}/template-sync/diff", Name = RouteNames.GetEventSessionTemplateSyncDiff)]
    [ProducesResponseType(typeof(HalResource<TemplateDiffDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<TemplateDiffDto>>> GetDiff(
        Guid sessionId,
        [FromQuery(Name = "templateVersion")] int templateVersion,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetEventSessionTemplateDiffQuery(sessionId, templateVersion),
            cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, DiffValidationProblem);
        }

        if (response.Id is null)
        {
            return this.ToValidationProblem(DiffValidationProblem, "Event session template diff was not returned.");
        }

        var diff = response.Id;
        var hasChanges =
            diff.AddedDefinitions.Count > 0 ||
            diff.ModifiedDefinitions.Count > 0 ||
            diff.RetiredDefinitions.Count > 0 ||
            diff.AddedOptions.Count > 0 ||
            diff.ModifiedOptions.Count > 0 ||
            diff.RetiredOptions.Count > 0;

        var resource = new EventSessionTemplateSyncResource(_tenantContext.TenantId, sessionId, diff.TargetTemplateVersion, hasChanges);
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
    /// Apply an operator-selected event-session-template sync plan.
    /// </summary>
    [HttpPost("{sessionId:guid}/template-sync/apply", Name = RouteNames.ApplyEventSessionTemplateSync)]
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
        Guid sessionId,
        [FromBody] EventSessionTemplateSyncApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new ApplyEventSessionTemplateSyncCommand(sessionId, request.Plan, request.BaseProvenanceVersion),
            cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Get paged event-session-template sync audit history.
    /// </summary>
    [HttpGet("{sessionId:guid}/template-sync/history", Name = RouteNames.GetEventSessionTemplateSyncHistory)]
    [ProducesResponseType(typeof(PaginatedResult<EventSessionTemplateSyncHistoryItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PaginatedResult<EventSessionTemplateSyncHistoryItemDto>>> GetHistory(
        Guid sessionId,
        [FromQuery] TemplateSyncHistoryQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetEventSessionTemplateSyncHistoryQuery(sessionId, query.Page, query.PageSize),
            cancellationToken);

        return Ok(response);
    }
}
