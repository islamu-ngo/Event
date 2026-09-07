using Explore.Domain.Enums;

namespace Explore.Domain;

public static class AdmissionCheckInRules
{
    public static AdmissionCheckInDecision Decide(
        AdmissionTicket ticket,
        AdmissionTarget target,
        TicketTypeEntitlement entitlement,
        AdmissionCheckInPolicy policy,
        AdmissionCheckInState currentState,
        AdmissionCheckInActionEnum action,
        Guid eventId,
        Guid? actorId,
        Guid? scannerCapabilityId,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        DateTime occurredAtUtc,
        Guid? expectedCheckInEventId = null)
    {
        ValidateLineage(ticket, target, entitlement, policy, currentState);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        RequireUuidV7(eventId, nameof(eventId));
        ValidateAuthority(actorId, scannerCapabilityId);
        RequireUtc(occurredAtUtc, nameof(occurredAtUtc));
        if (!target.IsOperational)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.AdmissionStopped, currentState);
        }

        return action switch
        {
            AdmissionCheckInActionEnum.CheckIn => DecideCheckIn(
                ticket,
                target,
                entitlement,
                policy,
                currentState,
                eventId,
                actorId,
                scannerCapabilityId,
                reasonCode,
                occurredAtUtc),
            AdmissionCheckInActionEnum.Undo => DecideUndo(
                ticket,
                target,
                currentState,
                eventId,
                actorId,
                scannerCapabilityId,
                reasonCode,
                occurredAtUtc,
                expectedCheckInEventId),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private static AdmissionCheckInDecision DecideCheckIn(
        AdmissionTicket ticket,
        AdmissionTarget target,
        TicketTypeEntitlement entitlement,
        AdmissionCheckInPolicy policy,
        AdmissionCheckInState currentState,
        Guid eventId,
        Guid? actorId,
        Guid? scannerCapabilityId,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        DateTime occurredAtUtc)
    {
        if (reasonCode is not null)
        {
            throw new ArgumentException("Check-in facts cannot carry an undo reason.", nameof(reasonCode));
        }

        if (!IsEntitled(target, entitlement))
        {
            return NoFact(AdmissionCheckInResultCodeEnum.NotEntitled, currentState);
        }

        if (occurredAtUtc < policy.OpensAtUtc)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.TooEarly, currentState);
        }

        if (occurredAtUtc > policy.ClosesAtUtc)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.TooLate, currentState);
        }

        if (currentState.ActiveCheckInEventId.HasValue)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.AlreadyCheckedIn, currentState);
        }

        if (currentState.EntryCount >= policy.MaximumEntries)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.ReEntryNotAllowed, currentState);
        }

        long sequence = checked(currentState.LastSequence + 1L);
        AdmissionCheckInEvent fact = CreateFact(
            ticket,
            target,
            sequence,
            AdmissionCheckInActionEnum.CheckIn,
            eventId,
            actorId,
            scannerCapabilityId,
            null,
            occurredAtUtc,
            null);
        AdmissionCheckInState nextState = currentState.Project(
            fact.Id,
            checked(currentState.EntryCount + 1),
            sequence);
        AdmissionCheckInResultCodeEnum resultCode = currentState.EntryCount == 0
            ? AdmissionCheckInResultCodeEnum.CheckedIn
            : AdmissionCheckInResultCodeEnum.ReEntered;
        return new AdmissionCheckInDecision(resultCode, fact, nextState);
    }

    private static AdmissionCheckInDecision DecideUndo(
        AdmissionTicket ticket,
        AdmissionTarget target,
        AdmissionCheckInState currentState,
        Guid eventId,
        Guid? actorId,
        Guid? scannerCapabilityId,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        DateTime occurredAtUtc,
        Guid? expectedCheckInEventId)
    {
        if (!reasonCode.HasValue || !Enum.IsDefined(reasonCode.Value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reasonCode),
                "Undo requires a supported reason code.");
        }
        if (!currentState.ActiveCheckInEventId.HasValue)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.NotCheckedIn, currentState);
        }

        if (!expectedCheckInEventId.HasValue ||
            expectedCheckInEventId.Value != currentState.ActiveCheckInEventId.Value)
        {
            return NoFact(AdmissionCheckInResultCodeEnum.CheckInNotFound, currentState);
        }

        if (eventId == currentState.ActiveCheckInEventId.Value)
        {
            throw new ArgumentException("Undo requires a new admission event identity.", nameof(eventId));
        }

        long sequence = checked(currentState.LastSequence + 1L);
        AdmissionCheckInEvent fact = CreateFact(
            ticket,
            target,
            sequence,
            AdmissionCheckInActionEnum.Undo,
            eventId,
            actorId,
            scannerCapabilityId,
            reasonCode,
            occurredAtUtc,
            currentState.ActiveCheckInEventId.Value);
        AdmissionCheckInState nextState = currentState.Project(null, currentState.EntryCount, sequence);
        return new AdmissionCheckInDecision(AdmissionCheckInResultCodeEnum.Undone, fact, nextState);
    }

    private static AdmissionCheckInEvent CreateFact(
        AdmissionTicket ticket,
        AdmissionTarget target,
        long sequence,
        AdmissionCheckInActionEnum action,
        Guid eventId,
        Guid? actorId,
        Guid? scannerCapabilityId,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode,
        DateTime occurredAtUtc,
        Guid? compensatedCheckInEventId) => new(
            eventId,
            ticket.TenantId,
            ticket.Id,
            target.Id,
            sequence,
            action,
            actorId,
            scannerCapabilityId,
            reasonCode,
            occurredAtUtc,
            compensatedCheckInEventId);

    private static AdmissionCheckInDecision NoFact(
        AdmissionCheckInResultCodeEnum resultCode,
        AdmissionCheckInState currentState) => new(resultCode, null, currentState);

    private static bool IsEntitled(AdmissionTarget target, TicketTypeEntitlement entitlement)
    {
        if (entitlement.EntitlementScopeTypeId != target.AdmissionTargetTypeId)
        {
            return false;
        }

        return (AdmissionTargetTypeEnum)target.AdmissionTargetTypeId switch
        {
            AdmissionTargetTypeEnum.Event =>
                entitlement.EventDayId is null && entitlement.EventSessionId is null,
            AdmissionTargetTypeEnum.EventDay =>
                entitlement.EventDayId == target.EventDayId && entitlement.EventSessionId is null,
            AdmissionTargetTypeEnum.EventSession =>
                entitlement.EventDayId is null && entitlement.EventSessionId == target.EventSessionId,
            _ => false
        };
    }

    private static void ValidateLineage(
        AdmissionTicket ticket,
        AdmissionTarget target,
        TicketTypeEntitlement entitlement,
        AdmissionCheckInPolicy policy,
        AdmissionCheckInState currentState)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entitlement);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(currentState);

        bool sharedAuthority = ticket.TenantId == target.TenantId &&
            ticket.EventId == target.EventId &&
            entitlement.TenantId == ticket.TenantId &&
            entitlement.TargetEventId == ticket.EventId &&
            entitlement.TicketTypeId == ticket.EventTicketTypeId &&
            policy.AppliesTo(target) &&
            currentState.TenantId == ticket.TenantId &&
            currentState.AdmissionTicketId == ticket.Id &&
            currentState.AdmissionTargetId == target.Id;
        if (!sharedAuthority)
        {
            throw new ArgumentException(
                "Admission ticket, entitlement, target, policy, and state must share exact tenant and event authority.");
        }
    }

    private static void ValidateAuthority(Guid? actorId, Guid? scannerCapabilityId)
    {
        if (actorId.HasValue == scannerCapabilityId.HasValue)
        {
            throw new ArgumentException("Exactly one actor or scanner capability must authorize an admission fact.");
        }

        RequireUuidV7(
            actorId ?? scannerCapabilityId!.Value,
            actorId.HasValue ? nameof(actorId) : nameof(scannerCapabilityId));
    }

    private static void RequireUuidV7(Guid value, string parameterName)
    {
        if (value == Guid.Empty || value.Version != 7 || value.Variant is < 8 or > 11)
        {
            throw new ArgumentException("Admission identity must be an RFC 4122 UUIDv7 value.", parameterName);
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value == default || value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Admission occurrence time must be a non-default UTC value.", parameterName);
        }
    }
}
