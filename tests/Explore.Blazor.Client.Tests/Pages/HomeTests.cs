using Blazouter.Enums;
using Blazouter.Extensions;
using Blazouter.Models;
using Explore.Blazor.Client.Pages;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages;

public class HomeTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public HomeTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.Services.AddBlazouter();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    #region Discovery State Tests

    [Test]
    public async Task HomeShowsDiscoveryForAuthenticatedVisitors()
    {
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Test User", "test@example.com");
        SetupHomeServices();

        var cut = _ctx.RenderMudComponent<Home>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        await Assert.That(cut.Markup).DoesNotContain("Discover events");
        await Assert.That(cut.Markup).Contains("Browsing events in");
        await Assert.That(cut.Markup).Contains("Brussels");
    }

    [Test]
    public async Task HomeShowsSameDiscoveryForAnonymousVisitors()
    {
        _ctx.SetAnonymousUser();
        SetupHomeServices();

        var cut = _ctx.RenderMudComponent<Home>();
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        await Assert.That(cut.Markup).DoesNotContain("Discover events");
        await Assert.That(cut.Markup).Contains("Browsing events in");
        await Assert.That(cut.Markup).Contains("Brussels");
    }

    [Test]
    public async Task HomeForwardsDiscoveryQueryFromBlazouterUrl()
    {
        _ctx.SetAnonymousUser();
        SetupHomeServices();
        var discoveryService = _ctx.Services.GetRequiredService<IHomeDiscoveryService>();
        _ctx.Services.GetRequiredService<NavigationManager>().NavigateTo("/home?mode=online");

        var cut = _ctx.Render<Blazouter.Components.Router>(parameters => parameters
            .Add(component => component.Routes,
            [
                new RouteConfig
                {
                    Path = "/home",
                    Component = typeof(Home),
                    Transition = RouteTransition.None
                }
            ]));
        cut.WaitForElement("[data-testid='home-discovery-context']", TimeSpan.FromSeconds(2));

        await discoveryService.Received(1).LoadAsync(
            null,
            "online",
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Home_ShowsOrganizationCentricShell_WhenPrimaryOrganizationIsAvailable()
    {
        // Arrange
        _ctx.SetAnonymousUser();
        SetupHomeServices(new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            EventCatalog = new PublicExperienceEventCatalogDto
            {
                Label = "Programs",
                Url = "/events?ActorId=11111111-1111-1111-1111-111111111111"
            },
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Available,
                DisplayName = "Northside Masjid",
                ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Handle = "@northside",
                WebsiteUrl = "https://northside.example"
            },
            Home = new PublicExperienceHomeDto
            {
                Blocks =
                [
                    new PublicExperienceHomeBlockDto
                    {
                        Key = "hero",
                        Kind = PublicExperienceHomeBlockKind.OrganizationSummary,
                        Title = "Northside Masjid",
                        Body = "Community programs for every family.",
                        SortOrder = 0
                    }
                ]
            },
            EventSections =
            [
                new PublicExperienceEventSectionDto
                {
                    Key = "youth",
                    Label = "Youth Programs",
                    Url = "/events?IncludedCategoryIds=11111111-1111-1111-1111-111111111112",
                    Icon = "calendar"
                }
            ],
            Ctas =
            [
                new PublicExperienceCtaDto
                {
                    Key = "donate",
                    Label = "Support Us",
                    Url = "/donate"
                }
            ],
            Footer = new FooterConfigDto
            {
                LinkGroups =
                [
                    new FooterLinkGroupDto
                    {
                        Id = Guid.NewGuid(),
                        Title = "Community",
                        Links =
                        [
                            new FooterLinkItemDto
                            {
                                Id = Guid.NewGuid(),
                                Label = "Volunteer",
                                Url = "/volunteer"
                            }
                        ]
                    }
                ]
            }
        },
        new List<EventListDto>
        {
            new EventListDto
            {
                Id = Guid.NewGuid(),
                Title = "Friday Family Night",
                Subtitle = "Dinner and reminders",
                FirstSessionDate = new DateTimeOffset(2026, 5, 8, 18, 0, 0, TimeSpan.Zero),
                EventTypeFullName = "Program",
                AudienceGenderFullName = "All",
                AudienceAgeFullName = "Families",
                ActorDisplayName = "Northside Masjid",
                ActorTypeFullName = "Organization",
                EventStatusFullName = "Published",
                VisibilityTypeFullName = "Public",
                EventFormatFullName = "In Person"
            }
        });

        // Act
        var cut = _ctx.RenderMudComponent<Home>();

        // Assert
        await Assert.That(cut.Markup).Contains("Northside Masjid");
        await Assert.That(cut.Markup).Contains("Community programs for every family.");
        await Assert.That(cut.Markup).Contains("Youth Programs");
        await Assert.That(cut.Markup).Contains("Support Us");
        await Assert.That(cut.Markup).Contains("Friday Family Night");
        await Assert.That(cut.Markup).Contains("Connect with Northside Masjid");
        await Assert.That(cut.Markup).Contains("Volunteer");
        await Assert.That(cut.Markup).DoesNotContain("Never Miss a Muslim Event Again");
    }

    [Test]
    public async Task Home_OrganizationRichTextBlock_RendersTenantContentAsEncodedText()
    {
        _ctx.SetAnonymousUser();
        SetupHomeServices(new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            EventCatalog = new PublicExperienceEventCatalogDto
            {
                Label = "Programs",
                Url = "/events"
            },
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Available,
                DisplayName = "Northside Masjid",
                ActorId = Guid.Parse("11111111-1111-1111-1111-111111111111")
            },
            Home = new PublicExperienceHomeDto
            {
                Blocks =
                [
                    new PublicExperienceHomeBlockDto
                    {
                        Key = "rich-text",
                        Kind = PublicExperienceHomeBlockKind.RichText,
                        Title = "<img src=x onerror=alert(1)>",
                        Subtitle = "<script>alert(1)</script>",
                        Body = "<strong>Community update</strong><script>alert(1)</script><img src=x onerror=alert(1)>",
                        SortOrder = 10
                    }
                ]
            }
        });

        var cut = _ctx.RenderMudComponent<Home>();

        var richTextBlock = cut.FindAll(".organization-home__block")
            .Single(element => element.TextContent.Contains("<strong>Community update</strong>", StringComparison.Ordinal));

        await Assert.That(richTextBlock.TextContent).Contains("<img src=x onerror=alert(1)>");
        await Assert.That(richTextBlock.TextContent).Contains("<script>alert(1)</script>");
        await Assert.That(richTextBlock.InnerHtml).Contains("&lt;strong&gt;Community update&lt;/strong&gt;");
        await Assert.That(richTextBlock.InnerHtml).Contains("&lt;script&gt;alert(1)&lt;/script&gt;");
        await Assert.That(richTextBlock.InnerHtml).DoesNotContain("<script");
        await Assert.That(richTextBlock.InnerHtml).DoesNotContain("<img");
        await Assert.That(cut.FindAll("script").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("img").Count).IsEqualTo(0);
    }

    [Test]
    public async Task Home_ShowsSafeOrganizationRemediation_WhenPrimaryOrganizationIsMissing()
    {
        // Arrange
        _ctx.SetAnonymousUser();
        SetupHomeServices(new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            EventCatalog = new PublicExperienceEventCatalogDto { Label = "Programs", Url = "/events" },
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto { State = PublicExperiencePrimaryOrganizationState.Missing }
        });

        // Act
        var cut = _ctx.RenderMudComponent<Home>();

        // Assert
        await Assert.That(cut.Markup).Contains("Organization home is not available yet");
        await Assert.That(cut.Markup).Contains("Browse Programs");
        await Assert.That(cut.Markup).DoesNotContain("Never Miss a Muslim Event Again");
    }

    #endregion

    #region Page Title Tests

    [Test]
    public async Task Home_RendersBrowsingContextTitle()
    {
        // Arrange
        _ctx.SetAnonymousUser();
        SetupHomeServices();

        // Act
        var cut = _ctx.RenderMudComponent<Home>();

        await Assert.That(cut.Markup).Contains("Browsing events in");
        await Assert.That(cut.Markup).DoesNotContain("Discover events");
    }

    #endregion

    #region Error Handling Tests

    [Test]
    public async Task Home_HandlesAuthError_Gracefully()
    {
        // Arrange - Set anonymous (simulates auth error fallback)
        _ctx.SetAnonymousUser();
        SetupHomeServices();

        // Act - Should not throw
        var cut = _ctx.RenderMudComponent<Home>();

        // Assert - Should render without crash
        await Assert.That(cut.Markup).DoesNotContain("mud-skeleton");
    }

    #endregion

    private void SetupHomeServices(
        PublicExperienceShellDto? shell = null,
        IReadOnlyList<EventListDto>? featuredEvents = null)
    {
        var eventService = Substitute.For<IEventService>();
        eventService.GetAllEventsAsync().Returns(new List<EventListDto>());
        eventService.GetEventsPagedAsync(
                Arg.Any<int>(),
                Arg.Any<int>(),
                searchTerm: Arg.Any<string?>(),
                categoryId: Arg.Any<Guid?>(),
                includedCategoryIds: Arg.Any<List<Guid>?>(),
                excludedCategoryIds: Arg.Any<List<Guid>?>(),
                categoryInclusionMode: Arg.Any<string?>(),
                categoryExclusionMode: Arg.Any<string?>(),
                includedTagIds: Arg.Any<List<Guid>?>(),
                excludedTagIds: Arg.Any<List<Guid>?>(),
                inclusionMode: Arg.Any<string?>(),
                exclusionMode: Arg.Any<string?>(),
                formatIds: Arg.Any<List<int>?>(),
                madhabIds: Arg.Any<List<int>?>(),
                registrationModeIds: Arg.Any<List<int>?>(),
                languageIds: Arg.Any<List<int>?>(),
                dateFrom: Arg.Any<DateTimeOffset?>(),
                dateTo: Arg.Any<DateTimeOffset?>(),
                sortBy: Arg.Any<string?>(),
                sortDescending: Arg.Any<bool?>(),
                eventTypeIds: Arg.Any<List<int>?>(),
                audienceGenderIds: Arg.Any<List<int>?>(),
                audienceAgeIds: Arg.Any<List<int>?>(),
                eventStatusIds: Arg.Any<List<int>?>(),
                genderModeIds: Arg.Any<List<int>?>(),
                includesQuranRecitation: Arg.Any<bool?>(),
                referencePrayerIds: Arg.Any<List<int>?>(),
                islamicPrimaryLanguageIds: Arg.Any<List<int>?>(),
                hasIslamicAspect: Arg.Any<bool?>(),
                skillLevelId: Arg.Any<int?>(),
                isCodingCompetition: Arg.Any<bool?>(),
                isHackathon: Arg.Any<bool?>(),
                requiresLaptop: Arg.Any<bool?>(),
                techStackTag: Arg.Any<string?>(),
                hasTechAspect: Arg.Any<bool?>(),
                actorId: Arg.Any<Guid?>(),
                organizationId: Arg.Any<Guid?>(),
                groupId: Arg.Any<Guid?>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<EventListDto>
            {
                Items = featuredEvents?.ToList() ?? [],
                PageNumber = 1,
                PageSize = 3,
                TotalCount = featuredEvents?.Count ?? 0
            });
        _ctx.Services.AddSingleton(eventService);

        var categoryService = Substitute.For<ICategoryService>();
        categoryService.GetAllCategoriesAsync().Returns(new List<CategoryListDto>());
        _ctx.Services.AddSingleton(categoryService);

        var organizationService = Substitute.For<IOrganizationService>();
        organizationService.GetMyOrganizationsAsync().Returns(new List<OrganizationListDto>());
        _ctx.Services.AddSingleton(organizationService);

        var userService = Substitute.For<IUserService>();
        _ctx.Services.AddSingleton(userService);

        var publicExperienceService = Substitute.For<IPublicExperienceService>();
        publicExperienceService.GetCachedShellAsync().Returns(Task.FromResult(shell));
        _ctx.Services.AddSingleton(publicExperienceService);

        var areaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var homeDiscoveryService = Substitute.For<IHomeDiscoveryService>();
        homeDiscoveryService.LoadAsync(
                Arg.Any<Guid?>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(new HomeDiscoveryDto
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
                }
            });
        _ctx.Services.AddSingleton(homeDiscoveryService);
        _ctx.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Interop.IHomeDiscoveryGeolocation>());

        var translation = Substitute.For<ITranslationService>();
        translation.T(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(call => call.ArgAt<string?>(1) ?? call.ArgAt<string>(0));
        _ctx.Services.AddSingleton(translation);

        // Add dialog and snackbar services
        _ctx.Services.AddSingleton(Substitute.For<IDialogService>());
        _ctx.Services.AddSingleton(Substitute.For<ISnackbar>());
    }
}
