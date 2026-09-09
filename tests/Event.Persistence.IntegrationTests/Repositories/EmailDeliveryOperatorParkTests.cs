
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Handlers.Commands;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliveryOperatorParkTests
{
    [Test]
    public async Task CapabilityPark_ExplicitOperatorCommandChangesRecoveryOwnership()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, EmailDispatchStatus.Parked, EmailDispatchParkReason.CapabilityUnavailable);
            await using var context = CreateContext(databasePath);

            var result = await Handler(context).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.IsSuccess).IsTrue();
            await using var observer = CreateContext(databasePath);
            var parked = await ReadAsync(observer, scenario);
            await Assert.That(parked.Status).IsEqualTo(EmailDispatchStatus.Parked);
            await Assert.That(parked.ParkReason).IsEqualTo(EmailDispatchParkReason.Operator);
            await Assert.That(parked.LastError).IsEqualTo(Request(scenario).Reason);
            await Assert.That(parked.UpdatedBy).IsEqualTo(scenario.UserId);
            await Assert.That(parked.ParkedAt).IsNotNull();
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task RepeatedOperatorPark_IsSuccessfulNoOpPreservingHoldTimeAndReason()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, EmailDispatchStatus.Pending);
            await using var context = CreateContext(databasePath);
            await Assert.That((await Handler(context).Handle(Request(scenario), CancellationToken.None)).IsSuccess).IsTrue();
            var before = await ReadAsync(context, scenario);
            await Assert.That(before.ParkReason).IsEqualTo(EmailDispatchParkReason.Operator);

            var repeated = await Handler(context).Handle(Request(scenario) with { Reason = "A later duplicate operator request" }, CancellationToken.None);

            await Assert.That(repeated.IsSuccess).IsTrue();
            await using var observer = CreateContext(databasePath);
            await Assert.That(Snapshot(await ReadAsync(observer, scenario))).IsEqualTo(Snapshot(before));
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(EmailDispatchStatus.Pending)]
    [Arguments(EmailDispatchStatus.RetryScheduled)]
    [Arguments(EmailDispatchStatus.DeadLettered)]
    public async Task ParkableWork_TransitionsToExplicitOperatorHold(EmailDispatchStatus status)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, status);
            await using var context = CreateContext(databasePath);

            var result = await Handler(context).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.IsSuccess).IsTrue();
            await using var observer = CreateContext(databasePath);
            var parked = await ReadAsync(observer, scenario);
            await Assert.That(parked.Status).IsEqualTo(EmailDispatchStatus.Parked);
            await Assert.That(parked.ParkReason).IsEqualTo(EmailDispatchParkReason.Operator);
            await Assert.That(parked.ParkedAt).IsNotNull();
            await Assert.That(parked.NextAttemptAt).IsNull();
            await Assert.That(parked.ProcessingLeaseToken).IsNull();
            await Assert.That(parked.UpdatedBy).IsEqualTo(scenario.UserId);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WrongTenantOrRedactedContent_CannotBeMutated(bool redacted)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, EmailDispatchStatus.Pending, redacted: redacted);
            await using var context = CreateContext(databasePath);
            var before = await ReadAsync(context, scenario);
            var request = redacted ? Request(scenario) : Request(scenario) with { TenantId = Guid.CreateVersion7() };

            var result = await Handler(context).Handle(request, CancellationToken.None);

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.FailureCode).IsEqualTo(redacted ? EmailDispatchFailureCodes.InvalidTransition : EmailDispatchFailureCodes.NotFound);
            await using var observer = CreateContext(databasePath);
            var after = await ReadAsync(observer, scenario);
            await Assert.That(Snapshot(after)).IsEqualTo(Snapshot(before));
            await Assert.That(after.ContentRedactedAt).IsEqualTo(before.ContentRedactedAt);
            await Assert.That(after.RecipientEmail).IsEqualTo(before.RecipientEmail);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    [Arguments(EmailDispatchStatus.Unknown)]
    [Arguments((EmailDispatchStatus)int.MaxValue)]
    public async Task UnknownOrUndefinedStatus_FailsClosedWithoutMutation(EmailDispatchStatus status)
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, status);
            await using var context = CreateContext(databasePath);
            var before = await ReadAsync(context, scenario);

            var result = await Handler(context).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.IsSuccess).IsFalse();
            await using var observer = CreateContext(databasePath);
            var after = await ReadAsync(observer, scenario);
            await Assert.That(Snapshot(after)).IsEqualTo(Snapshot(before));
            await Assert.That(after.UnknownAt).IsEqualTo(before.UnknownAt);
        }
        finally { DeleteDatabase(databasePath); }
    }

    [Test]
    public async Task AdmittedProcessing_PreservesLiveLeaseAndProviderHandoffEvidence()
    {
        var databasePath = NewDatabasePath();
        try
        {
            var scenario = await SeedAsync(databasePath, EmailDispatchStatus.Processing);
            var dispatch = scenario.Dispatch;
            await using var context = CreateContext(databasePath);
            var admission = await CreateEvaluator(context).EvaluateAndBeginProviderHandoffAsync(new EmailDispatchEligibilityRequest(
                TenantId: dispatch.TenantId, OutboxId: dispatch.OutboxId, ProcessingLeaseToken: dispatch.LeaseToken,
                AttemptNumber: 0, GlobalSmtpRateLimitPerMinute: 60, TenantSmtpRateLimitPerMinute: 60,
                ConsumerId: "operator-park-handoff-test", EvaluatedAt: DateTime.UtcNow));
            await Assert.That(admission.Outcome).IsEqualTo(EmailDispatchEligibilityOutcome.Eligible);
            var before = await ReadAsync(context, scenario);

            var result = await Handler(context).Handle(Request(scenario), CancellationToken.None);

            await Assert.That(result.IsSuccess).IsFalse();
            await using var observer = CreateContext(databasePath);
            var after = await ReadAsync(observer, scenario);
            await Assert.That(Snapshot(after)).IsEqualTo(Snapshot(before));
            await Assert.That(after.Status).IsEqualTo(EmailDispatchStatus.Processing);
            await Assert.That(after.ProcessingLeaseToken).IsEqualTo(dispatch.LeaseToken);
            await Assert.That(after.AttemptCount).IsEqualTo(1);
            var receipt = await observer.EmailDispatchReceipts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            var attempt = await observer.EmailDispatchAttempts
                .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate).AsNoTracking()
                .SingleAsync(row => row.TenantId == dispatch.TenantId && row.EmailDispatchOutboxId == dispatch.OutboxId);
            await Assert.That(receipt.Id).IsEqualTo(admission.ReceiptId!.Value);
            await Assert.That(receipt.Status).IsEqualTo(EmailDispatchReceiptStatus.Processing);
            await Assert.That(attempt.Outcome).IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
            await Assert.That(attempt.FailureCategory).IsEqualTo("provider_handoff_started");
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static async Task<Scenario> SeedAsync(string databasePath, EmailDispatchStatus status,
        EmailDispatchParkReason? parkReason = null, bool redacted = false)
    {
        await CreateDatabaseAsync(databasePath);
        await using var context = CreateContext(databasePath);
        var dispatch = await SeedProcessingDispatchAsync(context, $"operator-park-{Guid.CreateVersion7():N}");
        var row = await context.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.TenantScopedRepositoryExactTenantPredicate)
            .SingleAsync(value => value.TenantId == dispatch.TenantId && value.Id == dispatch.OutboxId);
        row.Status = status;
        if (status != EmailDispatchStatus.Processing)
        {
            row.ProcessingStartedAt = null;
            row.ProcessingLeaseToken = null;
        }
        DateTime now = DateTime.UtcNow;
        row.ParkReason = parkReason;
        row.ParkedAt = status == EmailDispatchStatus.Parked ? now : null;
        row.NextAttemptAt = status == EmailDispatchStatus.RetryScheduled ? now.AddMinutes(5) : null;
        row.DeadLetteredAt = status == EmailDispatchStatus.DeadLettered ? now : null;
        row.UnknownAt = status == EmailDispatchStatus.Unknown ? now : null;
        if (redacted)
        {
            row.ContentRedactedAt = now;
            row.RecipientEmail = string.Empty;
            row.Subject = string.Empty;
            row.PlainTextBody = null;
            row.HtmlBody = null;
            row.ReplyTo = null;
            row.LastError = null;
            row.ProviderMessageId = null;
            row.CorrelationId = null;
        }
        await context.SaveChangesAsync();
        return new(Dispatch: dispatch, UserId: row.RecipientUserId);
    }

    private static ParkEmailDispatchCommandHandler Handler(ExploreDbContext context) =>
        new(repository: new EmailDispatchOutboxRepository(context));

    private static ParkEmailDispatchCommand Request(Scenario scenario) => new()
    {
        TenantId = scenario.Dispatch.TenantId,
        OutboxId = scenario.Dispatch.OutboxId,
        ChangedBy = scenario.UserId,
        Reason = "Review before delivery"
    };

    private static async Task<EmailDispatchOutbox> ReadAsync(ExploreDbContext context, Scenario scenario) =>
        (await new EmailDispatchOutboxRepository(context).GetByTenantAndId(
            scenario.Dispatch.TenantId, scenario.Dispatch.OutboxId, CancellationToken.None))!;

    private static (EmailDispatchStatus Status, EmailDispatchParkReason? Reason, DateTime? ParkedAt,
        DateTime? UpdatedAt, Guid? UpdatedBy, string? Error, Guid? Lease) Snapshot(EmailDispatchOutbox row) =>
        (row.Status, row.ParkReason, row.ParkedAt, row.UpdatedAt, row.UpdatedBy, row.LastError, row.ProcessingLeaseToken);

    private static string NewDatabasePath() => Path.Combine(Path.GetTempPath(), $"email-operator-park-{Guid.CreateVersion7():N}.db");

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-wal");
        File.Delete(databasePath + "-shm");
    }

    private sealed record Scenario(SeededDispatch Dispatch, Guid UserId);
}
