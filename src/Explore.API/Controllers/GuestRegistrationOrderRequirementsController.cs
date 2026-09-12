using Asp.Versioning;
using Explore.API.Attributes;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Hateoas;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
[Tags("GuestRegistrationOrder")]
public sealed class GuestRegistrationOrderRequirementsController(
    IMediator mediator) : RegistrationOrderControllerBase
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay(AttemptCapabilityHeader, "Cache-Control", "Location")]
    [HttpPost("guest/{orderId:guid}/attempts", Name = RouteNames.LaunchGuestNativeRegistrationAttempt)]
    [EndpointSummary("Launch guest native registration attempt")]
    [ProducesResponseType(typeof(HalResource<NativeRegistrationAttemptDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<NativeRegistrationAttemptDto>>> LaunchGuestNativeAttempt(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] LaunchNativeRegistrationAttemptRequest request,
        CancellationToken cancellationToken = default) => await LaunchNativeAttempt(
        await mediator.Send(new LaunchGuestNativeRegistrationAttemptCommand(
            eventId, orderId, capability, request.RequirementId, request.ChannelId,
            request.FormId, request.FormVersionId, request.BindingId, request.SupersededAttemptId), cancellationToken),
        eventId,
        orderId,
        guest: true);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}/requirement-progress", Name = RouteNames.GetGuestNativeRegistrationRequirementProgress)]
    [EndpointSummary("Get guest native registration requirement progress")]
    [ProducesResponseType(typeof(HalResource<NativeRegistrationRequirementProgressCollectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<NativeRegistrationRequirementProgressCollectionDto>>> GetGuestNativeRequirementProgress(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default) => ToNativeProgressResource(
        await mediator.Send(new GetGuestNativeRegistrationRequirementProgressQuery(
            eventId, orderId, capability), cancellationToken), eventId, orderId, guest: true);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/provider-attempts", Name = RouteNames.LaunchGuestRegistrationProviderAttempt)]
    [EndpointSummary("Launch guest registration provider attempt")]
    [ProducesResponseType(typeof(HalResource<NativeRegistrationProviderLaunchDescriptorDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<NativeRegistrationProviderLaunchDescriptorDto>>> LaunchGuestProviderAttempt(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] LaunchRegistrationProviderAttemptRequest request,
        CancellationToken cancellationToken = default) => LaunchProviderAttempt(
        await mediator.Send(new LaunchGuestRegistrationProviderAttemptCommand(
            eventId, orderId, capability, request.RequirementId, request.ChannelId,
            request.BindingId, request.FormId, request.FormVersionId, request.SupersededAttemptId), cancellationToken),
        eventId,
        orderId,
        guest: true);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/attempts/{attemptId:guid}/skip", Name = RouteNames.SkipGuestNativeRegistrationRequirement)]
    [EndpointSummary("Skip guest optional native registration requirement")]
    [ProducesResponseType(typeof(NativeRegistrationSkipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NativeRegistrationSkipDto>> SkipGuestNativeRequirement(
        Guid eventId,
        Guid orderId,
        Guid attemptId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = AttemptCapabilityHeader)] string? attemptCapability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] SkipNativeRegistrationRequirementRequest request,
        CancellationToken cancellationToken = default) => MapNativeSkip(await mediator.Send(
        new SkipGuestNativeRegistrationRequirementCommand(
            eventId, orderId, capability, request.RequirementId, attemptId, attemptCapability), cancellationToken));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/attempts/{attemptId:guid}/submissions", Name = RouteNames.SubmitGuestNativeRegistrationAttempt)]
    [EndpointSummary("Submit guest native registration answers")]
    [ProducesResponseType(typeof(NativeRegistrationSubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NativeRegistrationSubmissionDto>> SubmitGuestNativeAttempt(
        Guid eventId,
        Guid orderId,
        Guid attemptId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = AttemptCapabilityHeader)] string? attemptCapability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] SubmitNativeRegistrationAttemptRequest request,
        CancellationToken cancellationToken = default) => MapNativeSubmission(await mediator.Send(
        new SubmitGuestNativeRegistrationAttemptCommand(
            eventId, orderId, capability, request.RequirementId, attemptId, attemptCapability,
            idempotencyKey, MapNativeAnswers(request.Answers)), cancellationToken));
}
