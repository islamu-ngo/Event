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

[ApiVersion("0.1")]
[Route("api/admin/control-plane")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class ControlPlaneController : EventControllerBase
{
    private readonly IMediator _mediator;
    private readonly IResourceAssembler<ControlPlaneOverviewDto, ControlPlaneOverviewDto> _overviewAssembler;
    private readonly IResourceAssembler<ControlPlaneDomainOverviewDto, ControlPlaneDomainOverviewDto> _domainAssembler;
    private readonly IResourceAssembler<ControlPlaneOperationsDto, ControlPlaneOperationsDto> _operationsAssembler;
    private readonly IResourceAssembler<ControlPlaneTenantDetailDto, ControlPlaneTenantListItemDto> _tenantAssembler;
    private readonly IResourceAssembler<ControlPlaneTenantPlanDetailDto, ControlPlaneTenantPlanListItemDto> _tenantPlanAssembler;
    private readonly IResourceAssembler<ControlPlaneTenantEffectiveConfigurationDto, ControlPlaneTenantEffectiveConfigurationDto> _tenantEffectiveConfigurationAssembler;

    public ControlPlaneController(
        IMediator mediator,
        IResourceAssembler<ControlPlaneOverviewDto, ControlPlaneOverviewDto> overviewAssembler,
        IResourceAssembler<ControlPlaneDomainOverviewDto, ControlPlaneDomainOverviewDto> domainAssembler,
        IResourceAssembler<ControlPlaneOperationsDto, ControlPlaneOperationsDto> operationsAssembler,
        IResourceAssembler<ControlPlaneTenantDetailDto, ControlPlaneTenantListItemDto> tenantAssembler,
        IResourceAssembler<ControlPlaneTenantPlanDetailDto, ControlPlaneTenantPlanListItemDto> tenantPlanAssembler,
        IResourceAssembler<ControlPlaneTenantEffectiveConfigurationDto, ControlPlaneTenantEffectiveConfigurationDto> tenantEffectiveConfigurationAssembler)
    {
        _mediator = mediator;
        _overviewAssembler = overviewAssembler;
        _domainAssembler = domainAssembler;
        _operationsAssembler = operationsAssembler;
        _tenantAssembler = tenantAssembler;
        _tenantPlanAssembler = tenantPlanAssembler;
        _tenantEffectiveConfigurationAssembler = tenantEffectiveConfigurationAssembler;
    }

    [HttpGet("overview", Name = RouteNames.GetControlPlaneOverview)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Overview")]
    [EndpointDescription("Returns the instance control-plane overview for single-tenant or multi-tenant administrators.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ControlPlaneOverviewDto>>> GetOverview(
        CancellationToken cancellationToken = default)
    {
        var overview = await _mediator.Send(new GetControlPlaneOverviewQuery(), cancellationToken);
        var resource = await _overviewAssembler.ToResource(overview, HttpContext);

        return Ok(resource);
    }

    [HttpGet("domains", Name = RouteNames.GetControlPlaneDomains)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Domains")]
    [EndpointDescription("Returns multi-tenant control-plane domain and DNS guidance for instance administrators.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneDomainOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ControlPlaneDomainOverviewDto>>> GetDomains(
        CancellationToken cancellationToken = default)
    {
        var domains = await _mediator.Send(new GetControlPlaneDomainsQuery(), cancellationToken);
        var resource = await _domainAssembler.ToResource(domains, HttpContext);

        return Ok(resource);
    }

    [HttpGet("operations", Name = RouteNames.GetControlPlaneOperations)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Operations")]
    [EndpointDescription("Returns control-plane operational status for jobs, outbox, email dispatch, moderation reporting, and storage.")]
    [ProducesResponseType(typeof(HalResource<ControlPlaneOperationsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<ControlPlaneOperationsDto>>> GetOperations(
        CancellationToken cancellationToken = default)
    {
        var operations = await _mediator.Send(new GetControlPlaneOperationsQuery(), cancellationToken);
        var resource = await _operationsAssembler.ToResource(operations, HttpContext);

        return Ok(resource);
    }

    [HttpGet("tenants", Name = RouteNames.GetControlPlaneTenants)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Control Plane Tenants")]
    [EndpointDescription("Returns the multi-tenant control-plane tenant lifecycle list for instance administrators.")]
    [ProducesResponseType(typeof(HalCollectionResource<ControlPlaneTenantListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<ControlPlaneTenantListItemDto>>> GetTenants(
        CancellationToken cancellationToken = default)
    {
        var tenants = await _mediator.Send(new GetControlPlaneTenantListQuery(), cancellationToken);
        var resource = await _tenantAssembler.ToCollectionResource(tenants, RouteNames.GetControlPlaneTenants, HttpContext);

        return Ok(resource);
    }


























}

public sealed record PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy ExistingTenantPolicy);

public sealed record CloneTenantPlanRequest(string Key, string Name);

public sealed record PreviewTenantPlanDiffRequest(TenantPlanEffectiveConfiguration Current, TenantPlanDraft Draft);

public sealed record SwitchTenantPlanAssignmentRequest(Guid TenantPlanVersionId);
public sealed record SetControlPlaneTenantSettingRequest(string Value);
