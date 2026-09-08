using Asp.Versioning;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.ExceptionHandling;
using Explore.API.Extensions;
using Explore.API.Filters;
using Explore.API.Hateoas;
using Explore.API.Models;
using Explore.Application.Contracts.Hateoas;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Application.Features.Promotions.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Queries;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Hateoas;
using Explore.Application.Responses;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Authenticated checkout: account-scoped registration for the signed-in caller's current order.
/// </summary>
/// <remarks>
/// Split out of RegistrationOrderController by route capability. The route template and every
/// <c>Name = RouteNames.*</c> are carried over verbatim, so URLs, operationIds, and the generated
/// client are unchanged by the split.
/// </remarks>
[ApiVersion("0.1")]
[Route("api/events/{eventId:guid}/registration-orders")]
[ApiController]
public sealed class AuthenticatedRegistrationOrderController(
    IMediator mediator,
    IResourceAssembler<RegistrationOrderDto, RegistrationOrderDto> assembler) : RegistrationOrderControllerBase
{
    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("", Name = RouteNames.StartAuthenticatedRegistrationOrder)]
    [EndpointSummary("Start authenticated registration order")]
    [EndpointDescription("Creates a registration order owned by the authenticated current account.")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BaseCommandResponse<Guid>>> StartAuthenticated(
        Guid eventId,
        [FromBody] StartRegistrationOrderRequest? request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return this.ToValidationProblem(RegistrationOrderValidationProblem, "A registration-order payload is required.");
        }

        BaseCommandResponse<Guid> response = await mediator.Send(
            new StartAuthenticatedRegistrationOrderCommand(
                eventId,
                request.TicketCatalogVersionId,
                request.BookingPartyType,
                request.Lines,
                request.PlatformContributionBasisPoints),
            cancellationToken);
        return response.IsSuccess
            ? CreatedAtRoute(RouteNames.GetCurrentRegistrationOrder, new { eventId, orderId = response.Id }, response)
            : AuthenticatedStartFailures.Map(this, response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [ProtectIdempotencyReplay(AttemptCapabilityHeader, "Cache-Control", "Location")]
    [HttpPost("{orderId:guid}/attempts", Name = RouteNames.LaunchAuthenticatedNativeRegistrationAttempt)]
    [EndpointSummary("Launch authenticated native registration attempt")]
    [ProducesResponseType(typeof(HalResource<NativeRegistrationAttemptDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<NativeRegistrationAttemptDto>>> LaunchAuthenticatedNativeAttempt(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] LaunchNativeRegistrationAttemptRequest request,
        CancellationToken cancellationToken = default) => await LaunchNativeAttempt(
        await mediator.Send(new LaunchAuthenticatedNativeRegistrationAttemptCommand(
            eventId, orderId, request.RequirementId, request.ChannelId,
            request.FormId, request.FormVersionId, request.BindingId, request.SupersededAttemptId), cancellationToken),
        eventId,
        orderId,
        guest: false);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}/requirement-progress", Name = RouteNames.GetAuthenticatedNativeRegistrationRequirementProgress)]
    [EndpointSummary("Get authenticated native registration requirement progress")]
    [ProducesResponseType(typeof(HalResource<NativeRegistrationRequirementProgressCollectionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<NativeRegistrationRequirementProgressCollectionDto>>> GetAuthenticatedNativeRequirementProgress(
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken = default) => ToNativeProgressResource(
        await mediator.Send(new GetAuthenticatedNativeRegistrationRequirementProgressQuery(
            eventId, orderId), cancellationToken), eventId, orderId, guest: false);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("{orderId:guid}/provider-attempts", Name = RouteNames.LaunchAuthenticatedRegistrationProviderAttempt)]
    [EndpointSummary("Launch authenticated registration provider attempt")]
    [ProducesResponseType(typeof(HalResource<NativeRegistrationProviderLaunchDescriptorDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<NativeRegistrationProviderLaunchDescriptorDto>>> LaunchAuthenticatedProviderAttempt(
        Guid eventId,
        Guid orderId,
        [FromBody] LaunchRegistrationProviderAttemptRequest request,
        CancellationToken cancellationToken = default) => LaunchProviderAttempt(
        await mediator.Send(new LaunchAuthenticatedRegistrationProviderAttemptCommand(
            eventId, orderId, request.RequirementId, request.ChannelId,
            request.BindingId, request.FormId, request.FormVersionId, request.SupersededAttemptId), cancellationToken),
        eventId,
        orderId,
        guest: false);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("{orderId:guid}/attempts/{attemptId:guid}/skip", Name = RouteNames.SkipAuthenticatedNativeRegistrationRequirement)]
    [EndpointSummary("Skip authenticated optional native registration requirement")]
    [ProducesResponseType(typeof(NativeRegistrationSkipDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NativeRegistrationSkipDto>> SkipAuthenticatedNativeRequirement(
        Guid eventId,
        Guid orderId,
        Guid attemptId,
        [FromHeader(Name = AttemptCapabilityHeader)] string? attemptCapability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] SkipNativeRegistrationRequirementRequest request,
        CancellationToken cancellationToken = default) => MapNativeSkip(await mediator.Send(
        new SkipAuthenticatedNativeRegistrationRequirementCommand(
            eventId, orderId, request.RequirementId, attemptId, attemptCapability), cancellationToken));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("{orderId:guid}/attempts/{attemptId:guid}/submissions", Name = RouteNames.SubmitAuthenticatedNativeRegistrationAttempt)]
    [EndpointSummary("Submit authenticated native registration answers")]
    [ProducesResponseType(typeof(NativeRegistrationSubmissionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NativeRegistrationSubmissionDto>> SubmitAuthenticatedNativeAttempt(
        Guid eventId,
        Guid orderId,
        Guid attemptId,
        [FromHeader(Name = AttemptCapabilityHeader)] string? attemptCapability,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] SubmitNativeRegistrationAttemptRequest request,
        CancellationToken cancellationToken = default) => MapNativeSubmission(await mediator.Send(
        new SubmitAuthenticatedNativeRegistrationAttemptCommand(
            eventId, orderId, request.RequirementId, attemptId, attemptCapability,
            idempotencyKey, MapNativeAnswers(request.Answers)), cancellationToken));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}", Name = RouteNames.GetCurrentRegistrationOrder)]
    [EndpointSummary("Get current registration order")]
    [EndpointDescription("Returns a registration order only to its authenticated current-account owner.")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationOrderDto>>> GetCurrent(
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrderDto? response = await mediator.Send(new GetCurrentRegistrationOrderQuery(orderId), cancellationToken);
        if (response is null || response.EventId != eventId)
        {
            return this.ToNotFoundProblem(RegistrationOrderNotFoundProblem);
        }

        return await ToHalResource(response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.AuthenticatedPolicy)]
    [PrivateNoStore]
    [HttpGet("{orderId:guid}/participants", Name = RouteNames.GetAuthenticatedRegistrationOrderParticipants)]
    [EndpointSummary("Get registration participants")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderParticipantsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HalResource<RegistrationOrderParticipantsDto>>> GetAuthenticatedParticipants(
        Guid eventId, Guid orderId, CancellationToken cancellationToken = default)
    {
        RegistrationOrderParticipantsDto? response = await mediator.Send(
            new GetAuthenticatedRegistrationOrderParticipantsQuery(eventId, orderId), cancellationToken);
        return response is null
            ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
            : Ok(ToParticipantsHalResource(response, eventId, guest: false));
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("{orderId:guid}/participants", Name = RouteNames.AddAuthenticatedRegistrationOrderParticipant)]
    [EndpointSummary("Add registration participant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AddAuthenticatedParticipant(
        Guid eventId, Guid orderId, [FromBody] RegistrationParticipantRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAuthenticated(eventId, orderId,
            new AddRegistrationParticipantCommand(orderId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details),
            cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPut("{orderId:guid}/participants/{participantId:guid}", Name = RouteNames.UpdateAuthenticatedRegistrationOrderParticipant)]
    [EndpointSummary("Update registration participant")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> UpdateAuthenticatedParticipant(
        Guid eventId, Guid orderId, Guid participantId, [FromBody] RegistrationParticipantRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAuthenticated(eventId, orderId,
            new UpdateRegistrationParticipantCommand(orderId, participantId, request.ParticipantTypeId, request.GuardianParticipantId, request.Details),
            cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPut("{orderId:guid}/assignments", Name = RouteNames.AssignAuthenticatedRegistrationOrderTickets)]
    [EndpointSummary("Assign registration tickets")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> AssignAuthenticatedTickets(
        Guid eventId, Guid orderId, [FromBody] RegistrationTicketAssignmentsRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAuthenticated(eventId, orderId,
            new BulkAssignRegistrationTicketsCommand(orderId, request.Assignments), cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("{orderId:guid}/assignments/company-csv", Name = RouteNames.ImportAuthenticatedRegistrationOrderCompanyAssignmentsCsv)]
    [EndpointSummary("Import company registration ticket assignments from CSV")]
    [ProducesResponseType(typeof(BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto>>> ImportAuthenticatedCompanyAssignmentsCsv(
        Guid eventId, Guid orderId, [FromBody] RegistrationCompanyAssignmentCsvRequest request,
        CancellationToken cancellationToken = default)
    {
        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> response = await mediator.Send(
            new ImportCompanyRegistrationAssignmentsCsvCommand(eventId, orderId, request.CsvUtf8, request.LineageKey), cancellationToken);
        return response.IsSuccess
            ? Ok(response)
            : response.FailureCode == "registration_order_not_found"
                ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
                : this.ToCommandValidationProblem(response, RegistrationOrderValidationProblem);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPut("{orderId:guid}/assignments/deferred", Name = RouteNames.DeferAuthenticatedRegistrationOrderTickets)]
    [EndpointSummary("Defer registration ticket assignments")]
    [ProducesResponseType(typeof(BaseCommandResponse<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public Task<ActionResult<BaseCommandResponse<Guid>>> DeferAuthenticatedTickets(
        Guid eventId, Guid orderId, [FromBody] RegistrationTicketDeferralsRequest request,
        CancellationToken cancellationToken = default) =>
        MutateAuthenticated(eventId, orderId,
            new BulkDeferRegistrationTicketsCommand(orderId, request.Assignments, request.AssignmentDeadline), cancellationToken);

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpPost("{orderId:guid}/promotion", Name = RouteNames.ApplyAuthenticatedRegistrationOrderPromotion)]
    [EndpointSummary("Apply registration order promotion")]
    [Consumes(HateoasConstants.JsonMediaType)]
    [ProducesResponseType(typeof(PromotionRedemptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionRedemptionResponseDto>> ApplyAuthenticatedPromotion(
        Guid eventId,
        Guid orderId,
        [FromBody] PromotionCodeRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapPromotionRedemption(await mediator.Send(
        new ApplyAuthenticatedPromotionCodeToRegistrationOrderCommand(eventId, orderId, request.Code), cancellationToken));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [RequireIdempotencyKey]
    [HttpDelete("{orderId:guid}/promotion", Name = RouteNames.RemoveAuthenticatedRegistrationOrderPromotion)]
    [EndpointSummary("Remove registration order promotion")]
    [ProducesResponseType(typeof(PromotionRedemptionResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PromotionRedemptionResponseDto>> RemoveAuthenticatedPromotion(
        Guid eventId,
        Guid orderId,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken = default) => MapPromotionRedemption(await mediator.Send(
        new RemoveAuthenticatedPromotionFromRegistrationOrderCommand(eventId, orderId), cancellationToken));

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("{orderId:guid}/continue", Name = RouteNames.ContinueAuthenticatedRegistrationOrder)]
    [EndpointSummary("Continue authenticated registration order")]
    [EndpointDescription("Advances a registration order owned by the authenticated current account.")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationOrderDto>>> ContinueAuthenticated(
        Guid eventId,
        Guid orderId,
        [FromBody] ContinueRegistrationOrderRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrderLifecycleResponseDto response = await mediator.Send(
            new ContinueAuthenticatedRegistrationOrderCommand(
                eventId,
                orderId,
                request?.PlatformContributionBasisPoints),
            cancellationToken);
        return await MapAuthenticatedLifecycle(eventId, response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpPost("{orderId:guid}/finalize", Name = RouteNames.FinalizeAuthenticatedRegistrationOrder)]
    [EndpointSummary("Finalize authenticated registration order")]
    [EndpointDescription("Finalizes a free registration order owned by the authenticated current account.")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationOrderDto>>> FinalizeAuthenticated(
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrderLifecycleResponseDto response = await mediator.Send(
            new FinalizeAuthenticatedRegistrationOrderCommand(eventId, orderId),
            cancellationToken);
        return await MapAuthenticatedLifecycle(eventId, response);
    }

    [Authorize]
    [EndpointClassification(EndpointClass.Authenticated)]
    [EnableRateLimiting(RateLimitingExtensions.WritePolicy)]
    [HttpDelete("{orderId:guid}", Name = RouteNames.CancelAuthenticatedRegistrationOrder)]
    [EndpointSummary("Cancel authenticated registration order")]
    [EndpointDescription("Cancels a registration order owned by the authenticated current account.")]
    [ProducesResponseType(typeof(HalResource<RegistrationOrderDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HalResource<RegistrationOrderDto>>> CancelAuthenticated(
        Guid eventId,
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        RegistrationOrderLifecycleResponseDto response = await mediator.Send(
            new CancelAuthenticatedRegistrationOrderCommand(eventId, orderId),
            cancellationToken);
        return await MapAuthenticatedLifecycle(eventId, response);
    }

    private async Task<ActionResult<BaseCommandResponse<Guid>>> MutateAuthenticated(
        Guid eventId, Guid orderId, IRegistrationParticipantMutation mutation,
        CancellationToken cancellationToken) => MapParticipantMutation(await mediator.Send(
            new MutateAuthenticatedRegistrationParticipantsCommand(eventId, orderId, mutation), cancellationToken));

    private async Task<ActionResult<HalResource<RegistrationOrderDto>>> MapAuthenticatedLifecycle(
        Guid eventId,
        RegistrationOrderLifecycleResponseDto response)
    {
        if (response.IsSuccess && response.Order is { } order && order.EventId == eventId)
        {
            return await ToHalResource(order);
        }

        return response.FailureCode == "registration_order_not_found" || response.Order is null || response.Order.EventId != eventId
            ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
            : this.ToCommandValidationProblem(response, RegistrationOrderValidationProblem);
    }

    private async Task<ObjectResult> ToHalResource(RegistrationOrderDto order)
    {
        var result = new ObjectResult(await assembler.ToResource(order, HttpContext))
        {
            StatusCode = StatusCodes.Status200OK
        };
        result.ContentTypes.Add(HateoasConstants.HalJsonMediaType);
        return result;
    }
}
