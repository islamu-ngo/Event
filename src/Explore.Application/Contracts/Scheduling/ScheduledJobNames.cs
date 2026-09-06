// ABOUTME: Stable operational job names for platform-owned scheduled work.
// ABOUTME: Keeps scheduler identifiers centralized so scheduler job names do not drift from Application contracts.

using System.Collections.Frozen;

namespace Explore.Application.Contracts.Scheduling;

public static class ScheduledJobNames
{
    public const string EmailDispatchDrain = "email-dispatch-drain";
    public const string PdsSyncDrain = "pds-sync-drain";
    public const string EmailDispatchRecoveryScan = "email-dispatch-recovery-scan";
    public const string DeadLetterSummary = "dead-letter-summary";
    public const string EventReminderDispatch = "event-reminder-dispatch";
    public const string WaitlistPromotionScan = "waitlist-promotion-scan";
    public const string TenantMaintenanceScan = "tenant-maintenance-scan";

    // Periodic maintenance sweeps. These names appear in scheduler rows and operator tooling, so they are
    // stable identifiers rather than descriptions: renaming one orphans its persisted trigger.
    public const string IdempotencyCleanup = "idempotency-cleanup";
    public const string AtprotoTransientCleanup = "atproto-transient-cleanup";
    public const string AiRetentionCleanup = "ai-retention-cleanup";
    public const string EmailDispatchRetentionCleanup = "email-dispatch-retention-cleanup";
    public const string WebhookRetentionCleanup = "webhook-retention-cleanup";
    public const string PrivacyErasureCredentialCleanup = "privacy-erasure-credential-cleanup";
    public const string StorageReconciliation = "storage-reconciliation";
    public const string RegistrationRetentionCleanup = "registration-retention-cleanup";
    public const string OrganizerPaymentReadinessReconciliation = "organizer-payment-readiness-reconciliation";
    public const string ConfigurationPortabilityRetentionCleanup =
        "configuration-portability-retention-cleanup";

    // Inventory-hold expiry runs as a pair: a deadline trigger per order gives punctuality, and a low
    // frequency sweep gives the correctness guarantee. Neither replaces the other — see the reconciliation
    // job for why the sweep cannot be dropped once deadlines exist.
    public const string InventoryHoldExpiry = "inventory-hold-expiry";
    public const string InventoryHoldExpiryReconciliation = "inventory-hold-expiry-reconciliation";

    /// <summary>Bounded drain migration: the timer moved to the scheduler, the claim semantics did not.</summary>
    public const string RegistrationFinalizationDrain = "registration-finalization-drain";
    public const string PaymentReconciliationDrain = "payment-reconciliation-drain";
    public const string FairReturnOrchestration = "fair-return-orchestration";
    public const string RegistrationProviderSubmissionWriteDrain = "registration-provider-submission-write-drain";
    public const string RegistrationProviderSubscriptionLifecycleDrain = "registration-provider-subscription-lifecycle-drain";
    public const string IntegrationSyncDrain = "integration-sync-drain";
    public const string LocalWebhookDeliveryDrain = "local-webhook-delivery-drain";
    public const string IncomingWebhookIntakeDrain = "incoming-webhook-intake-drain";
    public const string IncomingWebhookEffectDrain = "incoming-webhook-effect-drain";
    public const string WebhookBulkReplayDrain = "webhook-bulk-replay-drain";
    public const string WebhookProviderPublicationDrain = "webhook-provider-publication-drain";

    /// <summary>
    /// Every catalog name, used to bound telemetry label cardinality. Metric labels are exported and
    /// retained far more widely than logs, so a job name that is not in this catalog is collapsed rather
    /// than allowed to create its own time series.
    /// </summary>
    public static readonly FrozenSet<string> All = new[]
    {
        EmailDispatchDrain,
        PdsSyncDrain,
        EmailDispatchRecoveryScan,
        DeadLetterSummary,
        EventReminderDispatch,
        WaitlistPromotionScan,
        TenantMaintenanceScan,
        IdempotencyCleanup,
        AtprotoTransientCleanup,
        AiRetentionCleanup,
        EmailDispatchRetentionCleanup,
        WebhookRetentionCleanup,
        PrivacyErasureCredentialCleanup,
        StorageReconciliation,
        RegistrationRetentionCleanup,
        OrganizerPaymentReadinessReconciliation,
        ConfigurationPortabilityRetentionCleanup,
        InventoryHoldExpiry,
        InventoryHoldExpiryReconciliation,
        RegistrationFinalizationDrain,
        PaymentReconciliationDrain,
        FairReturnOrchestration,
        RegistrationProviderSubmissionWriteDrain,
        RegistrationProviderSubscriptionLifecycleDrain,
        IntegrationSyncDrain,
        LocalWebhookDeliveryDrain,
        IncomingWebhookIntakeDrain,
        IncomingWebhookEffectDrain,
        WebhookBulkReplayDrain,
        WebhookProviderPublicationDrain,
    }.ToFrozenSet(StringComparer.Ordinal);
}

/// <summary>Bounded outcome vocabulary for scheduled job execution telemetry.</summary>
public static class SchedulerJobOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    /// <summary>A trigger listener refused the execution; the job never ran.</summary>
    public const string Vetoed = "vetoed";
}
