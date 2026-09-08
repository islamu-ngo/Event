using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class IncomingWebhookAtomicEffectTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ProcessedResult_CommitsEffectReceiptAttemptAndSettlementExactlyOnce()
    {
        var seeded = await SeedAndClaimAsync("atomic-success");
        await using var processingContext = fixture.CreateDbContext();
        var service = CreateService(processingContext, seeded.Claim, FailureBoundary.None, seeded.ObservedAt);

        var result = await service.ProcessAsync(seeded.Claim, CancellationToken.None);

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var receipt = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        var outbox = await verificationContext.OutboxMessages
            .AsNoTracking()
            .SingleAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);
        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(message.SettlementSource).IsEqualTo(IncomingWebhookSettlementSource.EffectCommitted);
        await Assert.That(message.SettledByEffectReceiptId).IsEqualTo(receipt.Id);
        await Assert.That(receipt.EffectKind).IsEqualTo(AtomicEffectHandler.StableEffectKind);
        await Assert.That(outbox.EventType).IsEqualTo(AtomicEffectHandler.StableEffectKind);
        await Assert.That(message.ProcessingAttempts.Select(attempt => attempt.Outcome)).IsEquivalentTo(new[]
        {
            IncomingWebhookProcessingAttemptOutcome.Claimed,
            IncomingWebhookProcessingAttemptOutcome.Processed
        });
    }

    [Test]
    [Arguments(FailureBoundary.Handler)]
    [Arguments(FailureBoundary.Receipt)]
    [Arguments(FailureBoundary.Save)]
    public async Task FailureAtTransactionBoundary_RollsBackEffectReceiptAndSettlement(FailureBoundary boundary)
    {
        var seeded = await SeedAndClaimAsync("atomic-rollback-" + boundary.ToString().ToLowerInvariant());
        await using var processingContext = fixture.CreateDbContext();
        var service = CreateService(processingContext, seeded.Claim, boundary, seeded.ObservedAt);

        await Assert.That(async () =>
            await service.ProcessAsync(seeded.Claim, CancellationToken.None))
            .Throws<InjectedAtomicFailureException>();

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var receiptCount = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        var outboxCount = await verificationContext.OutboxMessages
            .CountAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processing);
        await Assert.That(message.SettledByEffectReceiptId).IsNull();
        await Assert.That(message.ProcessingAttempts.Select(attempt => attempt.Outcome))
            .IsEquivalentTo(new[] { IncomingWebhookProcessingAttemptOutcome.Claimed });
        await Assert.That(receiptCount).IsEqualTo(0);
        await Assert.That(outboxCount).IsEqualTo(0);
    }

    [Test]
    public async Task ConcurrentExecutors_CommitOneEffectAndRecordBothExecutions()
    {
        var seeded = await SeedAndClaimAsync("atomic-concurrent-receipt");
        var synchronization = new ConcurrentEffectSynchronization(2);
        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var serviceA = CreateConcurrentService(contextA, seeded.Claim, synchronization, seeded.ObservedAt);
        var serviceB = CreateConcurrentService(contextB, seeded.Claim, synchronization, seeded.ObservedAt.AddMilliseconds(1));

        var results = await Task.WhenAll(
            serviceA.ProcessAsync(seeded.Claim, CancellationToken.None),
            serviceB.ProcessAsync(seeded.Claim, CancellationToken.None));

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var receiptCount = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        var outboxCount = await verificationContext.OutboxMessages
            .CountAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(results.All(result => result.Outcome == IncomingWebhookClaimExecutionOutcome.Completed)).IsTrue();
        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(receiptCount).IsEqualTo(1);
        await Assert.That(outboxCount).IsEqualTo(1);
        await Assert.That(message.ProcessingAttempts.Select(attempt => attempt.Outcome)).IsEquivalentTo(new[]
        {
            IncomingWebhookProcessingAttemptOutcome.Claimed,
            IncomingWebhookProcessingAttemptOutcome.Processed,
            IncomingWebhookProcessingAttemptOutcome.SettledFromReceipt
        });
    }

    private async Task<SeededClaim> SeedAndClaimAsync(string identity)
    {
        await fixture.ResetAsync();
        Guid tenantId;
        await using (var setupContext = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Atomic Incoming Webhook Tenant",
                Slug = identity + "-" + Guid.NewGuid().ToString("N")[..8],
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            tenantId = tenant.Id;
            setupContext.Tenants.Add(tenant);
            setupContext.IncomingWebhookMessages.Add(CreateIncomingMessage(tenant.Id, identity));
            await setupContext.SaveChangesAsync();
        }

        var claimedAt = DateTime.UtcNow.AddMinutes(1);
        await using var claimContext = fixture.CreateDbContext();
        var claimRepository = new IncomingWebhookMessageRepository(claimContext);
        var claim = (await claimRepository.ClaimDueAsync(
            new IncomingWebhookClaimRequest("atomic-effect-worker", 1, claimedAt, TimeSpan.FromMinutes(5)),
            CancellationToken.None)).Single();
        await Assert.That(claim.TenantId).IsEqualTo(tenantId);
        return new SeededClaim(claim, claimedAt.AddSeconds(1));
    }

    private static IncomingWebhookProcessingService CreateService(
        ExploreDbContext dbContext,
        IncomingWebhookClaim claim,
        FailureBoundary boundary,
        DateTime observedAt)
    {
        IIncomingWebhookMessageRepository messageRepository = new IncomingWebhookMessageRepository(dbContext);
        IIncomingWebhookEffectReceiptRepository receiptRepository = new IncomingWebhookEffectReceiptRepository(dbContext);
        if (boundary == FailureBoundary.Save)
        {
            messageRepository = new FailingMessageRepository(messageRepository);
        }

        if (boundary == FailureBoundary.Receipt)
        {
            receiptRepository = new FailingReceiptRepository(receiptRepository);
        }

        return new IncomingWebhookProcessingService(
            messageRepository,
            receiptRepository,
            new EfCoreUnitOfWork(dbContext),
            [new AtomicEffectHandler(dbContext, claim, boundary)],
            Options.Create(new IncomingWebhookProcessingSettings()),
            new FixedTimeProvider(observedAt));
    }

    private static IncomingWebhookProcessingService CreateConcurrentService(
        ExploreDbContext dbContext,
        IncomingWebhookClaim claim,
        ConcurrentEffectSynchronization synchronization,
        DateTime observedAt) =>
        new(
            new IncomingWebhookMessageRepository(dbContext),
            new IncomingWebhookEffectReceiptRepository(dbContext),
            new EfCoreUnitOfWork(dbContext),
            [new ConcurrentEffectHandler(dbContext, claim, synchronization)],
            Options.Create(new IncomingWebhookProcessingSettings()),
            new FixedTimeProvider(observedAt));

    private static IncomingWebhookMessage CreateIncomingMessage(Guid tenantId, string identity)
    {
        var now = DateTime.UtcNow;
        var payload = System.Text.Encoding.UTF8.GetBytes("{\"effect\":\"" + identity + "\"}");
        var hash = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();
        return IncomingWebhookMessage.CreateVerified(
            tenantId,
            "atomic-test",
            identity,
            identity,
            "effect.requested",
            payload,
            hash,
            "application/json",
            "utf-8",
            null,
            now,
            now,
            now.AddDays(14),
            "webhook-retention-test-v1",
            now.AddDays(30),
            now.AddDays(90),
            now.AddDays(14),
            now.AddDays(30));
    }

    public enum FailureBoundary
    {
        None = 0,
        Handler = 1,
        Receipt = 2,
        Save = 3
    }

    private sealed record SeededClaim(IncomingWebhookClaim Claim, DateTime ObservedAt);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class InjectedAtomicFailureException(string boundary)
        : Exception("Injected atomic failure at " + boundary + ".");

    private sealed class AtomicEffectHandler(
        ExploreDbContext dbContext,
        IncomingWebhookClaim claim,
        FailureBoundary boundary) : IIncomingWebhookHandler
    {
        public const string StableEffectKind = "incoming.atomic-test.effect";

        public string EffectKind => StableEffectKind;

        public bool CanHandle(string provider, string? eventType) =>
            provider == "atomic-test" && eventType == "effect.requested";

        public Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken)
        {
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = nameof(IncomingWebhookMessage),
                AggregateId = claim.IncomingWebhookMessageId,
                EventType = StableEffectKind,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 8
            });
            if (boundary == FailureBoundary.Handler)
            {
                throw new InjectedAtomicFailureException("handler");
            }

            return Task.FromResult(IncomingWebhookProcessingResult.Processed("outbox:" + claim.IncomingWebhookMessageId.ToString("N")));
        }
    }

    private sealed class ConcurrentEffectHandler(
        ExploreDbContext dbContext,
        IncomingWebhookClaim claim,
        ConcurrentEffectSynchronization synchronization) : IIncomingWebhookHandler
    {
        public string EffectKind => AtomicEffectHandler.StableEffectKind;

        public bool CanHandle(string provider, string? eventType) =>
            provider == "atomic-test" && eventType == "effect.requested";

        public async Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken)
        {
            dbContext.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = nameof(IncomingWebhookMessage),
                AggregateId = claim.IncomingWebhookMessageId,
                EventType = EffectKind,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 8
            });
            await synchronization.ArriveAsync(cancellationToken);
            return IncomingWebhookProcessingResult.Processed("outbox:" + claim.IncomingWebhookMessageId.ToString("N"));
        }
    }

    private sealed class ConcurrentEffectSynchronization(int participantCount)
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == participantCount)
            {
                _allArrived.TrySetResult();
            }

            await _allArrived.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class FailingReceiptRepository(IIncomingWebhookEffectReceiptRepository inner)
        : IIncomingWebhookEffectReceiptRepository
    {
        public Task<IncomingWebhookEffectReceipt?> GetByIdentityAsync(
            Guid tenantId,
            Guid incomingWebhookMessageId,
            string effectKind,
            CancellationToken cancellationToken) =>
            inner.GetByIdentityAsync(tenantId, incomingWebhookMessageId, effectKind, cancellationToken);

        public async Task AddAsync(IncomingWebhookEffectReceipt receipt, CancellationToken cancellationToken)
        {
            await inner.AddAsync(receipt, cancellationToken);
            throw new InjectedAtomicFailureException("receipt");
        }
    }

    private sealed class FailingMessageRepository(IIncomingWebhookMessageRepository inner)
        : IIncomingWebhookMessageRepository
    {
        public Task<bool> TryCreateAsync(IncomingWebhookMessage message, CancellationToken cancellationToken) =>
            inner.TryCreateAsync(message, cancellationToken);

        public Task<IncomingWebhookMessage?> GetByProviderMessageIdForUpdateAsync(
            Guid tenantId,
            string provider,
            string providerMessageId,
            CancellationToken cancellationToken) =>
            inner.GetByProviderMessageIdForUpdateAsync(tenantId, provider, providerMessageId, cancellationToken);

        public Task<IncomingWebhookMessage?> GetByTenantAndIdForUpdateAsync(
            Guid tenantId,
            Guid incomingWebhookMessageId,
            CancellationToken cancellationToken) =>
            inner.GetByTenantAndIdForUpdateAsync(tenantId, incomingWebhookMessageId, cancellationToken);

        public Task<IReadOnlyList<IncomingWebhookClaim>> ClaimDueAsync(
            IncomingWebhookClaimRequest request,
            CancellationToken cancellationToken) =>
            inner.ClaimDueAsync(request, cancellationToken);

        public Task<IncomingWebhookMessage?> GetActiveClaimAsync(
            Guid tenantId,
            Guid incomingWebhookMessageId,
            Guid leaseToken,
            long processingFence,
            int processingGeneration,
            DateTime observedAt,
            CancellationToken cancellationToken) =>
            inner.GetActiveClaimAsync(
                tenantId,
                incomingWebhookMessageId,
                leaseToken,
                processingFence,
                processingGeneration,
                observedAt,
                cancellationToken);

        public Task<bool> RefreshActiveClaimAsync(
            IncomingWebhookMessage message,
            IncomingWebhookClaim claim,
            DateTime observedAt,
            CancellationToken cancellationToken) =>
            inner.RefreshActiveClaimAsync(message, claim, observedAt, cancellationToken);

        public Task<bool> TryRenewClaimAsync(
            Guid tenantId,
            Guid incomingWebhookMessageId,
            Guid leaseToken,
            long processingFence,
            int processingGeneration,
            DateTime observedAt,
            DateTime leaseExpiresAt,
            CancellationToken cancellationToken) =>
            inner.TryRenewClaimAsync(
                tenantId,
                incomingWebhookMessageId,
                leaseToken,
                processingFence,
                processingGeneration,
                observedAt,
                leaseExpiresAt,
                cancellationToken);

        public void TrackAppendedEvidence(IncomingWebhookMessage message) =>
            inner.TrackAppendedEvidence(message);

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new InjectedAtomicFailureException("save");
    }
}
