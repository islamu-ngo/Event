using System.Collections.Generic;
using System.Threading.Tasks;
using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.DTOs.UserAuthenticationToken;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Commands;
using Explore.Application.Features.UserAuthenticationTokens.Requests.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/[controller]")]
[ApiController]
[EndpointClassification(EndpointClass.Authenticated)]
public class UserAuthenticationTokenController(IMediator mediator) : ControllerBase
{
    private static readonly ApiNotFoundProblemDescriptor UserAuthenticationTokenNotFoundProblem = new(
        "User authentication token not found",
        "User authentication token not found.");

    // GET: api/userauthenticationtoken
    [HttpGet(Name = RouteNames.GetUserAuthenticationTokens)]
    [EndpointSummary("Get all User Authentication Tokens")]
    [EndpointDescription("Retrieve the current user's authentication token sessions")]
    [Authorize]
    [ProducesResponseType(typeof(List<UserAuthenticationTokenListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<List<UserAuthenticationTokenListDto>>> GetAll(CancellationToken cancellationToken = default)
    {
        var tokens = await mediator.Send(new GetUserAuthenticationTokenListRequest(), cancellationToken);
        return Ok(tokens);
    }

    // GET: api/userauthenticationtoken/{id}
    [HttpGet("{id}", Name = RouteNames.GetUserAuthenticationTokenById)]
    [EndpointSummary("Get User Authentication Token by ID")]
    [EndpointDescription("Retrieve details for one of the current user's authentication token sessions")]
    [Authorize]
    [ProducesResponseType(typeof(UserAuthenticationTokenDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<UserAuthenticationTokenDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var token = await mediator.Send(new GetUserAuthenticationTokenDetailsRequest { Id = id }, cancellationToken);

        return token is null ? this.ToNotFoundProblem(UserAuthenticationTokenNotFoundProblem) : Ok(token);
    }

    // DELETE: api/userauthenticationtoken/{id}
    [HttpDelete("{id}", Name = RouteNames.DeleteUserAuthenticationToken)]
    [EndpointSummary("Delete User Authentication Token")]
    [EndpointDescription("Idempotently revoke one of the current user's authentication sessions")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var command = new DeleteUserAuthenticationTokenCommand { Id = id };
        await mediator.Send(command, cancellationToken);

        return NoContent();
    }
}
