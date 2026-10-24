using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[ApiController]
[Route("api/User/{id:guid}")]
[EndpointClassification(EndpointClass.Authenticated)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[OutputCache(NoStore = true)]
[ProducesResponseType<ProblemDetails>(401)]
[ProducesResponseType<ProblemDetails>(403)]
[ProducesResponseType<ProblemDetails>(409)]
[ProducesResponseType<ProblemDetails>(503)]
public sealed class UserProfileCompletionController(
    IQueryHandler<ReadProfileCollectionOptionsRequest, IReadOnlyList<ModerationProfileCollectionOption>> options,
    IQueryHandler<ReadPrivateBirthDateStatusRequest, PrivateBirthDateStatus> status,
    ICommandHandler<RecordPrivateBirthDateCommand, BaseCommandResponse<Guid>> record,
    ICommandHandler<ClearPrivateBirthDateCommand, BaseCommandResponse<Guid>> clear) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("profile-completion", Name = RouteNames.GetUserProfileCompletion)]
    [ProducesResponseType<HalResource<PrivateProfileCompletionDto>>(200)]
    public async Task<ActionResult<HalResource<PrivateProfileCompletionDto>>> Get(Guid id, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();
        var birth = await status.QueryAsync(new(id), cancellationToken);
        var current = await options.QueryAsync(new(id), cancellationToken);
        var resource = new HalResource<PrivateProfileCompletionDto>(new(current, birth))
            .WithSelfLink(Link(RouteNames.GetUserProfileCompletion, id));
        // Readiness is not a permit: both actions reacquire before their body is consumed.
        if (current.Count != 0)
        {
            resource.WithLink("edit", HalLink.CreateAction(Link(RouteNames.UpdateCurrentUser, id), HttpMethods.Patch));
            resource.WithLink("record-birth-date", HalLink.CreateAction(Link(RouteNames.RecordPrivateBirthDate, id), HttpMethods.Put));
        }
        if (birth.HasDeclaration)
            resource.WithLink("clear-birth-date", HalLink.CreateAction(Link(RouteNames.ClearPrivateBirthDate, id), HttpMethods.Delete));
        return Ok(resource);
    }

    [Authorize]
    [EnableRateLimiting("write")]
    [ProfileCollectionAdmission]
    [SuppressIdempotencyResponseStorage]
    [RequestSizeLimit(4096)]
    [HttpPut("private-birth-date", Name = RouteNames.RecordPrivateBirthDate)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(200)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> RecordBirthDate(Guid id, [FromBody] RecordPrivateBirthDateDto declaration,
        [FromHeader(Name = ProfileCollectionAdmissionAttribute.HeaderName)] string? profileCollection,
        CancellationToken cancellationToken) => Ok(await record.ExecuteAsync(
            new(id, declaration.BirthDate, declaration.ExpectedRevision), cancellationToken));

    [Authorize]
    [EnableRateLimiting("write")]
    [SuppressIdempotencyResponseStorage]
    [HttpDelete("private-birth-date", Name = RouteNames.ClearPrivateBirthDate)]
    [ProducesResponseType<BaseCommandResponse<Guid>>(200)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ClearBirthDate(Guid id, [FromQuery] long expectedRevision,
        CancellationToken cancellationToken) => Ok(await clear.ExecuteAsync(new(id, expectedRevision), cancellationToken));

    private string Link(string route, Guid id) => Url.Link(route, new { id })
        ?? throw new InvalidOperationException("The private profile route is unavailable.");
}
