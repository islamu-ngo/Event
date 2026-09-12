using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/tenants/{tenantId:guid}/events/{eventId:guid}/registration-providers")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
[Tags("RegistrationProviderManagement")]
public sealed class RegistrationProviderConnectionsController(
    IMediator mediator,
    IResourceAssembler<RegistrationProviderConnectionDto, RegistrationProviderConnectionDto> connectionAssembler)
    : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor ProviderManagementValidationProblem = new(
        "registrationProviderManagement",
        "Registration provider management request failed",
        "The registration provider management request was invalid.");

    [HttpGet("connections", Name = RouteNames.GetRegistrationProviderConnections)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalCollectionResource<RegistrationProviderConnectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HalCollectionResource<RegistrationProviderConnectionDto>>> GetConnections(Guid tenantId, Guid eventId, CancellationToken cancellationToken = default) =>
        Ok(await connectionAssembler.ToCollectionResource(await mediator.Send(new GetRegistrationProviderConnectionsQuery(tenantId, eventId), cancellationToken), RouteNames.GetRegistrationProviderConnections, new RegistrationProviderEventCollectionContext(tenantId, eventId), HttpContext));

    [HttpGet("connections/{connectionId:guid}", Name = RouteNames.GetRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [RequestTimeout(RequestTimeoutExtensions.LookupPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(HalResource<RegistrationProviderConnectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationProviderConnectionDto>>> GetConnection(Guid tenantId, Guid eventId, Guid connectionId, CancellationToken cancellationToken = default) =>
        await mediator.Send(new GetRegistrationProviderConnectionQuery(tenantId, eventId, connectionId), cancellationToken) is { } result ? Ok(await connectionAssembler.ToResource(result, HttpContext)) : NotFound();

    [HttpPost("connections", Name = RouteNames.CreateRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> CreateConnection(Guid tenantId, Guid eventId, [FromBody] RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpsertRegistrationProviderConnectionCommand(tenantId, eventId, null, request), cancellationToken));

    [HttpPut("connections/{connectionId:guid}", Name = RouteNames.UpdateRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateConnection(Guid tenantId, Guid eventId, Guid connectionId, [FromBody] RegistrationProviderConnectionRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new UpsertRegistrationProviderConnectionCommand(tenantId, eventId, connectionId, request), cancellationToken));

    [HttpDelete("connections/{connectionId:guid}", Name = RouteNames.DeleteRegistrationProviderConnection)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> DeleteConnection(Guid tenantId, Guid eventId, Guid connectionId, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new DeleteRegistrationProviderConnectionCommand(tenantId, eventId, connectionId), cancellationToken));

    [HttpPut("connections/{connectionId:guid}/approved-origins", Name = RouteNames.ReplaceRegistrationProviderApprovedOrigins)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [PrivateNoStore]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ReplaceApprovedOrigins(Guid tenantId, Guid eventId, Guid connectionId, [FromBody] ReplaceRegistrationProviderApprovedOriginsRequestDto request, CancellationToken cancellationToken = default) =>
        ToActionResult(await mediator.Send(new ReplaceRegistrationProviderApprovedOriginsCommand(tenantId, eventId, connectionId, request.Origins), cancellationToken));

    private ActionResult<BaseCommandResponse<Guid>> ToActionResult(BaseCommandResponse<Guid> result) => result.IsSuccess
        ? Ok(result)
        : this.ToCommandValidationProblem(result, ProviderManagementValidationProblem);
}
