using System.Security.Cryptography;
using Explore.Domain;

namespace Event.Domain.UnitTests;

public sealed class PrivacyErasureRecoveryStateTests
{
    [Test]
    public async Task StoreIdentityIsWriterIssuedAndSurvivesOrdinaryMutations()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        PrivacyErasureRecoveryState initial = counter.GetRecoveryState();
        counter.AllocateNext();
        counter.AdvanceRetainedFloorTo(1);

        await Assert.That(initial.StoreId.Version).IsEqualTo(7);
        await Assert.That(initial.StoreId).IsNotEqualTo(PrivacyErasureCounter.Start().StoreId);
        await Assert.That(counter.StoreId).IsEqualTo(initial.StoreId);
        await Assert.That(counter.RecoveryGeneration).IsEqualTo(1);
        await Assert.That(counter.Revision).IsGreaterThan(initial.Revision);
        await Assert.That(counter.GetRecoveryState().AuthorityState)
            .IsEqualTo(new PrivacyErasureAuthorityState(1, 1));
    }

    [Test]
    public async Task KeyBindingAdvancesRevisionOnceAndMismatchCannotMutateCoordinates()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        string tag = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        long initial = counter.Revision;
        counter.BindIdentityKey("recovery-key", tag);
        PrivacyErasureRecoveryState bound = counter.GetRecoveryState();
        counter.BindIdentityKey("recovery-key", tag);

        await Assert.That(bound.Revision).IsEqualTo(initial + 1);
        await Assert.That(counter.GetRecoveryState()).IsEqualTo(bound);
        await Assert.That(() => counter.BindIdentityKey("different-key", tag))
            .Throws<InvalidOperationException>();
        await Assert.That(counter.GetRecoveryState()).IsEqualTo(bound);
    }

    [Test]
    public async Task DeletingReleasedHoldAdvancesRevisionWithoutMovingFloor()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        counter.AllocateNext();
        counter.RecordCompaction(1);
        PrivacyErasureRecoveryState held = counter.GetRecoveryState();
        counter.RecordCompaction(1);

        await Assert.That(counter.Revision).IsEqualTo(held.Revision + 1);
        await Assert.That(counter.GetState()).IsEqualTo(held.AuthorityState);
        await Assert.That(counter.RecoveryGeneration).IsEqualTo(held.RecoveryGeneration);
    }

    [Test]
    public async Task RecoveryTransitionRequiresExactOwnedTupleAndNeverChangesStoreIdentity()
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        PrivacyErasureRecoveryState stale = counter.GetRecoveryState();
        counter.AllocateNext();
        PrivacyErasureRecoveryState current = counter.GetRecoveryState();
        await Assert.That(() => counter.BeginRecovery(stale)).Throws<InvalidOperationException>();
        await Assert.That(() => counter.BeginRecovery(PrivacyErasureCounter.Start().GetRecoveryState()))
            .Throws<InvalidOperationException>();
        await Assert.That(counter.GetRecoveryState()).IsEqualTo(current);

        counter.BeginRecovery(current);

        await Assert.That(counter.RecoveryGeneration).IsEqualTo(2);
        await Assert.That(counter.Revision).IsEqualTo(current.Revision + 1);
        await Assert.That(counter.StoreId).IsEqualTo(current.StoreId);
        await Assert.That(counter.GetState()).IsEqualTo(current.AuthorityState);
        await Assert.That(() => counter.BeginRecovery(current)).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments("append")]
    [Arguments("floor")]
    [Arguments("key")]
    [Arguments("recovery")]
    public async Task ExhaustedRevisionRejectsMutationBeforeChangingAnyCoordinate(string operation)
    {
        PrivacyErasureCounter counter = PrivacyErasureCounter.Start();
        counter.AllocateNext();
        typeof(PrivacyErasureCounter).GetProperty(nameof(PrivacyErasureCounter.Revision))!
            .SetValue(counter, long.MaxValue);
        PrivacyErasureRecoveryState initial = counter.GetRecoveryState();
        string tag = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

        await Assert.That(() =>
        {
            switch (operation)
            {
                case "append": counter.AllocateNext(); break;
                case "floor": counter.RecordCompaction(1); break;
                case "key": counter.BindIdentityKey("recovery-key", tag); break;
                case "recovery": counter.BeginRecovery(initial); break;
            }
        }).Throws<InvalidOperationException>();
        await Assert.That(counter.GetRecoveryState()).IsEqualTo(initial);
    }

    [Test]
    public async Task RecoveryStateRejectsUnissuedIdentityAndNonpositiveCoordinates()
    {
        var state = new PrivacyErasureAuthorityState(0, 0);
        await Assert.That(() => new PrivacyErasureRecoveryState(Guid.Empty, 1, 1, state))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyErasureRecoveryState(Guid.NewGuid(), 1, 1, state))
            .Throws<ArgumentException>();
        await Assert.That(() => new PrivacyErasureRecoveryState(Guid.CreateVersion7(), 0, 1, state))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => new PrivacyErasureRecoveryState(Guid.CreateVersion7(), 1, 0, state))
            .Throws<ArgumentOutOfRangeException>();
    }
}
