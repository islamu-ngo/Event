// ABOUTME: Proves explicit anonymous cancellation cannot widen generic terminal or active-only transitions.
// ABOUTME: Rejects paid, attended, and non-anonymous authority using pinned facts rather than missing PII.

using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class AnonymousCancellationRulesTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(IdentityAccessModeEnum.GuestAllowed)]
    [Arguments(IdentityAccessModeEnum.CapabilityTokenAllowed)]
    public async Task ExplicitCancellationPreservesConfirmationAndGenericTerminalBoundary(IdentityAccessModeEnum profile)
    {
        var order = CreateOrder(profile: profile);
        await Assert.That(order.CanTransitionTo(RegistrationOrderStatusEnum.Cancelled)).IsFalse();
        await Assert.That(order.TryCancelConfirmedAnonymous(new(false, false, false, false), Now)).IsTrue();
        await Assert.That(order.ConfirmedAt).IsEqualTo(Now.AddMinutes(-1));
        await Assert.That(order.CancelledAt).IsEqualTo(Now);
        await Assert.That(order.UpdatedAt).IsEqualTo(Now);
        await Assert.That(order.ConcurrencyStamp).IsNotEqualTo(Guid.Empty);
        await Assert.That(order.TryCancelConfirmedAnonymous(new(false, false, false, false), Now.AddMinutes(1))).IsFalse();
        await Assert.That(order.CancelledAt).IsEqualTo(Now);
    }

    [Test]
    [Arguments(true, false, false, false)]
    [Arguments(false, true, false, false)]
    [Arguments(false, false, true, false)]
    [Arguments(false, false, false, true)]
    public async Task AnyExactPaidOrAttendanceEvidenceDenies(bool acceptance, bool attempt, bool success, bool attendance)
    {
        var order = CreateOrder();
        await Assert.That(order.TryCancelConfirmedAnonymous(new(acceptance, attempt, success, attendance), Now)).IsFalse();
        await Assert.That(order.CancelledAt).IsNull();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    public async Task AccountOrActorIsNotAnonymous(bool account, bool actor)
    {
        var order = CreateOrder(account, actor);
        await Assert.That(AnonymousCancellationRules.IsEligible(order, new(false, false, false, false))).IsFalse();
    }

    [Test]
    public async Task ConsumedReleaseIsExplicitConditionalAndRetainsAllocationFacts()
    {
        var order = CreateOrder();
        var hold = RegistrationInventoryHold.Create(order.Id, Guid.CreateVersion7(), Guid.CreateVersion7(),
            order.TenantId, 3, Now.AddMinutes(-2), Now.AddMinutes(5));
        await Assert.That(hold.TryConsume(Now.AddMinutes(-1))).IsTrue();
        await Assert.That(hold.TryRelease(Now)).IsFalse();
        await Assert.That(hold.TryReleaseConsumedForAnonymousCancellation(order, Now)).IsFalse();
        order.TryCancelConfirmedAnonymous(new(false, false, false, false), Now);
        await Assert.That(hold.TryReleaseConsumedForAnonymousCancellation(order, Now)).IsTrue();
        var stamp = hold.ConcurrencyStamp;
        await Assert.That(hold.TryReleaseConsumedForAnonymousCancellation(order, Now.AddSeconds(1))).IsFalse();
        await Assert.That(hold.Quantity).IsEqualTo(3);
        await Assert.That(hold.ConsumedAt).IsEqualTo(Now.AddMinutes(-1));
        await Assert.That(hold.ReleasedAt).IsEqualTo(Now);
        await Assert.That(hold.UpdatedAt).IsEqualTo(Now);
        await Assert.That(hold.ConcurrencyStamp).IsEqualTo(stamp);
    }

    private static RegistrationOrder CreateOrder(bool account = false, bool actor = false,
        IdentityAccessModeEnum profile = IdentityAccessModeEnum.GuestAllowed)
    {
        var tenantId = Guid.CreateVersion7();
        var eventId = Guid.CreateVersion7();
        var catalog = EventTicketCatalogVersion.Create(tenantId, eventId, "USD", 1);
        var order = RegistrationOrder.Create(tenantId, eventId,
            account ? Guid.CreateVersion7() : null, actor ? Guid.CreateVersion7() : null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(),
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)profile, GuestRecoveryPolicyEnum.EmailOptional),
            null, CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])), "USD", Now.AddMinutes(-2), Now.AddMinutes(5));
        var ticket = EventTicketType.Create(Guid.CreateVersion7(), order.TenantId, order.TicketCatalogVersionId,
            "Free", "USD", TicketPricingModeEnum.Free, null, null, null, ParticipantDataCollectionModeEnum.None,
            null, null, null, false, false, null, null, null, null);
        catalog.AddTicketType(ticket, null);
        catalog.AddEntitlement(ticket, TicketTypeEntitlement.CreateForEvent(ticket.Id, tenantId, eventId, 1));
        catalog.Publish();
        order.AddLine(RegistrationOrderLine.Create(catalog, ticket, order.Id, 1, null, null));
        order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Now.AddMinutes(-1));
        order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, Now.AddMinutes(-1));
        order.TransitionTo(RegistrationOrderStatusEnum.Confirmed, Now.AddMinutes(-1));
        return order;
    }
}
