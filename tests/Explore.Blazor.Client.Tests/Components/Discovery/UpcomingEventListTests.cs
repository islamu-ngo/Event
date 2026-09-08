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
    public async Task FederatedEventWithoutSourceAffordanceRendersWithoutLink()
    {
        var federatedEvent = CreateEvents(1).Single();
        federatedEvent.AdditionalProperties["eventDiscoverySource"] = "atproto";

        var cut = context.RenderMudComponent<UpcomingEventList>(parameters => parameters
            .Add(component => component.Events, new[] { federatedEvent }));
        var row = cut.Find("[data-testid='upcoming-event-row']");

        await Assert.That(row.HasAttribute("href")).IsFalse();
        await Assert.That(row.GetAttribute("role")).IsEqualTo("article");
        await Assert.That(row.GetAttribute("aria-label")).IsEqualTo("AT Protocol event: Upcoming event 1");
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
        await Assert.That(row.GetAttribute("aria-label")).IsEqualTo("View AT Protocol source: Upcoming event 1");
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
