using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Persistence;

namespace Event.Persistence.IntegrationTests.UnitOfWork;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RelationalAtprotoSessionRefreshLockTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SameScopeWaitsUntilTheAuthoritativeLeaseIsReleased()
    {
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var firstLock = new RelationalAtprotoSessionRefreshLock(firstContext);
        var secondLock = new RelationalAtprotoSessionRefreshLock(secondContext);
        await using var firstLease = await firstLock.AcquireAsync(
            tenantId, userId, "atproto", "did:plc:refresh-lock", CancellationToken.None);

        Task<IAsyncDisposable> competing = secondLock.AcquireAsync(
            tenantId, userId, "atproto", "did:plc:refresh-lock", CancellationToken.None);
        Task early = await Task.WhenAny(competing, Task.Delay(TimeSpan.FromMilliseconds(250)));

        await Assert.That(ReferenceEquals(early, competing)).IsFalse();
        await firstLease.DisposeAsync();
        await using var secondLease = await competing.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task CancelledWaitClosesItsConnectionAndScopeCanBeReacquired()
    {
        await using var firstContext = fixture.CreateDbContext();
        await using var cancelledContext = fixture.CreateDbContext();
        await using var retryContext = fixture.CreateDbContext();
        var tenantId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var firstLock = new RelationalAtprotoSessionRefreshLock(firstContext);
        var cancelledLock = new RelationalAtprotoSessionRefreshLock(cancelledContext);
        var retryLock = new RelationalAtprotoSessionRefreshLock(retryContext);
        IAsyncDisposable firstLease = await firstLock.AcquireAsync(
            tenantId, userId, "atproto", "did:plc:refresh-cancel", CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        await Assert.That(async () => await cancelledLock.AcquireAsync(
                tenantId,
                userId,
                "atproto",
                "did:plc:refresh-cancel",
                cancellation.Token))
            .Throws<OperationCanceledException>();

        await firstLease.DisposeAsync();
        await using IAsyncDisposable retryLease = await retryLock.AcquireAsync(
                tenantId,
                userId,
                "atproto",
                "did:plc:refresh-cancel",
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
    }

}
public sealed class RelationalAtprotoSessionRefreshLockKeyTests
{
    [Test]
    public async Task StableKeyIncludesEverySecurityScopeDimension()
    {
        var tenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
        var userId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000002");
        long baseline = RelationalAtprotoSessionRefreshLock.ComputeStableLockKey(
            tenantId, userId, "atproto", "did:plc:alice");

        await Assert.That(RelationalAtprotoSessionRefreshLock.ComputeStableLockKey(
            tenantId, userId, "atproto", "did:plc:alice")).IsEqualTo(baseline);
        await Assert.That(RelationalAtprotoSessionRefreshLock.ComputeStableLockKey(
            Guid.CreateVersion7(), userId, "atproto", "did:plc:alice")).IsNotEqualTo(baseline);
        await Assert.That(RelationalAtprotoSessionRefreshLock.ComputeStableLockKey(
            tenantId, Guid.CreateVersion7(), "atproto", "did:plc:alice")).IsNotEqualTo(baseline);
        await Assert.That(RelationalAtprotoSessionRefreshLock.ComputeStableLockKey(
            tenantId, userId, "other", "did:plc:alice")).IsNotEqualTo(baseline);
        await Assert.That(RelationalAtprotoSessionRefreshLock.ComputeStableLockKey(
            tenantId, userId, "atproto", "did:plc:bob")).IsNotEqualTo(baseline);
    }
}
