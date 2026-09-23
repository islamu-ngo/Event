using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests.Entities;

public sealed class StorageObjectDeletionTombstoneTests
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ProducerUncertaintyNeverExpiresIntoDeletionAuthority()
    {
        var pending = Create(producerSettled: false);
        await Assert.That(pending.State).IsEqualTo(StorageObjectDeletionState.AwaitingProducer);
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, Guid.CreateVersion7(),
            Now.AddYears(1), Now.AddYears(1).AddMinutes(1))).IsFalse();
        await Assert.That(pending.TrySettleProducer(null, Now)).IsTrue();
        await Assert.That(pending.State).IsEqualTo(StorageObjectDeletionState.Ready);
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, Guid.CreateVersion7(), Now, Now.AddMinutes(1))).IsTrue();
    }

    [Test]
    public async Task ReclaimedLeaseRejectsTheOldWorkersCompletionAndRetry()
    {
        var pending = Create(producerSettled: true);
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, first, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(pending.TryClaim(first, second, Now.AddSeconds(59), Now.AddMinutes(2))).IsFalse();
        await Assert.That(pending.TryClaim(first, second, Now.AddMinutes(1), Now.AddMinutes(2))).IsTrue();
        await Assert.That(pending.TryRecordAbsence(first, Now.AddMinutes(1))).IsFalse();
        await Assert.That(pending.TryScheduleRetry(first, Now.AddMinutes(1), Now.AddMinutes(3))).IsFalse();
        await Assert.That(pending.TryRecordAbsence(second, Now.AddMinutes(1))).IsTrue();
        await Assert.That(pending.State).IsEqualTo(StorageObjectDeletionState.Absent);
    }

    [Test]
    public async Task ExpiredClaimCannotDeclareAbsenceAndTerminalStateCannotBeReopened()
    {
        var pending = Create(producerSettled: true);
        Guid first = Guid.CreateVersion7(), second = Guid.CreateVersion7();
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, first, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(pending.TryRecordAbsence(first, Now.AddMinutes(1))).IsFalse();
        await Assert.That(pending.TryClaim(first, second, Now.AddMinutes(1), Now.AddMinutes(2))).IsTrue();
        await Assert.That(pending.TryRecordAbsence(second, Now.AddMinutes(1))).IsTrue();
        await Assert.That(pending.TrySettleProducer(null, Now.AddMinutes(2))).IsFalse();
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, Guid.CreateVersion7(),
            Now.AddMinutes(3), Now.AddMinutes(4))).IsFalse();
    }

    [Test]
    public async Task RetryPreservesPhysicalIdentityAndCannotRunBeforeItsDueTime()
    {
        var pending = Create(producerSettled: true);
        var identity = (pending.Id, pending.TenantId, pending.ProviderBindingId, pending.Provider,
            pending.ObjectKey, pending.ProviderObjectVersion);
        Guid first = Guid.CreateVersion7();
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, first, Now, Now.AddMinutes(1))).IsTrue();
        await Assert.That(pending.TryScheduleRetry(first, Now.AddSeconds(1), Now.AddMinutes(5))).IsTrue();
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, Guid.CreateVersion7(),
            Now.AddMinutes(4), Now.AddMinutes(6))).IsFalse();
        await Assert.That(pending.TryClaim(pending.ConcurrencyStamp, Guid.CreateVersion7(),
            Now.AddMinutes(5), Now.AddMinutes(6))).IsTrue();
        await Assert.That((pending.Id, pending.TenantId, pending.ProviderBindingId, pending.Provider,
            pending.ObjectKey, pending.ProviderObjectVersion)).IsEqualTo(identity);
        await Assert.That(pending.ToString()).DoesNotContain(pending.ObjectKey);
    }

    [Test]
    public async Task LateProviderAcknowledgementBindsTheExactVersionOnce()
    {
        var pending = StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.S3Compatible, Guid.CreateVersion7(), $"objects/{Guid.CreateVersion7():N}", null, false, Now);
        string version = Guid.CreateVersion7().ToString("N");
        await Assert.That(pending.TrySettleProducer(version, Now)).IsTrue();
        await Assert.That(pending.ProviderObjectVersion).IsEqualTo(version);
        await Assert.That(pending.TrySettleProducer(Guid.CreateVersion7().ToString("N"), Now)).IsFalse();
        await Assert.That(pending.ProviderObjectVersion).IsEqualTo(version);
    }

    private static StorageObjectDeletionTombstone Create(bool producerSettled) =>
        StorageObjectDeletionTombstone.Create(Guid.CreateVersion7(), Guid.CreateVersion7(),
            StorageProviders.Local, Guid.CreateVersion7(), $"objects/{Guid.CreateVersion7():N}", null, producerSettled, Now);
}
