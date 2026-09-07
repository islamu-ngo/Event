using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Services.Shell;

public sealed class UiShellStateRevocationTests : IDisposable
{
    private readonly BlazorTestContext _context = new();

    public UiShellStateRevocationTests()
    {
        _context.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        _context.Services.AddScoped<WorkspaceRouteClassifier>();
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task ReconcileAvailability_RevokedStudioRoute_FallsBackToEvents()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var classifier = _context.Services.GetRequiredService<WorkspaceRouteClassifier>();
        using var state = new UiShellState(navigation, classifier);

        navigation.NavigateTo("/studio?section=dashboard");

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Studio);
        await Assert.That(state.GetLastRoute(WorkspaceKey.Studio)).IsEqualTo("/studio?section=dashboard");

        state.ReconcileAvailability(key => key != WorkspaceKey.Studio);

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Events);
        await Assert.That(state.GetLastRoute(WorkspaceKey.Studio)).IsNull();
    }

    [Test]
    public async Task ReconcileAvailability_AvailableWorkspace_DoesNotNavigate()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var classifier = _context.Services.GetRequiredService<WorkspaceRouteClassifier>();
        using var state = new UiShellState(navigation, classifier);

        navigation.NavigateTo("/settings/instance");

        var changesBefore = 0;
        state.Changed += () => changesBefore++;

        state.ReconcileAvailability(_ => true);

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Settings);
        await Assert.That(state.GetLastRoute(WorkspaceKey.Settings)).IsEqualTo("/settings/instance");
        await Assert.That(changesBefore).IsEqualTo(0);
    }

    [Test]
    public async Task ReconcileAvailability_RemovesAllRevokedStoredRoutes()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var classifier = _context.Services.GetRequiredService<WorkspaceRouteClassifier>();
        using var state = new UiShellState(navigation, classifier);

        navigation.NavigateTo("/studio?section=dashboard");
        navigation.NavigateTo("/settings/instance");
        navigation.NavigateTo("/events?q=test");

        state.ReconcileAvailability(key => key == WorkspaceKey.Events);

        await Assert.That(state.GetLastRoute(WorkspaceKey.Studio)).IsNull();
        await Assert.That(state.GetLastRoute(WorkspaceKey.Settings)).IsNull();
        await Assert.That(state.GetLastRoute(WorkspaceKey.Events)).IsEqualTo("/events?q=test");
    }

    [Test]
    public async Task ReconcileAvailability_RevokedPersonalSettingsOrigin_ClearsUtilityAndFallsBackToEvents()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var classifier = _context.Services.GetRequiredService<WorkspaceRouteClassifier>();
        using var state = new UiShellState(navigation, classifier);
        navigation.NavigateTo("/studio/events");
        state.NavigateToPersonalSettings("/settings/personal/appearance");

        state.ReconcileAvailability(key => key != WorkspaceKey.Studio);

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Events);
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
        await Assert.That(state.GetLastRoute(WorkspaceKey.Studio)).IsNull();
    }

    [Test]
    public async Task ReconcileAvailability_RevokedSettingsUtility_ReturnsToCapturedWorkspaceRoute()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var classifier = _context.Services.GetRequiredService<WorkspaceRouteClassifier>();
        using var state = new UiShellState(navigation, classifier);
        navigation.NavigateTo("/events?q=iftar");
        state.NavigateToPersonalSettings("/settings/personal/security");

        state.ReconcileAvailability(key => key != WorkspaceKey.Settings);

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Events);
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
        await Assert.That(navigation.ToBaseRelativePath(navigation.Uri)).IsEqualTo("events?q=iftar");
    }
}
