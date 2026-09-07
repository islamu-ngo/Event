using Explore.Domain;

namespace Event.Domain.UnitTests.Entities;

public sealed class FairReturnWaitlistInvariantTests
{
    private static readonly DateTime UtcNow =
        new(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc);
    private static readonly string CommerceDigest =
        Convert.ToBase64String(new byte[32]);
    private static readonly string EntitlementDigest =
        Convert.ToBase64String(
            Enumerable.Repeat((byte)1, 32).ToArray());

    [Test]
    public async Task CommercialEquivalenceUsesEveryDimension()
    {
        FairReturnSupplyUnit baseline = Supply();
        await Assert.That(
                baseline.IsCommerciallyEquivalentTo(
                    Supply()))
            .IsTrue();
        Func<FairReturnSupplyUnit>[] changed =
        [
            () => Supply(tenantId: NewId()),
            () => Supply(eventId: NewId()),
            () => Supply(ticketTypeId: NewId()),
            () => Supply(catalogId: NewId()),
            () => Supply(policyId: NewId()),
            () => Supply(currency: "EUR"),
            () => Supply(commerce: EntitlementDigest),
            () => Supply(entitlement: CommerceDigest),
            () => Supply(gross: 10_001),
            () => Supply(funding: 2),
        ];
        foreach (Func<FairReturnSupplyUnit> mutation
                 in changed)
        {
            await Assert.That(
                    baseline
                        .IsCommerciallyEquivalentTo(
                            mutation()))
                .IsFalse();
        }
        await Assert.That(() =>
                baseline.IsCommerciallyEquivalentTo(
                    null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task EntryCommercialEquivalenceUsesEveryDimension()
    {
        EventWaitlistEntry entry = Entry();
        await Assert.That(
                entry.IsCommerciallyEquivalentTo(
                    Supply()))
            .IsTrue();
        FairReturnSupplyUnit[] changed =
        [
            Supply(tenantId: NewId()),
            Supply(eventId: NewId()),
            Supply(ticketTypeId: NewId()),
            Supply(catalogId: NewId()),
            Supply(policyId: NewId()),
            Supply(currency: "EUR"),
            Supply(commerce: EntitlementDigest),
            Supply(entitlement: CommerceDigest),
            Supply(gross: 10_001),
            Supply(funding: 2),
        ];
        foreach (FairReturnSupplyUnit supply
                 in changed)
        {
            await Assert.That(
                    entry.IsCommerciallyEquivalentTo(
                        supply))
                .IsFalse();
        }
        await Assert.That(() =>
                entry.IsCommerciallyEquivalentTo(
                    null!))
            .Throws<ArgumentNullException>();
    }

    [Test]
    public async Task SupplyTransitionsAreMonotonic()
    {
        FairReturnSupplyUnit supply = Supply();
        Guid initialStamp = supply.ConcurrencyStamp;
        await Assert.That(() =>
                supply.Release(UtcNow))
            .Throws<InvalidOperationException>();
        supply.Bind(UtcNow.AddSeconds(1));
        await Assert.That(supply.StatusId)
            .IsEqualTo(
                (int)FairReturnSupplyStatus.Bound);
        await Assert.That(supply.BoundAt)
            .IsEqualTo(UtcNow.AddSeconds(1));
        await Assert.That(supply.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(1));
        await Assert.That(supply.ConcurrencyStamp)
            .IsNotEqualTo(initialStamp);
        Guid boundStamp = supply.ConcurrencyStamp;
        supply.Release(UtcNow.AddSeconds(2));
        await Assert.That(supply.StatusId)
            .IsEqualTo(
                (int)FairReturnSupplyStatus.Available);
        await Assert.That(supply.BoundAt).IsNull();
        await Assert.That(supply.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(2));
        await Assert.That(supply.ConcurrencyStamp)
            .IsNotEqualTo(boundStamp);
        Guid releasedStamp = supply.ConcurrencyStamp;
        supply.Withdraw(UtcNow.AddSeconds(3));
        await Assert.That(supply.StatusId)
            .IsEqualTo(
                (int)FairReturnSupplyStatus.Withdrawn);
        await Assert.That(supply.WithdrawnAt)
            .IsEqualTo(UtcNow.AddSeconds(3));
        await Assert.That(supply.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(3));
        await Assert.That(supply.ConcurrencyStamp)
            .IsNotEqualTo(releasedStamp);
        Guid withdrawnStamp = supply.ConcurrencyStamp;
        supply.Withdraw(UtcNow.AddSeconds(4));
        await Assert.That(supply.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(3));
        await Assert.That(supply.ConcurrencyStamp)
            .IsEqualTo(withdrawnStamp);
        await Assert.That(() =>
                supply.Bind(UtcNow.AddSeconds(4)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task EntryTransitionsRejectInvalidStatesAndTrackFences()
    {
        EventWaitlistEntry entry = Entry();
        await Assert.That(() =>
                entry.Requeue(UtcNow))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                entry.Convert(UtcNow))
            .Throws<InvalidOperationException>();
        Guid queuedStamp = entry.ConcurrencyStamp;
        entry.MarkOffered(UtcNow.AddSeconds(1));
        await Assert.That(entry.StatusId)
            .IsEqualTo(
                (int)EventWaitlistEntryStatus.Offered);
        await Assert.That(entry.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(1));
        await Assert.That(entry.ConcurrencyStamp)
            .IsNotEqualTo(queuedStamp);
        await Assert.That(() =>
                entry.MarkOffered(
                    UtcNow.AddSeconds(2)))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                entry.Withdraw(
                    UtcNow.AddSeconds(2)))
            .Throws<InvalidOperationException>();
        Guid offeredStamp = entry.ConcurrencyStamp;
        entry.Requeue(UtcNow.AddSeconds(3));
        await Assert.That(entry.StatusId)
            .IsEqualTo(
                (int)EventWaitlistEntryStatus.Queued);
        await Assert.That(entry.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(3));
        await Assert.That(entry.ConcurrencyStamp)
            .IsNotEqualTo(offeredStamp);
    }

    [Test]
    public async Task OfferExpiryRequeuesAndReleasesOnce()
    {
        Lifecycle lifecycle = CreateLifecycle();
        await Assert.That(
                lifecycle.Offer.ExpiresAt)
            .IsEqualTo(UtcNow.AddMinutes(15));
        await Assert.That(lifecycle.Offer.Expire(
                lifecycle.Entry,
                lifecycle.Supply,
                UtcNow.AddMinutes(14)))
            .IsFalse();
        Guid activeStamp =
            lifecycle.Offer.ConcurrencyStamp;
        await Assert.That(lifecycle.Offer.Expire(
                lifecycle.Entry,
                lifecycle.Supply,
                UtcNow.AddMinutes(15)))
            .IsTrue();
        await Assert.That(lifecycle.Entry.StatusId)
            .IsEqualTo(
                (int)EventWaitlistEntryStatus.Queued);
        await Assert.That(lifecycle.Supply.StatusId)
            .IsEqualTo(
                (int)FairReturnSupplyStatus.Available);
        await Assert.That(
                lifecycle.Offer.OpenEventWaitlistEntryId)
            .IsNull();
        await Assert.That(lifecycle.Offer.ExpiredAt)
            .IsEqualTo(UtcNow.AddMinutes(15));
        await Assert.That(lifecycle.Offer.UpdatedAt)
            .IsEqualTo(UtcNow.AddMinutes(15));
        await Assert.That(
                lifecycle.Offer.ConcurrencyStamp)
            .IsNotEqualTo(activeStamp);
        await Assert.That(lifecycle.Offer.Expire(
                lifecycle.Entry,
                lifecycle.Supply,
                UtcNow.AddMinutes(16)))
            .IsFalse();
    }

    [Test]
    public async Task OfferFinalizationConvertsExactlyOnce()
    {
        Lifecycle lifecycle = CreateLifecycle();
        Guid activeStamp =
            lifecycle.Offer.ConcurrencyStamp;
        await Assert.That(lifecycle.Offer.Finalize(
                lifecycle.Entry,
                UtcNow.AddMinutes(14)))
            .IsTrue();
        await Assert.That(lifecycle.Entry.StatusId)
            .IsEqualTo(
                (int)EventWaitlistEntryStatus.Converted);
        await Assert.That(
                lifecycle.Entry
                    .OpenRegistrationOrderLineId)
            .IsNull();
        await Assert.That(
                lifecycle.Offer.OpenEventWaitlistEntryId)
            .IsNull();
        await Assert.That(lifecycle.Offer.FinalizedAt)
            .IsEqualTo(UtcNow.AddMinutes(14));
        await Assert.That(lifecycle.Offer.UpdatedAt)
            .IsEqualTo(UtcNow.AddMinutes(14));
        await Assert.That(
                lifecycle.Offer.ConcurrencyStamp)
            .IsNotEqualTo(activeStamp);
        await Assert.That(lifecycle.Offer.Finalize(
                lifecycle.Entry,
                UtcNow.AddMinutes(14)))
            .IsFalse();
    }

    [Test]
    public async Task OfferRejectsExpiryBoundaryAndInactiveFinalize()
    {
        Lifecycle active = CreateLifecycle();
        await Assert.That(() =>
                active.Offer.Finalize(
                    active.Entry,
                    active.Offer.ExpiresAt))
            .Throws<InvalidOperationException>();
        await Assert.That(active.Offer.StatusId)
            .IsEqualTo(
                (int)EventWaitlistOfferStatus.Active);

        Lifecycle expired = CreateLifecycle();
        expired.Offer.Expire(
            expired.Entry,
            expired.Supply,
            expired.Offer.ExpiresAt);
        await Assert.That(() =>
                expired.Offer.Finalize(
                    expired.Entry,
                    expired.Offer.ExpiresAt))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SourceSubstitutionStopsAfterPaymentClaim()
    {
        Lifecycle lifecycle = CreateLifecycle();
        FairReturnSupplyUnit replacement = Supply();
        lifecycle.Binding.SubstituteSource(
            lifecycle.Supply,
            replacement,
            UtcNow.AddMinutes(1));
        await Assert.That(
                lifecycle.Binding
                    .FairReturnSupplyUnitId)
            .IsEqualTo(replacement.Id);
        await Assert.That(lifecycle.Supply.StatusId)
            .IsEqualTo(
                (int)FairReturnSupplyStatus.Withdrawn);
        await Assert.That(replacement.StatusId)
            .IsEqualTo(
                (int)FairReturnSupplyStatus.Bound);
        lifecycle.Binding.ClaimPaymentDispatch(
            UtcNow.AddMinutes(2));
        await Assert.That(() =>
                lifecycle.Binding.SubstituteSource(
                    replacement,
                    Supply(),
                    UtcNow.AddMinutes(3)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task SourceSubstitutionRejectsInvalidAuthority()
    {
        Lifecycle lifecycle = CreateLifecycle();
        await Assert.That(() =>
                lifecycle.Binding.SubstituteSource(
                    null!,
                    Supply(),
                    UtcNow.AddMinutes(1)))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                lifecycle.Binding.SubstituteSource(
                    lifecycle.Supply,
                    null!,
                    UtcNow.AddMinutes(1)))
            .Throws<ArgumentNullException>();
        await Assert.That(() =>
                lifecycle.Binding.SubstituteSource(
                    Supply(),
                    Supply(),
                    UtcNow.AddMinutes(1)))
            .Throws<InvalidOperationException>();
        await Assert.That(() =>
                lifecycle.Binding.SubstituteSource(
                    lifecycle.Supply,
                    Supply(gross: 10_001),
                    UtcNow.AddMinutes(1)))
            .Throws<InvalidOperationException>();
        FairReturnSupplyUnit unavailable = Supply();
        unavailable.Bind(UtcNow);
        await Assert.That(() =>
                lifecycle.Binding.SubstituteSource(
                    lifecycle.Supply,
                    unavailable,
                    UtcNow.AddMinutes(1)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task PaymentClaimIsIdempotent()
    {
        Lifecycle lifecycle = CreateLifecycle();
        lifecycle.Binding.ClaimPaymentDispatch(
            UtcNow.AddMinutes(1));
        Guid claimedStamp =
            lifecycle.Binding.ConcurrencyStamp;
        lifecycle.Binding.ClaimPaymentDispatch(
            UtcNow.AddMinutes(2));
        await Assert.That(
                lifecycle.Binding
                    .PaymentDispatchClaimedAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(lifecycle.Binding.UpdatedAt)
            .IsEqualTo(UtcNow.AddMinutes(1));
        await Assert.That(
                lifecycle.Binding.ConcurrencyStamp)
            .IsEqualTo(claimedStamp);
    }

    [Test]
    public async Task EntryWithdrawalClosesOpenSlot()
    {
        EventWaitlistEntry entry = Entry();
        entry.Withdraw(UtcNow.AddSeconds(1));
        await Assert.That(entry.StatusId)
            .IsEqualTo(
                (int)EventWaitlistEntryStatus.Withdrawn);
        await Assert.That(
                entry.OpenRegistrationOrderLineId)
            .IsNull();
        entry.Withdraw(UtcNow.AddSeconds(2));
        await Assert.That(entry.UpdatedAt)
            .IsEqualTo(UtcNow.AddSeconds(1));
    }

    private static Lifecycle CreateLifecycle()
    {
        FairReturnSupplyPolicy policy =
            FairReturnSupplyPolicy.Create(
                NewId(),
                TenantId,
                EventId,
                CatalogId,
                TicketTypeId,
                true,
                15,
                UtcNow);
        EventWaitlistEntry entry = Entry();
        FairReturnSupplyUnit supply = Supply();
        Guid bindingId = NewId();
        EventWaitlistOffer offer =
            EventWaitlistOffer.Create(
                NewId(),
                policy,
                entry,
                supply,
                bindingId,
                NewId(),
                UtcNow);
        FairReturnSourceBinding binding =
            FairReturnSourceBinding.Create(
                bindingId,
                supply,
                entry,
                UtcNow);
        return new Lifecycle(
            entry,
            supply,
            offer,
            binding);
    }

    private static EventWaitlistEntry Entry() =>
        EventWaitlistEntry.Enqueue(
            NewId(),
            TenantId,
            EventId,
            TicketTypeId,
            CatalogId,
            PolicyId,
            NewId(),
            NewId(),
            NewId(),
            NewId(),
            "USD",
            CommerceDigest,
            EntitlementDigest,
            10_000,
            1,
            0,
            UtcNow);

    private static FairReturnSupplyUnit Supply(
        Guid? tenantId = null,
        Guid? eventId = null,
        Guid? ticketTypeId = null,
        Guid? catalogId = null,
        Guid? policyId = null,
        string currency = "USD",
        string? commerce = null,
        string? entitlement = null,
        long gross = 10_000,
        int funding = 1) =>
        FairReturnSupplyUnit.Create(
            NewId(),
            tenantId ?? TenantId,
            eventId ?? EventId,
            ticketTypeId ?? TicketTypeId,
            catalogId ?? CatalogId,
            policyId ?? PolicyId,
            currency,
            commerce ?? CommerceDigest,
            entitlement ?? EntitlementDigest,
            gross,
            funding,
            NewId(),
            UtcNow);

    private static readonly Guid TenantId = NewId();
    private static readonly Guid EventId = NewId();
    private static readonly Guid TicketTypeId = NewId();
    private static readonly Guid CatalogId = NewId();
    private static readonly Guid PolicyId = NewId();

    private static Guid NewId() =>
        Guid.CreateVersion7();

    private sealed record Lifecycle(
        EventWaitlistEntry Entry,
        FairReturnSupplyUnit Supply,
        EventWaitlistOffer Offer,
        FairReturnSourceBinding Binding);
}
