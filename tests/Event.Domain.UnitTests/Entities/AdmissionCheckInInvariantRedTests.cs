using System.Reflection;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Event.Domain.UnitTests.Entities;

public sealed class AdmissionCheckInInvariantRedTests
{
    private static readonly DateTime IssuedAt = new(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime OpensAt = new(2026, 8, 26, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ClosesAt = new(2026, 8, 26, 18, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task TargetsPoliciesAndState_CarryCanonicalScopeTenantAndConcurrencyState()
    {
        AdmissionFixture fixture = CreateAdmission();
        Guid dayId = Guid.CreateVersion7();
        Guid sessionId = Guid.CreateVersion7();
        AdmissionTarget eventTarget = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionTarget dayTarget = Target(fixture, AdmissionTargetTypeEnum.EventDay, dayId);
        AdmissionTarget sessionTarget = Target(fixture, AdmissionTargetTypeEnum.EventSession, sessionId: sessionId);
        AdmissionCheckInPolicy policy = Policy(sessionTarget, 1);
        AdmissionCheckInState state = AdmissionCheckInState.Create(
            Guid.CreateVersion7(), fixture.Ticket, sessionTarget);

        await Assert.That(eventTarget.ScopeId).IsEqualTo(fixture.Ticket.EventId);
        await Assert.That(dayTarget.ScopeId).IsEqualTo(dayId);
        await Assert.That(sessionTarget.ScopeId).IsEqualTo(sessionId);
        await Assert.That(eventTarget).IsAssignableTo<ITenantEntity>();
        await Assert.That(policy).IsAssignableTo<ITenantEntity>();
        await Assert.That(state).IsAssignableTo<ITenantEntity>();
        await Assert.That(eventTarget.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(policy.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(state.ConcurrencyStamp.Version).IsEqualTo(7);
        await Assert.That(state.TenantId).IsEqualTo(fixture.Ticket.TenantId);
        await Assert.That(state.AdmissionTicketId).IsEqualTo(fixture.Ticket.Id);
        await Assert.That(state.AdmissionTargetId).IsEqualTo(sessionTarget.Id);
    }

    [Test]
    public async Task State_IsRehydratableProjectionWithoutBooleanOrEventCollection()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        Guid stateId = Guid.CreateVersion7();
        Guid activeEventId = Guid.CreateVersion7();
        Guid stamp = Guid.CreateVersion7();
        AdmissionCheckInState state = AdmissionCheckInState.Rehydrate(
            stateId,
            fixture.Ticket.TenantId,
            fixture.Ticket.Id,
            target.Id,
            activeEventId,
            entryCount: 2,
            lastSequence: 3,
            stamp);

        await Assert.That(state.Id).IsEqualTo(stateId);
        await Assert.That(state.ActiveCheckInEventId).IsEqualTo(activeEventId);
        await Assert.That(state.EntryCount).IsEqualTo(2);
        await Assert.That(state.LastSequence).IsEqualTo(3);
        await Assert.That(state.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(typeof(AdmissionCheckInState).GetProperties()
            .Any(property => property.PropertyType == typeof(bool) ||
                property.Name.Contains("Events", StringComparison.OrdinalIgnoreCase))).IsFalse();
        await Assert.That(typeof(AdmissionTicket).Assembly.GetType("Explore.Domain.AdmissionCheckInLedger")).IsNull();
    }

    [Test]
    public async Task CheckIn_RejectsTargetOutsideExactPublishedEntitlementWithoutFact()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.EventDay, Guid.CreateVersion7());
        AdmissionCheckInState state = State(fixture, target);

        AdmissionCheckInDecision decision = Decide(
            fixture, target, Policy(target, 1), state, AdmissionCheckInActionEnum.CheckIn, OpensAt);

        await Assert.That(decision.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.NotEntitled);
        await Assert.That(decision.Event).IsNull();
        await Assert.That(decision.NextState).IsSameReferenceAs(state);
    }

    [Test]
    public async Task CheckIn_WindowIsInclusiveAndEarlyLateAttemptsDoNotAdvanceProjection()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 1);

        AdmissionCheckInDecision early = Decide(
            fixture, target, policy, State(fixture, target), AdmissionCheckInActionEnum.CheckIn, OpensAt.AddTicks(-1));
        AdmissionCheckInDecision late = Decide(
            fixture, target, policy, State(fixture, target), AdmissionCheckInActionEnum.CheckIn, ClosesAt.AddTicks(1));
        AdmissionCheckInDecision opening = Decide(
            fixture, target, policy, State(fixture, target), AdmissionCheckInActionEnum.CheckIn, OpensAt);
        AdmissionCheckInDecision closing = Decide(
            fixture, target, policy, State(fixture, target), AdmissionCheckInActionEnum.CheckIn, ClosesAt,
            actorId: null, scannerId: Guid.CreateVersion7());

        await Assert.That(early.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.TooEarly);
        await Assert.That(late.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.TooLate);
        await Assert.That(early.Event).IsNull();
        await Assert.That(late.Event).IsNull();
        await Assert.That(opening.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.CheckedIn);
        await Assert.That(closing.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.CheckedIn);
    }

    [Test]
    public async Task SingleEntry_DuplicateUndoAndReentryUseOnlyCurrentProjection()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 1);
        AdmissionCheckInState initial = State(fixture, target);

