// ABOUTME: PostgreSQL-backed tests for DB-enforced group hierarchy invariants.
// ABOUTME: Verifies same-tenant parent FKs, parent exclusivity, self-parent checks, and bounded ancestry helpers.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class GroupHierarchyConstraintTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ActorTypeLookup_ShouldContainEveryDefinedActorType()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var seededIds = await context.Set<ActorType>()
            .Select(actorType => actorType.Id)
            .ToArrayAsync();

        var missingIds = Enum.GetValues<ActorTypeEnum>()
            .Select(actorType => (int)actorType)
            .Except(seededIds)
            .ToArray();

        await Assert.That(missingIds).IsEmpty();
    }

    [Test]
    public async Task RoleLookup_ShouldContainEveryDefinedRole()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var seededIds = await context.Roles
            .Select(role => role.Id)
            .ToArrayAsync();

        var missingIds = Enum.GetValues<RoleEnum>()
            .Select(role => (int)role)
            .Except(seededIds)
            .ToArray();

        await Assert.That(missingIds).IsEmpty();
    }

    [Test]
    public async Task Group_ShouldRejectDualParentReferences()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "dual-parent");
        var organization = await SeedOrganizationAsync(context, tenant, "Dual Parent Org");
        var parentGroup = await SeedGroupAsync(context, tenant, "Dual Parent Group");

        var group = NewGroup(tenant, "Invalid Dual Parent");
        group.ParentOrganizationTenantId = organization.Id;
        group.ParentGroupTenantId = parentGroup.Id;
        context.GroupTenants.Add(group);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task Group_ShouldRejectSelfParentReference()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "self-parent");
        var group = NewGroup(tenant, "Self Parent Group");
        group.Id = Guid.NewGuid();
        group.ParentGroupTenantId = group.Id;
        context.GroupTenants.Add(group);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task Group_ShouldRejectParentOrganizationFromDifferentTenant()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "tenant-a");
        var tenantB = await SeedTenantAsync(context, "tenant-b");
        var foreignOrganization = await SeedOrganizationAsync(context, tenantB, "Foreign Org");

        var group = NewGroup(tenantA, "Cross Tenant Org Parent");
        group.ParentOrganizationTenantId = foreignOrganization.Id;
        context.GroupTenants.Add(group);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task Group_ShouldAllowRootGroupWithoutParents()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "root");

        var group = await SeedGroupAsync(context, tenant, "Root Group");

        await Assert.That(group.ParentOrganizationTenantId).IsNull();
        await Assert.That(group.ParentGroupTenantId).IsNull();
    }

    [Test]
    public async Task Group_ShouldAllowParentOrganizationFromSameTenant()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "same-org");
        var organization = await SeedOrganizationAsync(context, tenant, "Same Tenant Org");

        var group = NewGroup(tenant, "Org Child Group");
        group.ParentOrganizationTenantId = organization.Id;
        context.GroupTenants.Add(group);
        await context.SaveChangesAsync();

        await Assert.That(group.ParentOrganizationTenantId).IsEqualTo(organization.Id);
    }

    [Test]
    public async Task Group_ShouldAllowParentGroupFromSameTenant()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "same-group");
        var parentGroup = await SeedGroupAsync(context, tenant, "Same Tenant Parent");

        var child = await SeedGroupAsync(context, tenant, "Same Tenant Child", parentGroup.Id);

        await Assert.That(child.ParentGroupTenantId).IsEqualTo(parentGroup.Id);
    }

    [Test]
    public async Task Group_ShouldRejectParentGroupFromDifferentTenant()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenantA = await SeedTenantAsync(context, "tenant-group-a");
        var tenantB = await SeedTenantAsync(context, "tenant-group-b");
        var foreignGroup = await SeedGroupAsync(context, tenantB, "Foreign Group");

        var group = NewGroup(tenantA, "Cross Tenant Group Parent");
        group.ParentGroupTenantId = foreignGroup.Id;
        context.GroupTenants.Add(group);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    [Test]
    public async Task WouldCreateHierarchyCycle_ShouldDetectParentAncestryCycle()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "cycle");
        var root = await SeedGroupAsync(context, tenant, "Root");
        var child = await SeedGroupAsync(context, tenant, "Child", root.Id);
        var repository = new GroupRepository(context);

        var wouldCreateCycle = await repository.WouldCreateHierarchyCycle(
            root.GroupId,
            child.GroupId,
            tenant.Id,
            CancellationToken.None);

        await Assert.That(wouldCreateCycle).IsTrue();
    }

    [Test]
    public async Task WouldExceedHierarchyDepth_ShouldDetectParentChainAtMaximumDepth()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "depth");
        GroupTenant? parentGroup = null;

        for (var depth = 1; depth <= GroupHierarchyRules.MaxDepth; depth++)
        {
            parentGroup = await SeedGroupAsync(context, tenant, $"Depth {depth}", parentGroup?.Id);
        }

        var repository = new GroupRepository(context);

        var wouldExceedDepth = await repository.WouldExceedHierarchyDepth(
            parentGroup?.GroupId,
            tenant.Id,
            GroupHierarchyRules.MaxDepth,
            CancellationToken.None);

        await Assert.That(wouldExceedDepth).IsTrue();
    }

    [Test]
    public async Task WouldExceedHierarchyDepth_ShouldAllowParentChainBelowMaximumDepth()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "depth-allowed");
        GroupTenant? parentGroup = null;

        for (var depth = 1; depth < GroupHierarchyRules.MaxDepth; depth++)
        {
            parentGroup = await SeedGroupAsync(context, tenant, $"Allowed Depth {depth}", parentGroup?.Id);
        }

        var repository = new GroupRepository(context);

        var wouldExceedDepth = await repository.WouldExceedHierarchyDepth(
            parentGroup?.GroupId,
            tenant.Id,
            GroupHierarchyRules.MaxDepth,
            CancellationToken.None);

        await Assert.That(wouldExceedDepth).IsFalse();
    }

    [Test]
    public async Task WouldExceedHierarchyDepthForMove_ShouldDetectSubtreeDepthOverflow()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "move-depth");
        GroupTenant? deepParentGroup = null;

        for (var depth = 1; depth < GroupHierarchyRules.MaxDepth; depth++)
        {
            deepParentGroup = await SeedGroupAsync(
                context,
                tenant,
                $"Deep Parent {depth}",
                deepParentGroup?.Id);
        }

        var movingRoot = await SeedGroupAsync(context, tenant, "Moving Root");
        await SeedGroupAsync(context, tenant, "Moving Child", movingRoot.Id);
        var repository = new GroupRepository(context);

        var wouldExceedDepth = await repository.WouldExceedHierarchyDepthForMove(
            movingRoot.GroupId,
            deepParentGroup?.GroupId,
            tenant.Id,
            GroupHierarchyRules.MaxDepth,
            CancellationToken.None);

        await Assert.That(wouldExceedDepth).IsTrue();
    }

    [Test]
    public async Task ExecuteWithHierarchyMutationLock_ShouldRunOperationInsideTransaction()
    {
        await fixture.ResetAsync();
        Guid tenantId;
        await using (var seedContext = fixture.CreateDbContext())
        {
            tenantId = (await SeedTenantAsync(seedContext, "lock")).Id;
        }

        await using var context = CreateRetryingDbContext();
        var repository = new GroupRepository(context);

        var createdId = await repository.ExecuteWithHierarchyMutationLock(
            tenantId,
            async token =>
            {
                Tenant tenant = await context.Tenants.SingleAsync(value => value.Id == tenantId, token);
                var group = NewGroup(tenant, "Locked Mutation");
                context.GroupTenants.Add(group);
                await context.SaveChangesAsync(token);
                return group.Id;
            },
            CancellationToken.None);

        var exists = await context.GroupTenants.AnyAsync(group => group.Id == createdId);
        await Assert.That(exists).IsTrue();
    }

    private ExploreDbContext CreateRetryingDbContext()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention()
            .Options;

        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Retry-enabled group hierarchy mutation integration test.");
        return context;
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = $"Group Hierarchy {slugPrefix}",
            Slug = $"grp-hierarchy-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = 2,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static async Task<OrganizationTenant> SeedOrganizationAsync(
        ExploreDbContext context,
        Tenant tenant,
        string fullName)
    {
        var organization = new Organization
        {
            Id = Guid.CreateVersion7(),
            Pii = new OrganizationPii { FullName = fullName },
            ConcurrencyStamp = Guid.NewGuid(),
        };
        var participation = new OrganizationTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            OrganizationId = organization.Id,
            Organization = organization,
            ApprovalStatusId = 1,
            ApprovalStatus = null!,
            ConcurrencyStamp = Guid.NewGuid(),
        };

        context.OrganizationTenants.Add(participation);
        await context.SaveChangesAsync();
        return participation;
    }

    private static async Task<GroupTenant> SeedGroupAsync(
        ExploreDbContext context,
        Tenant tenant,
        string fullName,
        Guid? parentGroupId = null)
    {
        var group = NewGroup(tenant, fullName);
        group.ParentGroupTenantId = parentGroupId;
        context.GroupTenants.Add(group);
        await context.SaveChangesAsync();
        return group;
    }

    private static GroupTenant NewGroup(Tenant tenant, string fullName)
    {
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = fullName,
            ConcurrencyStamp = Guid.NewGuid(),
        };
        return new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            GroupId = group.Id,
            Group = group,
            ApprovalStatusId = 1,
            ApprovalStatus = null!,
            ConcurrencyStamp = Guid.NewGuid(),
        };
    }
}
