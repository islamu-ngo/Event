namespace Explore.Blazor.Client.Tests.Services;

public sealed class TenantNavLinksStateTests
{
    [Test]
    public async Task RefreshAsync_WhenDisposedWhileFetchIsInFlight_CompletesWithoutDisposedSemaphoreError()
    {
        var service = Substitute.For<ITenantNavigationService>();
        var fetchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchResult = new TaskCompletionSource<ICollection<TenantNavigationLinkDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        service.GetNavigationLinksAsync().Returns(_ =>
        {
            fetchStarted.SetResult();
            return fetchResult.Task;
        });

        var state = new TenantNavLinksState();
        var refreshTask = state.RefreshAsync(service);

        await fetchStarted.Task;
        state.Dispose();
        fetchResult.SetResult([
            new TenantNavigationLinkDto
            {
                Id = Guid.NewGuid(),
                Label = "Events",
                Url = "/events",
                Order = 10
            }
        ]);

        await refreshTask;
        await Assert.That(state.Links.Count).IsEqualTo(1);
    }
}
