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
using Explore.Domain;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Explore.API.Controllers;

/// <summary>
/// Guest and authenticated checkout are two doors into the same registration protocol: the same native
/// attempt lifecycle, the same requirement progress shape, the same participant mutations, and the same
/// not-found semantics. Only the identity of the caller and the capability token differ.
/// <para>
/// Holding that protocol here is what lets the two doors be separate controllers without the checkout
/// contract drifting between them — a divergence that would show up as one path accepting a registration
/// the other rejects.
/// </para>
/// </summary>
public abstract class RegistrationOrderControllerBase : ControllerBase
{
    protected const string CapabilityHeader = "X-Registration-Order-Capability";
    protected const string AttemptCapabilityHeader = "X-Registration-Attempt-Capability";
    protected const string IdempotencyKeyHeader = "Idempotency-Key";

    private protected static readonly ApiValidationProblemDescriptor RegistrationOrderValidationProblem = new(
        "registrationOrder",
        "Registration order request failed",
        "Registration order request failed.");

    private protected static readonly ApiNotFoundProblemDescriptor RegistrationOrderNotFoundProblem = new(
        "Registration order not found",
        "Registration order was not found.");

    /// <summary>Shared base: every registration-order command reports a missing order the same way.</summary>
    private protected static readonly CommandFailurePolicy OrderLifecycleFailures = CommandFailurePolicy
        .ValidatedBy(RegistrationOrderValidationProblem)
        .NotFound(RegistrationOrderNotFoundProblem, "registration_order_not_found");

    /// <summary>Guest checkout additionally rejects flows that turn out to need a real account.</summary>
    private protected static readonly CommandFailurePolicy GuestStartFailures = OrderLifecycleFailures
        .AuthenticationRequired(
            "Authentication required",
            "An authenticated account is required to start this registration.",
            "registration_order_identity_required");

    private protected static readonly CommandFailurePolicy AuthenticatedStartFailures = OrderLifecycleFailures
        .AuthenticationRequired("registration_order_authentication_required");

    protected Task<ActionResult<HalResource<NativeRegistrationAttemptDto>>> LaunchNativeAttempt(
        NativeRegistrationAttemptResult response,
        Guid eventId,
        Guid orderId,
        bool guest)
    {
        if (!response.Success || response.Form is null || string.IsNullOrWhiteSpace(response.AttemptCapabilityToken))
        {
            return Task.FromResult<ActionResult<HalResource<NativeRegistrationAttemptDto>>>(
                this.ToNotFoundProblem(RegistrationOrderNotFoundProblem));
        }

        Response.Headers[AttemptCapabilityHeader] = response.AttemptCapabilityToken;
        Response.Headers.CacheControl = "private, no-store";
        var dto = new NativeRegistrationAttemptDto(
            response.AttemptId, response.RequirementId, response.ChannelId, response.FormId, response.FormVersionId,
            response.ExpiresAt, response.AttemptCapabilityToken, response.Form, response.Subjects, response.Progress!);
        string routeName = guest
            ? RouteNames.SubmitGuestNativeRegistrationAttempt
            : RouteNames.SubmitAuthenticatedNativeRegistrationAttempt;
        string progressRouteName = guest
            ? RouteNames.GetGuestNativeRegistrationRequirementProgress
            : RouteNames.GetAuthenticatedNativeRegistrationRequirementProgress;
        var resource = new HalResource<NativeRegistrationAttemptDto>(dto)
            .WithLink(LinkRelations.Submit, HalLink.CreateAction(
                Url.Link(routeName, new { eventId, orderId, attemptId = response.AttemptId })!, HttpMethods.Post))
            .WithLink(LinkRelations.RequirementProgress, HalLink.Create(
                Url.Link(progressRouteName, new { eventId, orderId })!));
        if (response.CanSkip)
        {
            string skipRouteName = guest
                ? RouteNames.SkipGuestNativeRegistrationRequirement
                : RouteNames.SkipAuthenticatedNativeRegistrationRequirement;
            resource.WithLink(LinkRelations.Skip, HalLink.CreateAction(
                Url.Link(skipRouteName, new { eventId, orderId, attemptId = response.AttemptId })!, HttpMethods.Post));
        }

        return Task.FromResult<ActionResult<HalResource<NativeRegistrationAttemptDto>>>(
            CreatedAtRoute(routeName, new { eventId, orderId, attemptId = response.AttemptId }, resource));
    }

