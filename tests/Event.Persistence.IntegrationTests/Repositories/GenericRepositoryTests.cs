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

    private sealed record TestCurrentUserService(Guid? UserId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
    }
}
