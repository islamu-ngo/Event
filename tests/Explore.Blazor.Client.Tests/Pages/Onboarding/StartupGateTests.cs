using Explore.Blazor.Client.Pages.Onboarding;
using Explore.Blazor.Client.Routing.ControlPlane;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public class StartupGateTests : IDisposable
{
    private const string UnavailableMessage = "Could not determine a safe startup destination. Try refreshing.";
    private const string RoutingFailedMessage = "Startup routing failed. Try refreshing.";

    private readonly BlazorTestContext _ctx;
    private readonly IStartupRoutingService _startupRouting;
    private readonly BunitNavigationManager _nav;

    public StartupGateTests()
    {
        _ctx = new BlazorTestContext();
        _startupRouting = Substitute.For<IStartupRoutingService>();

        _ctx.Services.AddSingleton(_startupRouting);
        _ctx.Services.AddSingleton(Substitute.For<ILogger<StartupGate>>());

        _nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
        _nav.NavigateTo("/startup");
    }

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Interactive setup

    [Test]
    public async Task StartupGate_WhenDecisionIsSetup_RedirectsToSetupWizard()
    {
        _startupRouting.GetRootDecisionAsync().Returns(StartupRouteDecision.Setup);

        var cut = _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith("/setup");
        await Assert.That(cut.FindAll("[role='alert']")).IsEmpty();
    }

    #endregion

    #region Configured provider challenge

    [Test]
    [Arguments(StartupRouteDecision.KeycloakChallenge, "keycloak")]
    [Arguments(StartupRouteDecision.AtprotoChallenge, "atproto")]
    public async Task StartupGate_WhenDecisionIsProviderChallenge_ForceLoadsThatProviderAndNeverTheWizard(
        StartupRouteDecision decision,
        string expectedProvider)
    {
        _startupRouting.GetRootDecisionAsync().Returns(decision);

        _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri())
            .EndsWith($"/auth/challenge?provider={expectedProvider}&returnUrl=%2Fstartup");
        await Assert.That(LastNavigationForcedLoad()).IsTrue();
        await Assert.That(NavigatedUris().Any(uri => uri.Contains("/setup", StringComparison.Ordinal))).IsFalse();
        await Assert.That(NavigatedUris().Any(uri => uri.Contains("/onboarding", StringComparison.Ordinal))).IsFalse();
    }

    #endregion

    #region Completed routing

    [Test]
    public async Task StartupGate_WhenDecisionIsInstanceAdmin_RedirectsToControlPlaneOverview()
    {
        _startupRouting.GetRootDecisionAsync().Returns(StartupRouteDecision.InstanceAdmin);

        _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith(ControlPlaneRoutes.Overview);
    }

    [Test]
    public async Task StartupGate_WhenDecisionIsPublicHome_RedirectsToEvents()
    {
        _startupRouting.GetRootDecisionAsync().Returns(StartupRouteDecision.PublicHome);

        _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith("/events");
    }

    [Test]
    public async Task StartupGate_WhenDecisionIsPublicLanding_RedirectsToLanding()
    {
        _startupRouting.GetRootDecisionAsync().Returns(StartupRouteDecision.PublicLanding);

        _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith("/home");
    }

    [Test]
    public async Task StartupGate_WhenLocalRolesClaimAdminAuthority_StillHonorsTheServerDecision()
    {
        // Local roles and claims must never upgrade the destination: only the server-derived
        // decision selects the control plane.
        _ctx.SetAuthenticatedUserWithRoles(Guid.NewGuid(), "Local Admin", "InstanceAdmin", "Admin");
        _startupRouting.GetRootDecisionAsync().Returns(StartupRouteDecision.PublicHome);

        _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith("/events");
        await Assert.That(NavigatedUris().Any(uri => uri.Contains(ControlPlaneRoutes.Root, StringComparison.Ordinal)))
            .IsFalse();
    }

    #endregion

    #region Fail-closed accessible status

    [Test]
    public async Task StartupGate_WhenDecisionIsUnavailable_StaysPutAndAnnouncesTheFailure()
    {
        _startupRouting.GetRootDecisionAsync().Returns(StartupRouteDecision.Unavailable);

        var cut = _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith("/startup");
        await Assert.That(NavigationCount()).IsEqualTo(1);
        await Assert.That(cut.Find("[role='alert']").TextContent.Trim()).IsEqualTo(UnavailableMessage);
        await Assert.That(cut.FindAll("h1")).HasCount(1);
        await Assert.That(cut.FindAll("[role='status']")).IsEmpty();
    }

    [Test]
    public async Task StartupGate_WhenRoutingFails_StaysPutAndAnnouncesTheFailure()
    {
        _startupRouting.GetRootDecisionAsync().ThrowsAsync(new HttpRequestException("Routing unavailable."));

        var cut = _ctx.RenderMudComponent<StartupGate>();

        await Assert.That(LastNavigationUri()).EndsWith("/startup");
        await Assert.That(NavigationCount()).IsEqualTo(1);
        await Assert.That(cut.Find("[role='alert']").TextContent.Trim()).IsEqualTo(RoutingFailedMessage);
        await Assert.That(cut.FindAll("h1")).HasCount(1);
    }

    #endregion

    // BunitNavigationManager.History is stack ordered: the first element is the latest navigation.
    private string LastNavigationUri() => _nav.History.First().Uri;

    private bool LastNavigationForcedLoad() => _nav.History.First().Options.ForceLoad;

    private int NavigationCount() => _nav.History.Count;

    // Drops the seeded "/startup" entry so only component-initiated navigations remain.
    private IEnumerable<string> NavigatedUris() => _nav.History.SkipLast(1).Select(entry => entry.Uri);
}
