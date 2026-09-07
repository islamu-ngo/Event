using System.Collections.Immutable;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services;

public sealed class EventLocationRegistrationAccessService : IEventLocationRegistrationAccessService
{
    public IReadOnlyDictionary<Guid, EventLocationRegistrationAccess> ResolveMany(
        Guid tenantId,
        Guid eventId,
        Guid userId,
        DateTimeOffset asOfUtc,
        IReadOnlyCollection<Guid> requestedEventLocationIds,
        IReadOnlyCollection<EventRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(requestedEventLocationIds);
        ArgumentNullException.ThrowIfNull(registrations);

        if (tenantId == Guid.Empty
            || eventId == Guid.Empty
            || userId == Guid.Empty
            || asOfUtc == default
            || asOfUtc.Offset != TimeSpan.Zero)
        {
            return new Dictionary<Guid, EventLocationRegistrationAccess>();
        }

        Guid[] requestedIds = requestedEventLocationIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .Order()
            .ToArray();
        if (requestedIds.Length == 0)
        {
            return new Dictionary<Guid, EventLocationRegistrationAccess>();
        }

        LoadedOrderSource[] orderSources = registrations
            .Where(registration => HasValidLoadedSource(registration, tenantId, eventId, userId))
            .GroupBy(registration => registration.RegistrationOrderId!.Value)
            .Select(CreateLoadedOrderSource)
            .ToArray();
        if (orderSources.Length == 0)
        {
            return new Dictionary<Guid, EventLocationRegistrationAccess>();
        }

        var results = new Dictionary<Guid, EventLocationRegistrationAccess>(requestedIds.Length);
        foreach (Guid requestedId in requestedIds)
        {
            EventLocationRegistrationAccess strongest = orderSources
                .Select(source => Resolve(new(
                    requestedId,
                    asOfUtc,
                    source.Order,
                    source.Coverage)))
                .OrderByDescending(access => access.CoversRequestedEventLocation)
                .ThenByDescending(access => AuthorityRank(access.EffectiveState))
                .ThenBy(access => access.OrderId)
                .First();
            results.Add(requestedId, strongest);
        }

        return results;
    }

    public EventLocationRegistrationAccess Resolve(EventLocationRegistrationAccessRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        EventLocationRegistrationOrderFact order = request.Order;
        if (!HasValidIdentity(request))
        {
            return NoAccess(request, EventLocationRegistrationEffectiveState.Denied);
        }

        if (!IsOrderLive(order, request.AsOfUtc))
        {
            return NoAccess(request, EventLocationRegistrationEffectiveState.NonLive);
        }

        EventLocationRegistrationEffectiveState? orderState = ResolveParentState(order.OrderStatusId);
        if (orderState is not null && !HasAudienceAuthority(orderState.Value))
        {
            return NoAccess(request, orderState.Value);
        }

        ResolvedCoverage[] resolvedCoverage = request.Coverage
            .Where(item => IsCoverageForOrder(item, order))
            .Select(item => new ResolvedCoverage(
                item,
                ResolveEffectiveCoverageState(item, request.AsOfUtc, orderState)))
            .ToArray();
        ResolvedCoverage[] liveCoverage = resolvedCoverage
            .Where(item => HasAudienceAuthority(item.State))
            .ToArray();

        if (liveCoverage.Length == 0)
        {
            return NoAccess(request, ResolveRequestedTerminalState(resolvedCoverage, request.RequestedEventLocationId));
        }

        ResolvedCoverage[] requestedCoverage = liveCoverage
            .Where(item => item.Fact.EventLocationId == request.RequestedEventLocationId)
            .ToArray();
        EventLocationRegistrationEffectiveState effectiveState = requestedCoverage.Length == 0
            ? ResolveRequestedTerminalState(resolvedCoverage, request.RequestedEventLocationId)
            : requestedCoverage.MaxBy(item => AuthorityRank(item.State))!.State;

        return CreateAccess(
            order.OrderId,
            effectiveState,
            order.EventId,
            liveCoverage
                .Select(item => item.Fact.EventSessionId)
                .Distinct()
                .Order()
                .ToImmutableArray(),
            request.RequestedEventLocationId,
            requestedCoverage.Length > 0);
    }

