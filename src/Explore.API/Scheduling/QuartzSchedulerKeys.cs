// ABOUTME: Centralized Quartz job and trigger keys derived from the Application scheduled-job catalog.
// ABOUTME: Keeps scheduler identity stable across restarts so the persistent store recognizes existing rows.

using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Scheduling;
using Quartz;

namespace Explore.API.Scheduling;

/// <summary>
/// Job keys are persisted in the ADO job store, so they must never change for an existing deployment.
/// They are derived from <see cref="ScheduledJobNames"/> so the catalog stays the single source of naming truth.
/// </summary>
public static class QuartzSchedulerKeys
{
    /// <summary>Group for platform-owned recurring maintenance work.</summary>
    public const string RecurringGroup = "platform-recurring";

    /// <summary>Group for one-off triggers scheduled at runtime from application code.</summary>
    public const string OnDemandGroup = "platform-on-demand";

    /// <summary>Quartz requires <c>?</c> in either day-of-month or day-of-week; the catalog stores the same text.</summary>
    public const string EmailDispatchDrainCron = "*/10 * * * * ?";

    public const string EmailDispatchRecoveryScanCron = "0 */1 * * * ?";

    /// <summary>
    /// Five minutes, not the one minute the polling worker used: the per-order deadline trigger now handles
    /// the punctual case, so this sweep only has to catch orders no deadline covered.
    /// </summary>
    public const string InventoryHoldExpiryReconciliationCron = "0 */5 * * * ?";

    /// <summary>Matches the ten-second cadence of the polling worker this job replaced.</summary>
    public const string RegistrationFinalizationDrainCron = "*/10 * * * * ?";
    public const string PaymentReconciliationDrainCron = "*/30 * * * * ?";
    public const string FairReturnOrchestrationCron = "*/15 * * * * ?";
    public const string RegistrationProviderSubmissionWriteDrainCron = "*/10 * * * * ?";
    public const string RegistrationProviderSubscriptionLifecycleDrainCron = "*/30 * * * * ?";

    public static readonly JobKey EmailDispatchDrain =
        new(ScheduledJobNames.EmailDispatchDrain, RecurringGroup);

    public static readonly JobKey EmailDispatchRecoveryScan =
        new(ScheduledJobNames.EmailDispatchRecoveryScan, RecurringGroup);

    public static readonly JobKey EventReminderDispatch =
        new(ScheduledJobNames.EventReminderDispatch, OnDemandGroup);

    public static readonly JobKey IdempotencyCleanup =
        new(ScheduledJobNames.IdempotencyCleanup, RecurringGroup);

    public static readonly JobKey AiRetentionCleanup =
        new(ScheduledJobNames.AiRetentionCleanup, RecurringGroup);

    public static readonly JobKey AtprotoTransientCleanup =
        new(ScheduledJobNames.AtprotoTransientCleanup, RecurringGroup);

    public static readonly JobKey EmailDispatchRetentionCleanup =
        new(ScheduledJobNames.EmailDispatchRetentionCleanup, RecurringGroup);

    public static readonly JobKey WebhookRetentionCleanup =
        new(ScheduledJobNames.WebhookRetentionCleanup, RecurringGroup);

    public static readonly JobKey PrivacyErasureCredentialCleanup =
        new(ScheduledJobNames.PrivacyErasureCredentialCleanup, RecurringGroup);

    public static readonly JobKey StorageReconciliation =
        new(ScheduledJobNames.StorageReconciliation, RecurringGroup);

    public static readonly JobKey RegistrationRetentionCleanup =
        new(ScheduledJobNames.RegistrationRetentionCleanup, RecurringGroup);

    public static readonly JobKey OrganizerPaymentReadinessReconciliation =
        new(ScheduledJobNames.OrganizerPaymentReadinessReconciliation, RecurringGroup);

    public static readonly JobKey ConfigurationPortabilityRetentionCleanup =
        new(ScheduledJobNames.ConfigurationPortabilityRetentionCleanup, RecurringGroup);

    public static readonly JobKey InventoryHoldExpiry =
        new(ScheduledJobNames.InventoryHoldExpiry, OnDemandGroup);

    public static readonly JobKey InventoryHoldExpiryReconciliation =
        new(ScheduledJobNames.InventoryHoldExpiryReconciliation, RecurringGroup);

    public static readonly JobKey RegistrationFinalizationDrain =
        new(ScheduledJobNames.RegistrationFinalizationDrain, RecurringGroup);

