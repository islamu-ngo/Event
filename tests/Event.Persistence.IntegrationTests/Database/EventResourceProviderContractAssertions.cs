using Event.Persistence.IntegrationTests.Fixtures;

namespace Event.Persistence.IntegrationTests.Database;

internal static class EventResourceProviderContractAssertions
{
    public static async Task AssertInvalidPersistedOwnershipRejectedAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture)
    {
        await fixture.PrepareAsync();
        await EventResourcePersistenceTests.AssertInvalidPersistedOwnershipAsync(
            () => fixture.CreateSystemContext());
    }
}
