using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public class NotificationFanoutRunRepositoryBypassTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task FanoutRunBypasses_WithAmbientTenant_ReturnOnlyExactSourceAndPendingWorkerRows()
    {
        await fixture.ResetAsync();
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("fanout-run-a");
        var tenantB = CreateTenant("fanout-run-b");
        seedContext.Tenants.AddRange(tenantA, tenantB);
        await seedContext.SaveChangesAsync();

        var tenantAActor = CreateActor(tenantA.Id, "Tenant A Fanout Actor");
        var tenantBActor = CreateActor(tenantB.Id, "Tenant B Fanout Actor");
        seedContext.Actors.AddRange(tenantAActor, tenantBActor);
        await seedContext.SaveChangesAsync();

        var tenantAPendingRun = CreateRun(
            tenantA.Id,
            tenantAActor.Id,
            "event-moderation-light",
            "pending",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var tenantBPendingRun = CreateRun(
            tenantB.Id,
            tenantBActor.Id,
            "event-moderation-heavy",
            "pending",
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var tenantACompletedRun = CreateRun(
            tenantA.Id,
            tenantAActor.Id,
            "event-moderation-completed",
            "completed",
            new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc));
        seedContext.NotificationFanoutRuns.AddRange(tenantAPendingRun, tenantBPendingRun, tenantACompletedRun);
        await seedContext.SaveChangesAsync();

        await using var tenantBContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantB.Id));
        var visibleWithoutBypass = await tenantBContext.NotificationFanoutRuns
            .AsNoTracking()
            .Select(run => run.Id)
            .ToListAsync();

        var repository = new NotificationFanoutRunRepository(tenantBContext);
        var tenantAExactSource = await repository.GetBySourceAsync(
            tenantA.Id,
            tenantAPendingRun.FanoutKind,
            tenantAPendingRun.NotificationEntityTypeId,
            tenantAPendingRun.EntityId,
            tenantAPendingRun.SourceActorId);
        var tenantAWrongSourceDoesNotMatch = await repository.GetBySourceAsync(
            tenantA.Id,
            tenantBPendingRun.FanoutKind,
            tenantAPendingRun.NotificationEntityTypeId,
            tenantAPendingRun.EntityId,
            tenantAPendingRun.SourceActorId);
        var pendingWorkerRuns = await repository.GetPendingBatchAsync(pageSize: 10);
        var emptyWorkerBatch = await repository.GetPendingBatchAsync(pageSize: 0);

        await Assert.That(visibleWithoutBypass).IsEquivalentTo([tenantBPendingRun.Id]);
        await Assert.That(tenantAExactSource).IsNotNull();
        await Assert.That(tenantAExactSource!.Id).IsEqualTo(tenantAPendingRun.Id);
        await Assert.That(tenantAWrongSourceDoesNotMatch).IsNull();
        await Assert.That(pendingWorkerRuns.Select(run => run.Id))
            .IsEquivalentTo([tenantAPendingRun.Id, tenantBPendingRun.Id]);
        await Assert.That(emptyWorkerBatch).IsEmpty();
    }

    private static Tenant CreateTenant(string slugPrefix)
    {
        return new Tenant
        {
            FullName = $"Notification Fanout {slugPrefix}",
            Slug = $"{slugPrefix}-{Guid.NewGuid().ToString("N")[..8]}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static Actor CreateActor(Guid tenantId, string displayName)
    {
        var servicePrincipal = new ServicePrincipal
        {
            Id = Guid.CreateVersion7(),
            Code = $"fanout-bypass-{tenantId:N}-{Guid.CreateVersion7():N}",
            DisplayName = displayName,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        return new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Bot,
            ActorType = null!,
            ServicePrincipalId = servicePrincipal.Id,
            ServicePrincipal = servicePrincipal,
            Pii = new ActorPii { DisplayName = displayName },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private static NotificationFanoutRun CreateRun(
        Guid tenantId,
        Guid sourceActorId,
        string fanoutKind,
        string status,
        DateTime createdAt)
    {
        return new NotificationFanoutRun
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FanoutKind = fanoutKind,
            NotificationEntityTypeId = (int)NotificationEntityTypeEnum.Event,
            NotificationEntityType = null!,
            EntityId = Guid.CreateVersion7(),
            SourceActorId = sourceActorId,
            SourceActor = null!,
            Status = status,
            CreatedAt = createdAt,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
