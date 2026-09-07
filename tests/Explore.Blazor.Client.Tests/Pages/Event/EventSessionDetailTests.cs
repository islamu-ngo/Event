using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Events.Sessions;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventSessionDetailTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task Render_WhenSessionIsManagedDraft_ShowsParentEventLinkAndNoModerationActions()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var agendaItemService = Substitute.For<IEventSessionAgendaItemService>();
        var session = new EventSessionDto
        {
            Id = sessionId,
            EventId = eventId,
            EventTitle = "Parent Event",
            Title = "Internal draft workshop",
            EventSessionStatusFullName = "Draft",
            EventSessionStatusMasterCode = "DRAFT",
            ConcurrencyStamp = Guid.NewGuid(),
            IsScheduled = false,
            AdditionalProperties = CreateHalLinks("publish", "archive")
        };

        eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId)
            .Returns(session);
        agendaItemService.GetManagedAgendaItemsBySessionAsync(eventId, sessionId)
            .Returns(new List<EventSessionAgendaItemListDto>());

        _ctx.Services.AddScoped(_ => eventSessionService);
        _ctx.Services.AddScoped(_ => Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventModerationService>());
        _ctx.Services.AddScoped(_ => Substitute.For<IEventService>());
        _ctx.Services.AddScoped(_ => agendaItemService);

        var cut = _ctx.RenderMudComponent<EventSessionDetail>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Internal draft workshop", StringComparison.Ordinal))
                throw new InvalidOperationException("Session details were not rendered.");
        }, TimeSpan.FromSeconds(3));

        await eventSessionService.Received(1).GetManagedSessionByIdAsync(eventId, sessionId);
        await Assert.That(cut.Markup).Contains($"/events/{eventId}");
        await Assert.That(cut.Markup).Contains("Parent Event");
        await Assert.That(cut.Markup).Contains("Publish");
        await Assert.That(cut.Markup).Contains("Archive");
        await Assert.That(cut.Markup.Contains("Moderate", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Heavy Redact", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenManagedReadIsUnavailable_FallsBackToPublicRedactedSessionAndAgenda()
    {
        var eventId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        var agendaItemService = Substitute.For<IEventSessionAgendaItemService>();
        var publicSession = new EventSessionDto
        {
            Id = sessionId,
            EventId = eventId,
            EventTitle = "Public Parent",
            Title = "Public workshop",
            EventSessionStatusFullName = "Published",
            EventSessionStatusMasterCode = "PUBLISHED",
            AdditionalProperties = new Dictionary<string, object>()
        };

        eventSessionService.GetManagedSessionByIdAsync(eventId, sessionId).Returns((EventSessionDto?)null);
        eventSessionService.GetSessionByIdAsync(sessionId).Returns(publicSession);
        agendaItemService.GetAgendaItemsBySessionAsync(sessionId)
            .Returns(new List<EventSessionAgendaItemListDto>());
        _ctx.Services.AddScoped(_ => eventSessionService);
        _ctx.Services.AddScoped(_ => Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventModerationService>());
        _ctx.Services.AddScoped(_ => Substitute.For<IEventService>());
        _ctx.Services.AddScoped(_ => agendaItemService);

        var cut = _ctx.RenderMudComponent<EventSessionDetail>(parameters => parameters
            .Add(component => component.EventId, eventId)
            .Add(component => component.SessionId, sessionId));

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Public workshop", StringComparison.Ordinal))
                throw new InvalidOperationException("Public session details were not rendered.");
        }, TimeSpan.FromSeconds(3));

        await eventSessionService.Received(1).GetManagedSessionByIdAsync(eventId, sessionId);
        await eventSessionService.Received(1).GetSessionByIdAsync(sessionId);
        await agendaItemService.Received(1).GetAgendaItemsBySessionAsync(sessionId);
        await agendaItemService.DidNotReceive()
            .GetManagedAgendaItemsBySessionAsync(Arg.Any<Guid>(), Arg.Any<Guid>());
        await Assert.That(cut.Markup).DoesNotContain("Edit");
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static Dictionary<string, object> CreateHalLinks(params string[] linkRels)
    {
        var links = string.Join(
            ',',
            linkRels.Select(rel => $"\"{rel}\":{{\"href\":\"/api/eventsession/{rel}\",\"method\":\"POST\"}}"));
        using var doc = System.Text.Json.JsonDocument.Parse($"{{\"_links\":{{{links}}}}}");

        return new Dictionary<string, object>
        {
            ["_links"] = doc.RootElement.GetProperty("_links").Clone()
        };
    }
}
