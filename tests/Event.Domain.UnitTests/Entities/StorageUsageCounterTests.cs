namespace Event.Domain.UnitTests.Entities;

using Explore.Domain;

public class StorageUsageCounterTests
{
    [Test]
    public async Task Reserve_WhenQuotaAllows_IncreasesReservedBytes()
    {
        var counter = CreateCounter();

        counter.Reserve(bytes: 512, quotaBytes: 1024);

        await Assert.That(counter.ReservedBytes).IsEqualTo(512);
        await Assert.That(counter.UsedBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Reserve_WhenQuotaWouldBeExceeded_ThrowsInvalidOperationException()
    {
        var counter = CreateCounter();
        counter.Recalculate(usedBytes: 800, reservedBytes: 100, quarantinedBytes: 0, objectCount: 2, DomainTestClock.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            counter.Reserve(bytes: 200, quotaBytes: 1024);
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task FinalizeReservation_MovesBytesFromReservedToUsedAndIncrementsObjectCount()
    {
        var counter = CreateCounter();
        counter.Reserve(bytes: 256, quotaBytes: 1024);

        counter.FinalizeReservation(256);

        await Assert.That(counter.ReservedBytes).IsEqualTo(0);
        await Assert.That(counter.UsedBytes).IsEqualTo(256);
        await Assert.That(counter.ObjectCount).IsEqualTo(1);
    }

    [Test]
    public async Task ReleaseReservation_NeverMakesReservedBytesNegative()
    {
        var counter = CreateCounter();
        counter.Reserve(bytes: 128, quotaBytes: 1024);

        counter.ReleaseReservation(256);

        await Assert.That(counter.ReservedBytes).IsEqualTo(0);
    }

    private static StorageUsageCounter CreateCounter()
    {
        return new StorageUsageCounter
        {
            TenantId = Guid.CreateVersion7(),
            Provider = StorageProviders.Local
        };
    }
}
