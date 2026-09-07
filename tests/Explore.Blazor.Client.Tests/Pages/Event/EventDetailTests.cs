using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Blazouter.Services;
using Explore.Blazor.Client.Components.EventReporting;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Shared;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Event;

public sealed class EventDetailTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();

    public EventDetailTests()
    {
        _ctx.Services.AddSingleton<TimeProvider>(
            new FixedTimeProvider(TestTime.UtcNow));
    }

    [Test]
    public async Task GetImageUrl_WhenFeaturedImageUriMissing_UsesPublicStorageObjectUrl()
    {
        var imageId = Guid.NewGuid();
        var component = new EventDetail();
        SetProperty(component, "Navigation", _ctx.Services.GetRequiredService<NavigationManager>());
        SetField(component, "_eventDetails", new EventDto
        {
            Id = Guid.NewGuid(),
            FeaturedImageId = imageId,
            FeaturedImageUri = null
        });

        var imageUrl = InvokePrivate<string?>(component, "GetImageUrl");

        await Assert.That(imageUrl).IsNotNull();
        await Assert.That(imageUrl!).EndsWith($"/api/storageobject/{imageId}/content");
    }

    [Test]
    public async Task Render_WhenBackgroundColorMissing_KeepsThemeBackgroundAndPublishesFullBleedLayoutStyle()
    {
        var eventDto = CreateEventDto("PUBLISHED", "Published");
        eventDto = eventDto with { BackgroundColor = null, BackgroundImageUri = "https://example.test/background.webp", BackgroundEffect = "SoftOverlay" };
        var appearanceState = new MainContentAppearanceState();
        RegisterEventDetailServices(eventDto);
        _ctx.Services.AddSingleton(appearanceState);

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(appearanceState.HasAppearance).IsTrue();
        await Assert.That(appearanceState.Style).Contains("--layout-padding-inline: 0px;");
        await Assert.That(appearanceState.Style.Contains("background:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(appearanceState.Style.Contains("url(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(appearanceState.Style.Contains("linear-gradient", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenBackgroundColorPresent_PublishesBackgroundColorOnly()
    {
        var eventDto = CreateEventDto("PUBLISHED", "Published");
        eventDto = eventDto with { BackgroundColor = "#123456", BackgroundImageUri = "https://example.test/background.webp", BackgroundEffect = "StrongOverlay" };
        var appearanceState = new MainContentAppearanceState();
        RegisterEventDetailServices(eventDto);
        _ctx.Services.AddSingleton(appearanceState);

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(appearanceState.HasAppearance).IsTrue();
        await Assert.That(appearanceState.Style).Contains("--layout-padding-inline: 0px;");
        await Assert.That(appearanceState.Style).Contains("background: #123456;");
        await Assert.That(appearanceState.Style.Contains("url(", StringComparison.Ordinal)).IsFalse();
        await Assert.That(appearanceState.Style.Contains("linear-gradient", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenDraftLifecycleLinksReturned_ShowsManagementTopBarActions()
    {
        RegisterEventDetailServices(CreateEventDto("DRAFT", "Draft", "edit", "publish", "cancel", "archive"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("event-detail-action-bar", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("event-detail-wrapper--with-action-bar");
        await Assert.That(cut.Markup).Contains("Return to Edit");
        await Assert.That(cut.Markup).Contains("Publish");
        await Assert.That(cut.Markup).Contains("Cancel");
        await Assert.That(cut.Markup).Contains("Archive");
    }

    [Test]
    public async Task Render_WhenLifecycleLinksMissing_HidesManagementTopBarActions()
    {
        RegisterEventDetailServices(CreateEventDto("DRAFT", "Draft"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup.Contains("event-detail-action-bar", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("event-detail-wrapper--with-action-bar", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Return to Edit", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Publish", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Archive", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenStartRegistrationLinkExists_ShowsTicketSelectionAction()
    {
        var eventDto = CreateEventDto("PUBLISHED", "Published", "start-registration");
        RegisterEventDetailServices(eventDto);

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForAssertion(() => cut.Markup.Contains("Select tickets", StringComparison.Ordinal));

        await Assert.That(cut.FindAll($"a[href='/registration/events/{eventDto.Id}/tickets']").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Render_WhenRegistrationLinksAreMissing_HidesTicketSelectionAction()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).DoesNotContain("Select tickets");
    }

    [Test]
    public async Task Render_WhenParticipationLinksAreMissing_HidesParticipationCard()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).DoesNotContain("event-registration-card");
        await Assert.That(cut.Markup).DoesNotContain("Register now");
    }

    [Test]
    public async Task Render_WhenExternalRegistrationLinkExists_UsesHalTitleAndStoredRedirectHref()
    {
        const string href = "/api/events/public-actions/456/redirect?surface=event_detail";
        const string title = "Continue with the organizer";
        var eventDto = CreateEventDto("PUBLISHED", "Published");
        eventDto = eventDto with { AdditionalProperties = CreateHalLink("external-registration", href, title) };
        RegisterEventDetailServices(eventDto);

        var cut = _ctx.RenderMudComponent<EventDetail>();
        var link = cut.WaitForElement($"a[href='{href}']", TimeSpan.FromSeconds(3));

        await Assert.That(link.TextContent).Contains(title);
        await Assert.That(link.GetAttribute("target")).IsEqualTo("_blank");
        await Assert.That(link.GetAttribute("rel")).IsEqualTo("noopener noreferrer");
        await Assert.That(cut.Markup).DoesNotContain("Register now");
    }

    [Test]
    public async Task Render_WhenReportEventLinkReturned_ShowsHeaderReportAction()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published", "report-event"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("Report Event", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("event-detail-header-actions__report");
        await Assert.That(cut.Markup).Contains("event-detail-header-actions__report-button");
        await Assert.That(cut.Markup).Contains("Report Event");
        await Assert.That(cut.Markup.Contains("event-sidebar-link--button", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenReportEventLinkMissing_HidesHeaderReportAction()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup.Contains("Report Event", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("event-detail-header-actions__report", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("event-sidebar-link--button", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task BuildReportReturnPath_WhenCurrentEventPage_AddsReportIntent()
    {
        var eventId = Guid.NewGuid();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events/{eventId}");
        var component = new EventDetail();
        SetProperty(component, "Navigation", navigation);
        SetProperty(component, "EventId", eventId);

        var returnPath = InvokePrivate<string>(component, "BuildReportReturnPath");

        await Assert.That(returnPath).IsEqualTo($"/events/{eventId}?report=1");
    }

    [Test]
    public async Task OpenReportEventDialogAsync_WhenAnonymous_ShowsReportSpecificLoginPrompt()
    {
        var eventId = Guid.NewGuid();
        _ctx.SetAnonymousUser();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events/{eventId}");
        var dialogReference = Substitute.For<IDialogReference>();
        var dialogService = Substitute.For<IDialogService>();
        dialogService
            .ShowAsync<LoginPromptDialog>(
                Arg.Any<string>(),
                Arg.Any<DialogParameters>(),
                Arg.Any<DialogOptions>())
            .Returns(Task.FromResult(dialogReference));

        var component = new EventDetail();
        SetProperty(component, "Navigation", navigation);
        SetProperty(component, "DialogService", dialogService);
        SetProperty(component, "AccessibilityFocusService", CreateFocusService());
        SetProperty(component, "AuthStateProvider", CreateAuthStateProvider(isAuthenticated: false));
        SetProperty(component, "EventId", eventId);
        SetField(component, "_eventDetails", CreateEventDto("PUBLISHED", "Published", "report-event"));
        SetField(component, "_canReport", true);
        SetField(component, "_isAuthenticated", false);

        await InvokePrivateTaskAsync(component, "OpenReportEventDialogAsync");

        await dialogService.Received(1).ShowAsync<LoginPromptDialog>(
            "Sign in",
            Arg.Is<DialogParameters>(parameters =>
                parameters.Get<string>("ReturnUrl") == $"/events/{eventId}?report=1" &&
                parameters.Get<string>("Title") == "Need to report this?" &&
                parameters.Get<string>("Message") == "Sign in to report content that breaks our rules. You can also file a legal complaint without signing in." &&
                parameters.Get<string>("PrimaryActionText") == "Sign in" &&
                parameters.Get<string>("SecondaryActionText") == "Cancel"),
            Arg.Any<DialogOptions>());
    }

    [Test]
    public async Task TryOpenPendingReportDialogAsync_WhenAuthenticatedReportIntent_OpensDialogAndClearsIntent()
    {
        var eventId = Guid.NewGuid();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo($"/events/{eventId}?report=1");
        var dialogReference = Substitute.For<IDialogReference>();
        dialogReference.Result.Returns(DialogResult.Cancel());
        var dialogService = Substitute.For<IDialogService>();
        dialogService
            .ShowAsync<ReportEventDialog>(
                Arg.Any<string>(),
                Arg.Any<DialogParameters>(),
                Arg.Any<DialogOptions>())
            .Returns(Task.FromResult(dialogReference));
        var authStateProvider = CreateAuthStateProvider(isAuthenticated: true);

        var component = new EventDetail();
        SetProperty(component, "Navigation", navigation);
        SetProperty(component, "DialogService", dialogService);
        SetProperty(component, "AccessibilityFocusService", CreateFocusService());
        SetProperty(component, "AuthStateProvider", authStateProvider);
        SetProperty(component, "EventId", eventId);
        SetProperty(component, "ReportIntent", "1");
        SetField(component, "_eventDetails", CreateEventDto("PUBLISHED", "Published", "report-event"));
        SetField(component, "_canReport", true);

        await InvokePrivateTaskAsync(component, "TryOpenPendingReportDialogAsync");
        await Assert.That(GetField<bool>(component, "_hasHandledReportIntent")).IsTrue();

        await dialogService.Received(1).ShowAsync<ReportEventDialog>(
            "Report Event",
            Arg.Any<DialogParameters>(),
            Arg.Any<DialogOptions>());
        await Assert.That(navigation.Uri.Contains("report=1", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenModerationReportsLinkReturned_ShowsManagementReportsNavigation()
    {
        var eventDto = CreateEventDto("PUBLISHED", "Published", "moderation-reports");
        var eventId = eventDto.Id!.Value;
        RegisterEventDetailServices(eventDto);

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("Moderation Reports", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("event-detail-action-bar");
        await Assert.That(cut.Markup).Contains("Reports");
        await Assert.That(cut.Markup).Contains("Moderation Reports");
        await Assert.That(cut.Markup).Contains($"/events/{eventId}/moderation/reports");
    }

    [Test]
    public async Task Render_WhenModerationReportsLinkMissing_HidesManagementReportsNavigation()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup.Contains("Moderation Reports", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("/moderation/reports", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenOnlyModerateLinkReturned_ShowsModerateTopBarWithoutEdit()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published", "moderate-light"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("event-detail-action-bar", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Moderate");
        await Assert.That(cut.Markup.Contains("Return to Edit", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Edit<", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains("Cancel", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenOnlyHeavyModerateLinkReturned_ShowsHeavyRedactTopBarWithoutEdit()
    {
        RegisterEventDetailServices(CreateEventDto("DRAFT", "Draft", "moderate-heavy"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("event-detail-action-bar", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Heavy Redact");
        await Assert.That(cut.Markup.Contains("Return to Edit", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Edit<", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Moderate<", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenLightAndHeavyModerationLinksReturned_ShowsBothModerationActions()
    {
        RegisterEventDetailServices(CreateEventDto("PUBLISHED", "Published", "moderate-light", "moderate-heavy"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("event-detail-action-bar", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Moderate");
        await Assert.That(cut.Markup).Contains("Heavy Redact");
        await Assert.That(cut.Markup.Contains("Return to Edit", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenOnlyUnmoderateLinkReturned_ShowsRestoreTopBarWithoutEdit()
    {
        RegisterEventDetailServices(CreateEventDto("MODERATED", "Moderated", "unmoderate"));

        var cut = _ctx.RenderMudComponent<EventDetail>();
        cut.WaitForState(() => cut.Markup.Contains("event-detail-action-bar", StringComparison.Ordinal), TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains("Restore");
        await Assert.That(cut.Markup.Contains("Return to Edit", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Edit<", StringComparison.Ordinal)).IsFalse();
        await Assert.That(cut.Markup.Contains(">Moderate<", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Render_WhenEventAgendaHasMultipleSessions_ShowsSessionDetailLinks()
    {
        var eventId = Guid.NewGuid();
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        var eventDto = CreateEventDto("PUBLISHED", "Published") with
        {
            Id = eventId,
            SessionCount = 2
        };

        var sessions = new List<EventSessionListDto>
        {
            new()
            {
                Id = firstSessionId,
                EventId = eventId,
                Title = "Opening class",
                EventSessionStatusFullName = "Published",
                EventSessionStatusMasterCode = "PUBLISHED",
                StartTime = new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero)
            },
            new()
            {
                Id = secondSessionId,
                EventId = eventId,
                Title = "Workshop",
                EventSessionStatusFullName = "Draft",
                EventSessionStatusMasterCode = "DRAFT",
                StartTime = new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero)
            }
        };

        RegisterEventDetailServices(
            eventDto,
            sessions,
            [
                new EventDayListDto
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    LocalDate = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
                    Label = "Day 1"
                }
            ],
            [
                new EventAgendaItemListDto
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    Title = "Doors open",
                    LocalStartDate = new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero),
                    LocalStartTime = new TimeSpan(8, 30, 0),
                    LocalEndTime = new TimeSpan(9, 0, 0),
                    StartTime = new DateTimeOffset(2026, 6, 25, 8, 30, 0, TimeSpan.Zero),
                    EndTime = new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero)
                }
            ]);

        var cut = _ctx.RenderMudComponent<EventDetail>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains($"/events/{eventId}/sessions/{firstSessionId}", StringComparison.Ordinal))
                throw new InvalidOperationException("First session detail link was not rendered in the agenda section.");
        }, TimeSpan.FromSeconds(3));

        await Assert.That(cut.Markup).Contains($"/events/{eventId}/sessions/{secondSessionId}");

        // Click the first day item in the Miller Columns to select it and load its agenda items (including "Doors open")
        var dayItem = cut.Find(".agenda-miller__column--days .agenda-miller__item");
        dayItem.Click();

        await Assert.That(cut.Markup).Contains("Doors open");
    }


    [Test]
    public async Task RefreshRestoredEventDetailsAsync_WhenFreshHalLinksArrive_EnablesManagementTopBar()
    {
        var eventId = Guid.NewGuid();
        var restoredEvent = CreateEventDto("DRAFT", "Draft");
        restoredEvent = restoredEvent with { Id = eventId };

        var refreshedEvent = CreateEventDto("DRAFT", "Draft", "edit", "publish", "cancel", "archive");
        refreshedEvent = refreshedEvent with { Id = eventId };

        var eventService = Substitute.For<IEventService>();
        eventService.GetEventByIdAsync(eventId).Returns(refreshedEvent);

        var component = new EventDetail();
        SetProperty(component, "EventId", eventId);
        SetProperty(component, "EventService", eventService);
        SetProperty(component, "MainContentAppearanceState", new MainContentAppearanceState());
        SetProperty(component, "Logger", Substitute.For<ILogger<EventDetail>>());
        SetField(component, "_eventDetails", restoredEvent);
        SetField(component, "_isCheckingAuth", false);

        await InvokePrivateTaskAsync(component, "RefreshRestoredEventDetailsAsync");

        await Assert.That(GetProperty<bool>(component, "HasManagementTopBar")).IsTrue();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task DirectoryOperatorDisclosure_RendersForPaidPlatformManagedEvent()
    {
        EventDto eventDto = CreateEventDto("PUBLISHED", "Published", "start-registration") with
        {
            TicketPriceSummary = new TicketPriceSummary
            {
                SummaryCode = "FIXED",
                CurrencyCode = "EUR",
                CurrencyMinorUnitDigits = 2,
                FromAmountMinor = 1200
            }
        };
        RegisterEventDetailServices(eventDto);
        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedShellAsync().Returns(new PublicExperienceShellDto
        {
            DirectoryOperator = DirectoryOperator()
        });
        _ctx.Services.AddSingleton(publicExperience);

        var cut = _ctx.RenderMudComponent<EventDetail>();

        var notice = cut.WaitForElement("[data-testid='event-detail-directory-operator-disclosure']");

        await Assert.That(notice.TextContent)
            .Contains("Community Directory Foundation");
        await Assert.That(notice.QuerySelector("a[href='https://directory.example.test/legal']"))
            .IsNotNull();
        await Assert.That(notice.QuerySelector("a[href='https://directory.example.test/privacy']"))
            .IsNotNull();
    }

    [Test]
    public void DirectoryOperatorDisclosure_DoesNotRenderForFreeEvent()
    {
        EventDto eventDto = CreateEventDto("PUBLISHED", "Published", "start-registration") with
        {
            TicketPriceSummary = new TicketPriceSummary
            {
                SummaryCode = "FREE",
                CurrencyMinorUnitDigits = 0,
                FromAmountMinor = 0
            }
        };
        RegisterEventDetailServices(eventDto);
        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedShellAsync().Returns(new PublicExperienceShellDto
        {
            DirectoryOperator = DirectoryOperator()
        });
        _ctx.Services.AddSingleton(publicExperience);

        var cut = _ctx.RenderMudComponent<EventDetail>();

        cut.WaitForElement("a[href*='/registration/events/'][href$='/tickets']");
        cut.WaitForAssertion(() =>
            Assert.That(cut.FindAll("[data-testid='event-detail-directory-operator-disclosure']")).IsEmpty());
    }

    [Test]
    [Arguments("SLIDING_SCALE")]
    [Arguments("MIXED")]
    [Arguments("MIXED_WITH_FREE")]
    public async Task MissingDirectoryOperator_BlocksEveryNonFreeRegistrationSummary(
        string summaryCode)
    {
        EventDto eventDto = CreateEventDto("PUBLISHED", "Published", "start-registration") with
        {
            TicketPriceSummary = new TicketPriceSummary
            {
                SummaryCode = summaryCode,
                CurrencyCode = "EUR",
                FromAmountMinor = 500
            }
        };
        RegisterEventDetailServices(eventDto);
        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedShellAsync().Returns(new PublicExperienceShellDto());
        _ctx.Services.AddSingleton(publicExperience);

        var cut = _ctx.RenderMudComponent<EventDetail>();

        cut.WaitForElement("[data-testid='event-detail-paid-identity-unavailable']");
        await Assert.That(cut.FindAll($"a[href='/registration/events/{eventDto.Id}/tickets']")).IsEmpty();
    }

    [Test]
    public async Task CancelledPaidEvent_PrioritizesCancellationOverIdentityWarning()
    {
        EventDto eventDto = CreateEventDto("CANCELLED", "Cancelled", "start-registration") with
        {
            TicketPriceSummary = new TicketPriceSummary
            {
                SummaryCode = "MIXED_WITH_FREE",
                CurrencyCode = "EUR",
                FromAmountMinor = 500
            }
        };
        RegisterEventDetailServices(eventDto);
        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedShellAsync().Returns(new PublicExperienceShellDto());
        _ctx.Services.AddSingleton(publicExperience);

        var cut = _ctx.RenderMudComponent<EventDetail>();

        cut.WaitForElement("[data-testid='event-detail-cancelled']");
        await Assert.That(cut.FindAll("[data-testid='event-detail-paid-identity-unavailable']")).IsEmpty();
    }

    private static TenantDirectoryOperatorPublicDto DirectoryOperator() => new()
    {
        DocumentRevision = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101"),
        PublicName = "Community Directory",
        LegalName = "Community Directory Foundation",
        OperatorKindCode = "NONPROFIT",
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "directory@example.test",
        LegalNoticeUrl = "https://directory.example.test/legal",
        TermsUrl = "https://directory.example.test/terms",
        PrivacyUrl = "https://directory.example.test/privacy"
    };

    private void RegisterEventDetailServices(
        EventDto eventDto,
        ICollection<EventSessionListDto>? sessions = null,
        ICollection<EventDayListDto>? days = null,
        ICollection<EventAgendaItemListDto>? eventAgendaItems = null,
        ICollection<EventSessionAgendaItemListDto>? sessionAgendaItems = null)
    {
        _ctx.SetAnonymousUser();
        _ctx.JSInterop.SetupVoid("window.scrollTo", _ => true).SetVoidResult();

        var eventService = Substitute.For<IEventService>();
        eventService.GetEventByIdAsync(Arg.Any<Guid>()).Returns(eventDto);

        var eventSessionService = Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventSessionService>();
        eventSessionService.GetSessionsByEventAsync(Arg.Any<Guid>(), Arg.Any<bool>())
            .Returns(sessions ?? new List<EventSessionListDto>());

        var eventDayService = Substitute.For<IEventDayService>();
        eventDayService.GetDaysByEventAsync(Arg.Any<Guid>())
            .Returns(days ?? new List<EventDayListDto>());

        var eventAgendaItemService = Substitute.For<IEventAgendaItemService>();
        eventAgendaItemService.GetAgendaItemsByEventAsync(Arg.Any<Guid>())
            .Returns(eventAgendaItems ?? new List<EventAgendaItemListDto>());

        var sessionAgendaItemService = Substitute.For<IEventSessionAgendaItemService>();
        sessionAgendaItemService.GetAgendaItemsBySessionAsync(Arg.Any<Guid>())
            .Returns(sessionAgendaItems ?? new List<EventSessionAgendaItemListDto>());

        _ctx.Services.AddSingleton(eventService);
        _ctx.Services.AddSingleton(eventSessionService);
        _ctx.Services.AddSingleton(Substitute.For<Explore.Blazor.Client.Contracts.Services.IEventModerationService>());
        _ctx.Services.AddSingleton(Substitute.For<IMapsService>());
        _ctx.Services.AddScoped<RouterStateService>();
        _ctx.Services.AddSingleton(Substitute.For<IUserService>());
        _ctx.Services.AddSingleton(Substitute.For<IEventAspectService>());
        _ctx.Services.AddSingleton(sessionAgendaItemService);
        _ctx.Services.AddSingleton(eventAgendaItemService);
        _ctx.Services.AddSingleton(eventDayService);
        _ctx.Services.AddSingleton(Substitute.For<IActorSubscriptionService>());
        _ctx.Services.AddSingleton(Substitute.For<ITagService>());
        _ctx.Services.AddSingleton(Substitute.For<ICategoryService>());
        _ctx.Services.AddScoped<MainContentAppearanceState>();
        _ctx.Services.AddSingleton(Substitute.For<ILogger<EventDetail>>());
    }

    private static IAccessibilityFocusService CreateFocusService()
    {
        var focusService = Substitute.For<IAccessibilityFocusService>();
        focusService.SaveFocusAsync().Returns(Task.CompletedTask);
        focusService.RestoreFocusAsync(null).ReturnsForAnyArgs(Task.CompletedTask);
        return focusService;
    }

    private static AuthenticationStateProvider CreateAuthStateProvider(bool isAuthenticated)
    {
        var identity = isAuthenticated
            ? new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
                authenticationType: "TestAuth")
            : new ClaimsIdentity();
        return new FixedAuthenticationStateProvider(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private sealed class FixedAuthenticationStateProvider(AuthenticationState authenticationState) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(authenticationState);
    }

    private static EventDto CreateEventDto(string statusCode, string statusName, params string[] linkRels)
    {
        return new EventDto
        {
            Id = Guid.NewGuid(),
            ConcurrencyStamp = Guid.NewGuid(),
            Title = "Community Program",
            Content = "A community event.",
            ActorId = Guid.NewGuid(),
            ActorDisplayName = "ISLAMU",
            ActorTypeId = 2,
            ActorTypeFullName = "Organization",
            EventTypeFullName = "Program",
            EventStatusId = statusCode switch
            {
                "PUBLISHED" => 2,
                "MODERATED" => 6,
                _ => 1
            },
            EventStatusFullName = statusName,
            EventStatusMasterCode = statusCode,
            EventFormatId = 1,
            EventFormatFullName = "In person",
            EventFormatMasterCode = "IN_PERSON",
            VisibilityTypeId = 1,
            VisibilityTypeFullName = "Public",
            VisibilityTypeMasterCode = "PUBLIC",
            FirstSessionDate = TestTime.UtcNow.Date.AddDays(7),
            LastSessionDate = TestTime.UtcNow.Date.AddDays(7),
            AdditionalProperties = CreateHalLinks(linkRels)
        };
    }

    private static Dictionary<string, object> CreateHalLinks(params string[] linkRels)
    {
        var links = string.Join(
            ",",
            linkRels.Select(rel => $"\"{rel}\":{{\"href\":\"/api/event/1\",\"method\":\"GET\"}}"));
        using var doc = JsonDocument.Parse($"{{\"_links\":{{{links}}}}}");
        return new Dictionary<string, object>
        {
            ["_links"] = doc.RootElement.GetProperty("_links").Clone()
        };
    }

    private static Dictionary<string, object> CreateHalLink(string relation, string href, string title)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [relation] = new { href, method = "GET", title }
        }));
        return new Dictionary<string, object>
        {
            ["_links"] = doc.RootElement.Clone()
        };
    }

    private static T InvokePrivate<T>(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        return (T?)method.Invoke(instance, null)
            ?? throw new InvalidOperationException($"Method {methodName} returned null.");
    }

    private static async Task InvokePrivateTaskAsync(object instance, string methodName)
    {
        var method = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Method {methodName} was not found.");

        var task = method.Invoke(instance, null) as Task
            ?? throw new InvalidOperationException($"Method {methodName} did not return a task.");
        await task;
    }

    private static T GetProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");

        return (T?)property.GetValue(instance)
            ?? throw new InvalidOperationException($"Property {propertyName} returned null.");
    }

    private static T GetField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");

        return (T?)field.GetValue(instance)
            ?? throw new InvalidOperationException($"Field {fieldName} returned null.");
    }

    private static void SetField<T>(object instance, string fieldName, T value)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Field {fieldName} was not found.");
        field.SetValue(instance, value);
    }

    private static void SetProperty<T>(object instance, string propertyName, T value)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Property {propertyName} was not found.");
        property.SetValue(instance, value);
    }
}
