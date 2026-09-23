using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Queries;
using Explore.Application.Services;
using Explore.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>Protected native protocol for announced resource-policy writers and instance deployment bindings.</summary>
[ApiController]
[ApiVersion("0.1")]
[Route("api/event-resource-provider-activation")]
[Authorize]
[EndpointClassification(EndpointClass.Admin)]
[PrivateNoStore]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EventResourceProviderActivationController(
    IQueryHandler<GetEventResourceProviderBindingsQuery, EventResourceProviderBindingDocument> read,
    ICommandHandler<BindEventResourceProviderCommand, EventResourceProviderOperation> bind,
    ICommandHandler<BeginEventResourceProviderOperationCommand, EventResourceProviderOperation> begin,
    ICommandHandler<ActivateEventResourceProviderCommand, bool> activate) : ControllerBase
{
    [InstanceManagement]
    [HttpGet("bindings", Name = RouteNames.GetEventResourceProviderBindings)]
    [Authorize]
    [ProducesResponseType(typeof(EventResourceProviderBindingDocument), StatusCodes.Status200OK)]
    public async Task<ActionResult<EventResourceProviderBindingDocument>> GetBindings(CancellationToken cancellationToken) =>
        Ok(await read.QueryAsync(new(), cancellationToken));

    [InstanceManagement]
    [HttpPut("bindings", Name = RouteNames.BindEventResourceProvider)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(EventResourceProviderOperation), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventResourceProviderOperation>> Bind(
        [FromBody] BindEventResourceProviderCommand command, CancellationToken cancellationToken) =>
        Ok(await bind.ExecuteAsync(command, cancellationToken));

    [InstanceManagement]
    [HttpPost("begin", Name = RouteNames.BeginEventResourceProviderOperation)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(typeof(EventResourceProviderOperation), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<EventResourceProviderOperation>> Begin(
        [FromBody] BeginEventResourceProviderOperationCommand command, CancellationToken cancellationToken) =>
        Ok(await begin.ExecuteAsync(command, cancellationToken));

    [InstanceManagement]
    [HttpPost("activate", Name = RouteNames.ActivateEventResourceProvider)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [SuppressIdempotencyResponseStorage]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate([FromBody] ActivateEventResourceProviderCommand command, CancellationToken cancellationToken) =>
        await activate.ExecuteAsync(command, cancellationToken)
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status409Conflict, title: "Resource provider activation rejected");
}
