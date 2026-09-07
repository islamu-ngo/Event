using System.Text.Json;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioPagesTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IEventService _eventService;
    private readonly IEventCreationEligibilityService _eligibilityService;
    private readonly IUiShellContextService _shellContextService;
    private readonly UiShellState _shellState;

    public StudioPagesTests()
    {
        _eventService = _ctx.AddMockService<IEventService>();
        _eligibilityService = _ctx.AddMockService<IEventCreationEligibilityService>();
        _shellContextService = _ctx.AddMockService<IUiShellContextService>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _shellState = _ctx.Services.GetRequiredService<UiShellState>();

        _eventService.GetManagedEventsByActorAsync(
                Arg.Any<Guid>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PaginatedResult<EventListDto>.Empty(1, 100)));
        _eventService.GetMyEventsPagedAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(PaginatedResult<EventListDto>.Empty(1, 100)));
        _eligibilityService.GetEligibilityAsync().Returns(EventCreationEligibility.NotEligible);
        SetActors([]);
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task StudioHomeRendersVisibleHeading()
    {
        var cut = _ctx.RenderMudComponent<StudioHome>();

        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Studio");
    }

    [Test]
    public async Task StudioEventsRendersVisibleHeading()
    {
        var cut = _ctx.RenderMudComponent<StudioEvents>();

        await Assert.That(cut.Find("h1").TextContent).IsEqualTo("Events");
    }

    [Test]
    public async Task StudioEventsLoadsStrictManagedEventsForActiveActorId()
    {
        var actor = CreateActor("Organization", "Community Events", scopeId: Guid.CreateVersion7());
        var managedEvent = CreateEvent("Community Iftar");
        SetActors([actor]);
        SetActiveActor(actor, [actor]);
        _eventService.GetManagedEventsByActorAsync(
                actor.ActorId!.Value,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([managedEvent])));

        var cut = _ctx.RenderMudComponent<StudioEvents>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='studio-event-list']"));
        await Assert.That(cut.Markup).Contains("Community Iftar");
        await _eventService.Received(1).GetManagedEventsByActorAsync(
            actor.ActorId.Value,
            1,
            100,
            Arg.Any<CancellationToken>());
        await _eventService.DidNotReceive().GetManagedEventsByActorAsync(
            actor.ScopeId!.Value,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await _eventService.DidNotReceive().GetMyEventsPagedAsync(
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StudioEventsUsesPersonalFallbackWithoutManagedActor()
    {
        _eventService.GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([CreateEvent("Personal Workshop")])));

        var cut = _ctx.RenderMudComponent<StudioEvents>();

        cut.WaitForAssertion(() => cut.Find("[data-testid='studio-event-list']"));
        await Assert.That(cut.Markup).Contains("Personal Workshop");
        await _eventService.Received(1).GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>());
        await _eventService.DidNotReceive().GetManagedEventsByActorAsync(
            Arg.Any<Guid>(),
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task StudioEventsKeepsNewActorResultWhenPreviousLoadCompletesLast()
    {
        var first = CreateActor("Organization", "First Actor");
        var second = CreateActor("Group", "Second Actor");
        var actors = new[] { first, second };
        var firstLoad = new TaskCompletionSource<PaginatedResult<EventListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLoad = new TaskCompletionSource<PaginatedResult<EventListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetActors(actors);
        SetActiveActor(first, actors);
        _eventService.GetManagedEventsByActorAsync(
                first.ActorId!.Value,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(firstLoad.Task);
        _eventService.GetManagedEventsByActorAsync(
                second.ActorId!.Value,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(secondLoad.Task);

        var cut = _ctx.RenderMudComponent<StudioEvents>();
        cut.WaitForElement("[data-testid='studio-events-loading']");

        await cut.InvokeAsync(() => _shellState.TrySetActiveActor(second.ActorId.Value, actors));
        secondLoad.SetResult(CreateResult([CreateEvent("Second Actor Event")]));
        cut.WaitForAssertion(() => cut.Markup.Contains("Second Actor Event", StringComparison.Ordinal));

        firstLoad.SetResult(CreateResult([CreateEvent("Stale First Event")]));
        cut.WaitForAssertion(() =>
        {
            if (cut.Markup.Contains("Stale First Event", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("A stale actor load replaced the current actor result.");
            }
        });

        await Assert.That(cut.Markup).Contains("Second Actor Event");
        await Assert.That(cut.Markup).DoesNotContain("Stale First Event");
    }

    [Test]
    public async Task DisposedStudioEventsStopsListeningForActorChanges()
    {
        var first = CreateActor("Organization", "First Actor");
        var second = CreateActor("Group", "Second Actor");
        var actors = new[] { first, second };
        SetActors(actors);
        SetActiveActor(first, actors);
        var cut = _ctx.RenderMudComponent<StudioEvents>();
        cut.WaitForElement("[data-testid='studio-events-empty']");

        cut.Instance.Dispose();
        _eventService.ClearReceivedCalls();
        _shellState.TrySetActiveActor(second.ActorId!.Value, actors);

        await _eventService.DidNotReceive().GetManagedEventsByActorAsync(
            second.ActorId.Value,
            Arg.Any<int>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        cut.Dispose();
    }

    [Test]
    public async Task StudioEventsDoesNotFlashEmptyStateWhileLoading()
    {
        var load = new TaskCompletionSource<PaginatedResult<EventListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _eventService.GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>()).Returns(load.Task);

        var cut = _ctx.RenderMudComponent<StudioEvents>();

        cut.WaitForElement("[data-testid='studio-events-loading']");
        await Assert.That(cut.FindAll("[data-testid='studio-events-empty']")).IsEmpty();
        load.SetResult(PaginatedResult<EventListDto>.Empty(1, 100));
    }

    [Test]
    [Arguments(true, true, true)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    public async Task StudioEventsCreateRequiresEligibilityAndCollectionLink(
        bool eligible,
        bool hasCreateLink,
        bool expected)
    {
        _eligibilityService.GetEligibilityAsync().Returns(new EventCreationEligibility { CanCreate = eligible });
        _eventService.GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([], hasCreateLink)));

        var cut = _ctx.RenderMudComponent<StudioEvents>();
        cut.WaitForElement("[data-testid='studio-events-empty']");

        var createLinks = cut.FindAll("a[data-testid='studio-create-event']");
        await Assert.That(createLinks.Count).IsEqualTo(expected ? 1 : 0);
        if (expected)
        {
            await Assert.That(createLinks[0].GetAttribute("href")).IsEqualTo("/events/create");
        }
    }

    [Test]
    public async Task StudioEventsRendersOnlyHalAuthorizedRowActions()
    {
        var authorized = CreateEvent("Authorized Event", links: ["edit", "delete"]);
        var readOnly = CreateEvent("Read-only Event");
        _eventService.GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult([authorized, readOnly])));

        var cut = _ctx.RenderMudComponent<StudioEvents>();
        cut.WaitForElement("[data-testid='studio-event-list']");

        var editLinks = cut.FindAll("a[data-testid^='studio-edit-event-']");
        var deleteButtons = cut.FindAll("button[data-testid^='studio-delete-event-']");
        await Assert.That(editLinks.Count).IsEqualTo(1);
        await Assert.That(editLinks[0].GetAttribute("href")).IsEqualTo($"/events/{authorized.Id}/edit");
        await Assert.That(deleteButtons.Count).IsEqualTo(1);
    }

    [Test]
    public async Task StudioHomeShowsActorSummaryCountsAndGatedQuickActions()
    {
        var actor = CreateActor("Organization", "Community Events");
        var editableUpcoming = CreateEvent("Editable Upcoming", links: ["edit"]);
        var readOnlyUpcoming = CreateEvent("Read-only Upcoming");
        var editablePast = CreateEvent("Editable Past", isPast: true, links: ["edit"]);
        SetActors([actor]);
        SetActiveActor(actor, [actor]);
        _eligibilityService.GetEligibilityAsync().Returns(new EventCreationEligibility { CanCreate = true });
        _eventService.GetManagedEventsByActorAsync(
                actor.ActorId!.Value,
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateResult(
                [editableUpcoming, readOnlyUpcoming, editablePast],
                hasCreateLink: true,
                totalCount: 7)));

        var cut = _ctx.RenderMudComponent<StudioHome>();
        cut.WaitForElement("[data-testid='studio-dashboard']");

        await Assert.That(cut.Markup).Contains("Community Events");
        await Assert.That(cut.Find("[data-testid='studio-total-count']").TextContent).IsEqualTo("7");
        await Assert.That(cut.Find("[data-testid='studio-upcoming-count']").TextContent).IsEqualTo("2");
        await Assert.That(cut.Find("[data-testid='studio-editable-count']").TextContent).IsEqualTo("2");
        await Assert.That(cut.Find("a[data-testid='studio-view-events']").GetAttribute("href")).IsEqualTo("/studio/events");
        await Assert.That(cut.Find("a[data-testid='studio-create-event']").GetAttribute("href")).IsEqualTo("/events/create");
    }

    private void SetActors(IReadOnlyList<ManagedActorDto> actors)
    {
        _shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto { ManagedActors = actors.ToList() });
    }

    private void SetActiveActor(ManagedActorDto actor, IReadOnlyList<ManagedActorDto> actors) =>
        _shellState.ReconcileActiveActors(actors, actor.ActorId);

    private static ManagedActorDto CreateActor(string type, string name, Guid? scopeId = null) => new()
    {
        ActorId = Guid.CreateVersion7(),
        ScopeId = scopeId,
        ActorType = type,
        DisplayName = name
    };

    private static EventListDto CreateEvent(
        string title,
        bool isPast = false,
        IReadOnlyList<string>? links = null)
    {
        var id = Guid.CreateVersion7();
        var item = new EventListDto
        {
            Id = id,
            Title = title,
            IsPast = isPast,
            EventStatusFullName = isPast ? "Ended" : "Draft"
        };

        if (links is { Count: > 0 })
        {
            item.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
                links.ToDictionary(
                    relation => relation,
                    relation => new
                    {
                        href = relation == "edit" ? $"/events/{id}/edit" : $"/events/{id}",
                        method = relation == "delete" ? "DELETE" : "GET"
                    }));
        }

        return item;
    }

    private static PaginatedResult<EventListDto> CreateResult(
        IReadOnlyList<EventListDto> events,
        bool hasCreateLink = false,
        int? totalCount = null) => new()
        {
            Items = events.ToList(),
            PageNumber = 1,
            PageSize = 100,
            TotalCount = totalCount ?? events.Count,
            Links = hasCreateLink
            ? new Dictionary<string, HalLink>
            {
                ["create"] = new() { Href = "/events/create", Method = "POST" }
            }
            : null
        };
}
