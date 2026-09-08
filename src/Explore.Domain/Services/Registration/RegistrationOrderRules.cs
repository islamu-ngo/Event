using Explore.Domain.Enums;

namespace Explore.Domain.Services.Registration;

public static class RegistrationOrderRules
{
    public static bool CanConfirmParticipantAssignments(
        ParticipantDataCollectionModeEnum mode,
        int ticketUnitCount,
        IReadOnlyCollection<RegistrationTicketAssignment> assignments,
        DateTime? assignmentDeadline,
        DateTime evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(assignments);
        if (!Enum.IsDefined(mode) || ticketUnitCount <= 0)
        {
            throw new ArgumentException("Participant collection mode and ticket quantity are invalid.");
        }

        if (evaluatedAt.Kind != DateTimeKind.Utc || assignmentDeadline is { Kind: not DateTimeKind.Utc })
        {
            throw new ArgumentException("Participant assignment timestamps must be UTC.");
        }

        if (assignments.Count > ticketUnitCount ||
            assignments.Any(x => x.Ordinal > ticketUnitCount) ||
            assignments.Select(x => x.Id).Distinct().Count() != assignments.Count ||
            assignments.Select(x => x.Ordinal).Distinct().Count() != assignments.Count)
        {
            throw new ArgumentException("Ticket assignments must fit distinct ticket-unit ordinals.", nameof(assignments));
        }

        return mode switch
        {
            ParticipantDataCollectionModeEnum.None or ParticipantDataCollectionModeEnum.LeadBookerOnly =>
                assignments.Count == 0 && assignmentDeadline is null
                    ? true
                    : throw new ArgumentException("This collection mode does not permit ticket assignments.", nameof(assignments)),
            ParticipantDataCollectionModeEnum.PerTicketOptional =>
                assignmentDeadline is null && assignments.All(IsAssigned),
            ParticipantDataCollectionModeEnum.PerTicketRequired =>
                assignmentDeadline is null && assignments.Count == ticketUnitCount && assignments.All(IsAssigned),
            ParticipantDataCollectionModeEnum.DeferredAssignment =>
                assignmentDeadline > evaluatedAt && assignments.Count == ticketUnitCount && assignments.All(
                    assignment => IsAssigned(assignment) ||
                                  assignment.AssignmentStatusId == (int)AssignmentStatusEnum.Deferred &&
                                  assignment.ParticipantId is null &&
                                  assignment.AssignmentDeadline == assignmentDeadline),
            _ => false
        };
    }

    public static bool IsParticipantEligibleForTicket(RegistrationParticipant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        ParticipantTypeEnum type = (ParticipantTypeEnum)participant.ParticipantTypeId;
        return type is not (ParticipantTypeEnum.Child or ParticipantTypeEnum.Dependent) ||
               participant.GuardianParticipantId is not null;
    }

    public static bool IsTerminal(RegistrationOrderStatusEnum status) => status is
        RegistrationOrderStatusEnum.Confirmed or
        RegistrationOrderStatusEnum.Rejected or
        RegistrationOrderStatusEnum.Expired or
        RegistrationOrderStatusEnum.Cancelled;

    public static bool IsTerminalForCurrentWorkstream(RegistrationOrderStatusEnum status) =>
        IsTerminal(status) || status is RegistrationOrderStatusEnum.AwaitingPayment or RegistrationOrderStatusEnum.NeedsReconciliation;

