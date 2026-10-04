using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Blazouter.Models;
using Blazouter.Services;
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.EventReporting;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.Events.Components;
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task ObsoletePublicRouteLoad_CannotReplaceReturnedEvent(bool failObsoleteLoad)
    {
        var original = CreateEventDto("PUBLISHED", "Published", "edit");
        var related = CreateEventDto("PUBLISHED", "Published", "edit") with { Title = "Related offering" };
        RegisterEventDetailServices(original);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(_ =>
        {
            entered.TrySetResult();
            return delayed.Task;
        });
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var obsoleteLoad = GetField<Task>(cut.Instance, "_routeLoadTask");
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1"));
        await GetField<Task>(cut.Instance, "_routeLoadTask").WaitAsync(TimeSpan.FromSeconds(10));

        if (failObsoleteLoad)
            delayed.SetException(new ApiException("Discovery unavailable", 409, "",
                new Dictionary<string, IEnumerable<string>>(), null));
        else
            delayed.SetResult(related);
        await obsoleteLoad.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => { });

        await Assert.That(cut.FindAll($"a[href='/events/{original.Id}/edit']")).IsNotEmpty();
        await Assert.That(cut.FindAll($"a[href='/events/{related.Id}/edit']")).IsEmpty();
        await Assert.That(cut.FindAll("section[data-public-detail-status]")).IsEmpty();
    }

    [Test]
    [Arguments("sessions")]
    [Arguments("days")]
    [Arguments("aspects")]
    [Arguments("agenda")]
    public async Task ObsoleteDependentLoad_CannotPublishIntoReturnedEvent(string stage)
    {
        var original = CreateEventDto("PUBLISHED", "Published", "edit");
        var related = CreateEventDto("PUBLISHED", "Published", "edit") with
        {
            Title = "Related offering",
            AvailableAspects = ["Islamic"]
        };
        RegisterEventDetailServices(original);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task HoldAsync()
        {
            entered.TrySetResult();
            await released.Task;
        }
        if (stage == "sessions")
            _ctx.Services.GetRequiredService<IEventSessionService>()
                .GetSessionsByEventAsync(related.Id!.Value, Arg.Any<bool>()).Returns(async _ =>
                {
                    await HoldAsync();
                    return (ICollection<EventSessionListDto>)new List<EventSessionListDto> { new() { Id = Guid.NewGuid() } };
                });
        if (stage == "days")
            _ctx.Services.GetRequiredService<IEventDayService>()
                .GetDaysByEventAsync(related.Id!.Value, Arg.Any<bool>()).Returns(async _ =>
                {
                    await HoldAsync();
                    return (ICollection<EventDayListDto>)new List<EventDayListDto> { new() { Id = Guid.NewGuid() } };
                });
        if (stage == "aspects")
            _ctx.Services.GetRequiredService<IEventAspectService>()
                .GetIslamicAspectAsync(related.Id!.Value, Arg.Any<bool>()).Returns(async _ =>
                {
                    await HoldAsync();
                    return new EventIslamicAspectDto();
                });
        if (stage == "agenda")
            _ctx.Services.GetRequiredService<IEventAgendaItemService>()
                .GetAgendaItemsByEventAsync(related.Id!.Value).Returns(async _ =>
                {
                    await HoldAsync();
                    return (ICollection<EventAgendaItemListDto>)new List<EventAgendaItemListDto> { new() { Id = Guid.NewGuid() } };
                });
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(related);
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var obsoleteLoad = GetField<Task>(cut.Instance, "_routeLoadTask");
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1"));
        await GetField<Task>(cut.Instance, "_routeLoadTask").WaitAsync(TimeSpan.FromSeconds(10));
        released.SetResult();
        await obsoleteLoad.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => { });

        await Assert.That(cut.FindAll($"a[href='/events/{original.Id}/edit']")).IsNotEmpty();
        await Assert.That(cut.Instance.PersistedState!.EventId).IsEqualTo(original.Id!.Value);
        await Assert.That(cut.Instance.PersistedState.EventSessions).IsEmpty();
        await Assert.That(cut.Instance.PersistedState.EventDays).IsEmpty();
        await Assert.That(cut.Instance.PersistedState.IslamicAspect).IsNull();
        await Assert.That(GetField<ICollection<EventSessionListDto>>(cut.Instance, "_eventSessions")).IsEmpty();
        await Assert.That(GetField<ICollection<EventDayListDto>>(cut.Instance, "_eventDays")).IsEmpty();
        await Assert.That(GetField<ICollection<EventAgendaItemListDto>>(cut.Instance, "_eventAgendaItems")).IsEmpty();
        await Assert.That(typeof(EventDetail).GetField("_islamicAspect",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cut.Instance)).IsNull();
    }

    [Test]
    [Arguments("tags")]
    [Arguments("islamic")]
    [Arguments("tech")]
    [Arguments("agenda")]
    public async Task ObsoletePostEditRefresh_CannotPublishIntoNextEvent(string refresh)
    {
        var original = CreateEventDto("PUBLISHED", "Published", "edit");
        var related = CreateEventDto("PUBLISHED", "Published", "edit") with { Title = "Related offering" };
        RegisterEventDetailServices(original);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task HoldAsync()
        {
            entered.TrySetResult();
            await released.Task;
        }
        service.GetEventByIdAsync(original.Id!.Value).Returns(async _ =>
        {
            await HoldAsync();
            return original;
        });
        _ctx.Services.GetRequiredService<IEventAspectService>()
            .GetIslamicAspectAsync(original.Id.Value, true).Returns(async _ =>
            {
                await HoldAsync();
                return new EventIslamicAspectDto();
            });
        _ctx.Services.GetRequiredService<IEventAspectService>()
            .GetTechAspectAsync(original.Id.Value, true).Returns(async _ =>
            {
                await HoldAsync();
                return new EventTechAspectDto();
            });
        bool refreshing = false;
        _ctx.Services.GetRequiredService<IEventDayService>()
            .GetDaysByEventAsync(original.Id.Value, Arg.Any<bool>()).Returns(async _ =>
            {
                if (!refreshing)
                    return (ICollection<EventDayListDto>)new List<EventDayListDto>();
                await HoldAsync();
                return (ICollection<EventDayListDto>)new List<EventDayListDto> { new() { Id = Guid.NewGuid() } };
            });
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(related);
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        Task obsoleteRefresh = Task.CompletedTask;
        await cut.InvokeAsync(() =>
        {
            refreshing = true;
            if (refresh == "tags")
                typeof(EventDetail).GetMethod("OpenDetailTagManagement",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(cut.Instance, null);
            var methodName = refresh switch
            {
                "tags" => "HandleDetailTagCatSaved",
                "islamic" => "ReloadIslamicAspectAsync",
                "agenda" => "LoadEventAgendaAsync",
                _ => "ReloadTechAspectAsync"
            };
            var method = typeof(EventDetail).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
            obsoleteRefresh = (Task)method.Invoke(cut.Instance,
                refresh == "tags" ? [Array.Empty<Guid>(), GetField<long>(cut.Instance, "_loadGeneration")]
                : refresh == "agenda" ? [GetField<long>(cut.Instance, "_loadGeneration")] : null)!;
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));
        await GetField<Task>(cut.Instance, "_routeLoadTask").WaitAsync(TimeSpan.FromSeconds(10));
        released.SetResult();
        await obsoleteRefresh.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => { });

        await Assert.That(GetField<EventDto>(cut.Instance, "_eventDetails").Id).IsEqualTo(related.Id);
        await Assert.That(cut.FindAll("h1").Any(node => node.TextContent == related.Title)).IsTrue();
        await Assert.That(cut.FindAll($"a[href='/events/{related.Id}/edit']")).IsNotEmpty();
        await Assert.That(typeof(EventDetail).GetField("_islamicAspect",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cut.Instance)).IsNull();
        await Assert.That(typeof(EventDetail).GetField("_techAspect",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cut.Instance)).IsNull();
        await Assert.That(GetField<ICollection<EventDayListDto>>(cut.Instance, "_eventDays")).IsEmpty();
        await Assert.That(GetField<ICollection<EventAgendaItemListDto>>(cut.Instance, "_eventAgendaItems")).IsEmpty();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ObsoleteCancellation_CannotTargetNextEventOrInterruptItsLoad(bool holdConfirmation)
    {
        var original = CreateEventDto("PUBLISHED", "Published", "cancel");
        var related = CreateEventDto("PUBLISHED", "Published", "edit") with { Title = "Related offering" };
        RegisterEventDetailServices(original);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmation = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutation = new TaskCompletionSource<BaseCommandResponseOfGuid?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRead = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mutations = new List<(Guid EventId, Guid Stamp)>();
        var dialog = Substitute.For<IDialogService>();
        dialog.ShowMessageBoxAsync(Arg.Any<string>(), Arg.Any<string>(),
            yesText: Arg.Any<string>(), cancelText: Arg.Any<string>()).ReturnsForAnyArgs(_ =>
            {
                if (holdConfirmation)
                    entered.TrySetResult();
                return confirmation.Task;
            });
        service.CancelEventAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), default).Returns(call =>
        {
            mutations.Add((call.ArgAt<Guid>(0), call.ArgAt<Guid>(1)));
            entered.TrySetResult();
            return mutation.Task;
        });
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(_ =>
        {
            nextEntered.TrySetResult();
            return nextRead.Task;
        });
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        SetProperty(cut.Instance, "DialogService", dialog);
        SetProperty(cut.Instance, "AccessibilityFocusService", CreateFocusService());
        if (!holdConfirmation)
            confirmation.SetResult(true);
        Task oldAction = Task.CompletedTask;
        await cut.InvokeAsync(() => { oldAction = InvokePrivate<Task>(cut.Instance, "CancelEventAsync"); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));
        await nextEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var nextLoad = GetField<Task>(cut.Instance, "_routeLoadTask");
        if (holdConfirmation)
            confirmation.SetResult(true);
        else
            mutation.SetResult(new BaseCommandResponseOfGuid { Success = true });
        await oldAction.WaitAsync(TimeSpan.FromSeconds(10));
        nextRead.SetResult(related);
        await nextLoad.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => { });

        await Assert.That(mutations.Any(call => call.EventId != original.Id
            || call.Stamp != original.ConcurrencyStamp)).IsFalse();
        await Assert.That(mutations.Count).IsEqualTo(holdConfirmation ? 0 : 1);
        await Assert.That(GetField<EventDto>(cut.Instance, "_eventDetails").Id).IsEqualTo(related.Id);
        await Assert.That(cut.FindAll("h1").Any(node => node.TextContent == related.Title)).IsTrue();
        await Assert.That(cut.FindAll($"a[href='/events/{related.Id}/edit']")).IsNotEmpty();
    }

    [Test]
    public async Task ObsoleteReportAuthentication_CannotOpenDialogForNextEvent()
    {
        var original = CreateEventDto("PUBLISHED", "Published", "report-event");
        var related = CreateEventDto("PUBLISHED", "Published", "edit");
        RegisterEventDetailServices(original);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(related);
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = new TaskCompletionSource<AuthenticationState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var auth = Substitute.For<AuthenticationStateProvider>();
        bool firstRead = true;
        auth.GetAuthenticationStateAsync().Returns(_ =>
        {
            if (!firstRead)
                return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
            firstRead = false;
            entered.TrySetResult();
            return delayed.Task;
        });
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowAsync<ReportEventDialog>(Arg.Any<string>(), Arg.Any<DialogParameters>(),
            Arg.Any<DialogOptions>()).Returns(_ => Task.FromException<IDialogReference>(
                new InvalidOperationException("Obsolete report dialog opened")));
        SetProperty(cut.Instance, "AuthStateProvider", auth);
        SetProperty(cut.Instance, "DialogService", dialogs);
        SetField(cut.Instance, "_canReport", true);
        Task oldReport = Task.CompletedTask;
        await cut.InvokeAsync(() => { oldReport = InvokePrivate<Task>(cut.Instance, "OpenReportEventDialogAsync"); });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));
        await GetField<Task>(cut.Instance, "_routeLoadTask").WaitAsync(TimeSpan.FromSeconds(10));
        delayed.SetResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "TestAuth"))));
        await oldReport.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(GetField<bool>(cut.Instance, "_isAuthenticated")).IsFalse();
        await Assert.That(GetField<EventDto>(cut.Instance, "_eventDetails").Id).IsEqualTo(related.Id);
    }

    [Test]
    public async Task OldRenderedAgendaCallback_CannotBorrowPendingRouteGeneration()
    {
        var original = CreateEventDto("PUBLISHED", "Published", "edit");
        var related = CreateEventDto("PUBLISHED", "Published", "edit");
        var originalDay = new EventDayListDto { Id = Guid.NewGuid() };
        var relatedDay = new EventDayListDto { Id = Guid.NewGuid() };
        RegisterEventDetailServices(original, days: [originalDay],
            eventAgendaItems: [new EventAgendaItemListDto { Id = Guid.NewGuid() }]);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var days = _ctx.Services.GetRequiredService<IEventDayService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        var nextEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextRead = new TaskCompletionSource<EventDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldDays = new TaskCompletionSource<ICollection<EventDayListDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool oldCallbackInvoked = false;
        days.GetDaysByEventAsync(original.Id!.Value, Arg.Any<bool>()).Returns(_ =>
            oldCallbackInvoked ? oldDays.Task : Task.FromResult<ICollection<EventDayListDto>>([originalDay]));
        days.GetDaysByEventAsync(related.Id!.Value, Arg.Any<bool>())
            .Returns(Task.FromResult<ICollection<EventDayListDto>>([relatedDay]));
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(_ =>
        {
            nextEntered.TrySetResult();
            return nextRead.Task;
        });
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        var oldCallback = cut.FindComponent<AgendaMillerColumns>().Instance.OnDataChanged;
        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));
        await nextEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var nextLoad = GetField<Task>(cut.Instance, "_routeLoadTask");
        oldCallbackInvoked = true;
        Task oldRefresh = Task.CompletedTask;
        await cut.InvokeAsync(() => { oldRefresh = oldCallback.InvokeAsync(); });
        nextRead.SetResult(related);
        await nextLoad.WaitAsync(TimeSpan.FromSeconds(10));
        oldDays.SetResult([originalDay]);
        await oldRefresh.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(GetField<ICollection<EventDayListDto>>(cut.Instance, "_eventDays")
            .Select(day => day.Id).ToArray()).IsEquivalentTo([relatedDay.Id]);
        await Assert.That(GetField<EventDto>(cut.Instance, "_eventDetails").Id).IsEqualTo(related.Id);
    }

    [Test]
    public async Task PublicRouteChange_ReplacesOriginalEventActionIdentifiers()
    {
        var original = CreateEventDto("PUBLISHED", "Published", "edit");
        var related = CreateEventDto("PUBLISHED", "Published", "edit") with { Title = "Related offering" };
        RegisterEventDetailServices(original);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(original);
        service.GetEventBySlugCodeAsync("related-CODE2").Returns(related);
        RouteMatch Match(string slugCode) => new()
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = $"/events/{slugCode}",
            Params = new Dictionary<string, string> { ["slugCode"] = slugCode }
        };
        router.SetCurrentRoute(Match("original-CODE1"), "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        await Assert.That(cut.FindAll($"a[href='/events/{original.Id}/edit']")).IsNotEmpty();

        await cut.InvokeAsync(() => router.SetCurrentRoute(Match("related-CODE2"), "/events/related-CODE2"));

        await Assert.That(cut.FindAll($"a[href='/events/{related.Id}/edit']")).IsNotEmpty();
        await Assert.That(cut.FindAll($"a[href='/events/{original.Id}/edit']")).IsEmpty();
        await Assert.That(cut.FindAll("h1").Any(node => node.TextContent == related.Title)).IsTrue();

        await cut.InvokeAsync(() => router.SetCurrentRoute(new RouteMatch
        {
            Route = new RouteConfig { Path = "/" },
            MatchedPath = "/",
            Params = new Dictionary<string, string>()
        }, "/"));
        await Assert.That(cut.FindAll($"a[href='/events/{related.Id}/edit']")).IsNotEmpty();
    }

    [Test]
    public async Task ExplicitRecovery_RetainsPublicSlugWhenAmbientRouteIsCleared()
    {
        var current = CreateEventDto("PUBLISHED", "Published", "edit");
        RegisterEventDetailServices(current);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        var router = _ctx.Services.GetRequiredService<RouterStateService>();
        bool retryAllowed = false;
        service.GetEventByIdAsync(Arg.Any<Guid>()).Returns((EventDto?)null);
        service.GetEventBySlugCodeAsync("original-CODE1").Returns(_ => retryAllowed
            ? Task.FromResult<EventDto?>(current)
            : Task.FromException<EventDto?>(new ApiException(
                "Discovery unavailable", 409, "", new Dictionary<string, IEnumerable<string>>(), null)));
        router.SetCurrentRoute(new RouteMatch
        {
            Route = new RouteConfig { Path = "/events/:slugCode" },
            MatchedPath = "/events/original-CODE1",
            Params = new Dictionary<string, string> { ["slugCode"] = "original-CODE1" }
        }, "/events/original-CODE1");
        var cut = _ctx.RenderMudComponent<EventDetail>();
        var recovery = cut.Find("section[data-public-detail-status='409']");

        await cut.InvokeAsync(() => router.SetCurrentRoute(new RouteMatch
        {
            Route = new RouteConfig { Path = "/" },
            MatchedPath = "/",
            Params = new Dictionary<string, string>()
        }, "/"));
        retryAllowed = true;
        await recovery.QuerySelector("button")!.ClickAsync(new());

        await Assert.That(cut.FindAll("section[data-public-detail-status]")).IsEmpty();
        await Assert.That(cut.FindAll($"a[href='/events/{current.Id}/edit']")).IsNotEmpty();
    }

    [Test]
    [Arguments(409)]
    [Arguments(410)]
    [Arguments(503)]
    public async Task DiscoveryFailure_RendersRecoveryAndReloadsOnlyAfterExplicitAction(int status)
    {
        var current = CreateEventDto("PUBLISHED", "Published");
        RegisterEventDetailServices(current);
        var service = _ctx.Services.GetRequiredService<IEventService>();
        bool retryAllowed = false;
        service.GetEventByIdAsync(Arg.Any<Guid>()).Returns(_ => retryAllowed
            ? Task.FromResult<EventDto?>(current)
            : Task.FromException<EventDto?>(new ApiException(
                "Discovery unavailable", status, "", new Dictionary<string, IEnumerable<string>>(), null)));

        var cut = _ctx.RenderMudComponent<EventDetail>();

        var recovery = cut.Find($"section[data-public-detail-status='{status}']");
        await Assert.That(recovery.QuerySelectorAll("h1").Length).IsEqualTo(1);
        await Assert.That(cut.FindAll(".event-detail-wrapper").Count).IsEqualTo(0);
        retryAllowed = true;
        await recovery.QuerySelector("button")!.ClickAsync(new());

        await Assert.That(cut.FindAll("section[data-public-detail-status]").Count).IsEqualTo(0);
        await Assert.That(cut.FindAll("h1").Any(node => node.TextContent == current.Title)).IsTrue();
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
    [Arguments(false)]
    [Arguments(true)]
    public async Task Render_ResourcesSectionRequiresParentEventRelation(bool advertised)
    {
        var eventDto = CreateEventDto("PUBLISHED", "Published", advertised ? "resources" : "self");
        RegisterEventDetailServices(eventDto);
        var resources = _ctx.AddMockService<IEventResourceService>();
        resources.AudienceAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new EventResourceAudiencePageResource
            {
                _links = new Dictionary<string, HalLink>(),
                _embedded = new HalCollectionEmbeddedOfEventResourceAudienceDetailDto { Items = [] }
            });

        var cut = _ctx.RenderMudComponent<EventDetail>();

        if (advertised)
            cut.WaitForElement("#event-resources-title");
        else
        {
            cut.WaitForElement(".event-detail-wrapper");
            await Assert.That(cut.FindAll("#event-resources-title")).IsEmpty();
            await resources.DidNotReceive().AudienceAsync(Arg.Any<Guid>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>());
        }
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
