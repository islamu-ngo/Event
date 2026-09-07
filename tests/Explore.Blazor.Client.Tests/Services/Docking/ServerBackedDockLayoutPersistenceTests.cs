using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Interop;

namespace Explore.Blazor.Client.Tests.Services.Docking;

public sealed class ServerBackedDockLayoutPersistenceTests
{
    private readonly IDockLayoutPersistence _local = Substitute.For<IDockLayoutPersistence>();
    private readonly IUserSettingsService _settings = Substitute.For<IUserSettingsService>();
    private readonly AuthenticationStateProvider _auth =
        Substitute.For<AuthenticationStateProvider>();
    private readonly IUiShellContextService _shellContext = Substitute.For<IUiShellContextService>();
    private readonly ILogger<ServerBackedDockLayoutPersistence> _logger =
        Substitute.For<ILogger<ServerBackedDockLayoutPersistence>>();

    [Test]
    public async Task LoadAsync_AuthenticatedServerSnapshot_ReturnsServerWithoutLocalRead()
    {
        var snapshot = CreateSnapshot();
        SetAuthenticated(true);
        _settings.GetSettingsAsync(ServerBackedDockLayoutPersistence.PreferencesCategory, Arg.Any<CancellationToken>())
            .Returns(Settings((ServerBackedDockLayoutPersistence.LayoutPreferenceKey,
                LocalStorageDockLayoutPersistence.Serialize(snapshot))));

        var result = await CreatePersistence().LoadAsync("shell");

        await Assert.That(LocalStorageDockLayoutPersistence.Serialize(result!))
            .IsEqualTo(LocalStorageDockLayoutPersistence.Serialize(snapshot));
        await _local.DidNotReceive().LoadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAsync_AuthenticatedWithoutServerSnapshot_PromotesTenantLocalSnapshotOnce()
    {
        var snapshot = CreateSnapshot();
        SetAuthenticated(true);
        _settings.GetSettingsAsync(ServerBackedDockLayoutPersistence.PreferencesCategory, Arg.Any<CancellationToken>())
            .Returns(Settings((ServerBackedDockLayoutPersistence.LayoutPreferenceKey, "null")));
        _settings.UpdateSettingsBatchAsync(
                ServerBackedDockLayoutPersistence.PreferencesCategory,
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Applied(ServerBackedDockLayoutPersistence.LayoutPreferenceKey));
        _local.LoadAsync("shell", Arg.Any<CancellationToken>()).Returns(snapshot);

        var result = await CreatePersistence().LoadAsync("shell");

        await Assert.That(result).IsEqualTo(snapshot);
        await _settings.Received(1).UpdateSettingsBatchAsync(
            ServerBackedDockLayoutPersistence.PreferencesCategory,
            Arg.Is<IDictionary<string, string>>(values =>
                values.ContainsKey(ServerBackedDockLayoutPersistence.LayoutPreferenceKey)),
            Arg.Any<CancellationToken>());
        await _local.Received(1).DeleteAsync("shell", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveAsync_Anonymous_UsesOnlyTenantLocalPersistence()
    {
        var snapshot = CreateSnapshot();
        SetAuthenticated(false);
        _local.SaveAsync(snapshot, Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreatePersistence().SaveAsync(snapshot);

        await Assert.That(result).IsTrue();
        await _local.Received(1).SaveAsync(snapshot, Arg.Any<CancellationToken>());
        await _settings.DidNotReceive().UpdateSettingsBatchAsync(
            Arg.Any<string>(), Arg.Any<IDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveAsync_UserNavigationOverrideDisabled_OmitsWorkspaceNavigationPanel()
    {
        IDictionary<string, string>? captured = null;
        SetAuthenticated(true);
        _shellContext.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(new UiShellContextDto
            {
                NavigationDefaults = new UiShellNavigationDefaultsDto { AllowUserOverride = false }
            });
        _settings.UpdateSettingsBatchAsync(
                ServerBackedDockLayoutPersistence.PreferencesCategory,
                Arg.Do<IDictionary<string, string>>(values => captured = new Dictionary<string, string>(values)),
                Arg.Any<CancellationToken>())
            .Returns(Applied(ServerBackedDockLayoutPersistence.LayoutPreferenceKey));

        var result = await CreatePersistence().SaveAsync(CreateSnapshot());

        await Assert.That(result).IsTrue();
        var stored = LocalStorageDockLayoutPersistence.Deserialize(
            "shell",
            captured![ServerBackedDockLayoutPersistence.LayoutPreferenceKey],
            _logger);
        await Assert.That(stored).IsNotNull();
        await Assert.That(stored!.Panels.Any(panel => panel.Id == ShellDockPanels.WorkspaceNavId)).IsFalse();
        await Assert.That(stored.Panels.Any(panel => panel.Id == ShellDockPanels.AiAssistantId)).IsTrue();
    }

    private ServerBackedDockLayoutPersistence CreatePersistence() => new(
        _local,
        _settings,
        _auth,
        _shellContext,
        _logger);

    private void SetAuthenticated(bool authenticated)
    {
        var identity = authenticated
            ? new ClaimsIdentity(authenticationType: "TestAuth")
            : new ClaimsIdentity();
        _auth.GetAuthenticationStateAsync().Returns(
            new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private static DockLayoutSnapshot CreateSnapshot() => new(
        "shell",
        [
            new DockPanelState(ShellDockPanels.WorkspaceNavId, true, DockMode.Collapsed, 320, 10, true),
            new DockPanelState(ShellDockPanels.AiAssistantId, true, DockMode.Docked, 420, 20, true)
        ],
        TestTime.UtcNow);

    private static SettingGroupResponseDto Settings(params (string Key, string Value)[] values) => new()
    {
        Category = ServerBackedDockLayoutPersistence.PreferencesCategory,
        Settings = values.Select(value => new EffectiveSettingDto
        {
            Key = value.Key,
            Value = value.Value,
            SettingValueTypeCode = string.Empty,
            SettingValueTypeName = string.Empty
        }).ToList()
    };

    private static BatchUpdateResponseDto Applied(string key) => new()
    {
        Success = true,
        Results = [new SettingUpdateResultDto { Key = key, Applied = true }]
    };
}