    public static readonly JobKey PaymentReconciliationDrain =
        new(ScheduledJobNames.PaymentReconciliationDrain, RecurringGroup);
    public static readonly JobKey FairReturnOrchestration =
        new(ScheduledJobNames.FairReturnOrchestration, RecurringGroup);
    public static readonly JobKey RegistrationProviderSubmissionWriteDrain =
        new(ScheduledJobNames.RegistrationProviderSubmissionWriteDrain, RecurringGroup);
    public static readonly JobKey RegistrationProviderSubscriptionLifecycleDrain =
        new(ScheduledJobNames.RegistrationProviderSubscriptionLifecycleDrain, RecurringGroup);
    public static readonly JobKey IntegrationSyncDrain =
        new(ScheduledJobNames.IntegrationSyncDrain, RecurringGroup);
    public static readonly JobKey PdsSyncDrain =
        new(ScheduledJobNames.PdsSyncDrain, RecurringGroup);
    public static readonly JobKey LocalWebhookDeliveryDrain =
        new(ScheduledJobNames.LocalWebhookDeliveryDrain, RecurringGroup);
    public static readonly JobKey IncomingWebhookIntakeDrain =
        new(ScheduledJobNames.IncomingWebhookIntakeDrain, RecurringGroup);
    public static readonly JobKey IncomingWebhookEffectDrain =
        new(ScheduledJobNames.IncomingWebhookEffectDrain, RecurringGroup);
    public static readonly JobKey WebhookBulkReplayDrain =
        new(ScheduledJobNames.WebhookBulkReplayDrain, RecurringGroup);
    public static readonly JobKey WebhookProviderPublicationDrain =
        new(ScheduledJobNames.WebhookProviderPublicationDrain, RecurringGroup);

    /// <summary>
    /// Exact recurring identities owned by this host. The retired key stays here so disabling or upgrading
    /// removes only our stale definitions while preserving every foreign scheduler entry.
    /// </summary>
    public static readonly IReadOnlySet<JobKey> OwnedRecurringJobs = new HashSet<JobKey>
    {
        EmailDispatchDrain,
        EmailDispatchRecoveryScan,
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
        InventoryHoldExpiryReconciliation,
        RegistrationFinalizationDrain,
        PaymentReconciliationDrain,
        FairReturnOrchestration,
        RegistrationProviderSubmissionWriteDrain,
        RegistrationProviderSubscriptionLifecycleDrain,
        IntegrationSyncDrain,
        PdsSyncDrain,
        LocalWebhookDeliveryDrain,
        IncomingWebhookIntakeDrain,
        IncomingWebhookEffectDrain,
        WebhookBulkReplayDrain,
        WebhookProviderPublicationDrain,
        new("general-outbox-drain", RecurringGroup),
    };

    public static TriggerKey RecurringTriggerFor(JobKey jobKey)
    {
        ArgumentNullException.ThrowIfNull(jobKey);
        return new TriggerKey(jobKey.Name, jobKey.Group);
    }

    /// <summary>
    /// Jobs that may be woken by a <see cref="ScheduledDeadline"/>. The map is closed on purpose: an
    /// unknown job name is a caller mistake that should be reported, not a trigger quietly attached to a
    /// job that does not exist and can therefore never run.
    /// </summary>
    private static readonly Dictionary<string, JobKey> DeadlineJobKeysByName = new(StringComparer.Ordinal)
    {
        [ScheduledJobNames.EventReminderDispatch] = EventReminderDispatch,
        [ScheduledJobNames.InventoryHoldExpiry] = InventoryHoldExpiry,
    };

    public static bool TryResolveDeadlineJob(string jobName, out JobKey jobKey)
        => DeadlineJobKeysByName.TryGetValue(jobName, out jobKey!);

    /// <summary>
    /// Builds the deterministic trigger identity for one deadline. Determinism is what makes
    /// re-registration a replacement rather than a duplicate, and what lets cancellation find the trigger
    /// again in a later process.
    /// </summary>
    public static TriggerKey DeadlineTriggerFor(string jobName, string deadlineKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deadlineKey);

        var name = string.Concat(jobName, DeadlineKeySeparator, deadlineKey);

        // MySQL/MariaDB store trigger names in VARCHAR(200). A caller-supplied key long enough to overflow
        // that would fail at insert time on one provider only, so an over-long name collapses to a stable
        // digest instead: still deterministic, so cancellation and replacement keep working.
        return new TriggerKey(
            name.Length <= MaxTriggerNameLength ? name : BuildDigestedTriggerName(jobName, deadlineKey),
            OnDemandGroup);
    }

    private const string DeadlineKeySeparator = ":";

    /// <summary>Narrowest trigger-name column across the supported providers (MySQL/MariaDB).</summary>
    private const int MaxTriggerNameLength = 200;

    private static string BuildDigestedTriggerName(string jobName, string deadlineKey)
    {
        var digest = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(deadlineKey)));

        // Keep the job name readable in operator tooling, then spend the remaining budget on the digest.
        var availableForJobName = MaxTriggerNameLength - DeadlineKeySeparator.Length - digest.Length;
        var truncatedJobName = jobName.Length <= availableForJobName
            ? jobName
            : jobName[..Math.Max(0, availableForJobName)];

        return string.Concat(truncatedJobName, DeadlineKeySeparator, digest);
    }
}
