using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services;
using TUnit.Core;

namespace Event.Application.UnitTests.Services;

public sealed class ScheduledJobRegistryTests
{
    [Test]
    public async Task ListJobsIncludesImplementedEmailDispatchAndEventReminderContracts()
    {
        var registry = new ScheduledJobRegistry();

        var jobs = registry.ListJobs();
        var emailDrain = registry.FindByName(ScheduledJobNames.EmailDispatchDrain);
        var emailRecovery = registry.FindByName(ScheduledJobNames.EmailDispatchRecoveryScan);
        var eventReminder = registry.FindByName(ScheduledJobNames.EventReminderDispatch);

        await Assert.That(jobs.Count).IsGreaterThanOrEqualTo(8);
        await Assert.That(emailDrain).IsNotNull();
        await Assert.That(emailDrain!.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Cron);
        await Assert.That(emailDrain.PayloadKind).IsEqualTo(ScheduledJobPayloadKind.None);
        await Assert.That(emailDrain.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
        await Assert.That(emailDrain.CronExpression).IsEqualTo("*/10 * * * * ?");
        await Assert.That(emailRecovery).IsNotNull();
        await Assert.That(emailRecovery!.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Cron);
        await Assert.That(emailRecovery.PayloadKind).IsEqualTo(ScheduledJobPayloadKind.None);
        await Assert.That(emailRecovery.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
        await Assert.That(emailRecovery.CronExpression).IsEqualTo("0 */1 * * * ?");
        await Assert.That(eventReminder).IsNotNull();
        await Assert.That(eventReminder!.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Time);
        await Assert.That(eventReminder.PayloadKind).IsEqualTo(ScheduledJobPayloadKind.PointerOnly);
        await Assert.That(eventReminder.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
    }

    /// <summary>
    /// The catalog is what operators and the scheduler status surface read, so a job that runs but is not
    /// cataloged is invisible work. It also fixes each job's contract: hold expiry is a per-order deadline
    /// carrying a pointer, while its reconciliation sweep is payload-free and cron-driven.
    /// </summary>
    [Test]
    public async Task InventoryHoldAndFinalizationDrainJobsAreCatalogedAsImplemented()
    {
        var registry = new ScheduledJobRegistry();

        var holdExpiry = registry.FindByName(ScheduledJobNames.InventoryHoldExpiry);
        var reconciliation = registry.FindByName(ScheduledJobNames.InventoryHoldExpiryReconciliation);
        var finalizationDrain = registry.FindByName(ScheduledJobNames.RegistrationFinalizationDrain);

        await Assert.That(holdExpiry).IsNotNull();
        await Assert.That(holdExpiry!.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
        await Assert.That(holdExpiry.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Time);
        await Assert.That(holdExpiry.PayloadKind).IsEqualTo(ScheduledJobPayloadKind.PointerOnly);

        await Assert.That(reconciliation).IsNotNull();
        await Assert.That(reconciliation!.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
        await Assert.That(reconciliation.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Cron);
        await Assert.That(reconciliation.PayloadKind).IsEqualTo(ScheduledJobPayloadKind.None);

        await Assert.That(finalizationDrain).IsNotNull();
        await Assert.That(finalizationDrain!.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
        await Assert.That(finalizationDrain.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Cron);
    }

    /// <summary>
    /// Quartz rejects a cron expression that does not put <c>?</c> in exactly one of day-of-month or
    /// day-of-week, and a Unix-style five-field expression is silently wrong here. The catalog is the text
    /// operators read, so it must not drift from what the scheduler would actually accept.
    /// </summary>
    [Test]
    public async Task EveryCatalogedCronExpressionUsesQuartzDayFieldSyntax()
    {
        var registry = new ScheduledJobRegistry();

        foreach (ScheduledJobDescriptor job in registry.ListJobs()
                     .Where(job => !string.IsNullOrWhiteSpace(job.CronExpression)))
        {
            string[] fields = job.CronExpression!.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            await Assert.That(fields.Length).IsGreaterThanOrEqualTo(6)
                .Because($"{job.Name} must use Quartz's 6-or-7-field cron syntax, not a Unix 5-field expression.");
            await Assert.That(fields.Count(field => field == "?")).IsEqualTo(1)
                .Because($"{job.Name} must place '?' in exactly one of day-of-month or day-of-week.");
        }
    }

    [Test]
    public async Task GeneralOutboxIsNotPromisedAsAQuartzJob()
    {
        var registry = new ScheduledJobRegistry();

        await Assert.That(ScheduledJobNames.All).DoesNotContain("general-outbox-drain");
        await Assert.That(registry.ListJobs().Select(job => job.Name)).DoesNotContain("general-outbox-drain");
    }

    [Test]
    public async Task QueueDrivenMigrationsAreCatalogedAsPayloadFreeIntervals()
    {
        var registry = new ScheduledJobRegistry();
        string[] names =
        [
            ScheduledJobNames.IntegrationSyncDrain,
            ScheduledJobNames.LocalWebhookDeliveryDrain,
            ScheduledJobNames.IncomingWebhookIntakeDrain,
            ScheduledJobNames.IncomingWebhookEffectDrain,
            ScheduledJobNames.WebhookBulkReplayDrain,
            ScheduledJobNames.WebhookProviderPublicationDrain,
            ScheduledJobNames.PdsSyncDrain,
        ];

        foreach (string name in names)
        {
            ScheduledJobDescriptor? job = registry.FindByName(name);
            await Assert.That(job).IsNotNull();
            await Assert.That(job!.Status).IsEqualTo(ScheduledJobOperationalStatus.Implemented);
            await Assert.That(job.ScheduleKind).IsEqualTo(ScheduledJobScheduleKind.Interval);
            await Assert.That(job.PayloadKind).IsEqualTo(ScheduledJobPayloadKind.None);
        }
    }
}
