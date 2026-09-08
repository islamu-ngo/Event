// ABOUTME: Guest registration checkout endpoints driven by a capability token rather than an account.
// ABOUTME: The capability header is the only authority here, so every action re-checks it through the command.

using Asp.Versioning;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.API.Middleware;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using Explore.Application.Services.Registration;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Guest checkout: capability-token registration for callers without an account, including later claim.
/// </summary>
/// <remarks>
/// Split out of RegistrationOrderController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
public sealed class GuestRegistrationOrderController(
    IMediator mediator,
    TimeProvider timeProvider) : RegistrationOrderControllerBase
{
    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.AnonymousRegistrationPolicy)]
    [RequireIdempotencyKey]
    [RequireAnonymousRegistrationChallenge]
    [PrivateNoStore]
    [ProtectIdempotencyReplay(CapabilityHeader, "Cache-Control", "Location")]
    [HttpPost("guest", Name = RouteNames.StartGuestRegistrationOrder)]
    [EndpointSummary("Start guest registration order")]
    [EndpointDescription("Creates an anonymous registration order and reveals its opaque recovery capability once in a response header.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(GuestRegistrationOrderStartDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GuestRegistrationOrderStartDto>> StartGuest(
        Guid eventId,
        [FromBody] StartRegistrationOrderRequest? request,
        CancellationToken cancellationToken = default,
        [FromHeader(Name = AnonymousRegistrationChallengeBoundary.ChallengeHeader)] string? challenge = null,
        [FromHeader(Name = AnonymousRegistrationChallengeBoundary.ProofHeader)] string? proof = null)
    {
        if (request is null)
        {
            return this.ToValidationProblem(RegistrationOrderValidationProblem, "A registration-order payload is required.");
        }

        GuestRegistrationOrderStartDto response = await mediator.Send(
            new StartGuestRegistrationOrderCommand(
                eventId,
                request.TicketCatalogVersionId,
                request.BookingPartyType,
                request.Lines,
                request.PlatformContributionBasisPoints)
            {
                ChallengeAuthority = AnonymousRegistrationChallengeBoundary.GetAuthority(HttpContext)
            },
            cancellationToken);

        if (!response.IsSuccess)
        {
            return GuestStartFailures.Map(this, response);
        }

        Response.Headers[CapabilityHeader] = response.GuestCapabilityToken;
        Response.Headers.CacheControl = "private, no-store";
        return CreatedAtRoute(
            RouteNames.GetGuestRegistrationOrder,
            new { eventId, orderId = response.Id },
            response);
    }

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

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}", Name = RouteNames.GetGuestRegistrationOrder)]
    [EndpointSummary("Get guest registration order")]
    [EndpointDescription("Returns a registration order only when the route event, order, and opaque capability header match.")]
    [ProducesResponseType(typeof(HalResource<GuestRegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<GuestRegistrationOrderDto>>> GetGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default)
    {
        GuestRegistrationOrderDto? response = await mediator.Send(
            new GetGuestRegistrationOrderQuery(eventId, orderId, capability),
            cancellationToken);
        return response is null
            ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
            : Ok(GuestRegistrationOrderHalResourceFactory.Create(response, Url, timeProvider));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.Public)]
    [PrivateNoStore]
    [HttpGet("guest/{orderId:guid}/participants", Name = RouteNames.GetGuestRegistrationOrderParticipants)]
    [EndpointSummary("Get guest registration participants")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderParticipantsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationOrderParticipantsDto>>> GetGuestParticipants(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrderParticipantsDto? response = await mediator.Send(
            new GetGuestRegistrationOrderParticipantsQuery(eventId, orderId, capability), cancellationToken);
        return response is null
            ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
            : Ok(ToParticipantsHalResource(response, eventId, guest: true));
    }

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/participants", Name = RouteNames.AddGuestRegistrationOrderParticipant)]
    [EndpointSummary("Add guest registration participant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddGuestParticipant(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationParticipantRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new AddRegistrationParticipantCommand(orderId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details),
            cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPut("guest/{orderId:guid}/participants/{participantId:guid}", Name = RouteNames.UpdateGuestRegistrationOrderParticipant)]
    [EndpointSummary("Update guest registration participant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateGuestParticipant(
        Guid eventId, Guid orderId, Guid participantId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationParticipantRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new UpdateRegistrationParticipantCommand(orderId, participantId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details),
            cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPut("guest/{orderId:guid}/assignments", Name = RouteNames.AssignGuestRegistrationOrderTickets)]
    [EndpointSummary("Assign guest registration tickets")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AssignGuestTickets(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationTicketAssignmentsRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new BulkAssignRegistrationTicketsCommand(orderId, request.Assignments), cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPut("guest/{orderId:guid}/assignments/deferred", Name = RouteNames.DeferGuestRegistrationOrderTickets)]
    [EndpointSummary("Defer guest registration ticket assignments")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeferGuestTickets(
        Guid eventId, Guid orderId, [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] RegistrationTicketDeferralsRequest request, CancellationToken cancellationToken = default) =>
        MutateGuest(eventId, orderId, capability,
            new BulkDeferRegistrationTicketsCommand(orderId, request.Assignments, request.AssignmentDeadline), cancellationToken);

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control")]
    [HttpPost("guest/{orderId:guid}/promotion", Name = RouteNames.ApplyGuestRegistrationOrderPromotion)]
    [EndpointSummary("Apply guest registration order promotion")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionRedemptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PromotionRedemptionResponseDto>> ApplyGuestPromotion(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] PromotionCodeRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapPromotionRedemption(await mediator.Send(
        new ApplyGuestPromotionCodeToRegistrationOrderCommand(eventId, orderId, capability, request.Code), cancellationToken));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay("Cache-Control")]
    [HttpDelete("guest/{orderId:guid}/promotion", Name = RouteNames.RemoveGuestRegistrationOrderPromotion)]
    [EndpointSummary("Remove guest registration order promotion")]
    [ProducesResponseType(typeof(PromotionRedemptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PromotionRedemptionResponseDto>> RemoveGuestPromotion(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapPromotionRedemption(await mediator.Send(
        new RemoveGuestPromotionFromRegistrationOrderCommand(eventId, orderId, capability), cancellationToken));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/continue", Name = RouteNames.ContinueGuestRegistrationOrder)]
    [EndpointSummary("Continue guest registration order")]
    [EndpointDescription("Advances the guest order only when its opaque capability header matches the scoped route.")]
    [ProducesResponseType(typeof(GuestRegistrationOrderLifecycleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GuestRegistrationOrderLifecycleResponseDto>> ContinueGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        [FromBody] ContinueRegistrationOrderRequest? request = null,
        CancellationToken cancellationToken = default) =>
        MapGuestLifecycle(await mediator.Send(
            new ContinueGuestRegistrationOrderCommand(
                eventId,
                orderId,
                capability,
                request?.PlatformContributionBasisPoints),
            cancellationToken));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/finalize", Name = RouteNames.FinalizeGuestRegistrationOrder)]
    [EndpointSummary("Finalize guest registration order")]
    [EndpointDescription("Finalizes a free guest registration order only when its opaque capability header matches the scoped route.")]
    [ProducesResponseType(typeof(GuestRegistrationOrderLifecycleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<GuestRegistrationOrderLifecycleResponseDto>> FinalizeGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default) =>
        MapGuestLifecycle(await mediator.Send(
            new FinalizeGuestRegistrationOrderCommand(eventId, orderId, capability),
            cancellationToken));

    [AllowAnonymous]
    [EndpointClassification(EndpointClass.PublicTransactional)]
    [EnableRateLimiting(RateLimitingExtensions.PublicTransactionalPolicy)]
    [RequireIdempotencyKey]
    [HttpDelete("guest/{orderId:guid}", Name = RouteNames.CancelGuestRegistrationOrder)]
    [EndpointSummary("Cancel guest registration order")]
    [EndpointDescription("Cancels a guest registration order only when its opaque capability header matches the scoped route.")]
    [ProducesResponseType(typeof(GuestRegistrationOrderLifecycleResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<GuestRegistrationOrderLifecycleResponseDto>> CancelGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default) =>
        MapGuestLifecycle(await mediator.Send(
            new CancelGuestRegistrationOrderCommand(eventId, orderId, capability),
            cancellationToken));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("guest/{orderId:guid}/claim", Name = RouteNames.ClaimGuestRegistrationOrder)]
    [EndpointSummary("Claim guest registration order")]
    [EndpointDescription("Links a guest registration order to the authenticated current account only when the guest capability and verified account email match.")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> ClaimGuest(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = CapabilityHeader)] string? capability,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<Guid> response = await mediator.Send(
            new ClaimGuestRegistrationOrderCommand(eventId, orderId, capability), cancellationToken);
        return response.IsSuccess
            ? Ok(response)
            : response.FailureCode switch
            {
                "registration_order_authentication_required" => this.ToAuthenticationRequiredProblem(),
                "registration_order_not_found" => this.ToNotFoundProblem(RegistrationOrderNotFoundProblem),
                "registration_order_already_linked" => Conflict(response),
                _ => this.ToCommandValidationProblem(response, RegistrationOrderValidationProblem)
            };
    }

    private async Task<ActionResult<BaseCommandResponse<Guid>>> MutateGuest(
        Guid eventId, Guid orderId, string? capability, IRegistrationParticipantMutation mutation,
        CancellationToken cancellationToken) => MapParticipantMutation(await mediator.Send(
            new MutateGuestRegistrationParticipantsCommand(eventId, orderId, capability, mutation), cancellationToken));

    /// <summary>A missing order graph is not-found even without the code: the response cannot describe the resource.</summary>
    private ActionResult<GuestRegistrationOrderLifecycleResponseDto> MapGuestLifecycle(
        GuestRegistrationOrderLifecycleResponseDto response) =>
        response.IsSuccess
            ? Ok(response)
            : response.Order is null
                ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
                : OrderLifecycleFailures.Map(this, response);


}
