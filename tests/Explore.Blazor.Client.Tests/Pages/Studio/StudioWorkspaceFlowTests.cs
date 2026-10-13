using System.Text.Json;
using Blazouter.Enums;
using Blazouter.Extensions;
using Blazouter.Models;
using Explore.Blazor.Client.Contracts.Services.CustomProperties;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Models.Events;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Components;
using Explore.Blazor.Client.Pages.Events.Sessions;
using Explore.Blazor.Client.Pages.Studio;
using Explore.Blazor.Client.Providers;
using Explore.Blazor.Client.Routing.Guards;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Studio;

public sealed class StudioWorkspaceFlowTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly RoutedNavigation _navigation = new();
    private readonly IEventService _events;
    private readonly IEventSessionService _sessions;
    private readonly Guid _eventId = Guid.CreateVersion7();
    private readonly Guid _tenantId = Guid.CreateVersion7();
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public StudioWorkspaceFlowTests()
    {
        _ctx.Services.AddBlazouter();
        _ctx.Services.AddSingleton<NavigationManager>(_navigation);
        _ctx.Services.AddScoped<StudioEventContextState>();
        _events = _ctx.AddMockService<IEventService>();
        _sessions = _ctx.AddMockService<IEventSessionService>();
        _ctx.AddMockService<IEventLookupService>();
        _ctx.AddMockService<IDemographicLookupService>();
        _ctx.AddMockService<ICultureLookupService>();
        _ctx.AddMockService<IImageStorageService>();
        _ctx.AddMockService<IEventResourceService>()
            .AudienceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EventResourceAudiencePageResource { _links = new Dictionary<string, HalLink>() });
        _ctx.AddMockService<IEventRegistrationPolicyService>();
        _ctx.AddMockService<IUserService>();
        _ctx.AddMockService<ICustomPropertyDefinitionService>();
        _ctx.AddMockService<IRegistrationFormAuthoringService>();
        _ctx.AddMockService<IEventTemplateService>()
            .GetTemplatesAsync(Arg.Any<int?>(), 1, 100, Arg.Any<CancellationToken>())
            .Returns(new HalCollectionResourceOfEventTemplateListDto());
        _ctx.Services.AddSingleton<MainContentAppearanceState>();
        _ctx.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _ctx.Services.AddScoped<WorkspaceRouteClassifier>();
        _ctx.Services.AddScoped<UiShellState>();
        _ctx.AddMockService<IEventCreationEligibilityService>()
            .GetEligibilityAsync().Returns(EventCreationEligibility.NotEligible);
        _ctx.AddMockService<IUiShellContextService>()
            .GetCachedContextAsync(Arg.Any<CancellationToken>()).Returns(new UiShellContextDto());
        _events.GetMyEventsPagedAsync(1, 100, Arg.Any<CancellationToken>())
            .Returns(PaginatedResult<EventListDto>.Empty(1, 100));
        _sessions.GetSessionsByEventAsync(_eventId, true, Arg.Any<CancellationToken>())
            .Returns(new List<EventSessionListDto>());
        _events.GetEventCreationContextAsync(Arg.Any<CancellationToken>())
            .Returns(new EventCreationContextDto
            {
                CanCreate = true,
                DefaultPublisherMode = "personal",
                PublisherOptions = [new EventCreationPublisherOptionDto
                {
                    PublisherMode = "personal", CanPublish = true, DisplayName = "Publisher"
                }]
            });
        _ctx.JSInterop.SetupVoid("mudTimePicker.destroyPointerEvents", _ => true).SetVoidResult();
        _ctx.JSInterop.SetupVoid("Blazor._internal.NavigationLock.enableNavigationPrompt", _ => true).SetVoidResult();
        _ctx.JSInterop.SetupVoid("Blazor._internal.NavigationLock.disableNavigationPrompt", _ => true).SetVoidResult();
        _events.GetEventByIdAsync(_eventId).Returns(Resource("add-session", "edit"));
        _sessions.GetEventSessionCreateContextAsync(_eventId, Arg.Any<CancellationToken>())
            .Returns(new EventSessionCreateContextDto
            {
                EventId = _eventId,
                TimeZoneId = "UTC",
                EventStartDate = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero),
                EventEndDate = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Defaults = new EventSessionCreateDefaultsDto
                {
                    SessionDate = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero),
                    StartTime = "09:00:00",
                    EndTime = "10:00:00",
                    RegistrationModeId = 1
                }
            });
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task ActualRouter_DirtySessionStay_RetainsFieldAndMountedForm()
    {
        _ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var router = await OpenSessionAsync();
        var form = router.FindComponent<CreateSession>();
        var input = form.Find("input");
        await input.ChangeAsync(new ChangeEventArgs { Value = "برنامج" });
        await form.InvokeAsync(() => form.FindComponent<MudTextField<string>>().Instance.FocusAsync());
        var inputId = form.Find("input").Id;

        await router.InvokeAsync(() => _navigation.NavigateAsync($"/studio/events/{_eventId}"));

        await Assert.That(_navigation.ToBaseRelativePath(_navigation.Uri))
            .IsEqualTo($"studio/events/{_eventId}/sessions/create");
        await Assert.That(router.FindComponent<CreateSession>().Instance).IsSameReferenceAs(form.Instance);
        await Assert.That(router.FindComponent<CreateSession>().Find("input").GetAttribute("value"))
            .IsEqualTo("برنامج");
        await Assert.That(router.FindAll("[data-testid='studio-event-overview']")).IsEmpty();
        await Assert.That(router.FindComponent<CreateSession>().Find("input").Id).IsEqualTo(inputId);
    }

    [Test]
    public async Task ActualRouter_ConfirmedSave_ClearsLockAndReturnsToManagedSchedule()
    {
        // A rejected confirmation would prevent this route if successful save left the form dirty.
        _ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        _sessions.CreateSessionAsync(Arg.Any<CreateEventSessionDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Id = Guid.CreateVersion7() });
        var router = await OpenSessionAsync();
        await router.FindComponent<CreateSession>().Find("input")
            .ChangeAsync(new ChangeEventArgs { Value = "Prepared program" });
        var form = router.FindComponent<CreateSession>().FindComponent<EventSessionForm>();
        await router.InvokeAsync(() => form.Instance.EndDateChanged.InvokeAsync(new DateTime(2027, 1, 2)));
        var scheduleRendered = NextRender(router, () =>
            router.FindAll("[data-testid='studio-event-overview']").Count == 1 && router.FindAll("form").Count == 0);
        await router.FindComponent<CreateSession>().FindComponent<EditForm>()
            .InvokeAsync(() => router.FindComponent<CreateSession>().FindComponent<EditForm>()
                .Instance.OnValidSubmit.InvokeAsync());
        await _navigation.Pending.WaitAsync(Timeout);
        await scheduleRendered;

        await Assert.That(_navigation.ToBaseRelativePath(_navigation.Uri))
            .IsEqualTo($"studio/events/{_eventId}/schedule?programUpdated=1");
        await Assert.That(router.FindAll("form")).IsEmpty();
    }

    [Test]
    public async Task ActualRouter_DisposedDirtyForm_UnregistersInternalLock()
    {
        _ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var router = await OpenSessionAsync();
        await router.FindComponent<CreateSession>().Find("input")
            .ChangeAsync(new ChangeEventArgs { Value = "Unsaved program" });
        await _ctx.DisposeComponentsAsync();

        await _navigation.NavigateAsync("/studio/events");

        await Assert.That(_navigation.ToBaseRelativePath(_navigation.Uri)).IsEqualTo("studio/events");
    }

    private async Task<IRenderedComponent<Blazouter.Components.Router>> OpenSessionAsync()
    {
        return await OpenRouteAsync($"/studio/events/{_eventId}/sessions/create", "form");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualRouter_DetailsSave_RefreshesLoadedOverviewWithoutRestoringOldAuthority(bool denyRead)
    {
        _ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='studio-event-overview']");
        var details = NextRender(router, () => router.FindComponents<EventEdit>().Count == 1
            && router.FindAll("form").Count == 1);
        await router.InvokeAsync(() => _navigation.NavigateAsync($"/studio/events/{_eventId}/details"));
        await details;
        _events.UpdateEventAsync(_eventId, Arg.Any<EventDraftEditModel>()).Returns(_ =>
        {
            _events.GetEventByIdAsync(_eventId).Returns(denyRead ? null : Resource() with { Title = "Saved details" });
            return new BaseCommandResponseOfGuid { Success = true, Id = _eventId };
        });
        var overview = NextRender(router, () => router.FindComponents<StudioEventShell>().Count == 1
            && router.FindComponents<EventEdit>().Count == 0);

        await router.FindComponent<EventEdit>().FindComponent<EditForm>()
            .InvokeAsync(() => router.FindComponent<EventEdit>().FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync());
        await _navigation.Pending.WaitAsync(Timeout);
        await overview;

        var state = _ctx.Services.GetRequiredService<StudioEventContextState>();
        await Assert.That(state.Event?.Title).IsEqualTo(denyRead ? null : "Saved details");
        await Assert.That(router.FindAll($"a[href='/studio/events/{_eventId}/details']")).IsEmpty();
        await Assert.That(_navigation.ToBaseRelativePath(_navigation.Uri)).IsEqualTo($"studio/events/{_eventId}");
    }

    [Test]
    public async Task CanonicalRouteConfig_RegistersManagementAndKeepsPublicEntryIndependent()
    {
        var routes = ActualRoutes();
        foreach (var (path, component) in new (string, Type)[]
        {
            ("/studio/events/:eventId", typeof(StudioEventShell)),
            ("/studio/events/:eventId/details", typeof(EventEdit)),
            ("/studio/events/:eventId/schedule", typeof(StudioEventShell)),
            ("/studio/events/:eventId/sessions/create", typeof(CreateSession)),
            ("/studio/events/:eventId/sessions/:sessionId/edit", typeof(EditSession)),
            ("/studio/events/:eventId/resources", typeof(StudioEventShell)),
            ("/studio/events/:eventId/analytics", typeof(StudioEventShell))
        })
        {
            var route = routes.Single(entry => entry.Path == path);
            await Assert.That(route.Component).IsEqualTo(component);
            await Assert.That(route.Guards).Contains(typeof(AuthenticatedRouteGuard));
        }
        await Assert.That(routes.Any(route => route.Path == "/events/:eventId/edit"
            || route.Path == "/events/:eventId/sessions/create"
            || route.Path == "/events/:eventId/sessions/:sessionId/edit"
            || route.Path == "/event-created/:eventId"
            || route.Path == "/studio/events/:eventId/publication")).IsFalse();
        await Assert.That(routes.Single(route => route.Path == "/events/create").Component).IsEqualTo(typeof(CreateEvent));
        await Assert.That(routes.Single(route => route.Path == "/events/:eventId/sessions/:sessionId").Guards?.Count ?? 0).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ActualRouter_DraftAndPublishSuccess_OpenCurrentOverview(bool publish)
    {
        _ctx.JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var requests = new List<CreateEventDraftRequestDto>();
        _events.CreateEventAsync(Arg.Any<CreateEventDraftRequestDto>(), Arg.Any<string?>())
            .Returns(call =>
            {
                var request = call.ArgAt<CreateEventDraftRequestDto>(0);
                requests.Add(request);
                _events.GetEventByIdAsync(_eventId).Returns(Resource("edit") with
                {
                    EventStatusId = request.EventStatusId,
                    EventStatusFullName = publish ? "Published" : "Draft"
                });
                return new BaseCommandResponseOfGuid { Success = true, Id = _eventId };
            });
        var router = await OpenRouteAsync("/events/create", "form");
        var create = router.FindComponent<CreateEvent>();
        await create.InvokeAsync(() => create.FindComponents<MudTextField<string>>()
            .Single(field => field.Instance.Label == "Event name").Instance.ValueChanged.InvokeAsync("Prepared event"));
        await create.InvokeAsync(() => create.FindComponent<ScheduleTimelineComposer>().Instance.OnAddSession.InvokeAsync());
        var fields = create.FindComponent<EventSessionForm>().Instance;
        await create.InvokeAsync(() => fields.TitleChanged.InvokeAsync("Prepared session"));
        await create.InvokeAsync(() => fields.SessionDateChanged.InvokeAsync(new DateTime(2027, 1, 2)));
        await create.InvokeAsync(() => fields.EndDateChanged.InvokeAsync(new DateTime(2027, 1, 2)));
        await create.InvokeAsync(() => fields.StartTimeChanged.InvokeAsync(TimeSpan.FromHours(9)));
        await create.InvokeAsync(() => fields.EndTimeChanged.InvokeAsync(TimeSpan.FromHours(10)));
        await create.Find("[data-testid='program-save']").ClickAsync(new MouseEventArgs());
        var entered = NextRender(router, () => router.FindAll("[data-testid='event-status']").Count == 1);
        if (publish) await create.Find("form").SubmitAsync();
        else await create.Find("button.create-event__draft-button").ClickAsync(new MouseEventArgs());
        await _navigation.Pending.WaitAsync(Timeout);
        await entered;

        await Assert.That(_navigation.ToBaseRelativePath(_navigation.Uri)).IsEqualTo($"studio/events/{_eventId}");
        await Assert.That(requests.Count).IsEqualTo(1);
        await Assert.That(requests[0].EventStatusId).IsEqualTo(publish ? 2 : 1);
        await Assert.That(requests[0].Sessions!.Single().Title).IsEqualTo("Prepared session");
        await Assert.That(router.Find("[data-testid='event-status']").TextContent).IsEqualTo(publish ? "Published" : "Draft");
        await Assert.That(router.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(1, "Information only", false)]
    [Arguments(2, "Walk-in", false)]
    [Arguments(3, "External", false)]
    [Arguments(4, "Platform", true)]
    public async Task Overview_ParticipationVariants_UseOnlyCurrentHalActions(int mode, string name, bool ticketing)
    {
        var resource = ticketing
            ? Resource("edit", "configure-participation", "manage-ticket-types", "view-registration-analytics")
            : Resource("edit", "configure-participation");
        _events.GetEventByIdAsync(_eventId).Returns(resource with
        {
            ParticipationConfiguration = new ParticipationConfiguration
            {
                ParticipationHandlingModeId = mode,
                ParticipationHandlingModeName = name
            }
        });
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='studio-event-overview']");

        await Assert.That(router.Find("[data-testid='event-participation']").TextContent).IsEqualTo(name);
        await Assert.That(router.FindAll($"a[href='/studio/events/{_eventId}/registration']").Count).IsEqualTo(1);
        await Assert.That(router.FindAll($"a[href='/studio/events/{_eventId}/tickets']").Count).IsEqualTo(ticketing ? 1 : 0);
        await Assert.That(router.FindAll($"a[href='/studio/events/{_eventId}/analytics']").Count).IsEqualTo(ticketing ? 1 : 0);
        await Assert.That(router.FindAll("a[href$='/team'],a[href$='/danger-zone']")).IsEmpty();
    }

    [Test]
    [Arguments("analytics", "registration-analytics-route-unavailable")]
    [Arguments("resources", "resources-route-unavailable")]
    [Arguments("registration", "participation-route-unavailable")]
    [Arguments("tickets", "ticketing-route-unavailable")]
    public async Task DirectModuleRoute_WithoutCurrentRelation_FailsClosed(string section, string marker)
    {
        _events.GetEventByIdAsync(_eventId).Returns(Resource());
        var router = await OpenRouteAsync($"/studio/events/{_eventId}/{section}", $"[data-testid='{marker}']");

        await Assert.That(router.Find($"[data-testid='{marker}']").GetAttribute("role")).IsEqualTo("alert");
        await Assert.That(router.FindAll("form")).IsEmpty();
        await Assert.That(router.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    public async Task DirectOverview_ManagedReadUnavailable_DoesNotSubstitutePublicProgram()
    {
        _events.GetManagedEventProgramSummaryAsync(_eventId, Arg.Any<CancellationToken>()).Returns((EventProgramSummaryDto?)null);
        _events.GetEventProgramSummaryAsync(_eventId, Arg.Any<CancellationToken>()).Returns(Program("Public substitute"));
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='managed-program-unavailable']");

        await Assert.That(router.FindAll(".event-program-summary")).IsEmpty();
        await Assert.That(router.Find("[data-testid='managed-program-unavailable']").GetAttribute("role")).IsEqualTo("status");
    }

    [Test]
    public async Task ReloadDenial_RemovesAuthorityAndIgnoresLateManagedProgram()
    {
        var delayed = new TaskCompletionSource<EventProgramSummaryDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _events.GetManagedEventProgramSummaryAsync(_eventId, Arg.Any<CancellationToken>()).Returns(delayed.Task);
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='studio-event-overview']");
        var overview = router.FindComponent<StudioEventOverview>();
        var load = (Task)typeof(StudioEventOverview).GetField("_loadTask",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(overview.Instance)!;
        var denied = NextRender(router.FindComponent<StudioEventShell>(),
            () => router.FindAll("[data-testid='studio-event-overview']").Count == 0);
        _events.GetEventByIdAsync(_eventId).Returns((EventDto?)null);
        await router.InvokeAsync(() => _ctx.Services.GetRequiredService<StudioEventContextState>().ReloadAsync(_eventId));
        await denied;
        delayed.SetResult(Program("Obsolete private session"));
        await load.WaitAsync(Timeout);

        await Assert.That(router.FindAll(".event-program-summary")).IsEmpty();
        await Assert.That(router.FindAll($"a[href='/studio/events/{_eventId}/details']")).IsEmpty();
        await Assert.That(router.FindAll("[data-testid='studio-event-overview']")).IsEmpty();
    }

    [Test]
    public async Task Overview_CurrentManagedProgramAndReadiness_ExposeServerStateAndNativePublishCallback()
    {
        _events.GetEventByIdAsync(_eventId).Returns(Resource("edit", "publish-readiness", "publish"));
        _events.GetManagedEventProgramSummaryAsync(_eventId, Arg.Any<CancellationToken>()).Returns(Program("Private draft item"));
        _events.GetEventPublishReadinessAsync(_eventId, Arg.Any<CancellationToken>()).Returns(new EventPublishReadinessDto { EventId = _eventId, IsReady = true });
        _events.PublishEventAsync(_eventId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _events.GetEventByIdAsync(_eventId).Returns(Resource("edit") with { EventStatusFullName = "Published" });
            return new BaseCommandResponseOfGuid { Success = true, Id = _eventId };
        });
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='publish-event']");
        await Assert.That(router.Find(".event-program-summary__item-title").TextContent).IsEqualTo("Private draft item");
        await Assert.That(router.Find("[data-testid='publish-readiness']").GetAttribute("data-ready")).IsEqualTo("True");
        var context = _ctx.Services.GetRequiredService<StudioEventContextState>();
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Changed += () =>
        {
            if (context.Event?.EventStatusFullName == "Published") published.TrySetResult();
        };
        await router.Find("[data-testid='publish-event']").ClickAsync(new MouseEventArgs());
        await Assert.That(_ctx.Services.GetRequiredService<StudioEventContextState>().Event?.EventStatusFullName)
            .IsEqualTo("Published");
        await Assert.That(router.Find("[data-testid='event-status']").TextContent.Trim()).IsEqualTo("Published");
        await Assert.That(router.FindAll("[data-testid='publish-event']")).IsEmpty();
        await published.Task.WaitAsync(Timeout);
        await Assert.That(router.Find("[data-testid='event-status']").TextContent.Trim()).IsEqualTo("Published");
    }

    [Test]
    public async Task Overview_RtlAndLocalizedAction_RetainHeadingAndLinkSemantics()
    {
        var translation = _ctx.Services.GetRequiredService<ITranslationService>();
        translation.CurrentLanguage.Returns("ar");
        const string label = "تفاصيل";
        translation.T("ui.studio.event.details", Arg.Any<string?>()).Returns(label);
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='studio-event-overview']");
        var action = router.Find($"a[href='/studio/events/{_eventId}/details']");

        await Assert.That(router.Find("[data-testid='studio-event-overview']").GetAttribute("dir")).IsEqualTo("rtl");
        await Assert.That(action.TextContent.Trim()).IsEqualTo(label);
        await Assert.That(action.GetAttribute("tabindex")).IsNotEqualTo("1");
        await Assert.That(router.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    public async Task Overview_MissingStatusAndParticipation_AreExplicitWithoutInventingConfiguration()
    {
        _events.GetEventByIdAsync(_eventId).Returns(Resource() with
        {
            EventStatusFullName = null,
            ParticipationConfiguration = null
        });
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='studio-event-overview']");

        await Assert.That(string.IsNullOrWhiteSpace(router.Find("[data-testid='event-status']").TextContent)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(router.Find("[data-testid='event-participation']").TextContent)).IsFalse();
        await Assert.That(_ctx.Services.GetRequiredService<StudioEventContextState>().Event!.ParticipationConfiguration).IsNull();
        await Assert.That(router.FindAll(".studio-event-overview__actions a")).IsEmpty();
    }

    [Test]
    public async Task Overview_NotReady_DoesNotPublishDespiteLifecycleRelation()
    {
        _events.GetEventByIdAsync(_eventId).Returns(Resource("edit", "publish-readiness", "publish"));
        _events.GetEventPublishReadinessAsync(_eventId, Arg.Any<CancellationToken>()).Returns(new EventPublishReadinessDto
        {
            EventId = _eventId,
            IsReady = false,
            Errors = [new EventPublishReadinessErrorDto { FieldPath = "Sessions[0].StartTime", Message = "Schedule incomplete" }]
        });
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='publish-readiness']");

        await Assert.That(router.FindAll("[data-testid='publish-event']")).IsEmpty();
        await Assert.That(router.Find("li strong").TextContent).IsEqualTo("Sessions[0].StartTime");
    }

    [Test]
    [Arguments(1, true)]
    [Arguments(4, false)]
    public async Task Overview_TicketAuthority_ComesFromHalRatherThanParticipationMode(int mode, bool available)
    {
        _events.GetEventByIdAsync(_eventId).Returns((available ? Resource("manage-ticket-types") : Resource()) with
        {
            ParticipationConfiguration = new ParticipationConfiguration { ParticipationHandlingModeId = mode }
        });
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "[data-testid='studio-event-overview']");

        await Assert.That(router.FindAll($"a[href='/studio/events/{_eventId}/tickets']").Count).IsEqualTo(available ? 1 : 0);
    }

    [Test]
    public async Task DirectSessionEditAndReload_UseCanonicalManagedIdentity()
    {
        var sessionId = Guid.CreateVersion7();
        _sessions.GetManagedSessionByIdAsync(_eventId, sessionId).Returns(new EventSessionDto
        {
            Id = sessionId,
            EventId = _eventId,
            Title = "Managed child",
            StartTime = new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero),
            EndTime = new DateTimeOffset(2027, 1, 2, 10, 0, 0, TimeSpan.Zero),
            AdditionalProperties = new Dictionary<string, object>
            {
                ["_links"] = JsonSerializer.SerializeToElement(
                new { edit = new { href = $"/api/eventsession/{sessionId}", method = "PATCH" } })
            }
        });
        var route = $"/studio/events/{_eventId}/sessions/{sessionId}/edit";
        var router = await OpenRouteAsync(route, "form");
        await Assert.That(router.FindComponent<EditSession>().Instance.SessionId).IsEqualTo(sessionId);
        await Assert.That(router.FindComponent<EditSession>().Find("input").GetAttribute("value")).IsEqualTo("Managed child");
        await _ctx.DisposeComponentsAsync();
        await _navigation.NavigateAsync("/");
        var reloaded = await OpenRouteAsync(route, "form");
        await Assert.That(reloaded.FindComponent<EditSession>().Instance.EventId).IsEqualTo(_eventId);
        await Assert.That(reloaded.FindComponent<EditSession>().Find("input").GetAttribute("value")).IsEqualTo("Managed child");
    }

    [Test]
    [Arguments("details")]
    [Arguments("sessions/create")]
    [Arguments("sessions/edit")]
    public async Task DirectEditor_WithoutItsCurrentRelation_ExposesNoForm(string section)
    {
        var sessionId = Guid.CreateVersion7();
        _events.GetEventByIdAsync(_eventId).Returns(Resource());
        _sessions.GetManagedSessionByIdAsync(_eventId, sessionId).Returns(new EventSessionDto
        {
            Id = sessionId,
            EventId = _eventId,
            Title = "Read-only child"
        });
        var suffix = section == "sessions/edit" ? $"sessions/{sessionId}/edit" : section;
        var router = await OpenRouteAsync($"/studio/events/{_eventId}/{suffix}", "a[href='/studio/events'],button");

        await Assert.That(router.FindAll("form")).IsEmpty();
        await Assert.That(router.FindAll("input")).IsEmpty();
        await Assert.That(router.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    public async Task ConfirmedSessionSaveWithNavigationFailure_RetainsWorkspaceLinkAndPreventsRepeatCreate()
    {
        var requests = new List<CreateEventSessionDto>();
        _sessions.CreateSessionAsync(Arg.Any<CreateEventSessionDto>()).Returns(call =>
        {
            requests.Add(call.ArgAt<CreateEventSessionDto>(0));
            return new BaseCommandResponseOfGuid { Success = true, Id = Guid.CreateVersion7() };
        });
        var router = await OpenSessionAsync();
        var component = router.FindComponent<CreateSession>();
        await component.Find("input").ChangeAsync(new ChangeEventArgs { Value = "Saved once" });
        await component.InvokeAsync(() => component.FindComponent<EventSessionForm>().Instance.EndDateChanged
            .InvokeAsync(new DateTime(2027, 1, 2)));
        _navigation.FailNextNavigation = true;
        await component.Find("form").SubmitAsync();
        await Assert.That(component.FindComponent<EditForm>().Instance.EditContext!.GetValidationMessages().ToArray()).IsEmpty();

        await Assert.That(component.Find("[data-testid='saved-session-workspace']").GetAttribute("href"))
            .IsEqualTo($"/studio/events/{_eventId}/schedule");
        await Assert.That(component.Find("[data-testid='saved-session-workspace']").Closest("[role='status']"))
            .IsNotNull();
        await component.Find("form").SubmitAsync();
        await Assert.That(requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task AllEventsReturn_PreservesExistingActorAndManagedList()
    {
        var actor = new ManagedActorDto { ActorId = Guid.CreateVersion7(), ActorType = "Organization", DisplayName = "Publisher" };
        _ctx.Services.GetRequiredService<IUiShellContextService>()
            .GetCachedContextAsync(Arg.Any<CancellationToken>()).Returns(new UiShellContextDto { ManagedActors = [actor], PinnedActorId = actor.ActorId });
        _ctx.Services.GetRequiredService<UiShellState>().ReconcileActiveActors([actor], actor.ActorId);
        _events.GetManagedEventsByActorAsync(actor.ActorId!.Value, 1, 100, Arg.Any<CancellationToken>())
            .Returns(new PaginatedResult<EventListDto> { Items = [new EventListDto { Id = _eventId, Title = "Actor program" }], TotalCount = 1, PageNumber = 1, PageSize = 100 });
        _events.GetEventByIdAsync(_eventId).Returns((EventDto?)null);
        var router = await OpenRouteAsync($"/studio/events/{_eventId}", "a[href='/studio/events']");
        var list = NextRender(router, () => router.FindAll("[data-testid='studio-event-list']").Count == 1);
        await router.InvokeAsync(() => _navigation.NavigateAsync(router.Find("a[href='/studio/events']").GetAttribute("href")!));
        await list;

        await Assert.That(_ctx.Services.GetRequiredService<UiShellState>().ActiveActorId).IsEqualTo(actor.ActorId);
        await Assert.That(router.FindAll("[data-testid='studio-event-list']").Count).IsEqualTo(1);
    }

    private async Task<IRenderedComponent<Blazouter.Components.Router>> OpenRouteAsync(string route, string selector)
    {
        var configurations = ActualRoutes()
            .Where(entry => entry.Path.StartsWith("/studio/events", StringComparison.Ordinal) || entry.Path == "/events/create")
            .Select(entry => new RouteConfig { Path = entry.Path, Component = entry.Component, EnableCache = false, Transition = RouteTransition.None })
            .ToList();
        var router = _ctx.RenderMudComponent<Blazouter.Components.Router>(parameters =>
            parameters.Add(component => component.Routes, configurations));
        var entered = NextRender(router, () => router.FindAll(selector).Count > 0);
        await router.InvokeAsync(() => _navigation.NavigateAsync(route));
        await entered;
        return router;
    }

    private static IReadOnlyList<RouteConfig> ActualRoutes()
    {
        using var context = new BlazorTestContext();
        context.ComponentFactories.AddStub<TenantContextProvider>(
            parameters => builder => builder.AddContent(0, parameters.Get(component => component.ChildContent)));
        context.ComponentFactories.AddStub<LanguageProvider>(
            parameters => builder => builder.AddContent(0, parameters.Get(component => component.ChildContent)));
        context.ComponentFactories.AddStub<Blazouter.Components.Router>();
        var routes = context.Render<Explore.Blazor.Client.Routes>().FindComponent<Stub<Blazouter.Components.Router>>()
            .Instance.Parameters.Get(router => router.Routes);
        return routes.ToArray();
    }

    private static Task NextRender<T>(IRenderedComponent<T> component, Func<bool> matches) where T : IComponent
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        component.OnMarkupUpdated += (_, _) => { if (matches()) signal.TrySetResult(); };
        return signal.Task.WaitAsync(Timeout);
    }

    private EventProgramSummaryDto Program(string title) => new()
    {
        EventId = _eventId,
        Sections = [new EventProgramSectionDto { SectionKey = "unassigned", Title = "Program",
            SessionGroups = [new EventProgramSessionGroupSectionDto { Title = "Sessions",
                Days = [new EventProgramDayGroupDto { Items = [new EventProgramItemDto { SessionId = Guid.CreateVersion7(), Title = title }] }] }] }]
    };

    private EventDto Resource(params string[] relations) => new()
    {
        Id = _eventId,
        TenantId = _tenantId,
        Title = "Managed event",
        EventStatusFullName = "Draft",
        Timezone = "UTC",
        AdditionalProperties = new Dictionary<string, object>
        {
            ["_links"] = JsonSerializer.SerializeToElement(relations.ToDictionary(
                relation => relation,
                relation => new { href = $"/api/events/{_eventId}/{relation}", method = "GET" }))
        }
    };

    // The real router receives NavigationManager events. Await its native pre-navigation
    // hook directly rather than polling bUnit's asynchronous NavigateTo history.
    private sealed class RoutedNavigation : NavigationManager
    {
        public RoutedNavigation() => Initialize("http://localhost/", "http://localhost/");
        public Task Pending { get; private set; } = Task.CompletedTask;
        public bool FailNextNavigation { get; set; }

        protected override void NavigateToCore(string uri, bool forceLoad) => StartNavigation(uri);

        protected override void NavigateToCore(string uri, NavigationOptions options) => StartNavigation(uri);

        private void StartNavigation(string uri)
        {
            if (FailNextNavigation)
            {
                FailNextNavigation = false;
                throw new InvalidOperationException("Navigation unavailable");
            }
            Pending = NavigateAsync(uri);
        }

        protected override void SetNavigationLockState(bool value) { }

        protected override void HandleLocationChangingHandlerException(Exception exception, LocationChangingContext context)
            => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();

        public async Task NavigateAsync(string uri)
        {
            var destination = ToAbsoluteUri(uri).AbsoluteUri;
            if (await NotifyLocationChangingAsync(destination, null, false))
            {
                Uri = destination;
                NotifyLocationChanged(false);
            }
        }
    }
}