    public static RegistrationOrderStatusEnum GetCheckoutDestination(long totalDueMinor)
    {
        if (totalDueMinor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalDueMinor));
        }

        return totalDueMinor == 0
            ? RegistrationOrderStatusEnum.Confirmed
            : RegistrationOrderStatusEnum.AwaitingPayment;
    }

    public static RegistrationOrderLifecycleDecision DescribeLifecycle(RegistrationOrderStatusEnum status)
    {
        return new RegistrationOrderLifecycleDecision(
            CanContinue: status is RegistrationOrderStatusEnum.AwaitingRequirements or RegistrationOrderStatusEnum.ReadyForCheckout,
            CanViewRequirementProgress: status == RegistrationOrderStatusEnum.AwaitingRequirements,
            CanManagePromotion: status == RegistrationOrderStatusEnum.ReadyForCheckout,
            CanFinalize: status == RegistrationOrderStatusEnum.ReadyForCheckout,
            CanViewPaymentStatus: status is RegistrationOrderStatusEnum.AwaitingPayment
                or RegistrationOrderStatusEnum.NeedsReconciliation
                or RegistrationOrderStatusEnum.Confirmed,
            CanCancel: CanTransition(status, RegistrationOrderStatusEnum.Cancelled));
    }

    public static bool CanTransition(RegistrationOrderStatusEnum current, RegistrationOrderStatusEnum desired)
    {
        if (!Enum.IsDefined(current) || !Enum.IsDefined(desired))
        {
            return false;
        }

        return current == desired || (current, desired) switch
        {
            (RegistrationOrderStatusEnum.Draft, RegistrationOrderStatusEnum.AwaitingIdentity or RegistrationOrderStatusEnum.AwaitingParticipantDetails or RegistrationOrderStatusEnum.AwaitingRequirements or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled) => true,
            (RegistrationOrderStatusEnum.AwaitingIdentity, RegistrationOrderStatusEnum.AwaitingParticipantDetails or RegistrationOrderStatusEnum.AwaitingRequirements or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.AwaitingParticipantDetails, RegistrationOrderStatusEnum.AwaitingIdentity or RegistrationOrderStatusEnum.AwaitingRequirements or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.AwaitingRequirements, RegistrationOrderStatusEnum.AwaitingIdentity or RegistrationOrderStatusEnum.AwaitingParticipantDetails or RegistrationOrderStatusEnum.ReadyForCheckout or RegistrationOrderStatusEnum.AwaitingApproval or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.ReadyForCheckout, RegistrationOrderStatusEnum.AwaitingRequirements or RegistrationOrderStatusEnum.AwaitingPayment or RegistrationOrderStatusEnum.AwaitingApproval or RegistrationOrderStatusEnum.Waitlisted or RegistrationOrderStatusEnum.Confirmed or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.AwaitingPayment, RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.AwaitingApproval, RegistrationOrderStatusEnum.ReadyForCheckout or RegistrationOrderStatusEnum.Confirmed or RegistrationOrderStatusEnum.Rejected or RegistrationOrderStatusEnum.Waitlisted or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.Waitlisted, RegistrationOrderStatusEnum.AwaitingApproval or RegistrationOrderStatusEnum.Confirmed or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled or RegistrationOrderStatusEnum.NeedsReconciliation) => true,
            (RegistrationOrderStatusEnum.NeedsReconciliation, RegistrationOrderStatusEnum.AwaitingIdentity or RegistrationOrderStatusEnum.AwaitingParticipantDetails or RegistrationOrderStatusEnum.AwaitingRequirements or RegistrationOrderStatusEnum.ReadyForCheckout or RegistrationOrderStatusEnum.AwaitingPayment or RegistrationOrderStatusEnum.AwaitingApproval or RegistrationOrderStatusEnum.Waitlisted or RegistrationOrderStatusEnum.Confirmed or RegistrationOrderStatusEnum.Rejected or RegistrationOrderStatusEnum.Expired or RegistrationOrderStatusEnum.Cancelled) => true,
            _ => false
        };
    }

    public static void EnsureCanTransition(RegistrationOrderStatusEnum current, RegistrationOrderStatusEnum desired)
    {
        if (!CanTransition(current, desired))
        {
            throw new InvalidOperationException($"Registration order cannot transition from {current} to {desired}.");
        }
    }

    private static bool IsAssigned(RegistrationTicketAssignment assignment) =>
        assignment.AssignmentStatusId == (int)AssignmentStatusEnum.Assigned && assignment.ParticipantId is not null;
}

public readonly record struct RegistrationOrderLifecycleDecision(
    bool CanContinue,
    bool CanViewRequirementProgress,
    bool CanManagePromotion,
    bool CanFinalize,
    bool CanViewPaymentStatus,
    bool CanCancel);