        AdmissionCheckInDecision first = Decide(
            fixture, target, policy, initial, AdmissionCheckInActionEnum.CheckIn, OpensAt.AddMinutes(1));
        AdmissionCheckInDecision duplicate = Decide(
            fixture, target, policy, first.NextState, AdmissionCheckInActionEnum.CheckIn, OpensAt.AddMinutes(2));
        AdmissionCheckInDecision undo = Decide(
            fixture, target, policy, first.NextState, AdmissionCheckInActionEnum.Undo, OpensAt.AddMinutes(3),
            reasonCode: AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection);
        AdmissionCheckInDecision reentry = Decide(
            fixture, target, policy, undo.NextState, AdmissionCheckInActionEnum.CheckIn, OpensAt.AddMinutes(4));

        await Assert.That(first.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.CheckedIn);
        await Assert.That(duplicate.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.AlreadyCheckedIn);
        await Assert.That(undo.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.Undone);
        await Assert.That(reentry.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.ReEntryNotAllowed);
        await Assert.That(initial.EntryCount).IsEqualTo(0);
        await Assert.That(initial.LastSequence).IsEqualTo(0);
        await Assert.That(undo.NextState.ActiveCheckInEventId).IsNull();
        await Assert.That(undo.NextState.EntryCount).IsEqualTo(1);
        await Assert.That(undo.NextState.LastSequence).IsEqualTo(2);
    }

    [Test]
    public async Task ConfiguredReEntry_AppendsOrderedFactsAfterCompensatingUndo()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 2);
        Guid scannerId = Guid.CreateVersion7();

        AdmissionCheckInDecision first = Decide(
            fixture, target, policy, State(fixture, target), AdmissionCheckInActionEnum.CheckIn,
            OpensAt.AddMinutes(1), actorId: null, scannerId: scannerId);
        AdmissionCheckInDecision undo = Decide(
            fixture, target, policy, first.NextState, AdmissionCheckInActionEnum.Undo,
            OpensAt.AddMinutes(2), actorId: null, scannerId: scannerId,
            reasonCode: AdmissionCheckInUndoReasonCodeEnum.WrongTarget);
        AdmissionCheckInDecision second = Decide(
            fixture, target, policy, undo.NextState, AdmissionCheckInActionEnum.CheckIn,
            OpensAt.AddMinutes(3), actorId: null, scannerId: scannerId);

        await Assert.That(second.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.ReEntered);
        await Assert.That(first.Event!.Sequence).IsEqualTo(1);
        await Assert.That(undo.Event!.Sequence).IsEqualTo(2);
        await Assert.That(second.Event!.Sequence).IsEqualTo(3);
        await Assert.That(second.NextState.EntryCount).IsEqualTo(2);
        await Assert.That(second.NextState.ActiveCheckInEventId).IsEqualTo(second.Event.Id);
        await Assert.That(new[] { first.Event, undo.Event, second.Event }.All(fact =>
            fact!.ScannerCapabilityId == scannerId)).IsTrue();
    }

    [Test]
    public async Task Undo_RequiresActiveProjectionAndClosedReasonCode()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 2);
        AdmissionCheckInState initial = State(fixture, target);
        Guid actorId = Guid.CreateVersion7();

        AdmissionCheckInDecision inactive = Decide(
            fixture, target, policy, initial, AdmissionCheckInActionEnum.Undo, OpensAt,
            actorId: actorId,
            reasonCode: AdmissionCheckInUndoReasonCodeEnum.OperatorCorrection);
        AdmissionCheckInDecision active = Decide(
            fixture, target, policy, initial, AdmissionCheckInActionEnum.CheckIn, OpensAt, actorId: actorId);

        await Assert.That(inactive.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.NotCheckedIn);
        await Assert.That(inactive.Event).IsNull();
        await Assert.That(() => Decide(
                fixture, target, policy, active.NextState, AdmissionCheckInActionEnum.Undo,
                OpensAt.AddMinutes(1), actorId: actorId))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => Decide(
                fixture, target, policy, active.NextState, AdmissionCheckInActionEnum.Undo,
                OpensAt.AddMinutes(1), actorId: actorId,
                reasonCode: (AdmissionCheckInUndoReasonCodeEnum)999))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task Facts_AreImmutableAndCaptureOrderedAuthorityReasonAndUtcTime()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 2);
        Guid actorId = Guid.CreateVersion7();
        DateTime checkedInAt = OpensAt.AddMinutes(10);
        DateTime undoneAt = checkedInAt.AddMinutes(1);

        AdmissionCheckInDecision checkIn = Decide(
            fixture, target, policy, State(fixture, target), AdmissionCheckInActionEnum.CheckIn,
            checkedInAt, actorId: actorId);
        AdmissionCheckInDecision undo = Decide(
            fixture, target, policy, checkIn.NextState, AdmissionCheckInActionEnum.Undo,
            undoneAt, actorId: actorId,
            reasonCode: AdmissionCheckInUndoReasonCodeEnum.DuplicateScan);

        AdmissionCheckInEvent checkInFact = checkIn.Event!;
        AdmissionCheckInEvent undoFact = undo.Event!;
        await Assert.That(checkInFact.AdmissionTicketId).IsEqualTo(fixture.Ticket.Id);
        await Assert.That(checkInFact.AdmissionTargetId).IsEqualTo(target.Id);
        await Assert.That(checkInFact.TenantId).IsEqualTo(fixture.Ticket.TenantId);
        await Assert.That(checkInFact.Sequence).IsEqualTo(1);
        await Assert.That(checkInFact.OccurredAtUtc).IsEqualTo(checkedInAt);
        await Assert.That(checkInFact.AdmissionCheckInUndoReasonCodeId).IsNull();
        await Assert.That(checkInFact.CompensatedCheckInEventId).IsNull();
        await Assert.That(undoFact.Sequence).IsEqualTo(2);
        await Assert.That(undoFact.OccurredAtUtc).IsEqualTo(undoneAt);
        await Assert.That(undoFact.AdmissionCheckInUndoReasonCodeId)
            .IsEqualTo((int)AdmissionCheckInUndoReasonCodeEnum.DuplicateScan);
        await Assert.That(undoFact.CompensatedCheckInEventId).IsEqualTo(checkInFact.Id);
        await Assert.That(undoFact.ActorId).IsEqualTo(actorId);
        await Assert.That(undoFact.Id.Version).IsEqualTo(7);
        await Assert.That(typeof(AdmissionCheckInEvent).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => !method.IsSpecialName && method.DeclaringType == typeof(AdmissionCheckInEvent))).IsEmpty();
        await Assert.That(typeof(AdmissionCheckInEvent).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.SetMethod?.IsPublic == true)).IsEmpty();
    }

    [Test]
    public async Task Rules_ArePureAndSameInputProducesSameDecision()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 2);
        AdmissionCheckInState state = State(fixture, target);
        Guid eventId = Guid.CreateVersion7();
        Guid actorId = Guid.CreateVersion7();

        AdmissionCheckInDecision first = AdmissionCheckInRules.Decide(
            fixture.Ticket, target, fixture.EventEntitlement, policy, state,
            AdmissionCheckInActionEnum.CheckIn, eventId, actorId, null, null, OpensAt);
        AdmissionCheckInDecision replay = AdmissionCheckInRules.Decide(
            fixture.Ticket, target, fixture.EventEntitlement, policy, state,
            AdmissionCheckInActionEnum.CheckIn, eventId, actorId, null, null, OpensAt);

        await Assert.That(state.ActiveCheckInEventId).IsNull();
        await Assert.That(state.EntryCount).IsEqualTo(0);
        await Assert.That(state.LastSequence).IsEqualTo(0);
        await Assert.That(replay.ResultCode).IsEqualTo(first.ResultCode);
        await Assert.That(replay.Event!.Id).IsEqualTo(first.Event!.Id);
        await Assert.That(replay.Event.Sequence).IsEqualTo(first.Event.Sequence);
        await Assert.That(replay.NextState.ActiveCheckInEventId).IsEqualTo(first.NextState.ActiveCheckInEventId);
    }

    [Test]
    public async Task ResultCodes_AreStableAndExhaustiveForEveryDomainDecision()
    {
        await Assert.That(Enum.GetValues<AdmissionCheckInResultCodeEnum>().ToDictionary(value => value.ToString(), value => (int)value))
            .IsEquivalentTo(new Dictionary<string, int>
            {
                ["CheckedIn"] = 1,
                ["ReEntered"] = 2,
                ["AlreadyCheckedIn"] = 3,
                ["Undone"] = 4,
                ["NotCheckedIn"] = 5,
                ["NotEntitled"] = 6,
                ["TooEarly"] = 7,
                ["TooLate"] = 8,
                ["ReEntryNotAllowed"] = 9,
                ["CheckInNotFound"] = 10,
                ["AdmissionStopped"] = 11
            });
    }

    [Test]
    public async Task StoppedTargetRejectsAdmissionUntilExplicitRestore()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, maximumEntries: 1);
        AdmissionCheckInState state = State(fixture, target);
        Guid actorId = Guid.CreateVersion7();
        target.Stop();

        AdmissionCheckInDecision stopped = Decide(
            fixture,
            target,
            policy,
            state,
            AdmissionCheckInActionEnum.CheckIn,
            OpensAt,
            actorId);
        target.Restore();
        AdmissionCheckInDecision restored = Decide(
            fixture,
            target,
            policy,
            state,
            AdmissionCheckInActionEnum.CheckIn,
            OpensAt,
            actorId);

        await Assert.That(stopped.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.AdmissionStopped);
        await Assert.That(stopped.Event).IsNull();
        await Assert.That(restored.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.CheckedIn);
        await Assert.That(restored.Event).IsNotNull();
    }

    [Test]
    public async Task Contract_RejectsMalformedIdentityTimeAuthorityAndCrossScopeState()
    {
        AdmissionFixture fixture = CreateAdmission();
        AdmissionTarget target = Target(fixture, AdmissionTargetTypeEnum.Event);
        AdmissionCheckInPolicy policy = Policy(target, 1);
        AdmissionCheckInState state = State(fixture, target);

        await Assert.That(() => AdmissionCheckInState.Rehydrate(
                Guid.NewGuid(), fixture.Ticket.TenantId, fixture.Ticket.Id, target.Id,
                null, 0, 0, Guid.CreateVersion7()))
            .Throws<ArgumentException>();
        await Assert.That(() => AdmissionCheckInState.Rehydrate(
                Guid.CreateVersion7(), fixture.Ticket.TenantId, fixture.Ticket.Id, target.Id,
                Guid.CreateVersion7(), 0, 0, Guid.CreateVersion7()))
            .Throws<ArgumentException>();
        await Assert.That(() => Decide(
                fixture, target, policy, state, AdmissionCheckInActionEnum.CheckIn,
                DateTime.SpecifyKind(OpensAt, DateTimeKind.Unspecified)))
            .Throws<ArgumentException>();
        await Assert.That(() => Decide(
                fixture, target, policy, state, AdmissionCheckInActionEnum.CheckIn,
                OpensAt, actorId: Guid.NewGuid()))
            .Throws<ArgumentException>();
        await Assert.That(() => Decide(
                fixture, target, policy, state, AdmissionCheckInActionEnum.CheckIn,
                OpensAt, actorId: Guid.CreateVersion7(), scannerId: Guid.CreateVersion7()))
            .Throws<ArgumentException>();

        AdmissionTarget otherTarget = Target(fixture, AdmissionTargetTypeEnum.Event);
        await Assert.That(() => Decide(
                fixture, otherTarget, Policy(otherTarget, 1), state,
                AdmissionCheckInActionEnum.CheckIn, OpensAt))
            .Throws<ArgumentException>();
    }

    private static AdmissionCheckInDecision Decide(
        AdmissionFixture fixture,
        AdmissionTarget target,
        AdmissionCheckInPolicy policy,
        AdmissionCheckInState state,
        AdmissionCheckInActionEnum action,
        DateTime occurredAtUtc,
        Guid? actorId = null,
        Guid? scannerId = null,
        AdmissionCheckInUndoReasonCodeEnum? reasonCode = null) => AdmissionCheckInRules.Decide(
            fixture.Ticket,
            target,
            fixture.EventEntitlement,
            policy,
            state,
            action,
            Guid.CreateVersion7(),
            actorId ?? (scannerId is null ? Guid.CreateVersion7() : null),
            scannerId,
            reasonCode,
            occurredAtUtc,
            action == AdmissionCheckInActionEnum.Undo ? state.ActiveCheckInEventId : null);

    private static AdmissionTarget Target(
        AdmissionFixture fixture,
        AdmissionTargetTypeEnum type,
        Guid? dayId = null,
        Guid? sessionId = null) => AdmissionTarget.Create(
            Guid.CreateVersion7(),
            fixture.Ticket.TenantId,
            fixture.Ticket.EventId,
            type,
            dayId,
            sessionId);

    private static AdmissionCheckInPolicy Policy(AdmissionTarget target, int maximumEntries) =>
        AdmissionCheckInPolicy.Create(Guid.CreateVersion7(), target, OpensAt, ClosesAt, maximumEntries);

    private static AdmissionCheckInState State(AdmissionFixture fixture, AdmissionTarget target) =>
        AdmissionCheckInState.Create(Guid.CreateVersion7(), fixture.Ticket, target);

    private static AdmissionFixture CreateAdmission()
    {
        AdmissionTicketTestAuthority authority = AdmissionTicketTestAuthority.Create(IssuedAt);
        AdmissionTicket ticket = AdmissionTicket.Issue(
            authority.Order,
            authority.OrderLine,
            authority.Assignment,
            authority.Participant,
            authority.Catalog,
            authority.TicketType,
            Guid.CreateVersion7(),
            "TKT-CHECK-IN",
            Guid.CreateVersion7(),
            1,
            1,
            Convert.ToBase64String(new byte[32]),
            IssuedAt);
        return new(ticket, authority.TicketType.Entitlements.Single());
    }

    private sealed record AdmissionFixture(AdmissionTicket Ticket, TicketTypeEntitlement EventEntitlement);
}
