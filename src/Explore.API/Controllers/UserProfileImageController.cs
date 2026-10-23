using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/User")]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class UserProfileImageController(ICommandHandler<UpdateUserProfileImageCommand, BaseCommandResponse<Guid>> update)
    : EventControllerBase
{
    [HttpPatch("{id:guid}/profile-image", Name = RouteNames.UpdateCurrentUserProfileImage)]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(16 * 1024)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(409)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateImage(Guid id, [FromBody] UpdateUserProfileImageDto image,
        [FromHeader(Name = "If-Match")] string? ifMatch, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(ifMatch?.Trim().Trim('"'), out var stamp) || stamp == Guid.Empty)
            return this.ToValidationProblem(new("user", "Profile image validation failed", "Profile image update failed."),
                "If-Match must contain the current user concurrency stamp.");
        var response = await update.ExecuteAsync(new() { UserId = id, ExpectedConcurrencyStamp = stamp, ProfileImage = image }, cancellationToken);
        if (!response.IsSuccess) return this.ToCommandValidationProblem(response,
            new("user", "Profile image validation failed", "Profile image update failed."));
        return Ok(response);
    }
}
