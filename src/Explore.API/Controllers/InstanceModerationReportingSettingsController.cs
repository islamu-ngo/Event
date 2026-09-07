namespace Explore.API.Controllers;

using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Hateoas;
using Explore.Application.Authentication;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiVersion("0.1")]
[Route("api/instance/settings/moderation-reporting")]
[ApiController]
[Authorize]
[EndpointClassification(EndpointClass.Authenticated)]
[Produces(HateoasConstants.JsonMediaType, HateoasConstants.HalJsonMediaType)]
public sealed class InstanceModerationReportingSettingsController(IMediator mediator) : EventControllerBase
{
    private static readonly ApiValidationProblemDescriptor UpdateLocksValidationProblem = new(
        "moderationReportingProviderLocks",
        "Moderation reporting provider lock validation failed",
        "Moderation reporting provider lock update failed.");

    [HttpPatch("locks", Name = RouteNames.UpdateInstanceModerationReportingProviderLocks)]
    [EndpointSummary("Update Instance Moderation Reporting Provider Locks")]
    [EndpointDescription("Updates instance governance locks that control tenant Osprey and Coop reporting provider overrides.")]
    [Consumes("application/json")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> UpdateLocks(
        [FromBody] UpdateReportingProviderLocksDto locks,
        CancellationToken cancellationToken = default)
    {
        Guid? userId = await mediator.ResolveCurrentUserIdAsync(User, cancellationToken);
        if (!userId.HasValue)
        {
            return this.ToAuthenticationRequiredProblem(
                detail: "The authenticated principal could not be resolved to an application user.");
        }

        var response = await mediator.Send(
            new UpdateReportingProviderLocksCommand(userId.Value, locks),
            cancellationToken);

        if (!response.IsSuccess)
        {
            if (response.FailureCode == FailureCodes.AdminRequired)
            {
                return this.ToForbiddenProblem(
                    detail: response.Message ?? "Moderation reporting provider locks can only be updated by instance administrators.");
            }

            return this.ToCommandValidationProblem(response, UpdateLocksValidationProblem);
        }

        return Ok(response);
    }
}
