using Event.Persistence.IntegrationTests.Database;

namespace Event.Persistence.IntegrationTests;

[NotInParallel("EventResourcePersistence")]
[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
public sealed class EventResourceRelativeAvailabilityTests(EventResourcePersistenceTests.TestDatabase database)
{
    [Test]
    public Task DirectWritesEnforceRelativeOrderAndValidWindowsReconstruct() =>
        EventResourceRelativeAvailabilityContractAssertions.AssertContractAsync(() => database.CreateContext());
}
