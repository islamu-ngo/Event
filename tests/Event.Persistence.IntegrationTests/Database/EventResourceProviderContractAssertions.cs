using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Specifications.EventResources;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Database;

internal static class EventResourceProviderContractAssertions
{
    public static async Task AssertCursorDiscoveryIsBoundedAsync(PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await AssertCursorDiscoveryIsBoundedAsync(() => fixture.CreateSystemContext());
    }

    internal static async Task AssertCursorDiscoveryIsBoundedAsync(Func<ExploreDbContext> contextFactory)
    {
        await using var database = EventResourcePersistenceTests.TestDatabase.CreateProvider(contextFactory);
        var scope = await database.SeedScopeAsync();
        var rows = Enumerable.Range(0, 500).Select(_ =>
            EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId)).ToArray();
        await using (var seed = contextFactory())
        {
            seed.EventResources.AddRange(rows);
            await seed.SaveChangesAsync();
        }
        await using var context = contextFactory();
        var repository = new EventResourceRepository(context);
        var specification = new EventResourceQuerySpecification().And(EventResourceFilter.Event(scope.EventAId));
        var complete = await repository.ListCandidatesAsync(scope.TenantAId, 500, specification, default);
        await Assert.That(complete.Count).IsEqualTo(500);
        await Assert.That(complete.Select(row => row.Id).Distinct().Count()).IsEqualTo(500);
        var lastReturned = complete[19];
        var continuation = await repository.ListCandidatesAsync(scope.TenantAId, 500,
            specification.And(EventResourceFilter.After(new(lastReturned.SortOrder, lastReturned.Id))), default);
        await Assert.That(continuation.Select(row => row.Id).SequenceEqual(complete.Skip(20).Select(row => row.Id))).IsTrue();
        await Assert.That((await repository.ListCandidatesAsync(scope.TenantBId, 500, specification, default)).Count).IsEqualTo(0);
        await Assert.That(context.ChangeTracker.Entries<Explore.Domain.EventResource>().Any()).IsFalse();
        await Assert.That(async () => await repository.ListCandidatesAsync(scope.TenantAId, 501, specification, default))
            .Throws<ArgumentOutOfRangeException>();
    }

    public static async Task AssertInvalidPersistedOwnershipRejectedAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await EventResourcePersistenceTests.AssertInvalidPersistedOwnershipAsync(
            () => fixture.CreateSystemContext());
    }
}