    protected ActionResult<HalResource<NativeRegistrationRequirementProgressCollectionDto>> ToNativeProgressResource(
        NativeRegistrationRequirementProgressCollectionDto? progress,
        Guid eventId,
        Guid orderId,
        bool guest)
    {
        if (progress is null)
        {
            return this.ToNotFoundProblem(RegistrationOrderNotFoundProblem);
        }

        string selfRouteName = guest
            ? RouteNames.GetGuestNativeRegistrationRequirementProgress
            : RouteNames.GetAuthenticatedNativeRegistrationRequirementProgress;
        string launchRouteName = guest
            ? RouteNames.LaunchGuestNativeRegistrationAttempt
            : RouteNames.LaunchAuthenticatedNativeRegistrationAttempt;
        string providerLaunchRouteName = guest
            ? RouteNames.LaunchGuestRegistrationProviderAttempt
            : RouteNames.LaunchAuthenticatedRegistrationProviderAttempt;
        var routeValues = new { eventId, orderId };
        var resource = new HalResource<NativeRegistrationRequirementProgressCollectionDto>(progress)
            .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(selfRouteName, routeValues)!));
        if (progress.Requirements.Count != 0)
        {
            resource.WithLink(LinkRelations.LaunchAttempt, HalLink.CreateAction(
                Url.Link(launchRouteName, routeValues)!, HttpMethods.Post));
        }
        if (progress.ProviderRequirements?.Count != 0)
        {
            resource.WithLink(LinkRelations.LaunchProviderAttempt, HalLink.CreateAction(
                Url.Link(providerLaunchRouteName, routeValues)!, HttpMethods.Post));
        }
        return Ok(resource);
    }

    protected ActionResult<HalResource<NativeRegistrationProviderLaunchDescriptorDto>> LaunchProviderAttempt(
        RegistrationProviderAttemptResult response,
        Guid eventId,
        Guid orderId,
        bool guest)
    {
        if (!response.Success || response.Descriptor is not { Available: true } descriptor)
        {
            return this.ToNotFoundProblem(RegistrationOrderNotFoundProblem);
        }

        Response.Headers.CacheControl = "private, no-store";
        string routeName = guest
            ? RouteNames.LaunchGuestRegistrationProviderAttempt
            : RouteNames.LaunchAuthenticatedRegistrationProviderAttempt;
        var resource = new HalResource<NativeRegistrationProviderLaunchDescriptorDto>(descriptor)
            .WithLink(LinkRelations.Self, HalLink.CreateAction(
                Url.Link(routeName, new { eventId, orderId })!, HttpMethods.Post))
            .WithLink(LinkRelations.RequirementProgress, HalLink.Create(
                Url.Link(guest
                    ? RouteNames.GetGuestNativeRegistrationRequirementProgress
                    : RouteNames.GetAuthenticatedNativeRegistrationRequirementProgress, new { eventId, orderId })!));
        return CreatedAtRoute(routeName, new { eventId, orderId }, resource);
    }

    protected ActionResult<NativeRegistrationSkipDto> MapNativeSkip(NativeRegistrationSkipResult response) =>
        response.Success && response.Progress is not null
            ? Ok(new NativeRegistrationSkipDto(response.Progress))
            : this.ToNotFoundProblem(RegistrationOrderNotFoundProblem);

    protected ActionResult<NativeRegistrationSubmissionDto> MapNativeSubmission(
        NativeRegistrationSubmissionResult response)
    {
        if (response.Success)
        {
            return Ok(new NativeRegistrationSubmissionDto(response.SubmissionId, true));
        }

        if (response.FailureCode != "registration_submission_invalid")
        {
            return this.ToNotFoundProblem(RegistrationOrderNotFoundProblem);
        }

        var errors = response.Issues
            .GroupBy(issue => issue.FieldKey ?? "$")
            .ToDictionary(group => group.Key, group => group.Select(issue => issue.Code).Distinct().ToArray());
        return ValidationProblem(new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Registration submission validation failed",
            Detail = "The submitted answers did not satisfy the pinned registration form."
        });
    }

    protected static IReadOnlyList<RegistrationSubmissionAnswerInput> MapNativeAnswers(
        IReadOnlyList<NativeRegistrationSubmissionAnswerRequest> answers) => answers.Select(answer =>
        new RegistrationSubmissionAnswerInput(
            answer.FieldId,
            answer.SubjectType,
            answer.SubjectId,
            answer.TicketAssignmentOrderLineId,
            answer.Value is JsonElement element
                ? element
                : JsonSerializer.SerializeToElement(answer.Value))).ToArray();

    protected ActionResult<BaseCommandResponse<Guid>> MapParticipantMutation(BaseCommandResponse<Guid> response) =>
        response.IsSuccess
            ? Ok(response)
            : response.FailureCode == "registration_order_not_found"
                ? this.ToNotFoundProblem(RegistrationOrderNotFoundProblem)
                : this.ToCommandValidationProblem(response, RegistrationOrderValidationProblem);

    protected HalResource<RegistrationOrderParticipantsDto> ToParticipantsHalResource(
        RegistrationOrderParticipantsDto participants, Guid eventId, bool guest)
    {
        string prefix = guest ? "guest/" : string.Empty;
        var values = new { eventId, orderId = participants.RegistrationOrderId };
        string selfRoute = guest
            ? RouteNames.GetGuestRegistrationOrderParticipants
            : RouteNames.GetAuthenticatedRegistrationOrderParticipants;
        var resource = new HalResource<RegistrationOrderParticipantsDto>(participants)
            .WithLink(LinkRelations.Self, HalLink.Create(Url.Link(selfRoute, values)!));
        if (!participants.CanManage)
        {
            return resource;
        }

        resource.WithLink(LinkRelations.AddParticipant, HalLink.CreateAction(
            Url.Link(guest ? RouteNames.AddGuestRegistrationOrderParticipant : RouteNames.AddAuthenticatedRegistrationOrderParticipant, values)!, HttpMethods.Post));
        resource.WithLink(LinkRelations.UpdateParticipant, new HalLink
        {
            Href = $"/api/events/{eventId:D}/registration-orders/{prefix}{participants.RegistrationOrderId:D}/participants/{{participantId}}",
            Templated = true,
            Method = HttpMethods.Put
        });
        resource.WithLink(LinkRelations.AssignTickets, HalLink.CreateAction(
            Url.Link(guest ? RouteNames.AssignGuestRegistrationOrderTickets : RouteNames.AssignAuthenticatedRegistrationOrderTickets, values)!, HttpMethods.Put));
        if (!guest && participants.CanImportCompanyCsv)
        {
            resource.WithLink(LinkRelations.ImportCompanyAssignmentsCsv, HalLink.CreateAction(
                Url.Link(RouteNames.ImportAuthenticatedRegistrationOrderCompanyAssignmentsCsv, values)!, HttpMethods.Post));
        }

        resource.WithLink(LinkRelations.DeferTickets, HalLink.CreateAction(
            Url.Link(guest ? RouteNames.DeferGuestRegistrationOrderTickets : RouteNames.DeferAuthenticatedRegistrationOrderTickets, values)!, HttpMethods.Put));
        return resource;
    }

    protected ActionResult<PromotionRedemptionResponseDto> MapPromotionRedemption(PromotionRedemptionResponseDto response) =>
        response.IsSuccess ? Ok(response) : this.ToNotFoundProblem(RegistrationOrderNotFoundProblem);
}
