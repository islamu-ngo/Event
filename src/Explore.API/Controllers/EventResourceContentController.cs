using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Operations;
using Explore.Application.DTOs.EventResource;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Features.EventResources;
using Explore.Application.Features.EventResources.Requests.Commands;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Explore.API.Controllers;

[ApiController]
[ApiVersion("0.1")]
[Route("api/eventresource")]
[Tags("EventResources")]
[EndpointClassification(EndpointClass.Public)]
[PrivateNoStore]
[RevalidateIdempotencyReplay]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
public sealed class EventResourceContentController(
    ICommandHandler<CreateEventResourceUploadSessionCommand, BaseCommandResponse<StorageUploadSessionDto>> upload,
    IQueryHandler<GetEventResourceContentQuery, EventResourceAuthorityResult> content,
    IQueryHandler<CompleteEventResourceContentQuery, EventResourceHeaderResult> completion,
    IOptions<RequestTimeoutOptions> timeouts,
    TimeProvider clock) : ControllerBase
{
    private static readonly CommandFailurePolicy UploadFailures = CommandFailurePolicy
        .ValidatedBy(new("eventResourceUpload", "Resource upload is invalid", "The resource upload intent is invalid."))
        .NotFound(new("Resource not found", "The requested resource was not found."), FailureCodes.NotFound)
        .AuthenticationRequired(FailureCodes.AuthenticationRequired)
        .Forbidden("Resource authority required", "Current resource upload authority is required.",
            "event_resource_forbidden", "resource_upload_policy_denied", "privacy_erasure_fenced")
        .Conflict("Resource upload conflict", "Refresh the resource before retrying.", "event_resource_upload_conflict")
        .Unavailable("Resource upload unavailable", "Resource upload authority could not be established.",
            "event_resource_unavailable");

    [HttpPost("{id:guid}/upload-sessions", Name = RouteNames.CreateEventResourceUploadSession)]
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [RequireIdempotencyKey]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [ProducesResponseType(typeof(BaseCommandResponse<StorageUploadSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BaseCommandResponse<StorageUploadSessionDto>>> CreateUploadSession(
        Guid id, [FromBody] CreateEventResourceUploadSessionDto dto, CancellationToken cancellationToken)
    {
        var result = await upload.ExecuteAsync(new(id, dto), cancellationToken);
        return result.FailureCode == FailureCodes.QuotaExceeded
            ? this.ToStorageUploadProblem(result)
            : UploadFailures.Map(this, result, () => Ok(result));
    }

    [HttpGet("{id:guid}/content", Name = RouteNames.GetEventResourceContent)]
    [AllowAnonymous]
    [RequestTimeout(RequestTimeoutExtensions.DefaultPolicy)]
    [ProducesResponseType(typeof(byte[]), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var timeout = timeouts.Value.Policies[RequestTimeoutExtensions.DefaultPolicy].Timeout
            ?? throw new InvalidOperationException("The resource content request timeout must be bounded.");
        var result = await content.QueryAsync(new(id, clock.GetUtcNow().Add(timeout)), cancellationToken);
        Response.RegisterForDisposeAsync(result);
        return new EventResourceFileResult(result, completion, ReadFailure);
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
