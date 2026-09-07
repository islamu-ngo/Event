using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenant-user-role-grants")]
[ApiController]
[Authorize]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public class TenantUserRoleGrantController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "tenantUserRoleGrant",
        "Tenant user role grant validation failed",
        "Tenant user role grant creation failed.");

    private static readonly ApiNotFoundProblemDescriptor TenantUserRoleGrantNotFoundProblem = new(
        "Tenant user role grant not found",
        "Tenant user role grant not found.");

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;
    private readonly IResourceAssembler<TenantUserRoleGrantDto, TenantUserRoleGrantListDto> _resourceAssembler;

    public TenantUserRoleGrantController(
        IMediator mediator,
        ITenantContext tenantContext,
        IResourceAssembler<TenantUserRoleGrantDto, TenantUserRoleGrantListDto> resourceAssembler)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
        _resourceAssembler = resourceAssembler;
    }

    [HttpGet(Name = RouteNames.GetTenantUserRoleGrants)]
    [EndpointSummary("Get all tenant user role grants")]
    [EndpointDescription("Retrieve tenant-scoped user role grants")]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType(typeof(HalCollectionResource<TenantUserRoleGrantListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<TenantUserRoleGrantListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var tenantUserRoleGrants = await _mediator.Send(
            new GetTenantUserRoleGrantListRequest { TenantId = _tenantContext.TenantId },
            cancellationToken);
        var halResource = await _resourceAssembler.ToCollectionResource(
            tenantUserRoleGrants,
            RouteNames.GetTenantUserRoleGrants,
            null,
            HttpContext);

        return Ok(halResource);
    }

    [HttpGet("{id:guid}", Name = RouteNames.GetTenantUserRoleGrantById)]
    [EndpointSummary("Get tenant user role grant by ID")]
    [EndpointDescription("Retrieve details of a tenant user role grant")]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType(typeof(HalResource<TenantUserRoleGrantDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<TenantUserRoleGrantDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var tenantUserRoleGrant = await _mediator.Send(
            new GetTenantUserRoleGrantDetailsRequest { Id = id, TenantId = _tenantContext.TenantId },
            cancellationToken);
        if (tenantUserRoleGrant is null)
        {
            return this.ToNotFoundProblem(TenantUserRoleGrantNotFoundProblem);
        }

        var halResource = await _resourceAssembler.ToResource(tenantUserRoleGrant, HttpContext);
        return Ok(halResource);
    }

    [HttpPost(Name = RouteNames.CreateTenantUserRoleGrant)]
    [EndpointSummary("Create tenant user role grant")]
    [EndpointDescription("Grant a tenant-scoped role to an existing tenant-local user")]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateTenantUserRoleGrantDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateTenantUserRoleGrantCommand
        {
            TenantUserRoleGrantDto = dto,
            TenantId = _tenantContext.TenantId
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return Ok(response);
    }

    [HttpDelete("{id:guid}", Name = RouteNames.RevokeTenantUserRoleGrant)]
    [EndpointSummary("Revoke tenant user role grant")]
    [EndpointDescription("Revoke an active tenant user role grant")]
    [EndpointClassification(EndpointClass.Authenticated)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Revoke(Guid id, CancellationToken cancellationToken = default)
    {
        var revoked = await _mediator.Send(
            new RevokeTenantUserRoleGrantCommand { Id = id, TenantId = _tenantContext.TenantId },
            cancellationToken);
        if (!revoked)
        {
            return this.ToNotFoundProblem(TenantUserRoleGrantNotFoundProblem);
        }

        return NoContent();
    }
}
