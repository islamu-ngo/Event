using Explore.Application.Caching;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Location;
using Explore.Application.Exceptions;
using Explore.Application.Features.EventLocations.Requests.Commands;
using Explore.Application.Features.EventLocations.Validators;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.EventLocations.Handlers.Commands;

public sealed class UpdateEventLocationPolicyCommandHandler(
    IEventLocationRepository eventLocations,
    IEventLocationDisclosureAuditRepository audits,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    ITenantContext tenantContext,
    IUserContext userContext,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateEventLocationPolicyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        UpdateEventLocationPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new UpdateEventLocationPolicyCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.EventLocationId,
                "event_location_policy_validation_failed",
                "EventLocation policy update failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        Guid actorUserId = userContext.GetRequiredUserId();
        BaseCommandResponse<Guid> result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation? eventLocation = await eventLocations.GetForUpdateAsync(
                request.EventLocationId,
                token);
            if (eventLocation is null
                || eventLocation.TenantId != tenantContext.TenantId
                || eventLocation.EventId != request.EventId)
            {
                return Failure(
                    request.EventLocationId,
                    "event_location_policy_not_found",
                    "EventLocation policy was not found.");
            }

            if (eventLocation.ConcurrencyStamp != request.ExpectedConcurrencyStamp
                || eventLocation.PolicyVersion != request.ExpectedPolicyVersion)
            {
                throw ConcurrencyConflict(eventLocation.Id);
            }

            EventLocationDisclosureFields selectedFields = MergeSelectedFields(eventLocation, request.Fields);
            var fullDetailsAudience = (LocationDisclosureAudienceEnum)(
                request.Audience?.FullDetailsAudienceId ?? eventLocation.FullDetailsAudienceId);
            DateTime? revealFullDetailsFromUtc = eventLocation.RevealFullDetailsFromUtc;
            if (request.Audience?.RevealFullDetailsFromUtc is { HasValue: true } revealUpdate)
            {
                revealFullDetailsFromUtc = revealUpdate.Value;
            }

            if (eventLocation.IsToBeAnnounced
                && (selectedFields != EventLocationDisclosureFields.None
                    || fullDetailsAudience != LocationDisclosureAudienceEnum.Never
                    || revealFullDetailsFromUtc.HasValue))
            {
                return Failure(
                    eventLocation.Id,
                    "event_location_policy_tba_fields_forbidden",
                    "A to-be-announced EventLocation cannot disclose physical-location fields.");
            }

            EventLocationDisclosureAudit audit = eventLocation.ChangeDisclosurePolicy(
                selectedFields,
                fullDetailsAudience,
                revealFullDetailsFromUtc,
                request.ExpectedPolicyVersion,
                actorUserId,
                EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
                timeProvider.GetUtcNow().UtcDateTime,
                request.NeedsPrivacyReview);
            await audits.AppendAsync(audit, token);
            return Success(eventLocation.Id);
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            return result;
        }

        await cache.RemoveByTagAsync(
            CacheTags.EventLocation(request.EventLocationId),
            CancellationToken.None);
        return result;
    }

    private static EventLocationDisclosureFields MergeSelectedFields(
        EventLocation eventLocation,
        UpdateEventLocationDisclosureFieldsDto? update)
    {
        EventLocationDisclosureFields fields = EventLocationDisclosureFields.None;
        fields = SetField(fields, EventLocationDisclosureFields.VenueName, update?.ShowVenueName ?? eventLocation.ShowVenueName);
        fields = SetField(fields, EventLocationDisclosureFields.City, update?.ShowCity ?? eventLocation.ShowCity);
        fields = SetField(fields, EventLocationDisclosureFields.Country, update?.ShowCountry ?? eventLocation.ShowCountry);
        fields = SetField(fields, EventLocationDisclosureFields.RoomName, update?.ShowRoomName ?? eventLocation.ShowRoomName);
        fields = SetField(fields, EventLocationDisclosureFields.StreetAddress, update?.ShowStreetAddress ?? eventLocation.ShowStreetAddress);
        fields = SetField(fields, EventLocationDisclosureFields.Postcode, update?.ShowPostcode ?? eventLocation.ShowPostcode);
        fields = SetField(fields, EventLocationDisclosureFields.Coordinates, update?.ShowCoordinates ?? eventLocation.ShowCoordinates);
        return fields;
    }

    private static EventLocationDisclosureFields SetField(
        EventLocationDisclosureFields fields,
        EventLocationDisclosureFields field,
        bool enabled) => enabled ? fields | field : fields;

    private static ConcurrencyConflictException ConcurrencyConflict(Guid eventLocationId) => new(
        ConcurrencyConflictException.ConcurrentUpdate,
        "The EventLocation disclosure policy was modified by another request. Reload and retry.",
        nameof(EventLocation),
        eventLocationId.ToString("D"));

    private static BaseCommandResponse<Guid> Success(Guid eventLocationId) =>
        BaseCommandResponse.Success(eventLocationId, "EventLocation policy updated.");

    private static BaseCommandResponse<Guid> Failure(
        Guid eventLocationId,
        string code,
        string message,
        IEnumerable<string>? errors = null) => BaseCommandResponse.Failure<Guid>(
            code,
            message,
            errors ?? [message],
            eventLocationId);
}

