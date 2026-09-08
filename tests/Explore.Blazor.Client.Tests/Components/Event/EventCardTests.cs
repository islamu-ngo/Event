using System.Globalization;
using EventCardComponent = Explore.Blazor.Client.Pages.Events.Components.EventCard;

namespace Explore.Blazor.Client.Tests.Components.Event;

public class EventCardTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private static readonly DateTimeOffset TestNow =
        new(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset TestDate =
        new(2026, 7, 25, 17, 0, 0, TimeSpan.Zero);
    private static readonly string ExpectedDetailedDate =
        $"{TestDate.ToString("ddd", CultureInfo.InvariantCulture)}, {TestDate.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant()} {TestDate:dd}, {TestDate.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
    private const string LongestTicketPriceSummary = "Free and other ticket options";

    public EventCardTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(TestNow));
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private static EventListDto CreateTestEvent() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Test Blazor Conference",
        Description = "A conference about Blazor development and modern web.",
        FirstSessionDate = TestDate,
        ActorDisplayName = "Test Organization",
        ActorTypeFullName = "Organization",
        EventTypeFullName = "Conference",
        EventStatusFullName = "Upcoming",
        EventFormatFullName = "In-Person",
        AudienceGenderFullName = "All",
        AudienceAgeFullName = "Adults",
        VisibilityTypeFullName = "Public"
    };

    private static EventListDto CreateTestEventWithLongestTicketPriceSummary() => CreateTestEvent() with
    {
        TicketPriceSummary = new EventTicketPriceSummaryDto
        {
            SummaryCode = "MIXED_WITH_FREE"
        }
    };

    private static string GetPriceSummarySelector(LayoutMode layout) => layout switch
    {
        LayoutMode.CompactGrid => ".event-card__hover-reveal .event-card__hover-item--price",
        LayoutMode.DetailedList => ".event-card__dl-chips .event-card__price-summary",
        LayoutMode.SingleRow => ".event-card__sr-chips .event-card__price-summary",
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, null)
    };

    [Test]
    public async Task EventCard_RendersTitle_InDetailedListLayout()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList));

        await Assert.That(cut.Markup).Contains("Test Blazor Conference");
    }

    [Test]
    public async Task EventCard_RendersTitle_InCompactGridLayout()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.CompactGrid));

        await Assert.That(cut.Markup).Contains("Test Blazor Conference");
    }

    [Test]
    public async Task EventCard_RendersTitle_InSingleRowLayout()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.SingleRow));

        await Assert.That(cut.Markup).Contains("Test Blazor Conference");
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCard_CommunityReportedBadge_CannotBeConfiguredAway(LayoutMode layout)
    {
        var eventDto = CreateTestEvent();
        eventDto = eventDto with { ProvenanceTypeCode = "COMMUNITY_REPORTED" };
        var visibility = new Dictionary<string, bool>
        {
            ["event_list.card.show_date"] = false,
            ["event_list.card.show_location"] = false,
            ["event_list.card.show_organizer"] = false,
            ["event_list.card.show_description"] = false,
            ["event_list.card.show_price"] = false,
            ["event_list.card.show_status"] = false,
            ["event_list.card.show_provenance"] = false
        };

        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, eventDto)
            .Add(component => component.Layout, layout)
            .Add(component => component.CardFieldVisibility, visibility));

        await Assert.That(cut.FindAll(".event-card__provenance-badge").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).Contains("Community reported");
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCardImagesAreAccessibleLazyAndLayoutStable(LayoutMode layout)
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, layout));

        var image = cut.Find("img");

        await Assert.That(image.GetAttribute("alt")).IsEqualTo("Test Blazor Conference");
        await Assert.That(image.GetAttribute("loading")).IsEqualTo("lazy");
        await Assert.That(image.GetAttribute("decoding")).IsEqualTo("async");
        await Assert.That(image.GetAttribute("width")).IsNotNull().And.IsNotEmpty();
        await Assert.That(image.GetAttribute("height")).IsNotNull().And.IsNotEmpty();
    }

    [Test]
    public async Task EventCard_ShowsAllFields_WhenNoCardFieldVisibility()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList));

        // All fields visible by default when CardFieldVisibility is null
        await Assert.That(cut.Markup).Contains(ExpectedDetailedDate);
        await Assert.That(cut.Markup).Contains("Test Organization");
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCard_LongestTicketPriceSummary_RendersInLayoutPriceStructure(LayoutMode layout)
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, CreateTestEventWithLongestTicketPriceSummary())
            .Add(component => component.Layout, layout));

        var summary = cut.Find(GetPriceSummarySelector(layout));

        await Assert.That(summary.TextContent).Contains(LongestTicketPriceSummary);
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCard_NullTicketPriceSummary_OmitsPriceStructure(LayoutMode layout)
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, CreateTestEvent())
            .Add(component => component.Layout, layout));

        await Assert.That(cut.FindAll(GetPriceSummarySelector(layout))).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain(LongestTicketPriceSummary);
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCard_DisabledPriceVisibility_HidesTicketPriceSummary(LayoutMode layout)
    {
        var visibility = new Dictionary<string, bool>
        {
            ["event_list.card.show_price"] = false
        };
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, CreateTestEventWithLongestTicketPriceSummary())
            .Add(component => component.Layout, layout)
            .Add(component => component.CardFieldVisibility, visibility));

        await Assert.That(cut.FindAll(GetPriceSummarySelector(layout))).IsEmpty();
        await Assert.That(cut.Markup).DoesNotContain(LongestTicketPriceSummary);
    }

    [Test]
    public async Task EventCard_HidesDateField_WhenVisibilityDisabled()
    {
        var visibility = new Dictionary<string, bool>
        {
            ["event_list.card.show_date"] = false
        };

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList)
            .Add(x => x.CardFieldVisibility, visibility));

        // Date hidden but title still visible
        await Assert.That(cut.Markup).DoesNotContain(ExpectedDetailedDate);
        await Assert.That(cut.Markup).Contains("Test Blazor Conference");
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCard_FormatsCurrentYearScheduleWithoutYear(LayoutMode layout)
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, layout));

        await Assert.That(cut.Markup).Contains(ExpectedDetailedDate);
        await Assert.That(cut.Markup).DoesNotContain($"{TestDate:dd}, {TestDate:yyyy}");
    }

    [Test]
    public async Task EventCard_FormatsOtherYearScheduleWithYear()
    {
        var eventDto = CreateTestEvent();
        var pastDate = new DateTimeOffset(2025, 7, 25, 17, 0, 0, TimeSpan.Zero);
        eventDto = eventDto with { FirstSessionDate = pastDate };
        var expected =
            $"{pastDate.ToString("ddd", CultureInfo.InvariantCulture)}, {pastDate.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant()} {pastDate:dd}, {pastDate:yyyy}, {pastDate.ToString("h:mm tt", CultureInfo.InvariantCulture)}";

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, eventDto)
            .Add(x => x.Layout, LayoutMode.DetailedList));

        await Assert.That(cut.Markup).Contains(expected);
    }

    [Test]
    public async Task EventCard_HidesOrganizerField_WhenVisibilityDisabled()
    {
        var visibility = new Dictionary<string, bool>
        {
            ["event_list.card.show_organizer"] = false
        };

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList)
            .Add(x => x.CardFieldVisibility, visibility));

        await Assert.That(cut.Markup).DoesNotContain("Test Organization");
        await Assert.That(cut.Markup).Contains("Test Blazor Conference");
    }

    [Test]
    public async Task EventCard_AppliesCorrectCssClass_ForLayout()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.CompactGrid));

        await Assert.That(cut.Markup).Contains("event-card--CompactGrid");
    }

    [Test]
    public async Task EventCard_RendersShareButton_WithAccessibleLabel()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList)
            .Add(x => x.OnShareRequested, EventCallback.Factory.Create<EventListDto>(this, _ => { })));

        var shareButton = cut.Find("button[aria-label='Share event: Test Blazor Conference']");

        await Assert.That(shareButton).IsNotNull();
    }

    [Test]
    public async Task EventCard_WhenPast_RendersEndedBadgeAndSuppressesShareAction()
    {
        var eventDto = CreateTestEvent();
        eventDto = eventDto with { IsPast = true };

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, eventDto)
            .Add(x => x.Layout, LayoutMode.DetailedList)
            .Add(x => x.OnShareRequested, EventCallback.Factory.Create<EventListDto>(this, _ => { })));

        await Assert.That(cut.Markup).Contains("event-card--past");
        await Assert.That(cut.Markup).Contains("Ended");
        await Assert.That(cut.Markup).DoesNotContain("Share event: Test Blazor Conference");
    }

    [Test]
    public async Task EventCard_ShareButton_InvokesShareCallbackWithoutSelectingCard()
    {
        var shareCount = 0;
        var selectCount = 0;
        EventListDto? sharedEvent = null;
        var eventDto = CreateTestEvent();

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, eventDto)
            .Add(x => x.Layout, LayoutMode.DetailedList)
            .Add(x => x.OnClick, EventCallback.Factory.Create<EventListDto>(this, _ => selectCount++))
            .Add(x => x.OnShareRequested, EventCallback.Factory.Create<EventListDto>(this, evt =>
            {
                shareCount++;
                sharedEvent = evt;
            })));

        cut.Find("button[aria-label='Share event: Test Blazor Conference']").Click();

        await Assert.That(shareCount).IsEqualTo(1);
        await Assert.That(selectCount).IsEqualTo(0);
        await Assert.That(sharedEvent).IsSameReferenceAs(eventDto);
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task EventCard_WithFederatedSource_RendersIsolatedNewTabLink(LayoutMode layout)
    {
        var eventDto = CreateTestEvent();
        const string sourceHref = "/api/event/source/conference";
        SetFederatedSource(eventDto, sourceHref);

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, eventDto)
            .Add(x => x.Layout, layout));

        var link = cut.Find("a.event-card__external-link");
        await Assert.That(link.TextContent).Contains("Open");
        await Assert.That(link.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
        await Assert.That(link.GetAttribute("aria-label"))
            .IsEqualTo("Open Test Blazor Conference on its external platform in a new tab");
    }

    [Test]
    public async Task EventCard_WithoutSafeFederatedSource_HidesExternalLink()
    {
        var eventDto = CreateTestEvent();
        SetFederatedSource(eventDto, "javascript:alert('unsafe')");

        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, eventDto)
            .Add(x => x.Layout, LayoutMode.DetailedList));

        await Assert.That(cut.FindAll("a.event-card__external-link")).IsEmpty();
    }

    private static void SetFederatedSource(EventListDto eventDto, string href)
    {
        eventDto.AdditionalProperties["eventDiscoverySource"] = "atproto";
        eventDto.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = href, Method = "GET" }
            });
    }

    [Test]
    public async Task EventCardRendersLabeledKeyboardTarget()
    {
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList));

        var card = cut.Find(".event-card");

        await Assert.That(card.GetAttribute("role")).IsEqualTo("button");
        await Assert.That(card.GetAttribute("tabindex")).IsEqualTo("0");
        await Assert.That(card.GetAttribute("aria-label")).IsEqualTo("View event: Test Blazor Conference");
    }

    [Test]
    public async Task EventCardKeyDownWithEnterInvokesCardCallback()
    {
        var selectCount = 0;
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.DetailedList)
            .Add(x => x.OnClick, EventCallback.Factory.Create<EventListDto>(this, _ => selectCount++)));

        await cut.Find(".event-card").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        await Assert.That(selectCount).IsEqualTo(1);
    }

    [Test]
    public async Task EventCardKeyDownWithSpaceInvokesCardCallback()
    {
        var selectCount = 0;
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.CompactGrid)
            .Add(x => x.OnClick, EventCallback.Factory.Create<EventListDto>(this, _ => selectCount++)));

        await cut.Find(".event-card").KeyDownAsync(new KeyboardEventArgs { Key = " " });

        await Assert.That(selectCount).IsEqualTo(1);
    }

    [Test]
    public async Task EventCardKeyDownWithUnrelatedKeyDoesNotInvokeCardCallback()
    {
        var selectCount = 0;
        var cut = _ctx.RenderMudComponent<EventCardComponent>(p => p
            .Add(x => x.Event, CreateTestEvent())
            .Add(x => x.Layout, LayoutMode.SingleRow)
            .Add(x => x.OnClick, EventCallback.Factory.Create<EventListDto>(this, _ => selectCount++)));

        await cut.Find(".event-card").KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        await Assert.That(selectCount).IsEqualTo(0);
    }

    [Test]
    public async Task FederatedEventCard_UsesOnlyServerSourceAffordance()
    {
        var selectCount = 0;
        var eventDto = CreateTestEvent();
        const string sourceHref = "/api/event/federated/record/source";
        eventDto = eventDto with { Id = null };
        eventDto.AdditionalProperties["eventDiscoverySource"] = "atproto";
        eventDto.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = sourceHref, Method = "GET" }
            });
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, eventDto)
            .Add(component => component.Layout, LayoutMode.DetailedList)
            .Add(component => component.OnClick, EventCallback.Factory.Create<EventListDto>(this, _ => selectCount++))
            .Add(component => component.OnShareRequested, EventCallback.Factory.Create<EventListDto>(this, _ => { })));

        var externalLink = cut.Find("a.event-card__external-link");
        await Assert.That(externalLink.GetAttribute("href")).IsEqualTo(sourceHref);
        await Assert.That(externalLink.GetAttribute("target")).IsEqualTo("_blank");

        cut.Find(".event-card").Click();

        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        await Assert.That(navigation.Uri).EndsWith("/api/event/federated/record/source");
        await Assert.That(selectCount).IsEqualTo(0);
        await Assert.That(cut.Markup).Contains("AT Protocol");
        await Assert.That(cut.Markup).DoesNotContain("Share event: Test Blazor Conference");
    }

    [Test]
    public async Task FederatedEventCard_WithoutSourceAffordance_IsNonInteractive()
    {
        var selectCount = 0;
        var eventDto = CreateTestEvent();
        eventDto = eventDto with { Id = null };
        eventDto.AdditionalProperties["eventDiscoverySource"] = "atproto";
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, eventDto)
            .Add(component => component.Layout, LayoutMode.CompactGrid)
            .Add(component => component.OnClick, EventCallback.Factory.Create<EventListDto>(this, _ => selectCount++)));

        var card = cut.Find(".event-card");
        card.Click();

        await Assert.That(card.GetAttribute("role")).IsEqualTo("article");
        await Assert.That(card.GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(card.GetAttribute("aria-label")).IsEqualTo("AT Protocol event: Test Blazor Conference");
        await Assert.That(selectCount).IsEqualTo(0);
    }

    [Test]
    public async Task LocalEventCard_WithFailedPdsDelivery_ShowsStableRecoveryGuidanceOnly()
    {
        var eventDto = CreateTestEvent();
        eventDto = eventDto with { AtprotoDeliveryStatus = "failed", AtprotoDeliveryFailureCode = "session_unavailable" };
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, eventDto)
            .Add(component => component.Layout, LayoutMode.DetailedList));

        await Assert.That(cut.Markup).Contains("AT Protocol delivery needs attention");
        await Assert.That(cut.Markup).Contains("Reconnect your AT Protocol account");
        await Assert.That(cut.Markup).DoesNotContain("session_unavailable");
    }

    [Test]
    public async Task LocalEventCard_WithUnknownPdsFailure_RendersBoundedGuidanceWithoutRawFailure()
    {
        var eventDto = CreateTestEvent();
        eventDto = eventDto with { AtprotoDeliveryStatus = "failed", AtprotoDeliveryFailureCode = "HTTP 500 provider body: private upstream detail" };
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, eventDto)
            .Add(component => component.Layout, LayoutMode.SingleRow));

        await Assert.That(cut.Markup).Contains("AT Protocol delivery needs attention");
        await Assert.That(cut.Markup).Contains("Review the event's public data");
        await Assert.That(cut.Markup).DoesNotContain("private upstream detail");
        await Assert.That(cut.FindAll("button")).IsEmpty();
    }

    [Test]
    [Arguments(LayoutMode.CompactGrid)]
    [Arguments(LayoutMode.DetailedList)]
    [Arguments(LayoutMode.SingleRow)]
    public async Task FederatedEventStateSemanticsPersistAcrossNarrowAndWideLayoutClasses(LayoutMode layout)
    {
        var eventDto = CreateTestEvent();
        eventDto = eventDto with { Id = null };
        eventDto.AdditionalProperties["eventDiscoverySource"] = "atproto";
        var cut = _ctx.RenderMudComponent<EventCardComponent>(parameters => parameters
            .Add(component => component.Event, eventDto)
            .Add(component => component.Layout, layout));
        var card = cut.Find(".event-card");

        await Assert.That(card.ClassList).Contains($"event-card--{layout}");
        await Assert.That(card.GetAttribute("role")).IsEqualTo("article");
        await Assert.That(card.GetAttribute("tabindex")).IsEqualTo("-1");
        await Assert.That(card.GetAttribute("aria-label")).IsEqualTo("AT Protocol event: Test Blazor Conference");
        await Assert.That(card.TextContent).Contains("AT Protocol");
    }

}
