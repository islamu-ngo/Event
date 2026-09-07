using Explore.Blazor.Client.Components.Discovery;
using Explore.Blazor.Client.Contracts.Interop;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Components.Discovery;

public sealed class HomeDiscoveryExperienceTests : IDisposable
{
    private readonly HomeDiscoveryTestContext context = new();
    private readonly IHomeDiscoveryService discoveryService = Substitute.For<IHomeDiscoveryService>();
    private readonly IHomeDiscoveryGeolocation geolocation = Substitute.For<IHomeDiscoveryGeolocation>();
    private readonly IAccessibilityFocusService focusService = Substitute.For<IAccessibilityFocusService>();

    public HomeDiscoveryExperienceTests()
    {
        context.Services.RemoveAll<IHomeDiscoveryService>();
        context.Services.RemoveAll<IHomeDiscoveryGeolocation>();
        context.Services.RemoveAll<ITranslationService>();
        context.Services.RemoveAll<IAccessibilityAnnouncerService>();
        context.Services.RemoveAll<IAccessibilityFocusService>();
        context.Services.AddSingleton(discoveryService);
        context.Services.AddSingleton(geolocation);
        var translation = Substitute.For<ITranslationService>();
        translation.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        context.Services.AddSingleton(translation);
        context.Services.AddSingleton(Substitute.For<IAccessibilityAnnouncerService>());
        context.Services.AddSingleton(focusService);
        context.Interop.SetupVoid("history.replaceState", _ => true).SetVoidResult();
    }

    public void Dispose() => context.Dispose();