public sealed class ConfirmEventLocationRemediationCommandHandler(
    IEventLocationRepository eventLocations,
    IUnitOfWork unitOfWork,
    HybridCache cache,
    ITenantContext tenantContext,
    IUserContext userContext,
    TimeProvider timeProvider)
    : IRequestHandler<ConfirmEventLocationRemediationCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ConfirmEventLocationRemediationCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ConfirmEventLocationRemediationCommandValidator();
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return Failure(
                request.EventLocationId,
                "event_location_remediation_validation_failed",
                "EventLocation remediation confirmation failed validation.",
                validation.Errors.Select(error => error.ErrorMessage));
        }

        Guid actorUserId = userContext.GetRequiredUserId();
        BaseCommandResponse<Guid> result = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            EventLocation? eventLocation = await eventLocations.GetForUpdateAsync(
                request.EventLocationId,
                token);
            if (eventLocation is null
                || eventLocation.TenantId != tenantContext.TenantId
                || eventLocation.EventId != request.EventId)
            {
                return Failure(
                    request.EventLocationId,
                    "event_location_remediation_not_found",
                    "EventLocation remediation target was not found.");
            }

            if (eventLocation.ConcurrencyStamp != request.ExpectedConcurrencyStamp
                || eventLocation.PolicyVersion != request.ExpectedPolicyVersion)
            {
                throw ConcurrencyConflict(eventLocation.Id);
            }

            if (!eventLocation.NeedsPrivacyReview)
            {
                return Success(eventLocation.Id, "EventLocation privacy review is already complete.");
            }

            if (!eventLocation.SatisfiesPublicationVenueRequirement(eventLocation.Location))
            {
                return Failure(
                    eventLocation.Id,
                    "event_location_remediation_location_unusable",
                    "Replace the unusable physical location or explicitly select TBA before confirming remediation.");
            }

            DateTime changedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            EventLocationDisclosureAudit audit = eventLocation.CompletePrivacyReview(
                actorUserId,
                changedAtUtc);
            OutboxMessage correction = LocationPrivacyOutboxMessageFactory.CreateProjectionCorrection(
                Guid.CreateVersion7(),
                eventLocation,
                changedAtUtc);
            await eventLocations.SaveGovernanceChangesAsync([audit], [correction], token);
            return Success(eventLocation.Id, "EventLocation privacy remediation confirmed.");
        }, cancellationToken);

        if (!result.IsSuccess)
        {
            return result;
        }

        await cache.RemoveByTagAsync(CacheTags.EventLocation(request.EventLocationId), CancellationToken.None);
        await cache.RemoveByTagAsync(CacheTags.EventLocationsByEvent(request.EventId), CancellationToken.None);
        await cache.RemoveByTagAsync(CacheTags.Event(request.EventId), CancellationToken.None);
        await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantContext.TenantId), CancellationToken.None);
        return result;
    }

    private static ConcurrencyConflictException ConcurrencyConflict(Guid eventLocationId) => new(
        ConcurrencyConflictException.ConcurrentUpdate,
        "The EventLocation was modified by another request. Reload and retry remediation.",
        nameof(EventLocation),
        eventLocationId.ToString("D"));

    private static BaseCommandResponse<Guid> Success(Guid eventLocationId, string message) =>
        BaseCommandResponse.Success(eventLocationId, message);

    private static BaseCommandResponse<Guid> Failure(
        Guid eventLocationId,
        string code,
        string message,
        IEnumerable<string>? errors = null) => BaseCommandResponse.Failure<Guid>(
            code,
            message,
            errors ?? [message],
            eventLocationId);
}
