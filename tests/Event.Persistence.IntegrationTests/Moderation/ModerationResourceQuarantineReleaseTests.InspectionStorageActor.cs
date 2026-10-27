using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineReleaseTests
{
    [Test]
    public async Task SeparateStorageActorIsQualifiedBeforeInspectionAndDeletionCannotBorrowEventActor()
    {
        await using var fixture = await InspectionFixture.CreateAsync(file: true, separateStorageActor: true);
        var callbacks = 0;
        await using (var session = fixture.Session())
        {
            var pending = await session.PendingAsync(fixture.Intent);
            await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            {
                callbacks++;
                return Task.CompletedTask;
            }, default)).IsTrue();
        }
        await Assert.That(callbacks).IsGreaterThan(0);
        await using (var writer = fixture.Native.Context())
        {
            var actor = await writer.Actors.SingleAsync(row => row.Id == fixture.StorageActorId);
            actor.IsDeleted = true;
            await writer.SaveChangesAsync();
        }
        callbacks = 0;
        await using (var session = fixture.Session())
        {
            var pending = await session.PendingAsync(fixture.Intent);
            await Assert.That(await pending.DiscloseAsync((_, _, _) =>
            {
                callbacks++;
                return Task.CompletedTask;
            }, default)).IsFalse();
        }
        await Assert.That(callbacks).IsEqualTo(0);
    }
}
