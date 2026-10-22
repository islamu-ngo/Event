using Explore.Persistence.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
[NotInParallel("EventResourceAuditRetention")]
public sealed class EventResourceAuditRetentionPersistenceTests(EventResourcePersistenceTests.TestDatabase database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task SweepRemovesWholeExpiredRowsWithEachTenantsCurrentRetention()
    {
        var scope = await database.SeedScopeAsync();
        var first = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        var second = EventResourcePersistenceTests.CreateDraft(scope.TenantBId, scope.EventBId);
        var expired = Audit(first, Now.AddDays(-30));
        var retained = Audit(first, Now.AddDays(-30).AddMilliseconds(1));
        var disabled = Audit(second, Now.AddDays(1));
        await using (var seed = database.CreateSelectedContext(null))
        {
            await using var humanGate = new Explore.Persistence.Security.HumanSecuritySourceGate(seed);
            await using var human = await humanGate.AcquireAsync();
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(seed);
            await using var discovery = await discoveryGate.AcquireAsync();
            seed.AddRange(first, second, expired, retained, disabled);
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = scope.TenantBId,
                Tenant = null!,
                SettingKey = GovernanceSettingKeys.EventResources.AuditRetentionDays,
                Value = "0"
            });
            await seed.SaveChangesAsync();
        }
        var factory = new EventResourceAuditRetentionManualContextFactory(database.CreateSelectedContext);
        var service = new EventResourceAuditRetentionService(
            new EventResourceAuditOwnerDiscoveryRepository(factory, null),
            new EventResourceAuditRetentionExecutionScopeFactory(factory), new Clock());
        await Assert.That(await service.CleanupAsync(default)).IsEqualTo(2L);
        await using var tenantA = database.CreateSelectedContext(scope.TenantAId);
        await using var tenantB = database.CreateSelectedContext(scope.TenantBId);
        var remainingA = await tenantA.Set<EventResourceAuditEntry>().AsNoTracking()
            .Where(row => row.EventResourceId == first.Id || row.EventResourceId == second.Id).ToArrayAsync();
        var remainingB = await tenantB.Set<EventResourceAuditEntry>().AsNoTracking()
            .Where(row => row.EventResourceId == first.Id || row.EventResourceId == second.Id).ToArrayAsync();
        var remaining = remainingA.Concat(remainingB).ToArray();
        await Assert.That(remaining.Select(row => row.Id)).IsEquivalentTo([retained.Id]);
        await Assert.That(remaining.Single().ResponsibleManagerUserId).IsEqualTo(retained.ResponsibleManagerUserId);
        await Assert.That(await tenantA.EventResources.CountAsync(row => row.Id == first.Id || row.Id == second.Id)
            + await tenantB.EventResources.CountAsync(row => row.Id == first.Id || row.Id == second.Id)).IsEqualTo(2);
    }

    [Test]
    public async Task ZeroRetentionContinuesAcrossFreshTenantDeleteBatches()
    {
        var scope = await database.SeedScopeAsync();
        var resource = EventResourcePersistenceTests.CreateDraft(scope.TenantBId, scope.EventBId);
        var rows = Enumerable.Range(0, EventResourceAuditRetentionService.DeleteBatchSize + 1)
            .Select(_ => Audit(resource, Now.AddDays(1))).ToArray();
        await using (var seed = database.CreateSelectedContext(null))
        {
            await using var humanGate = new Explore.Persistence.Security.HumanSecuritySourceGate(seed);
            await using var human = await humanGate.AcquireAsync();
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(seed);
            await using var discovery = await discoveryGate.AcquireAsync();
            seed.Add(resource);
            seed.AddRange(rows);
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = scope.TenantBId,
                Tenant = null!,
                SettingKey = GovernanceSettingKeys.EventResources.AuditRetentionDays,
                Value = "0"
            });
            await seed.SaveChangesAsync();
        }
        var factory = new EventResourceAuditRetentionManualContextFactory(database.CreateSelectedContext);
        var service = new EventResourceAuditRetentionService(
            new EventResourceAuditOwnerDiscoveryRepository(factory, null),
            new EventResourceAuditRetentionExecutionScopeFactory(factory), new Clock());
        await Assert.That(await service.CleanupAsync(default)).IsEqualTo(1001L);
        await using var verification = database.CreateSelectedContext(scope.TenantBId);
        await Assert.That(await verification.Set<EventResourceAuditEntry>()
            .CountAsync(row => row.EventResourceId == resource.Id)).IsEqualTo(0);
        await Assert.That(await verification.EventResources.CountAsync(row => row.Id == resource.Id)).IsEqualTo(1);
    }

    [Test]
    public async Task ErasureClearsOnlySubjectAttributionAndInvalidatesAffectedSnapshots()
    {
        var scope = await database.SeedScopeAsync();
        Guid subject = Guid.CreateVersion7(), other = Guid.CreateVersion7();
        var authored = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        authored.CreatedBy = subject;
        authored.UpdatedBy = other;
        var shared = EventResourcePersistenceTests.CreateDraft(scope.TenantBId, scope.EventBId);
        shared.CreatedBy = other;
        shared.UpdatedBy = other;
        var unrelated = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        unrelated.CreatedBy = other;
        var subjectAudit = EventResourceAuditEntry.Create(shared.TenantId, shared.Id, subject,
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, Now);
        var otherAudit = EventResourceAuditEntry.Create(shared.TenantId, shared.Id, other,
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, Now);
        await using (var seed = database.CreateSelectedContext(null))
        {
            seed.AddRange(authored, shared, unrelated, subjectAudit, otherAudit);
            await seed.SaveChangesAsync();
        }
        await using (var context = database.CreateSelectedContext(null))
        {
            await using var humanGate = new Explore.Persistence.Security.HumanSecuritySourceGate(context);
            await using var human = await humanGate.AcquireAsync();
            await using var discoveryGate = new EventDiscoveryEnrollmentGate(context);
            await using var discovery = await discoveryGate.AcquireAsync();
            var erasure = new UserLocationPrivacyErasureRepository(context);
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
            {
                await erasure.AnonymizeRetainedAuditEvidenceAsync(subject, token);
                return true;
            });
        }
        await using var tenantA = database.CreateSelectedContext(scope.TenantAId);
        await using var tenantB = database.CreateSelectedContext(scope.TenantBId);
        var first = await tenantA.EventResources.AsNoTracking().SingleAsync(row => row.Id == authored.Id);
        var second = await tenantB.EventResources.AsNoTracking().SingleAsync(row => row.Id == shared.Id);
        await Assert.That(first.CreatedBy).IsNull();
        await Assert.That(first.UpdatedBy).IsEqualTo(other);
        await Assert.That(first.ConcurrencyStamp).IsNotEqualTo(authored.ConcurrencyStamp);
        await Assert.That(second.CreatedBy).IsEqualTo(other);
        await Assert.That(second.UpdatedBy).IsEqualTo(other);
        await Assert.That(second.ConcurrencyStamp).IsNotEqualTo(shared.ConcurrencyStamp);
        await Assert.That(first.IsDeleted || second.IsDeleted).IsFalse();
        await Assert.That((await tenantB.Set<EventResourceAuditEntry>().AsNoTracking()
            .SingleAsync(row => row.Id == subjectAudit.Id)).ResponsibleManagerUserId).IsNull();
        await Assert.That((await tenantB.Set<EventResourceAuditEntry>().AsNoTracking()
            .SingleAsync(row => row.Id == otherAudit.Id)).ResponsibleManagerUserId).IsEqualTo(other);
        await Assert.That((await tenantA.EventResources.AsNoTracking()
            .SingleAsync(row => row.Id == unrelated.Id)).ConcurrencyStamp).IsEqualTo(unrelated.ConcurrencyStamp);
    }

    private static EventResourceAuditEntry Audit(EventResource resource, DateTime occurredAt) =>
        EventResourceAuditEntry.Create(resource.TenantId, resource.Id, Guid.CreateVersion7(),
            EventResourceAuditAction.UpdateMetadata, EventResourceAuditOutcome.Succeeded,
            EventResourceAuditReason.OrganizerMutation, occurredAt);

    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}

internal sealed class EventResourceAuditRetentionManualContextFactory(Func<Guid?, ExploreDbContext> createContext)
    : IGlobalExploreDbContextFactory, ITenantExploreDbContextFactory
{
    public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(createContext(null));
    }

    public Task<ExploreDbContext> CreateDbContextAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(createContext(tenantId));
    }
}
