using Explore.Blazor.Client.Layout;
using Microsoft.JSInterop;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Learning;

public sealed class AnnouncementBarStateTests
{
    [Test]
    public async Task AppliedDismissalHidesTheBarAndUpdatesHeaderVisibility()
    {
        using var harness = new AnnouncementHarness();
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Saved());
        var cut = harness.Render();

        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
        await Assert.That(harness.Visibility.Last()).IsFalse();
    }

    [Test]
    [Arguments("null")]
    [Arguments("failed-batch")]
    [Arguments("unapplied-key")]
    [Arguments("missing-key")]
    [Arguments("wrong-key")]
    [Arguments("missing-results")]
    [Arguments("duplicate-key")]
    public async Task UnconfirmedDismissalRemainsVisibleWithAnHonestFailureStatus(string outcome)
    {
        using var harness = new AnnouncementHarness();
        BatchUpdateResponseDto? result = outcome switch
        {
            "null" => null,
            "failed-batch" => Saved() with { Success = false },
            "unapplied-key" => new() { Success = true, Results = [new() { Key = DismissalKey, Applied = false }] },
            "missing-key" => new() { Success = true, Results = [] },
            "wrong-key" => new() { Success = true, Results = [new() { Key = "event_list.page_size", Applied = true }] },
            "missing-results" => new() { Success = true, Results = null! },
            "duplicate-key" => new() { Success = true, Results =
                [new() { Key = DismissalKey, Applied = true }, new() { Key = DismissalKey, Applied = false }] },
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(result));
        var cut = harness.Render();

        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(1);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task CurrentRevisionDismissalIsReadAfterInteractiveRendering()
    {
        using var harness = new AnnouncementHarness();
        harness.Preferences.GetSettingsAsync(PreferenceCategory, Arg.Any<CancellationToken>())
            .Returns(Preference("4"));

        var cut = harness.Render();

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
    }

    [Test]
    public async Task ExplicitRetryCanSaveAfterAFailedDismissal()
    {
        using var harness = new AnnouncementHarness();
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BatchUpdateResponseDto?>(null), Task.FromResult<BatchUpdateResponseDto?>(Saved()));
        var cut = harness.Render();
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
        await Assert.That(harness.Visibility.Last()).IsFalse();
    }

    [Test]
    public async Task RevisionChangeIgnoresThePreviousDismissalSaveCallback()
    {
        using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<BatchUpdateResponseDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return saved.Task;
            });
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Settings = harness.Settings with { AnnouncementBarRevision = 5, AnnouncementBarMessage = "New announcement" };
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        saved.SetResult(Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.FindComponent<MudAlert>().Instance.ShowCloseIcon).IsTrue();
        await Assert.That(cut.Find(".announcement-bar__text").TextContent.Trim())
            .IsEqualTo(harness.Settings.AnnouncementBarMessage);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task TenantChangeIgnoresThePreviousDismissalSaveCallback()
    {
        using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<BatchUpdateResponseDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return saved.Task;
            });
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        harness.Settings = harness.Settings with { TenantId = Guid.CreateVersion7() };
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        saved.SetResult(Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".announcement-bar__text").TextContent.Trim())
            .IsEqualTo(harness.Settings.AnnouncementBarMessage);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task ReplacementSettingsBlockDismissalUntilTheirPreferencesAreLoaded()
    {
        using var harness = new AnnouncementHarness();
        var cut = harness.Render();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new TaskCompletionSource<SettingGroupResponseDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Preferences.GetSettingsAsync(PreferenceCategory, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return preferences.Task;
            });
        string? savedRevision = null;
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                savedRevision = call.Arg<IDictionary<string, string>>()[DismissalKey];
                return Saved();
            });

        harness.Settings = harness.Settings with { AnnouncementBarRevision = 5 };
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await Assert.That(savedRevision).IsNull();
        Task loaded = OnNextMarkup(cut);
        preferences.SetResult(null);
        await loaded.WaitAsync(TimeSpan.FromSeconds(10));
        await cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());

        await Assert.That(savedRevision).IsEqualTo("5");
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(0);
    }

    [Test]
    public async Task AccountChangeIgnoresThePreviousDismissalSaveCallback()
    {
        using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<BatchUpdateResponseDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return saved.Task;
            });
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cut.InvokeAsync(harness.Authentication.ChangeAccount);
        saved.SetResult(Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task NewSettingsCannotBeOverwrittenByAnOlderPreferenceRead()
    {
        using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = new TaskCompletionSource<SettingGroupResponseDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Preferences.GetSettingsAsync(PreferenceCategory, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return preferences.Task;
            }, _ => Task.FromResult<SettingGroupResponseDto?>(null));
        var cut = harness.Render();
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var visible = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.BecameVisible = visible;

        harness.Settings = harness.Settings with { AnnouncementBarRevision = 5, AnnouncementBarMessage = "New announcement" };
        await cut.InvokeAsync(harness.NotifySettingsChanged);
        await visible.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task rendered = OnNextMarkup(cut);
        preferences.SetResult(Preference("99"));
        await rendered.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task DisposedBarIgnoresAnOutstandingDismissalResult()
    {
        using var harness = new AnnouncementHarness();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<BatchUpdateResponseDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Preferences.UpdateSettingsBatchAsync(PreferenceCategory,
                Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                requested.TrySetResult();
                return saved.Task;
            });
        var cut = harness.Render();
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await cut.InvokeAsync(cut.Instance.Dispose);
        saved.SetResult(Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(harness.Visibility.Last()).IsTrue();
    }

    [Test]
    public async Task ProductionSettingsRefreshUpdatesTheBarAndInvalidatesPendingDismissal()
    {
        using var context = new BlazorTestContext();
        var publicApi = Substitute.For<IPublicExperienceClient>();
        var current = new PublicExperienceSettingsDto
        {
            TenantId = Guid.CreateVersion7(), AnnouncementBarEnabled = true,
            AnnouncementBarRevision = 4, AnnouncementBarMessage = "Original announcement",
            DirectoryOperator = new TenantDirectoryOperatorPublicDto
                { PublicName = "Directory", LegalName = "Directory legal name" }
        };
        publicApi.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(current));
        var experience = new PublicExperienceService(publicApi, Substitute.For<ILogger<PublicExperienceService>>());
        var authentication = new ChangingAuthenticationState();
        authentication.ChangeAccount();
        var api = Substitute.For<ISettingsClient>();
        api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(Preference("0"));
        await using var preferences = new UserSettingsService(api, authentication,
            Substitute.For<IJSRuntime>(), Substitute.For<ILogger<UserSettingsService>>(), experience);
        context.Services.AddSingleton<IPublicExperienceService>(experience);
        context.Services.AddSingleton<IUserSettingsService>(preferences);
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new TaskCompletionSource<BatchUpdateResponseDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        string? savedRevision = null;
        api.UpdateUserSettingsBatchAsync(PreferenceCategory, Arg.Any<UpdateSettingBatchDto>(),
                Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                savedRevision = call.Arg<UpdateSettingBatchDto>().Values[DismissalKey];
                requested.TrySetResult();
                return saved.Task;
            });
        var cut = context.RenderMudComponent<AnnouncementBar>();
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        Task closing = cut.InvokeAsync(() => cut.FindComponent<MudAlert>().Instance.CloseIconClicked.InvokeAsync());
        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        current = current with { AnnouncementBarRevision = 5, AnnouncementBarMessage = "Replacement announcement" };
        Task refreshed = OnNextMarkup(cut, () => cut.FindAll(".announcement-bar__text")
            .SingleOrDefault()?.TextContent.Trim() == current.AnnouncementBarMessage);
        await cut.InvokeAsync(async () => { await experience.GetSettingsAsync(); });
        await refreshed.WaitAsync(TimeSpan.FromSeconds(10));
        saved.SetResult(Saved());
        await closing.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(savedRevision).IsEqualTo("4");
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        await Assert.That(cut.Find(".announcement-bar__text").TextContent.Trim())
            .IsEqualTo(current.AnnouncementBarMessage);
        await Assert.That(cut.FindAll("[role='status']").Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProductionUnavailableTransitionHidesTheBarWithoutARefetchLoop(bool missingIdentity)
    {
        using var context = new BlazorTestContext();
        var publicApi = Substitute.For<IPublicExperienceClient>();
        var current = new PublicExperienceSettingsDto
        {
            TenantId = Guid.CreateVersion7(), AnnouncementBarEnabled = true,
            AnnouncementBarRevision = 4, AnnouncementBarMessage = "Current announcement",
            DirectoryOperator = new TenantDirectoryOperatorPublicDto
                { PublicName = "Directory", LegalName = "Directory legal name" }
        };
        bool unavailable = false;
        int publicReads = 0;
        publicApi.GetPublicExperienceSettingsAsync(Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                publicReads++;
                if (unavailable && !missingIdentity)
                    return Task.FromException<PublicExperienceSettingsDto>(new HttpRequestException("Unavailable"));
                return Task.FromResult(unavailable ? current with { DirectoryOperator = null } : current);
            });
        var experience = new PublicExperienceService(publicApi, Substitute.For<ILogger<PublicExperienceService>>());
        var authentication = new ChangingAuthenticationState();
        authentication.ChangeAccount();
        var api = Substitute.For<ISettingsClient>();
        api.GetUserSettingsAsync(PreferenceCategory, Arg.Any<string?>(), Arg.Any<string?>(),
                Arg.Any<CancellationToken>()).Returns(Preference("0"));
        await using var preferences = new UserSettingsService(api, authentication,
            Substitute.For<IJSRuntime>(), Substitute.For<ILogger<UserSettingsService>>(), experience);
        context.Services.AddSingleton<IPublicExperienceService>(experience);
        context.Services.AddSingleton<IUserSettingsService>(preferences);
        context.Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = context.RenderMudComponent<AnnouncementBar>();
        await Assert.That(cut.FindAll(".announcement-bar").Count).IsEqualTo(1);
        Task hidden = OnNextMarkup(cut, () => cut.FindAll(".announcement-bar").Count == 0);

        unavailable = true;
        await cut.InvokeAsync(async () => { await experience.GetSettingsAsync(); });
        await hidden.WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.That(experience.SettingsAvailability).IsEqualTo(PublicExperienceAvailability.Unavailable);
        await Assert.That(publicReads).IsEqualTo(2);
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

    private const string PreferenceCategory = "PublicExperiencePreferences";
    private const string DismissalKey = "public_experience_preferences.announcement_bar.dismissed_revision";

    private static BatchUpdateResponseDto Saved() => new()
    {
        Success = true, Results = [new SettingUpdateResultDto { Key = DismissalKey, Applied = true }]
    };

    private static SettingGroupResponseDto Preference(string revision) => new()
    {
        Category = PreferenceCategory,
        Settings = [new EffectiveSettingDto { Key = DismissalKey, Value = revision }]
    };

    private sealed class AnnouncementHarness : IDisposable
    {
        private readonly BlazorTestContext _context = new();
        internal IPublicExperienceService Experience { get; } = Substitute.For<IPublicExperienceService>();
        internal IUserSettingsService Preferences { get; } = Substitute.For<IUserSettingsService>();
        internal ChangingAuthenticationState Authentication { get; } = new();
        internal List<bool> Visibility { get; } = [];
        internal TaskCompletionSource? BecameVisible { get; set; }
        internal PublicExperienceSettingsDto Settings { get; set; } = new()
        {
            TenantId = Guid.CreateVersion7(), AnnouncementBarEnabled = true,
            AnnouncementBarRevision = 4, AnnouncementBarMessage = "Announcement"
        };

        internal AnnouncementHarness()
        {
            Experience.GetCachedSettingsAsync().Returns(_ => Task.FromResult<PublicExperienceSettingsDto?>(Settings));
            _context.Services.AddSingleton(Experience);
            _context.Services.AddSingleton(Preferences);
            _context.Services.AddSingleton<AuthenticationStateProvider>(Authentication);
        }

        internal IRenderedComponent<AnnouncementBar> Render() =>
            _context.RenderMudComponent<AnnouncementBar>(parameters => parameters
                .Add(component => component.OnVisibilityChanged, (bool visible) =>
                {
                    Visibility.Add(visible);
                    if (visible)
                        BecameVisible?.TrySetResult();
                }));

        internal void NotifySettingsChanged() => Experience.SettingsChanged += Raise.Event<Action>();

        public void Dispose() => _context.Dispose();
    }

    private sealed class ChangingAuthenticationState : AuthenticationStateProvider
    {
        private AuthenticationState _state = new(new ClaimsPrincipal(new ClaimsIdentity()));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(_state);

        internal void ChangeAccount()
        {
            _state = new(new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString("D"))], "TestAuth")));
            NotifyAuthenticationStateChanged(Task.FromResult(_state));
        }
    }
}
