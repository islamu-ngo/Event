namespace Explore.Blazor.Client.Tests.Services.Shell;

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Shell;
using Microsoft.AspNetCore.Components.Web;

public sealed class UiShellStateTests : IDisposable
{
    private readonly BlazorTestContext _context = new();

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task NavigationPublishesWorkspaceAndPreservesLastRouteQuery()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        var classifier = new WorkspaceRouteClassifier(new WorkspaceRegistry());
        using var state = new UiShellState(navigation, classifier);
        var changes = 0;
        state.Changed += () => changes++;

        navigation.NavigateTo("/events?q=iftar&format=online");
        navigation.NavigateTo("/settings?view=all");

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Settings);
        await Assert.That(state.GetLastRoute(WorkspaceKey.Events)).IsEqualTo("/events?q=iftar&format=online");
        await Assert.That(state.GetLastRoute(WorkspaceKey.Settings)).IsEqualTo("/settings?view=all");
        await Assert.That(changes).IsEqualTo(2);
    }

    [Test]
    [Arguments("/events?q=iftar", "events")]
    [Arguments("/studio/events", "studio")]
    [Arguments("/ai/chats/01912345-6789-7abc-8def-0123456789ab", "ai")]
    public async Task PersonalSettingsNavigationPreservesLiveWorkspaceOrigin(string originRoute, string expectedWorkspace)
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
        navigation.NavigateTo(originRoute);

        state.NavigateToPersonalSettings("/settings/personal/appearance");

        await Assert.That(state.ActiveWorkspace.Value).IsEqualTo(expectedWorkspace);
        await Assert.That(state.IsPersonalSettingsOpen).IsTrue();
        await Assert.That(state.PersonalSettingsReturnRoute).IsEqualTo(originRoute);
        await Assert.That(state.GetLastRoute(state.ActiveWorkspace)).IsEqualTo(originRoute);
        await Assert.That(state.GetLastRoute(WorkspaceKey.Settings)).IsNull();
    }

    [Test]
    public async Task DirectPersonalSettingsLoadUsesDedicatedSettingsWorkspace()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings/personal/appearance");

        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Settings);
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
        await Assert.That(state.GetLastRoute(WorkspaceKey.Settings)).IsEqualTo("/settings/personal/appearance");
    }

    [Test]
    public async Task InSessionPersonalNavigationWithoutContractUsesDedicatedSettingsWorkspace()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
        navigation.NavigateTo("/events?q=origin");

        navigation.NavigateTo("/settings/personal/appearance");

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Settings);
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
    }

    [Test]
    [Arguments("settings/personal", "/settings/personal")]
    [Arguments("/SETTINGS/PERSONAL/APPEARANCE?custom=true", "/settings/personal/appearance?custom=true")]
    public async Task PersonalSettingsContractNormalizesCanonicalRoutes(string destination, string expectedRoute)
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
        navigation.NavigateTo("/studio/events?q=draft");

        state.NavigateToPersonalSettings(destination);

        await Assert.That(new Uri(navigation.Uri).PathAndQuery).IsEqualTo(expectedRoute);
        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Studio);
        await Assert.That(state.PersonalSettingsReturnRoute).IsEqualTo("/studio/events?q=draft");
    }

    [Test]
    public async Task ModifiedPersonalSettingsClickKeepsNativeNavigationAndDoesNotCaptureOrigin()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
        navigation.NavigateTo("/ai/chats/current");

        state.NavigateToPersonalSettings(
            "/settings/personal/appearance",
            new MouseEventArgs { CtrlKey = true });

        await Assert.That(new Uri(navigation.Uri).PathAndQuery).IsEqualTo("/ai/chats/current");
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
    }

    [Test]
    [Arguments("/settings/personal?returnUrl=/events")]
    [Arguments("/settings/personal/appearance/advanced")]
    [Arguments("/settings/personal/%2Fadvanced")]
    [Arguments("https://example.test/settings/personal")]
    public async Task PersonalSettingsContractRejectsNonCanonicalDestinations(string destination)
    {
        using var state = CreateState();

        var exception = Assert.Throws<ArgumentException>(() => state.NavigateToPersonalSettings(destination));

        await Assert.That(exception).IsNotNull();
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
    }

    [Test]
    public async Task PersonalSectionNavigationKeepsOriginUntilLeavingPersonalSettings()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
        navigation.NavigateTo("/studio/events?q=draft");
        state.NavigateToPersonalSettings("/settings/personal/security");

        navigation.NavigateTo("/settings/personal/privacy");

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Studio);
        await Assert.That(state.IsPersonalSettingsOpen).IsTrue();
        await Assert.That(state.PersonalSettingsReturnRoute).IsEqualTo("/studio/events?q=draft");

        navigation.NavigateTo("/settings/admin");

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Settings);
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
    }

    [Test]
    [Arguments("/settings/personalization")]
    [Arguments("/settings/personal-preview")]
    public async Task PersonalSettingsRouteRequiresSegmentBoundary(string route)
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        using var state = new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
        navigation.NavigateTo("/events?q=origin");

        navigation.NavigateTo(route);

        await Assert.That(state.ActiveWorkspace).IsEqualTo(WorkspaceKey.Settings);
        await Assert.That(state.IsPersonalSettingsOpen).IsFalse();
        await Assert.That(state.PersonalSettingsReturnRoute).IsNull();
    }

    [Test]
    public async Task ReconcileActiveActorsPrefersPinnedActorAndPreservesAuthorizedSelection()
    {
        var firstActor = CreateActor("Organization", "First");
        var secondActor = CreateActor("Group", "Second");
        using var state = CreateState();

        state.ReconcileActiveActors([firstActor, secondActor], pinnedActorId: null);
        var selected = state.TrySetActiveActor(secondActor.ActorId!.Value, [firstActor, secondActor]);
        state.ReconcileActiveActors([firstActor, secondActor], pinnedActorId: null);

        await Assert.That(selected).IsTrue();
        await Assert.That(state.ActiveActorId).IsEqualTo(secondActor.ActorId);

        state.ReconcileActiveActors([firstActor, secondActor], firstActor.ActorId);

        await Assert.That(state.ActiveActorId).IsEqualTo(firstActor.ActorId);
    }

    [Test]
    public async Task TrySetActiveActorRejectsUnauthorizedActor()
    {
        var actor = CreateActor("Organization", "Authorized");
        using var state = CreateState();
        state.ReconcileActiveActors([actor], pinnedActorId: null);

        var selected = state.TrySetActiveActor(Guid.CreateVersion7(), [actor]);

        await Assert.That(selected).IsFalse();
        await Assert.That(state.ActiveActorId).IsEqualTo(actor.ActorId);
    }

    private UiShellState CreateState()
    {
        var navigation = _context.Services.GetRequiredService<NavigationManager>();
        return new UiShellState(navigation, new WorkspaceRouteClassifier(new WorkspaceRegistry()));
    }

    private static ManagedActorDto CreateActor(string type, string name) => new()
    {
        ActorId = Guid.CreateVersion7(),
        ActorType = type,
        DisplayName = name
    };
}
