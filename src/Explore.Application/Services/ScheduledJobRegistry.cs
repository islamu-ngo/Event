using Explore.Application.Contracts.Scheduling;

namespace Explore.Application.Services;

public sealed class ScheduledJobRegistry : IScheduledJobRegistry
{
    private static readonly ScheduledJobDescriptor[] Jobs =
    [
        new(
            ScheduledJobNames.EmailDispatchDrain,
            "EmailDispatch",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Claims due EmailDispatchOutbox rows and executes approved dispatch transports.",
            "*/10 * * * * ?"),
        new(
            ScheduledJobNames.EventReminderDispatch,
            "EventLifecycle",
            ScheduledJobScheduleKind.Time,
            ScheduledJobPayloadKind.PointerOnly,
            ScheduledJobOperationalStatus.Implemented,
            "Wakes a pre-persisted event reminder EmailDispatchOutbox row at its scheduled time."),
        new(
            ScheduledJobNames.PdsSyncDrain,
            "PDS",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Drains durable AT Protocol PDS work with fenced leases and bounded concurrency."),
        new(
            ScheduledJobNames.EmailDispatchRecoveryScan,
            "EmailDispatch",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Marks stale EmailDispatchOutbox processing leases as Unknown for operator review.",
            "0 */1 * * * ?"),
        new(
            ScheduledJobNames.DeadLetterSummary,
            "Operations",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Planned,
            "Future operator summary for dead-lettered platform work."),
        new(
            ScheduledJobNames.WaitlistPromotionScan,
            "EventLifecycle",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Planned,
            "Future waitlist promotion scanner that creates durable domain intents before side effects."),
        new(
            ScheduledJobNames.InventoryHoldExpiry,
            "Registration",
            ScheduledJobScheduleKind.Time,
            ScheduledJobPayloadKind.PointerOnly,
            ScheduledJobOperationalStatus.Implemented,
            "Expires one registration order's due capacity holds at the order's earliest hold expiry."),
        new(
            ScheduledJobNames.InventoryHoldExpiryReconciliation,
            "Registration",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Safety-net sweep for expired holds and lifecycle recovery targets no deadline covered.",
            "0 */5 * * * ?"),
        new(
            ScheduledJobNames.RegistrationFinalizationDrain,
            "Registration",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Drains durable registration-finalization effects under the shared fenced claim.",
            "*/10 * * * * ?"),
        new(
            ScheduledJobNames.PaymentReconciliationDrain,
            "Registration",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Drains durable Checkout dispatch and retrieves authoritative provider payment state.",
            "*/30 * * * * ?"),
        new(
            ScheduledJobNames.FairReturnOrchestration,
            "FairReturn",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.PointerOnly,
            ScheduledJobOperationalStatus.Implemented,
            "Wakes the durable fair-return payment " +
            "settlement and refund-intent drain.",
            "*/15 * * * * ?"),
        new(
            ScheduledJobNames.RegistrationProviderSubmissionWriteDrain,
            "RegistrationProvider",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Drains fenced outbound registration-provider submission effects.",
            "*/10 * * * * ?"),
        new(
            ScheduledJobNames.RegistrationProviderSubscriptionLifecycleDrain,
            "RegistrationProvider",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Renews provider subscriptions and reconciles response checkpoints.",
            "*/30 * * * * ?"),
        new(
            ScheduledJobNames.IntegrationSyncDrain,
            "IntegrationSync",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Drains tenant-bound integration sync rows with fenced provider handoff settlement."),
        new(
            ScheduledJobNames.LocalWebhookDeliveryDrain,
            "Webhooks",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Recovers stale Local-provider leases and drains durable HTTP delivery attempts."),
        new(
            ScheduledJobNames.IncomingWebhookIntakeDrain,
            "Webhooks",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Claims and processes durable incoming webhook messages in their tenant context."),
        new(
            ScheduledJobNames.IncomingWebhookEffectDrain,
            "Webhooks",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Executes durable incoming-webhook effect pointers with fenced settlement."),
        new(
            ScheduledJobNames.WebhookBulkReplayDrain,
            "Webhooks",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Processes bounded queued bulk-replay operations."),
        new(
            ScheduledJobNames.WebhookProviderPublicationDrain,
            "Webhooks",
            ScheduledJobScheduleKind.Interval,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Implemented,
            "Dispatches and reconciles durable provider-publication work."),
        new(
            ScheduledJobNames.TenantMaintenanceScan,
            "Operations",
            ScheduledJobScheduleKind.Cron,
            ScheduledJobPayloadKind.None,
            ScheduledJobOperationalStatus.Planned,
            "Future tenant maintenance scanner for scheduled platform controls.")
    ];

    public IReadOnlyCollection<ScheduledJobDescriptor> ListJobs()
    {
        return Jobs;
    }

    public ScheduledJobDescriptor? FindByName(string name)
    {
        return Jobs.FirstOrDefault(job => string.Equals(job.Name, name, StringComparison.Ordinal));
    }
}