    private static bool HasValidLoadedSource(
        EventRegistration registration,
        Guid tenantId,
        Guid eventId,
        Guid userId)
    {
        RegistrationOrder? order = registration.RegistrationOrder;
        EventSession? session = registration.EventSession;
        Event? @event = registration.Event;
        return registration.TenantId == tenantId
            && registration.EventId == eventId
            && registration.LinkedUserId == userId
            && registration.RegistrationOrderId is { } orderId
            && order is not null
            && order.Id == orderId
            && order.TenantId == tenantId
            && order.EventId == eventId
            && order.AccountUserId == userId
            && @event is not null
            && @event.Id == eventId
            && @event.TenantId == tenantId
            && !@event.IsDeleted
            && session is not null
            && session.Id == registration.EventSessionId
            && session.TenantId == tenantId
            && session.EventId == eventId
            && HasValidPlacementIdentity(session, tenantId, eventId);
    }

    private static LoadedOrderSource CreateLoadedOrderSource(
        IGrouping<Guid, EventRegistration> registrations)
    {
        EventRegistration[] rows = registrations.ToArray();
        RegistrationOrder order = rows[0].RegistrationOrder!;
        var orderFact = new EventLocationRegistrationOrderFact(
            order.Id,
            order.EventId,
            order.RegistrationOrderStatusId,
            order.IsDeleted,
            ToUtcOffset(order.ExpiresAt));

        ImmutableArray<EventLocationRegistrationCoverageFact> coverage = rows
            .Select(registration => new EventLocationRegistrationCoverageFact(
                order.Id,
                order.EventId,
                registration.EventSession.EventDayId,
                registration.EventSession.Id,
                registration.EventSession.EventLocationId!.Value,
                registration.ApprovalStatusId,
                registration.EventSession.RegistrationModeId,
                registration.IsDeleted || registration.EventSession.IsDeleted || registration.EventSession.EventLocation!.IsDeleted,
                ExpiresAtUtc: null))
            .ToImmutableArray();
        return new(orderFact, coverage);
    }

    private static DateTimeOffset? ToUtcOffset(DateTime? value) => value is { Kind: DateTimeKind.Utc } utc
        ? new DateTimeOffset(utc)
        : null;

    private static bool HasValidPlacementIdentity(EventSession session, Guid tenantId, Guid eventId)
        => session.Id != Guid.Empty
            && session.TenantId == tenantId
            && session.EventId == eventId
            && session.EventLocationId is { } eventLocationId
            && eventLocationId != Guid.Empty
            && session.EventLocation is { } eventLocation
            && eventLocation.Id == eventLocationId
            && eventLocation.TenantId == tenantId
            && eventLocation.EventId == eventId;

    private static int? ToApprovalStatusId(RegistrationOrderStatusEnum status) => status switch
    {
        RegistrationOrderStatusEnum.Confirmed => (int)ApprovalStatusEnum.Approved,
        RegistrationOrderStatusEnum.AwaitingApproval => (int)ApprovalStatusEnum.Pending,
        RegistrationOrderStatusEnum.Waitlisted => (int)ApprovalStatusEnum.Waitlisted,
        RegistrationOrderStatusEnum.Rejected => (int)ApprovalStatusEnum.Rejected,
        RegistrationOrderStatusEnum.Cancelled => (int)ApprovalStatusEnum.Cancelled,
        _ => null
    };

    private static bool HasValidIdentity(EventLocationRegistrationAccessRequest request)
        => request.RequestedEventLocationId != Guid.Empty
            && request.AsOfUtc != default
            && request.Order.OrderId != Guid.Empty
            && request.Order.EventId != Guid.Empty
            && Enum.IsDefined((RegistrationOrderStatusEnum)request.Order.OrderStatusId)
            && !request.Coverage.IsDefault;

    private static bool IsOrderLive(EventLocationRegistrationOrderFact order, DateTimeOffset asOfUtc)
        => !order.IsDeleted
            && (!order.ExpiresAtUtc.HasValue || order.ExpiresAtUtc.Value > asOfUtc);

    private static bool IsCoverageForOrder(
        EventLocationRegistrationCoverageFact coverage,
        EventLocationRegistrationOrderFact order)
        => coverage.OrderId == order.OrderId
            && coverage.EventId == order.EventId
            && coverage.EventSessionId != Guid.Empty
            && coverage.EventLocationId != Guid.Empty;

    private static EventLocationRegistrationEffectiveState ResolveEffectiveCoverageState(
        EventLocationRegistrationCoverageFact coverage,
        DateTimeOffset asOfUtc,
        EventLocationRegistrationEffectiveState? orderState)
    {
        if (coverage.IsDeleted || coverage.ExpiresAtUtc.HasValue && coverage.ExpiresAtUtc.Value <= asOfUtc)
        {
            return EventLocationRegistrationEffectiveState.NonLive;
        }

        return ApplyOrderCeiling(
            ResolveCoverageState(coverage.ApprovalStatusId, coverage.RegistrationModeId),
            orderState);
    }