    [Test]
    public async Task OneCompositePayloadRendersHeroAndThreeEventLayouts()
    {
        var areaId = Guid.NewGuid();
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(CompleteHome(areaId));

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='hero-carousel']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-testid='upcoming-event-list']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-card--DetailedList").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll(".event-card--SingleRow").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-card--CompactGrid").Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(cut.Markup).Contains("Upcoming in Brussels");
        await Assert.That(cut.Markup).Contains("Most viewed in Brussels");
        await Assert.That(cut.Markup).Contains("Most viewed online");
        await Assert.That(cut.Markup).Contains("Recently added");
        await discoveryService.Received(1).LoadAsync(null, null, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ContextUsesOneBrowsingHeadingAndDisclosure()
    {
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>())
            .Returns(CompleteHome(Guid.NewGuid()));

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        var heading = cut.Find("h1");
        var trigger = cut.Find("[data-testid='home-discovery-context-trigger']");
        await Assert.That(heading.TextContent).Contains("Browsing events in");
        await Assert.That(cut.Find(".hero-carousel__persistent-header .home-discovery__context-heading")).IsNotNull();
        await Assert.That(cut.Markup).DoesNotContain("Discover events");
        await Assert.That(trigger.TextContent).Contains("Brussels");
        await Assert.That(trigger.GetAttribute("aria-expanded")).IsEqualTo("false");

        trigger.Click();

        await Assert.That(cut.Find("[data-testid='home-discovery-context-trigger']")
            .GetAttribute("aria-expanded")).IsEqualTo("true");
        await Assert.That(cut.Markup).Contains("Use my current location");
        await Assert.That(cut.Markup).Contains("Browse online events");
    }

    [Test]
    public async Task CurrentLocationActionIsHiddenWithoutCentroidAreas()
    {
        var home = CompleteHome(Guid.NewGuid());
        var area = home.Context!.AvailableAreas!.Single();
        home = home with
        {
            Context = home.Context with
            {
                AvailableAreas = home.Context.AvailableAreas
                    .Select(candidate => candidate == area
                        ? candidate with { CentroidLatitude = null, CentroidLongitude = null }
                        : candidate with { })
                    .ToList()
            }
        };
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(home);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));
        OpenContextMenu(cut);

        await Assert.That(cut.Markup).DoesNotContain("Use my current location");
    }

    [Test]
    public async Task ExplicitLocationReducesOriginToAreaBeforeHistoryUpdateAndApiSelection()
    {
        var areaId = Guid.NewGuid();
        var initial = CompleteHome(areaId);
        var selected = CompleteHome(areaId);
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(initial);
        geolocation.GetCurrentPositionAsync(Arg.Any<CancellationToken>())
            .Returns(new HomeDiscoveryGeolocationResult(
                HomeDiscoveryGeolocationStatus.Available,
                50.8466,
                4.3528));
        discoveryService.FindClosestArea(
                Arg.Any<IEnumerable<PublicDiscoveryAreaDto>>(),
                50.8466,
                4.3528)
            .Returns(initial.Context!.AvailableAreas!.Single());
        discoveryService.SelectAreaAsync(areaId, Arg.Any<CancellationToken>()).Returns(selected);
        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));
        OpenContextMenu(cut);

        cut.FindAll("button").Single(button => button.TextContent.Contains("Use my current location", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() =>
            Assert.That(context.Interop.Invocations.Count(invocation =>
                    invocation.Identifier == "history.replaceState"))
                .IsEqualTo(1));

        await discoveryService.Received(1).SelectAreaAsync(areaId, Arg.Any<CancellationToken>());
        var uri = context.Interop.Invocations
            .Single(invocation => invocation.Identifier == "history.replaceState")
            .Arguments[2]
            ?.ToString() ?? string.Empty;
        await Assert.That(uri).Contains($"areaId={areaId}");
        await Assert.That(uri).DoesNotContain("50.8466");
        await Assert.That(uri).DoesNotContain("4.3528");
        await focusService.Received().FocusByIdAsync("home-discovery-context-trigger", true);
    }

    [Test]
    public async Task DeniedLocationKeepsCurrentContextWithoutSelectionCall()
    {
        var areaId = Guid.NewGuid();
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(CompleteHome(areaId));
        geolocation.GetCurrentPositionAsync(Arg.Any<CancellationToken>())
            .Returns(new HomeDiscoveryGeolocationResult(HomeDiscoveryGeolocationStatus.Denied));
        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));
        OpenContextMenu(cut);

        cut.FindAll("button").Single(button => button.TextContent.Contains("Use my current location", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Location permission was denied"));

        await discoveryService.DidNotReceive().SelectAreaAsync(
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task OnlineActionPreservesSelectedAreaId()
    {
        var areaId = Guid.NewGuid();
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(CompleteHome(areaId));
        var online = CompleteHome(areaId);
        online = online with { Context = online.Context! with { Mode = HomeDiscoveryMode.Online } };
        discoveryService.SelectOnlineAsync(areaId, Arg.Any<CancellationToken>()).Returns(online);
        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));
        OpenContextMenu(cut);

        cut.FindAll("button").Single(button => button.TextContent.Contains("Browse online events", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.That(cut.Find("[data-testid='home-discovery-context-trigger']").TextContent)
            .Contains("online events"));

        await discoveryService.Received(1).SelectOnlineAsync(areaId, Arg.Any<CancellationToken>());
        var uri = context.Interop.Invocations
            .Single(invocation => invocation.Identifier == "history.replaceState")
            .Arguments[2]
            ?.ToString() ?? string.Empty;
        await Assert.That(uri).Contains($"areaId={areaId}");
        await Assert.That(uri).Contains("mode=online");
    }

    [Test]
    public async Task EmptyHeroKeepsContextAndShowsCompactEmptyState()
    {
        var home = CompleteHome(Guid.NewGuid());
        home = home with { Hero = [] };
        home.SectionStatuses!["hero"] = HomeDiscoverySectionStatus.Empty;
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(home);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        await Assert.That(cut.Markup).Contains("No featured events match this browsing context yet");
        await Assert.That(cut.Find("[data-testid='home-discovery-context-trigger']")).IsNotNull();
    }

    [Test]
    public async Task AreaChoiceUsesDisclosureAndPreservesStableAreaOnly()
    {
        var currentAreaId = Guid.NewGuid();
        var selectedAreaId = Guid.NewGuid();
        var initial = CompleteHome(currentAreaId);
        initial.Context!.AvailableAreas!.Add(new PublicDiscoveryAreaDto
        {
            Id = selectedAreaId,
            DisplayName = "Antwerp",
            City = "Antwerp",
            CountryCode = "BE",
            SortOrder = 2
        });
        var selected = CompleteHome(selectedAreaId);
        selected = selected with { Context = selected.Context! with { SelectedAreaDisplayName = "Antwerp" } };
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(initial);
        discoveryService.SelectAreaAsync(selectedAreaId, Arg.Any<CancellationToken>()).Returns(selected);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));
        OpenContextMenu(cut);
        cut.FindAll("button").Single(button => button.TextContent.Contains("Antwerp", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() => Assert.That(cut.Find("[data-testid='home-discovery-context-trigger']").TextContent)
            .Contains("Antwerp"));

        await discoveryService.Received(1).SelectAreaAsync(selectedAreaId, Arg.Any<CancellationToken>());
        var uri = context.Interop.Invocations
            .Single(invocation => invocation.Identifier == "history.replaceState")
            .Arguments[2]
            ?.ToString() ?? string.Empty;
        await Assert.That(uri).Contains($"areaId={selectedAreaId}");
        await Assert.That(uri).DoesNotContain("Antwerp");
    }

    [Test]
    public async Task FailedSectionShowsBoundedMessageWhileSuccessfulSectionsRemain()
    {
        var home = CompleteHome(Guid.NewGuid());
        home = home with { UpcomingInArea = [] };
        home.SectionStatuses!["upcoming"] = HomeDiscoverySectionStatus.Failed;
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(home);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        await Assert.That(cut.Markup).Contains("This section is temporarily unavailable");
        await Assert.That(cut.Markup).Contains("Most viewed online");
        await Assert.That(cut.Markup).Contains("Recently added");
    }

    [Test]
    public async Task FederatedHomeEventRendersTypedDataWithoutInventingSourceAction()
    {
        var home = CompleteHome(Guid.NewGuid());
        home = home with
        {
            UpcomingInArea =
            [
                new EventDiscoveryItemDto
                {
                    Source = "atproto",
                    FederatedEvent = new FederatedEventDto
                    {
                        Name = "Federated neighborhood iftar",
                        Description = "Published from a community PDS",
                        StartsAtUtc = new DateTimeOffset(2026, 9, 4, 18, 30, 0, TimeSpan.Zero),
                        Mode = "in-person"
                    },
                    Federation = new EventFederationMetadataDto
                    {
                        AtprotoRecordId = Guid.NewGuid(),
                        Provenance = "AT Protocol"
                    }
                }
            ]
        };
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(home);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForElement("[data-testid='upcoming-event-row']", TimeSpan.FromSeconds(2));
        var row = cut.Find("[data-testid='upcoming-event-row']");

        await Assert.That(cut.Markup).Contains("Federated neighborhood iftar");
        await Assert.That(row.HasAttribute("href")).IsFalse();
        await Assert.That(row.GetAttribute("aria-label")).IsEqualTo("AT Protocol event: Federated neighborhood iftar");
    }

    [Test]
    public async Task FederatedHeroEventUsesDescriptiveServerSourceLink()
    {
        const string sourcePath = "/api/event/federated/source-record/source";
        var home = CompleteHome(Guid.NewGuid());
        var federated = new EventDiscoveryItemDto
        {
            Source = "atproto",
            FederatedEvent = new FederatedEventDto
            {
                Name = "Federated hero gathering",
                StartsAtUtc = new DateTimeOffset(2026, 9, 4, 18, 30, 0, TimeSpan.Zero)
            }
        };
        federated.AdditionalProperties["_links"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new Dictionary<string, HalLink>
            {
                ["source"] = new() { Href = sourcePath, Method = "GET" }
            });
        home = home with { Hero = [federated] };
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(home);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        var link = cut.WaitForElement(".hero-carousel__slide-link", TimeSpan.FromSeconds(2));

        await Assert.That(link.GetAttribute("href")).IsEqualTo(sourcePath);
        await Assert.That(link.GetAttribute("aria-label")).IsEqualTo("View AT Protocol source: Federated hero gathering");
    }

    [Test]
    public async Task RefreshedServerResultReplacesTombstonedFederatedCard()
    {
        var areaId = Guid.NewGuid();
        var stale = CompleteHome(areaId);
        stale = stale with
        {
            UpcomingInArea =
            [
                new EventDiscoveryItemDto
                {
                    Source = "atproto",
                    FederatedEvent = new FederatedEventDto
                    {
                        Name = "Tombstoned federated gathering",
                        StartsAtUtc = new DateTimeOffset(2026, 9, 4, 18, 30, 0, TimeSpan.Zero)
                    }
                }
            ]
        };
        var refreshed = CompleteHome(areaId);
        refreshed = refreshed with { Context = refreshed.Context! with { Mode = HomeDiscoveryMode.Online } };
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(stale);
        discoveryService.LoadAsync(null, "online", Arg.Any<CancellationToken>()).Returns(refreshed);

        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Tombstoned federated gathering"));

        cut.Render(parameters => parameters
            .Add(component => component.UrlMode, "online"));

        cut.WaitForAssertion(() => Assert.That(cut.Markup).DoesNotContain("Tombstoned federated gathering"));
        await Assert.That(cut.Markup).Contains("Upcoming event");
        await discoveryService.Received(1).LoadAsync(null, "online", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SupersededRefreshCannotRestoreCanceledStaleResult()
    {
        var areaId = Guid.NewGuid();
        var initial = CompleteHome(areaId);
        var stale = CompleteHome(areaId);
        stale = stale with { UpcomingInArea = [new EventDiscoveryItemDto { Event = Event("Canceled stale event", "stale") }] };
        var current = CompleteHome(areaId);
        current = current with { UpcomingInArea = [new EventDiscoveryItemDto { Event = Event("Current refreshed event", "current") }] };
        var staleCompletion = new TaskCompletionSource<HomeDiscoveryDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken staleCancellation = default;
        discoveryService.LoadAsync(null, null, Arg.Any<CancellationToken>()).Returns(initial);
        discoveryService.LoadAsync(null, "online", Arg.Any<CancellationToken>()).Returns(call =>
        {
            staleCancellation = call.ArgAt<CancellationToken>(2);
            return staleCompletion.Task;
        });
        discoveryService.LoadAsync(null, "all", Arg.Any<CancellationToken>()).Returns(current);
        var cut = context.RenderMudComponent<HomeDiscoveryExperience>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Upcoming event"));

        cut.Render(parameters => parameters.Add(component => component.UrlMode, "online"));
        cut.WaitForAssertion(() => Assert.That(staleCancellation.CanBeCanceled).IsTrue());
        cut.Render(parameters => parameters.Add(component => component.UrlMode, "all"));
        cut.WaitForAssertion(() => Assert.That(cut.Markup).Contains("Current refreshed event"));
        staleCompletion.SetResult(stale);

        cut.WaitForAssertion(() => Assert.That(cut.Markup).DoesNotContain("Canceled stale event"));
        await Assert.That(staleCancellation.IsCancellationRequested).IsTrue();
    }

    private static HomeDiscoveryDto CompleteHome(Guid areaId)
    {
        var hero = Event("Hero event", "hero");
        var upcoming = Event("Upcoming event", "upcoming");
        var spotlight = Event("Spotlight event", "spotlight");
        var viewedArea = Event("Viewed area event", "viewed-area");
        var viewedOnline = Event("Viewed online event", "viewed-online");
        var curated = Event("Curated event", "curated");
        var recent = Event("Recent event", "recent");
        return new HomeDiscoveryDto
        {
            Context = new HomeDiscoveryContextDto
            {
                Mode = HomeDiscoveryMode.Area,
                SelectedAreaId = areaId,
                SelectedAreaDisplayName = "Brussels",
                AvailableAreas =
                [
                    new PublicDiscoveryAreaDto
                    {
                        Id = areaId,
                        DisplayName = "Brussels",
                        CentroidLatitude = 50.85,
                        CentroidLongitude = 4.35
                    }
                ]
            },
            Hero = [new EventDiscoveryItemDto { Event = hero }],
            UpcomingInArea = [new EventDiscoveryItemDto { Event = upcoming }],
            Spotlight = new HomeDiscoverySectionDto
            {
                Key = "spotlight",
                Label = "Community spotlight",
                Items = [new EventDiscoveryItemDto { Event = spotlight }]
            },
            MostViewedInArea = [new EventDiscoveryItemDto { Event = viewedArea }],
            MostViewedOnline = [new EventDiscoveryItemDto { Event = viewedOnline }],
            CuratedSections =
            [
                new HomeDiscoverySectionDto
                {
                    Key = "family",
                    Label = "Family programs",
                    Items = [new EventDiscoveryItemDto { Event = curated }]
                }
            ],
            RecentlyAdded = [new EventDiscoveryItemDto { Event = recent }],
            SectionStatuses = new Dictionary<string, HomeDiscoverySectionStatus>
            {
                ["hero"] = HomeDiscoverySectionStatus.Available,
                ["upcoming"] = HomeDiscoverySectionStatus.Available,
                ["spotlight"] = HomeDiscoverySectionStatus.Available,
                ["most-viewed-area"] = HomeDiscoverySectionStatus.Available,
                ["most-viewed-online"] = HomeDiscoverySectionStatus.Available,
                ["curated:family"] = HomeDiscoverySectionStatus.Available,
                ["recently-added"] = HomeDiscoverySectionStatus.Available
            }
        };
    }

    private static EventListDto Event(string title, string slug) => new()
    {
        Id = Guid.NewGuid(),
        Title = title,
        Slug = slug,
        PublicCode = "abc123",
        EventFormatId = 1,
        EventFormatFullName = "In person",
        FirstSessionDate = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero)
    };

    private static void OpenContextMenu(IRenderedComponent<HomeDiscoveryExperience> cut)
    {
        cut.Find("[data-testid='home-discovery-context-trigger']").Click();
        cut.WaitForElement("[data-testid='home-discovery-context-menu']", TimeSpan.FromSeconds(2));
    }

    private sealed class HomeDiscoveryTestContext : BlazorTestContext
    {
        public Bunit.BunitJSInterop Interop => JSInterop;
    }
}
