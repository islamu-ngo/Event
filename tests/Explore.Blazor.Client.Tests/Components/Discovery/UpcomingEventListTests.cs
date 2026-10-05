using Explore.Blazor.Client.Components.Discovery;

namespace Explore.Blazor.Client.Tests.Components.Discovery;

public sealed class UpcomingEventListTests : IDisposable
{
    private readonly BlazorTestContext context = new();

    [Test]
    public async Task RendersSixRowsPerColumnAsDirectEventLinks()
    {
        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, CreateEvents(7)));

        await Assert.That(cut.FindAll("[data-testid='upcoming-event-column']").Count).IsEqualTo(2);
        await Assert.That(cut.FindAll("[data-testid='upcoming-event-row']").Count).IsEqualTo(7);
        await Assert.That(cut.FindAll(".event-card").Count).IsEqualTo(0);
        await Assert.That(cut.Find("[data-testid='upcoming-event-row']").GetAttribute("href"))
            .IsEqualTo("/events/upcoming-event-1-UP001");
        await Assert.That(cut.Markup).Contains("Sat, Aug 1");
        await Assert.That(cut.Markup).Contains("Community organizer");
    }

    [Test]
    public async Task RendersUpToThreeSixItemColumnsForResponsiveDisclosure()
    {
        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, CreateEvents(20)));

        await Assert.That(cut.FindAll("[data-testid='upcoming-event-column']").Count).IsEqualTo(3);
        await Assert.That(cut.FindAll("[data-testid='upcoming-event-row']").Count).IsEqualTo(18);
    }

    [Test]
    public async Task MissingFeaturedImageUsesLocalGeneratedArtwork()
    {
        var events = CreateEvents(1);
        events[0] = events[0] with { FeaturedImageUri = null };

        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, events));

        await Assert.That(cut.Find("img").GetAttribute("src") ?? string.Empty)
            .StartsWith("data:image/svg+xml");
    }

    [Test]
    public async Task MatchingOccurrenceTimePrecedesSecondarySessionTitle()
    {
        var item = CreateEvents(1).Single() with
        {
            MatchingSession = new()
            {
                Id = Guid.CreateVersion7(),
                Title = new string('x', 200),
                LocalStartDate = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
                LocalStartTime = TimeSpan.FromHours(9),
                StartsAtUtc = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero)
            }
        };
        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, [item]));

        var metadata = cut.Find(".upcoming-event-list__metadata");
        await Assert.That(metadata.FirstElementChild!.TagName).IsEqualTo("TIME");
        await Assert.That(metadata.QuerySelector("time")!.GetAttribute("datetime")).IsEqualTo("2026-10-04");
        await Assert.That(metadata.QuerySelector("time")!.TextContent).Contains("09:00");
        await Assert.That(cut.Find("[data-testid='upcoming-event-row']").GetAttribute("aria-label"))
            .Contains("09:00");
    }

    [Test]
    [Arguments(-1, false, true, false)]
    [Arguments(0, false, true, true)]
    [Arguments(3599, false, true, true)]
    [Arguments(3600, false, true, false)]
    [Arguments(1, true, false, true)]
    [Arguments(1, false, false, false)]
    public async Task OngoingStateUsesResponseInstantAndHalfOpenOccurrence(
        int secondsAfterStart, bool openEnded, bool hasEnd, bool expectedOngoing)
    {
        var start = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
        var item = CreateEvents(1).Single() with
        {
            MatchingSession = new()
            {
                Id = Guid.CreateVersion7(),
                LocalStartDate = start.Date,
                LocalStartTime = TimeSpan.FromHours(9),
                StartsAtUtc = start,
                EndsAtUtc = hasEnd ? start.AddHours(1) : null,
                IsOpenEnded = openEnded
            }
        };
        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, [item])
            .Add(component => component.ReferenceTimeUtc, start.AddSeconds(secondsAfterStart)));

        await Assert.That(cut.FindAll("[data-occurrence-state='ongoing']").Count)
            .IsEqualTo(expectedOngoing ? 1 : 0);
    }

    [Test]
    [Arguments("Pacific/Kiritimati", "today")]
    [Arguments("Pacific/Honolulu", "tomorrow")]
    [Arguments("Etc/UTC", "tomorrow")]
    [Arguments("unknown-zone", null)]
    public async Task RelativeDayUsesEventTimezoneRatherThanBrowserOrUtcDate(
        string timezone, string? expectedDay)
    {
        var item = CreateEvents(1).Single() with
        {
            Timezone = timezone,
            MatchingSession = new()
            {
                Id = Guid.CreateVersion7(),
                LocalStartDate = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                LocalStartTime = TimeSpan.FromHours(9),
                StartsAtUtc = new DateTimeOffset(2026, 5, 31, 19, 0, 0, TimeSpan.Zero)
            }
        };
        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, [item])
            .Add(component => component.ReferenceTimeUtc,
                new DateTimeOffset(2026, 5, 31, 11, 0, 0, TimeSpan.Zero)));

        await Assert.That(cut.Find(".upcoming-event-list__metadata time")
            .GetAttribute("data-relative-day")).IsEqualTo(expectedDay);
    }

    [Test]
    public async Task FederatedEventWithoutSourceAffordanceRendersWithoutLink()
    {
        var federatedEvent = CreateEvents(1).Single();
        federatedEvent.AdditionalProperties["eventDiscoverySource"] = "atproto";

        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, new[] { federatedEvent }));
        var row = cut.Find("[data-testid='upcoming-event-row']");

        await Assert.That(row.HasAttribute("href")).IsFalse();
        await Assert.That(row.GetAttribute("role")).IsEqualTo("article");
        await Assert.That(row.GetAttribute("aria-label"))
            .IsEqualTo($"AT Protocol event: {federatedEvent.Title}. {row.QuerySelector("time")!.TextContent}");
    }

    [Test]
    public async Task FederatedEventWithSafeInternalSourceHrefPreservesExactLink()
    {
        var federatedEvent = CreateEvents(1).Single();
        const string sourceHref = "/api/event/source?page=2";
        federatedEvent.AdditionalProperties["eventDiscoverySource"] = "atproto";
        federatedEvent.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = sourceHref, Method = "GET" }
            });

        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, new[] { federatedEvent }));
        var row = cut.Find("[data-testid='upcoming-event-row']");
        var externalLink = cut.Find("a.upcoming-event-list__external-link");

        await Assert.That(row.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(row.GetAttribute("aria-label"))
            .IsEqualTo($"View AT Protocol source: {federatedEvent.Title}. {row.QuerySelector("time")!.TextContent}");
        await Assert.That(externalLink.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(externalLink.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(externalLink.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
    }

    [Test]
    public async Task FederatedSourceRendersNewTabOpenAction()
    {
        var eventItem = CreateEvents(1).Single();
        const string sourceHref = "/api/event/source/upcoming-event";
        eventItem.AdditionalProperties["eventDiscoverySource"] = "atproto";
        eventItem.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = sourceHref, Method = "GET" }
            });

        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, new[] { eventItem }));

        var row = cut.Find("[data-testid='upcoming-event-row']");
        var externalLink = cut.Find("a.upcoming-event-list__external-link");

        await Assert.That(row.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(externalLink.TextContent).Contains("Open");
        await Assert.That(externalLink.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(externalLink.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(externalLink.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
        await Assert.That(externalLink.GetAttribute("aria-label"))
            .IsEqualTo("Open Upcoming event 1 on its external platform in a new tab");
    }

    [Test]
    [Arguments("javascript:alert(document.cookie)")]
    [Arguments("//example.test/source?access_token=credential-canary")]
    [Arguments("/api\\event")]
    [Arguments("/api/\u0001")]
    [Arguments("https://user:pass@example.test/source")]
    [Arguments("not-a-uri")]
    [Arguments("/api/event/source?access_token=credential-canary")]
    [Arguments("/api/event/source?ACCESS_TOKEN=credential-canary")]
    [Arguments("/api/event/source?access%5Ftoken=credential-canary")]
    [Arguments("/api/%")]
    public async Task FederatedEventWithHostileSourceHrefRendersWithoutLink(string hostileHref)
    {
        var federatedEvent = CreateEvents(1).Single();
        federatedEvent.AdditionalProperties["eventDiscoverySource"] = "atproto";
        federatedEvent.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = hostileHref, Method = "GET" }
            });

        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, new[] { federatedEvent }));
        var row = cut.Find("[data-testid='upcoming-event-row']");

        await Assert.That(row.HasAttribute("href")).IsFalse();
        await Assert.That(row.GetAttribute("role")).IsEqualTo("article");
        await Assert.That(cut.FindAll("a.upcoming-event-list__external-link")).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain("credential-canary");
    }

    public void Dispose()
    {
        context.Dispose();
        GC.SuppressFinalize(this);
    }

    private static List<EventListDto> CreateEvents(int count) => Enumerable.Range(1, count)
        .Select(index => new EventListDto
        {
            Id = Guid.NewGuid(),
            Title = $"Upcoming event {index}",
            Slug = $"upcoming-event-{index}",
            PublicCode = $"UP{index:D3}",
            FeaturedImageUri = $"https://example.test/upcoming/{index}.webp",
            ActorDisplayName = "Community organizer",
            EventFormatFullName = "In person",
            FirstSessionDate = new DateTimeOffset(2026, 8, index, 18, 0, 0, TimeSpan.Zero)
        })
        .ToList();
}
