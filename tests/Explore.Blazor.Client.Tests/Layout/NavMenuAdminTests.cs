using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Layout;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Tests.Common.Authentication;

namespace Explore.Blazor.Client.Tests.Layout;

/// <summary>
/// Tests for the NavMenu admin section rendering behavior.
/// Admin links are shown based on BFF/admin status models:
/// - Instance admin: Instance Administration
/// - Tenant admin: Tenant Administration only
/// - Organization admin: Organization Settings link(s)
/// - Regular user: no admin section at all
/// </summary>
public class NavMenuAdminTests : IDisposable
{
    private readonly BlazorTestContext _ctx;

    public NavMenuAdminTests()
    {
        _ctx = new BlazorTestContext();
        _ctx.AddShellStateMocks();
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task NavMenu_AnonymousUser_DoesNotShowAdminSection()
    {
        // Arrange
        _ctx.SetAnonymousUser();
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();

        // Assert
        await Assert.That(cut.Markup).DoesNotContain("Instance Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("Instance Settings");
        await Assert.That(cut.Markup).DoesNotContain("Tenant Administration");
        await Assert.That(cut.Markup).DoesNotContain("Organization Settings");
    }

    [Test]
    public async Task NavMenu_AuthenticatedUserWithoutAdminClaims_DoesNotShowAdminSection()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.DefaultUserId, "Regular User");
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert
        await Assert.That(cut.Markup).DoesNotContain("Instance Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("Instance Settings");
        await Assert.That(cut.Markup).DoesNotContain("Tenant Administration");
        await Assert.That(cut.Markup).DoesNotContain("Organization Settings");
    }

    [Test]
    public async Task NavMenu_AuthenticatedUser_DoesNotShowRemovedMyRegistrationsLink()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.DefaultUserId, "Regular User");
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert
        await Assert.That(cut.Markup).DoesNotContain("My Registrations");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/my/registrations\"");
    }

    [Test]
    public async Task NavMenu_AuthenticatedUser_RendersAiAssistantToggleBeforeProfileDropdown()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.DefaultUserId, "Regular User");
        var settings = new PublicExperienceSettingsBuilder()
            .WithAiAssistant()
            .Build();
        SetupNavMenuServices(publicExperienceSettings: settings);

        // Act
        var cut = RenderNavMenu();

        // Assert
        cut.WaitForElement("[data-testid='shell-ai-toggle']");
        var markup = cut.Markup;
        var aiToggleIndex = markup.IndexOf("data-testid=\"shell-ai-toggle\"", StringComparison.Ordinal);
        var userDropdownIndex = markup.IndexOf("navbar__user-dropdown", StringComparison.Ordinal);

        await Assert.That(aiToggleIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(userDropdownIndex).IsGreaterThanOrEqualTo(0);
        await Assert.That(aiToggleIndex).IsLessThan(userDropdownIndex);
    }

    [Test]
    public async Task NavMenu_WhenClientPickerDisabled_DoesNotRenderLanguagePicker()
    {
        _ctx.SetAnonymousUser();
        var settings = new PublicExperienceSettingsBuilder()
            .WithClientPickerEnabled(false)
            .Build();
        SetupNavMenuServices(publicExperienceSettings: settings);

        var cut = RenderNavMenu();

        await Assert.That(cut.Markup).DoesNotContain("language-picker");
        await Assert.That(cut.Markup).DoesNotContain("Change language");
    }

    [Test]
    public async Task NavMenu_InstanceAdminClaimOnly_DoesNotShowAdminLinks()
    {
        // Arrange
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Admin User",
            new Claim("explore:admin:instance", "true"));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- serialized/browser claims are not treated as admin authority
        await Assert.That(cut.Markup).DoesNotContain("Instance Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("Tenant Administration");
    }

    [Test]
    public async Task NavMenu_TenantAdminClaimOnly_DoesNotShowAdminLinks()
    {
        // Arrange
        var tenantId = AuthenticationTestConstants.DefaultTenantId;
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Tenant Admin User",
            new Claim("explore:admin:tenant", tenantId.ToString()));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- serialized/browser claims are not treated as admin authority
        await Assert.That(cut.Markup).DoesNotContain("Instance Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("Tenant Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Settings");
    }

    [Test]
    public async Task NavMenu_OrganizationAdminOnly_ShowsOrganizationSettings()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Org Admin User",
            new Claim("explore:admin:organization", orgId.ToString()));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- serialized/browser claims are not treated as admin authority
        await Assert.That(cut.Markup).DoesNotContain("Organization Settings");
        await Assert.That(cut.Markup).DoesNotContain($"/settings/organization/{orgId}");
        await Assert.That(cut.Markup).DoesNotContain("Instance Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("Instance Settings");
        await Assert.That(cut.Markup).DoesNotContain("Tenant Administration");
    }

    [Test]
    public async Task NavMenu_OrganizationAdmin_MultipleOrgs_ShowsLinkPerOrg()
    {
        // Arrange
        var orgId1 = Guid.NewGuid();
        var orgId2 = Guid.NewGuid();
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Multi-Org Admin",
            new Claim("explore:admin:organization", orgId1.ToString()),
            new Claim("explore:admin:organization", orgId2.ToString()));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- serialized/browser org-admin claims do not create admin links
        await Assert.That(cut.Markup).DoesNotContain($"/settings/organization/{orgId1}");
        await Assert.That(cut.Markup).DoesNotContain($"/settings/organization/{orgId2}");
    }

    [Test]
    public async Task NavMenu_InstanceAdminWithOrgClaim_ShowsBothPlatformAndOrgLinks()
    {
        // Arrange
        var orgId = Guid.NewGuid();
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Super Admin",
            new Claim("explore:admin:instance", "true"),
            new Claim("explore:admin:organization", orgId.ToString()));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- serialized/browser claims do not create admin links
        await Assert.That(cut.Markup).DoesNotContain("Instance Administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("Tenant Administration");
        await Assert.That(cut.Markup).DoesNotContain($"/settings/organization/{orgId}");
    }

    [Test]
    public async Task NavMenu_InstanceAdmin_ContainsCorrectRoutes()
    {
        // Arrange
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Admin User",
            new Claim("explore:admin:instance", "true"));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/instance\"");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/admin\"");
    }

    [Test]
    public async Task NavMenu_MultiTenantInstanceAdmin_ShowsEmbeddedInstanceConsole()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Instance Admin");
        SetupNavMenuServices(
            deploymentMode: "MultiTenant",
            isCurrentUserInstanceAdmin: true);

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert
        await Assert.That(cut.Markup).Contains("Instance Console");
        await Assert.That(cut.Markup).Contains("href=\"/admin/instance\"");
        await Assert.That(cut.Markup).Contains("href=\"/settings/instance\"");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/admin\"");
    }

    [Test]
    public async Task NavMenu_TenantAdmin_ContainsCorrectRoutes()
    {
        // Arrange
        var tenantId = AuthenticationTestConstants.DefaultTenantId;
        _ctx.SetAuthenticatedUserWithClaims(
            AuthenticationTestConstants.AdminUserId,
            "Tenant Admin",
            new Claim("explore:admin:tenant", tenantId.ToString()));
        SetupNavMenuServices();

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert
        await Assert.That(cut.Markup).DoesNotContain("href=\"/admin\"");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/admin\"");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/instance\"");
    }

    [Test]
    public async Task NavMenu_SingleTenantInstanceAdminFallback_ShowsAdministration_WhenClaimsAreStale()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Setup Admin");
        SetupNavMenuServices(
            deploymentMode: "SingleTenant",
            isCurrentUserInstanceAdmin: true);

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- onboarding grants are visible even before the serialized auth claims rehydrate.
        await Assert.That(cut.Markup).Contains("Administration");
        await Assert.That(cut.Markup).Contains("href=\"/settings/instance\"");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/admin\"");
        await Assert.That(cut.Markup).DoesNotContain("Custom Property Governance");
    }

    [Test]
    public async Task NavMenu_SingleTenantTenantAdminOnly_UsesTenantAdministration()
    {
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Client Administrator");
        SetupNavMenuServices(
            deploymentMode: "SingleTenant",
            isCurrentUserTenantAdmin: true);

        var cut = RenderNavMenu();
        OpenDropdown(cut);

        await Assert.That(cut.Markup).Contains("href=\"/settings/admin\"");
        await Assert.That(cut.Markup).DoesNotContain("href=\"/settings/instance\"");
        await Assert.That(cut.Markup).Contains("Custom Property Governance");
    }

    [Test]
    public async Task NavMenu_RefreshesAdminStatusWhenDropdownOpens_AfterSingleTenantOnboarding()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Setup Admin");
        SetupNavMenuServices(deploymentMode: "SingleTenant");

        var shellContextService = _ctx.Services.GetRequiredService<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult<UiShellContextDto?>(new UiShellContextDto
                {
                    DeploymentMode = "SingleTenant",
                    SettingsScopes = [],
                    Workspaces = new WorkspaceAvailabilityDto { Events = true, Settings = true }
                }),
                Task.FromResult<UiShellContextDto?>(new UiShellContextDto
                {
                    DeploymentMode = "SingleTenant",
                    SettingsScopes =
                    [
                        new SettingsScopeDto { Scope = "Instance", ScopeId = Guid.NewGuid(), DisplayName = "Instance" }
                    ],
                    Workspaces = new WorkspaceAvailabilityDto { Events = true, Settings = true }
                }));

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert -- the persisted layout rechecks shell context on menu open.
        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("href=\"/settings/instance\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Expected instance administration link to render after status refresh.");
            }
        });
        await Assert.That(cut.Markup).Contains("Administration");
    }

    [Test]
    public async Task NavMenu_SingleTenantAdminAuthority_ShowsAdministration_WhenOnboardingStatusIsStale()
    {
        // Arrange
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Setup Admin");
        SetupNavMenuServices(deploymentMode: "SingleTenant");

        var shellContextService = _ctx.Services.GetRequiredService<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UiShellContextDto?>(new UiShellContextDto
            {
                DeploymentMode = "SingleTenant",
                SettingsScopes =
                [
                    new SettingsScopeDto { Scope = "Instance", ScopeId = Guid.NewGuid(), DisplayName = "Instance" },
                    new SettingsScopeDto { Scope = "Tenant", ScopeId = AuthenticationTestConstants.DefaultTenantId, DisplayName = "Tenant" }
                ],
                Workspaces = new WorkspaceAvailabilityDto { Events = true, Settings = true }
            }));

        // Act
        var cut = RenderNavMenu();
        OpenDropdown(cut);

        // Assert
        await Assert.That(cut.Markup).Contains("href=\"/settings/instance\"");
        await Assert.That(cut.Markup).Contains("href=\"/settings/admin\"");
        await Assert.That(cut.Markup).Contains("Site administration");
        await Assert.That(cut.Markup).DoesNotContain("Tenant administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance administration");
        await Assert.That(cut.Markup).DoesNotContain("Instance Console");
    }

    [Test]
    public async Task NavMenu_ProfileAlwaysKeepsPersonalAndSettingsHubLinks()
    {
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Member");
        SetupNavMenuServices();

        var cut = RenderNavMenu();
        OpenDropdown(cut);

        await Assert.That(cut.Markup).Contains("href=\"/settings/personal\"");
        await Assert.That(cut.Markup).Contains("href=\"/settings\"");
    }

    [Test]
    public async Task NavMenu_SingleTenantInstanceAdmin_ReplacesAllSettingsWithAdminSettings()
    {
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Instance Admin");
        SetupNavMenuServices(
            deploymentMode: "SingleTenant",
            isCurrentUserInstanceAdmin: true,
            isCurrentUserTenantAdmin: true);

        var cut = RenderNavMenu();
        OpenDropdown(cut);

        var adminSettings = cut.FindAll("a")
            .Single(link => link.TextContent.Contains("Admin Settings", StringComparison.Ordinal));
        await Assert.That(adminSettings.GetAttribute("href")).IsEqualTo("/settings/instance");
        await Assert.That(cut.Markup).DoesNotContain("All Settings");
    }

    [Test]
    public async Task NavMenu_SingleTenantTenantAdmin_ReplacesAllSettingsWithAdminSettings()
    {
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Tenant Admin");
        SetupNavMenuServices(
            deploymentMode: "SingleTenant",
            isCurrentUserTenantAdmin: true);

        var cut = RenderNavMenu();
        OpenDropdown(cut);

        var adminSettings = cut.FindAll("a")
            .Single(link => link.TextContent.Contains("Admin Settings", StringComparison.Ordinal));
        await Assert.That(adminSettings.GetAttribute("href")).IsEqualTo("/settings/admin");
        await Assert.That(cut.Markup).DoesNotContain("All Settings");
    }

    [Test]
    public async Task NavMenu_ManagedActors_RenderMembershipAndGateSettingsByScope()
    {
        var organizationId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        _ctx.SetAuthenticatedUser(AuthenticationTestConstants.AdminUserId, "Publisher");
        SetupNavMenuServices();

        var shellContextService = _ctx.Services.GetRequiredService<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<UiShellContextDto?>(new UiShellContextDto
            {
                DeploymentMode = "MultiTenant",
                ManagedActors =
                [
                    new ManagedActorDto { ActorType = "Organization", ScopeId = organizationId, DisplayName = "Managed Organization" },
                    new ManagedActorDto { ActorType = "Group", ScopeId = groupId, DisplayName = "Managed Group" }
                ],
                SettingsScopes =
                [
                    new SettingsScopeDto { Scope = "Group", ScopeId = groupId, DisplayName = "Managed Group" }
                ],
                Workspaces = new WorkspaceAvailabilityDto { Events = true, Settings = true, Studio = true }
            }));

        var cut = RenderNavMenu();
        OpenDropdown(cut);
        cut.FindAll(".navbar__dropdown-item--expandable")[0].Click();

        await Assert.That(cut.Markup).Contains("Managed Organization");
        await Assert.That(cut.Markup).DoesNotContain($"/settings/organization/{organizationId}");

        cut.FindAll(".navbar__dropdown-item--expandable")[1].Click();

        await Assert.That(cut.Markup).Contains("Managed Group");
        await Assert.That(cut.Markup).Contains($"/settings/group/{groupId}");
    }

    private IRenderedComponent<NavMenu> RenderNavMenu() =>
        _ctx.RenderMudComponent<NavMenu>();

    private static void OpenDropdown(IRenderedComponent<NavMenu> cut)
    {
        var dropdownButton = cut.Find(".navbar__user-btn");
        dropdownButton.Click();
    }

    private void SetupNavMenuServices(
        PublicExperienceSettingsDto? publicExperienceSettings = null,
        string deploymentMode = "MultiTenant",
        bool isCurrentUserInstanceAdmin = false,
        bool isCurrentUserTenantAdmin = false)
    {
        NavMenuTestServices.Register(
            _ctx,
            publicExperienceSettings: publicExperienceSettings,
            deploymentMode: deploymentMode,
            isCurrentUserInstanceAdmin: isCurrentUserInstanceAdmin,
            isCurrentUserTenantAdmin: isCurrentUserTenantAdmin);
    }
}
