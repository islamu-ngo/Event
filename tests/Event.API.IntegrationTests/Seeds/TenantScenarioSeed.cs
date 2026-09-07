using Event.Api.IntegrationTests.Builders;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Event.Api.IntegrationTests.Seeds;

/// <summary>
/// Seeds complete tenant contexts (tenant + user + actor) for integration test scenarios.
/// </summary>
public static class TenantScenarioSeed
{
    /// <summary>Carries the IDs of all entities created by a tenant scenario seed.</summary>
    public sealed record TenantScenarioResult(Guid TenantId, Guid UserId, Guid ActorId);

    /// <summary>Carries the IDs for a tenant user that can publish events for an organization.</summary>
    public sealed record TenantOrganizationScenarioResult(
        Guid TenantId,
        Guid UserId,
        Guid ActorId,
        Guid OrganizationId,
        Guid OrganizationActorId);

    /// <summary>
    /// Seeds an active tenant with a user and user-type actor.
    /// Uses <see cref="PlatformDefaults.DefaultTenantId"/> for single-tenant API compatibility.
    /// </summary>
    public static async Task<TenantScenarioResult> SeedActiveTenantWithUserAsync(ExploreDbContext context)
    {
        var tenant = await context.Tenants.FindAsync(PlatformDefaults.DefaultTenantId);
        if (tenant is null)
        {
            tenant = new TenantBuilder()
                .WithId(PlatformDefaults.DefaultTenantId)
                .WithFullName("Default Test Tenant")
                .WithSlug("default-test")
                .Build();
            context.Tenants.Add(tenant);
        }

        var user = new UserBuilder().WithId(Guid.CreateVersion7()).Build();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new ActorBuilder()
            .WithId(Guid.CreateVersion7())
            .WithUserId(user.Id)
            .Build();
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        context.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return new TenantScenarioResult(tenant.Id, user.Id, actor.Id);
    }

    /// <summary>
    /// Seeds an active tenant user plus an approved organization publisher the user can create events for.
    /// </summary>
    public static async Task<TenantOrganizationScenarioResult> SeedActiveTenantWithOrganizationPublisherAsync(ExploreDbContext context)
    {
        TenantScenarioResult seeded = await SeedActiveTenantWithUserAsync(context);
        await EnsureOrgAdminCanCreateEventsAsync(context);
        Tenant tenant = await context.Tenants.SingleAsync(value => value.Id == seeded.TenantId);
        User user = await context.Users.SingleAsync(value => value.Id == seeded.UserId);

        var organizationId = Guid.CreateVersion7();
        var organization = new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii
            {
                OrganizationId = organizationId,
                FullName = "AI Test Publisher Organization"
            },
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        var organizationTenant = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = seeded.TenantId,
            Tenant = tenant,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
            ApprovalStatus = null!,
            IsVisible = true,
            IsOrganizerEligible = true,
            ApprovedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.OrganizationTenants.Add(organizationTenant);
        await context.SaveChangesAsync();

        var organizationActor = new ActorBuilder()
            .WithActorType(ActorTypeEnum.Organization)
            .WithDisplayName("AI Test Publisher Organization")
            .Build();
        organizationActor.OrganizationId = organizationId;
        context.Actors.Add(organizationActor);
        await context.SaveChangesAsync();

        context.OrganizationMembers.Add(new OrganizationMember
        {
            Id = Guid.CreateVersion7(),
            TenantId = seeded.TenantId,
            Tenant = tenant,
            OrganizationTenantId = organizationTenant.Id,
            OrganizationTenant = organizationTenant,
            UserId = seeded.UserId,
            User = user,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return new TenantOrganizationScenarioResult(
            seeded.TenantId,
            seeded.UserId,
            seeded.ActorId,
            organizationId,
            organizationActor.Id);
    }

    private static async Task EnsureOrgAdminCanCreateEventsAsync(ExploreDbContext context)
    {
        var eventCreatePermissionId = await context.Permissions
            .Where(permission => permission.MasterCode == PermissionCodes.EventCreate)
            .Select(permission => permission.Id)
            .SingleAsync();

        var roleId = (int)RoleEnum.OrgAdmin;
        var exists = await context.RolePermissions
            .AnyAsync(rolePermission => rolePermission.RoleId == roleId
                && rolePermission.PermissionId == eventCreatePermissionId);

        if (exists)
        {
            return;
        }

        context.RolePermissions.Add(new RolePermission
        {
            RoleId = roleId,
            Role = null!,
            PermissionId = eventCreatePermissionId,
            Permission = null!,
            GrantedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a secondary tenant with its own user and actor.
    /// Uses a random tenant ID for multi-tenant isolation testing.
    /// </summary>
    public static async Task<TenantScenarioResult> SeedSecondaryTenantWithUserAsync(
        ExploreDbContext context,
        string name = "Secondary Tenant")
    {
        var tenant = new TenantBuilder()
            .WithFullName(name)
            .Build();
        context.Tenants.Add(tenant);

        var user = new UserBuilder().WithId(Guid.CreateVersion7()).Build();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new ActorBuilder()
            .WithId(Guid.CreateVersion7())
            .WithUserId(user.Id)
            .WithDisplayName($"{name} User")
            .Build();
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        return new TenantScenarioResult(tenant.Id, user.Id, actor.Id);
    }
}
