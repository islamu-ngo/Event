using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class GenericRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task Exists_WhenEntityExists_ReturnsTrueWithoutTrackingEntity()
    {
        await fixture.ResetAsync();
        using var context = fixture.CreateDbContext();
        var tenant = new Tenant
        {
            FullName = "Generic Repository Exists",
            Slug = $"generic-repository-exists-{Guid.NewGuid():N}",
            TenantStatusId = 2,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var repository = new GenericRepository<Tenant, Guid>(context);

        var exists = await repository.Exists(tenant.Id);

        await Assert.That(exists).IsTrue();
        await Assert.That(context.ChangeTracker.Entries<Tenant>()).IsEmpty();
    }

    [Test]
    public async Task Update_WhenAuditableEntityAlreadyHasUpdatedAt_StoresCurrentUserAsUpdatedBy()
    {
        await fixture.ResetAsync();
        var actorId = Guid.CreateVersion7();
        var manualUpdatedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        await using (var context = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                FullName = "Generic Repository Audit",
                Slug = $"generic-repository-audit-{Guid.NewGuid():N}",
                TenantStatusId = 2,
                TenantStatus = null!,
            };

            context.Tenants.Add(tenant);
            await context.SaveChangesAsync();
        }

        await using (var context = fixture.CreateDbContext())
        {
            context.CurrentUserService = new TestCurrentUserService(actorId);
            var repository = new GenericRepository<Tenant, Guid>(context);
            var tenant = await context.Tenants.SingleAsync(t => t.FullName == "Generic Repository Audit");
            tenant.Description = "changed by launch-critical write";
            tenant.UpdatedAt = manualUpdatedAt;

            await repository.Update(tenant);
        }

        await using (var context = fixture.CreateDbContext())
        {
            var tenant = await context.Tenants.SingleAsync(t => t.FullName == "Generic Repository Audit");

            await Assert.That(tenant.UpdatedAt).IsEqualTo(manualUpdatedAt);
            await Assert.That(tenant.UpdatedBy).IsEqualTo(actorId);
        }
    }

    [Test]
    public async Task Delete_WhenGroupParticipationsAreTracked_PreservesRowsAndAuditOwnership()
    {
        await fixture.ResetAsync();
        var userId = Guid.CreateVersion7();
        Guid groupId;
        Guid participationId;
        Guid tenantId;
        await using (var context = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                FullName = "Tracked group deletion",
                Slug = $"tracked-group-{Guid.CreateVersion7():N}",
                TenantStatusId = 2,
                TenantStatus = null!
            };
            var group = new Group { FullName = "Tracked group" };
            var participation = new GroupTenant
            {
                Tenant = tenant,
                TenantId = tenant.Id,
                Group = group,
                GroupId = group.Id,
                ApprovalStatusId = (int)Explore.Domain.Enums.ApprovalStatusEnum.Pending,
                ApprovalStatus = null!
            };
            context.GroupTenants.Add(participation);
            await context.SaveChangesAsync();
            groupId = group.Id;
            participationId = participation.Id;
            tenantId = tenant.Id;
        }
        await using (var context = fixture.CreateDbContext())
        {
            context.CurrentUserService = new TestCurrentUserService(userId);
            var repository = new GroupRepository(context);
            var group = await repository.GetById(groupId)
                ?? throw new InvalidOperationException("Expected the persisted group.");
            await Assert.That(group.TenantParticipations.Single().Id).IsEqualTo(participationId);

            await repository.Delete(group);
        }
        await using var verification = fixture.CreateDbContext();
        await Assert.That(await verification.Groups.AnyAsync(group => group.Id == groupId)).IsFalse();
        var retained = await verification.Groups.IgnoreQueryFilters(["SoftDelete"])
            .SingleAsync(group => group.Id == groupId);
        await Assert.That(retained.IsDeleted).IsTrue();
        await Assert.That(retained.DeletedBy).IsEqualTo((Guid?)userId);
        await Assert.That(retained.DeletedAt).IsNotNull();
        await Assert.That(retained.UpdatedBy).IsEqualTo((Guid?)userId);
        await Assert.That(retained.UpdatedAt).IsEqualTo(retained.DeletedAt);
        var dependent = await verification.GroupTenants.IgnoreQueryFilters(["SoftDelete"])
            .SingleAsync(item => item.Id == participationId);
        await Assert.That(dependent.IsDeleted).IsFalse();
        await Assert.That(dependent.GroupId).IsEqualTo(groupId);
        await Assert.That(dependent.TenantId).IsEqualTo(tenantId);
    }

    private sealed record TestCurrentUserService(Guid? UserId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
    }
}
