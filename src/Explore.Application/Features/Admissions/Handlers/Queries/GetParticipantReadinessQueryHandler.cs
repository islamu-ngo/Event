using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.Admissions.Handlers.Queries;

public sealed class GetParticipantReadinessQueryHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationParticipantRepository participants,
    IParticipantAdmissionEligibilityRepository readiness,
    IGuestCapabilityTokenService capabilities,
    IEventRepository events,
    IAuthorizationProvider authorization,
    ICurrentUserService currentUser,
    ITenantContext tenant,
    TimeProvider timeProvider) :
    IRequestHandler<
        GetParticipantReadinessQuery,
        ParticipantReadinessDto?>
{
    public async Task<ParticipantReadinessDto?> Handle(
        GetParticipantReadinessQuery request,
        CancellationToken cancellationToken)
    {
        if (request.EventId == Guid.Empty
            || request.RegistrationOrderId == Guid.Empty
            || request.ParticipantId == Guid.Empty
            || request.RegistrationTicketAssignmentId ==
            Guid.Empty)
        {
            return null;
        }

        RegistrationOrder? order =
            await inventory.GetOrderWithLinesAsync(
                request.RegistrationOrderId,
                tenant.TenantId,
                cancellationToken);
        if (order is null || order.EventId != request.EventId)
        {
            return null;
        }

        ParticipantAdmissionEligibility? eligibility =
            await readiness.GetAsync(
                tenant.TenantId,
                request.RegistrationTicketAssignmentId,
                cancellationToken);
        RegistrationParticipant? participant =
            await participants.GetParticipantAsync(
                request.ParticipantId,
                request.RegistrationOrderId,
                tenant.TenantId,
                cancellationToken);
        if (eligibility is null
            || participant is null
            || eligibility.EventId != request.EventId
            || eligibility.RegistrationOrderId !=
            request.RegistrationOrderId
            || eligibility.ParticipantId !=
            request.ParticipantId)
        {
            return null;
        }

        Guid? userId = currentUser.IsAuthenticated
            ? currentUser.UserId
            : null;
        bool isSubject =
            userId.HasValue
            && (order.AccountUserId == userId
                || participant.LinkedUserId == userId
                || eligibility.SubjectUserId == userId);
        bool hasGuestCapability =
            RegistrationOrderAccessGuard.HasGuestAccess(
                order,
                request.EventId,
                request.CapabilityToken,
                capabilities,
                timeProvider);
        bool isOrganizer =
            userId.HasValue
            && await OrganizerMayManageAsync(
                order,
                cancellationToken);
        if (!isSubject && !hasGuestCapability && !isOrganizer)
        {
            return null;
        }

        Event? admissionEvent =
            await events.GetAuthorizationTargetByIdAsync(
                order.EventId,
                cancellationToken);
        bool orderConfirmed =
            order.RegistrationOrderStatusId ==
            (int)RegistrationOrderStatusEnum.Confirmed
            && order.ConfirmedAt.HasValue
            && admissionEvent?.TenantId == order.TenantId
            && admissionEvent.EventStatusId !=
            (int)EventStatusEnum.Cancelled;
        ParticipantAdmissionReadinessDecision decision =
            eligibility.DescribeReadiness(
                orderConfirmed,
                order.TotalDueMinorSnapshot == 0
                || orderConfirmed);
        AdmissionTicket? ticket =
            await readiness.GetIssuedTicketAsync(
                tenant.TenantId,
                request.RegistrationTicketAssignmentId,
                cancellationToken);
        bool activeAdmission =
            ticket?.AdmissionTicketStatusId ==
            (int)AdmissionTicketStatusEnum.Active;

        return new ParticipantReadinessDto
        {
            RegistrationTicketAssignmentId =
                eligibility
                    .RegistrationTicketAssignmentId,
            StatusCode = StatusCode(decision.Code),
            SupportCode = SupportCode(
                decision.Code,
                activeAdmission),
            ActiveAdmissionAvailable = activeAdmission,
            CanComplete =
                isSubject
                && !eligibility.RevokedAt.HasValue
                && eligibility.RequirementsCompletedAt is null,
            CanApprove =
                isOrganizer
                && eligibility.ApprovalRequired
                && eligibility.RequirementsCompletedAt.HasValue
                && (!eligibility.ConsentRequired
                    || eligibility.SubjectConsentRecordId.HasValue)
                && !eligibility.ApprovedAt.HasValue
                && !eligibility.RevokedAt.HasValue,
            CanRevoke =
                isOrganizer
                && !eligibility.RevokedAt.HasValue,
            EventId = eligibility.EventId,
            RegistrationOrderId =
                eligibility.RegistrationOrderId,
            ParticipantId = eligibility.ParticipantId,
        };
    }

    private async Task<bool> OrganizerMayManageAsync(
        RegistrationOrder order,
        CancellationToken cancellationToken)
    {
        Event? eventEntity =
            await events.GetAuthorizationTargetByIdAsync(
                order.EventId,
                cancellationToken);
        if (eventEntity?.TenantId != order.TenantId)
        {
            return false;
        }

        AuthorizationDecision decision =
            await authorization.AuthorizeAsync(
                new AuthorizationRequest(
                    AuthorizationCapabilityCatalog.Require(
                        ResourceKinds.Event,
                        AuthorizationActions.Events
                            .ManageAttendees),
                    eventEntity.Id.ToString("D"),
                    Scope: ResourceDescriptors
                        .EventAuthorizationTarget
                        .GetScope(eventEntity),
                    Facts: ResourceDescriptors
                        .EventAuthorizationTarget
                        .GetFacts(eventEntity)),
                cancellationToken);
        return decision.IsAllowed;
    }

    private static string StatusCode(
        ParticipantAdmissionReadinessCode code) =>
        code switch
        {
            ParticipantAdmissionReadinessCode.Ready =>
                "ready",
            ParticipantAdmissionReadinessCode
                .OrderNotConfirmed =>
                "order_not_confirmed",
            ParticipantAdmissionReadinessCode.PaymentPending =>
                "payment_pending",
            ParticipantAdmissionReadinessCode
                .SubjectOwnershipPending =>
                "subject_ownership_pending",
            ParticipantAdmissionReadinessCode
                .ParticipantCompletionPending =>
                "participant_completion_pending",
            ParticipantAdmissionReadinessCode
                .SubjectConsentPending =>
                "subject_consent_pending",
            ParticipantAdmissionReadinessCode
                .ApprovalPending =>
                "approval_pending",
            ParticipantAdmissionReadinessCode.Revoked =>
                "revoked",
            _ => throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                null),
        };

    private static string SupportCode(
        ParticipantAdmissionReadinessCode code,
        bool activeAdmission) =>
        code switch
        {
            ParticipantAdmissionReadinessCode.Ready
                when activeAdmission => "none",
            ParticipantAdmissionReadinessCode.Ready =>
                "credential_pending",
            ParticipantAdmissionReadinessCode.Revoked =>
                "contact_organizer",
            _ => "action_required",
        };
}