    private static EventLocationRegistrationEffectiveState? ResolveParentState(int orderStatusId)
        => ToApprovalStatusId((RegistrationOrderStatusEnum)orderStatusId) is { } approvalStatusId
            ? ResolveCoverageState(approvalStatusId, registrationModeId: null)
            : null;

    private static EventLocationRegistrationEffectiveState ResolveCoverageState(
        int? approvalStatusId,
        int? registrationModeId)
        => approvalStatusId switch
        {
            (int)ApprovalStatusEnum.Approved => EventLocationRegistrationEffectiveState.Confirmed,
            (int)ApprovalStatusEnum.Pending => EventLocationRegistrationEffectiveState.Pending,
            (int)ApprovalStatusEnum.Waitlisted => EventLocationRegistrationEffectiveState.Waitlisted,
            (int)ApprovalStatusEnum.Rejected => EventLocationRegistrationEffectiveState.Rejected,
            (int)ApprovalStatusEnum.Cancelled => EventLocationRegistrationEffectiveState.Cancelled,
            (int)ApprovalStatusEnum.Revoked => EventLocationRegistrationEffectiveState.Revoked,
            null => registrationModeId switch
            {
                (int)RegistrationModeEnum.Open => EventLocationRegistrationEffectiveState.Confirmed,
                (int)RegistrationModeEnum.ApprovalRequired => EventLocationRegistrationEffectiveState.Pending,
                _ => EventLocationRegistrationEffectiveState.Denied
            },
            _ => EventLocationRegistrationEffectiveState.Denied
        };

    private static EventLocationRegistrationEffectiveState ApplyOrderCeiling(
        EventLocationRegistrationEffectiveState childState,
        EventLocationRegistrationEffectiveState? orderState)
    {
        if (!HasAudienceAuthority(childState) || orderState is null)
        {
            return childState;
        }

        return orderState == EventLocationRegistrationEffectiveState.Confirmed
            ? childState
            : orderState.Value;
    }

    private static int AuthorityRank(EventLocationRegistrationEffectiveState state) => state switch
    {
        EventLocationRegistrationEffectiveState.Confirmed => 2,
        EventLocationRegistrationEffectiveState.Pending or EventLocationRegistrationEffectiveState.Waitlisted => 1,
        _ => 0
    };

    private static bool HasAudienceAuthority(EventLocationRegistrationEffectiveState state)
        => state is EventLocationRegistrationEffectiveState.Pending
            or EventLocationRegistrationEffectiveState.Waitlisted
            or EventLocationRegistrationEffectiveState.Confirmed;

    private static EventLocationRegistrationEffectiveState ResolveRequestedTerminalState(
        IReadOnlyCollection<ResolvedCoverage> coverage,
        Guid requestedEventLocationId)
        => coverage
            .Where(item => item.Fact.EventLocationId == requestedEventLocationId)
            .Select(item => item.State)
            .OrderByDescending(TerminalEvidenceRank)
            .FirstOrDefault(EventLocationRegistrationEffectiveState.Denied);

    private static int TerminalEvidenceRank(EventLocationRegistrationEffectiveState state) => state switch
    {
        EventLocationRegistrationEffectiveState.NonLive => 4,
        EventLocationRegistrationEffectiveState.Revoked => 3,
        EventLocationRegistrationEffectiveState.Cancelled => 2,
        EventLocationRegistrationEffectiveState.Rejected => 1,
        _ => 0
    };

    private static EventLocationRegistrationAccess NoAccess(
        EventLocationRegistrationAccessRequest request,
        EventLocationRegistrationEffectiveState effectiveState)
        => CreateAccess(
            request.Order.OrderId,
            effectiveState,
            request.Order.EventId,
            [],
            request.RequestedEventLocationId,
            false);

    private static EventLocationRegistrationAccess CreateAccess(
        Guid orderId,
        EventLocationRegistrationEffectiveState effectiveState,
        Guid eventId,
        ImmutableArray<Guid> coveredEventSessionIds,
        Guid requestedEventLocationId,
        bool coversRequestedEventLocation)
        => new(
            orderId,
            effectiveState,
            eventId,
            coversWholeEvent: false,
            coveredEventDayId: null,
            coveredEventSessionIds,
            requestedEventLocationId,
            coversRequestedEventLocation);

    private sealed record ResolvedCoverage(
        EventLocationRegistrationCoverageFact Fact,
        EventLocationRegistrationEffectiveState State);

    private sealed record LoadedOrderSource(
        EventLocationRegistrationOrderFact Order,
        ImmutableArray<EventLocationRegistrationCoverageFact> Coverage);
}
