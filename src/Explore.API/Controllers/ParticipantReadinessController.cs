using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions;
using Explore.Application.Features.Admissions.Requests.Commands;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route(
    "api/events/{eventId:guid}/participant-readiness/" +
    "registration-orders/{orderId:guid}/participants/" +
    "{participantId:guid}/assignments/{assignmentId:guid}")]
[ApiController]
[EndpointClassification(EndpointClass.Public)]
public sealed class ParticipantReadinessController(
    IMediator mediator,
    IResourceAssembler<
        ParticipantReadinessDto,
        ParticipantReadinessDto> assembler) :
    ControllerBase
{
    private const string CapabilityHeader =
        "X-Registration-Order-Capability";
    private static readonly ApiNotFoundProblemDescriptor
        ReadinessNotFound = new(
            "Participant readiness unavailable",
            "Participant readiness was not found.");
    private static readonly ApiValidationProblemDescriptor
        ReadinessValidation = new(
            "participantReadiness",
            "Participant readiness request failed",
            "Participant readiness request failed.");
    private static readonly CommandFailurePolicy
        ReadinessFailures = CommandFailurePolicy
            .ValidatedBy(ReadinessValidation)
            .NotFound(
                ReadinessNotFound,
                ParticipantAdmissionFailureCodes
                    .ParticipantUnavailable)
            .Forbidden(
                "Participant readiness denied",
                "Participant readiness authority could not be established.",
                ParticipantAdmissionFailureCodes
                    .SubjectAuthorityRequired,
                ParticipantAdmissionFailureCodes
                    .ApprovalUnavailable)
            .Conflict(
                "Participant readiness conflict",
                "Participant readiness could not be changed.",
                ParticipantAdmissionFailureCodes
                    .CompletionEvidenceIncomplete,
                ParticipantAdmissionFailureCodes
                    .ConsentEvidenceRequired,
                ParticipantAdmissionFailureCodes
                    .AdmissionRevoked);

    [AllowAnonymous]
    [PrivateNoStore]
    [HttpGet("", Name = RouteNames.GetParticipantReadiness)]
    [EndpointSummary("Get participant admission readiness")]
    [EndpointDescription(
        "Returns one bounded readiness resource after subject, purchaser, guest-capability, or organizer authority is proven.")]
    [ProducesResponseType(
        typeof(HalResource<ParticipantReadinessDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    public async Task<ActionResult<
        HalResource<ParticipantReadinessDto>>> GetReadiness(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            [FromHeader(Name = CapabilityHeader)]
            string? capabilityToken,
            CancellationToken cancellationToken = default)
    {
        ParticipantReadinessDto? readiness =
            await mediator.Send(
                Query(
                    eventId,
                    orderId,
                    participantId,
                    assignmentId,
                    capabilityToken),
                cancellationToken);
        return readiness is null
            ? this.ToNotFoundProblem(ReadinessNotFound)
            : Ok(await assembler.ToResource(
                readiness,
                HttpContext));
    }

    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost(
        "complete",
        Name = RouteNames.CompleteParticipantReadiness)]
    [EndpointSummary("Complete participant admission readiness")]
    [ProducesResponseType(
        typeof(HalResource<ParticipantReadinessDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<
        HalResource<ParticipantReadinessDto>>> Complete(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken = default) =>
        await MapMutationAsync(
            await mediator.Send(
                new CompleteParticipantAdmissionCommand(
                    eventId,
                    orderId,
                    assignmentId,
                    participantId),
                cancellationToken),
            eventId,
            orderId,
            participantId,
            assignmentId,
            cancellationToken);

    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost(
        "approve",
        Name = RouteNames.ApproveParticipantReadiness)]
    [EndpointSummary("Approve participant admission readiness")]
    [ProducesResponseType(
        typeof(HalResource<ParticipantReadinessDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<
        HalResource<ParticipantReadinessDto>>> Approve(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken = default) =>
        await MapMutationAsync(
            await mediator.Send(
                new ApproveParticipantAdmissionCommand(
                    eventId,
                    orderId,
                    assignmentId,
                    participantId),
                cancellationToken),
            eventId,
            orderId,
            participantId,
            assignmentId,
            cancellationToken);

    [Authorize]
    [PrivateNoStore]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost(
        "revoke",
        Name = RouteNames.RevokeParticipantReadiness)]
    [EndpointSummary("Revoke participant admission readiness")]
    [ProducesResponseType(
        typeof(HalResource<ParticipantReadinessDto>),
        StatusCodes.Status200OK)]
    [ProducesResponseType(
        typeof(ValidationProblemDetails),
        StatusCodes.Status400BadRequest)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status403Forbidden)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status404NotFound)]
    [ProducesResponseType(
        typeof(ProblemDetails),
        StatusCodes.Status409Conflict)]
    public async Task<ActionResult<
        HalResource<ParticipantReadinessDto>>> Revoke(
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken = default) =>
        await MapMutationAsync(
            await mediator.Send(
                new RevokeParticipantAdmissionCommand(
                    eventId,
                    orderId,
                    assignmentId,
                    participantId),
                cancellationToken),
            eventId,
            orderId,
            participantId,
            assignmentId,
            cancellationToken);

    private async Task<ActionResult<
        HalResource<ParticipantReadinessDto>>>
        MapMutationAsync(
            BaseCommandResponse<Guid> response,
            Guid eventId,
            Guid orderId,
            Guid participantId,
            Guid assignmentId,
            CancellationToken cancellationToken)
    {
        if (!response.IsSuccess)
        {
            return ReadinessFailures.Map(this, response);
        }

        ParticipantReadinessDto? readiness =
            await mediator.Send(
                Query(
                    eventId,
                    orderId,
                    participantId,
                    assignmentId,
                    capabilityToken: null),
                cancellationToken);
        return readiness is null
            ? this.ToNotFoundProblem(ReadinessNotFound)
            : Ok(await assembler.ToResource(
                readiness,
                HttpContext));
    }

    private static GetParticipantReadinessQuery Query(
        Guid eventId,
        Guid orderId,
        Guid participantId,
        Guid assignmentId,
        string? capabilityToken) =>
        new(
            eventId,
            orderId,
            participantId,
            assignmentId,
            capabilityToken);
}
