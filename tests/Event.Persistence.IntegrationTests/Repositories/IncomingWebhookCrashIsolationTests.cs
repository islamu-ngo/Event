using System.Security.Cryptography;
using System.Text;
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
public sealed class IncomingWebhookCrashIsolationTests(PostgreSqlContainerFixture fixture)
{
    private const string StableEffectKind = "incoming.crash-isolation.effect";

    [Test]
    public async Task CancelledWorker_RecoversExpiredClaimOnceWithoutCrossTenantEffect()
    {
        await fixture.ResetAsync();
        var seeded = await SeedTwoTenantsAsync();
        var claimedAt = DateTime.UtcNow.AddMinutes(1);
        var claims = await ClaimAsync("crash-isolation-initial", claimedAt, batchSize: 2, TimeSpan.FromSeconds(30));
        var claimA = claims.Single(claim => claim.TenantId == seeded.TenantAId);
        var claimB = claims.Single(claim => claim.TenantId == seeded.TenantBId);
        var gate = new ConcurrentWorkerGate(participantCount: 2);
        using var cancellation = new CancellationTokenSource();

        await using var contextA = fixture.CreateDbContext();
        await using var contextB = fixture.CreateDbContext();
        var serviceA = CreateService(
            contextA,
            new TenantBoundEffectHandler(contextA, seeded.TenantAId, seeded.MessageAId, gate, cancellation),
            claimedAt.AddSeconds(1));
        var serviceB = CreateService(
            contextB,
            new TenantBoundEffectHandler(contextB, seeded.TenantBId, seeded.MessageBId, gate),
            claimedAt.AddSeconds(1));

        var cancelledExecution = serviceA.ProcessAsync(claimA, cancellation.Token);
        var completedExecution = serviceB.ProcessAsync(claimB, CancellationToken.None);

        await Assert.That(() => cancelledExecution).Throws<OperationCanceledException>();
        var tenantBResult = await completedExecution;
        await Assert.That(tenantBResult.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);

        await using (var forgedContext = fixture.CreateDbContext())
        {
            var forgedClaim = claimA with { TenantId = seeded.TenantBId };
            var forgedHandler = new FailIfInvokedHandler();
            var forgedService = CreateService(forgedContext, forgedHandler, claimedAt.AddSeconds(2));

            var forgedResult = await forgedService.ProcessAsync(forgedClaim, CancellationToken.None);

            await Assert.That(forgedResult.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.LeaseLost);
            await Assert.That(forgedHandler.InvocationCount).IsEqualTo(0);
        }

        var recoveredAt = claimedAt.AddSeconds(31);
        var recoveredClaim = (await ClaimAsync(
            "crash-isolation-recovery",
            recoveredAt,
            batchSize: 2,
            TimeSpan.FromMinutes(1))).Single();
        await Assert.That(recoveredClaim.TenantId).IsEqualTo(seeded.TenantAId);
        await Assert.That(recoveredClaim.IncomingWebhookMessageId).IsEqualTo(seeded.MessageAId);
        await Assert.That(recoveredClaim.ProcessingFence).IsGreaterThan(claimA.ProcessingFence);

        await using (var recoveryContext = fixture.CreateDbContext())
        {
            var recoveryService = CreateService(
                recoveryContext,
                new TenantBoundEffectHandler(recoveryContext, seeded.TenantAId, seeded.MessageAId),
                recoveredAt.AddSeconds(1));
            var recoveryResult = await recoveryService.ProcessAsync(recoveredClaim, CancellationToken.None);
            await Assert.That(recoveryResult.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var messages = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(message => message.ProcessingAttempts)
            .Where(message => message.Id == seeded.MessageAId || message.Id == seeded.MessageBId)
            .ToDictionaryAsync(message => message.Id);
        var receipts = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(receipt => receipt.IncomingWebhookMessageId == seeded.MessageAId ||
                              receipt.IncomingWebhookMessageId == seeded.MessageBId)
            .ToListAsync();
        var outboxIds = await verificationContext.OutboxMessages
            .AsNoTracking()
            .Where(message => message.EventType == StableEffectKind)
            .Select(message => message.AggregateId)
            .ToListAsync();

        await Assert.That(messages[seeded.MessageAId].Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(messages[seeded.MessageBId].Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(messages[seeded.MessageAId].ProcessingAttempts.Select(attempt => attempt.Outcome))
            .IsEquivalentTo(new[]
            {
                IncomingWebhookProcessingAttemptOutcome.Claimed,
                IncomingWebhookProcessingAttemptOutcome.LeaseExpired,
                IncomingWebhookProcessingAttemptOutcome.Claimed,
                IncomingWebhookProcessingAttemptOutcome.Processed
            });
        await Assert.That(messages[seeded.MessageBId].ProcessingAttempts.Select(attempt => attempt.Outcome))
            .IsEquivalentTo(new[]
            {
                IncomingWebhookProcessingAttemptOutcome.Claimed,
                IncomingWebhookProcessingAttemptOutcome.Processed
            });
        await Assert.That(receipts.Select(receipt => (receipt.TenantId, receipt.IncomingWebhookMessageId)))
            .IsEquivalentTo(new[]
            {
                (seeded.TenantAId, seeded.MessageAId),
                (seeded.TenantBId, seeded.MessageBId)
            });
        await Assert.That(outboxIds).IsEquivalentTo(new[] { seeded.MessageAId, seeded.MessageBId });
    }

    private async Task<SeededTenants> SeedTwoTenantsAsync()
    {
        var tenantA = CreateTenant("crash-isolation-a");
        var tenantB = CreateTenant("crash-isolation-b");
        var messageA = CreateIncomingMessage(tenantA.Id, tenantB.Id, "crash-isolation-a");
        var messageB = CreateIncomingMessage(tenantB.Id, tenantA.Id, "crash-isolation-b");

        await using var context = fixture.CreateDbContext();
        context.Tenants.AddRange(tenantA, tenantB);
        context.IncomingWebhookMessages.AddRange(messageA, messageB);
        await context.SaveChangesAsync();
        return new SeededTenants(tenantA.Id, tenantB.Id, messageA.Id, messageB.Id);
    }

    private async Task<IReadOnlyList<IncomingWebhookClaim>> ClaimAsync(
        string leaseOwner,
        DateTime claimedAt,
        int batchSize,
        TimeSpan leaseDuration)
    {
        await using var context = fixture.CreateDbContext();
        return await new IncomingWebhookMessageRepository(context).ClaimDueAsync(
            new IncomingWebhookClaimRequest(leaseOwner, batchSize, claimedAt, leaseDuration),
            CancellationToken.None);
    }

    private static IncomingWebhookProcessingService CreateService(
        ExploreDbContext context,
        IIncomingWebhookHandler handler,
        DateTime observedAt) =>
        new(
            new IncomingWebhookMessageRepository(context),
            new IncomingWebhookEffectReceiptRepository(context),
            new EfCoreUnitOfWork(context),
            [handler],
            Options.Create(new IncomingWebhookProcessingSettings()),
            new FixedTimeProvider(observedAt));

    private static Tenant CreateTenant(string slugPrefix) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FullName = "Incoming Webhook Crash Isolation Tenant",
            Slug = slugPrefix + "-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };

    private static IncomingWebhookMessage CreateIncomingMessage(
        Guid persistedTenantId,
        Guid forgedPayloadTenantId,
        string identity)
    {
        var now = DateTime.UtcNow;
        var payload = Encoding.UTF8.GetBytes(
            "{\"tenantId\":\"" + forgedPayloadTenantId.ToString("D") + "\",\"effect\":\"" + identity + "\"}");
        var payloadHash = "sha256:" + Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        return IncomingWebhookMessage.CreateVerified(
            persistedTenantId,
            "crash-isolation-test",
            identity,
            identity,
            "effect.requested",
            payload,
            payloadHash,
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

    private sealed record SeededTenants(
        Guid TenantAId,
        Guid TenantBId,
        Guid MessageAId,
        Guid MessageBId);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class ConcurrentWorkerGate(int participantCount)
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

    private sealed class TenantBoundEffectHandler(
        ExploreDbContext context,
        Guid expectedTenantId,
        Guid expectedMessageId,
        ConcurrentWorkerGate? gate = null,
        CancellationTokenSource? cancellation = null) : IIncomingWebhookHandler
    {
        public string EffectKind => StableEffectKind;

        public bool CanHandle(string provider, string? eventType) =>
            provider == "crash-isolation-test" && eventType == "effect.requested";

        public async Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext processingContext,
            CancellationToken cancellationToken)
        {
            if (processingContext.TenantId != expectedTenantId ||
                processingContext.IncomingWebhookMessageId != expectedMessageId)
            {
                throw new InvalidOperationException("Processing identity did not come from the persisted claim.");
            }

            context.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = nameof(IncomingWebhookMessage),
                AggregateId = processingContext.IncomingWebhookMessageId,
                EventType = StableEffectKind,
                Status = OutboxMessageStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                MaxRetries = 8
            });

            if (gate is not null)
            {
                await gate.ArriveAsync(cancellationToken);
            }

            if (cancellation is not null)
            {
                cancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return IncomingWebhookProcessingResult.Processed(
                "outbox:" + processingContext.IncomingWebhookMessageId.ToString("N"));
        }
    }

    private sealed class FailIfInvokedHandler : IIncomingWebhookHandler
    {
        public int InvocationCount { get; private set; }

        public string EffectKind => StableEffectKind;

        public bool CanHandle(string provider, string? eventType) => true;

        public Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            throw new InvalidOperationException("A cross-tenant forged claim reached the handler.");
        }
    }
}
