using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class TenantLifecycleTransitionRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task LifecycleTransaction_WhenStatusAndAuditSucceed_CommitsExactlyOneAudit()
    {
        await fixture.ResetAsync();
        var tenantId = await SeedActiveTenantAsync();
        var operatorId = Guid.NewGuid();
        var transitionedAt = TruncateToMicroseconds(DateTime.UtcNow);

        await using (var context = fixture.CreateDbContext())
        {
            var tenantRepository = new TenantRepository(context);
            var lifecycleLogRepository = new TenantLifecycleLogRepository(context);
            var unitOfWork = new EfCoreUnitOfWork(context);

            await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var transitioned = await tenantRepository.TryTransitionStatusAsync(
                    tenantId,
                    (int)TenantStatusEnum.Active,
                    (int)TenantStatusEnum.Suspended,
                    transitionedAt,
                    operatorId,
                    ct);
                await Assert.That(transitioned).IsTrue();

                await lifecycleLogRepository.CreateAsync(CreateLifecycleLog(
                    tenantId,
                    operatorId,
                    (int)TenantStatusEnum.Suspended,
                    transitionedAt), ct);
            });
        }

        await using var verifyContext = fixture.CreateDbContext();
        var savedTenant = await verifyContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId);
        var savedLogs = await verifyContext.TenantLifecycleLogs
            .AsNoTracking()
            .Where(log => log.TenantId == tenantId)
            .ToListAsync();

        await Assert.That(savedTenant.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Suspended);
        await Assert.That(savedLogs).HasSingleItem();
        await Assert.That(savedLogs[0].OldStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(savedLogs[0].NewStatusId).IsEqualTo((int)TenantStatusEnum.Suspended);
        await Assert.That(savedLogs[0].TransitionedByUserId).IsEqualTo(operatorId);
    }

    [Test]
    public async Task LifecycleTransaction_WhenAuditWriteFails_RollsBackStatusAndAudit()
    {
        await fixture.ResetAsync();
        var tenantId = await SeedActiveTenantAsync();
        var operatorId = Guid.NewGuid();
        var transitionedAt = TruncateToMicroseconds(DateTime.UtcNow);

        await using (var context = fixture.CreateDbContext())
        {
            var tenantRepository = new TenantRepository(context);
            var lifecycleLogRepository = new TenantLifecycleLogRepository(context);
            var unitOfWork = new EfCoreUnitOfWork(context);

            await Assert.ThrowsAsync<DbUpdateException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var transitioned = await tenantRepository.TryTransitionStatusAsync(
                    tenantId,
                    (int)TenantStatusEnum.Active,
                    (int)TenantStatusEnum.Suspended,
                    transitionedAt,
                    operatorId,
                    ct);
                await Assert.That(transitioned).IsTrue();

                await lifecycleLogRepository.CreateAsync(CreateLifecycleLog(
                    tenantId,
                    operatorId,
                    int.MaxValue,
                    transitionedAt), ct);
            }));
        }

        await using var verifyContext = fixture.CreateDbContext();
        var savedTenant = await verifyContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId);
        var savedAuditCount = await verifyContext.TenantLifecycleLogs
            .AsNoTracking()
            .CountAsync(log => log.TenantId == tenantId);

        await Assert.That(savedTenant.TenantStatusId).IsEqualTo((int)TenantStatusEnum.Active);
        await Assert.That(savedAuditCount).IsEqualTo(0);
    }

    [Test]
    public async Task TryTransitionStatusAsync_WhenTwoWritersRace_AllowsExactlyOneWinner()
    {
        await fixture.ResetAsync();
        var tenantId = await SeedActiveTenantAsync();
        var suspendedBy = Guid.NewGuid();
        var archivedBy = Guid.NewGuid();

        await using var suspendedContext = fixture.CreateTenantFilteredDbContext();
        await using var archivedContext = fixture.CreateTenantFilteredDbContext();
        var suspendedRepository = new TenantRepository(suspendedContext);
        var archivedRepository = new TenantRepository(archivedContext);
        var transitionedAt = TruncateToMicroseconds(DateTime.UtcNow);

        var results = await Task.WhenAll(
            suspendedRepository.TryTransitionStatusAsync(
                tenantId,
                (int)TenantStatusEnum.Active,
                (int)TenantStatusEnum.Suspended,
                transitionedAt,
                suspendedBy,
                CancellationToken.None),
            archivedRepository.TryTransitionStatusAsync(
                tenantId,
                (int)TenantStatusEnum.Active,
                (int)TenantStatusEnum.Archived,
                transitionedAt,
                archivedBy,
                CancellationToken.None));

        await Assert.That(results.Count(result => result)).IsEqualTo(1);

        await using var verifyContext = fixture.CreateTenantFilteredDbContext();
        var savedTenant = await verifyContext.Tenants.AsNoTracking().SingleAsync(tenant => tenant.Id == tenantId);
        var expectedStatus = results[0] ? TenantStatusEnum.Suspended : TenantStatusEnum.Archived;
        var expectedOperator = results[0] ? suspendedBy : archivedBy;

        await Assert.That(savedTenant.TenantStatusId).IsEqualTo((int)expectedStatus);
        await Assert.That(savedTenant.UpdatedBy).IsEqualTo(expectedOperator);
        await Assert.That(savedTenant.UpdatedAt).IsEqualTo(transitionedAt);
    }

    private async Task<Guid> SeedActiveTenantAsync()
    {
        await using var context = fixture.CreateTenantFilteredDbContext();
        var activeStatus = await context.TenantStatuses.SingleAsync(status => status.Id == (int)TenantStatusEnum.Active);
        var tenant = new Tenant
        {
            FullName = "Lifecycle CAS Tenant",
            Slug = $"lifecycle-cas-{Guid.NewGuid():N}",
            TenantStatusId = activeStatus.Id,
            TenantStatus = activeStatus
        };

        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static TenantLifecycleLog CreateLifecycleLog(
        Guid tenantId,
        Guid operatorId,
        int newStatusId,
        DateTime transitionedAt) => new()
        {
            TenantId = tenantId,
            Tenant = null!,
            OldStatusId = (int)TenantStatusEnum.Active,
            NewStatusId = newStatusId,
            NewStatus = null!,
            TransitionedByUserId = operatorId,
            Reason = "Lifecycle persistence test",
            TransitionedAt = transitionedAt,
            CreatedAt = transitionedAt,
            CreatedBy = operatorId
        };

    private static DateTime TruncateToMicroseconds(DateTime value) => new(
        value.Ticks - value.Ticks % TimeSpan.TicksPerMicrosecond,
        DateTimeKind.Utc);
}
