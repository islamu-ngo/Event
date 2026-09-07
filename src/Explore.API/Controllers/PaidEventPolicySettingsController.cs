using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Commands;
using Explore.Application.Features.PaidEventPolicies.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/instance/settings/paid-event-policy")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class InstancePaidEventPolicySettingsController(
    IMediator mediator,
    IResourceAssembler<PaidEventPolicyDto, PaidEventPolicyDto> assembler) : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Paid-event policy not found",
        "The active instance paid-event policy was not found.");

    private static readonly ApiValidationProblemDescriptor PolicyValidationProblem = new(
        "paidEventPolicy",
        "Paid-event policy validation failed",
        "Paid-event policy update failed.");

    [HttpGet("", Name = RouteNames.GetInstancePaidEventPolicySettings)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<PaidEventPolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<PaidEventPolicyDto>>> Get(CancellationToken cancellationToken)
    {
        PaidEventPolicyDto? policy = await mediator.Send(new GetInstancePaidEventPolicyQuery(), cancellationToken);
        if (policy is null)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }

        var response = new ObjectResult(await assembler.ToResource(policy, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        response.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return response;
    }

    [HttpPut("", Name = RouteNames.UpdateInstancePaidEventPolicySettings)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        [FromBody] RevisePaidEventPolicyDto policy,
        CancellationToken cancellationToken)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(new ReviseInstancePaidEventPolicyCommand(policy), cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToCommandValidationProblem(response, PolicyValidationProblem);
    }
}

[ApiVersion("0.1")]
[ApiController]
[Route("api/tenants/{tenantId:guid}/settings/paid-event-policy")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class TenantPaidEventPolicySettingsController(
    IMediator mediator,
    IResourceAssembler<TenantPaidEventPolicyConfigurationDto, TenantPaidEventPolicyConfigurationDto> assembler) : EventControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor NotFoundProblem = new(
        "Paid-event policy not found",
        "The active paid-event policy configuration was not found.");

    private static readonly ApiValidationProblemDescriptor TenantPolicyValidationProblem = new(
        "paidEventPolicy",
        "Paid-event policy validation failed",
        "Paid-event policy update failed.");

    [HttpGet("", Name = RouteNames.GetTenantPaidEventPolicySettings)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<TenantPaidEventPolicyConfigurationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<TenantPaidEventPolicyConfigurationDto>>> Get(
        [FromRoute] Guid tenantId,
        CancellationToken cancellationToken)
    {
        TenantPaidEventPolicyConfigurationDto? configuration = await mediator.Send(
            new GetTenantPaidEventPolicyConfigurationQuery(tenantId),
            cancellationToken);
        if (configuration is null)
        {
            return this.ToNotFoundProblem(NotFoundProblem);
        }

        var response = new ObjectResult(await assembler.ToResource(configuration, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        response.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return response;
    }

    [HttpPut("", Name = RouteNames.UpdateTenantPaidEventPolicySettings)]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> Update(
        [FromRoute] Guid tenantId,
        [FromBody] RevisePaidEventPolicyDto policy,
        CancellationToken cancellationToken)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new ReviseTenantPaidEventPolicyCommand(tenantId, policy),
            cancellationToken);
        return response.IsSuccess ? Ok(response) : this.ToCommandValidationProblem(response, TenantPolicyValidationProblem);
    }
}
