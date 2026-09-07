using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Features.Tenants.Requests.Commands.CreateTenantNavLink;
using Explore.Application.Features.Tenants.Requests.Commands.DeleteTenantNavLink;
using Explore.Application.Features.Tenants.Requests.Commands.ReorderTenantNavLinks;
using Explore.Application.Features.Tenants.Requests.Commands.UpdateTenantNavLink;
using Explore.Application.Features.Tenants.Requests.Queries;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
public class TenantController : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor CreateValidationProblem = new(
        "tenant",
        "Tenant validation failed",
        "Tenant creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateValidationProblem = new(
        "tenant",
        "Tenant validation failed",
        "Tenant update failed.");

    private static readonly ApiValidationProblemDescriptor CreateNavigationValidationProblem = new(
        "tenantNavigationLink",
        "Tenant navigation link validation failed",
        "Tenant navigation link creation failed.");

    private static readonly ApiValidationProblemDescriptor UpdateNavigationValidationProblem = new(
        "tenantNavigationLink",
        "Tenant navigation link validation failed",
        "Tenant navigation link update failed.");

    private static readonly ApiValidationProblemDescriptor ReorderNavigationValidationProblem = new(
        "tenantNavigationLink",
        "Tenant navigation link validation failed",
        "Tenant navigation link reorder failed.");

    private static readonly ApiNotFoundProblemDescriptor TenantNotFoundProblem = new(
        "Tenant not found",
        "Tenant not found.");

    private static readonly ApiNotFoundProblemDescriptor NavigationNotFoundProblem = new(
        "Tenant navigation link not found",
        "Tenant navigation link not found.");

    private readonly IMediator _mediator;
    private readonly ITenantContext _tenantContext;

    public TenantController(IMediator mediator, ITenantContext tenantContext)
    {
        _mediator = mediator;
        _tenantContext = tenantContext;
    }

    // GET: api/tenant
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet(Name = RouteNames.GetTenants)]
    [EndpointSummary("Get all Tenants")]
    [EndpointDescription("Retrieve a list of all tenants")]
    [ProducesResponseType(typeof(List<TenantListDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "ListData")]
    public async Task<ActionResult<List<TenantListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var tenants = await _mediator.Send(new GetTenantListRequest(), cancellationToken);
        return Ok(tenants);
    }

    // GET: api/tenant/count
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("count", Name = RouteNames.GetActiveTenantCount)]
    [EndpointSummary("Get Active Tenant Count")]
    [EndpointDescription("Returns the number of active tenants. Used by deployment mode toggle safeguards.")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> GetCount(CancellationToken cancellationToken = default)
    {
        var count = await _mediator.Send(new GetActiveTenantCountQuery(), cancellationToken);
        return Ok(count);
    }

    // GET: api/tenant/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpGet("{id:guid}", Name = RouteNames.GetTenantById)]
    [EndpointSummary("Get Tenant by ID")]
    [EndpointDescription("Retrieve details of a specific tenant")]
    [ProducesResponseType(typeof(TenantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [OutputCache(PolicyName = "DetailData")]
    public async Task<ActionResult<TenantDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var tenant = await _mediator.Send(new GetTenantDetailsRequest { Id = id }, cancellationToken);

        return Ok(tenant);
    }

    // POST: api/tenant
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost(Name = RouteNames.CreateTenant)]
    [EndpointSummary("Create new Tenant")]
    [EndpointDescription("Create a new tenant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Create([FromBody] CreateTenantDto dto, CancellationToken cancellationToken = default)
    {
        var command = new CreateTenantCommand
        {
            TenantDto = dto,
            RequestingUserId = CurrentUserId
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateValidationProblem);
        }

        return Ok(response);
    }


    // PATCH: api/tenant/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("{id:guid}", Name = RouteNames.UpdateTenant)]
    [EndpointSummary("Update Tenant")]
    [EndpointDescription("Partially update tenant metadata. Lifecycle transitions use dedicated control-plane actions.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(Guid id, [FromBody] UpdateTenantDto dto, CancellationToken cancellationToken = default)
    {
        var command = new UpdateTenantCommand { TenantId = id, Update = dto };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(TenantNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateValidationProblem);
        }

        return Ok(response);
    }

    // DELETE: api/tenant/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("{id:guid}", Name = RouteNames.DeleteTenant)]
    [EndpointSummary("Delete Tenant")]
    [EndpointDescription("Delete a tenant")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteTenantCommand { Id = id };
        await _mediator.Send(command, cancellationToken);

        return NoContent();
    }

    // GET: api/tenant/navigation
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [HttpGet("navigation", Name = RouteNames.GetTenantNavigationLinks)]
    [EndpointSummary("Get Tenant Navigation Links")]
    [EndpointDescription("Retrieve all navigation links for the current tenant")]
    [ProducesResponseType(typeof(List<TenantNavigationLinkDto>), StatusCodes.Status200OK)]
    [OutputCache(PolicyName = "TenantNav")]
    public async Task<ActionResult<List<TenantNavigationLinkDto>>> GetNavigation(CancellationToken cancellationToken = default)
    {
        var links = await _mediator.Send(new GetTenantNavLinksQuery(), cancellationToken);
        return Ok(links);
    }

    // POST: api/tenant/navigation
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPost("navigation", Name = RouteNames.CreateTenantNavigationLink)]
    [EndpointSummary("Create Tenant Navigation Link")]
    [EndpointDescription("Create a new navigation link for the tenant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateNavigation(
        [FromBody] CreateTenantNavigationLinkDto dto,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var command = new CreateTenantNavLinkCommand { NavigationLinkDto = dto };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, CreateNavigationValidationProblem);
        }

        // Invalidate cache
        await cacheStore.EvictByTagAsync("tenant-nav", cancellationToken);

        return Ok(response);
    }

    // PATCH: api/tenant/navigation/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPatch("navigation/{id:guid}", Name = RouteNames.UpdateTenantNavigationLink)]
    [EndpointSummary("Update Tenant Navigation Link")]
    [EndpointDescription("Partially update an existing navigation link; reorder remains a separate action.")]
    [ProducesResponseType(typeof(BaseCommandResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<bool>>> UpdateNavigation(
        Guid id,
        [FromBody] UpdateTenantNavigationLinkDto dto,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var command = new UpdateTenantNavLinkCommand
        {
            NavigationLinkId = id,
            TenantId = _tenantContext.TenantId,
            Update = dto
        };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return response.FailureCode == FailureCodes.NotFound
                ? this.ToNotFoundProblem(NavigationNotFoundProblem)
                : this.ToCommandValidationProblem(response, UpdateNavigationValidationProblem);
        }

        // Invalidate cache
        await cacheStore.EvictByTagAsync("tenant-nav", cancellationToken);

        return Ok(response);
    }

    // DELETE: api/tenant/navigation/{id}
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpDelete("navigation/{id:guid}", Name = RouteNames.DeleteTenantNavigationLink)]
    [EndpointSummary("Delete Tenant Navigation Link")]
    [EndpointDescription("Delete a navigation link")]
    [ProducesResponseType(typeof(BaseCommandResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<bool>>> DeleteNavigation(
        Guid id,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var command = new DeleteTenantNavLinkCommand { Id = id };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToNotFoundProblem(NavigationNotFoundProblem);
        }

        // Invalidate cache
        await cacheStore.EvictByTagAsync("tenant-nav", cancellationToken);

        return Ok(response);
    }

    // PUT: api/tenant/navigation/reorder
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [HttpPut("navigation/reorder", Name = RouteNames.ReorderTenantNavigationLinks)]
    [EndpointSummary("Reorder Tenant Navigation Links")]
    [EndpointDescription("Reorder multiple navigation links")]
    [ProducesResponseType(typeof(BaseCommandResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BaseCommandResponse<bool>>> ReorderNavigation(
        [FromBody] List<UpdateTenantNavigationLinkOrderDto> orders,
        [FromServices] IOutputCacheStore cacheStore,
        CancellationToken cancellationToken = default)
    {
        var command = new ReorderTenantNavLinksCommand { NavigationLinkOrders = orders };
        var response = await _mediator.Send(command, cancellationToken);

        if (!response.IsSuccess)
        {
            return this.ToCommandValidationProblem(response, ReorderNavigationValidationProblem);
        }

        // Invalidate cache
        await cacheStore.EvictByTagAsync("tenant-nav", cancellationToken);

        return Ok(response);
    }
}
