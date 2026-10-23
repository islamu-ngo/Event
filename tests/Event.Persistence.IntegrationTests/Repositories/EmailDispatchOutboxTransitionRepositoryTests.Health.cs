using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class EmailDispatchOutboxTransitionRepositoryTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeEmailHealthCountsCrossTenantsWithoutPrivateRows(bool globallyPaused)
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        DateTime now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        DateTime before = now.AddMinutes(-10);
        var first = await SeedTenantAsync(seed, "email-health-first");
        var second = await SeedTenantAsync(seed, "email-health-second");
        var paused = await SeedTenantAsync(seed, "email-health-paused");
        EmailDispatchStatus[] statuses =
        [
            EmailDispatchStatus.Pending, EmailDispatchStatus.RetryScheduled,
            EmailDispatchStatus.Processing, EmailDispatchStatus.DeadLettered,
            EmailDispatchStatus.Unknown, EmailDispatchStatus.Parked
        ];
        foreach (var tenant in new[] { first, second, paused })
        {
            foreach (var status in statuses)
            {
                var row = await SeedDispatchAsync(seed, tenant.Id, status);
                row.CreatedAt = now.AddHours(-2);
                row.NextAttemptAt = status == EmailDispatchStatus.RetryScheduled ? now : null;
                row.ProcessingStartedAt = status == EmailDispatchStatus.Processing ? before : null;
            }
            var futureRetry = await SeedDispatchAsync(seed, tenant.Id, EmailDispatchStatus.RetryScheduled);
            futureRetry.NextAttemptAt = now.AddHours(1);
            var freshProcessing = await SeedDispatchAsync(seed, tenant.Id, EmailDispatchStatus.Processing);
            freshProcessing.ProcessingStartedAt = before.AddSeconds(1);
            var secondFreshProcessing = await SeedDispatchAsync(seed, tenant.Id, EmailDispatchStatus.Processing);
            secondFreshProcessing.ProcessingStartedAt = before.AddSeconds(2);
        }
        foreach (var status in statuses)
        {
            var redacted = await SeedDispatchAsync(seed, first.Id,
                status == EmailDispatchStatus.Processing ? EmailDispatchStatus.Parked : status);
            redacted.ContentRedactedAt = now;
            redacted.RecipientEmail = string.Empty;
            redacted.Subject = string.Empty;
            redacted.PlainTextBody = null;
            redacted.HtmlBody = null;
            redacted.ReplyTo = null;
            redacted.LastError = null;
            redacted.ProviderMessageId = null;
            redacted.CorrelationId = null;
            redacted.CreatedAt = now.AddDays(-1);
            redacted.NextAttemptAt = null;
            redacted.ProcessingStartedAt = null;
            redacted.ProcessingLeaseToken = null;
            var deleted = await SeedDispatchAsync(seed, first.Id, status);
            deleted.IsDeleted = true;
            deleted.CreatedAt = now.AddDays(-1);
            deleted.NextAttemptAt = null;
            deleted.ProcessingStartedAt = status == EmailDispatchStatus.Processing ? before : null;
        }
        seed.EmailDispatchTenantControls.Add(new EmailDispatchTenantControl
        {
            Id = Guid.CreateVersion7(),
            TenantId = paused.Id,
            IsPaused = true,
            CreatedAt = now
        });
        seed.EmailDispatchProcessorStates.Add(new EmailDispatchProcessorState
        {
            Id = Guid.CreateVersion7(),
            ProcessorCode = "smtp",
            IsPaused = globallyPaused,
            OptionalRemindersDeferred = true,
            GlobalSmtpRateLimitPerMinuteOverride = 60,
            UpdatedAt = now
        });
        await seed.SaveChangesAsync();

        // The production Global pool has only purpose-role execution authority.
        await using var authority = await CreateEmailAuthorityAsync(seed);
        var reader = new EmailDispatchHealthSnapshotReader(new EmailGlobalFactory(authority.GlobalFactory));
        var snapshot = await reader.ReadAsync(now, before, 1, CancellationToken.None);
        await using var global = await authority.GlobalFactory.CreateDbContextAsync();
        await Assert.That(snapshot.DueDispatchCount).IsEqualTo(4);
        await Assert.That(snapshot.RetryScheduledCount).IsEqualTo(4);
        await Assert.That(snapshot.StaleProcessingCount).IsEqualTo(2);
        await Assert.That(snapshot.DeadLetteredCount).IsEqualTo(2);
        await Assert.That(snapshot.UnknownCount).IsEqualTo(2);
        await Assert.That(snapshot.ParkedCount).IsEqualTo(2);
        await Assert.That(snapshot.OldestDueCreatedAt).IsEqualTo(now.AddHours(-2));
        var backlog = snapshot.TenantBacklogSample;
        await Assert.That(backlog.Count).IsEqualTo(1);
        await Assert.That(backlog[first.Id]).IsEqualTo(2);
        await Assert.That(snapshot.OptionalReminderDeferralActive).IsTrue();
        await Assert.That(snapshot.GlobalPaused).IsEqualTo(globallyPaused);
        await Assert.That(snapshot.GlobalSmtpRateLimitOverrideActive).IsTrue();

        await RequireEmailDenialAsync(global,
            $"SELECT recipient_email FROM \"{seed.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchema()}\"." +
            $"\"{seed.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetTableName()}\"");
        await using var runtime = await authority.RuntimeFactory.CreateDbContextAsync(first.Id);
        await Assert.That(await runtime.EmailDispatchOutbox.AsNoTracking().CountAsync(row => row.TenantId == second.Id))
            .IsEqualTo(0);
        await Assert.That(await runtime.EmailDispatchOutbox.AsNoTracking().CountAsync(row => row.TenantId == first.Id))
            .IsGreaterThan(0);
    }

    [Test]
    public async Task NativeEmailHealthRejectsRuntimeExecutionAndInvalidBounds()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        var tenant = await SeedTenantAsync(seed, "email-health-denial");
        await SeedDispatchAsync(seed, tenant.Id, EmailDispatchStatus.Pending);
        await using var authority = await CreateEmailAuthorityAsync(seed);
        await using var runtime = await authority.RuntimeFactory.CreateDbContextAsync(tenant.Id);
        await RequireEmailDenialAsync(runtime,
            $"SELECT * FROM {PostgresEmailDispatchAdmissionContract.HealthFunctionSql(runtime)}(now(), now(), 1)");

        await using var global = await authority.GlobalFactory.CreateDbContextAsync();
        var state = seed.Model.FindEntityType(typeof(EmailDispatchProcessorState))!;
        await RequireEmailDenialAsync(global,
            $"SELECT pause_reason, paused_by FROM \"{state.GetSchema()}\".\"{state.GetTableName()}\"");
        foreach (string arguments in new[]
                 {
                     "now(), now(), 0", "now(), now(), 101", "now(), now(), NULL::integer",
                     "NULL::timestamptz, now(), 1", "now(), NULL::timestamptz, 1",
                     "'infinity'::timestamptz, now(), 1", "now(), '-infinity'::timestamptz, 1",
                     "now(), now() + interval '1 second', 1"
                 })
        {
            string? rejection = null;
            try
            {
                await global.Database.ExecuteSqlRawAsync(
                    $"SELECT * FROM {PostgresEmailDispatchAdmissionContract.HealthFunctionSql(global)}({arguments})");
            }
            catch (PostgresException exception) { rejection = exception.SqlState; }
            await Assert.That(rejection).IsEqualTo("22023");
        }
    }

    [Test]
    public async Task NativeEmailHealthEmptyQueueHasZeroCountsAndNoProcessorFlags()
    {
        await fixture.ResetAsync();
        await using var seed = fixture.CreateDbContext();
        await using var authority = await CreateEmailAuthorityAsync(seed);
        var reader = new EmailDispatchHealthSnapshotReader(new EmailGlobalFactory(authority.GlobalFactory));
        var snapshot = await reader.ReadAsync(DateTime.UtcNow, DateTime.UnixEpoch, 100, CancellationToken.None);

        await Assert.That(snapshot.DueDispatchCount).IsEqualTo(0);
        await Assert.That(snapshot.RetryScheduledCount).IsEqualTo(0);
        await Assert.That(snapshot.StaleProcessingCount).IsEqualTo(0);
        await Assert.That(snapshot.DeadLetteredCount).IsEqualTo(0);
        await Assert.That(snapshot.UnknownCount).IsEqualTo(0);
        await Assert.That(snapshot.ParkedCount).IsEqualTo(0);
        await Assert.That(snapshot.OldestDueCreatedAt).IsNull();
        await Assert.That(snapshot.TenantBacklogSample).IsEmpty();
        await Assert.That(snapshot.OptionalReminderDeferralActive).IsFalse();
        await Assert.That(snapshot.GlobalPaused).IsFalse();
        await Assert.That(snapshot.GlobalSmtpRateLimitOverrideActive).IsFalse();
    }
}
