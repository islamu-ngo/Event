using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Authentication;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Atproto.Requests.Commands;
using Explore.Application.Features.Authentication.Atproto.Requests.Queries;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("api/auth/atproto/transient")]
[Authorize(AuthenticationSchemes = AtprotoTransientAuthenticationDefaults.Scheme)]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[EnableRateLimiting(AtprotoTransientAuthenticationDefaults.RatePolicy)]
[RequestTimeout(AtprotoTransientAuthenticationDefaults.Scheme)]
[RequestSizeLimit(AtprotoTransientAuthenticationDefaults.MaximumBodyBytes)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public sealed class AtprotoTransientStoreController(
    ICommandHandler<ProbeAtprotoTransientCommand, BaseCommandResponse<Guid>> probeHandler,
    ICommandHandler<CreateAtprotoTransientCommand, AtprotoTransientCommandResult> createHandler,
    IQueryHandler<ReadAtprotoTransientQuery, AtprotoTransientValue?> readHandler,
    ICommandHandler<ConsumeAtprotoTransientCommand, AtprotoTransientCommandResult> consumeHandler) : ControllerBase
{
    [SuppressIdempotencyResponseStorage]
    [HttpPost("probe", Name = RouteNames.ProbeAtprotoTransient)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Probe(CancellationToken cancellationToken)
    {
        var result = await probeHandler.ExecuteAsync(new ProbeAtprotoTransientCommand(), cancellationToken);
        return Failures.Map(this, result, () => NoContent());
    }

    private static readonly ApiNotFoundProblemDescriptor Missing = new("Not Found", "Transient record not found.");
    private static readonly CommandFailurePolicy Failures = CommandFailurePolicy
        .ValidatedBy(new("request", "Invalid transient request", "Invalid transient request."))
        .NotFound(Missing, FailureCodes.NotFound)
        .Conflict("Conflict", "Transient record already exists.", FailureCodes.ConcurrencyConflict);

    [SuppressIdempotencyResponseStorage]
    [HttpPost("create", Name = RouteNames.CreateAtprotoTransient)]
    [ProducesResponseType<AtprotoTransientResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AtprotoTransientResponse>> Create([FromBody] CreateAtprotoTransientRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createHandler.ExecuteAsync(request.ToCommand(), cancellationToken);
        return Failures.Map(this, result, () => Ok(AtprotoTransientResponse.From(result.Value!)));
    }

    [SuppressIdempotencyResponseStorage]
    [HttpPost("read", Name = RouteNames.ReadAtprotoTransient)]
    [ProducesResponseType<AtprotoTransientResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AtprotoTransientResponse>> Read([FromBody] ReadAtprotoTransientRequest request,
        CancellationToken cancellationToken)
    {
        var result = await readHandler.QueryAsync(request.ToQuery(), cancellationToken);
        return result is null ? this.ToNotFoundProblem(Missing) : Ok(AtprotoTransientResponse.From(result));
    }

    [SuppressIdempotencyResponseStorage]
    [HttpPost("consume", Name = RouteNames.ConsumeAtprotoTransient)]
    [ProducesResponseType<AtprotoTransientResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AtprotoTransientResponse>> Consume([FromBody] ConsumeAtprotoTransientRequest request,
        CancellationToken cancellationToken)
    {
        var result = await consumeHandler.ExecuteAsync(request.ToCommand(), cancellationToken);
        return Failures.Map(this, result, () => Ok(AtprotoTransientResponse.From(result.Value!)));
    }
}
