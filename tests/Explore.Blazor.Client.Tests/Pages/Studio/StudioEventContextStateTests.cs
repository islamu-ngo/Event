using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioEventContextStateTests
{
    [Test]
    public async Task LoadAsync_ForSameEventWhileRequestIsInFlight_CallsServiceOnce()
    {
        var eventId = Guid.CreateVersion7();
        var completion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventService = Substitute.For<IEventService>();
        eventService.GetEventByIdAsync(eventId).Returns(completion.Task);
        var state = new StudioEventContextState(eventService);

        var shellLoad = state.LoadAsync(eventId);
        var navigationLoad = state.LoadAsync(eventId);
        completion.SetResult(new EventDto { Id = eventId, Title = "Community gathering" });

        await Task.WhenAll(shellLoad, navigationLoad);
        await eventService.Received(1).GetEventByIdAsync(eventId);
        await Assert.That(state.Event?.Title).IsEqualTo("Community gathering");
    }
}
