using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.InstanceAdmin.Plans;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/admin/instance")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class InstanceAdminController : EventControllerBase
{
    private readonly IQueryHandler<GetInstanceOverviewQuery, InstanceOverviewDto> _overviewQuery;
    private readonly IQueryHandler<GetInstanceDomainsQuery, InstanceDomainOverviewDto> _domainsQuery;
    private readonly IQueryHandler<GetInstanceOperationsQuery, InstanceOperationsDto> _operationsQuery;
    private readonly IQueryHandler<GetInstanceTenantListQuery, IReadOnlyList<InstanceTenantListItemDto>> _tenantsQuery;
    private readonly IResourceAssembler<InstanceOverviewDto, InstanceOverviewDto> _overviewAssembler;
    private readonly IResourceAssembler<InstanceDomainOverviewDto, InstanceDomainOverviewDto> _domainAssembler;
    private readonly IResourceAssembler<InstanceOperationsDto, InstanceOperationsDto> _operationsAssembler;
    private readonly IResourceAssembler<InstanceTenantDetailDto, InstanceTenantListItemDto> _tenantAssembler;
    private readonly IResourceAssembler<InstanceTenantPlanDetailDto, InstanceTenantPlanListItemDto> _tenantPlanAssembler;
    private readonly IResourceAssembler<InstanceTenantEffectiveConfigurationDto, InstanceTenantEffectiveConfigurationDto> _tenantEffectiveConfigurationAssembler;

    public InstanceAdminController(
        IQueryHandler<GetInstanceOverviewQuery, InstanceOverviewDto> overviewQuery,
        IQueryHandler<GetInstanceDomainsQuery, InstanceDomainOverviewDto> domainsQuery,
        IQueryHandler<GetInstanceOperationsQuery, InstanceOperationsDto> operationsQuery,
        IQueryHandler<GetInstanceTenantListQuery, IReadOnlyList<InstanceTenantListItemDto>> tenantsQuery,
        IResourceAssembler<InstanceOverviewDto, InstanceOverviewDto> overviewAssembler,
        IResourceAssembler<InstanceDomainOverviewDto, InstanceDomainOverviewDto> domainAssembler,
        IResourceAssembler<InstanceOperationsDto, InstanceOperationsDto> operationsAssembler,
        IResourceAssembler<InstanceTenantDetailDto, InstanceTenantListItemDto> tenantAssembler,
        IResourceAssembler<InstanceTenantPlanDetailDto, InstanceTenantPlanListItemDto> tenantPlanAssembler,
        IResourceAssembler<InstanceTenantEffectiveConfigurationDto, InstanceTenantEffectiveConfigurationDto> tenantEffectiveConfigurationAssembler)
    {
        _overviewQuery = overviewQuery;
        _domainsQuery = domainsQuery;
        _operationsQuery = operationsQuery;
        _tenantsQuery = tenantsQuery;
        _overviewAssembler = overviewAssembler;
        _domainAssembler = domainAssembler;
        _operationsAssembler = operationsAssembler;
        _tenantAssembler = tenantAssembler;
        _tenantPlanAssembler = tenantPlanAssembler;
        _tenantEffectiveConfigurationAssembler = tenantEffectiveConfigurationAssembler;
    }

    [HttpGet("overview", Name = RouteNames.GetInstanceAdminOverview)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Overview")]
    [EndpointDescription("Returns the instance administration overview for single-tenant or multi-tenant administrators.")]
    [ProducesResponseType(typeof(HalResource<InstanceOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceOverviewDto>>> GetOverview(
        CancellationToken cancellationToken = default)
    {
        var overview = await _overviewQuery.QueryAsync(new GetInstanceOverviewQuery(), cancellationToken);
        var resource = await _overviewAssembler.ToResource(overview, HttpContext);

        return Ok(resource);
    }

    [HttpGet("domains", Name = RouteNames.GetInstanceAdminDomains)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Domains")]
    [EndpointDescription("Returns multi-tenant instance administration domain and DNS guidance for instance administrators.")]
    [ProducesResponseType(typeof(HalResource<InstanceDomainOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceDomainOverviewDto>>> GetDomains(
        CancellationToken cancellationToken = default)
    {
        var domains = await _domainsQuery.QueryAsync(new GetInstanceDomainsQuery(), cancellationToken);
        var resource = await _domainAssembler.ToResource(domains, HttpContext);

        return Ok(resource);
    }

    [HttpGet("operations", Name = RouteNames.GetInstanceAdminOperations)]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Operations")]
    [EndpointDescription("Returns instance administration operational status for jobs, outbox, email dispatch, moderation reporting, and storage.")]
    [ProducesResponseType(typeof(HalResource<InstanceOperationsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalResource<InstanceOperationsDto>>> GetOperations(
        CancellationToken cancellationToken = default)
    {
        var operations = await _operationsQuery.QueryAsync(new GetInstanceOperationsQuery(), cancellationToken);
        var resource = await _operationsAssembler.ToResource(operations, HttpContext);

        return Ok(resource);
    }

    [HttpGet("tenants", Name = RouteNames.GetInstanceAdminTenants)]
    [RequireMultiTenant]
    [EnableRateLimiting(RateLimitingExtensions.ControlPlanePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.ControlPlanePolicy)]
    [EndpointSummary("Get Instance Administration Tenants")]
    [EndpointDescription("Returns the multi-tenant instance administration tenant lifecycle list for instance administrators.")]
    [ProducesResponseType(typeof(HalCollectionResource<InstanceTenantListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<InstanceTenantListItemDto>>> GetTenants(
        CancellationToken cancellationToken = default)
    {
        var tenants = await _tenantsQuery.QueryAsync(new GetInstanceTenantListQuery(), cancellationToken);
        var resource = await _tenantAssembler.ToCollectionResource(tenants, RouteNames.GetInstanceAdminTenants, HttpContext);

        return Ok(resource);
    }


























}

public sealed record PublishTenantPlanVersionRequest(TenantPlanExistingAssignmentPolicy ExistingTenantPolicy);

public sealed record CloneTenantPlanRequest(string Key, string Name);

public sealed record PreviewTenantPlanDiffRequest(TenantPlanEffectiveConfiguration Current, TenantPlanDraft Draft);

public sealed record SwitchTenantPlanAssignmentRequest(Guid TenantPlanVersionId);
public sealed record SetInstanceTenantSettingRequest(string Value);
