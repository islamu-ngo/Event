using Explore.Application.Authorization;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Admissions;
using Explore.Application.Features.Admissions.Requests.Queries;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.Admissions.Handlers.Queries;

public sealed class GetParticipantReadinessQueryHandler(
    IRegistrationPrivateReader reader) :
    IQueryHandler<
        GetParticipantReadinessQuery,
        ParticipantReadinessDto?>
{
    public async Task<ParticipantReadinessDto?> QueryAsync(
        GetParticipantReadinessQuery query,
        CancellationToken cancellationToken = default)
    {
        if (query.EventId == Guid.Empty
            || query.RegistrationOrderId == Guid.Empty
            || query.ParticipantId == Guid.Empty
            || query.RegistrationTicketAssignmentId ==
            Guid.Empty)
        {
            return null;
        }

        var graph = await reader.ReadAsync(new(RegistrationPrivateReadKind.Readiness,
            query.EventId, query.RegistrationOrderId, ParticipantId: query.ParticipantId,
            AssignmentId: query.RegistrationTicketAssignmentId), query.CapabilityToken, cancellationToken);
        if (graph is null)
        {
            return null;
        }

        RegistrationOrder order = graph.Order;
        ParticipantAdmissionEligibility? eligibility = graph.Eligibility;
        RegistrationParticipant? participant = graph.Participant;
        if (eligibility is null
            || participant is null
            || eligibility.EventId != query.EventId
            || eligibility.RegistrationOrderId !=
            query.RegistrationOrderId
            || eligibility.ParticipantId !=
            query.ParticipantId)
        {
            return null;
        }

        Guid? userId = graph.PrincipalId;
        bool isSubject =
            userId.HasValue
            && (order.AccountUserId == userId
                || participant.LinkedUserId == userId
                || eligibility.SubjectUserId == userId);
        bool isOrganizer = graph.CanManageAttendees;
        Event? admissionEvent = graph.Event;
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
        AdmissionTicket? ticket = graph.Ticket;
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
