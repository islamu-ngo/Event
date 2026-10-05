using Explore.Blazor.Client.Components.Collection;
using Explore.Blazor.Client.Components.Discovery;
using Explore.Blazor.Client.Components.Events;
using Explore.Blazor.Client.Components.Presentation;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Components;

public sealed class DiscoveryOccurrencePresentationTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    [Test]
    public async Task DiscoveryConsumersRenderMatchingLocalDateInsteadOfAggregateStart()
    {
        var item = CreateEvent();
        var list = _ctx.RenderMudComponent<UpcomingEventList>(p => p.Add(c => c.Events, new[] { item }));
        using var heroContext = new BlazorTestContext();
        var hero = heroContext.RenderMudComponent<HeroCarousel>(p => p.Add(c => c.Events, new[] { item }));

        foreach (var time in new[] { list.Find("time"), hero.Find("time") })
        {
            await Assert.That(time.GetAttribute("datetime")).IsEqualTo("2026-06-01");
            await Assert.That(time.TextContent).Contains(item.MatchingSession!.LocalStartDate.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture));
            await Assert.That(time.TextContent).DoesNotContain(item.FirstSessionDate!.Value.ToString("ddd, MMM d", System.Globalization.CultureInfo.CurrentCulture));
        }
    }

    [Test]
    [Arguments(null)]
    [Arguments(0)]
    [Arguments(2)]
    public async Task AdditionalMatchesAreRenderedOnlyWhenKnownAndPositive(int? count)
    {
        var item = CreateEvent() with { AdditionalSessionCount = count, SessionCount = 500 };
        var list = _ctx.RenderMudComponent<UpcomingEventList>(p => p.Add(c => c.Events, new[] { item }));
        using var heroContext = new BlazorTestContext();
        var hero = heroContext.RenderMudComponent<HeroCarousel>(p => p.Add(c => c.Events, new[] { item }));
        foreach (var cut in new[] { list.FindAll("[data-additional-session-count]"), hero.FindAll("[data-additional-session-count]") })
        {
            await Assert.That(cut.Count).IsEqualTo(count > 0 ? 1 : 0);
            if (count > 0)
                await Assert.That(cut[0].GetAttribute("data-additional-session-count")).IsEqualTo("2");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TimelineGroupsByMatchingCalendarDateWithoutTimezoneConversion(bool isPast)
    {
        var cut = _ctx.RenderMudComponent<EventTimeline>(p => p
            .Add(c => c.Events, new[] { CreateEvent() }).Add(c => c.IsPast, isPast));
        await Assert.That(cut.FindComponent<EventTimelineGroup>().Instance.Date).IsEqualTo(new DateTime(2026, 6, 1));
    }

    [Test]
    public async Task RemoteProjectionWithoutLocalGraphPreservesPublishedDateAndSourceAffordance()
    {
        var item = CreateEvent() with { MatchingSession = null };
        item.AdditionalProperties["eventDiscoverySource"] = "atproto";
        item.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink> { ["source"] = new() { Href = "/api/events/source", Method = "GET" } });
        var list = _ctx.RenderMudComponent<UpcomingEventList>(p => p.Add(c => c.Events, new[] { item }));
        using var heroContext = new BlazorTestContext();
        var hero = heroContext.RenderMudComponent<HeroCarousel>(p => p.Add(c => c.Events, new[] { item }));
        await Assert.That(list.Find("time").GetAttribute("datetime")).IsEqualTo("2026-04-01");
        await Assert.That(hero.Find("time").GetAttribute("datetime")).IsEqualTo("2026-04-01");
        await Assert.That(list.Find("a.upcoming-event-list__external-link").GetAttribute("href")).IsEqualTo("/api/events/source");
        await Assert.That(hero.Find("a.hero-carousel__external-link").GetAttribute("href")).IsEqualTo("/api/events/source");
    }

    [Test]
    public async Task CommunityAttributionSeparatesContributorFromSourceAndGrantsNoActions()
    {
        _ctx.Services.AddSingleton(provider => new UiShellState(
            provider.GetRequiredService<NavigationManager>(), new WorkspaceRouteClassifier(new WorkspaceRegistry())));
        _ctx.Services.AddSingleton(Substitute.For<IEventService>());
        _ctx.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventOrganizerClaimService>());
        var cut = _ctx.RenderMudComponent<EventProvenancePanel>(p => p.Add(c => c.Event, new EventDto
        {
            Id = Guid.CreateVersion7(),
            Title = "Reported event",
            ProvenanceTypeCode = "COMMUNITY_REPORTED",
            ActorDisplayName = "Contributor account",
            SourcePublisherName = "Original publisher"
        }));
        await Assert.That(cut.Find("[data-attribution='contributor']").TextContent).Contains("Contributor account");
        await Assert.That(cut.Find("[data-attribution='source-publisher']").TextContent).Contains("Original publisher");
        await Assert.That(cut.FindAll("button")).IsEmpty();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    private static EventListDto CreateEvent() => new()
    {
        Id = Guid.CreateVersion7(),
        Title = "Multi-session event",
        Slug = "multi-session",
        PublicCode = "MATCH",
        FirstSessionDate = new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
        LastSessionDate = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
        MatchingSession = new()
        {
            Id = Guid.CreateVersion7(),
            Title = "June occurrence",
            LocalStartDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(14)),
            LocalStartTime = TimeSpan.FromHours(9),
            StartsAtUtc = new DateTimeOffset(2026, 5, 31, 19, 0, 0, TimeSpan.Zero),
            IsOpenEnded = true
        }
    };
}
