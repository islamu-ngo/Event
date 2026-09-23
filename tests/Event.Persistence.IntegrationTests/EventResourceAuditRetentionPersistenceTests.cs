using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

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
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(first, second, expired, retained, disabled);
            await seed.SaveChangesAsync();
        }
        var governance = Substitute.For<IEventResourceGovernancePolicyReader>();
        governance.ReadAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var policy = EventResourceGovernancePolicy.Default(long.MaxValue);
            return EventResourceGovernancePolicy.Create(policy.EnabledDeliveryTypes, policy.EnabledAudiences,
                policy.PermittedFileTypes, policy.MaxUploadBytes, false, [],
                call.Arg<Guid>() == scope.TenantBId ? 0 : 30, policy.MaxActiveResources, long.MaxValue);
        });
        await using var context = database.CreateContext();
        var repository = new EventResourceAuditRetentionRepository(context);
        var service = new EventResourceAuditRetentionService(repository, governance, new EfCoreUnitOfWork(context), new Clock());
        await Assert.That(await service.CleanupAsync(default)).IsEqualTo(2L);
        await using var verification = database.CreateContext();
        var remaining = await verification.Set<EventResourceAuditEntry>().AsNoTracking()
            .Where(row => row.EventResourceId == first.Id || row.EventResourceId == second.Id).ToArrayAsync();
        await Assert.That(remaining.Select(row => row.Id)).IsEquivalentTo([retained.Id]);
        await Assert.That(remaining.Single().ResponsibleManagerUserId).IsEqualTo(retained.ResponsibleManagerUserId);
        await Assert.That(await verification.EventResources.CountAsync(row => row.Id == first.Id || row.Id == second.Id)).IsEqualTo(2);
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
        await using (var seed = database.CreateContext())
        {
            seed.AddRange(authored, shared, unrelated, subjectAudit, otherAudit);
            await seed.SaveChangesAsync();
        }
        await using (var context = database.CreateContext())
        {
            var erasure = new UserLocationPrivacyErasureRepository(context);
            await new EfCoreUnitOfWork(context).ExecuteSerializableAsync(async token =>
            {
                await erasure.AnonymizeRetainedAuditEvidenceAsync(subject, token);
                return true;
            });
        }
        await using var verification = database.CreateContext();
        var first = await verification.EventResources.AsNoTracking().SingleAsync(row => row.Id == authored.Id);
        var second = await verification.EventResources.AsNoTracking().SingleAsync(row => row.Id == shared.Id);
        await Assert.That(first.CreatedBy).IsNull();
        await Assert.That(first.UpdatedBy).IsEqualTo(other);
        await Assert.That(first.ConcurrencyStamp).IsNotEqualTo(authored.ConcurrencyStamp);
        await Assert.That(second.CreatedBy).IsEqualTo(other);
        await Assert.That(second.UpdatedBy).IsEqualTo(other);
        await Assert.That(second.ConcurrencyStamp).IsNotEqualTo(shared.ConcurrencyStamp);
        await Assert.That(first.IsDeleted || second.IsDeleted).IsFalse();
        await Assert.That((await verification.Set<EventResourceAuditEntry>().AsNoTracking()
            .SingleAsync(row => row.Id == subjectAudit.Id)).ResponsibleManagerUserId).IsNull();
        await Assert.That((await verification.Set<EventResourceAuditEntry>().AsNoTracking()
            .SingleAsync(row => row.Id == otherAudit.Id)).ResponsibleManagerUserId).IsEqualTo(other);
        await Assert.That((await verification.EventResources.AsNoTracking()
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
