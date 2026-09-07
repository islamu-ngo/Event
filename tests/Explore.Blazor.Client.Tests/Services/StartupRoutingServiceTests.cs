namespace Explore.Blazor.Client.Tests.Services;

public sealed class StartupRoutingServiceTests
{
    #region Interactive setup

    [Test]
    public async Task GetRootDecisionAsync_ReturnsSetup_ForInteractivePending()
    {
        var instanceOnboarding = CreateInstanceOnboardingService(InteractivePending());
        var publicExperience = CreatePublicExperienceService();
        var service = new StartupRoutingService(instanceOnboarding, publicExperience);

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.Setup);
        await publicExperience.DidNotReceive().GetCachedShellAsync();
        await publicExperience.DidNotReceive().GetCachedSettingsAsync();
    }

    #endregion

    #region Configured administrator pending

    [Test]
    [Arguments("Keycloak", StartupRouteDecision.KeycloakChallenge)]
    [Arguments("Atproto", StartupRouteDecision.AtprotoChallenge)]
    public async Task GetRootDecisionAsync_ChallengesTheConfiguredProvider_AndNeverTheWizard(
        string provider,
        StartupRouteDecision expected)
    {
        var instanceOnboarding = CreateInstanceOnboardingService(ConfiguredAdministratorPending(provider));
        var publicExperience = CreatePublicExperienceService();
        var service = new StartupRoutingService(instanceOnboarding, publicExperience);

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(expected);
        await Assert.That(decision).IsNotEqualTo(StartupRouteDecision.Setup);
        await publicExperience.DidNotReceive().GetCachedShellAsync();
    }

    [Test]
    [Arguments("keycloak")]
    [Arguments("KEYCLOAK")]
    [Arguments("atproto")]
    [Arguments("Google")]
    [Arguments("")]
    public async Task GetRootDecisionAsync_ReturnsUnavailable_WhenPendingProviderIsNotAnExactConfiguredProvider(
        string provider)
    {
        var instanceOnboarding = CreateInstanceOnboardingService(ConfiguredAdministratorPending(provider));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.Unavailable);
        await Assert.That(decision).IsNotEqualTo(StartupRouteDecision.Setup);
    }

    [Test]
    public async Task GetRootDecisionAsync_ReturnsUnavailable_WhenPendingProviderIsMissing()
    {
        var instanceOnboarding = CreateInstanceOnboardingService(ConfiguredAdministratorPending(provider: null));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.Unavailable);
        await Assert.That(decision).IsNotEqualTo(StartupRouteDecision.Setup);
    }

    #endregion

    #region Completed routing

    [Test]
    public async Task GetRootDecisionAsync_ReturnsInstanceAdmin_ForAuthenticatedInstanceAdminInMultiTenant()
    {
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: "MultiTenant",
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.InstanceAdmin);
    }

    [Test]
    public async Task GetRootDecisionAsync_ReturnsPublicHome_WhenServerWithholdsInstanceAdminAuthority()
    {
        // Instance-admin authority is only ever the server-provided status flag; the client never
        // derives it from a local role, claim, or authentication state.
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: "MultiTenant",
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: false));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.PublicHome);
    }

    [Test]
    public async Task GetRootDecisionAsync_ReturnsPublicHome_ForInstanceAdminInSingleTenant()
    {
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: "SingleTenant",
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.PublicHome);
    }

    [Test]
    public async Task GetRootDecisionAsync_ReturnsPublicLanding_FromCachedShellHomeRoute()
    {
        var publicExperience = CreatePublicExperienceService(
            homeRoute: "/home",
            shell: new PublicExperienceShellDto());
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: "SingleTenant",
            isAuthenticated: false,
            isCurrentUserInstanceAdmin: false));
        var service = new StartupRoutingService(instanceOnboarding, publicExperience);

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.PublicLanding);
        await publicExperience.DidNotReceive().GetCachedSettingsAsync();
    }

    [Test]
    public async Task GetRootDecisionAsync_FallsBackToCachedSettingsHomeRoute_WhenShellIsUnavailable()
    {
        var publicExperience = CreatePublicExperienceService(
            homeRoute: "/home",
            settings: new PublicExperienceSettingsDto());
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: "MultiTenant",
            isAuthenticated: false,
            isCurrentUserInstanceAdmin: false));
        var service = new StartupRoutingService(instanceOnboarding, publicExperience);

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.PublicLanding);
        await publicExperience.Received(1).GetCachedSettingsAsync();
    }

    #endregion

    #region Fail-closed surfaces

    [Test]
    [Arguments("HybridTenant")]
    [Arguments("singletenant")]
    [Arguments(" ")]
    [Arguments("")]
    public async Task GetRootDecisionAsync_ReturnsUnavailable_WhenCompletedDeploymentModeIsNotCanonical(string mode)
    {
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: mode,
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.Unavailable);
    }

    [Test]
    public async Task GetRootDecisionAsync_ReturnsUnavailable_WhenCompletedDeploymentModeIsMissing()
    {
        var instanceOnboarding = CreateInstanceOnboardingService(Completed(
            selectedDeploymentMode: null,
            isAuthenticated: true,
            isCurrentUserInstanceAdmin: true));
        var service = new StartupRoutingService(instanceOnboarding, CreatePublicExperienceService());

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.Unavailable);
    }

    [Test]
    public async Task GetRootDecisionAsync_ReturnsUnavailable_WhenStartupStatusIsUnavailable()
    {
        var instanceOnboarding = CreateInstanceOnboardingService(InstanceOnboardingStartupStatus.Unavailable);
        var publicExperience = CreatePublicExperienceService();
        var service = new StartupRoutingService(instanceOnboarding, publicExperience);

        var decision = await service.GetRootDecisionAsync();

        await Assert.That(decision).IsEqualTo(StartupRouteDecision.Unavailable);
        await Assert.That(decision).IsNotEqualTo(StartupRouteDecision.Setup);
        await publicExperience.DidNotReceive().GetCachedShellAsync();
    }

    #endregion

    private static InstanceOnboardingStartupStatus InteractivePending() => new(
        InstanceOnboardingStartupDisposition.InteractivePending,
        Provider: null,
        Generation: 1,
        IsAuthenticated: false,
        IsCurrentUserInstanceAdmin: false,
        SelectedDeploymentMode: null);

    private static InstanceOnboardingStartupStatus ConfiguredAdministratorPending(string? provider) => new(
        InstanceOnboardingStartupDisposition.ConfiguredAdministratorPending,
        provider,
        Generation: 2,
        IsAuthenticated: false,
        IsCurrentUserInstanceAdmin: false,
        SelectedDeploymentMode: null);

    private static InstanceOnboardingStartupStatus Completed(
        string? selectedDeploymentMode,
        bool isAuthenticated,
        bool isCurrentUserInstanceAdmin) => new(
        InstanceOnboardingStartupDisposition.Completed,
        Provider: null,
        Generation: 3,
        isAuthenticated,
        isCurrentUserInstanceAdmin,
        selectedDeploymentMode);

    private static IInstanceOnboardingService CreateInstanceOnboardingService(
        InstanceOnboardingStartupStatus status)
    {
        var instanceOnboarding = Substitute.For<IInstanceOnboardingService>();
        instanceOnboarding.GetStartupStatusAsync(Arg.Any<CancellationToken>()).Returns(status);
        return instanceOnboarding;
    }

    private static IPublicExperienceService CreatePublicExperienceService(
        string homeRoute = "/events",
        PublicExperienceShellDto? shell = null,
        PublicExperienceSettingsDto? settings = null)
    {
        var publicExperience = Substitute.For<IPublicExperienceService>();
        publicExperience.GetCachedShellAsync().Returns(Task.FromResult(shell));
        publicExperience.GetCachedSettingsAsync().Returns(Task.FromResult(settings));
        publicExperience.ResolveHomeRoute(Arg.Any<PublicExperienceShellDto?>()).Returns(homeRoute);
        publicExperience.ResolveHomeRoute(Arg.Any<PublicExperienceSettingsDto?>()).Returns(homeRoute);
        return publicExperience;
    }
}
