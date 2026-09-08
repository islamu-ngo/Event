using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Commands;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Control-plane tenant plan authoring: drafts, versions, publication, cloning, validation, and diffs.
/// </summary>
/// <remarks>
/// Split out of ControlPlaneController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/admin/control-plane")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ControlPlaneTenantPlanController : EventControllerBase
{
    private readonly IMediator _mediator;
    private readonly IResourceAssembler<ControlPlaneTenantPlanDetailDto, ControlPlaneTenantPlanListItemDto> _tenantPlanAssembler;

    public ControlPlaneTenantPlanController(
        IMediator mediator,
        IResourceAssembler<ControlPlaneTenantPlanDetailDto, ControlPlaneTenantPlanListItemDto> tenantPlanAssembler)
    {
        _mediator = mediator;
        _tenantPlanAssembler = tenantPlanAssembler;
    }

    [HttpGet("plans", Name = RouteNames.GetControlPlaneTenantPlans)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Tenant Plans")]
    [EndpointDescription("Returns SaaS tenant plan templates for instance administrators.")]
    [ProducesResponseType(typeof(HalCollectionResource<ControlPlaneTenantPlanListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<ControlPlaneTenantPlanListItemDto>>> GetTenantPlans(
        CancellationToken cancellationToken = default)
    {
        var plans = await _mediator.Send(new GetControlPlaneTenantPlanListQuery(), cancellationToken);
        var resource = await _tenantPlanAssembler.ToCollectionResource(plans, RouteNames.GetControlPlaneTenantPlans, HttpContext);

        return Ok(resource);
    }

    [HttpGet("plans/{key}", Name = RouteNames.GetControlPlaneTenantPlanByKey)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Tenant Plan")]
    [EndpointDescription("Returns one SaaS tenant plan template with version, setting, and quota metadata.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneTenantPlanDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<ControlPlaneTenantPlanDetailDto>>> GetTenantPlanByKey(
        string key,
        CancellationToken cancellationToken = default)
    {
        var plan = await _mediator.Send(new GetControlPlaneTenantPlanDetailQuery(key), cancellationToken);
        if (plan is null)
        {
            return NotFound();
        }

        var resource = await _tenantPlanAssembler.ToResource(plan, HttpContext);

        return Ok(resource);
    }

    [HttpPost("plans", Name = RouteNames.CreateControlPlaneTenantPlanDraft)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Create Control Plane Tenant Plan Draft")]
    [EndpointDescription("Creates a draft SaaS tenant plan template for instance administrators.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateTenantPlanDraft(
        [FromBody] TenantPlanDraft draft,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CreateControlPlaneTenantPlanDraftCommand(draft), cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("plans/{key}/versions", Name = RouteNames.CreateControlPlaneTenantPlanVersionDraft)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Create Control Plane Tenant Plan Version Draft")]
    [EndpointDescription("Creates a draft version for an existing SaaS tenant plan template.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateTenantPlanVersionDraft(
        string key,
        [FromBody] TenantPlanDraft draft,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new CreateControlPlaneTenantPlanVersionDraftCommand(key, draft), cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPatch("plans/versions/{versionId:guid}", Name = RouteNames.UpdateControlPlaneTenantPlanVersionDraft)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Update Control Plane Tenant Plan Version Draft")]
    [EndpointDescription("Partially updates draft SaaS tenant plan version groups before publication.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateTenantPlanVersionDraft(
        Guid versionId,
        [FromBody] PatchControlPlaneTenantPlanVersionDraftDto update,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new UpdateControlPlaneTenantPlanVersionDraftCommand(versionId, update),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("plans/versions/{versionId:guid}/publish", Name = RouteNames.PublishControlPlaneTenantPlanVersion)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Publish Control Plane Tenant Plan Version")]
    [EndpointDescription("Publishes a draft tenant plan version with an explicit existing-tenant assignment policy.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> PublishTenantPlanVersion(
        Guid versionId,
        [FromBody] PublishTenantPlanVersionRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new PublishControlPlaneTenantPlanVersionCommand(versionId, request.ExistingTenantPolicy),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("plans/versions/{versionId:guid}/archive", Name = RouteNames.ArchiveControlPlaneTenantPlanVersion)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Archive Control Plane Tenant Plan Version")]
    [EndpointDescription("Archives a tenant plan version that should no longer be used for provisioning.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ArchiveTenantPlanVersion(
        Guid versionId,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new ArchiveControlPlaneTenantPlanVersionCommand(versionId), cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("plans/versions/{sourceVersionId:guid}/clone", Name = RouteNames.CloneControlPlaneTenantPlan)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Clone Control Plane Tenant Plan")]
    [EndpointDescription("Clones an existing tenant plan version into a new draft plan template.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CloneTenantPlan(
        Guid sourceVersionId,
        [FromBody] CloneTenantPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new CloneControlPlaneTenantPlanCommand(sourceVersionId, request.Key, request.Name),
            cancellationToken);

        return this.MapCommandResponse(response);
    }

    [HttpPost("plans/validate", Name = RouteNames.ValidateControlPlaneTenantPlanDraft)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Validate Control Plane Tenant Plan Draft")]
    [EndpointDescription("Validates a tenant plan draft against registered settings, sensitive values, and quota rules.")]
    [ProducesResponseType(typeof(TenantPlanValidationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TenantPlanValidationResult>> ValidateTenantPlanDraft(
        [FromBody] TenantPlanDraft draft,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new ValidateControlPlaneTenantPlanDraftQuery(draft), cancellationToken);

        return Ok(result);
    }

    [HttpPost("plans/preview-diff", Name = RouteNames.PreviewControlPlaneTenantPlanDiff)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Preview Control Plane Tenant Plan Diff")]
    [EndpointDescription("Previews setting changes between a tenant's effective configuration and a tenant plan draft.")]
    [ProducesResponseType(typeof(TenantPlanDiffResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TenantPlanDiffResult>> PreviewTenantPlanDiff(
        [FromBody] PreviewTenantPlanDiffRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new PreviewControlPlaneTenantPlanDiffQuery(request.Current, request.Draft),
            cancellationToken);

        return Ok(result);
    }
}
