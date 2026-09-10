
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Models;
using Explore.Application.Notifications;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryParkReasonTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StaticCapabilityUnavailable_ParksRequiredButSkipsOptionalWithoutHandoff(bool required)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var dispatch = await SeedAsync(databasePath);
            await using var context = CreateContext(databasePath);
            if (required)
                await UseRequiredPolicyAsync(context, dispatch);
            await SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpHost, "\"\"");

            var result = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(Request(dispatch));

            await Assert.That(result.Outcome).IsEqualTo(required ? EmailDispatchEligibilityOutcome.Parked : EmailDispatchEligibilityOutcome.Skipped);
            await Assert.That(result.ReceiptId).IsNull();
            await Assert.That(result.AttemptNumber).IsNull();
            await using var observer = CreateContext(databasePath);
            var persisted = await ReadAsync(observer, dispatch);
            await Assert.That(persisted.Status).IsEqualTo(required ? EmailDispatchStatus.Parked : EmailDispatchStatus.Skipped);
            await Assert.That(persisted.ParkReason).IsEqualTo(required ? EmailDispatchParkReason.CapabilityUnavailable : (EmailDispatchParkReason?)null);
            await Assert.That(persisted.ParkedAt.HasValue).IsEqualTo(required);
            await Assert.That(persisted.ProcessingLeaseToken).IsNull();
            await Assert.That(persisted.AttemptCount).IsEqualTo(0);
            await Assert.That(persisted.NextAttemptAt).IsNull();
            var delivery = await observer.NotificationDeliveries
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            await Assert.That(delivery.StatusId).IsEqualTo((int)(required ? NotificationDeliveryStatusEnum.Parked : NotificationDeliveryStatusEnum.Skipped));
            await AssertEvidenceCountAsync(observer, dispatch, expected: 0);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task AdmittedConfigurationFailure_PersistsCapabilityParkReasonAndExistingAttempt()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var dispatch = await SeedAsync(databasePath);
            await using var context = CreateContext(databasePath);
            await UseRequiredPolicyAsync(context, dispatch);
            var handoff = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(Request(dispatch));
            await Assert.That(handoff.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
            await Assert.That(handoff.ReceiptId).IsNotNull();
            var outcome = await new EmailDispatchOutboxRepository(context).SettleProviderFailure(new EmailDispatchFailureSettlement(
                TenantId: dispatch.TenantId, OutboxId: dispatch.OutboxId, ProcessingLeaseToken: dispatch.LeaseToken,
                AttemptNumber: handoff.AttemptNumber!.Value, Outcome: SmtpDeliveryOutcome.ConfigurationFailure,
                RetryDelay: TimeSpan.FromSeconds(30), MaxAttempts: 5, SettledAt: DateTime.UtcNow), CancellationToken.None);

            await Assert.That(outcome).IsEqualTo(EmailDispatchFailureSettlementOutcome.Parked);
            await using var observer = CreateContext(databasePath);
            var persisted = await ReadAsync(observer, dispatch);
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Parked);
            await Assert.That(persisted.ParkReason).IsEqualTo(EmailDispatchParkReason.CapabilityUnavailable);
            await Assert.That(persisted.ParkedAt).IsNotNull();
            await Assert.That(persisted.ProcessingLeaseToken).IsNull();
            await Assert.That(persisted.NextAttemptAt).IsNull();
            await Assert.That(persisted.AttemptCount).IsEqualTo(1);
            await AssertEvidenceCountAsync(observer, dispatch, expected: 1);
            var receipt = await observer.EmailDispatchReceipts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            await Assert.That(receipt.Id).IsEqualTo(handoff.ReceiptId!.Value);
            await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Failed);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task OperatorPark_RecordsOperatorProvenanceWithoutHandoff()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var dispatch = await SeedAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var pending = await TrackedAsync(context, dispatch);
            pending.Status = EmailDispatchStatus.Pending;
            pending.ProcessingStartedAt = null;
            pending.ProcessingLeaseToken = null;
            await context.SaveChangesAsync();
            var repository = new EmailDispatchOutboxRepository(context);
            bool changed = await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(token => repository.TryParkForOperator(
                tenantId: dispatch.TenantId, outboxId: dispatch.OutboxId, reason: "Operator review requested",
                changedBy: null, parkedAt: DateTime.UtcNow, cancellationToken: token));

            await Assert.That(changed).IsTrue();
            await using var observer = CreateContext(databasePath);
            var persisted = await ReadAsync(observer, dispatch);
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Parked);
            await Assert.That(persisted.ParkReason).IsEqualTo(EmailDispatchParkReason.Operator);
            await Assert.That(persisted.ParkedAt).IsNotNull();
            await AssertEvidenceCountAsync(observer, dispatch, expected: 0);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(EmailDispatchParkReason.CapabilityUnavailable)]
    [Arguments(EmailDispatchParkReason.Operator)]
    public async Task ExplicitReplay_ClearsPersistedParkReason(EmailDispatchParkReason reason)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var dispatch = await SeedAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var parked = await TrackedAsync(context, dispatch);
            parked.Status = EmailDispatchStatus.Parked;
            parked.ParkReason = reason;
            parked.ParkedAt = DateTime.UtcNow;
            parked.ProcessingStartedAt = null;
            parked.ProcessingLeaseToken = null;
            await context.SaveChangesAsync();
            var repository = new EmailDispatchOutboxRepository(context);

            bool replayed = await new EfCoreUnitOfWork(context).ExecuteInTransactionAsync(token => repository.TryReplayForOperator(
                tenantId: dispatch.TenantId, outboxId: dispatch.OutboxId, changedBy: null,
                replayAt: DateTime.UtcNow, cancellationToken: token));

            await Assert.That(replayed).IsTrue();
            await using var observer = CreateContext(databasePath);
            var persisted = await ReadAsync(observer, dispatch);
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Pending);
            await Assert.That(persisted.ParkReason).IsNull();
            await Assert.That(persisted.ParkedAt).IsNull();
            await Assert.That(persisted.ProcessingLeaseToken).IsNull();
            await AssertEvidenceCountAsync(observer, dispatch, expected: 0);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task DisabledOptionalDelivery_IsSkippedWithoutParkReason()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var dispatch = await SeedAsync(databasePath);
            await using var context = CreateContext(databasePath);
            await ConfirmEmailDisableAsync(context);

            var result = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(Request(dispatch));

            await Assert.That(result.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Skipped);
            await using var observer = CreateContext(databasePath);
            var persisted = await ReadAsync(observer, dispatch);
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Skipped);
            await Assert.That(persisted.ParkReason).IsNull();
            await Assert.That(persisted.ParkedAt).IsNull();
            await AssertEvidenceCountAsync(observer, dispatch, expected: 0);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task UndefinedParkReason_IsRejectedByDatabase()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var dispatch = await SeedAsync(databasePath);
            await using var context = CreateContext(databasePath);
            var parked = await TrackedAsync(context, dispatch);
            parked.Status = EmailDispatchStatus.Parked;
            parked.ParkReason = (EmailDispatchParkReason)int.MaxValue;
            parked.ParkedAt = DateTime.UtcNow;
            parked.ProcessingStartedAt = null;
            parked.ProcessingLeaseToken = null;

            await Assert.That(async () => await context.SaveChangesAsync()).Throws<DbUpdateException>();

            await using var observer = CreateContext(databasePath);
            var persisted = await ReadAsync(observer, dispatch);
            await Assert.That(persisted.Status).IsEqualTo(EmailDispatchStatus.Processing);
            await Assert.That(persisted.ParkReason).IsNull();
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static async Task<SeededDispatch> SeedAsync(string databasePath)
    {
        await CreateDatabaseAsync(databasePath);
        await using var context = CreateContext(databasePath);
        return await SeedProcessingDispatchAsync(context, $"park-reason-{Guid.CreateVersion7():N}");
    }

    private static async Task UseRequiredPolicyAsync(ExploreDbContext context, SeededDispatch dispatch)
    {
        var delivery = await context.NotificationDeliveries
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .Include(row => row.NotificationIntent)
            .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
        delivery.IsRequired = true;
        delivery.DeliveryPolicyId = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired;
        delivery.PreferenceCategoryCode = NotificationPreferenceCategoryCodes.TrustSafety;
        delivery.TemplateKey = NotificationFanoutOccurrenceCoordinationPolicy.HeavyModerationUnavailableTemplateKey;
        delivery.NotificationIntent!.CategoryId = (int)NotificationCategoryEnum.TrustSafetyModeration;
        delivery.NotificationIntent.TemplateKey = delivery.TemplateKey;
        (await TrackedAsync(context, dispatch)).Kind = EmailDispatchKind.ModerationAvailabilityRequired;
        await context.SaveChangesAsync();
    }

    private static EmailDispatchEligibilityRequest Request(SeededDispatch dispatch) =>
        new(TenantId: dispatch.TenantId, OutboxId: dispatch.OutboxId, ProcessingLeaseToken: dispatch.LeaseToken,
            AttemptNumber: 0, GlobalSmtpRateLimitPerMinute: 60, TenantSmtpRateLimitPerMinute: 60,
            ConsumerId: "park-reason-test", EvaluatedAt: DateTime.UtcNow);

    private static Task<EmailDispatchOutbox> TrackedAsync(ExploreDbContext context, SeededDispatch dispatch) =>
        context.EmailDispatchOutbox.IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .SingleAsync(row => row.TenantId == dispatch.TenantId && row.Id == dispatch.OutboxId);

    private static async Task<EmailDispatchOutbox> ReadAsync(ExploreDbContext context, SeededDispatch dispatch) =>
        (await new EmailDispatchOutboxRepository(context).GetByTenantAndId(dispatch.TenantId, dispatch.OutboxId, CancellationToken.None))!;

    private static async Task AssertEvidenceCountAsync(ExploreDbContext context, SeededDispatch dispatch, int expected)
    {
        await Assert.That(await context.EmailDispatchAttempts
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .CountAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId)).IsEqualTo(expected);
        await Assert.That(await context.EmailDispatchReceipts
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .CountAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId)).IsEqualTo(expected);
    }

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-park-reason-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }
}
