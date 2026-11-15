namespace Event.Distributed.IntegrationTests;

[Category("NativeCleanupChild")]
public sealed class ManagedCleanupChildTests
{
    /// <summary>
    /// Holds a genuine owned fixture for the parent cleanup probe. This explicit
    /// child invocation is not independently admissible assurance evidence.
    /// Uses the real topology's cold-start allowance; the parent owns termination.
    /// </summary>
    [Test]
    [Explicit]
    public async Task OwnedFixtureRemainsLiveUntilItsNativeOwnerStopsIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        await using var fixture = await OwnedAppHostFixture.CreateAsync("Split", timeout.Token);
        await fixture.StartAsync(timeout.Token);
        var lifetime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await lifetime.Task.WaitAsync(timeout.Token);
    }
}
