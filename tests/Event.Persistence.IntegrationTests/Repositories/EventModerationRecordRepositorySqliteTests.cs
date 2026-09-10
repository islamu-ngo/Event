using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed class EventModerationRecordRepositorySqliteTests
{
    [Test]
    public async Task EmptyModerationHistoryDoesNotPreventEventDetails()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync();
        var repository = new EventModerationRecordRepository(fixture.Context);

        await Assert.That(await repository.GetLatestByEventAsync(
            fixture.TenantId, target.Id, CancellationToken.None)).IsNull();
        await Assert.That((await repository.GetByEventAsync(
            fixture.TenantId, target.Id, CancellationToken.None)).Count).IsEqualTo(0);
    }

    [Test]
    public async Task ModerationHistoryUsesUtcInstantAndStableIdentityWithinExactTenantAndEvent()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var target = await fixture.SeedEventAsync();
        var older = EventModerationRecord.CreateLightModeration(
            Guid.CreateVersion7(), fixture.TenantId, target.Id, null, "test-reason",
            (int)EventStatusEnum.Published, null,
            new DateTimeOffset(2026, 9, 9, 18, 0, 0, TimeSpan.FromHours(8)));
        var firstAtLatestInstant = EventModerationRecord.CreateLightModeration(
            Guid.Parse("01900000-0000-7000-8000-000000000001"), fixture.TenantId, target.Id, null, "test-reason",
            (int)EventStatusEnum.Published, null,
            new DateTimeOffset(2026, 9, 9, 8, 0, 0, TimeSpan.FromHours(-4)));
        var secondAtLatestInstant = EventModerationRecord.CreateLightModeration(
            Guid.Parse("01900000-0000-7000-8000-000000000002"), fixture.TenantId, target.Id, null, "test-reason",
            (int)EventStatusEnum.Published, null,
            new DateTimeOffset(2026, 9, 9, 14, 0, 0, TimeSpan.FromHours(2)));
        fixture.Context.EventModerationRecords.AddRange(older, firstAtLatestInstant, secondAtLatestInstant);
        await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var repository = new EventModerationRecordRepository(fixture.Context);

        var history = await repository.GetByEventAsync(fixture.TenantId, target.Id, CancellationToken.None);
        await Assert.That(history.Select(record => record.Id)
            .SequenceEqual([secondAtLatestInstant.Id, firstAtLatestInstant.Id, older.Id])).IsTrue();
        await Assert.That((await repository.GetLatestByEventAsync(
            fixture.TenantId, target.Id, CancellationToken.None))!.Id).IsEqualTo(secondAtLatestInstant.Id);
        await Assert.That(await repository.GetLatestByEventAsync(
            Guid.CreateVersion7(), target.Id, CancellationToken.None)).IsNull();
        await Assert.That((await repository.GetByEventAsync(
            fixture.TenantId, Guid.CreateVersion7(), CancellationToken.None)).Count).IsEqualTo(0);
    }
}
