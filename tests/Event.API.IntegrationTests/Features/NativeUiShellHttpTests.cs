using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.UiShell;
using Explore.Application.Features.UiShell.Requests.Queries;
using Explore.Application.Operations.Decorators;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeUiShellHttpTests
{
    [Test]
    public async Task Controller_ConsumesOnlyTheClosedQueryAndPreservesThePrivateAuthenticatedRoute()
    {
        var controller = typeof(UiShellController);
        await Assert.That(controller.GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType).ToArray())
            .IsEquivalentTo(new[] { typeof(IQueryHandler<GetUiShellContextRequest, UiShellContextDto>) });
        await Assert.That(controller.GetCustomAttribute<RouteAttribute>()!.Template).IsEqualTo("api/ui-shell");
        await Assert.That(controller.IsDefined(typeof(AuthorizeAttribute))).IsTrue();
        await Assert.That(controller.IsDefined(typeof(AllowAnonymousAttribute))).IsFalse();
        var action = controller.GetMethod(nameof(UiShellController.GetContext))!;
        await Assert.That(action.GetCustomAttribute<HttpGetAttribute>()!.Template).IsEqualTo("context");
        await Assert.That(action.GetCustomAttribute<HttpGetAttribute>()!.Name).IsEqualTo(RouteNames.GetUiShellContext);
    }

    [Test]
    public async Task AnonymousAndAuthenticatedWithoutUsableIdentity_ReturnUnauthorizedProblemDetails()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var anonymous = await client.GetAsync("/api/ui-shell/context");
        await UnauthorizedAsync(anonymous);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/ui-shell/context");
        request.Headers.Add(TestAuthHandler.AuthHeaderName, Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new[] { new TestAuthHandler.TestClaimDto(ClaimTypes.Name, "No platform identity") }))));
        using var missingIdentity = await client.SendAsync(request);
        await UnauthorizedAsync(missingIdentity);
    }

    [Test]
    public async Task PersistedAuthority_ProjectsOnlyTheCallersManagedActorsAndSettingsScopes()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var data = await SeedAuthorityAsync(factory);
        using var request = Request(data.Owner.UserId);
        using var response = await client.SendAsync(request);
        var shell = await ReadShellAsync(response);
        await Assert.That(shell.TenantId).IsEqualTo(data.Owner.TenantId);
        await Assert.That(shell.DeploymentMode).IsEqualTo("SingleTenant");
        await Assert.That(shell.Workspaces.Studio).IsTrue();
        await Assert.That(shell.ManagedActors.Select(actor => actor.ActorId).ToArray())
            .IsEquivalentTo(new[] { data.Owner.OrganizationActorId, data.GroupActorId });
        await Assert.That(shell.ManagedActors.Single(actor => actor.ActorId == data.Owner.OrganizationActorId).ScopeId)
            .IsEqualTo(data.Owner.OrganizationId);
        await Assert.That(shell.ManagedActors.Single(actor => actor.ActorId == data.GroupActorId).ScopeId).IsEqualTo(data.GroupId);
        await Assert.That(shell.PinnedActorId).IsEqualTo(data.Owner.OrganizationActorId);
        await Assert.That(shell.SettingsScopes.Select(item => item.Scope).ToArray())
            .IsEquivalentTo(new[] { "Personal", "Organization", "Group", "Tenant", "Instance" });
        await Assert.That(shell.SettingsScopes.Single(item => item.Scope == "Personal").ScopeId).IsEqualTo(data.Owner.UserId);
        await Assert.That(shell.SettingsScopes.Single(item => item.Scope == "Tenant").ScopeId).IsEqualTo(data.Owner.TenantId);
        await Assert.That(shell.SettingsScopes.Single(item => item.Scope == "Instance").ScopeId).IsNull();
        await Assert.That(shell.SettingsScopes.Single(item => item.Scope == "Organization").DisplayName)
            .IsEqualTo(shell.ManagedActors.Single(actor => actor.ActorId == data.Owner.OrganizationActorId).DisplayName);
        await Assert.That(shell.SettingsScopes.Single(item => item.Scope == "Group").DisplayName).IsEqualTo("Local organizers");
        await PrivateAsync(response);

        // Claimed roles, internal identity and query-string authority cannot select the owner's shell.
        using var attack = Request(data.Other.UserId,
            $"/api/ui-shell/context?userId={data.Owner.UserId}&tenantId={data.ForeignTenantId}&actorId={data.Owner.OrganizationActorId}",
            ("internal_user_id", data.Owner.UserId.ToString()), ("explore:admin:instance", "true"),
            ("explore:admin:tenant", data.Owner.TenantId.ToString()), (ClaimTypes.Role, "platform.admin"));
        using var attackResponse = await client.SendAsync(attack);
        var other = await ReadShellAsync(attackResponse);
        await Assert.That(other.TenantId).IsEqualTo(data.Owner.TenantId);
        await Assert.That(other.ManagedActors).IsEmpty();
        await Assert.That(other.PinnedActorId).IsNull();
        await Assert.That(other.Workspaces.Studio).IsFalse();
        await Assert.That(other.SettingsScopes.Count).IsEqualTo(1);
        await Assert.That(other.SettingsScopes[0].ScopeId).IsEqualTo(data.Other.UserId);
        await PrivateAsync(attackResponse);
    }

    [Test]
    [Arguments(false, "none", false)]
    [Arguments(true, "fake", true)]
    [Arguments(true, "openai", false)]
    [Arguments(true, "unsupported", false)]
    public async Task TenantSettings_ControlPersonalStudioAndAiWithoutDisclosingProviderConfiguration(
        bool enabled, string provider, bool aiAvailable)
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        Guid userId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var data = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            userId = data.UserId;
            var tenant = await db.Tenants.SingleAsync(item => item.Id == data.TenantId);
            AddSetting(db, tenant, GovernanceSettingKeys.Events.UserSubmissionEnabled, enabled);
            AddSetting(db, tenant, GovernanceSettingKeys.AiAssistant.Enabled, enabled);
            AddSetting(db, tenant, GovernanceSettingKeys.AiAssistant.Provider, provider);
            AddSetting(db, tenant, GovernanceSettingKeys.UiShell.DefaultNavModeEvents, "Collapsed");
            AddSetting(db, tenant, GovernanceSettingKeys.UiShell.DefaultNavModeStudio, "Collapsed");
            AddSetting(db, tenant, GovernanceSettingKeys.UiShell.AllowUserNavOverride, false);
            AddSetting(db, tenant, GovernanceSettingKeys.UiShell.OrganizerDefaultWorkspace, "Studio");
            await db.SaveChangesAsync();
        }
        using var request = Request(userId);
        using var response = await client.SendAsync(request);
        var shell = await ReadShellAsync(response);
        await Assert.That(shell.Workspaces.Studio).IsEqualTo(enabled);
        await Assert.That(shell.Workspaces.Ai).IsEqualTo(aiAvailable);
        await Assert.That(shell.Workspaces.Events).IsTrue();
        await Assert.That(shell.Workspaces.Settings).IsTrue();
        await Assert.That(shell.ManagedActors).IsEmpty();
        await Assert.That(shell.NavigationDefaults.Events).IsEqualTo("Collapsed");
        await Assert.That(shell.NavigationDefaults.Studio).IsEqualTo("Collapsed");
        await Assert.That(shell.NavigationDefaults.Ai).IsEqualTo("Docked");
        await Assert.That(shell.NavigationDefaults.AllowUserOverride).IsFalse();
        await Assert.That(shell.NavigationDefaults.OrganizerDefaultWorkspace).IsEqualTo("Studio");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { "tenantId", "deploymentMode", "workspaces", "managedActors", "settingsScopes", "navigationDefaults" });
        await Assert.That(json.RootElement.TryGetProperty("pinnedActorId", out _)).IsFalse();
    }

    [Test]
    public async Task NoOverrides_UsesRegistryNavigationAndWorkspaceDefaults()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var request = Request(Guid.CreateVersion7());
        using var response = await client.SendAsync(request);
        var shell = await ReadShellAsync(response);
        await Assert.That(shell.NavigationDefaults).IsEqualTo(new UiShellNavigationDefaultsDto());
        await Assert.That(shell.Workspaces).IsEqualTo(new WorkspaceAvailabilityDto { Studio = true });
        await Assert.That(shell.PinnedActorId).IsNull();
        await Assert.That(shell.ManagedActors).IsEmpty();
    }

    [Test]
    public async Task AdminWithoutPublishingPermission_RetainsSettingsButCannotPinTheOrganizationActor()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        var data = await SeedAuthorityAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var grants = await db.RolePermissions.Where(item => item.RoleId == (int)RoleEnum.OrgAdmin
                && item.Permission.MasterCode == PermissionCodes.EventCreate).ToListAsync();
            db.RolePermissions.RemoveRange(grants);
            await db.SaveChangesAsync();
        }
        using var request = Request(data.Owner.UserId);
        using var response = await client.SendAsync(request);
        var shell = await ReadShellAsync(response);
        await Assert.That(shell.ManagedActors.Select(actor => actor.ActorId).ToArray()).IsEquivalentTo(new[] { data.GroupActorId });
        await Assert.That(shell.PinnedActorId).IsNull();
        var organizationScope = shell.SettingsScopes.Single(item => item.Scope == "Organization");
        await Assert.That(organizationScope.ScopeId).IsEqualTo(data.Owner.OrganizationId);
        await Assert.That(organizationScope.DisplayName).IsEqualTo("Organization");
    }

    [Test]
    public async Task MultiTenantHost_UsesResolvedTenantAndPersistedModeAndFailsClosedWithoutTenant()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        factory.AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
        using var client = factory.CreateClient();
        var data = await SeedAuthorityAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(Guid.CreateVersion7(), DeploymentMode.MultiTenant, now);
            bootstrap.CompleteInteractive(data.Owner.UserId, now);
            db.Set<InstanceBootstrapState>().Add(bootstrap);
            await db.SaveChangesAsync();
        }
        using var unresolved = Request(data.Other.UserId);
        using var unresolvedResponse = await client.SendAsync(unresolved);
        await Assert.That(unresolvedResponse.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        using var request = Request(data.Other.UserId);
        request.Headers.Add("X-Tenant-Slug", "foreign-shell");
        using var response = await client.SendAsync(request);
        var shell = await ReadShellAsync(response);
        await Assert.That(shell.TenantId).IsEqualTo(data.ForeignTenantId);
        await Assert.That(shell.DeploymentMode).IsEqualTo("MultiTenant");
        await Assert.That(shell.NavigationDefaults.Events).IsEqualTo("Collapsed");
        await Assert.That(shell.SettingsScopes.Select(item => item.Scope).ToArray()).IsEquivalentTo(new[] { "Personal", "Tenant" });
        await Assert.That(shell.SettingsScopes.Single(item => item.Scope == "Tenant").ScopeId).IsEqualTo(data.ForeignTenantId);
        await Assert.That(shell.ManagedActors).IsEmpty();
        await Assert.That(shell.PinnedActorId).IsNull();
        await PrivateAsync(response);
    }

    [Test]
    public async Task NativeHost_ResolvesIndependentDecoratedQueriesAndPreservesIdentityAndCancellationFailures()
    {
        await using var factory = new AuthenticatedWebApplicationFactory();
        using var client = factory.CreateClient();
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var query = first.ServiceProvider.GetRequiredService<IQueryHandler<GetUiShellContextRequest, UiShellContextDto>>();
        var other = second.ServiceProvider.GetRequiredService<IQueryHandler<GetUiShellContextRequest, UiShellContextDto>>();
        await Assert.That(query).IsTypeOf<AuthorizationQueryHandlerDecorator<GetUiShellContextRequest, UiShellContextDto>>();
        await Assert.That(ReferenceEquals(query, other)).IsFalse();
        await Assert.That(ActivatorUtilities.CreateInstance<UiShellController>(first.ServiceProvider)).IsNotNull();
        var accessor = first.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext();
        first.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        try
        {
            await Assert.That(async () => await query.QueryAsync(new(), default)).Throws<UnauthorizedAccessException>();
            accessor.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("sub", Guid.CreateVersion7().ToString())], TestAuthHandler.SchemeName));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            var exception = await Assert.That(async () => await query.QueryAsync(new(), cancellation.Token))
                .Throws<OperationCanceledException>();
            await Assert.That(exception!.CancellationToken).IsEqualTo(cancellation.Token);
            accessor.HttpContext.User.AddIdentity(new ClaimsIdentity(
                [new Claim("sub", Guid.CreateVersion7().ToString())], "OtherAuthority"));
            await Assert.That(async () => await query.QueryAsync(new(), default)).Throws<UnauthorizedAccessException>();
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private static async Task<AuthorityData> SeedAuthorityAsync(AuthenticatedWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var owner = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(db);
        var other = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var tenant = await db.Tenants.SingleAsync(item => item.Id == owner.TenantId);
        var user = await db.Users.SingleAsync(item => item.Id == owner.UserId);
        var membership = await db.TenantUsers.SingleAsync(item => item.UserId == user.Id);
        var platformRole = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(), UserId = user.Id, User = user, RoleId = platformRole.Id, Role = platformRole
        });
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, TenantUserId = membership.Id,
            TenantUser = membership, RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        var permission = await db.Permissions.SingleAsync(item => item.MasterCode == PermissionCodes.EventCreate);
        if (!await db.RolePermissions.AnyAsync(item => item.RoleId == (int)RoleEnum.GroupAdmin && item.PermissionId == permission.Id))
            db.RolePermissions.Add(new RolePermission { RoleId = (int)RoleEnum.GroupAdmin, Role = null!, PermissionId = permission.Id, Permission = permission });
        var group = AddGroup(db, tenant, user, "Local organizers");
        var foreignTenant = new Tenant
        {
            Id = Guid.CreateVersion7(), Slug = "foreign-shell", FullName = "Foreign tenant",
            TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!
        };
        db.Tenants.Add(foreignTenant);
        var foreignMembership = new TenantUser
        {
            Id = Guid.CreateVersion7(), TenantId = foreignTenant.Id, Tenant = foreignTenant,
            UserId = other.UserId, User = await db.Users.SingleAsync(item => item.Id == other.UserId),
            StatusId = (int)TenantUserStatusEnum.Active
        };
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = foreignTenant.Id, Tenant = foreignTenant,
            TenantUserId = foreignMembership.Id, TenantUser = foreignMembership,
            RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        AddGroup(db, foreignTenant, user, "Foreign organizers");
        AddSetting(db, tenant, GovernanceSettingKeys.Events.UserSubmissionEnabled, false);
        AddSetting(db, tenant, GovernanceSettingKeys.PublicExperience.Mode, "OrganizationCentric");
        AddSetting(db, tenant, GovernanceSettingKeys.PublicExperience.PrimaryOrganizationId, owner.OrganizationId.ToString());
        AddSetting(db, foreignTenant, GovernanceSettingKeys.UiShell.DefaultNavModeEvents, "Collapsed");
        await db.SaveChangesAsync();
        return new(owner, other, group.GroupId, group.Id, foreignTenant.Id);
    }

    private static Actor AddGroup(ExploreDbContext db, Tenant tenant, User user, string name)
    {
        var group = new Group { Id = Guid.CreateVersion7(), FullName = name };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, GroupId = group.Id, Group = group,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!, IsVisible = true, IsOrganizerEligible = true
        };
        db.GroupMembers.Add(new GroupMember
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, GroupTenantId = participation.Id,
            GroupTenant = participation, UserId = user.Id, User = user, RoleId = (int)RoleEnum.GroupAdmin, Role = null!
        });
        var actor = new ActorBuilder().WithActorType(ActorTypeEnum.Group).WithDisplayName(name).Build();
        actor.GroupId = group.Id;
        actor.Group = group;
        db.Actors.Add(actor);
        return actor;
    }

    private static void AddSetting<T>(ExploreDbContext db, Tenant tenant, string key, T value) =>
        db.Set<TenantSetting>().Add(new TenantSetting
        {
            Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant, SettingKey = key, Value = JsonSerializer.Serialize(value)
        });

    private static HttpRequestMessage Request(Guid userId, string path = "/api/ui-shell/context", params (string Type, string Value)[] claims)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId, "Shell caller", claims));
        return request;
    }

    private static async Task<UiShellContextDto> ReadShellAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK).Because(await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<UiShellContextDto>())!;
    }

    private static async Task UnauthorizedAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(response.Content.Headers.ContentType!.MediaType).IsEqualTo("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo(401);
    }

    private static async Task PrivateAsync(HttpResponseMessage response)
    {
        await Assert.That(response.Headers.CacheControl!.Private).IsTrue();
        await Assert.That(response.Headers.CacheControl.NoStore).IsTrue();
        await Assert.That(response.Headers.Pragma.Any(value => value.Name == "no-cache")).IsTrue();
        await Assert.That(response.Headers.GetValues("Referrer-Policy").Single()).IsEqualTo("no-referrer");
    }

    private sealed record AuthorityData(
        TenantScenarioSeed.TenantOrganizationScenarioResult Owner, TenantScenarioSeed.TenantScenarioResult Other,
        Guid? GroupId, Guid GroupActorId, Guid ForeignTenantId);
}
