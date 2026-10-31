namespace Event.Distributed.IntegrationTests;

[Category("NativeCleanupChild")]
public sealed class ManagedCleanupChildTests
{
    /// <summary>
    /// Holds a genuine owned fixture for the parent cleanup probe. This explicit
    /// child invocation is not independently admissible assurance evidence.
    /// </summary>
    [Test]
    [Explicit]
    public async Task OwnedFixtureRemainsLiveUntilItsNativeOwnerStopsIt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var fixture = await OwnedAppHostFixture.CreateAsync("Split", timeout.Token);
        await fixture.StartAsync(timeout.Token);
        var lifetime = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await lifetime.Task.WaitAsync(timeout.Token);
    }
}
