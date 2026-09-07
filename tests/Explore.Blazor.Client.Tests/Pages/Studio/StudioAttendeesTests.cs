using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioAttendeesTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IStudioContextService _studio;
    private readonly IEventService _events;
    private readonly IContactShareConsentService _consents;
    private readonly IAccessibilityAnnouncerService _announcer;

    public StudioAttendeesTests()
    {
        _studio = _ctx.AddMockService<IStudioContextService>();
        _events = _ctx.AddMockService<IEventService>();
        _consents = _ctx.AddMockService<IContactShareConsentService>();
        _announcer = _ctx.AddMockService<IAccessibilityAnnouncerService>();
        _ctx.AddMockService<IBrowserActionInterop>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.Services.AddScoped<StudioEventContextState>();
        _ctx.Services.AddScoped<RouterStateService>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ActorNavigation_RequiresExactViewParticipantsRelation(bool hasRelation)
    {
        _studio.GetContextAsync(null, Arg.Any<CancellationToken>()).Returns(Context(hasRelation));

        var cut = _ctx.RenderMudComponent<StudioAttendeesNavigationLink>();
        int expected = hasRelation ? 1 : 0;

        cut.WaitForState(() => cut.FindAll("[data-testid='studio-attendees-navigation-link']").Count == expected);
        await Assert.That(cut.FindAll("[data-testid='studio-attendees-navigation-link']").Count).IsEqualTo(expected);
    }

    [Test]
    public async Task ActorPage_WithoutViewParticipants_HidesSectionsRowsAndOperations()
    {
        _studio.GetContextAsync(null, Arg.Any<CancellationToken>()).Returns(Context(false));

        var cut = _ctx.RenderMudComponent<StudioAttendees>();

        cut.WaitForElement("[data-testid='studio-attendees-unavailable']");
        await Assert.That(cut.FindAll(".studio-attendees__event")).IsEmpty();
        await Assert.That(cut.FindAll("[data-testid='studio-attendee-row']")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("View order");
        await _events.DidNotReceive().GetMyEventsPagedAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventPage_WithoutViewParticipants_HidesRowsAndDoesNotRequestAttendees()
    {
        var eventId = Guid.CreateVersion7();
        _events.GetEventByIdAsync(eventId).Returns(Event(eventId));

        var cut = _ctx.RenderMudComponent<StudioEventAttendees>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='event-attendees-unavailable']");
        await Assert.That(cut.FindAll("[data-testid='studio-attendee-row']")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("View order");
        await _studio.DidNotReceive().GetEventAttendeesAsync(eventId, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventPage_WithViewParticipants_UsesSequentialHeadingLevels()
    {
        var eventId = Guid.CreateVersion7();
        EventDto resource = Event(eventId);
        resource.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, object> { ["view-participants"] = new { href = "/participants" } });
        _events.GetEventByIdAsync(eventId).Returns(resource);
        _studio.GetEventAttendeesAsync(eventId, Arg.Any<CancellationToken>()).Returns(AttendeeOrders());

        var cut = _ctx.RenderMudComponent<StudioEventAttendees>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='studio-attendee-row']");
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("h2").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("h3")).IsEmpty();
    }

    [Test]
    [Arguments(true, 1)]
    [Arguments(false, 0)]
    public async Task EventPage_ExportActionRequiresExactHalRelation(bool hasExportRelation, int expectedButtons)
    {
        Guid eventId = Guid.CreateVersion7();
        EventDto resource = Event(eventId) with { OrganizerActorId = Guid.CreateVersion7() };
        var links = new Dictionary<string, object> { ["view-participants"] = new { href = "/participants" } };
        if (hasExportRelation)
        {
            links["export-attendees"] = new { href = "/export", method = "POST" };
        }

        resource.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(links);
        _events.GetEventByIdAsync(eventId).Returns(resource);
        _studio.GetEventAttendeesAsync(eventId, Arg.Any<CancellationToken>()).Returns([]);

        var cut = _ctx.RenderMudComponent<StudioEventAttendees>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[data-testid='export-attendees']").Count != expectedButtons)
            {
                throw new InvalidOperationException("Export affordance did not match HAL state.");
            }
        });
        await Assert.That(cut.FindAll("[data-testid='export-attendees']").Count).IsEqualTo(expectedButtons);
        await _consents.DidNotReceive().ExportSharedContactsAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task EventPage_ExportAction_AnnouncesSuccessfulDownload()
    {
        Guid eventId = Guid.CreateVersion7();
        Guid organizerActorId = Guid.CreateVersion7();
        EventDto resource = Event(eventId) with { OrganizerActorId = organizerActorId };
        resource.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, object>
            {
                ["view-participants"] = new { href = "/participants" },
                ["export-attendees"] = new { href = "/export", method = "POST" }
            });
        _events.GetEventByIdAsync(eventId).Returns(resource);
        _studio.GetEventAttendeesAsync(eventId, Arg.Any<CancellationToken>()).Returns([]);
        _consents.ExportSharedContactsAsync(organizerActorId, "csv", eventId, Arg.Any<CancellationToken>())
            .Returns(((byte[] FileBytes, string FileName)?)([1, 2, 3], "attendees.csv"));
        _ctx.Services.GetRequiredService<IBrowserActionInterop>()
            .DownloadBase64FileAsync(Arg.Any<string>(), "attendees.csv", "text/csv", Arg.Any<CancellationToken>())
            .Returns(true);

        var cut = _ctx.RenderMudComponent<StudioEventAttendees>(parameters => parameters
            .Add(component => component.EventId, eventId));

        await cut.Find("[data-testid='export-attendees']").ClickAsync(new MouseEventArgs());

        await _announcer.Received(1).AnnouncePoliteAsync("Export downloaded.");
        await _announcer.DidNotReceive().AnnounceAssertiveAsync(Arg.Any<string>());
    }

    [Test]
    public async Task AttendeeList_ShowsRowsAndOrderOperationOnlyFromLinkedResources()
    {
        var eventId = Guid.CreateVersion7();
        _studio.GetEventAttendeesAsync(eventId, Arg.Any<CancellationToken>()).Returns(AttendeeOrders());

        var cut = _ctx.RenderMudComponent<StudioAttendeeList>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='studio-attendee-row']");
        await Assert.That(cut.Markup).Contains("Sam");
        await Assert.That(cut.Markup).Contains("View order");
    }

    [Test]
    public async Task AttendeeList_WithoutOrderSelfRelationKeepsRowButHidesOrderOperation()
    {
        var eventId = Guid.CreateVersion7();
        _studio.GetEventAttendeesAsync(eventId, Arg.Any<CancellationToken>()).Returns(AttendeeOrders(hasOrderSelf: false));

        var cut = _ctx.RenderMudComponent<StudioAttendeeList>(parameters => parameters
            .Add(component => component.EventId, eventId));

        cut.WaitForElement("[data-testid='studio-attendee-row']");
        await Assert.That(cut.Markup).Contains("Sam");
        await Assert.That(cut.Markup).DoesNotContain("View order");
    }

    private static IReadOnlyList<StudioAttendeeOrder> AttendeeOrders(bool hasOrderSelf = true)
    {
        var order = new HalResourceOfRegistrationOrderDto
        {
            Id = Guid.CreateVersion7(),
            _links = hasOrderSelf
                ? new Dictionary<string, HalLink> { ["self"] = new() { Href = "/order" } }
                : new Dictionary<string, HalLink>()
        };
        var participants = JsonSerializer.Deserialize<HalResourceOfRegistrationOrderParticipantsDto>(
            $$"""{"registrationOrderId":"{{order.Id}}","lines":[],"participants":[{"id":"{{Guid.CreateVersion7()}}","registrationOrderId":"{{order.Id}}","participantTypeId":1,"displayName":"Sam"}],"assignments":[]}""")!;
        return [new StudioAttendeeOrder(order, participants)];
    }

    private static HalResourceOfStudioContextDto Context(bool hasRelation) => new()
    {
        _links = hasRelation
            ? new Dictionary<string, HalLink> { ["view-participants"] = new() { Href = "/participants" } }
            : new Dictionary<string, HalLink>()
    };

    private static EventDto Event(Guid eventId)
    {
        var resource = new EventDto { Id = eventId, Title = "Community gathering" };
        resource.AdditionalProperties["_links"] = JsonSerializer.SerializeToElement(new Dictionary<string, object>());
        return resource;
    }
}
