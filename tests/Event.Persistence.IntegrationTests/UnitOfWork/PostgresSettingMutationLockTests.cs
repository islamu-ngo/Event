using Explore.Persistence;

namespace Event.Persistence.IntegrationTests.UnitOfWork;

public sealed class RelationalSettingMutationLockTests
{
    [Test]
    public async Task NormalizeCanonicalKeys_ReturnsDistinctOrdinalOrder()
    {
        string[] normalized = RelationalSettingMutationLock.NormalizeCanonicalKeys(
            [" Zebra ", "beta", "ALPHA", "alpha"]);

        await Assert.That(normalized.SequenceEqual(
            ["alpha", "beta", "zebra"],
            StringComparer.Ordinal)).IsTrue();
    }
}
