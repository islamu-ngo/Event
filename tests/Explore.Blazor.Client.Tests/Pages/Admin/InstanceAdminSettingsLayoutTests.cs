// ABOUTME: bUnit tests for instance admin settings layout section reachability.
// ABOUTME: Verifies single-tenant administration exposes tenant-level public experience controls.

using Explore.Blazor.Client.Contracts.Services.ControlPlane;
using Explore.Blazor.Client.Contracts.Services.Federation;
using Explore.Blazor.Client.Contracts.Services.PaidEventPolicies;
using Explore.Blazor.Client.Contracts.Services.Scheduling;
using Explore.Blazor.Client.Models;
using Explore.Blazor.Client.Pages.Admin.Instance.Components;
using Explore.Blazor.Client.Tests.Common.Authentication;
using Explore.Blazor.Client.Tests.Services;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class InstanceAdminSettingsLayoutTests : IDisposable
{
    private readonly BlazorTestContext _ctx;
    private readonly IInstanceOnboardingService _instanceOnboardingService;
    private readonly ITenantOnboardingService _tenantOnboardingService;
    private readonly ITenantPublicExperienceAdminService _publicExperienceAdminService;
    private readonly IOrganizationService _organizationService;
    private readonly IUserService _userService;
    private readonly IPlatformMonetizationService _monetizationService;
    private readonly IPaidEventPolicyService _paidEventPolicyService;
    private readonly ISchedulerAdminService _schedulerAdminService;
    private readonly LocalIdentityUiTransport _localTransport = new() { Discoverable = false };
    private readonly HttpClient _localHttp;

    public InstanceAdminSettingsLayoutTests()
    {
        _ctx = new BlazorTestContext();
        _localHttp = _localTransport.CreateHttpClient();
        _ctx.Services.AddSingleton<IControlPlaneOverviewService>(_localTransport.CreateOverview(_localHttp));
        _ctx.Services.AddScoped(_ => _localTransport.CreateService(_localHttp));
        _ctx.AddShellStateMocks();
        _ctx.SetAuthenticatedUser(Guid.NewGuid(), "Instance Admin", "admin@example.com");
        _ctx.AddMockService<IControlPlaneOperationsService>();

        // The layout discovers the scheduler section by asking the server for it. The default substitute returns
        // no resource, which is the "host has not enabled the scheduler admin API" case every other test assumes.
        _schedulerAdminService = _ctx.AddMockService<ISchedulerAdminService>();

        _instanceOnboardingService = _ctx.AddMockService<IInstanceOnboardingService>();
        _tenantOnboardingService = _ctx.AddMockService<ITenantOnboardingService>();
        _publicExperienceAdminService = _ctx.AddMockService<ITenantPublicExperienceAdminService>();
        _organizationService = _ctx.AddMockService<IOrganizationService>();
        _userService = _ctx.AddMockService<IUserService>();
        _monetizationService = _ctx.AddMockService<IPlatformMonetizationService>();
        _paidEventPolicyService = _ctx.AddMockService<IPaidEventPolicyService>();
        _monetizationService.GetAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfPlatformMonetizationSettingsDto());
        _paidEventPolicyService.GetInstanceAsync(Arg.Any<CancellationToken>()).Returns(new HalResourceOfPaidEventPolicyDto());

        ConfigureSingleTenantInstanceDefaults();
    }

    public void Dispose()
    {
        _ctx.Dispose();
        _localHttp.Dispose();
        _localTransport.Dispose();
    }

    [Test]
    [Arguments(DeploymentMode.SingleTenant, true)]
    [Arguments(DeploymentMode.SingleTenant, false)]
    [Arguments(DeploymentMode.MultiTenant, true)]
    [Arguments(DeploymentMode.MultiTenant, false)]
    public async Task LocalAccountsNavigationComesOnlyFromOverviewInEitherDeploymentMode(DeploymentMode mode, bool discoverable)
    {
        _localTransport.Discoverable = discoverable;
        _instanceOnboardingService.GetDeploymentModeAsync().Returns(new DeploymentModeDto { Mode = mode });
        _instanceOnboardingService.GetStatusAsync().Returns(new InstanceOnboardingStatusDto
        {
            IsCompleted = true, IsAuthenticated = true, IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = mode.ToString()
        });
        var cut = RenderInstanceAdminSettingsLayout();
        cut.WaitForState(() => cut.FindAll("[role='option']").Any());
        cut.WaitForAssertion(() =>
        {
            if (cut.FindAll("[role='option']").Any(item => item.TextContent.Trim() == "Local accounts") != discoverable)
                throw new InvalidOperationException("Local accounts navigation must follow overview discovery.");
        });
        await Assert.That(_localTransport.Requests.Any(request => request.Uri.AbsolutePath == "/api/admin/control-plane/overview")).IsTrue();
        if (discoverable)
        {
            cut.FindAll("[role='option']").Single(item => item.TextContent.Trim() == "Local accounts").Click();
            cut.WaitForElement("#local-accounts-heading");
        }
    }

    /// <summary>
    /// Whether the scheduler section exists is a server fact: the host may not expose the administration API at
    /// all. The layout must therefore render the item from the served resource, never from the administrator's
    /// local claims.
    /// </summary>
    [Test]
    public async Task InstanceAdminSettingsLayout_WhenSchedulerAdminApiIsServed_ExposesSchedulerNavigation()
    {
        _schedulerAdminService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns(new HalResourceOfSchedulerAdminOverviewDto());

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Background Scheduler", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Background Scheduler navigation item was not rendered.");
            }
        });

        await Assert.That(cut.Markup).Contains("Background Scheduler", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_WhenSchedulerAdminApiIsAbsent_HidesSchedulerNavigation()
    {
        _schedulerAdminService.GetOverviewAsync(Arg.Any<CancellationToken>())
            .Returns((HalResourceOfSchedulerAdminOverviewDto?)null);

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Support Access", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Operations navigation group was not rendered.");
            }
        });

        await Assert.That(cut.Markup).DoesNotContain("Background Scheduler", StringComparison.OrdinalIgnoreCase);
    }

    private IRenderedComponent<InstanceAdminSettingsLayout> RenderInstanceAdminSettingsLayout() =>
        _ctx.RenderMudComponent<InstanceAdminSettingsLayout>();

    [Test]
    public async Task InstanceAdminSettingsLayout_SingleTenant_ExposesPublicExperienceNavigation()
    {
        // Arrange
        // Act
        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Public Experience", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Public Experience navigation item was not rendered.");
            }
        });

        await Assert.That(cut.Markup).Contains("Public Experience", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_MonetizationIncludesPaidEventCeiling()
    {
        var cut = RenderInstanceAdminSettingsLayout();
        cut.WaitForState(() => cut.Markup.Contains("Monetization", StringComparison.Ordinal));

        cut.FindAll("[role='option']")
            .Single(item => item.TextContent.Trim() == "Monetization")
            .Click();

        cut.WaitForElement("[data-testid='instance-paid-policy-section']");
        await Assert.That(cut.FindAll("[data-testid='instance-monetization-section']").Count).IsEqualTo(1);
        await _paidEventPolicyService.Received(1).GetInstanceAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_SingleTenantInstanceOnly_HidesTenantAdministrationSections()
    {
        _userService.GetAdminAuthorityAsync()
            .Returns(new AdminAuthorityDto
            {
                IsInstanceAdmin = true,
                AdminTenantIds = [],
                AdminOrganizationIds = [],
                HasAnyAuthority = true
            });
        _tenantOnboardingService.GetStatusAsync()
            .Returns(new TenantOnboardingStatusDto
            {
                IsAuthenticated = true,
                IsCurrentUserTenantAdministrator = false,
                IsCurrentUserPlatformAdministrator = true
            });

        var cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForState(() => cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase));
        var navLabels = cut.FindAll("[role='option']")
            .Select(item => item.TextContent.Trim())
            .ToArray();
        await Assert.That(navLabels).DoesNotContain("Public Experience");
        await Assert.That(navLabels).DoesNotContain("Members");
        await Assert.That(navLabels).DoesNotContain("Organizations");
        await Assert.That(navLabels).DoesNotContain("Policies");
        cut.FindAll("[role='option']")
            .Single(item => item.TextContent.Trim() == "Advanced")
            .Click();
        cut.WaitForState(() => cut.Markup.Contains("AT Protocol Event Federation", StringComparison.Ordinal));
        await _ctx.Services.GetRequiredService<IAtprotoFederationSettingsService>().Received(1)
            .GetInstanceAsync(Arg.Any<CancellationToken>());
        await _publicExperienceAdminService.DidNotReceive()
            .ApplySingleTenantPolicySettingsAsync(Arg.Any<TenantPolicySettingsDto>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_SingleTenant_RendersPublicExperienceSectionWhenSelected()
    {
        // Arrange
        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Public Experience", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Public Experience navigation item was not rendered.");
            }
        });

        // Act
        SelectSection(cut, "Public Experience");

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Choose what anonymous visitors see after launch", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Public Experience settings section was not rendered.");
            }
        });

        await Assert.That(cut.Markup).Contains("Event catalog label", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Advanced home blocks JSON", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_RendersAuthenticationAndAuthorizationProviderSettings()
    {
        // Arrange
        _instanceOnboardingService.GetAuthProviderConfigurationAsAdminAsync()
            .Returns(new AuthProviderConfigurationDto
            {
                PrimaryProviderId = 1,
                PrimaryProviderCode = "KEYCLOAK",
                PrimaryProviderName = "Keycloak",
                KeycloakDetectedFromEnvironment = true,
                KeycloakAuthority = "https://identity.example.test/realms/event",
                KeycloakClientId = "event-bff"
            });
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto
            {
                Provider = "local",
                CerbosDetectedFromEnvironment = true,
                CerbosGrpcEndpoint = "cerbosgrpc.local:3593"
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Provider navigation item was not rendered.");
            }
        });

        // Act
        SelectSection(cut, "Authentication and Authorization Providers");

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authentication Providers", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Authorization Providers", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Provider settings sections were not rendered.");
            }
        });

        await Assert.That(cut.Markup).Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Authentication Providers", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Authorization Providers", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Keycloak", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Keycloak client-secret rotation", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Local (Built-in RBAC)", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Cerbos (External PDP)", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("Enable Cerbos Authorization", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_WhenIdentityIsNotReady_ShowsInlineRecoveryInsteadOfProviderEditor()
    {
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto());

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("admin session is still becoming ready", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected inline identity-readiness guidance.");
            }
        });

        await Assert.That(cut.Markup).Contains("Reload settings", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).Contains("sign out and back in", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("Exactly one authorization provider is active", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("Enable Cerbos Authorization", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_FailedModuleAutosave_RendersAuthoritativeValue()
    {
        _instanceOnboardingService.GetModuleSettingsAsync()
            .Returns(
                new ModuleSettingsDto { EnableIslamicModule = false },
                new ModuleSettingsDto { EnableIslamicModule = false });
        _instanceOnboardingService.UpdateModuleSettingsAsync(Arg.Any<ModuleSettingsDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = "Module update failed." });
        var cut = RenderInstanceAdminSettingsLayout();
        cut.WaitForState(() => cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase));
        SelectSection(cut, "Modules");
        var moduleSwitch = cut.FindComponents<MudSwitch<bool>>()
            .Single(component => component.Markup.Contains("Enable Islamic module", StringComparison.Ordinal));

        await cut.InvokeAsync(() => moduleSwitch.Instance.ValueChanged.InvokeAsync(true));
        cut.WaitForAssertion(() =>
        {
            var restored = cut.FindComponents<MudSwitch<bool>>()
                .Single(component => component.Markup.Contains("Enable Islamic module", StringComparison.Ordinal));
            if (restored.Instance.Value)
            {
                throw new InvalidOperationException("The failed autosave did not render the authoritative module value.");
            }
        });

        await _instanceOnboardingService.Received(2).GetModuleSettingsAsync();
        await Assert.That(cut.Find("[role='alert']").TextContent).Contains("Module update failed.", StringComparison.Ordinal);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_OrdinaryPolicyAndAiSections_HaveNoBroadSaveButton()
    {
        _instanceOnboardingService.GetAiAssistantGovernanceSettingsAsync()
            .Returns(new AiAssistantGovernanceSettingsDto
            {
                Enabled = true,
                Provider = "openai-compatible",
                EndpointUrl = "https://ai.example.test/v1",
                ModelId = "model-a"
            });
        var cut = RenderInstanceAdminSettingsLayout();
        cut.WaitForState(() => cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase));
        SelectSection(cut, "Policies");
        await Assert.That(cut.Markup).DoesNotContain("Save Settings", StringComparison.Ordinal);

        SelectSection(cut, "AI");
        await Assert.That(cut.Markup).DoesNotContain("Save Settings", StringComparison.Ordinal);
        await Assert.That(cut.Markup).Contains("Save provider configuration", StringComparison.Ordinal);
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_FailedGovernanceAutosave_RendersAuthoritativeValue()
    {
        _instanceOnboardingService.GetMcpGovernanceSettingsAsync()
            .Returns(
                new McpGovernanceSettingsDto { Enabled = false },
                new McpGovernanceSettingsDto { Enabled = false });
        _instanceOnboardingService.UpdateMcpGovernanceSettingsAsync(Arg.Any<McpGovernanceSettingsDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = "MCP update failed." });
        var cut = RenderInstanceAdminSettingsLayout();
        cut.WaitForState(() => cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase));
        SelectSection(cut, "Advanced");
        cut.WaitForState(() => cut.Markup.Contains("Enable MCP adapter at runtime", StringComparison.Ordinal));
        var mcpSwitch = cut.FindComponents<MudSwitch<bool>>()
            .Single(component => component.Markup.Contains("Enable MCP adapter at runtime", StringComparison.Ordinal));

        await cut.InvokeAsync(() => mcpSwitch.Instance.ValueChanged.InvokeAsync(true));
        cut.WaitForAssertion(() =>
        {
            var restored = cut.FindComponents<MudSwitch<bool>>()
                .Single(component => component.Markup.Contains("Enable MCP adapter at runtime", StringComparison.Ordinal));
            if (restored.Instance.Value)
            {
                throw new InvalidOperationException("The failed governance autosave did not render the authoritative MCP value.");
            }
        });

        await _instanceOnboardingService.Received(2).GetMcpGovernanceSettingsAsync();
        await Assert.That(cut.FindAll("[role='alert']").Any(alert =>
            alert.TextContent.Contains("MCP update failed.", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_FailedAiAutosave_RendersAuthoritativeValue()
    {
        _instanceOnboardingService.GetAiAssistantGovernanceSettingsAsync()
            .Returns(
                new AiAssistantGovernanceSettingsDto { Enabled = false },
                new AiAssistantGovernanceSettingsDto { Enabled = false });
        _instanceOnboardingService.UpdateAiAssistantGovernanceSettingsAsync(Arg.Any<AiAssistantGovernanceSettingsDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = false, Message = "AI update failed." });
        var cut = RenderInstanceAdminSettingsLayout();
        cut.WaitForState(() => cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase));
        SelectSection(cut, "AI");
        var aiSwitch = cut.FindComponents<MudSwitch<bool>>()
            .Single(component => component.Markup.Contains("Enable AI Assistant", StringComparison.Ordinal));

        await cut.InvokeAsync(() => aiSwitch.Instance.ValueChanged.InvokeAsync(true));
        cut.WaitForAssertion(() =>
        {
            var restored = cut.FindComponents<MudSwitch<bool>>()
                .Single(component => component.Markup.Contains("Enable AI Assistant", StringComparison.Ordinal));
            if (restored.Instance.Value)
            {
                throw new InvalidOperationException("The failed AI autosave did not render the authoritative value.");
            }
        });

        await _instanceOnboardingService.Received(2).GetAiAssistantGovernanceSettingsAsync();
        await Assert.That(cut.FindAll("[role='alert']").Any(alert =>
            alert.TextContent.Contains("AI update failed.", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_DeploymentManagedAuthorization_KeepsAuthenticationSaveAvailable()
    {
        _instanceOnboardingService.GetAuthProviderConfigurationAsAdminAsync()
            .Returns(new AuthProviderConfigurationDto { PrimaryProviderId = 1, GoogleSsoEnabled = true });
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto
            {
                Provider = "local",
                AuthorizationProviderManagedByDeployment = true,
                AuthorizationProviderBootstrapStatus = "ready"
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderAuthProvidersSection();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authorization is managed by deployment configuration", StringComparison.OrdinalIgnoreCase)
                || cut.Markup.Contains("Enable Cerbos Authorization", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected read-only deployment authorization with authentication controls intact.");
            }
        });

        cut.FindAll("button")
            .Single(button => button.TextContent.Contains("Save Settings", StringComparison.OrdinalIgnoreCase))
            .Click();

        await _instanceOnboardingService.Received(1)
            .UpdateAuthProviderConfigurationAsAdminAsync(Arg.Any<AuthProviderConfigurationDto>());
        await _instanceOnboardingService.DidNotReceive()
            .UpdateAuthorizationProviderConfigurationAsAdminAsync(Arg.Any<AuthorizationProviderConfigurationDto>());
        await _instanceOnboardingService.Received(1).RefreshAuthSchemesAsync();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_CerbosPolicySync_InvokesAdminBffSync()
    {
        // Arrange
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto
            {
                Provider = "cerbos",
                CerbosGrpcEndpoint = "cerbosgrpc.local:3593",
                CerbosEndpointVerified = true,
                CerbosAdminUsernameConfigured = true,
                CerbosAdminPasswordConfigured = true
            });
        _instanceOnboardingService.SyncAuthorizationPolicyPackageAsAdminAsync()
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "Authorization policy package synced."
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderAuthProvidersSection();

        // Act
        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Sync Policies", StringComparison.OrdinalIgnoreCase))
            .Click();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authorization policy package synced.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Policy sync success message was not rendered.");
            }
        });
        await _instanceOnboardingService.Received(1).SyncAuthorizationPolicyPackageAsAdminAsync();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_CerbosPolicySync_FailureShowsSafeMessage()
    {
        // Arrange
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto
            {
                Provider = "cerbos",
                CerbosGrpcEndpoint = "cerbosgrpc.local:3593",
                CerbosEndpointVerified = true,
                CerbosAdminUsernameConfigured = true,
                CerbosAdminPasswordConfigured = true
            });
        _instanceOnboardingService.SyncAuthorizationPolicyPackageAsAdminAsync()
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "secret-token leaked by backend",
                Errors = ["stack trace"]
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderAuthProvidersSection();

        // Act
        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Sync Policies", StringComparison.OrdinalIgnoreCase))
            .Click();

        // Assert
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authorization policy package sync failed. Check Admin API settings and retry.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Safe policy sync failure message was not rendered.");
            }
        });
        await Assert.That(cut.Markup).DoesNotContain("secret-token", StringComparison.OrdinalIgnoreCase);
        await Assert.That(cut.Markup).DoesNotContain("stack trace", StringComparison.OrdinalIgnoreCase);
        await _instanceOnboardingService.Received(1).SyncAuthorizationPolicyPackageAsAdminAsync();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_CerbosPolicySync_DisabledUntilEndpointVerified()
    {
        // Arrange
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto
            {
                Provider = "cerbos",
                CerbosGrpcEndpoint = "cerbosgrpc.local:3593",
                CerbosEndpointVerified = false
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderAuthProvidersSection();

        // Act
        var syncButton = cut.FindAll("button")
            .First(button => button.TextContent.Contains("Sync Policies", StringComparison.OrdinalIgnoreCase));

        // Assert
        await Assert.That(syncButton.HasAttribute("disabled")).IsTrue();
        await Assert.That(cut.Markup).Contains("Save and verify the Cerbos endpoint before syncing authorization policies.", StringComparison.OrdinalIgnoreCase);
        await _instanceOnboardingService.DidNotReceive().SyncAuthorizationPolicyPackageAsAdminAsync();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_DeploymentManagedCerbosFailure_EnablesServerRetry()
    {
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto
            {
                Provider = "cerbos",
                AuthorizationProviderManagedByDeployment = true,
                AuthorizationProviderBootstrapStatus = "failed",
                AuthorizationProviderBootstrapMessage = "The deployment-managed Cerbos PDP endpoint could not be reached.",
                CerbosGrpcEndpoint = "http://cerbos:3593",
                CerbosEndpointVerified = false,
                CerbosEndpointOwnership = new SecretOwnershipDto
                {
                    Mode = "deployment-managed",
                    Badge = "Managed by Deployment",
                    Editable = false
                }
            });
        _instanceOnboardingService.SyncAuthorizationPolicyPackageAsAdminAsync()
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = false,
                Message = "Automatic Cerbos setup did not complete."
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderAuthProvidersSection();
        var retryButton = cut.FindAll("button")
            .First(button => button.TextContent.Contains("Retry Authorization Setup", StringComparison.OrdinalIgnoreCase));

        await Assert.That(retryButton.HasAttribute("disabled")).IsFalse();
        retryButton.Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authorization policy package sync failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected safe retry failure feedback.");
            }
        });
        await _instanceOnboardingService.Received(1).SyncAuthorizationPolicyPackageAsAdminAsync();
    }

    [Test]
    public async Task InstanceAdminSettingsLayout_DeploymentManagedCerbosRetry_RefreshesReadiness()
    {
        var failed = new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            AuthorizationProviderManagedByDeployment = true,
            AuthorizationProviderBootstrapStatus = "failed",
            CerbosGrpcEndpoint = "http://cerbos:3593",
            CerbosEndpointVerified = false,
            CerbosEndpointOwnership = new SecretOwnershipDto
            {
                Mode = "deployment-managed",
                Badge = "Managed by Deployment",
                Editable = false
            }
        };
        var ready = new AuthorizationProviderConfigurationDto
        {
            Provider = "cerbos",
            AuthorizationProviderManagedByDeployment = true,
            AuthorizationProviderConfigured = true,
            AuthorizationProviderBootstrapStatus = "ready",
            CerbosGrpcEndpoint = "http://cerbos:3593",
            CerbosEndpointVerified = true,
            CerbosPoliciesSynchronized = true,
            CerbosEndpointOwnership = failed.CerbosEndpointOwnership
        };
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(failed, ready);
        _instanceOnboardingService.SyncAuthorizationPolicyPackageAsAdminAsync()
            .Returns(new BaseCommandResponseOfGuid
            {
                Success = true,
                Message = "Cerbos endpoint verification and policy synchronization completed."
            });

        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderAuthProvidersSection();
        cut.FindAll("button")
            .First(button => button.TextContent.Contains("Retry Authorization Setup", StringComparison.OrdinalIgnoreCase))
            .Click();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Verified", StringComparison.OrdinalIgnoreCase)
                || !cut.Markup.Contains("Cerbos endpoint verification and policy synchronization completed.", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Expected refreshed deployment readiness after a successful retry.");
            }
        });
        await _instanceOnboardingService.Received(2)
            .GetAuthorizationProviderConfigurationAsAdminAsync();
    }

    private void ConfigureSingleTenantInstanceDefaults()
    {
        _instanceOnboardingService.GetStatusAsync()
            .Returns(new InstanceOnboardingStatusDto
            {
                IsCompleted = true,
                IsAuthenticated = true,
                IsCurrentUserInstanceAdmin = true,
                SelectedDeploymentMode = "SingleTenant"
            });
        _userService.GetAdminAuthorityAsync()
            .Returns(new AdminAuthorityDto
            {
                IsInstanceAdmin = true,
                AdminTenantIds = [AuthenticationTestConstants.DefaultTenantId],
                AdminOrganizationIds = [],
                HasAnyAuthority = true
            });
        _instanceOnboardingService.GetDeploymentModeAsync()
            .Returns(new DeploymentModeDto { Mode = DeploymentMode.SingleTenant });
        _instanceOnboardingService.GetModuleSettingsAsync().Returns(new ModuleSettingsDto());
        _instanceOnboardingService.GetEventPolicyAsync().Returns(new EventPolicyDto());
        _instanceOnboardingService.GetOrganizationPolicyAsync().Returns(new OrganizationPolicyDto());
        _instanceOnboardingService.GetBrandingSettingsAsync().Returns(new BrandingSettingsDto());
        _instanceOnboardingService.GetDomainSettingsAsync().Returns(new DomainSettingsDto());
        _instanceOnboardingService.GetTenantDelegationAsync().Returns(new TenantDelegationSettingsDto());
        _instanceOnboardingService.GetRenderPolicyAsync().Returns(new RenderPolicySettingsDto());
        _instanceOnboardingService.GetStorageSettingsAsync().Returns(new HalResourceOfInstanceStorageSettingsDto());
        _instanceOnboardingService.GetSmtpSettingsAsync().Returns(new HalResourceOfInstanceSmtpSettingsDto());
        _instanceOnboardingService.GetAuthProviderConfigurationAsAdminAsync().Returns(new AuthProviderConfigurationDto());
        _instanceOnboardingService.GetAuthorizationProviderConfigurationAsAdminAsync()
            .Returns(new AuthorizationProviderConfigurationDto { Provider = "local" });
        _instanceOnboardingService.UpdateAuthProviderConfigurationAsAdminAsync(Arg.Any<AuthProviderConfigurationDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        _instanceOnboardingService.UpdateAuthorizationProviderConfigurationAsAdminAsync(Arg.Any<AuthorizationProviderConfigurationDto>())
            .Returns(new BaseCommandResponseOfGuid { Success = true });
        _instanceOnboardingService.RefreshAuthSchemesAsync().Returns(Task.CompletedTask);
        _instanceOnboardingService.RefreshAuthSessionAsync().Returns(true);
        _instanceOnboardingService.GetAnalyticsGovernanceSettingsAsync()
            .Returns(new AnalyticsGovernanceSettingsDto());
        _instanceOnboardingService.GetFooterGovernanceSettingsAsync().Returns(new FooterGovernanceSettingsDto());
        _instanceOnboardingService.GetActiveTenantCountAsync().Returns(1);

        _tenantOnboardingService.GetSettingsAsync().Returns(new TenantPolicySettingsDto());
        _publicExperienceAdminService.ApplySingleTenantPolicySettingsAsync(
                Arg.Any<TenantPolicySettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _publicExperienceAdminService.ApplyAnnouncementBarSettingsAsync(
                Arg.Any<TenantPolicySettingsDto>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        _publicExperienceAdminService.GetSettingsAsync(Arg.Any<CancellationToken>())
            .Returns(new TenantPublicExperienceAdminModel());
        _organizationService.GetOrganizationsPagedAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(PaginatedResult<OrganizationListDto>.Empty());
    }

    private IRenderedComponent<InstanceAdminSettingsLayout> RenderAuthProvidersSection()
    {
        IRenderedComponent<InstanceAdminSettingsLayout> cut = RenderInstanceAdminSettingsLayout();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Authentication and Authorization Providers", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Provider navigation item was not rendered.");
            }
        });

        SelectSection(cut, "Authentication and Authorization Providers");

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Exactly one authorization provider is active", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Authorization provider settings were not rendered.");
            }
        });

        return cut;
    }

    private static void SelectSection(IRenderedComponent<InstanceAdminSettingsLayout> cut, string label) =>
        cut.FindAll("[role='option']")
            .Single(item => item.TextContent.Trim() == label)
            .Click();
}
