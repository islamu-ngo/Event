using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.Webhooks.Handlers.Commands;
using Explore.Application.Features.Webhooks.Requests.Commands;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class IncomingWebhookSettlementRecoveryTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExistingReceipt_SettlesWithoutReplayingEffect()
    {
        var seeded = await SeedAndClaimAsync("receipt-recovery");
        await using (var effectContext = fixture.CreateDbContext())
        {
            effectContext.IncomingWebhookEffectReceipts.Add(IncomingWebhookEffectReceipt.Create(
                seeded.Claim.TenantId,
                seeded.Claim.IncomingWebhookMessageId,
                StableEffectKind,
                CreatePayloadHash("receipt-recovery"),
                seeded.Claim.ProcessingGeneration,
                seeded.ObservedAt,
                "outbox:existing"));
            effectContext.OutboxMessages.Add(CreateOutboxMessage(seeded.Claim.IncomingWebhookMessageId));
            await effectContext.SaveChangesAsync();
        }

        await using var processingContext = fixture.CreateDbContext();
        var service = CreateService(
            processingContext,
            new UnexpectedInvocationHandler(),
            new IncomingWebhookProcessingSettings(),
            seeded.ObservedAt.AddSeconds(1));

        var result = await service.ProcessAsync(seeded.Claim, CancellationToken.None);

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var outboxCount = await verificationContext.OutboxMessages
            .CountAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);
        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(message.SettlementSource).IsEqualTo(IncomingWebhookSettlementSource.ExistingReceipt);
        await Assert.That(message.ProcessingAttempts.Select(attempt => attempt.Outcome)).IsEquivalentTo(new[]
        {
            IncomingWebhookProcessingAttemptOutcome.Claimed,
            IncomingWebhookProcessingAttemptOutcome.SettledFromReceipt
        });
        await Assert.That(outboxCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExistingReceipt_WithMismatchedPayloadHash_FailsClosedWithoutReplay()
    {
        var seeded = await SeedAndClaimAsync("receipt-hash-conflict");
        await using (var effectContext = fixture.CreateDbContext())
        {
            effectContext.IncomingWebhookEffectReceipts.Add(IncomingWebhookEffectReceipt.Create(
                seeded.Claim.TenantId,
                seeded.Claim.IncomingWebhookMessageId,
                StableEffectKind,
                CreatePayloadHash("different-payload"),
                seeded.Claim.ProcessingGeneration,
                seeded.ObservedAt,
                "outbox:conflict"));
            await effectContext.SaveChangesAsync();
        }

        await using var processingContext = fixture.CreateDbContext();
        var service = CreateService(
            processingContext,
            new UnexpectedInvocationHandler(),
            new IncomingWebhookProcessingSettings(),
            seeded.ObservedAt.AddSeconds(1));

        await Assert.That(() => service.ProcessAsync(seeded.Claim, CancellationToken.None))
            .Throws<InvalidOperationException>();

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var outboxCount = await verificationContext.OutboxMessages
            .CountAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processing);
        await Assert.That(message.SettledByEffectReceiptId).IsNull();
        await Assert.That(message.ProcessingAttempts.Select(attempt => attempt.Outcome))
            .IsEquivalentTo(new[] { IncomingWebhookProcessingAttemptOutcome.Claimed });
        await Assert.That(outboxCount).IsEqualTo(0);
    }

    [Test]
    public async Task TransientFailure_RetriesThenCommitsEffectExactlyOnce()
    {
        var seeded = await SeedAndClaimAsync("retry-then-process");
        var state = new RetryHandlerState();
        var settings = CreateRetrySettings(maxAttempts: 3);

        await using (var firstContext = fixture.CreateDbContext())
        {
            var service = CreateService(
                firstContext,
                new RetryThenProcessHandler(firstContext, state),
                settings,
                seeded.ObservedAt);
            await service.ProcessAsync(seeded.Claim, CancellationToken.None);
        }

        var retryAt = (await ReadMessageAsync(seeded.Claim.IncomingWebhookMessageId)).NextAttemptAt!.Value;
        var reclaimedAt = retryAt.AddMilliseconds(1);
        var retryClaim = await ClaimSingleAsync("receipt-retry-worker", reclaimedAt);

        await using (var secondContext = fixture.CreateDbContext())
        {
            var service = CreateService(
                secondContext,
                new RetryThenProcessHandler(secondContext, state),
                settings,
                reclaimedAt.AddSeconds(1));
            await service.ProcessAsync(retryClaim, CancellationToken.None);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var receiptCount = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        var outboxCount = await verificationContext.OutboxMessages
            .CountAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(state.InvocationCount).IsEqualTo(2);
        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(message.AttemptCount).IsEqualTo(2);
        await Assert.That(receiptCount).IsEqualTo(1);
        await Assert.That(outboxCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExhaustedAttempts_DeadLettersAndCannotBeClaimedAgain()
    {
        var seeded = await SeedAndClaimAsync("retry-exhausted");
        var settings = CreateRetrySettings(maxAttempts: 2);

        await using (var firstContext = fixture.CreateDbContext())
        {
            var service = CreateService(
                firstContext,
                new AlwaysRetryHandler(),
                settings,
                seeded.ObservedAt);
            await service.ProcessAsync(seeded.Claim, CancellationToken.None);
        }

        var retryAt = (await ReadMessageAsync(seeded.Claim.IncomingWebhookMessageId)).NextAttemptAt!.Value;
        var reclaimedAt = retryAt.AddMilliseconds(1);
        var retryClaim = await ClaimSingleAsync("receipt-dead-letter-worker", reclaimedAt);

        await using (var secondContext = fixture.CreateDbContext())
        {
            var service = CreateService(
                secondContext,
                new AlwaysRetryHandler(),
                settings,
                reclaimedAt.AddSeconds(1));
            await service.ProcessAsync(retryClaim, CancellationToken.None);
        }

        var terminalClaims = await ClaimAsync("receipt-terminal-worker", reclaimedAt.AddHours(1), 10);
        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);

        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.DeadLettered);
        await Assert.That(message.AttemptCount).IsEqualTo(2);
        await Assert.That(message.ProcessingAttempts.Select(attempt => attempt.Outcome)).IsEquivalentTo(new[]
        {
            IncomingWebhookProcessingAttemptOutcome.Claimed,
            IncomingWebhookProcessingAttemptOutcome.RetryScheduled,
            IncomingWebhookProcessingAttemptOutcome.Claimed,
            IncomingWebhookProcessingAttemptOutcome.DeadLettered
        });
        await Assert.That(terminalClaims).IsEmpty();
    }

    [Test]
    public async Task ConflictAndPermanentRejection_AreExcludedFromAutomaticClaims()
    {
        var seeded = await SeedAndClaimAsync("permanent-rejection");
        await using (var processingContext = fixture.CreateDbContext())
        {
            var service = CreateService(
                processingContext,
                new PermanentRejectionHandler(),
                new IncomingWebhookProcessingSettings(),
                seeded.ObservedAt);
            await service.ProcessAsync(seeded.Claim, CancellationToken.None);
        }

        var conflictId = Guid.Empty;
        await using (var conflictContext = fixture.CreateDbContext())
        {
            var conflict = CreateIncomingMessage(seeded.Claim.TenantId, "payload-conflict");
            conflict.ClassifyDuplicate(CreatePayloadHash("changed-payload"), seeded.ObservedAt.AddSeconds(1));
            conflictId = conflict.Id;
            conflictContext.IncomingWebhookMessages.Add(conflict);
            await conflictContext.SaveChangesAsync();
        }

        var claims = await ClaimAsync("receipt-terminal-exclusion-worker", seeded.ObservedAt.AddHours(1), 10);
        await using var verificationContext = fixture.CreateDbContext();
        var statuses = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Where(message =>
                message.Id == seeded.Claim.IncomingWebhookMessageId ||
                message.Id == conflictId)
            .Select(message => new { message.Id, message.StatusId })
            .ToDictionaryAsync(message => message.Id, message => (IncomingWebhookMessageStatus)message.StatusId);

        await Assert.That(statuses[seeded.Claim.IncomingWebhookMessageId])
            .IsEqualTo(IncomingWebhookMessageStatus.RejectedPermanent);
        await Assert.That(statuses[conflictId]).IsEqualTo(IncomingWebhookMessageStatus.PayloadConflict);
        await Assert.That(claims).IsEmpty();
    }

    [Test]
    public async Task LeaseExpiryDuringHandler_RollsBackStagedEffectAndReturnsLeaseLost()
    {
        var seeded = await SeedAndClaimAsync("lease-expired-during-handler");
        var timeProvider = new MutableTimeProvider(seeded.ObservedAt);
        await using var processingContext = fixture.CreateDbContext();
        var service = new IncomingWebhookProcessingService(
            new IncomingWebhookMessageRepository(processingContext),
            new IncomingWebhookEffectReceiptRepository(processingContext),
            new EfCoreUnitOfWork(processingContext),
            [new LeaseExpiringHandler(processingContext, timeProvider)],
            Options.Create(new IncomingWebhookProcessingSettings()),
            timeProvider);

        var result = await service.ProcessAsync(seeded.Claim, CancellationToken.None);

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var receiptCount = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        var outboxCount = await verificationContext.OutboxMessages
            .CountAsync(candidate => candidate.AggregateId == message.Id);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.LeaseLost);
        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processing);
        await Assert.That(receiptCount).IsEqualTo(0);
        await Assert.That(outboxCount).IsEqualTo(0);
    }

    [Test]
    public async Task AuthorizedRedrive_CommitsGenerationRecordAndAuditInOneTransaction()
    {
        var seeded = await SeedAndClaimAsync("authorized-redrive");
        await using (var processingContext = fixture.CreateDbContext())
        {
            var service = CreateService(
                processingContext,
                new AlwaysRetryHandler(),
                CreateRetrySettings(maxAttempts: 1),
                seeded.ObservedAt);
            await service.ProcessAsync(seeded.Claim, CancellationToken.None);
        }

        var actorUserId = Guid.CreateVersion7();
        var redrivenAt = seeded.ObservedAt.AddMinutes(1);
        await using (var redriveContext = fixture.CreateDbContext())
        {
            var currentUser = new StaticCurrentUserService(actorUserId);
            var machinePrincipal = new NoMachinePrincipalAccessor();
            var handler = new RedriveIncomingWebhookCommandHandler(
                new IncomingWebhookMessageRepository(redriveContext),
                new WebhookAuditEventWriter(
                    new WebhookAuditEventRepository(redriveContext),
                    currentUser,
                    machinePrincipal,
                    new FixedWebhookRetentionPolicyResolver(),
                    new FixedTimeProvider(redrivenAt)),
                new EfCoreUnitOfWork(redriveContext),
                currentUser,
                machinePrincipal,
                new FixedTimeProvider(redrivenAt));
            var response = await handler.Handle(new RedriveIncomingWebhookCommand
            {
                TenantId = seeded.Claim.TenantId,
                IncomingWebhookMessageId = seeded.Claim.IncomingWebhookMessageId,
                ExpectedProcessingGeneration = seeded.Claim.ProcessingGeneration,
                Reason = "operator-confirmed-recovery"
            }, CancellationToken.None);

            await Assert.That(response.IsSuccess).IsTrue();
        }

        await using var verificationContext = fixture.CreateDbContext();
        var message = await verificationContext.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.RedriveRecords)
            .SingleAsync(candidate => candidate.Id == seeded.Claim.IncomingWebhookMessageId);
        var audit = await verificationContext.WebhookAuditEvents
            .AsNoTracking()
            .SingleAsync(candidate => candidate.TenantId == seeded.Claim.TenantId &&
                candidate.TargetId == message.Id &&
                candidate.ActionId == (int)WebhookAuditAction.IncomingRedriveScheduled);

        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.RetryDue);
        await Assert.That(message.ProcessingGeneration).IsEqualTo(2);
        await Assert.That(message.RedriveRecords).HasSingleItem();
        await Assert.That(message.RedriveRecords.Single().Result)
            .IsEqualTo(IncomingWebhookRedriveResult.Scheduled);
        await Assert.That(audit.PrincipalReference).IsEqualTo($"user:{actorUserId:D}");
        await Assert.That(audit.SafeAfterJson).DoesNotContain("operator-confirmed-recovery");
    }

    private async Task<SeededClaim> SeedAndClaimAsync(string identity)
    {
        await fixture.ResetAsync();
        Guid tenantId;
        await using (var setupContext = fixture.CreateDbContext())
        {
            await LookupTableSeeder.SeedAsync(setupContext);
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = "Incoming Webhook Recovery Tenant",
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
        var claim = await ClaimSingleAsync("receipt-effect-worker", claimedAt);
        await Assert.That(claim.TenantId).IsEqualTo(tenantId);
        return new SeededClaim(claim, claimedAt.AddSeconds(1));
    }

    private async Task<IncomingWebhookClaim> ClaimSingleAsync(string leaseOwner, DateTime claimedAt) =>
        (await ClaimAsync(leaseOwner, claimedAt, 1)).Single();

    private async Task<IReadOnlyList<IncomingWebhookClaim>> ClaimAsync(
        string leaseOwner,
        DateTime claimedAt,
        int batchSize)
    {
        await using var claimContext = fixture.CreateDbContext();
        return await new IncomingWebhookMessageRepository(claimContext).ClaimDueAsync(
            new IncomingWebhookClaimRequest(leaseOwner, batchSize, claimedAt, TimeSpan.FromMinutes(5)),
            CancellationToken.None);
    }

    private async Task<IncomingWebhookMessage> ReadMessageAsync(Guid messageId)
    {
        await using var context = fixture.CreateDbContext();
        return await context.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(message => message.Id == messageId);
    }

    private static IncomingWebhookProcessingService CreateService(
        ExploreDbContext dbContext,
        IIncomingWebhookHandler handler,
        IncomingWebhookProcessingSettings settings,
        DateTime observedAt) =>
        new(
            new IncomingWebhookMessageRepository(dbContext),
            new IncomingWebhookEffectReceiptRepository(dbContext),
            new EfCoreUnitOfWork(dbContext),
            [handler],
            Options.Create(settings),
            new FixedTimeProvider(observedAt));

    private static IncomingWebhookProcessingSettings CreateRetrySettings(int maxAttempts) =>
        new()
        {
            MaxAttempts = maxAttempts,
            InitialRetryDelaySeconds = 1,
            MaxRetryDelaySeconds = 10
        };

    private static IncomingWebhookMessage CreateIncomingMessage(Guid tenantId, string identity)
    {
        var now = DateTime.UtcNow;
        var payload = CreatePayload(identity);
        return IncomingWebhookMessage.CreateVerified(
            tenantId,
            "atomic-test",
            identity,
            identity,
            "effect.requested",
            payload,
            CreatePayloadHash(identity),
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

    private static byte[] CreatePayload(string identity) =>
        System.Text.Encoding.UTF8.GetBytes("{\"effect\":\"" + identity + "\"}");

    private static string CreatePayloadHash(string identity) =>
        "sha256:" + Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(CreatePayload(identity))).ToLowerInvariant();

    private static OutboxMessage CreateOutboxMessage(Guid incomingWebhookMessageId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            AggregateType = nameof(IncomingWebhookMessage),
            AggregateId = incomingWebhookMessageId,
            EventType = StableEffectKind,
            Status = OutboxMessageStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            MaxRetries = 8
        };

    private const string StableEffectKind = "incoming.atomic-test.effect";

    private sealed record SeededClaim(IncomingWebhookClaim Claim, DateTime ObservedAt);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

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

    private sealed class MutableTimeProvider(DateTime utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = new(utcNow, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private abstract class RecoveryHandler : IIncomingWebhookHandler
    {
        public string EffectKind => StableEffectKind;

        public bool CanHandle(string provider, string? eventType) =>
            provider == "atomic-test" && eventType == "effect.requested";

        public abstract Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken);
    }

    private sealed class UnexpectedInvocationHandler : RecoveryHandler
    {
        public override Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A durable receipt must prevent handler replay.");
    }

    private sealed class RetryHandlerState
    {
        public int InvocationCount;
    }

    private sealed class RetryThenProcessHandler(ExploreDbContext dbContext, RetryHandlerState state)
        : RecoveryHandler
    {
        public override Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref state.InvocationCount) == 1)
            {
                return Task.FromResult(IncomingWebhookProcessingResult.RetryDue("transient_dependency"));
            }

            dbContext.OutboxMessages.Add(CreateOutboxMessage(context.IncomingWebhookMessageId));
            return Task.FromResult(IncomingWebhookProcessingResult.Processed("outbox:retry-success"));
        }
    }

    private sealed class AlwaysRetryHandler : RecoveryHandler
    {
        public override Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(IncomingWebhookProcessingResult.RetryDue("transient_dependency"));
    }

    private sealed class PermanentRejectionHandler : RecoveryHandler
    {
        public override Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken) =>
            Task.FromResult(IncomingWebhookProcessingResult.RejectedPermanent("invalid_contract"));
    }

    private sealed class LeaseExpiringHandler(
        ExploreDbContext dbContext,
        MutableTimeProvider timeProvider) : RecoveryHandler
    {
        public override Task<IncomingWebhookProcessingResult> HandleAsync(
            IncomingWebhookProcessingContext context,
            CancellationToken cancellationToken)
        {
            dbContext.OutboxMessages.Add(CreateOutboxMessage(context.IncomingWebhookMessageId));
            timeProvider.Advance(TimeSpan.FromMinutes(10));
            return Task.FromResult(IncomingWebhookProcessingResult.Processed("outbox:stale-lease"));
        }
    }
}
