using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Services.Shell;

public sealed class ShellPreferencesServiceTests
{
    private readonly IUserSettingsService _settings = Substitute.For<IUserSettingsService>();
    private readonly AuthenticationStateProvider _auth =
        Substitute.For<AuthenticationStateProvider>();

    [Test]
    public async Task LoadAsync_ValidPersistedSelection_ReturnsAuthorizedWorkspaceActorAndScope()
    {
        var actorId = Guid.CreateVersion7();
        var context = Context(
            studioAvailable: true,
            actors: [new ManagedActorDto { ActorId = actorId, ActorType = "Organization" }],
            scopes: [new SettingsScopeDto { Scope = "Tenant" }]);
        SetAuthenticated();
        _settings.GetSettingsAsync(ShellPreferencesService.PreferencesCategory, Arg.Any<CancellationToken>())
            .Returns(Settings(
                (ShellPreferencesService.LastWorkspaceKey, "\"studio\""),
                (ShellPreferencesService.LastActorKey, $"\"{actorId}\""),
                (ShellPreferencesService.LastSettingsScopeKey, "\"tenant\"")));

        var result = await CreateService().LoadAsync(context);

        await Assert.That(result.LastWorkspace).IsEqualTo(WorkspaceKey.Studio.Value);
        await Assert.That(result.LastActorId).IsEqualTo(actorId);
        await Assert.That(result.LastSettingsScopeHref).IsEqualTo("/settings/admin");
    }

    [Test]
    public async Task LoadAsync_RevokedSelection_DropsValuesAndFallsBackToEventsAndPersonal()
    {
        SetAuthenticated();
        _settings.GetSettingsAsync(ShellPreferencesService.PreferencesCategory, Arg.Any<CancellationToken>())
            .Returns(Settings(
                (ShellPreferencesService.LastWorkspaceKey, "\"studio\""),
                (ShellPreferencesService.LastActorKey, $"\"{Guid.CreateVersion7()}\""),
                (ShellPreferencesService.LastSettingsScopeKey, "\"instance\"")));
        _settings.ResetSettingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await CreateService().LoadAsync(Context(studioAvailable: false));

        await Assert.That(result.LastWorkspace).IsEqualTo(WorkspaceKey.Events.Value);
        await Assert.That(result.LastActorId).IsNull();
        await Assert.That(result.LastSettingsScopeHref).IsEqualTo("/settings/personal");
        await _settings.Received(1).ResetSettingAsync(ShellPreferencesService.LastWorkspaceKey, Arg.Any<CancellationToken>());
        await _settings.Received(1).ResetSettingAsync(ShellPreferencesService.LastActorKey, Arg.Any<CancellationToken>());
        await _settings.Received(1).ResetSettingAsync(ShellPreferencesService.LastSettingsScopeKey, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SaveSelectionAsync_RepeatedSelection_WritesOneBatchAndNeverPersistsPersonalRoute()
    {
        var actorId = Guid.CreateVersion7();
        SetAuthenticated();
        _settings.UpdateSettingsBatchAsync(
                ShellPreferencesService.PreferencesCategory,
                Arg.Any<IDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new BatchUpdateResponseDto { Success = true, Results = [] });
        var service = CreateService();

        await service.SaveSelectionAsync(WorkspaceKey.Studio.Value, actorId, "/settings/personal/appearance");
        await service.SaveSelectionAsync(WorkspaceKey.Studio.Value, actorId, "/settings/personal/privacy");

        await _settings.Received(1).UpdateSettingsBatchAsync(
            ShellPreferencesService.PreferencesCategory,
            Arg.Is<IDictionary<string, string>>(values =>
                values[ShellPreferencesService.LastWorkspaceKey] == WorkspaceKey.Studio.Value
                && values[ShellPreferencesService.LastActorKey] == actorId.ToString()
                && !values.ContainsKey(ShellPreferencesService.LastSettingsScopeKey)),
            Arg.Any<CancellationToken>());
    }

    private ShellPreferencesService CreateService() => new(
        _settings,
        _auth,
        Substitute.For<ILogger<ShellPreferencesService>>());

    private void SetAuthenticated()
    {
        var identity = new ClaimsIdentity(authenticationType: "TestAuth");
        _auth.GetAuthenticationStateAsync().Returns(
            new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private static UiShellContextDto Context(
        bool studioAvailable,
        IReadOnlyList<ManagedActorDto>? actors = null,
        IReadOnlyList<SettingsScopeDto>? scopes = null) => new()
        {
            Workspaces = new WorkspaceAvailabilityDto { Studio = studioAvailable, Ai = false },
            ManagedActors = actors?.ToList() ?? [],
            SettingsScopes = scopes?.ToList() ?? [],
            NavigationDefaults = new UiShellNavigationDefaultsDto { OrganizerDefaultWorkspace = "Events" }
        };

    private static SettingGroupResponseDto Settings(params (string Key, string Value)[] values) => new()
    {
        Category = ShellPreferencesService.PreferencesCategory,
        Settings = values.Select(value => new EffectiveSettingDto
        {
            Key = value.Key,
            Value = value.Value,
            SettingValueTypeCode = string.Empty,
            SettingValueTypeName = string.Empty
        }).ToList()
    };
}
