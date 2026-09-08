using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class WebhookAuditEventRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task AppendAsync_PersistsNormalizedForeignKeysAndDatabaseTimestamp()
    {
        var tenantId = await ResetAndSeedTenantAsync("audit-append");
        await using var context = fixture.CreateDbContext();
        var auditEvent = CreateAuditEvent(tenantId);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await new WebhookAuditEventRepository(context).AppendAsync(auditEvent, CancellationToken.None);

        var after = DateTime.UtcNow.AddSeconds(1);
        var persisted = await context.WebhookAuditEvents
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == auditEvent.Id);
        await Assert.That(persisted.OccurredAt).IsBetween(before, after);
        await Assert.That(persisted.ActionId).IsEqualTo((int)WebhookAuditAction.EndpointUpdated);
        await Assert.That(persisted.OutcomeId).IsEqualTo((int)WebhookAuditOutcome.Succeeded);
        await Assert.That(persisted.PrincipalKindId).IsEqualTo((int)WebhookAuditPrincipalKind.User);
        await Assert.That(persisted.EffectiveScopeKindId).IsEqualTo((int)WebhookAuditScopeKind.Tenant);
        await Assert.That(persisted.TargetKindId).IsEqualTo((int)WebhookAuditTargetKind.Endpoint);
    }

    [Test]
    public async Task SaveChanges_WhenAuditEventIsUpdatedOrDeleted_RejectsMutation()
    {
        var tenantId = await ResetAndSeedTenantAsync("audit-immutable");
        await using var context = fixture.CreateDbContext();
        var auditEvent = CreateAuditEvent(tenantId);
        await new WebhookAuditEventRepository(context).AppendAsync(auditEvent, CancellationToken.None);

        context.Entry(auditEvent).Property(candidate => candidate.ReasonCode).CurrentValue = "changed_reason";
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());

        context.ChangeTracker.Clear();
        var persisted = await context.WebhookAuditEvents.SingleAsync(candidate => candidate.Id == auditEvent.Id);
        context.WebhookAuditEvents.Remove(persisted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
    }

    [Test]
    public async Task UnitOfWork_WhenMandatoryAuditValidationFails_RollsBackBusinessWrite()
    {
        var tenantId = await ResetAndSeedTenantAsync("audit-rollback");
        var consumerId = Guid.CreateVersion7();
        await using (var context = fixture.CreateDbContext())
        {
            var consumerRepository = new WebhookConsumerRepository(context);
            var currentUser = new StaticCurrentUserService(Guid.CreateVersion7());
            var auditWriter = new WebhookAuditEventWriter(
                new WebhookAuditEventRepository(context),
                currentUser,
                new NoMachinePrincipalAccessor(),
                new FixedWebhookRetentionPolicyResolver(),
                TimeProvider.System);
            var unitOfWork = new EfCoreUnitOfWork(context);

            await Assert.ThrowsAsync<ArgumentException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await consumerRepository.CreateAsync(new WebhookConsumer
                {
                    Id = consumerId,
                    TenantId = tenantId,
                    ConsumerKind = WebhookConsumerKind.Tenant,
                    Name = "Rollback consumer",
                    Status = WebhookConsumerStatus.Active,
                    ProviderMode = WebhookProviderMode.Local,
                    ConfigurationVersion = 1,
                    CreatedAt = DateTime.UtcNow
                }, token);
                await auditWriter.AppendAsync(
                    new WebhookAuditWriteRequest(
                        tenantId,
                        WebhookAuditAction.ConsumerCreated,
                        WebhookAuditTargetKind.Consumer,
                        consumerId,
                        "consumer_created",
                        WebhookAuditOutcome.Succeeded,
                        SafeAfterJson: "{\"payload\":{}}"),
                    token);
            }, CancellationToken.None));
        }

        await using var verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.WebhookConsumers
            .CountAsync(candidate => candidate.Id == consumerId)).IsEqualTo(0);
        await Assert.That(await verificationContext.WebhookAuditEvents
            .CountAsync(candidate => candidate.TargetId == consumerId)).IsEqualTo(0);
    }

    private async Task<Guid> ResetAndSeedTenantAsync(string identity)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        await LookupTableSeeder.SeedAsync(context);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Webhook Audit Tenant",
            Slug = $"{identity}-{Guid.NewGuid():N}"[..Math.Min(identity.Length + 9, 100)],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);
        await context.SaveChangesAsync();
        return tenant.Id;
    }

    private static WebhookAuditEvent CreateAuditEvent(Guid tenantId) =>
        WebhookAuditEvent.Create(
            tenantId,
            WebhookAuditPrincipalKind.User,
            "user:018f0000-0000-7000-8000-000000000001",
            WebhookAuditScopeKind.Tenant,
            tenantId,
            WebhookAuditAction.EndpointUpdated,
            WebhookAuditTargetKind.Endpoint,
            Guid.CreateVersion7(),
            "{\"configurationVersion\":1}",
            "{\"configurationVersion\":2,\"destinationHost\":\"integrator.example\"}",
            "endpoint-v2",
            "trace-123",
            "pending_work_preserve_existing",
            WebhookAuditOutcome.Succeeded,
            "webhook-retention-test-v1",
            DateTime.UtcNow.AddDays(365));

    private sealed class StaticCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;

        public bool IsAuthenticated => true;
    }

    private sealed class NoMachinePrincipalAccessor : IMachinePrincipalAccessor
    {
        public ApiKeyPrincipalContext? Current => null;

        public bool IsMachineCaller => false;
    }
}
