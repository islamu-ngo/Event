using Explore.API.Hateoas;
using Explore.API.Hateoas.Policies;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Hateoas;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Api.IntegrationTests.Features.Hateoas;

public sealed class OnboardingStatusLinkPolicyTests
{
    [Test]
    public async Task Instance_status_always_includes_self()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: false, isSecretValid: false);

        var links = policy.GetLinks(new InstanceOnboardingStatusDto(), user: null).ToArray();
        var self = links.Single(link => link.Rel == LinkRelations.Self);

        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetInstanceOnboardingStatus);
    }

    [Test]
    public async Task Valid_setup_authority_includes_provider_and_completion_affordances()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: true, isSecretValid: true);
        var status = new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = false
        };

        var links = policy.GetLinks(status, user: null).ToArray();
        var authentication = links.Single(link => link.Rel == "manage-authentication");
        var authorization = links.Single(link => link.Rel == "manage-authorization");
        var saveProfile = links.Single(link => link.Rel == "save-profile");
        var complete = links.Single(link => link.Rel == "complete");

        await Assert.That(authentication.RouteName)
            .IsEqualTo(RouteNames.GetInstanceOnboardingAuthProviderConfigurationInternal);
        await Assert.That(authentication.Method).IsEqualTo("GET");
        await Assert.That(authentication.RequiresAuth).IsFalse();
        await Assert.That(authentication.PermissionAction).IsNull();
        await Assert.That(authorization.RouteName)
            .IsEqualTo(RouteNames.GetInstanceOnboardingAuthorizationProviderConfigurationInternal);
        await Assert.That(authorization.Method).IsEqualTo("GET");
        await Assert.That(authorization.RequiresAuth).IsFalse();
        await Assert.That(authorization.PermissionAction).IsNull();
        await Assert.That(saveProfile.RouteName).IsEqualTo("SaveInstanceOnboardingProfile");
        await Assert.That(saveProfile.Method).IsEqualTo("PATCH");
        await Assert.That(saveProfile.RequiresAuth).IsTrue();
        await Assert.That(complete.RouteName).IsEqualTo(RouteNames.CompleteInstanceOnboarding);
        await Assert.That(complete.Method).IsEqualTo("POST");
        await Assert.That(complete.RequiresAuth).IsTrue();
    }

    [Test]
    public async Task Valid_setup_authority_but_unauthenticated_omits_save_profile_and_complete_affordances()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: true, isSecretValid: true);
        var status = new InstanceOnboardingStatusDto
        {
            IsAuthenticated = false,
            IsCompleted = false
        };

        var links = policy.GetLinks(status, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == "manage-authentication")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "manage-authorization")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "save-profile")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "complete")).IsFalse();
    }

    [Test]
    public async Task Invalid_setup_secret_suppresses_provider_and_completion_affordances()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: true, isSecretValid: false);
        var status = new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = false
        };

        var links = policy.GetLinks(status, user: null).ToArray();

        await Assert.That(links.Length).IsEqualTo(1);
        await Assert.That(links[0].Rel).IsEqualTo(LinkRelations.Self);
    }

    [Test]
    public async Task Completed_instance_admin_gets_permission_checked_provider_management()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: false, isSecretValid: false);
        var status = new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = true,
            IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = "SingleTenant"
        };

        var links = policy.GetLinks(status, user: null).ToArray();
        var authentication = links.Single(link => link.Rel == "manage-authentication");
        var authorization = links.Single(link => link.Rel == "manage-authorization");

        await AssertInstanceSettingsViewLink(
            authentication,
            RouteNames.GetInstanceAuthProviderConfiguration);
        await AssertInstanceSettingsViewLink(
            authorization,
            RouteNames.GetInstanceAuthorizationProviderConfiguration);
        await Assert.That(links.Any(link => link.Rel == "complete")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "manage-tenants")).IsFalse();
    }

    [Test]
    public async Task Completed_instance_admin_omits_setup_save_profile_but_keeps_management_links()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: false, isSecretValid: false);
        var status = new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = true,
            IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = "SingleTenant"
        };

        var links = policy.GetLinks(status, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == "save-profile")).IsFalse();
        await Assert.That(links.Any(link => link.Rel == "manage-authentication")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "manage-authorization")).IsTrue();
    }

    [Test]
    public async Task Completed_multi_tenant_instance_admin_gets_manage_tenants_affordance()
    {
        var policy = CreateInstancePolicy(isSetupModeActive: false, isSecretValid: false);
        var status = new InstanceOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = true,
            IsCurrentUserInstanceAdmin = true,
            SelectedDeploymentMode = "MultiTenant"
        };

        var links = policy.GetLinks(status, user: null).ToArray();
        var manageTenants = links.Single(link => link.Rel == "manage-tenants");

        await AssertInstanceSettingsViewLink(manageTenants, RouteNames.GetControlPlaneTenants);
    }

    [Test]
    public async Task Authenticated_tenant_status_includes_self()
    {
        var tenantId = Guid.NewGuid();
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            TenantId = tenantId
        };

        var links = new TenantOnboardingStatusLinkPolicy().GetLinks(status, user: null).ToArray();
        var self = links.Single(link => link.Rel == LinkRelations.Self);

        await Assert.That(self.RouteName).IsEqualTo(RouteNames.GetTenantOnboardingStatus);
        await Assert.That(self.Method).IsEqualTo("GET");
        await Assert.That(self.RequiresAuth).IsTrue();
    }

    [Test]
    public async Task Incomplete_tenant_admin_gets_scoped_manage_and_complete_affordances()
    {
        var tenantId = Guid.NewGuid();
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = false,
            IsCurrentUserTenantAdministrator = true,
            TenantId = tenantId
        };

        var links = new TenantOnboardingStatusLinkPolicy().GetLinks(status, user: null).ToArray();
        var manage = links.Single(link => link.Rel == "manage-tenant-settings");
        var complete = links.Single(link => link.Rel == "complete");

        await AssertTenantSettingLink(manage, "GET", AuthorizationActions.TenantSettings.Update, tenantId);
        await Assert.That(manage.RouteName).IsEqualTo(RouteNames.GetTenantOnboardingPolicySettings);
        await AssertTenantSettingLink(complete, "POST", AuthorizationActions.TenantSettings.Update, tenantId);
        await Assert.That(complete.RouteName).IsEqualTo(RouteNames.CompleteTenantOnboarding);
    }

    [Test]
    public async Task Incomplete_platform_admin_gets_control_plane_manage_and_complete_affordances()
    {
        var tenantId = Guid.NewGuid();
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = false,
            IsCurrentUserPlatformAdministrator = true,
            TenantId = tenantId
        };

        var links = new TenantOnboardingStatusLinkPolicy().GetLinks(status, user: null).ToArray();
        var manage = links.Single(link => link.Rel == "manage-control-plane");
        var complete = links.Single(link => link.Rel == "complete");

        await AssertInstanceSettingLink(
            manage,
            RouteNames.GetControlPlaneTenantById,
            "GET",
            AuthorizationActions.InstanceSettings.View);
        await AssertInstanceSettingLink(
            complete,
            RouteNames.CompleteTenantOnboarding,
            "POST",
            AuthorizationActions.InstanceSettings.Update);
    }

    [Test]
    public async Task Completed_tenant_status_suppresses_completion_affordance()
    {
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCompleted = true,
            IsCurrentUserTenantAdministrator = true,
            TenantId = Guid.NewGuid()
        };

        var links = new TenantOnboardingStatusLinkPolicy().GetLinks(status, user: null).ToArray();

        await Assert.That(links.Any(link => link.Rel == "manage-tenant-settings")).IsTrue();
        await Assert.That(links.Any(link => link.Rel == "complete")).IsFalse();
    }

    [Test]
    public async Task Unauthenticated_tenant_status_suppresses_action_links()
    {
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = false,
            IsCurrentUserTenantAdministrator = true,
            IsCurrentUserPlatformAdministrator = true,
            TenantId = Guid.NewGuid()
        };

        var links = new TenantOnboardingStatusLinkPolicy().GetLinks(status, user: null).ToArray();

        await Assert.That(links.Length).IsEqualTo(1);
        await Assert.That(links[0].Rel).IsEqualTo(LinkRelations.Self);
    }

    [Test]
    public async Task Empty_tenant_context_suppresses_action_links()
    {
        var status = new TenantOnboardingStatusDto
        {
            IsAuthenticated = true,
            IsCurrentUserTenantAdministrator = true,
            IsCurrentUserPlatformAdministrator = true,
            TenantId = Guid.Empty
        };

        var links = new TenantOnboardingStatusLinkPolicy().GetLinks(status, user: null).ToArray();

        await Assert.That(links.Length).IsEqualTo(1);
        await Assert.That(links[0].Rel).IsEqualTo(LinkRelations.Self);
    }

    private static InstanceOnboardingStatusLinkPolicy CreateInstancePolicy(
        bool isSetupModeActive,
        bool isSecretValid)
    {
        var setupSecretProvider = Substitute.For<ISetupSecretProvider>();
        setupSecretProvider.IsSetupModeActive.Returns(isSetupModeActive);
        setupSecretProvider.ValidateSecret(Arg.Any<string>()).Returns(isSecretValid);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Setup-Secret"] = "request-secret";

        return new InstanceOnboardingStatusLinkPolicy(
            setupSecretProvider,
            new HttpContextAccessor { HttpContext = httpContext });
    }

    private static async Task AssertInstanceSettingsViewLink(LinkDefinition link, string routeName)
    {
        await Assert.That(link.RouteName).IsEqualTo(routeName);
        await Assert.That(link.Method).IsEqualTo("GET");
        await Assert.That(link.RequiresAuth).IsTrue();
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(link.PermissionAction).IsEqualTo(AuthorizationActions.InstanceSettings.View);
    }

    private static async Task AssertTenantSettingLink(
        LinkDefinition link,
        string method,
        string action,
        Guid tenantId)
    {
        await Assert.That(link.Method).IsEqualTo(method);
        await Assert.That(link.RequiresAuth).IsTrue();
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.TenantSetting);
        await Assert.That(link.PermissionAction).IsEqualTo(action);
        await Assert.That(link.PermissionResourceId).IsEqualTo($"{tenantId}:onboarding");
        await Assert.That(link.PermissionScope!.TenantId).IsEqualTo(tenantId.ToString());
    }

    private static async Task AssertInstanceSettingLink(
        LinkDefinition link,
        string routeName,
        string method,
        string action)
    {
        await Assert.That(link.RouteName).IsEqualTo(routeName);
        await Assert.That(link.Method).IsEqualTo(method);
        await Assert.That(link.RequiresAuth).IsTrue();
        await Assert.That(link.PermissionResourceKind).IsEqualTo(ResourceKinds.InstanceSetting);
        await Assert.That(link.PermissionAction).IsEqualTo(action);
        await Assert.That(link.PermissionResourceId)
            .IsEqualTo(GetControlPlaneTenantListQuery.SettingKey);
        await Assert.That(link.PermissionScope).IsNull();
    }
}
