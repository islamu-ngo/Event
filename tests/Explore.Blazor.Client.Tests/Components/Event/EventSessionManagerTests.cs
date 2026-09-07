using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Events.Components;

namespace Explore.Blazor.Client.Tests.Components.Event;

public sealed class EventSessionManagerTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task RendersDraftSession_WhenManagedSessionsAreIncluded()
    {
        var eventId = Guid.NewGuid();
        var publishedSessionId = Guid.NewGuid();
        var draftSessionId = Guid.NewGuid();
        var eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var agendaItemService = Substitute.For<IEventSessionAgendaItemService>();
        var sessions = new List<EventSessionListDto>
        {
            new()
            {
                Id = publishedSessionId,
                EventId = eventId,
                Title = "Published keynote",
                StartTime = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero),
                LocationFullName = "Main Hall",
                EventSessionStatusFullName = "Published",
                EventSessionStatusMasterCode = "PUBLISHED"
            },
            new()
            {
                Id = draftSessionId,
                EventId = eventId,
                Title = "Internal draft workshop",
                EventSessionStatusFullName = "Draft",
                EventSessionStatusMasterCode = "DRAFT",
                IsScheduled = false,
                SortOrder = 2
            }
        };

        eventSessionService.GetSessionsByEventAsync(eventId, includeManagedSessions: true)
            .Returns(sessions);
        agendaItemService.GetManagedAgendaItemsBySessionAsync(eventId, Arg.Any<Guid>())
            .Returns(new List<EventSessionAgendaItemListDto>());

        _ctx.Services.AddScoped(_ => eventSessionService);
        _ctx.Services.AddScoped(_ => agendaItemService);

        var cut = _ctx.RenderMudComponent<EventSessionManager>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.IncludeManagedSessions, true));

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Internal draft workshop", StringComparison.Ordinal))
                throw new InvalidOperationException("Draft session was not rendered.");
        });

        await eventSessionService.Received(1).GetSessionsByEventAsync(eventId, includeManagedSessions: true);
        await agendaItemService.Received(1).GetManagedAgendaItemsBySessionAsync(eventId, publishedSessionId);
        await agendaItemService.Received(1).GetManagedAgendaItemsBySessionAsync(eventId, draftSessionId);
        _ = agendaItemService.DidNotReceive().GetAgendaItemsBySessionAsync(Arg.Any<Guid>());
        await Assert.That(cut.Markup).Contains("Draft");
        await Assert.That(cut.Markup).Contains("Schedule TBD");
        await Assert.That(cut.Markup).Contains("Location TBD");
        await Assert.That(cut.Markup).Contains($"/events/{eventId}/sessions/{draftSessionId}");
    }

    [Test]
    public async Task RendersSessionDetailLinks_WhenProvidedSessionsDoNotCarryHalSelfLinks()
    {
        var eventId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        var eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var agendaItemService = Substitute.For<IEventSessionAgendaItemService>();
        var sessions = new List<EventSessionListDto>
        {
            new()
            {
                Id = firstSessionId,
                EventId = eventId,
                Title = "Workshop A",
                EventSessionStatusFullName = "Published",
                EventSessionStatusMasterCode = "PUBLISHED"
            },
            new()
            {
                Id = secondSessionId,
                EventId = eventId,
                Title = "Workshop B",
                EventSessionStatusFullName = "Draft",
                EventSessionStatusMasterCode = "DRAFT",
                SortOrder = 2
            }
        };

        agendaItemService.GetAgendaItemsBySessionAsync(Arg.Any<Guid>())
            .Returns(new List<EventSessionAgendaItemListDto>());

        _ctx.Services.AddScoped(_ => eventSessionService);
        _ctx.Services.AddScoped(_ => agendaItemService);

        var cut = _ctx.RenderMudComponent<EventSessionManager>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionItems, sessions));

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains($"/events/{eventId}/sessions/{firstSessionId}", StringComparison.Ordinal))
                throw new InvalidOperationException("First session detail link was not rendered.");
        });

        await Assert.That(cut.Markup).Contains($"/events/{eventId}/sessions/{secondSessionId}");
        _ = eventSessionService.DidNotReceive().GetSessionsByEventAsync(Arg.Any<Guid>(), Arg.Any<bool>());
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }
}
