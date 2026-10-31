using System.Text.Json;
using Explore.Blazor.Client.Helpers;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public class EventListTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IEventService _eventService;
    private readonly Explore.Blazor.Client.Contracts.Services.IEventSessionService _eventSessionService;
    private readonly ICategoryService _categoryService;
    private readonly ITagService _tagService;
    private readonly Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService _eventLookupService;
    private readonly Explore.Blazor.Client.Contracts.Services.Lookup.IDemographicLookupService _demographicLookupService;
    private readonly Explore.Blazor.Client.Contracts.Services.Lookup.ICultureLookupService _cultureLookupService;
    private readonly ILocationService _locationService;
    private readonly IPublicExperienceService _publicExperienceService;
    private readonly DockLayoutState _dockLayoutState;
    private readonly IDockLayoutPersistence _dockLayoutPersistence;

    public EventListTests()
    {
        _ctx = new BlazorTestContext();

        _eventService = Substitute.For<IEventService>();
        _eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        _categoryService = Substitute.For<ICategoryService>();
        _tagService = Substitute.For<ITagService>();
        _eventLookupService = Substitute.For<Explore.Blazor.Client.Contracts.Services.Lookup.IEventLookupService>();
        _demographicLookupService = Substitute.For<Explore.Blazor.Client.Contracts.Services.Lookup.IDemographicLookupService>();
        _cultureLookupService = Substitute.For<Explore.Blazor.Client.Contracts.Services.Lookup.ICultureLookupService>();
        _locationService = Substitute.For<ILocationService>();
        _publicExperienceService = Substitute.For<IPublicExperienceService>();
        _dockLayoutState = new DockLayoutState();
        _dockLayoutPersistence = Substitute.For<IDockLayoutPersistence>();
        _dockLayoutPersistence.LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(null));
        _dockLayoutPersistence.SaveAsync(Arg.Any<DockLayoutSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
        _dockLayoutPersistence.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        _ctx.Services.AddSingleton(_eventService);
        _ctx.Services.AddSingleton(_eventSessionService);
        _ctx.Services.AddSingleton(_categoryService);
        _ctx.Services.AddSingleton(_tagService);
        _ctx.Services.AddSingleton(_eventLookupService);
        _ctx.Services.AddSingleton(_demographicLookupService);
        _ctx.Services.AddSingleton(_cultureLookupService);
        _ctx.Services.AddSingleton(_locationService);
        _ctx.Services.AddSingleton(_publicExperienceService);
        _ctx.Services.AddSingleton(_dockLayoutState);
        _ctx.Services.AddSingleton(_dockLayoutPersistence);

        _ctx.Services.AddSingleton(Substitute.For<IUserService>());
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
        _ctx.Services.AddSingleton(Substitute.For<ILogger<EventList>>());
        _ctx.Services.AddSingleton(Substitute.For<IContactShareConsentService>());
        _ctx.Services.AddSingleton(Substitute.For<IUserSettingsService>());
        _ctx.Services.AddSingleton(new FeatureStateContainer());

        SetupDefaultLookupResponses();
        _publicExperienceService.GetSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            IsIslamicModuleEnabled = true,
            IsTechModuleEnabled = true
        });
        _publicExperienceService.GetCachedShellAsync().Returns(Task.FromResult<PublicExperienceShellDto?>(null));
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    private void SetupDefaultLookupResponses()
    {
        _eventLookupService.GetEventTypesAsync().Returns(new List<EventTypeListDto>());
        _demographicLookupService.GetAudienceGendersAsync().Returns(new List<AudienceGenderListDto>());
        _demographicLookupService.GetAudienceAgesAsync().Returns(new List<AudienceAgeListDto>());
        _eventLookupService.GetEventStatusesAsync().Returns(new List<EventStatusListDto>());
        _eventLookupService.GetEventFormatsAsync().Returns(new List<EventFormatListDto>());
        _categoryService.GetAllCategoriesAsync().Returns(new List<CategoryListDto>());
        _tagService.GetAllTagsAsync().Returns(new List<TagListDto>());
        _cultureLookupService.GetMadhabsAsync().Returns(new List<MadhabListDto>());
        _locationService.GetAllLocationsAsync().Returns(new List<LocationListDto>());
        _eventLookupService.GetRegistrationModesAsync().Returns(new List<RegistrationModeListDto>());
        _cultureLookupService.GetLanguagesAsync().Returns(new List<LanguageListDto>());
    }

    private static EventDiscoveryTraversalResource CreateResult(int pageNumber, int pageSize, List<EventListDto> items)
    {
        return new EventDiscoveryTraversalResource
        {
            SnapshotCount = items.Count,
            _embedded = new HalCollectionEmbeddedOfEventDiscoveryItemDto
            {
                Items = items.Select(item => new HalResourceOfEventDiscoveryItemDto
                {
                    Source = "local",
                    Event = item
                }).ToList()
            }
        };
    }

    [Test]
    public async Task LoadMore_FollowsHalCursorAndKeepsOriginalSearchWithoutRandomPages()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = CreateResult(1, 20, [new() { Id = firstId, Title = "First identity" }]) with
        {
            SnapshotCount = 2,
            HasMore = true,
            _links = new Dictionary<string, HalLink>
            {
                ["next"] = new() { Href = "/api/Event?cursor=opaque%2Bnext&pageSize=1&searchTerm=community" }
            }
        };
        var second = CreateResult(1, 20, [new() { Id = secondId, Title = "Second identity" }]) with { SnapshotCount = 2 };
        using var handler = new DiscoveryHttpHandler((200, first), (200, second));
        using var http = RegisterDiscoveryHttpClient(handler);
        _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/events?q=community");
        var cut = _ctx.RenderMudComponent<EventList>();

        await cut.FindComponent<Explore.Blazor.Client.Pages.Events.Components.EventListPagination>()
            .Find("button").ClickAsync(new MouseEventArgs());

        var ids = cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()
            .Select(card => card.Instance.Event.Id).ToArray();
        await Assert.That(ids).IsEquivalentTo(new Guid?[] { firstId, secondId });
        var query = System.Web.HttpUtility.ParseQueryString(handler.Requests[1].Query);
        await Assert.That(query["cursor"]).IsEqualTo("opaque+next");
        await Assert.That(query["pageSize"]).IsEqualTo("1");
        await Assert.That(query["searchTerm"]).IsEqualTo("community");
        await Assert.That(query["pageNumber"]).IsNull();
        await Assert.That(cut.FindComponents<MudPagination>().Count).IsEqualTo(0);
        await Assert.That(cut.FindComponent<Explore.Blazor.Client.Pages.Events.Components.EventListPagination>()
            .Instance.HasMore).IsFalse();
    }

    [Test]
    [Arguments(400, "discovery_cursor_invalid")]
    [Arguments(410, "discovery_cursor_expired")]
    [Arguments(409, "discovery_restart_required")]
    [Arguments(503, "discovery_unavailable")]
    public async Task RejectedContinuation_OffersExplicitReplacementAndKeepsSearch(int status, string code)
    {
        var oldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var initial = CreateResult(1, 20, [new() { Id = oldId, Title = "Previously loaded identity" }]) with
        {
            SnapshotCount = 2,
            HasMore = true,
            _links = new Dictionary<string, HalLink>
            {
                ["next"] = new() { Href = "/api/Event?cursor=old&pageSize=20&searchTerm=community" }
            }
        };
        var problem = new ProblemDetails
        {
            Status = status,
            AdditionalProperties = new Dictionary<string, object> { ["code"] = code }
        };
        var replacement = CreateResult(1, 20, [new() { Id = newId, Title = "Current identity" }]);
        using var handler = new DiscoveryHttpHandler((200, initial), (status, problem), (200, replacement));
        using var http = RegisterDiscoveryHttpClient(handler);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/events?q=community");
        var cut = _ctx.RenderMudComponent<EventList>();

        await cut.FindComponent<Explore.Blazor.Client.Pages.Events.Components.EventListPagination>()
            .Find("button").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindAll(".event-list__recovery").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventListPagination>().Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("[data-discovery-state]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()
            .Select(card => card.Instance.Event.Id)).IsEquivalentTo(new Guid?[] { oldId });
        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(navigation.Uri).Contains("q=community");

        await cut.Find(".event-list__recovery button").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()
            .Select(card => card.Instance.Event.Id)).IsEquivalentTo(new Guid?[] { newId });
        var query = System.Web.HttpUtility.ParseQueryString(handler.Requests[2].Query);
        await Assert.That(query["cursor"]).IsNull();
        await Assert.That(query["searchTerm"]).IsEqualTo("community");
        await Assert.That(cut.FindAll(".event-list__recovery").Count).IsEqualTo(0);
    }

    [Test]
    public async Task TruncatedEmptyMembership_IsNotAnExhaustiveEmptySearch()
    {
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, []) with { Truncated = true }));
        var cut = _ctx.RenderMudComponent<EventList>();
        await Assert.That(cut.FindAll(".event-list__truncated").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-list__truncated + button").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("No events found");
        await Assert.That(cut.FindAll("[data-discovery-state]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
    }

    [Test]
    public async Task InitialUnavailable_DoesNotClaimNoMatches()
    {
        SetupPagedResult(Task.FromException<EventDiscoveryTraversalResource>(new HttpRequestException("Unavailable")));
        var cut = _ctx.RenderMudComponent<EventList>();
        await Assert.That(cut.FindAll(".event-list__recovery").Count).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("No events found");
        await Assert.That(cut.FindAll("[data-discovery-state]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
    }

    [Test]
    public async Task EmptyUpcoming_PastActionQueriesExistingDiscoveryWithoutInventingEvents()
    {
        using var handler = new DiscoveryHttpHandler(
            (200, CreateResult(1, 20, [])),
            (200, CreateResult(1, 20, [])));
        using var http = RegisterDiscoveryHttpClient(handler);
        var cut = _ctx.RenderMudComponent<EventList>();

        await Assert.That(cut.FindAll("[data-discovery-state='upcoming']").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()).IsEmpty();
        await Assert.That(handler.Requests.Count).IsEqualTo(1);
        await Assert.That(System.Web.HttpUtility.ParseQueryString(handler.Requests[0].Query)["view"])
            .IsEqualTo("UpcomingAndOngoing");

        await cut.Find("[data-discovery-action='past']").ClickAsync(new MouseEventArgs());

        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(handler.Requests[1].AbsolutePath).IsEqualTo(handler.Requests[0].AbsolutePath);
        var query = System.Web.HttpUtility.ParseQueryString(handler.Requests[1].Query);
        await Assert.That(query["view"]).IsEqualTo("Past");
        await Assert.That(query["cursor"]).IsNull();
        await Assert.That(cut.FindAll("[data-discovery-state='past']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-state='upcoming']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()).IsEmpty();
    }

    [Test]
    public async Task EmptyFilteredUpcoming_PastActionRetainsSearchFiltersAndCuratedUrls()
    {
        var actorId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        const string curatedUrl = "/events?AudienceAgeIds=2";
        _publicExperienceService.GetCachedShellAsync().Returns(Task.FromResult<PublicExperienceShellDto?>(new()
        {
            EventSections =
            [
                new PublicExperienceEventSectionDto { Key = "youth", Label = "Youth Programs", Url = curatedUrl }
            ]
        }));
        using var handler = new DiscoveryHttpHandler(
            (200, CreateResult(1, 20, [])),
            (200, CreateResult(1, 20, [])),
            (200, CreateResult(1, 20, [])));
        using var http = RegisterDiscoveryHttpClient(handler);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/events?q=community&actorId={actorId}&organizationId={organizationId}&groupId={groupId}");
        var cut = _ctx.RenderMudComponent<EventList>();
        var filter = cut.FindComponent<Explore.Blazor.Client.Pages.Events.Components.EventFilterBar>().Instance;
        await cut.InvokeAsync(async () =>
        {
            filter.SelectedFormatIds = [2];
            filter.SelectedAudienceAgeIds = [3];
            await filter.OnSearchRequested.InvokeAsync();
        });

        await Assert.That(cut.FindAll("[data-discovery-state='filtered']").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".event-list__preset-list a").GetAttribute("href")).IsEqualTo(curatedUrl);
        var originalQuery = System.Web.HttpUtility.ParseQueryString(handler.Requests[1].Query);
        await Assert.That(originalQuery["searchTerm"]).IsEqualTo("community");
        await Assert.That(originalQuery["actorId"]).IsEqualTo(actorId.ToString());
        await Assert.That(originalQuery["organizationId"]).IsEqualTo(organizationId.ToString());
        await Assert.That(originalQuery["groupId"]).IsEqualTo(groupId.ToString());
        await Assert.That(originalQuery["formatIds"]).IsEqualTo("2");
        await Assert.That(originalQuery["audienceAgeIds"]).IsEqualTo("3");

        await cut.Find("[data-discovery-action='past']").ClickAsync(new MouseEventArgs());

        await Assert.That(handler.Requests.Count).IsEqualTo(3);
        await Assert.That(handler.Requests[2].AbsolutePath).IsEqualTo(handler.Requests[0].AbsolutePath);
        var pastQuery = System.Web.HttpUtility.ParseQueryString(handler.Requests[2].Query);
        await Assert.That(pastQuery["view"]).IsEqualTo("Past");
        await Assert.That(pastQuery["cursor"]).IsNull();
        foreach (var key in originalQuery.AllKeys.Where(key =>
                     key is not null && !string.Equals(key, "view", StringComparison.OrdinalIgnoreCase)))
        {
            var expectedValues = originalQuery.GetValues(key)
                ?? throw new InvalidOperationException($"Expected query values for '{key}'.");
            await Assert.That(pastQuery.GetValues(key)).IsEquivalentTo(expectedValues);
        }
        await Assert.That(cut.FindAll("[data-discovery-state='past']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
        await Assert.That(cut.Find(".event-list__preset-list a").GetAttribute("href")).IsEqualTo(curatedUrl);
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()).IsEmpty();
    }

    [Test]
    public async Task EmptyPast_UpcomingActionRestoresDefaultViewAndRetainsFilters()
    {
        var actorId = Guid.NewGuid();
        using var handler = new DiscoveryHttpHandler(
            (200, CreateResult(1, 20, [])),
            (200, CreateResult(1, 20, [])),
            (200, CreateResult(1, 20, [])));
        using var http = RegisterDiscoveryHttpClient(handler);
        _ctx.Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/events?q=community&actorId={actorId}");
        var cut = _ctx.RenderMudComponent<EventList>();
        var filter = cut.FindComponent<Explore.Blazor.Client.Pages.Events.Components.EventFilterBar>().Instance;
        await cut.InvokeAsync(async () =>
        {
            filter.SelectedTemporalView = Explore.Blazor.Client.Models.TemporalView.Past;
            filter.SelectedFormatIds = [2];
            filter.SelectedAudienceAgeIds = [3];
            await filter.OnSearchRequested.InvokeAsync();
        });

        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        var pastQuery = System.Web.HttpUtility.ParseQueryString(handler.Requests[1].Query);
        await Assert.That(pastQuery["view"]).IsEqualTo("Past");
        await Assert.That(pastQuery["searchTerm"]).IsEqualTo("community");
        await Assert.That(pastQuery["actorId"]).IsEqualTo(actorId.ToString());
        await Assert.That(pastQuery["formatIds"]).IsEqualTo("2");
        await Assert.That(pastQuery["audienceAgeIds"]).IsEqualTo("3");
        await Assert.That(cut.FindAll("[data-discovery-state='past']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-action='upcoming']").Count).IsEqualTo(1);

        await cut.Find("[data-discovery-action='upcoming']").ClickAsync(new MouseEventArgs());

        await Assert.That(handler.Requests.Count).IsEqualTo(3);
        await Assert.That(handler.Requests[2].AbsolutePath).IsEqualTo(handler.Requests[0].AbsolutePath);
        var upcomingQuery = System.Web.HttpUtility.ParseQueryString(handler.Requests[2].Query);
        await Assert.That(upcomingQuery["view"]).IsEqualTo("UpcomingAndOngoing");
        await Assert.That(upcomingQuery["cursor"]).IsNull();
        foreach (var key in pastQuery.AllKeys.Where(key =>
                     key is not null && !string.Equals(key, "view", StringComparison.OrdinalIgnoreCase)))
        {
            var expectedValues = pastQuery.GetValues(key)
                ?? throw new InvalidOperationException($"Expected query values for '{key}'.");
            await Assert.That(upcomingQuery.GetValues(key)).IsEquivalentTo(expectedValues);
        }
        await Assert.That(cut.FindAll("[data-discovery-state='filtered']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-state='past']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='upcoming']")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()).IsEmpty();
    }

    [Test]
    [Arguments(Explore.Blazor.Client.Models.TemporalView.Past, "past")]
    [Arguments(Explore.Blazor.Client.Models.TemporalView.All, "all")]
    public async Task EmptyExplicitTemporalView_DistinguishesPresentationWithoutPastAction(
        Explore.Blazor.Client.Models.TemporalView view, string state)
    {
        using var handler = new DiscoveryHttpHandler(
            (200, CreateResult(1, 20, [])),
            (200, CreateResult(1, 20, [])));
        using var http = RegisterDiscoveryHttpClient(handler);
        var cut = _ctx.RenderMudComponent<EventList>();
        var filter = cut.FindComponent<Explore.Blazor.Client.Pages.Events.Components.EventFilterBar>().Instance;

        await cut.InvokeAsync(async () =>
        {
            filter.SelectedTemporalView = view;
            await filter.OnSearchRequested.InvokeAsync();
        });

        await Assert.That(handler.Requests.Count).IsEqualTo(2);
        await Assert.That(System.Web.HttpUtility.ParseQueryString(handler.Requests[1].Query)["view"])
            .IsEqualTo(view.ToString());
        await Assert.That(cut.FindAll($"[data-discovery-state='{state}']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-state]").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()).IsEmpty();
    }

    private HttpClient RegisterDiscoveryHttpClient(DiscoveryHttpHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        _ctx.Services.AddSingleton<IEventService>(new EventService(
            new EventClient(http),
            Substitute.For<IEventLifecycleClient>(),
            Substitute.For<IEventManagementReadClient>(),
            Substitute.For<IEventParticipationClient>(),
            Substitute.For<IEventPublicActionClient>(),
            Substitute.For<ILogger<EventService>>()));
        return http;
    }

    private sealed class DiscoveryHttpHandler(params (int Status, object Payload)[] responses) : HttpMessageHandler
    {
        private readonly Queue<(int Status, object Payload)> pending = new(responses);
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = pending.Dequeue();
            return Task.FromResult(new HttpResponseMessage((System.Net.HttpStatusCode)response.Status)
            {
                Content = new StringContent(JsonSerializer.Serialize(response.Payload),
                    System.Text.Encoding.UTF8, response.Status == 200 ? "application/hal+json" : "application/problem+json")
            });
        }
    }

    private static DockLayoutSnapshot CreateWorkspaceSnapshot(bool customizeOpen, int customizeWidth)
    {
        return new DockLayoutSnapshot(
            "events",
            [
                new DockPanelState(EventDockPanels.CustomizeViewId, customizeOpen, DockMode.Docked, customizeWidth, Order: 10, IsActive: customizeOpen),
                new DockPanelState(EventDockPanels.EventPreviewId, true, DockMode.Inspector, Width: 440, Order: 20, IsActive: true)
            ],
            TestTime.UtcNow);
    }

    private static bool IsExpectedAutosaveSnapshot(DockLayoutSnapshot? snapshot)
    {
        return snapshot is not null
            && snapshot.LayoutKey == "events"
            && snapshot.Panels.Count == 1
            && snapshot.Panels.Any(panel => panel.Id == EventDockPanels.CustomizeViewId && panel.IsOpen)
            && snapshot.Panels.All(panel => panel.Id != EventDockPanels.EventPreviewId)
            && snapshot.Panels.All(panel => panel.Id != ShellDockPanels.WorkspaceNavId)
            && snapshot.Panels.All(panel => panel.Id != ShellDockPanels.AiAssistantId);
    }

    private static DockPanelDescriptor CreateShellPersistentDescriptor(DockPanelId id, DockSide side)
    {
        return new DockPanelDescriptor(
            id,
            DockScope.Shell,
            side,
            DockMode.Docked,
            Title: "Shell panel",
            AriaLabel: "Shell panel",
            DefaultWidth: 320,
            MinWidth: 280,
            MaxWidth: 520,
            Order: 10,
            IsResizable: true,
            CanClose: true,
            PersistState: true);
    }

    private void SetupPagedResult(Task<EventDiscoveryTraversalResource> resultTask)
    {
        _eventService.GetEventDiscoveryAsync(
            Arg.Any<string?>(),                // cursor
            Arg.Any<int>(),                    // pageSize
            Arg.Any<string?>(),                // searchTerm
            Arg.Any<Guid?>(),                  // categoryId
            Arg.Any<List<Guid>?>(),            // includedCategoryIds
            Arg.Any<List<Guid>?>(),            // excludedCategoryIds
            Arg.Any<string?>(),                // categoryInclusionMode
            Arg.Any<string?>(),                // categoryExclusionMode
            Arg.Any<List<Guid>?>(),            // includedTagIds
            Arg.Any<List<Guid>?>(),            // excludedTagIds
            Arg.Any<string?>(),                // inclusionMode
            Arg.Any<string?>(),                // exclusionMode
            Arg.Any<List<int>?>(),             // formatIds
            Arg.Any<List<int>?>(),             // madhabIds
            Arg.Any<List<int>?>(),             // registrationModeIds
            Arg.Any<List<int>?>(),             // languageIds
            Arg.Any<DateTimeOffset?>(),        // dateFrom
            Arg.Any<DateTimeOffset?>(),        // dateTo
            Arg.Any<string?>(),                // sortBy
            Arg.Any<bool?>(),                  // sortDescending
            Arg.Any<List<int>?>(),             // eventTypeIds
            Arg.Any<List<int>?>(),             // audienceGenderIds
            Arg.Any<List<int>?>(),             // audienceAgeIds
            Arg.Any<List<int>?>(),             // eventStatusIds
            Arg.Any<List<int>?>(),             // genderModeIds
            Arg.Any<bool?>(),                  // includesQuranRecitation
            Arg.Any<List<int>?>(),             // referencePrayerIds
            Arg.Any<List<int>?>(),             // islamicPrimaryLanguageIds
            Arg.Any<bool?>(),                  // hasIslamicAspect
            Arg.Any<int?>(),                   // skillLevelId
            Arg.Any<bool?>(),                  // isCodingCompetition
            Arg.Any<bool?>(),                  // isHackathon
            Arg.Any<bool?>(),                  // requiresLaptop
            Arg.Any<string?>(),                // techStackTag
            Arg.Any<bool?>(),                  // hasTechAspect
            Arg.Any<Guid?>(),                  // actorId
            Arg.Any<Guid?>(),                  // organizationId
            Arg.Any<Guid?>(),                  // groupId
            Arg.Any<string?>(),                // view
            Arg.Any<Guid?>(),                  // areaId
            Arg.Any<List<CustomPropertyFilterCriterion>?>(),
            Arg.Any<string?>(),
            Arg.Any<CancellationToken>())
            .Returns(resultTask);
    }

    private static async Task OpenCustomizationDrawerAsync(IRenderedComponent<EventList> cut)
    {
        await cut.Find("[aria-label='Customize view']").ClickAsync(new MouseEventArgs());
    }

    private static async Task SelectRenderedEventAsync(IRenderedComponent<EventList> cut, string title)
    {
        var selector = $"[aria-label='View event: {title}']";
        cut.WaitForElement(selector);
        await cut.Find(selector).ClickAsync(new MouseEventArgs());
        cut.WaitForElement("[aria-label='Event preview']");
    }

    private static async Task OpenTagManagementAsync(IRenderedComponent<EventList> cut)
    {
        cut.WaitForElement("[aria-label='Manage tags']");
        await cut.Find("[aria-label='Manage tags']").ClickAsync(new MouseEventArgs());
        cut.WaitForElement(".tagcat-manager__popup");
    }

    private void SetupEventDetailResponses(Guid eventId, bool withEditLink = false)
    {
        const string title = "Dock Baseline Event";
        SetupPagedResult(Task.FromResult(CreateResult(1, 20,
            [new EventListDto
            {
                Id = eventId,
                Title = title,
                AdditionalProperties = withEditLink ? CreateHalLinks("edit") : new Dictionary<string, object>()
            }])));
        SetupEventDetailResponse(eventId, title, withEditLink);
    }

    private void SetupEventDetailResponse(Guid eventId, string title, bool withEditLink = false)
    {
        _eventService.GetEventByIdAsync(eventId).Returns(new EventDto
        {
            Id = eventId,
            Title = title,
            Description = "Event used for sidebar baseline tests",
            AdditionalProperties = withEditLink ? CreateHalLinks("edit") : new Dictionary<string, object>()
        });

        _eventSessionService.GetSessionsByEventAsync(eventId, Arg.Any<bool>()).Returns(new List<EventSessionListDto>());
    }

    private static Dictionary<string, object> CreateHalLinks(params string[] relations)
    {
        var links = string.Join(
            ',',
            relations.Select(relation => $"\"{relation}\":{{\"href\":\"/api/eventregistration\",\"method\":\"POST\"}}"));
        using var document = JsonDocument.Parse($"{{\"_links\":{{{links}}}}}");
        return new Dictionary<string, object>
        {
            ["_links"] = document.RootElement.GetProperty("_links").Clone()
        };
    }

    [Test]
    public async Task Traversal_ForwardsOwnershipQueryStateThroughGeneratedHttp()
    {
        var actorId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        using var handler = new DiscoveryHttpHandler((200, CreateResult(1, 20, [])));
        using var http = RegisterDiscoveryHttpClient(handler);
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events?actorId={actorId}&organizationId={organizationId}&groupId={groupId}");

        var cut = _ctx.RenderMudComponent<EventList>();

        var query = System.Web.HttpUtility.ParseQueryString(handler.Requests.Single().Query);
        await Assert.That(query["actorId"]).IsEqualTo(actorId.ToString());
        await Assert.That(query["organizationId"]).IsEqualTo(organizationId.ToString());
        await Assert.That(query["groupId"]).IsEqualTo(groupId.ToString());
    }

    [Test]
    public async Task Render_RegistersWorkspaceDockHostAndCustomizePanelDescriptor()
    {
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [])));

        var cut = _ctx.RenderMudComponent<EventList>();

        var host = cut.Find("[data-testid='event-list-workspace-dock-host'][data-dock-scope='workspace']");
        var customizePanel = _dockLayoutState.GetPanel(EventDockPanels.CustomizeViewId);
        var previewPanel = _dockLayoutState.GetPanel(EventDockPanels.EventPreviewId);

        await Assert.That(host).IsNotNull();
        await Assert.That(customizePanel).IsNotNull();
        await Assert.That(customizePanel!.Descriptor).IsEqualTo(EventDockPanels.CustomizeView);
        await Assert.That(previewPanel).IsNotNull();
        await Assert.That(previewPanel!.Descriptor).IsEqualTo(EventDockPanels.EventPreview);
        await Assert.That(cut.FindAll("[data-testid='event-list-workspace-dock-host'][data-dock-scope='workspace']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task FirstRender_HydratesWorkspaceDockLayoutAfterDescriptorsRegister()
    {
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [])));
        _dockLayoutPersistence.LoadAsync("events", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DockLayoutSnapshot?>(CreateWorkspaceSnapshot(customizeOpen: true, customizeWidth: 360)));

        var cut = _ctx.RenderMudComponent<EventList>();

        cut.WaitForAssertion(() =>
        {
            var customizePanel = _dockLayoutState.GetPanel(EventDockPanels.CustomizeViewId);
            if (customizePanel?.State is not { IsOpen: true, Width: 360 })
                throw new InvalidOperationException("Expected workspace snapshot to restore Customize View state.");

            var previewPanel = _dockLayoutState.GetPanel(EventDockPanels.EventPreviewId);
            if (previewPanel?.State.IsOpen == true)
                throw new InvalidOperationException("Expected non-persistent Event Preview state to be ignored during restore.");
        });

        await _dockLayoutPersistence.Received(1).LoadAsync("events", Arg.Any<CancellationToken>());
        await _dockLayoutPersistence.DidNotReceive().SaveAsync(Arg.Any<DockLayoutSnapshot>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Render_WithShellEventSections_ShowsCuratedFilterLinks()
    {
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [])));
        _publicExperienceService.GetCachedShellAsync().Returns(Task.FromResult<PublicExperienceShellDto?>(new PublicExperienceShellDto
        {
            EventCatalog = new PublicExperienceEventCatalogDto { Label = "Programs", Url = "/events" },
            EventSections =
            [
                new PublicExperienceEventSectionDto
                {
                    Key = "youth",
                    Label = "Youth Programs",
                    Url = "/events?AudienceAgeIds=2",
                    Icon = "youth",
                    SortOrder = 20
                },
                new PublicExperienceEventSectionDto
                {
                    Key = "education",
                    Label = "Education",
                    Url = "/events?IncludedCategoryIds=11111111-1111-1111-1111-111111111111",
                    Icon = "education",
                    SortOrder = 10
                },
                new PublicExperienceEventSectionDto
                {
                    Key = "hidden",
                    Label = "",
                    Url = "/events?hidden=true",
                    SortOrder = 0
                }
            ]
        }));

        var cut = _ctx.RenderMudComponent<EventList>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup).Contains("Featured Programs");
            Assert.That(cut.Markup).Contains("Education");
            Assert.That(cut.Markup).Contains("Youth Programs");
            Assert.That(cut.Markup).Contains("Show Education");
            Assert.That(cut.Markup).DoesNotContain("hidden=true");
        });

        await Assert.That(cut.Markup.IndexOf("Education", StringComparison.Ordinal))
            .IsLessThan(cut.Markup.IndexOf("Youth Programs", StringComparison.Ordinal));
    }

    [Test]
    public async Task OpeningCustomizationDrawer_RendersCustomizeViewThroughWorkspaceDock()
    {
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [])));

        var cut = _ctx.RenderMudComponent<EventList>();

        await OpenCustomizationDrawerAsync(cut);

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.FindAll("[data-testid='dock-panel-host'][data-dock-panel-id='events.customize-view']").Count).IsEqualTo(1);
            Assert.That(cut.Markup).Contains("Customize View");
        });

        var panel = _dockLayoutState.GetPanel(EventDockPanels.CustomizeViewId);
        await Assert.That(panel?.State.IsOpen).IsTrue();
        await Assert.That(panel?.State.Mode).IsEqualTo(DockMode.Docked);
    }

    [Test]
    public async Task DoesNotShowEmptyState_BeforeFirstEventsLoadCompletes()
    {
        // Arrange — result stays pending
        var pendingResult = new TaskCompletionSource<EventDiscoveryTraversalResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupPagedResult(pendingResult.Task);

        // Act
        var cut = _ctx.RenderMudComponent<EventList>();

        // Assert — empty state must not appear while load is pending
        await Assert.That(cut.Markup).DoesNotContain("No events found");
        await Assert.That(cut.FindAll(".event-list__page[aria-busy='true']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-state]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();

        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindAll(".event-list__page[aria-busy='false']").Count == 1)
                loaded.TrySetResult();
        };
        pendingResult.SetResult(CreateResult(1, 20, []));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task ShowsNoEventsState_OnlyAfterInitialLoadCompletesWithEmptyResult()
    {
        // Arrange — start with pending result
        var pendingResult = new TaskCompletionSource<EventDiscoveryTraversalResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupPagedResult(pendingResult.Task);

        var cut = _ctx.RenderMudComponent<EventList>();

        // Pre-condition: while pending, empty state is not shown
        await Assert.That(cut.FindAll("[data-discovery-state]")).IsEmpty();
        await Assert.That(cut.FindAll(".event-list__page[aria-busy='true']").Count).IsEqualTo(1);
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindAll(".event-list__page[aria-busy='false']").Count == 1)
                loaded.TrySetResult();
        };

        // Complete the request already subscribed by the rendered component.
        pendingResult.SetResult(CreateResult(1, 20, []));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.FindAll("[data-discovery-state='upcoming']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-action='past']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-list__recovery")).IsEmpty();
        await Assert.That(cut.FindAll(".event-list__truncated")).IsEmpty();
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()).IsEmpty();
    }

    [Test]
    public async Task ShowsNoMatchesState_WhenFilteredResultIsEmpty()
    {
        var actorId = Guid.NewGuid();
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [])));
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events?actorId={actorId}");

        var cut = _ctx.RenderMudComponent<EventList>();

        await Assert.That(cut.FindAll("[data-discovery-state='filtered']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[data-discovery-action='past']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-list__page[aria-busy='false']").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-list__recovery")).IsEmpty();
        await Assert.That(cut.FindAll(".event-list__truncated")).IsEmpty();
    }

    [Test]
    public async Task HidesNoEventsState_WhenResultsExist()
    {
        var events = new List<EventListDto>
        {
            new() { Id = Guid.NewGuid(), Title = "Blazor Summit", Description = "Event description" }
        };
        var pendingResult = new TaskCompletionSource<EventDiscoveryTraversalResource>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupPagedResult(pendingResult.Task);

        var cut = _ctx.RenderMudComponent<EventList>();
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindAll(".event-list__page[aria-busy='false']").Count == 1)
                loaded.TrySetResult();
        };
        pendingResult.SetResult(CreateResult(1, 20, events));
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.Markup).Contains("Blazor Summit");
        await Assert.That(cut.Markup).DoesNotContain("No events found");
        await Assert.That(cut.FindComponents<Explore.Blazor.Client.Pages.Events.Components.EventCard>()
            .Select(card => card.Instance.Event.Id)).IsEquivalentTo(events.Select(evt => evt.Id));
        await Assert.That(cut.FindAll("[data-discovery-state]")).IsEmpty();
        await Assert.That(cut.FindAll("[data-discovery-action='past']")).IsEmpty();
    }

    [Test]
    public async Task SelectingEvent_PreservesCustomizationDrawerAndOpensDetailDrawer()
    {
        var eventId = Guid.NewGuid();
        SetupEventDetailResponses(eventId);

        var cut = _ctx.RenderMudComponent<EventList>();

        await OpenCustomizationDrawerAsync(cut);
        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.CustomizeViewId)?.State.IsOpen).IsTrue();

        await SelectRenderedEventAsync(cut, "Dock Baseline Event");

        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.CustomizeViewId)?.State.IsOpen).IsTrue();
        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsTrue();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.FindAll("[data-testid='dock-panel-host'][data-dock-panel-id='events.event-preview'][data-dock-mode='inspector']").Count).IsEqualTo(1);
            Assert.That(cut.FindAll("[data-testid='dock-resize-handle'][data-dock-resize-panel-id='events.event-preview']").Count).IsEqualTo(0);
        });
    }

    [Test]
    public async Task SelectingEvent_WithBlankFeaturedImage_DisplaysFallbackAfterDetailLoads()
    {
        var eventId = Guid.NewGuid();
        var eventItem = new EventListDto
        {
            Id = eventId,
            Title = "Fallback Preview Event",
            FeaturedImageUri = "   "
        };
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [eventItem])));
        SetupEventDetailResponse(eventId, eventItem.Title);

        var cut = _ctx.RenderMudComponent<EventList>();

        await SelectRenderedEventAsync(cut, eventItem.Title);

        var image = cut.Find("img.event-list__detail-image-fallback");

        await Assert.That(cut.FindAll(".event-list__detail-image-skeleton")).IsEmpty();
        await Assert.That(image.GetAttribute("class")).DoesNotContain("event-list__detail-image--loading");
        await Assert.That(image.GetAttribute("src")).StartsWith("data:image/svg+xml;utf8,");
        await Assert.That(cut.FindAll("img.event-list__detail-image-actual")).IsEmpty();
    }

    [Test]
    public async Task DetailImageLoaded_ClearsSidebarImageSkeleton()
    {
        var eventId = Guid.NewGuid();
        var eventItem = new EventListDto
        {
            Id = eventId,
            Title = "Image Preview Event",
            FeaturedImageUri = "https://cdn.example.test/event.jpg"
        };
        SetupPagedResult(Task.FromResult(CreateResult(1, 20, [eventItem])));
        SetupEventDetailResponse(eventId, eventItem.Title);

        var cut = _ctx.RenderMudComponent<EventList>();

        await SelectRenderedEventAsync(cut, eventItem.Title);

        var loadingImage = cut.Find("img.event-list__detail-image-actual");
        await Assert.That(cut.FindAll(".event-list__detail-image-skeleton")).Count().IsEqualTo(1);
        await Assert.That(loadingImage.GetAttribute("class")).Contains("event-list__detail-image--loading");

        await loadingImage.TriggerEventAsync("onload", EventArgs.Empty);

        var loadedImage = cut.Find("img.event-list__detail-image-actual");
        await Assert.That(cut.FindAll(".event-list__detail-image-skeleton")).IsEmpty();
        await Assert.That(loadedImage.GetAttribute("class")).DoesNotContain("event-list__detail-image--loading");
    }

    [Test]
    public async Task OpeningCustomizationDrawer_PreservesOpenDetailDrawer()
    {
        var eventId = Guid.NewGuid();
        SetupEventDetailResponses(eventId);

        var cut = _ctx.RenderMudComponent<EventList>();

        await SelectRenderedEventAsync(cut, "Dock Baseline Event");

        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsTrue();

        await OpenCustomizationDrawerAsync(cut);

        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsTrue();
        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.CustomizeViewId)?.State.IsOpen).IsTrue();
    }

    [Test]
    public async Task ClosingDetailDrawer_ResetsDetailPanelTransientState()
    {
        var eventId = Guid.NewGuid();
        SetupEventDetailResponses(eventId, withEditLink: true);

        var cut = _ctx.RenderMudComponent<EventList>();

        await SelectRenderedEventAsync(cut, "Dock Baseline Event");
        await OpenTagManagementAsync(cut);

        var tagPopupClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindAll(".tagcat-manager__popup").Count == 0)
            {
                tagPopupClosed.TrySetResult();
            }
        };

        await cut.Find(".event-details-sidebar [aria-label='Close']").ClickAsync(new MouseEventArgs());
        await tagPopupClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(cut.FindAll(".tagcat-manager__popup")).IsEmpty();
        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsTrue();

        var previewClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cut.OnMarkupUpdated += (_, _) =>
        {
            if (cut.FindAll("[aria-label='Event preview']").Count == 0)
            {
                previewClosed.TrySetResult();
            }
        };

        await cut.Find(".event-details-sidebar [aria-label='Close']").ClickAsync(new MouseEventArgs());
        await previewClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsFalse();
        await Assert.That(cut.FindAll("[aria-label='Event preview']")).IsEmpty();
    }

    [Test]
    public async Task BackdropClosingEventPreview_SynchronizesDetailPreviewState()
    {
        var eventId = Guid.NewGuid();
        SetupEventDetailResponses(eventId, withEditLink: true);

        var cut = _ctx.RenderMudComponent<EventList>();

        await SelectRenderedEventAsync(cut, "Dock Baseline Event");
        await OpenTagManagementAsync(cut);
        await cut.Find("[data-testid='dock-overlay-backdrop']").ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.FindAll("[aria-label='Event preview']")).IsEmpty();
            Assert.That(cut.FindAll(".tagcat-manager__popup")).IsEmpty();
            Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsFalse();
        });
    }

    [Test]
    public async Task NavigatingEventPreview_ResetsTransientPopupStateBeforeSelectingNextEvent()
    {
        var firstEventId = Guid.NewGuid();
        var nextEventId = Guid.NewGuid();
        var events = new List<EventListDto>
        {
            new() { Id = firstEventId, Title = "First Preview Event", AdditionalProperties = CreateHalLinks("edit") },
            new() { Id = nextEventId, Title = "Next Preview Event", AdditionalProperties = CreateHalLinks("edit") }
        };

        SetupPagedResult(Task.FromResult(CreateResult(1, 20, events)));
        SetupEventDetailResponse(firstEventId, "First Preview Event", withEditLink: true);
        SetupEventDetailResponse(nextEventId, "Next Preview Event", withEditLink: true);

        var cut = _ctx.RenderMudComponent<EventList>();
        cut.WaitForAssertion(() =>
            Assert.That(cut.Markup).Contains("First Preview Event"));
        await SelectRenderedEventAsync(cut, "First Preview Event");
        await OpenTagManagementAsync(cut);

        await cut.Find("[aria-label='Next event']").ClickAsync(new MouseEventArgs());

        await Assert.That(cut.Find($"[aria-label='View event: Next Preview Event']").ClassList).Contains("event-card--selected");
        await Assert.That(cut.FindAll(".tagcat-manager__popup")).IsEmpty();
        await Assert.That(_dockLayoutState.GetPanel(EventDockPanels.EventPreviewId)?.State.IsOpen).IsTrue();
    }

}
