using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Tests.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class AnnouncementBarStateTests
{
    [Test]
    public async Task AppliedDismissalHidesTheBarAndUpdatesHeaderVisibility()
    {
        await using var harness = new AnnouncementHarness();
        var cut = harness.Render();

        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
        await Assert.That(harness.Visibility.Last()).IsFalse();
    }

    [Test]
    [Arguments("null")]
    [Arguments("unknown-decision")]
    [Arguments("wrong-edition")]
    [Arguments("missing-marker")]
    [Arguments("wrong-owner")]
    [Arguments("missing-observation")]
    [Arguments("unsettled-read")]
    public async Task UnconfirmedDismissalRemainsVisibleWithAnHonestFailureStatus(string outcome)
    {
        await using var harness = new AnnouncementHarness();
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var result = harness.Saved();
                switch (outcome)
                {
                    case "null": return Task.FromResult<HalResourceOfAnnouncementDismissalReceiptDto>(null!);
                    case "unknown-decision": result.Decision = 99; break;
                    case "wrong-edition": result.Marker!.Observation.Edition = AnnouncementTestData.OtherEdition; break;
                    case "missing-marker": result.Marker = null; break;
                    case "wrong-owner": result.Marker!.Observation.Owner.TenantId = AnnouncementTestData.Tenant; break;
                    case "missing-observation": result.Marker!.Observation = null!; break;
                    case "unsettled-read": harness.ClearMarker(); break;
                    default: throw new ArgumentOutOfRangeException(nameof(outcome));
                }
                return Task.FromResult(result);
            });
        var cut = harness.Render();

        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(1);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task CurrentEditionDismissalIsReadAfterInteractiveRendering()
    {
        await using var harness = new AnnouncementHarness();
        harness.StoreCurrentMarker();

        var cut = harness.Render();

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitReconciliationAndRetryCanSaveAfterAFailedDismissal()
    {
        await using var harness = new AnnouncementHarness();
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<HalResourceOfAnnouncementDismissalReceiptDto>(null!),
                _ => Task.FromResult(harness.Saved()));
        var cut = harness.Render();
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await cut.InvokeAsync(() => cut.FindComponent<MudButton>().Instance.OnClick.InvokeAsync());
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
        await Assert.That(harness.Visibility.Last()).IsFalse();
    }

    [Test]
    public async Task EditionChangeIgnoresThePreviousDismissalSaveCallback()
    {
        await using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HoldDismissal(requested, saved);
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Settings = AnnouncementTestData.Settings(edition: AnnouncementTestData.OtherEdition, message: "New announcement");
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        saved.SetResult(harness.Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponent<MudAlert>().Instance.ShowCloseIcon).IsTrue();
        await Assert.That(cut.Find(".announcement-bar__text").TextContent.Trim())
            .IsEqualTo(harness.Settings.Announcement!.Material!.Message);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task SamePublicationRefreshOffersReconciliationAfterPendingDismissalSettles()
    {
        await using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HoldDismissal(requested, saved);
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Settings = AnnouncementTestData.Settings(epoch: AnnouncementTestData.OtherEpoch);
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        saved.SetResult(harness.Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponent<MudAlert>().Instance.ShowCloseIcon).IsFalse();
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponent<MudButton>().Instance.Disabled).IsFalse();
    }

    [Test]
    public async Task TenantChangeIgnoresThePreviousDismissalSaveCallback()
    {
        await using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HoldDismissal(requested, saved);
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Settings = AnnouncementTestData.Settings(tenant: AnnouncementTestData.OtherTenant);
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        saved.SetResult(harness.Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".announcement-bar__text").TextContent.Trim())
            .IsEqualTo(harness.Settings.Announcement!.Material!.Message);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task ReplacementSettingsBlockDismissalUntilTheirPrivateStateIsLoaded()
    {
        await using var harness = new AnnouncementHarness();
        var cut = harness.Render();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new TaskCompletionSource<HalResourceOfAnnouncementSelfStateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Api.GetMyAnnouncementDismissalAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { requested.TrySetResult(); return preferences.Task; },
                _ => Task.FromResult(harness.CurrentSelf()));
        Guid? savedEdition = null;
        harness.Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                savedEdition = call.Arg<AnnouncementDismissRequestDto>().Observed.Edition;
                return harness.Saved();
            });

        harness.Settings = AnnouncementTestData.Settings(edition: AnnouncementTestData.OtherEdition);
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await Assert.That(savedEdition).IsNull();
        Task loaded = OnNextMarkup(cut);
        preferences.SetResult(harness.CurrentSelf());
        await loaded.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(savedEdition).IsEqualTo(AnnouncementTestData.OtherEdition);
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
    }

    [Test]
    public async Task AccountChangeIgnoresThePreviousDismissalSaveCallback()
    {
        await using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HoldDismissal(requested, saved);
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cut.InvokeAsync(harness.Authentication.ChangeAccount);
        saved.SetResult(harness.Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task NewSettingsCannotBeOverwrittenByAnOlderPrivateStateRead()
    {
        await using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new TaskCompletionSource<HalResourceOfAnnouncementSelfStateDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = AnnouncementTestData.Self(harness.Settings, new() { Observation = AnnouncementTestData.Observation() });
        harness.Api.GetMyAnnouncementDismissalAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => { requested.TrySetResult(); return preferences.Task; },
                _ => Task.FromResult(harness.CurrentSelf()));
        var cut = harness.Render();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var visible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.BecameVisible = visible;

        harness.Settings = AnnouncementTestData.Settings(edition: AnnouncementTestData.OtherEdition, message: "New announcement");
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        await visible.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task rendered = OnNextMarkup(cut);
        preferences.SetResult(old);
        await rendered.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task DisposedBarIgnoresAnOutstandingDismissalResult()
    {
        await using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HoldDismissal(requested, saved);
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cut.InvokeAsync(cut.Instance.Dispose);
        saved.SetResult(harness.Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task ProductionSettingsRefreshUpdatesTheBarAndInvalidatesPendingDismissal()
    {
        await using var harness = new AnnouncementHarness(production: true);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.HoldDismissal(requested, saved);
        var cut = harness.Render();
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Settings = AnnouncementTestData.Settings(edition: AnnouncementTestData.OtherEdition, message: "Replacement announcement");
        Task refreshed = OnNextMarkup(cut, () => cut.FindAll(".announcement-bar__text")
            .SingleOrDefault()?.TextContent.Trim() == harness.Settings.Announcement!.Material!.Message);
        await cut.InvokeAsync(async () => { await harness.Experience.GetSettingsAsync(); });
        await refreshed.WaitAsync(TimeSpan.FromSeconds(10));
        saved.SetResult(harness.Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(harness.Written?.Edition).IsEqualTo(AnnouncementTestData.Edition);
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".announcement-bar__text").TextContent.Trim())
            .IsEqualTo(harness.Settings.Announcement!.Material!.Message);
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProductionUnavailableTransitionHidesTheBarWithoutARefetchLoop(bool missingIdentity)
    {
        await using var harness = new AnnouncementHarness(production: true);
        var cut = harness.Render();
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        Task hidden = OnNextMarkup(cut, () => cut.FindAll(".announcement-bar").Count == 0);

        harness.Unavailable = true;
        harness.MissingIdentity = missingIdentity;
        await cut.InvokeAsync(async () => { await harness.Experience.GetSettingsAsync(); });
        await hidden.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(harness.Experience.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Unavailable);
        await Assert.That(harness.PublicReads).IsEqualTo(2);
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
    }

    private static Task OnNextMarkup(IRenderedComponent<AnnouncementBar> cut, Func<bool>? condition = null)
    {
        var rendered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (condition is not null && !condition())
                return;
            cut.OnMarkupUpdated -= handler;
            rendered.TrySetResult();
        };
        cut.OnMarkupUpdated += handler;
        return rendered.Task;
    }

    private sealed class AnnouncementHarness : IAsyncDisposable
    {
        private readonly BlazorTestContext _context = new();
        private readonly AnnouncementClientService _service;
        private AnnouncementDismissalMarkerDto? _marker;
        private int _markerAccount;
        private int _writeAccount;
        internal IPublicExperienceService Experience { get; }
        internal IAnnouncementsClient Api { get; } = Substitute.For<IAnnouncementsClient>();
        internal ChangingAuthenticationState Authentication { get; } = new();
        internal List<bool> Visibility { get; } = [];
        internal TaskCompletionSource? BecameVisible { get; set; }
        internal PublicExperienceSettingsDto Settings { get; set; } = AnnouncementTestData.Settings();
        internal AnnouncementObservationDto? Written { get; private set; }
        internal bool Unavailable { get; set; }
        internal bool MissingIdentity { get; set; }
        internal int PublicReads { get; private set; }

        internal AnnouncementHarness(bool production = false)
        {
            if (production)
            {
                var publicApi = Substitute.For<IPublicExperienceClient>();
                publicApi.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                    .Returns(_ =>
                    {
                        PublicReads++;
                        if (Unavailable && !MissingIdentity)
                            return Task.FromException<PublicExperienceSettingsDto>(new HttpRequestException("Unavailable"));
                        return Task.FromResult(Unavailable ? Settings with { DirectoryOperator = null } : Settings);
                    });
                Experience = new PublicExperienceService(publicApi, Substitute.For<ILogger<PublicExperienceService>>());
            }
            else
            {
                Experience = Substitute.For<IPublicExperienceService>();
                Experience.GetCachedSettingsAsync().Returns(_ => Task.FromResult<PublicExperienceSettingsDto?>(Settings));
            }
            Api.GetMyAnnouncementDismissalAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => CurrentSelf());
            Api.When(api => api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()))
                .Do(call =>
                {
                    Written = call.Arg<AnnouncementDismissRequestDto>().Observed;
                    _writeAccount = Authentication.Version;
                });
            Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Saved());
            _service = new(Api, Experience, Substitute.For<IUserSettingsService>(), Authentication,
                Substitute.For<ILogger<AnnouncementClientService>>());
            _context.Services.AddSingleton(Experience);
            _context.Services.AddSingleton<IAnnouncementClientService>(_service);
            _context.Services.AddSingleton<AuthenticationStateProvider>(Authentication);
        }

        internal HalResourceOfAnnouncementSelfStateDto CurrentSelf() => AnnouncementTestData.Self(Settings,
            _markerAccount == Authentication.Version && _marker?.Observation?.TenantId == Settings.Announcement!.TenantId ? _marker : null);

        internal HalResourceOfAnnouncementDismissalReceiptDto Saved()
        {
            _marker = new() { Observation = Written ?? Settings.Announcement!.Observation! };
            _markerAccount = _writeAccount;
            return new() { Decision = 0, Marker = _marker, _links = new Dictionary<string, HalLink>() };
        }

        internal void StoreCurrentMarker()
        {
            _marker = new() { Observation = Settings.Announcement!.Observation! };
            _markerAccount = Authentication.Version;
        }

        internal void ClearMarker() => _marker = null;

        internal void HoldDismissal(TaskCompletionSource requested,
            TaskCompletionSource<HalResourceOfAnnouncementDismissalReceiptDto> saved) =>
            Api.DismissAnnouncementAsync(Arg.Any<AnnouncementDismissRequestDto>(),
                    Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(_ => { requested.TrySetResult(); return saved.Task; });

        internal IRenderedComponent<AnnouncementBar> Render() =>
            _context.RenderMudComponent<AnnouncementBar>(parameters => parameters
                .Add(component => component.OnVisibilityChanged, (bool visible) =>
                {
                    Visibility.Add(visible);
                    if (visible)
                        BecameVisible?.TrySetResult();
                }));

        internal void NotifySettingsChanged() => Experience.SettingsChanged += Raise.Event<Action>();

        public async ValueTask DisposeAsync()
        {
            await _service.DisposeAsync();
            _context.Dispose();
        }
    }

    private sealed class ChangingAuthenticationState : AuthenticationStateProvider
    {
        private AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, AnnouncementTestData.Tenant.ToString("D"))], "TestAuth")));
        internal int Version { get; private set; }
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);
        internal void ChangeAccount()
        {
            Version++;
            _state = new(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, AnnouncementTestData.OtherTenant.ToString("D"))], "TestAuth")));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }
    }
}
