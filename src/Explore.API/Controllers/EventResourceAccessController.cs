using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.EventResources;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/eventresource")]
[Tags("EventResources")]
[EndpointClassification(EndpointClass.Public)]
[PrivateNoStore]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EventResourceAccessController(
    IQueryHandler<GetEventResourceAccessQuery, EventResourceAuthorityResult> access,
    IQueryHandler<GetEventResourceAccessHeadersQuery, EventResourceHeaderResult> completion,
    IOptions<RequestTimeoutOptions> timeouts,
    TimeProvider clock) : ControllerBase
{
    [HttpGet("{id:guid}/access", Name = RouteNames.GetEventResourceAccess)]
    [AllowAnonymous]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var timeout = timeouts.Value.Policies[RequestTimeoutExtensions.DefaultPolicy].Timeout
            ?? throw new InvalidOperationException("The resource access request timeout must be bounded.");
        var result = await access.QueryAsync(new(id, clock.GetUtcNow().Add(timeout)), cancellationToken);
        Response.RegisterForDisposeAsync(result);
        return new EventResourceRedirectResult(result, completion, ReadFailure);
    }

    private IActionResult ReadFailure(EventResourceAuthorityOutcome outcome) => outcome switch
    {
        EventResourceAuthorityOutcome.AuthenticationRequired => this.ToAuthenticationRequiredProblem(),
        EventResourceAuthorityOutcome.Forbidden => this.ToForbiddenProblem(),
        EventResourceAuthorityOutcome.NotFound => this.ToNotFoundProblem(
            new("Resource not found", "The requested resource was not found.")),
        _ => this.ToServiceUnavailableProblem("Resource authority unavailable",
            "Current resource authority could not be established.", EventResourceManagementFailureCodes.Unavailable)
    };
}
