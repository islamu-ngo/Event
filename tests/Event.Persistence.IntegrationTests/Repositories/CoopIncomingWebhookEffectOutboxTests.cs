// ABOUTME: PostgreSQL tests for durable Coop callback pointers and their inbox retention dependency.
// ABOUTME: Proves atomic settlement, replay identity, tenant-safe constraints, rollback, and payload cleanup ordering.

using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services.Webhooks;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Secrets.Database;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class CoopIncomingWebhookEffectOutboxTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ProcessAsync_ValidCoopDecision_CommitsPointerWithoutAppliedEffectReceipt()
    {
        var seeded = await SeedAndClaimAsync("coop-pointer-success");
        await using var processingContext = fixture.CreateDbContext();
        var service = CreateService(processingContext, seeded.ObservedAt);

        var result = await service.ProcessAsync(seeded.Claim, CancellationToken.None);

        await using var verificationContext = fixture.CreateDbContext();
        var message = await LoadMessageAsync(verificationContext, seeded.Claim.IncomingWebhookMessageId);
        var pointer = await verificationContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        var receiptCount = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);

        await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);
        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processed);
        await Assert.That(message.SettlementSource).IsEqualTo(IncomingWebhookSettlementSource.None);
        await Assert.That(message.SettledByEffectReceiptId).IsNull();
        await Assert.That(receiptCount).IsEqualTo(0);
        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Pending);
        await Assert.That(pointer.ProviderDecisionId).IsEqualTo(seeded.ProviderDecisionId);
        await Assert.That(pointer.PayloadSha256).IsEqualTo(message.PayloadHash);
        await Assert.That(typeof(IncomingWebhookEffectOutbox).GetProperties()
            .Any(property => property.Name.Contains("PayloadBytes", StringComparison.Ordinal) ||
                             property.Name.Contains("RawPayload", StringComparison.Ordinal))).IsFalse();
    }

    [Test]
    public async Task ProcessAsync_SaveFailure_RollsBackPointerAndInboxSettlement()
    {
        var seeded = await SeedAndClaimAsync("coop-pointer-rollback");
        await using var processingContext = fixture.CreateDbContext();
        var innerMessageRepository = new IncomingWebhookMessageRepository(processingContext);
        var service = new IncomingWebhookProcessingService(
            new FailingSaveMessageRepository(innerMessageRepository),
            new IncomingWebhookEffectReceiptRepository(processingContext),
            new EfCoreUnitOfWork(processingContext),
            [new CoopDecisionIncomingWebhookHandler(new IncomingWebhookEffectOutboxRepository(processingContext))],
            Options.Create(new IncomingWebhookProcessingSettings()),
            new FixedTimeProvider(seeded.ObservedAt));

        await Assert.ThrowsAsync<InjectedPointerFailureException>(() =>
            service.ProcessAsync(seeded.Claim, CancellationToken.None));

        await using var verificationContext = fixture.CreateDbContext();
        var message = await LoadMessageAsync(verificationContext, seeded.Claim.IncomingWebhookMessageId);
        var pointerCount = await verificationContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);

        await Assert.That(message.Status).IsEqualTo(IncomingWebhookMessageStatus.Processing);
        await Assert.That(message.SettledByEffectReceiptId).IsNull();
        await Assert.That(pointerCount).IsEqualTo(0);
    }

    [Test]
    public async Task HandleAsync_ExistingProviderDecision_DeduplicatesExactHashAndRejectsChangedHash()
    {
        await fixture.ResetAsync();
        var receivedAt = DateTime.UtcNow.AddMinutes(-5);
        var tenant = CreateTenant("coop-pointer-replay");
        var original = CreateIncomingMessage(tenant.Id, "decision-replay", "same-content", receivedAt);
        var pointer = IncomingWebhookEffectOutbox.CreatePending(
            tenant.Id,
            original.Id,
            "coop",
            original.ProviderMessageId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            original.PayloadHash,
            receivedAt.AddSeconds(1));
        await using (var setupContext = fixture.CreateDbContext())
        {
            setupContext.Tenants.Add(tenant);
            setupContext.IncomingWebhookMessages.Add(original);
            setupContext.IncomingWebhookEffectOutboxes.Add(pointer);
            await setupContext.SaveChangesAsync();
        }

        await using var handlerContext = fixture.CreateDbContext();
        var handler = new CoopDecisionIncomingWebhookHandler(
            new IncomingWebhookEffectOutboxRepository(handlerContext));
        var exactReplayContext = CreateClaimedProcessingContext(
            tenant.Id,
            "decision-replay",
            "same-content",
            receivedAt.AddMinutes(1));
        var conflictingReplayContext = CreateClaimedProcessingContext(
            tenant.Id,
            "decision-replay",
            "changed-content",
            receivedAt.AddMinutes(2));

        var exact = await handler.HandleAsync(exactReplayContext, CancellationToken.None);
        var conflict = await handler.HandleAsync(conflictingReplayContext, CancellationToken.None);

        await Assert.That(exact.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.PointerPersisted);
        await Assert.That(conflict.Outcome).IsEqualTo(IncomingWebhookProcessingOutcome.RejectedPermanent);
        await Assert.That(conflict.FailureCategory).IsEqualTo("coop_provider_decision_payload_conflict");
        await Assert.That(await handlerContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task DatabaseConstraints_RejectDuplicateProviderDecisionAndDuplicateMessageEffect()
    {
        var seeded = await SeedConstraintRowsAsync();

        await using (var duplicateProviderContext = fixture.CreateDbContext())
        {
            duplicateProviderContext.IncomingWebhookEffectOutboxes.Add(IncomingWebhookEffectOutbox.CreatePending(
                seeded.TenantAId,
                seeded.MessageBId,
                "coop",
                seeded.ProviderDecisionId,
                CoopDecisionIncomingWebhookHandler.StableEffectKind,
                HashPayload("other-content"),
                seeded.CreatedAt.AddSeconds(2)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateProviderContext.SaveChangesAsync());
        }

        await using (var duplicateMessageEffectContext = fixture.CreateDbContext())
        {
            duplicateMessageEffectContext.IncomingWebhookEffectOutboxes.Add(IncomingWebhookEffectOutbox.CreatePending(
                seeded.TenantAId,
                seeded.MessageAId,
                "coop",
                "different-provider-decision",
                CoopDecisionIncomingWebhookHandler.StableEffectKind,
                HashPayload("other-content"),
                seeded.CreatedAt.AddSeconds(3)));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateMessageEffectContext.SaveChangesAsync());
        }
    }

    [Test]
    public async Task DatabaseConstraints_RejectCrossTenantInboxReferenceAndRetainedInboxDeletion()
    {
        var seeded = await SeedConstraintRowsAsync();

        await using (var crossTenantContext = fixture.CreateDbContext())
        {
            crossTenantContext.IncomingWebhookEffectOutboxes.Add(IncomingWebhookEffectOutbox.CreatePending(
                seeded.TenantBId,
                seeded.MessageBId,
                "coop",
                "cross-tenant-decision",
                CoopDecisionIncomingWebhookHandler.StableEffectKind,
                HashPayload("cross-tenant"),
                seeded.CreatedAt.AddSeconds(2)));
            await Assert.ThrowsAsync<DbUpdateException>(() => crossTenantContext.SaveChangesAsync());
        }

        await using (var deleteContext = fixture.CreateDbContext())
        {
            var message = await deleteContext.IncomingWebhookMessages
                .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
                .SingleAsync(candidate => candidate.Id == seeded.MessageAId);
            deleteContext.IncomingWebhookMessages.Remove(message);
            await Assert.ThrowsAsync<DbUpdateException>(() => deleteContext.SaveChangesAsync());
        }
    }

    [Test]
    public async Task CleanupTenantAsync_PendingEffectPointerRetainsCallbackPayload()
    {
        await fixture.ResetAsync();
        var cleanupAt = new DateTime(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);
        var receivedAt = cleanupAt.AddDays(-100);
        var tenant = CreateTenant("coop-pointer-retention");
        var message = CreateIncomingMessage(tenant.Id, "decision-retention", "retained-content", receivedAt);
        var leaseToken = Guid.CreateVersion7();
        message.Claim("coop-retention-test", leaseToken, receivedAt.AddHours(1), receivedAt.AddMinutes(1));
        message.SettlePointerPersisted(
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            leaseToken,
            message.ProcessingFence,
            message.ProcessingGeneration,
            receivedAt.AddMinutes(2));
        var pointer = IncomingWebhookEffectOutbox.CreatePending(
            tenant.Id,
            message.Id,
            "coop",
            message.ProviderMessageId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            message.PayloadHash,
            receivedAt.AddMinutes(2));
        await using (var setupContext = fixture.CreateDbContext())
        {
            setupContext.Tenants.Add(tenant);
            setupContext.IncomingWebhookMessages.Add(message);
            setupContext.IncomingWebhookEffectOutboxes.Add(pointer);
            await setupContext.SaveChangesAsync();
        }

        await using (var cleanupContext = fixture.CreateDbContext())
        {
            var result = await new WebhookRetentionCleanupRepository(cleanupContext).CleanupTenantAsync(
                tenant.Id,
                cleanupAt,
                batchSize: 100,
                dryRun: false,
                CancellationToken.None);
            await Assert.That(result.InboundPayloadsCleared).IsEqualTo(0);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var retained = await LoadMessageAsync(verificationContext, message.Id);
        await Assert.That(retained.PayloadBytes.IsEmpty).IsFalse();
        await Assert.That(retained.PayloadClearedAt).IsNull();
    }

    [Test]
    public async Task ClaimDueAsync_ConcurrentWorkersClaimOnePointerExactlyOnce()
    {
        var seeded = await SeedConstraintRowsAsync();
        var claimedAt = seeded.CreatedAt.AddMinutes(1);
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var firstRepository = new IncomingWebhookEffectOutboxRepository(firstContext);
        var secondRepository = new IncomingWebhookEffectOutboxRepository(secondContext);

        var claims = await Task.WhenAll(
            firstRepository.ClaimDueAsync(
                new IncomingWebhookEffectClaimRequest(
                    "coop-effect-worker-a",
                    1,
                    claimedAt,
                    TimeSpan.FromMinutes(2)),
                CancellationToken.None),
            secondRepository.ClaimDueAsync(
                new IncomingWebhookEffectClaimRequest(
                    "coop-effect-worker-b",
                    1,
                    claimedAt,
                    TimeSpan.FromMinutes(2)),
                CancellationToken.None));

        await Assert.That(claims.Sum(batch => batch.Count)).IsEqualTo(1);
        await using var verificationContext = fixture.CreateDbContext();
        var pointer = await verificationContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(pointer.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(pointer.AttemptCount).IsEqualTo(1);
        await Assert.That(pointer.ProcessingFence).IsEqualTo(1);
    }

    [Test]
    public async Task ClaimDueAsync_ExpiredLeaseIsRecoveredWithNewFenceAndToken()
    {
        var seeded = await SeedConstraintRowsAsync();
        var firstClaimedAt = seeded.CreatedAt.AddMinutes(1);
        IncomingWebhookEffectClaim firstClaim;
        await using (var firstContext = fixture.CreateDbContext())
        {
            firstClaim = (await new IncomingWebhookEffectOutboxRepository(firstContext).ClaimDueAsync(
                new IncomingWebhookEffectClaimRequest(
                    "coop-effect-worker-a",
                    1,
                    firstClaimedAt,
                    TimeSpan.FromSeconds(30)),
                CancellationToken.None)).Single();
        }

        IncomingWebhookEffectClaim recoveredClaim;
        await using (var recoveryContext = fixture.CreateDbContext())
        {
            recoveredClaim = (await new IncomingWebhookEffectOutboxRepository(recoveryContext).ClaimDueAsync(
                new IncomingWebhookEffectClaimRequest(
                    "coop-effect-worker-b",
                    1,
                    firstClaimedAt.AddMinutes(1),
                    TimeSpan.FromMinutes(2)),
                CancellationToken.None)).Single();
        }

        await Assert.That(recoveredClaim.EffectOutboxId).IsEqualTo(firstClaim.EffectOutboxId);
        await Assert.That(recoveredClaim.LeaseToken).IsNotEqualTo(firstClaim.LeaseToken);
        await Assert.That(recoveredClaim.ProcessingFence).IsEqualTo(2);
        await using var verificationContext = fixture.CreateDbContext();
        var pointer = await verificationContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync();
        await Assert.That(pointer.AttemptCount).IsEqualTo(2);
        await Assert.That(pointer.ProcessingFence).IsEqualTo(2);
    }

    [Test]
    public async Task EffectProcessAsync_CommandSuccessAtomicallyCreatesReceiptAndCompletesPointer()
    {
        await fixture.ResetAsync();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        var tenant = CreateTenant("coop-effect-settlement");
        const string providerDecisionId = "decision-effect-settlement";
        var payload = Encoding.UTF8.GetBytes($$"""
            {
              "tenantId": "{{tenant.Id}}",
              "eventId": "{{Guid.CreateVersion7()}}",
              "reportId": "{{Guid.CreateVersion7()}}",
              "caseId": "{{Guid.CreateVersion7()}}",
              "providerDecisionId": "{{providerDecisionId}}",
              "action": { "id": "allow" }
            }
            """);
        var message = IncomingWebhookMessage.CreateVerified(
            tenant.Id,
            "coop",
            providerDecisionId,
            providerDecisionId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            payload,
            HashPayload(payload),
            "application/json",
            "utf-8",
            null,
            createdAt,
            createdAt,
            createdAt.AddDays(14),
            "webhook-retention-test-v1",
            createdAt.AddDays(30),
            createdAt.AddDays(90),
            createdAt.AddDays(14),
            createdAt.AddDays(30));
        var pointer = IncomingWebhookEffectOutbox.CreatePending(
            tenant.Id,
            message.Id,
            "coop",
            providerDecisionId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            message.PayloadHash,
            createdAt.AddSeconds(1));
        await using (var setupContext = fixture.CreateDbContext())
        {
            setupContext.Tenants.Add(tenant);
            setupContext.IncomingWebhookMessages.Add(message);
            setupContext.IncomingWebhookEffectOutboxes.Add(pointer);
            await setupContext.SaveChangesAsync();
        }

        IncomingWebhookEffectClaim claim;
        var claimedAt = createdAt.AddMinutes(1);
        await using (var claimContext = fixture.CreateDbContext())
        {
            claim = (await new IncomingWebhookEffectOutboxRepository(claimContext).ClaimDueAsync(
                new IncomingWebhookEffectClaimRequest(
                    "coop-effect-settlement-worker",
                    1,
                    claimedAt,
                    TimeSpan.FromMinutes(5)),
                CancellationToken.None)).Single();
        }

        await using (var processingContext = fixture.CreateDbContext())
        {
            var service = new IncomingWebhookEffectProcessingService(
                new IncomingWebhookEffectOutboxRepository(processingContext),
                new IncomingWebhookMessageRepository(processingContext),
                new IncomingWebhookEffectReceiptRepository(processingContext),
                new EfCoreUnitOfWork(processingContext),
                new SuccessfulCoopDecisionMediator(),
                Options.Create(new IncomingWebhookProcessingSettings()),
                new FixedTimeProvider(claimedAt.AddSeconds(1)));

            var result = await service.ProcessAsync(claim, CancellationToken.None);
            await Assert.That(result.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);
        }

        await using var verificationContext = fixture.CreateDbContext();
        var settledPointer = await verificationContext.IncomingWebhookEffectOutboxes
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == pointer.Id);
        var receipt = await verificationContext.IncomingWebhookEffectReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .SingleAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        await Assert.That(settledPointer.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(receipt.PayloadHash).IsEqualTo(message.PayloadHash);
        await Assert.That(receipt.EffectKind).IsEqualTo(pointer.EffectKind);
    }

    [Test]
    public async Task CleanupTenantAsync_DeadLetteredEffectRetainsPayloadUntilReplayWindowExpires()
    {
        await fixture.ResetAsync();
        var receivedAt = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var tenant = CreateTenant("coop-effect-deadletter-retention");
        var message = CreateIncomingMessage(
            tenant.Id,
            "decision-deadletter-retention",
            "retained-deadletter-content",
            receivedAt);
        var inboxLease = Guid.CreateVersion7();
        message.Claim("coop-retention-test", inboxLease, receivedAt.AddHours(1), receivedAt.AddMinutes(1));
        message.SettlePointerPersisted(
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            inboxLease,
            message.ProcessingFence,
            message.ProcessingGeneration,
            receivedAt.AddMinutes(2));
        var pointer = IncomingWebhookEffectOutbox.CreatePending(
            tenant.Id,
            message.Id,
            "coop",
            message.ProviderMessageId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            message.PayloadHash,
            receivedAt.AddMinutes(2));
        var pointerLease = Guid.CreateVersion7();
        pointer.Claim("coop-effect-worker", pointerLease, receivedAt.AddHours(1), receivedAt.AddMinutes(3));
        pointer.DeadLetter(
            pointerLease,
            pointer.ProcessingFence,
            pointer.ProcessingGeneration,
            "coop_effect_command_rejected",
            "The local workflow rejected the callback.",
            receivedAt.AddMinutes(4));
        await using (var setupContext = fixture.CreateDbContext())
        {
            setupContext.Tenants.Add(tenant);
            setupContext.IncomingWebhookMessages.Add(message);
            setupContext.IncomingWebhookEffectOutboxes.Add(pointer);
            await setupContext.SaveChangesAsync();
        }

        await using (var beforeExpiryContext = fixture.CreateDbContext())
        {
            var result = await new WebhookRetentionCleanupRepository(beforeExpiryContext).CleanupTenantAsync(
                tenant.Id,
                receivedAt.AddDays(13),
                100,
                false,
                CancellationToken.None);
            await Assert.That(result.InboundPayloadsCleared).IsEqualTo(0);
        }

        await using (var afterExpiryContext = fixture.CreateDbContext())
        {
            var result = await new WebhookRetentionCleanupRepository(afterExpiryContext).CleanupTenantAsync(
                tenant.Id,
                receivedAt.AddDays(31),
                100,
                false,
                CancellationToken.None);
            await Assert.That(result.InboundPayloadsCleared).IsEqualTo(1);
        }
    }

    [Test]
    public async Task CurrentBaseline_CreatesEffectPointerTable()
    {
        var databaseName = "coop_pointer_migration_" + Guid.NewGuid().ToString("N");
        var connectionString = await CreateDatabaseAsync(databaseName);
        try
        {
            await using var context = CreateMigratorContext(connectionString);
            var migrator = context.GetService<IMigrator>();

            await migrator.MigrateAsync();
            await Assert.That(await EffectPointerTableExistsAsync(context)).IsTrue();
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private static ExploreDbContext CreateMigratorContext(string connectionString)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Migrator,
            Provider = PrimaryDatabaseProvider.PostgreSql,
            Host = connection.Host,
            Port = connection.Port,
            Database = connection.Database,
            Schema = RelationalModelNamespace.Name,
            Username = connection.Username,
            Password = connection.Password,
            TlsMode = PrimaryDatabaseTlsMode.Disabled
        });
        options.ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
        return new ExploreDbContext(options.Options);
    }

    private async Task<SeededClaim> SeedAndClaimAsync(string identity)
    {
        await fixture.ResetAsync();
        var receivedAt = DateTime.UtcNow.AddMinutes(-5);
        var tenant = CreateTenant(identity);
        var message = CreateIncomingMessage(tenant.Id, identity, "decision-content", receivedAt);
        await using (var setupContext = fixture.CreateDbContext())
        {
            setupContext.Tenants.Add(tenant);
            setupContext.IncomingWebhookMessages.Add(message);
            await setupContext.SaveChangesAsync();
        }

        var claimedAt = receivedAt.AddMinutes(1);
        await using var claimContext = fixture.CreateDbContext();
        var claim = (await new IncomingWebhookMessageRepository(claimContext).ClaimDueAsync(
            new IncomingWebhookClaimRequest("coop-pointer-worker", 1, claimedAt, TimeSpan.FromMinutes(5)),
            CancellationToken.None)).Single();
        return new SeededClaim(claim, message.ProviderMessageId, claimedAt.AddSeconds(1));
    }

    private async Task<ConstraintSeed> SeedConstraintRowsAsync()
    {
        await fixture.ResetAsync();
        var createdAt = DateTime.UtcNow.AddMinutes(-5);
        var tenantA = CreateTenant("coop-constraint-a");
        var tenantB = CreateTenant("coop-constraint-b");
        var messageA = CreateIncomingMessage(tenantA.Id, "constraint-decision-a", "content-a", createdAt);
        var messageB = CreateIncomingMessage(tenantA.Id, "constraint-decision-b", "content-b", createdAt.AddSeconds(1));
        var pointer = IncomingWebhookEffectOutbox.CreatePending(
            tenantA.Id,
            messageA.Id,
            "coop",
            messageA.ProviderMessageId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            messageA.PayloadHash,
            createdAt.AddSeconds(1));
        await using var context = fixture.CreateDbContext();
        context.Tenants.AddRange(tenantA, tenantB);
        context.IncomingWebhookMessages.AddRange(messageA, messageB);
        context.IncomingWebhookEffectOutboxes.Add(pointer);
        await context.SaveChangesAsync();
        return new ConstraintSeed(
            tenantA.Id,
            tenantB.Id,
            messageA.Id,
            messageB.Id,
            messageA.ProviderMessageId,
            createdAt);
    }

    private async Task<string> CreateDatabaseAsync(string databaseName)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await command.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = databaseName,
            SearchPath = RelationalModelNamespace.Name
        }.ConnectionString;
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)";
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<bool> EffectPointerTableExistsAsync(ExploreDbContext context)
    {
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        IEntityType effectPointer = context.Model.FindEntityType(typeof(IncomingWebhookEffectOutbox))
            ?? throw new InvalidOperationException("The incoming webhook effect pointer is not mapped.");
        string schema = effectPointer.GetSchema() ?? context.Model.GetDefaultSchema()
            ?? throw new InvalidOperationException("The event model must declare a default schema.");
        string table = effectPointer.GetTableName()
            ?? throw new InvalidOperationException("The incoming webhook effect pointer has no table mapping.");
        command.CommandText = "SELECT to_regclass(@table)::text";
        command.Parameters.AddWithValue("table", $"{schema}.{table}");
        return await command.ExecuteScalarAsync() is string;
    }

    private static IncomingWebhookProcessingService CreateService(ExploreDbContext context, DateTime observedAt) =>
        new(
            new IncomingWebhookMessageRepository(context),
            new IncomingWebhookEffectReceiptRepository(context),
            new EfCoreUnitOfWork(context),
            [new CoopDecisionIncomingWebhookHandler(new IncomingWebhookEffectOutboxRepository(context))],
            Options.Create(new IncomingWebhookProcessingSettings()),
            new FixedTimeProvider(observedAt));

    private static IncomingWebhookProcessingContext CreateClaimedProcessingContext(
        Guid tenantId,
        string providerDecisionId,
        string payloadIdentity,
        DateTime claimedAt)
    {
        var message = CreateIncomingMessage(tenantId, providerDecisionId, payloadIdentity, claimedAt.AddMinutes(-1));
        var leaseToken = Guid.CreateVersion7();
        message.Claim("coop-pointer-replay", leaseToken, claimedAt.AddMinutes(5), claimedAt);
        return IncomingWebhookProcessingContext.FromClaimedMessage(
            message,
            leaseToken,
            message.ProcessingFence,
            message.ProcessingGeneration,
            claimedAt.AddSeconds(1));
    }

    private static IncomingWebhookMessage CreateIncomingMessage(
        Guid tenantId,
        string providerDecisionId,
        string payloadIdentity,
        DateTime receivedAt)
    {
        var payload = Encoding.UTF8.GetBytes("{\"decision\":\"" + payloadIdentity + "\"}");
        return IncomingWebhookMessage.CreateVerified(
            tenantId,
            "coop",
            providerDecisionId,
            providerDecisionId,
            CoopDecisionIncomingWebhookHandler.StableEffectKind,
            payload,
            HashPayload(payload),
            "application/json",
            "utf-8",
            null,
            receivedAt,
            receivedAt,
            receivedAt.AddDays(14),
            "webhook-retention-test-v1",
            receivedAt.AddDays(30),
            receivedAt.AddDays(90),
            receivedAt.AddDays(14),
            receivedAt.AddDays(30));
    }

    private static Tenant CreateTenant(string slugPrefix) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            FullName = "Coop Effect Pointer Tenant",
            Slug = slugPrefix + "-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };

    private static string HashPayload(string payload) => HashPayload(Encoding.UTF8.GetBytes(payload));

    private static string HashPayload(byte[] payload) =>
        "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(payload)).ToLowerInvariant();

    private static Task<IncomingWebhookMessage> LoadMessageAsync(ExploreDbContext context, Guid messageId) =>
        context.IncomingWebhookMessages
            .IgnoreTenantFilter(TenantFilterBypassReasons.WebhookTenantOperation)
            .AsNoTracking()
            .Include(candidate => candidate.ProcessingAttempts)
            .SingleAsync(candidate => candidate.Id == messageId);

    private sealed record SeededClaim(
        IncomingWebhookClaim Claim,
        string ProviderDecisionId,
        DateTime ObservedAt);

    private sealed record ConstraintSeed(
        Guid TenantAId,
        Guid TenantBId,
        Guid MessageAId,
        Guid MessageBId,
        string ProviderDecisionId,
        DateTime CreatedAt);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class SuccessfulCoopDecisionMediator : IMediator
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object response = request is ProcessCoopDecisionCallbackCommand
                ? BaseCommandResponse.Success(Guid.CreateVersion7())
                : throw new NotSupportedException();
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Publish(object notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task Publish<TNotification>(
            TNotification notification,
            CancellationToken cancellationToken = default)
            where TNotification : INotification => Task.CompletedTask;

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class InjectedPointerFailureException : Exception;

    private sealed class FailingSaveMessageRepository(IIncomingWebhookMessageRepository inner)
        : IIncomingWebhookMessageRepository
    {
        public Task<bool> TryCreateAsync(IncomingWebhookMessage message, CancellationToken cancellationToken) =>
            inner.TryCreateAsync(message, cancellationToken);

        public Task<IncomingWebhookMessage?> GetByProviderMessageIdForUpdateAsync(
            Guid tenantId,
            string provider,
            string providerMessageId,
            CancellationToken cancellationToken) =>
            inner.GetByProviderMessageIdForUpdateAsync(
                tenantId,
                provider,
                providerMessageId,
                cancellationToken);

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
            throw new InjectedPointerFailureException();
    }
}
