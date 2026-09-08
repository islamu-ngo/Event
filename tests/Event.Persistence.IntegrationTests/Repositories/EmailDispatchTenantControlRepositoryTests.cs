using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EmailDispatchTenantControlRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task SetTenantPauseStateCreatesAndUpdatesSingleTenantControlRow()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(context, "email-dispatch-control");
        var actorId = Guid.NewGuid();
        var repository = new EmailDispatchOutboxRepository(context);

        var paused = await repository.SetTenantPauseState(
            tenant.Id,
            true,
            "tenant maintenance",
            actorId,
            DateTime.UtcNow,
            CancellationToken.None);

        await Assert.That(paused.TenantId).IsEqualTo(tenant.Id);
        await Assert.That(paused.IsPaused).IsTrue();
        await Assert.That(paused.PauseReason).IsEqualTo("tenant maintenance");
        await Assert.That(paused.PausedBy).IsEqualTo(actorId);
        await Assert.That(await repository.IsTenantPaused(tenant.Id, CancellationToken.None)).IsTrue();

        var resumed = await repository.SetTenantPauseState(
            tenant.Id,
            false,
            null,
            actorId,
            DateTime.UtcNow,
            CancellationToken.None);

        await Assert.That(resumed.Id).IsEqualTo(paused.Id);
        await Assert.That(resumed.IsPaused).IsFalse();
        await Assert.That(resumed.PauseReason).IsNull();
        await Assert.That(resumed.PausedAt).IsNull();
        await Assert.That(resumed.PausedBy).IsNull();
        await Assert.That(await repository.IsTenantPaused(tenant.Id, CancellationToken.None)).IsFalse();

        var rowCount = await context.EmailDispatchTenantControls
            .IgnoreQueryFilters()
            .CountAsync(control => control.TenantId == tenant.Id);
        await Assert.That(rowCount).IsEqualTo(1);
    }

    private static async Task<Tenant> SeedTenantAsync(ExploreDbContext context, string slugPrefix)
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            FullName = $"Email Dispatch {slugPrefix}",
            Slug = $"email-dispatch-{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant;
    }
}
