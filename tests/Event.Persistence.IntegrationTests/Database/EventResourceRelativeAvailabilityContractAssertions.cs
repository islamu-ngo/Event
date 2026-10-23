using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

internal static class EventResourceRelativeAvailabilityContractAssertions
{
    public static async Task AssertProviderContractAsync(PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await AssertContractAsync(() => fixture.CreateSystemContext());
    }

    internal static async Task AssertContractAsync(Func<ExploreDbContext> contextFactory)
    {
        await using var database = EventResourcePersistenceTests.TestDatabase.CreateProvider(contextFactory);
        var scope = await database.SeedScopeAsync();
        foreach (var anchor in Enum.GetValues<EventResourceAvailabilityAnchorEnum>())
        {
            foreach (long endTicks in new[] { TimeSpan.TicksPerMinute, 0L })
            {
                await using var insert = contextFactory();
                var malformed = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
                Set(malformed, nameof(EventResource.AvailabilityStartAnchorId), (int?)anchor);
                Set(malformed, nameof(EventResource.AvailabilityEndAnchorId), (int?)anchor);
                Set(malformed, nameof(EventResource.AvailabilityStartOffsetTicks), (long?)TimeSpan.TicksPerMinute);
                Set(malformed, nameof(EventResource.AvailabilityEndOffsetTicks), (long?)endTicks);
                insert.EventResources.Add(malformed);
                await Assert.That(() => insert.SaveChangesAsync()).Throws<DbUpdateException>();
                await using var verify = contextFactory();
                await Assert.That(await verify.EventResources.AnyAsync(row => row.Id == malformed.Id)).IsFalse();
            }

            Guid id;
            await using (var seed = contextFactory())
            {
                var valid = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
                valid.ReplacePolicy(EventResourceAvailability.Create(startAnchor: anchor, startOffset: TimeSpan.FromMinutes(1),
                    endAnchor: anchor, endOffset: TimeSpan.FromMinutes(2)),
                    valid.AudienceRules, valid.ConcurrencyStamp, scope.ActorId, valid.CreatedAt);
                seed.EventResources.Add(valid);
                await seed.SaveChangesAsync();
                id = valid.Id;
            }
            foreach (long endTicks in new[] { TimeSpan.TicksPerMinute, 0L })
            {
                await using var update = contextFactory();
                await Assert.That(() => update.EventResources.Where(row => row.Id == id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(
                        row => row.AvailabilityEndOffsetTicks, (long?)endTicks))).Throws<DbException>();
                await using var verify = contextFactory();
                var unchanged = await verify.EventResources.AsNoTracking().SingleAsync(row => row.Id == id);
                await Assert.That(unchanged.Availability.EndOffsetTicks).IsEqualTo((long?)TimeSpan.FromMinutes(2).Ticks);
                var instant = new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
                var sameAnchorSchedule = new EventResourceScheduleFacts(instant, instant, instant, instant);
                await Assert.That(unchanged.Availability.IsAvailableAt(instant.AddSeconds(90), sameAnchorSchedule)).IsTrue();
                await Assert.That(unchanged.Availability.IsAvailableAt(instant.AddMinutes(2), sameAnchorSchedule)).IsFalse();
            }
        }

        var start = new DateTimeOffset(2040, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var schedule = new EventResourceScheduleFacts(start, start.AddHours(1), start, start.AddHours(1));
        EventResourceAvailability[] windows =
        [
            EventResourceAvailability.Create(),
            EventResourceAvailability.Create(startAnchor: EventResourceAvailabilityAnchorEnum.EventStart, startOffset: TimeSpan.Zero),
            EventResourceAvailability.Create(endAnchor: EventResourceAvailabilityAnchorEnum.EventEnd, endOffset: TimeSpan.Zero),
            EventResourceAvailability.Create(startAnchor: EventResourceAvailabilityAnchorEnum.EventStart, startOffset: TimeSpan.FromMinutes(2),
                endAnchor: EventResourceAvailabilityAnchorEnum.EventEnd, endOffset: TimeSpan.FromMinutes(-1)),
            EventResourceAvailability.Create(absoluteStartUtc: start,
                endAnchor: EventResourceAvailabilityAnchorEnum.EventEnd, endOffset: TimeSpan.Zero),
            EventResourceAvailability.Create(startAnchor: EventResourceAvailabilityAnchorEnum.EventStart, startOffset: TimeSpan.Zero,
                absoluteEndUtc: start.AddHours(1))
        ];
        foreach (var window in windows)
        {
            Guid id;
            await using (var write = contextFactory())
            {
                var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
                write.EventResources.Add(resource);
                await write.SaveChangesAsync();
                resource.ReplacePolicy(window, resource.AudienceRules, resource.ConcurrencyStamp, scope.ActorId, resource.CreatedAt);
                await write.SaveChangesAsync();
                id = resource.Id;
            }
            await using var read = contextFactory();
            var reconstructed = (await read.EventResources.AsNoTracking().SingleAsync(row => row.Id == id)).Availability;
            await Assert.That(reconstructed).IsEqualTo(window);
            await Assert.That(reconstructed.IsAvailableAt(start.AddMinutes(30), schedule)).IsTrue();
            if (window.EndAnchor.HasValue || window.AbsoluteEndUtc.HasValue)
                await Assert.That(reconstructed.IsAvailableAt(start.AddHours(1), schedule)).IsFalse();
            if (window.StartAnchor.HasValue || window.AbsoluteStartUtc.HasValue)
                await Assert.That(reconstructed.IsAvailableAt(start.AddMinutes(-1), schedule)).IsFalse();
        }
    }

    private static void Set<T>(EventResource resource, string property, T value) =>
        typeof(EventResource).GetProperty(property)!.SetValue(resource, value);
}
