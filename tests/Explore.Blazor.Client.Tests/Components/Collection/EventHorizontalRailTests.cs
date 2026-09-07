using Explore.Blazor.Client.Components.Collection;
using Explore.Blazor.Client.Models;
using ProductionEventCard = Explore.Blazor.Client.Pages.Events.Components.EventCard;

namespace Explore.Blazor.Client.Tests.Components.Collection;

public sealed class EventHorizontalRailTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task RailUsesProductionCompactGridCards()
    {
        var events = CreateEvents(3);
        var cut = Render(events);
        var cards = cut.FindComponents<ProductionEventCard>();

        await Assert.That(cards.Count).IsEqualTo(3);
        await Assert.That(cards.All(card => card.Instance.Layout == LayoutMode.CompactGrid)).IsTrue();
        await Assert.That(cut.Markup).DoesNotContain("Trending");
    }

    [Test]
    public async Task RailExposesHeadingViewAllAndFocusableRtlList()
    {
        var cut = _ctx.RenderMudComponent<EventHorizontalRail>(parameters => parameters
            .Add(component => component.Title, "Recently added")
            .Add(component => component.ViewAllLabel, "View all events")
            .Add(component => component.ViewAllUrl, "/events?sort=createdat")
            .Add(component => component.RightToLeft, true)
            .Add(component => component.Events, CreateEvents(2)));

        await Assert.That(cut.Find("h2").TextContent.Trim()).IsEqualTo("Recently added");
        await Assert.That(cut.Find("a[href='/events?sort=createdat']").TextContent.Trim()).IsEqualTo("View all events");

        var list = cut.Find("[data-testid='event-horizontal-rail']");
        await Assert.That(list.GetAttribute("role")).IsEqualTo("list");
        await Assert.That(list.GetAttribute("tabindex")).IsEqualTo("0");
        await Assert.That(list.GetAttribute("dir")).IsEqualTo("rtl");
    }

    [Test]
    public async Task RailDistinguishesLoadingFromEmptyState()
    {
        var cut = _ctx.RenderMudComponent<EventHorizontalRail>(parameters => parameters
            .Add(component => component.IsLoading, true));

        await Assert.That(cut.FindAll("[data-testid='event-rail-skeleton']").Count).IsEqualTo(5);
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(0);

        cut.Render(parameters => parameters
            .Add(component => component.IsLoading, false)
            .Add(component => component.Events, []));

        await Assert.That(cut.Find("[role='status']").TextContent.Trim()).IsEqualTo("No events available.");
        await Assert.That(cut.FindAll("[data-testid='event-rail-skeleton']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task ProductionCardClickIsForwardedOnce()
    {
        var events = CreateEvents(1);
        EventListDto? selected = null;
        var cut = _ctx.RenderMudComponent<EventHorizontalRail>(parameters => parameters
            .Add(component => component.Events, events)
            .Add(component => component.OnEventClick, (EventListDto item) => selected = item));

        await cut.InvokeAsync(() =>
            cut.FindComponent<ProductionEventCard>().Instance.OnClick.InvokeAsync(events[0]));

        await Assert.That(selected).IsSameReferenceAs(events[0]);
    }

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private IRenderedComponent<EventHorizontalRail> Render(IEnumerable<EventListDto> events)
    {
        return _ctx.RenderMudComponent<EventHorizontalRail>(parameters => parameters
            .Add(component => component.Events, events));
    }

    private static List<EventListDto> CreateEvents(int count)
    {
        return Enumerable.Range(1, count)
            .Select(index => new EventListDto
            {
                Id = Guid.NewGuid(),
                Title = $"Event {index}",
                ActorDisplayName = "Community organizer",
                EventTypeFullName = "Community",
                EventFormatFullName = "In-Person",
                FirstSessionDate = new DateTimeOffset(2026, 9, index, 18, 0, 0, TimeSpan.Zero)
            })
            .ToList();
    }
}
