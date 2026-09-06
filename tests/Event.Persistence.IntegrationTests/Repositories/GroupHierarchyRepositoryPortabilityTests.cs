// ABOUTME: Verifies group hierarchy validation through provider-neutral EF Core queries.
// ABOUTME: Exercises file-backed SQLite behavior and query translation for every supported provider.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("PersistenceSqlite")]
public sealed class GroupHierarchyRepositoryPortabilityTests
{
    [Test]
    public async Task HierarchyValidation_FileBackedSqlite_PreservesCycleDepthTenantAndSoftDeleteRules()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"event-group-hierarchy-{Guid.NewGuid():N}.db");

        try
        {
            await using var context = CreateSqliteContext(databasePath);
            await context.Database.EnsureCreatedAsync();

            var tenantStatus = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active",
                IsActiveState = true
            };
            var approvalStatus = new ApprovalStatus
            {
                Id = (int)ApprovalStatusEnum.Approved,
                MasterCode = "APPROVED",
                FullName = "Approved"
            };
            context.AddRange(tenantStatus, approvalStatus);

            var tenant = await SeedTenantAsync(context, tenantStatus, "primary");
            var otherTenant = await SeedTenantAsync(context, tenantStatus, "other");
            var root = await SeedGroupAsync(context, tenant, approvalStatus, "Root");
            var child = await SeedGroupAsync(context, tenant, approvalStatus, "Child", root.Id);
            var otherRoot = await SeedGroupAsync(context, otherTenant, approvalStatus, "Other Root");
            var otherChild = await SeedGroupAsync(context, otherTenant, approvalStatus, "Other Child", otherRoot.Id);
            var repository = new GroupRepository(context);

            await Assert.That(await repository.WouldCreateHierarchyCycle(
                root.GroupId,
                child.GroupId,
                tenant.Id,
                CancellationToken.None)).IsTrue();
            await Assert.That(await repository.WouldCreateHierarchyCycle(
                otherRoot.GroupId,
                otherChild.GroupId,
                tenant.Id,
                CancellationToken.None)).IsFalse();

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await Assert.ThrowsAsync<OperationCanceledException>(() => repository.WouldCreateHierarchyCycle(
                    root.GroupId,
                    child.GroupId,
                    tenant.Id,
                    cancellation.Token));
            }

            GroupTenant? deepParent = null;
            GroupTenant? allowedParent = null;
            GroupTenant? allowedMoveParent = null;
            for (var depth = 1; depth <= GroupHierarchyRules.MaxDepth; depth++)
            {
                deepParent = await SeedGroupAsync(
                    context,
                    tenant,
                    approvalStatus,
                    $"Depth {depth}",
                    deepParent?.Id);
                if (depth == GroupHierarchyRules.MaxDepth - 1)
                {
                    allowedParent = deepParent;
                }

                if (depth == GroupHierarchyRules.MaxDepth - 2)
                {
                    allowedMoveParent = deepParent;
                }
            }

            await Assert.That(await repository.WouldExceedHierarchyDepth(
                allowedParent!.GroupId,
                tenant.Id,
                GroupHierarchyRules.MaxDepth,
                CancellationToken.None)).IsFalse();
            await Assert.That(await repository.WouldExceedHierarchyDepth(
                deepParent!.GroupId,
                tenant.Id,
                GroupHierarchyRules.MaxDepth,
                CancellationToken.None)).IsTrue();

            var movingRoot = await SeedGroupAsync(context, tenant, approvalStatus, "Moving Root");
            await SeedGroupAsync(context, tenant, approvalStatus, "Moving Child", movingRoot.Id);
            await Assert.That(await repository.WouldExceedHierarchyDepthForMove(
                movingRoot.GroupId,
                allowedMoveParent!.GroupId,
                tenant.Id,
                GroupHierarchyRules.MaxDepth,
                CancellationToken.None)).IsFalse();
            await Assert.That(await repository.WouldExceedHierarchyDepthForMove(
                movingRoot.GroupId,
                deepParent.GroupId,
                tenant.Id,
                GroupHierarchyRules.MaxDepth,
                CancellationToken.None)).IsTrue();

            child.IsDeleted = true;
            await context.SaveChangesAsync();
            await Assert.That(await repository.WouldCreateHierarchyCycle(
                root.GroupId,
                child.GroupId,
                tenant.Id,
                CancellationToken.None)).IsFalse();

            var cycleA = await SeedGroupAsync(context, tenant, approvalStatus, "Existing Cycle A");
            var cycleB = await SeedGroupAsync(context, tenant, approvalStatus, "Existing Cycle B", cycleA.Id);
            cycleA.ParentGroupTenantId = cycleB.Id;
            await context.SaveChangesAsync();
            await Assert.That(await repository.WouldCreateHierarchyCycle(
                movingRoot.GroupId,
                cycleA.GroupId,
                tenant.Id,
                CancellationToken.None)).IsFalse();
            await Assert.That(await repository.WouldExceedHierarchyDepth(
                cycleA.GroupId,
                tenant.Id,
                GroupHierarchyRules.MaxDepth,
                CancellationToken.None)).IsTrue();

            var lockedGroupId = await repository.ExecuteWithHierarchyMutationLock(
                tenant.Id,
                async token => (await SeedGroupAsync(
                    context,
                    tenant,
                    approvalStatus,
                    "Locked SQLite Mutation",
                    cancellationToken: token)).Id,
                CancellationToken.None);
            await Assert.That(await context.GroupTenants.AnyAsync(
                participation => participation.Id == lockedGroupId)).IsTrue();
        }
        finally
        {
            File.Delete(databasePath);
            File.Delete(databasePath + "-shm");
            File.Delete(databasePath + "-wal");
        }
    }

    private static ExploreDbContext CreateSqliteContext(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true
        }.ToString();
        var context = new ExploreDbContext(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseSqlite(connectionString)
                .UseSnakeCaseNamingConvention()
                .Options);
        context.EnableTenantFilterBypass("File-backed SQLite group hierarchy portability test.");
        return context;
    }

    private static async Task<Tenant> SeedTenantAsync(
        ExploreDbContext context,
        TenantStatus tenantStatus,
        string suffix)
    {
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = $"Hierarchy {suffix}",
            Slug = $"hierarchy-{suffix}-{Guid.NewGuid():N}",
            TenantStatusId = tenantStatus.Id,
            TenantStatus = tenantStatus,
            CreatedAt = DateTime.UtcNow
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }

    private static async Task<GroupTenant> SeedGroupAsync(
        ExploreDbContext context,
        Tenant tenant,
        ApprovalStatus approvalStatus,
        string fullName,
        Guid? parentGroupTenantId = null,
        CancellationToken cancellationToken = default)
    {
        var group = new Group
        {
            Id = Guid.CreateVersion7(),
            FullName = fullName,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            GroupId = group.Id,
            Group = group,
            ApprovalStatusId = approvalStatus.Id,
            ApprovalStatus = approvalStatus,
            ParentGroupTenantId = parentGroupTenantId,
            CreatedAt = DateTime.UtcNow,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.GroupTenants.Add(participation);
        await context.SaveChangesAsync(cancellationToken);
        return participation;
    }
}
