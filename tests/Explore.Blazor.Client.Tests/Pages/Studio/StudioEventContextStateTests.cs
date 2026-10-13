using System.Security.Claims;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;

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
        using var state = new StudioEventContextState(eventService);

        var shellLoad = state.LoadAsync(eventId);
        var navigationLoad = state.LoadAsync(eventId);
        await Assert.That(ReferenceEquals(shellLoad, navigationLoad)).IsTrue();
        completion.SetResult(new EventDto { Id = eventId, Title = "Community gathering" });

        await Task.WhenAll(shellLoad, navigationLoad);
        await Assert.That(state.Event?.Title).IsEqualTo("Community gathering");
    }

    [Test]
    public async Task ReloadAsync_NewerDenialCannotBeReplacedByOlderSuccess()
    {
        var eventId = Guid.CreateVersion7();
        var oldCompletion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var currentCompletion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Substitute.For<IEventService>();
        service.GetEventByIdAsync(eventId).Returns(oldCompletion.Task, currentCompletion.Task);
        using var state = new StudioEventContextState(service);

        var oldLoad = state.LoadAsync(eventId);
        var currentLoad = state.ReloadAsync(eventId);
        currentCompletion.SetResult(null);
        await currentLoad.WaitAsync(TimeSpan.FromSeconds(5));
        oldCompletion.SetResult(new EventDto { Id = eventId, Title = "Revoked workspace" });

        await Assert.That(await oldLoad.WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
        await Assert.That(state.Event).IsNull();
        await Assert.That(state.ErrorMessage).IsNotNull();
        await Assert.That(state.IsLoading).IsFalse();
    }

    [Test]
    public async Task LoadAsync_ReturningToEventDoesNotAcceptItsEarlierGeneration()
    {
        var firstId = Guid.CreateVersion7();
        var secondId = Guid.CreateVersion7();
        var firstCompletion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompletion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestCompletion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Substitute.For<IEventService>();
        service.GetEventByIdAsync(firstId).Returns(firstCompletion.Task, latestCompletion.Task);
        service.GetEventByIdAsync(secondId).Returns(secondCompletion.Task);
        using var state = new StudioEventContextState(service);

        var firstLoad = state.LoadAsync(firstId);
        var secondLoad = state.LoadAsync(secondId);
        var latestLoad = state.LoadAsync(firstId);
        latestCompletion.SetResult(new EventDto { Id = firstId, Title = "Current authority" });
        await latestLoad.WaitAsync(TimeSpan.FromSeconds(5));
        firstCompletion.SetResult(new EventDto { Id = firstId, Title = "Old authority" });
        secondCompletion.SetResult(new EventDto { Id = secondId, Title = "Other event" });

        await Assert.That(await firstLoad.WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
        await Assert.That(await secondLoad.WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
        await Assert.That(state.Event?.Title).IsEqualTo("Current authority");
        await Assert.That(state.EventId).IsEqualTo(firstId);
    }

    [Test]
    public async Task LoadAsync_AfterFailureStartsFreshRequest()
    {
        var eventId = Guid.CreateVersion7();
        var service = Substitute.For<IEventService>();
        service.GetEventByIdAsync(eventId).Returns(
            Task.FromException<EventDto?>(new HttpRequestException("Workspace unavailable")),
            Task.FromResult<EventDto?>(new EventDto { Id = eventId, Title = "Recovered workspace" }));
        using var state = new StudioEventContextState(service);

        await Assert.That(await state.LoadAsync(eventId).WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
        await Assert.That(await state.LoadAsync(eventId).WaitAsync(TimeSpan.FromSeconds(5))).IsNotNull();
        await Assert.That(state.Event?.Title).IsEqualTo("Recovered workspace");
        await Assert.That(state.ErrorMessage).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task InvalidationOrDisposal_RejectsPendingResourceAndNotification(bool dispose)
    {
        var eventId = Guid.CreateVersion7();
        var completion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Substitute.For<IEventService>();
        service.GetEventByIdAsync(eventId).Returns(completion.Task);
        using var state = new StudioEventContextState(service);
        var changed = 0;
        state.Changed += () => changed++;
        var load = state.LoadAsync(eventId);
        if (dispose) state.Dispose(); else state.Invalidate();
        var notificationsAtBoundary = changed;
        completion.SetResult(new EventDto { Id = eventId, Title = "Invalidated authority" });
        await Assert.That(await load.WaitAsync(TimeSpan.FromSeconds(5))).IsNull();
        await Assert.That(state.Event).IsNull();
        await Assert.That(changed).IsEqualTo(notificationsAtBoundary);
        await Assert.That(state.IsLoading).IsFalse();
    }

    [Test]
    public async Task AuthenticationChange_ClearsWorkspaceBeforeNewIdentityCompletes()
    {
        var eventId = Guid.CreateVersion7();
        var service = Substitute.For<IEventService>();
        service.GetEventByIdAsync(eventId).Returns(new EventDto { Id = eventId, Title = "Old identity" });
        var authentication = new WorkspaceAuthentication();
        using var state = new StudioEventContextState(service, authentication);
        await state.LoadAsync(eventId);
        var before = state.Generation;
        var pending = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        authentication.Change(pending.Task);
        await Assert.That(state.Event).IsNull();
        await Assert.That(state.EventId).IsNull();
        await Assert.That(state.Generation > before).IsTrue();
        pending.SetResult(new AuthenticationState(new ClaimsPrincipal()));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EventScopedInvalidation_DoesNotClearAnotherSelectedWorkspace(bool sameEvent)
    {
        var eventId = Guid.CreateVersion7();
        var service = Substitute.For<IEventService>();
        var resource = new EventDto { Id = eventId, Title = "Current selected workspace" };
        service.GetEventByIdAsync(eventId).Returns(resource);
        using var state = new StudioEventContextState(service);
        await state.LoadAsync(eventId);
        var generation = state.Generation;

        state.Invalidate(sameEvent ? eventId : Guid.CreateVersion7());

        await Assert.That(state.Event).IsEqualTo(sameEvent ? null : resource);
        await Assert.That(state.Generation).IsEqualTo(sameEvent ? generation + 1 : generation);
    }

    private sealed class WorkspaceAuthentication : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
        public void Change(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);
    }

    [Test]
    public async Task ReloadAsync_ClearsOldAffordancesWhileAuthorityIsRevalidated()
    {
        var eventId = Guid.CreateVersion7();
        var completion = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = Substitute.For<IEventService>();
        service.GetEventByIdAsync(eventId).Returns(
            Task.FromResult<EventDto?>(new EventDto { Id = eventId, Title = "Previously managed event" }),
            completion.Task);
        using var state = new StudioEventContextState(service);
        await state.LoadAsync(eventId);

        var reload = state.ReloadAsync(eventId);
        var retainedOldResource = state.Event;
        completion.SetResult(null);
        await reload.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(retainedOldResource).IsNull();
        await Assert.That(state.Event).IsNull();
    }
}
