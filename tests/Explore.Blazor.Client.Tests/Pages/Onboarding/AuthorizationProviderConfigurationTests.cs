using Bunit.TestDoubles;
using Explore.Blazor.Client.Pages.Onboarding;
using Explore.Blazor.Client.Pages.Onboarding.Components;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Onboarding;

public class AuthorizationProviderConfigurationTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IInstanceOnboardingService _instanceOnboardingService;
    private readonly BunitNavigationManager _nav;

    public AuthorizationProviderConfigurationTests()
    {
        _ctx = new BlazorTestContext();
        _instanceOnboardingService = Substitute.For<IInstanceOnboardingService>();
        _ctx.Services.AddSingleton(_instanceOnboardingService);
        _ctx.Services.AddSingleton(Substitute.For<ILogger<AuthorizationProviderConfiguration>>());
        _nav = _ctx.Services.GetRequiredService<BunitNavigationManager>();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task Load_WhenProviderIntentIsUnset_DefaultsToLocalAndCollapsesCerbosConfiguration()
    {
        SetupIncompleteOnboardingStatus();
        SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = string.Empty,
            CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
            CerbosDetectedFromEnvironment = true,
            CerbosEndpointOwnership = BootstrapOwnership()
        });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();

        cut.WaitForAssertion(() =>
        {
            var markup = cut.Markup;
            var workspace = cut.FindComponent<OnboardingWorkspace>();
            var providerGroup = cut.FindComponent<MudRadioGroup<string>>();
            var radios = cut.FindComponents<MudRadio<string>>();

            if (workspace is null
                || cut.FindAll("h1").Count != 1
                || !cut.Find("h1").TextContent.Contains("Authorization", StringComparison.OrdinalIgnoreCase)
                || markup.Contains("authz-page__header", StringComparison.Ordinal)
                || providerGroup.Instance.Value != "local"
                || radios.Count != 2
                || !markup.Contains("Advanced: use Cerbos PDP", StringComparison.OrdinalIgnoreCase)
                || !markup.Contains("Continue with Local RBAC", StringComparison.OrdinalIgnoreCase)
                || !markup.Contains("authz-page__local-choice--selected", StringComparison.Ordinal)
                || markup.Contains("Cerbos connection", StringComparison.OrdinalIgnoreCase)
                || markup.Contains("Download ZIP", StringComparison.OrdinalIgnoreCase)
                || markup.Contains("Ready to continue?", StringComparison.OrdinalIgnoreCase)
                || markup.Contains("Step 3 of 6", StringComparison.OrdinalIgnoreCase)
                || markup.Contains("<main", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Expected the unified-workspace Local default with collapsed Cerbos details. " +
                    $"Value={providerGroup.Instance.Value}; Radios={radios.Count}; " +
                    $"Advanced={markup.Contains("Advanced: use Cerbos PDP", StringComparison.OrdinalIgnoreCase)}; " +
                    $"LocalAction={markup.Contains("Continue with Local RBAC", StringComparison.OrdinalIgnoreCase)}; " +
                    $"SelectedClass={markup.Contains("authz-page__local-choice--selected", StringComparison.Ordinal)}; " +
                    $"CerbosConnection={markup.Contains("Cerbos connection", StringComparison.OrdinalIgnoreCase)}; " +
                    $"Download={markup.Contains("Download ZIP", StringComparison.OrdinalIgnoreCase)}; " +
                    $"NestedMain={markup.Contains("<main", StringComparison.OrdinalIgnoreCase)}.");
            }
        });

        await _instanceOnboardingService.Received(1).GetAuthorizationProviderConfigurationAsync();
    }

    [Test]
    public async Task Load_WhenAuthoritativeConfigurationFails_DisablesLocalFallback()
    {
        SetupIncompleteOnboardingStatus();
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsync()
            .Returns<Task<AuthorizationProviderConfigurationDto>>(_ => throw new HttpRequestException("unavailable"));

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Could not load authorization provider configuration", StringComparison.OrdinalIgnoreCase)
                || cut.FindAll("button").Any(button =>
                    button.TextContent.Contains("Continue with Local RBAC", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Expected a fail-closed load error without an editable Local fallback.");
            }
        });
    }

    [Test]
    public async Task SelectCerbos_FromAdvancedDisclosure_RevealsConnectionAndPolicyPackage()
    {
        SetupIncompleteOnboardingStatus();
        SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = "local",
            CerbosEndpointOwnership = ApplicationOwnership()
        });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("details");

        var details = cut.Find("details");
        await Assert.That(details.HasAttribute("open")).IsFalse();
        details.SetAttribute("open", string.Empty);
        await Assert.That(details.HasAttribute("open")).IsTrue();
        cut.FindAll("input[type=radio]").Last().Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Cerbos connection", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Download ZIP", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Runtime PDP endpoint", StringComparison.OrdinalIgnoreCase)
                || cut.Markup.Contains("authz-page__local-choice--selected", StringComparison.Ordinal)
                || cut.FindAll("button").Count(button =>
                    button.TextContent.Contains("Test endpoint", StringComparison.OrdinalIgnoreCase)) != 1
                || !cut.FindAll("button").Any(button =>
                    button.TextContent.Contains("Continue with Cerbos", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Expected Cerbos configuration after explicit advanced selection.");
            }
        });
    }

    [Test]
    public async Task VerifyCerbos_WhenCommandSucceeds_ShowsReachablePdpAndUnverifiedPolicies()
    {
        SetupIncompleteOnboardingStatus();
        var service = SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
            CerbosEndpointVerified = false,
            CerbosEndpointOwnership = ApplicationOwnership()
        });
        service.VerifyCerbosEndpointAsync(Arg.Any<string>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Message = "Cerbos PDP endpoint verified successfully."
        });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("button");

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Test endpoint", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Cerbos PDP endpoint verified successfully", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Reachable", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Policies not verified", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected reachable PDP status with unknown policy readiness.");
            }
        });
    }

    [Test]
    public async Task SaveLocal_WhenCommandSucceeds_RedirectsToInstanceOnboarding()
    {
        SetupIncompleteOnboardingStatus();
        var service = SetupFetchConfiguration(new AuthorizationProviderConfigurationDto { Provider = "local" });
        service.UpdateAuthorizationProviderConfigurationAsAdminAsync(Arg.Any<AuthorizationProviderConfigurationDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "Authorization provider saved." });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("button");

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue with Local RBAC", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!_nav.Uri.EndsWith("/onboarding/instance", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected instance onboarding, got '{_nav.Uri}'.");
            }
        });

        await service.Received(1).UpdateAuthorizationProviderConfigurationAsAdminAsync(
            Arg.Is<AuthorizationProviderConfigurationDto>(request => request.Provider == "local"));
    }

    [Test]
    public async Task SaveCerbos_WhenPoliciesManuallyConfirmed_SavesWithoutBrowserPolicySync()
    {
        SetupIncompleteOnboardingStatus();
        var service = SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
            CerbosEndpointVerified = true,
            CerbosEndpointOwnership = ApplicationOwnership()
        });
        service.UpdateAuthorizationProviderConfigurationAsAdminAsync(Arg.Any<AuthorizationProviderConfigurationDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true, Message = "Authorization provider saved." });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("input[type=checkbox]");
        cut.Find("input[type=checkbox]").Change(true);

        cut.WaitForAssertion(() =>
        {
            if (!cut.FindAll("button").Any(button =>
                    button.TextContent.Contains("Continue with Cerbos", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Expected Cerbos continuation after manual policy confirmation.");
            }
        });

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Continue with Cerbos", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!_nav.Uri.EndsWith("/onboarding/instance", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected instance onboarding, got '{_nav.Uri}'.");
            }
        });

        await service.DidNotReceive().SyncAuthorizationPolicyPackageAsync();
    }

    [Test]
    public async Task SyncPolicies_WhenServerSyncFails_ShowsSafeManualFallback()
    {
        SetupIncompleteOnboardingStatus();
        var service = SetupFetchConfiguration(CreateSyncableCerbosConfiguration());
        service.SyncAuthorizationPolicyPackageAsync(Arg.Any<AuthorizationPolicyPackageSyncRequestDto>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = false,
            Message = "Authorization policy package sync failed.",
            Errors = ["Configure Cerbos Admin API credentials before publishing."]
        });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("button");
        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Sync policies now", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authorization policy package sync failed", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("I have manually installed the Cerbos policy package", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected safe sync failure and manual fallback.");
            }
        });
    }

    [Test]
    public async Task SyncPolicies_WhenServerSyncSucceeds_AllowsCerbosContinuation()
    {
        SetupIncompleteOnboardingStatus();
        var service = SetupFetchConfiguration(CreateSyncableCerbosConfiguration());
        service.SyncAuthorizationPolicyPackageAsync(Arg.Any<AuthorizationPolicyPackageSyncRequestDto>()).Returns(new BaseCommandResponseOfGuid
        {
            Success = true,
            Message = "Policies synced. You can continue with Cerbos."
        });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("button");
        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Sync policies now", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Policies synced. You can continue with Cerbos.", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Policies confirmed", StringComparison.OrdinalIgnoreCase)
                || !cut.FindAll("button").Any(button =>
                    button.TextContent.Contains("Continue with Cerbos", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Expected successful sync to unlock Cerbos continuation.");
            }
        });

        await service.Received(1).SyncAuthorizationPolicyPackageAsync(
            Arg.Is<AuthorizationPolicyPackageSyncRequestDto>(request =>
                request.AdminUsername == null && request.AdminPassword == null));
    }

    [Test]
    public async Task Load_WithDeploymentCredentials_KeepsOneTimeOverrideCollapsed()
    {
        SetupIncompleteOnboardingStatus();
        SetupFetchConfiguration(CreateSyncableCerbosConfiguration());

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();

        cut.WaitForAssertion(() =>
        {
            var disclosure = cut.Find("details.authz-page__credentials");
            if (disclosure.HasAttribute("open")
                || !disclosure.TextContent.Contains("Use one-time credentials instead", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected the one-time override to remain collapsed when deployment credentials are available.");
            }
        });
    }

    [Test]
    public async Task SyncPolicies_WithoutDeploymentCredentials_UsesOneTimePairAndClearsTheForm()
    {
        SetupIncompleteOnboardingStatus();
        var service = SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
            CerbosEndpointVerified = true,
            CerbosEndpointOwnership = ApplicationOwnership()
        });
        service.SyncAuthorizationPolicyPackageAsync(Arg.Any<AuthorizationPolicyPackageSyncRequestDto>())
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "Policies synced."
            });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("details.authz-page__credentials[open]");
        cut.Find("#cerbos-one-time-admin-username").Input("one-time-admin");
        cut.Find("#cerbos-one-time-admin-password").Input("one-time-password");

        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Sync policies now", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Policies synced.", StringComparison.OrdinalIgnoreCase)
                || cut.FindAll("#cerbos-one-time-admin-password").Count != 0)
            {
                throw new InvalidOperationException("Expected successful sync to remove the cleared one-time credential form.");
            }
        });

        await service.Received(1).SyncAuthorizationPolicyPackageAsync(
            Arg.Is<AuthorizationPolicyPackageSyncRequestDto>(request =>
                request.AdminUsername == "one-time-admin"
                && request.AdminPassword == "one-time-password"));
    }

    [Test]
    [Arguments("local", "ready")]
    [Arguments("cerbos", "ready")]
    public async Task Load_WhenDeploymentOwnsProviderAndNoRemediationIsNeeded_SkipsPage(
        string provider,
        string bootstrapStatus)
    {
        SetupIncompleteOnboardingStatus();
        SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = provider,
            AuthorizationProviderManagedByDeployment = true,
            AuthorizationProviderConfigured = bootstrapStatus == "ready",
            AuthorizationProviderBootstrapStatus = bootstrapStatus
        });

        _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();

        await Assert.That(_nav.Uri).EndsWith("/onboarding/instance");
    }

    [Test]
    public async Task Load_WhenDeploymentCredentialsAreMissing_ShowsOpenOneTimeRemediation()
    {
        SetupIncompleteOnboardingStatus();
        SetupFetchConfiguration(new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            CerbosGrpcEndpoint = "http://cerbos:3593",
            AuthorizationProviderManagedByDeployment = true,
            AuthorizationProviderConfigured = false,
            AuthorizationProviderBootstrapStatus = "pending",
            CerbosEndpointOwnership = new SecretOwnershipDto
            {
                Mode = "deployment-managed",
                Badge = "Deployment Managed",
                Description = "Change the endpoint in deployment configuration.",
                Editable = false
            }
        });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Find("details.authz-page__credentials").HasAttribute("open")
                || !cut.Markup.Contains("Enter one-time Admin API credentials", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Sync policies and continue", StringComparison.OrdinalIgnoreCase)
                || _nav.Uri.EndsWith("/onboarding/instance", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected immediate one-time credential remediation when deployment secrets are absent.");
            }
        });
    }

    [Test]
    public async Task Load_WhenDeploymentCerbosFailed_ShowsLockedFailClosedRemediationOnly()
    {
        SetupIncompleteOnboardingStatus();
        SetupFetchConfiguration(FailedDeploymentCerbosConfiguration());

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Cerbos needs attention", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("The deployment-managed Cerbos PDP endpoint could not be reached.", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Retry automatic setup", StringComparison.OrdinalIgnoreCase)
                || cut.FindAll("input[type=radio]").Count != 0
                || cut.FindAll("input[type=checkbox]").Count != 0
                || !cut.Find("input").HasAttribute("disabled"))
            {
                throw new InvalidOperationException("Expected locked Cerbos remediation without a Local fallback.");
            }
        });
    }

    [Test]
    public async Task RetryDeploymentCerbos_WhenReconciliationSucceeds_RedirectsToInstance()
    {
        SetupIncompleteOnboardingStatus();
        var failed = FailedDeploymentCerbosConfiguration();
        var ready = FailedDeploymentCerbosConfiguration();
        ready.AuthorizationProviderBootstrapStatus = "ready";
        ready.AuthorizationProviderConfigured = true;
        ready.CerbosEndpointVerified = true;
        ready.CerbosPoliciesSynchronized = true;

        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsync().Returns(failed, ready);
        _instanceOnboardingService.SyncAuthorizationPolicyPackageAsync(Arg.Any<AuthorizationPolicyPackageSyncRequestDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });

        var cut = _ctx.RenderMudComponent<AuthorizationProviderConfiguration>();
        cut.WaitForElement("button");
        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Retry automatic setup", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!_nav.Uri.EndsWith("/onboarding/instance", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Expected instance onboarding, got '{_nav.Uri}'.");
            }
        });
    }

    private void SetupIncompleteOnboardingStatus()
    {
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = false,
            IsAuthenticated = false
        });
    }

    private IInstanceOnboardingService SetupFetchConfiguration(AuthorizationProviderConfigurationDto model)
    {
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsync().Returns(model);
        return _instanceOnboardingService;
    }

    private static AuthorizationProviderConfigurationDto CreateSyncableCerbosConfiguration() => new()
    {
        Provider = "cerbos",
        CerbosGrpcEndpoint = "cerbosgrpc.openislamu.org:443",
        CerbosEndpointVerified = true,
        CerbosAdminUsernameConfigured = true,
        CerbosAdminPasswordConfigured = true,
        CerbosEndpointOwnership = ApplicationOwnership()
    };

    private static AuthorizationProviderConfigurationDto FailedDeploymentCerbosConfiguration() => new()
    {
        Provider = "cerbos",
        CerbosGrpcEndpoint = "http://cerbos:3593",
        AuthorizationProviderManagedByDeployment = true,
        AuthorizationProviderConfigured = false,
        AuthorizationProviderBootstrapStatus = "failed",
        AuthorizationProviderBootstrapMessage = "The deployment-managed Cerbos PDP endpoint could not be reached.",
        CerbosAdminUsernameConfigured = true,
        CerbosAdminPasswordConfigured = true,
        CerbosEndpointOwnership = new SecretOwnershipDto
        {
            Mode = "deployment-managed",
            Badge = "Deployment Managed",
            Description = "Change the endpoint in deployment configuration.",
            Editable = false
        }
    };

    private static SecretOwnershipDto ApplicationOwnership() => new()
    {
        Mode = "application-managed",
        Badge = "Application Managed",
        Description = "Saved by the application.",
        Editable = true
    };

    private static SecretOwnershipDto BootstrapOwnership() => new()
    {
        Mode = "application-managed",
        Source = "deployment-bootstrap",
        Badge = "Bootstrap from Deployment",
        Description = "These values were detected from deployment configuration.",
        Editable = true,
        BootstrapAvailable = true
    };
}
