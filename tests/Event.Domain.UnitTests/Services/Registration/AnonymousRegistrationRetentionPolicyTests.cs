using System.Security.Cryptography;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Domain.ValueObjects;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class AnonymousRegistrationRetentionPolicyTests
{
    private static readonly DateTime Created = new(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Bound = new(2027, 1, 8, 14, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(0, 1)]
    [Arguments(7, 8)]
    [Arguments(30, 31)]
    public async Task FiniteEventEndResolvesInclusiveConfiguredRange(int days, int day)
    {
        var actual = AnonymousRegistrationRetentionPolicy.ResolveInitialDeadline(
            new DateTimeOffset(2027, 1, 1, 14, 0, 0, TimeSpan.Zero), days);
        await Assert.That(actual).IsEqualTo(new DateTime(2027, 1, day, 14, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    [Arguments(-1)]
    [Arguments(31)]
    public async Task OutOfRangeConfigurationCannotResolve(int days) =>
        await Assert.That(() => AnonymousRegistrationRetentionPolicy.ResolveInitialDeadline(
            new DateTimeOffset(Bound), days)).Throws<ArgumentOutOfRangeException>();

    [Test]
    public async Task MissingFiniteEventEndCannotResolve() =>
        await Assert.That(() => AnonymousRegistrationRetentionPolicy.ResolveInitialDeadline(null, 7))
            .Throws<ArgumentException>();

    [Test]
    [Arguments(IdentityAccessModeEnum.GuestAllowed)]
    [Arguments(IdentityAccessModeEnum.CapabilityTokenAllowed)]
    public async Task ClaimPreservesGuestOriginAndOriginalBound(IdentityAccessModeEnum mode)
    {
        var order = Order(mode: mode);
        order.SetPii(RegistrationOrderPii.Create(order.Id, order.TenantId, "Entry", "claim@example.test", null, null,
            (int)RegistrationRetentionPolicyEnum.StandardOperational, Created, Bound));
        order.TryLinkGuestOrderToAccount(Guid.CreateVersion7(), "claim@example.test");
        await Assert.That(AnonymousRegistrationRetentionPolicy.AppliesTo(order)).IsTrue();
        await Assert.That(order.AnonymousPiiRetentionUntilUtc).IsEqualTo(Bound);
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, Bound)).IsFalse();
    }

    [Test]
    public async Task EmptyAccountPiiIsNotGuestOrigin()
    {
        var order = Order(account: true);
        await Assert.That(order.Pii).IsNull();
        await Assert.That(AnonymousRegistrationRetentionPolicy.AppliesTo(order)).IsFalse();
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, Created, Bound)).IsTrue();
        await Assert.That(AnonymousRegistrationRetentionPolicy.GetDisclosureDeadline(order, Created)).IsNull();
    }

    [Test]
    public async Task HistoricalGuestCannotAcquireFreshAuthority()
    {
        var order = Order(historical: true);
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, Created)).IsFalse();
        await Assert.That(() => AnonymousRegistrationRetentionPolicy.GetDisclosureDeadline(order, null))
            .Throws<InvalidOperationException>();
        await Assert.That(order.AnonymousPiiRetentionUntilUtc).IsNull();
    }

    [Test]
    public async Task HeldRowsHaveIndependentStrictOperationalExpiry()
    {
        var order = Order();
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, Bound.AddTicks(-1))).IsTrue();
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, Bound)).IsFalse();
        await Assert.That(AnonymousRegistrationRetentionPolicy.GetDisclosureDeadline(order, null)).IsEqualTo(Bound);
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, Created.AddDays(1), Created.AddDays(1))).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ParticipantAndPurchaserEditsPreservePhysicalHoldOrEarlierDeadline(bool held)
    {
        int policy = (int)(held ? RegistrationRetentionPolicyEnum.LegalHold : RegistrationRetentionPolicyEnum.StandardOperational);
        DateTime earlierBound = Created.AddDays(2);
        var participant = RegistrationParticipantPii.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Entry", null, null,
            policy, Created, earlierBound);
        var purchaser = RegistrationOrderPii.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), "Entry", null, null, null,
            policy, Created, earlierBound);
        participant.Update("Changed", null, null, (int)RegistrationRetentionPolicyEnum.StandardOperational, Created.AddDays(1), Bound);
        purchaser.Update("Changed", null, null, null, (int)RegistrationRetentionPolicyEnum.StandardOperational, Created.AddDays(1), Bound);
        await Assert.That(participant.RetentionUntil).IsEqualTo(held ? (DateTime?)null : earlierBound);
        await Assert.That(purchaser.RetentionUntil).IsEqualTo(held ? (DateTime?)null : earlierBound);
    }

    [Test]
    public async Task CancellationDoesNotShortenOrExtendBound()
    {
        var order = Order();
        order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, Created);
        order.TransitionTo(RegistrationOrderStatusEnum.Cancelled, Created.AddMinutes(1));
        await Assert.That(order.AnonymousPiiRetentionUntilUtc).IsEqualTo(Bound);
        await Assert.That(AnonymousRegistrationRetentionPolicy.CanDisclose(order, null, Bound.AddTicks(-1))).IsTrue();
    }

    private static RegistrationOrder Order(bool account = false, bool historical = false,
        IdentityAccessModeEnum mode = IdentityAccessModeEnum.GuestAllowed) => RegistrationOrder.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), account ? Guid.CreateVersion7() : null, null,
            BookingPartyTypeEnum.Individual, Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)mode, GuestRecoveryPolicyEnum.EmailOptional),
            null, account ? null : CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            "USD", Created, Created.AddMinutes(15), account || historical ? null : Bound);
}
