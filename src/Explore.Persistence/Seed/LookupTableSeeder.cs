using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Modules;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Seed;

/// <summary>
/// Seeds lookup/enum tables at runtime. Runs in ALL environments (dev, staging, production).
///
/// Why runtime seeding instead of HasData():
/// EF Core 10 has a known bug (#36682) where dotnet ef migrations add crashes with
/// "Sequence contains no elements" when the model has circular FKs (User/Organization ↔ Actor)
/// combined with HasData on any entities. Moving seed data to runtime eliminates the issue.
///
/// The data was originally seeded via HasData() in entity configurations and exists in
/// existing migration files. This seeder ensures idempotent seeding for fresh databases
/// where migrations run in order.
/// </summary>
public static class LookupTableSeeder
{
    /// <summary>
    /// Seeds all lookup tables if they don't already contain data.
    /// Must be called after migrations are applied.
    /// </summary>
    public static async Task SeedAsync(ExploreDbContext context, CancellationToken cancellationToken = default)
    {
        await SeedActorTypesAsync(context, cancellationToken);
        await SeedActorSubscriptionStatusesAsync(context, cancellationToken);
        await SeedActorSubscriptionNotificationLevelsAsync(context, cancellationToken);
        await SeedRoleScopesAsync(context, cancellationToken);
        await SeedSettingScopesAsync(context, cancellationToken);
        await SeedSettingValueTypesAsync(context, cancellationToken);
        await SeedSecretSourceTypesAsync(context, cancellationToken);
        await SeedSecretValidationStatusesAsync(context, cancellationToken);
        await SeedExternalApiKeyOwnerTypesAsync(context, cancellationToken);
        await SeedNotificationScopeTypesAsync(context, cancellationToken);
        await SeedNotificationCategoriesAsync(context, cancellationToken);
        await SeedNotificationPreferenceCategoriesAsync(context, cancellationToken);
        await SeedNotificationPreferenceChannelsAsync(context, cancellationToken);
        await SeedNotificationOwnershipTypesAsync(context, cancellationToken);
        await SeedNotificationIntentStatusesAsync(context, cancellationToken);
        await SeedNotificationRecipientKindsAsync(context, cancellationToken);
        await SeedNotificationDeliveryStatusesAsync(context, cancellationToken);
        await SeedNotificationDeliveryPoliciesAsync(context, cancellationToken);
        await SeedNotificationExternalDelegationStatusesAsync(context, cancellationToken);
        await SeedExternalWorkflowProviderKindsAsync(context, cancellationToken);
        await SeedAccountAuthorityKindsAsync(context, cancellationToken);
        await SeedAuthenticationProvidersAsync(context, cancellationToken);
        await SeedSupportAccessSessionStatusesAsync(context, cancellationToken);
        await SeedSupportAccessModesAsync(context, cancellationToken);
        await SeedSupportAccessEndReasonsAsync(context, cancellationToken);
        await SeedSupportAccessAuditEventTypesAsync(context, cancellationToken);
        await SeedWebhookProviderBindingVerificationStatesAsync(context, cancellationToken);
        await SeedWebhookLookupsAsync(context, cancellationToken);
        await SeedApprovalStatusesAsync(context, cancellationToken);
        await SeedLocationPrivacyLookupsAsync(context, cancellationToken);
        await SeedLocationAddressGovernanceLookupsAsync(context, cancellationToken);
        await SeedAnalyticsProvidersAsync(context, cancellationToken);
        await SeedTenantStatusesAsync(context, cancellationToken);
        await SeedTenantPlanStatusesAsync(context, cancellationToken);
        await SeedTenantPlanAssignmentStatusesAsync(context, cancellationToken);
        await SeedTenantPlanApplicationStatusesAsync(context, cancellationToken);
        await SeedAudienceAgesAsync(context, cancellationToken);
        await SeedAudienceGendersAsync(context, cancellationToken);
        await SeedDidCustodyTypesAsync(context, cancellationToken);
        await SeedEventFormatsAsync(context, cancellationToken);
        await SeedEventAuthorityLookupsAsync(context, cancellationToken);
        await SeedEventParticipationLookupsAsync(context, cancellationToken);
        await SeedTicketingLookupsAsync(context, cancellationToken);
        await SeedParticipantLookupsAsync(context, cancellationToken);
        await SeedRegistrationOrderLookupsAsync(context, cancellationToken);
        await SeedAdmissionLookupsAsync(context, cancellationToken);
        await SeedPromotionLookupsAsync(context, cancellationToken);
        await SeedRegistrationWorkflowLookupsAsync(context, cancellationToken);
        await SeedRegistrationFormLookupsAsync(context, cancellationToken);
        await SeedRegistrationRetentionLookupsAsync(context, cancellationToken);
        await SeedContactShareLookupsAsync(context, cancellationToken);
        await SeedRegistrationRuntimeLookupsAsync(context, cancellationToken);
        await SeedRegistrationProviderLookupsAsync(context, cancellationToken);
        await SeedPlatformMonetizationDefaultsAsync(context, cancellationToken);
        await SeedEventStatusesAsync(context, cancellationToken);
        await SeedEventSessionStatusesAsync(context, cancellationToken);
        await SeedEventTypesAsync(context, cancellationToken);
        await SeedFileTypesAsync(context, cancellationToken);
        await SeedLanguagesAsync(context, cancellationToken);
        await SeedMadhabsAsync(context, cancellationToken);
        await SeedModuleDefinitionsAsync(context, cancellationToken);
        await SeedOrganizationPositionsAsync(context, cancellationToken);
        await SeedGroupPositionsAsync(context, cancellationToken);
        await SeedRegistrationModesAsync(context, cancellationToken);
        await SeedRolesAsync(context, cancellationToken);
        await SeedSystemSettingsAsync(context, cancellationToken);
        await SeedTagTypesAsync(context, cancellationToken);
        await SeedVisibilityTypesAsync(context, cancellationToken);
        await SeedPermissionsAsync(context, cancellationToken);
        await SeedEventRolePermissionsAsync(context, cancellationToken);
        await SeedNotificationTypesAsync(context, cancellationToken);
        await SeedNotificationEntityTypesAsync(context, cancellationToken);
        await SeedDefaultFooterLinkGroupsAsync(context, cancellationToken);
        await SeedExternalApiKeyStatusesAsync(context, cancellationToken);
        await SeedExternalApiKeyCreditPeriodsAsync(context, cancellationToken);
        await SeedNotificationReasonsAsync(context, cancellationToken);
        await SeedAiConversationStatusesAsync(context, cancellationToken);
        await SeedAiMessageRolesAsync(context, cancellationToken);
        await SeedAiRunStatusesAsync(context, cancellationToken);
        await SeedAiReferenceKindsAsync(context, cancellationToken);
        await SeedAiProposedActionKindsAsync(context, cancellationToken);
        await SeedAiProposedActionStatusesAsync(context, cancellationToken);
        await SeedAiProviderKindsAsync(context, cancellationToken);
        await SeedEventSessionKindsAsync(context, cancellationToken);
        await SeedScheduleItemKindsAsync(context, cancellationToken);
        await SeedEventRegistrationPoliciesAsync(context, cancellationToken);
        await SeedRegistrationScopesAsync(context, cancellationToken);
        await SeedUiThemePresetsAsync(context, cancellationToken);
    }

    private static async Task SeedWebhookProviderBindingVerificationStatesAsync(
        ExploreDbContext context,
        CancellationToken cancellationToken)
    {
        var states = new WebhookProviderBindingVerificationStateLookup[]
        {
            new() { Id = (int)WebhookProviderBindingVerificationState.LegacyUnverified, MasterCode = "LEGACY_UNVERIFIED", FullName = "Legacy unverified", Description = "Legacy binding identity could not be proven and cannot grant provider authority" },
            new() { Id = (int)WebhookProviderBindingVerificationState.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Binding is awaiting provider ownership verification" },
            new() { Id = (int)WebhookProviderBindingVerificationState.Verified, MasterCode = "VERIFIED", FullName = "Verified", Description = "Provider ownership matches the persisted tenant and webhook consumer" },
            new() { Id = (int)WebhookProviderBindingVerificationState.Rejected, MasterCode = "REJECTED", FullName = "Rejected", Description = "Provider ownership verification was rejected" },
            new() { Id = (int)WebhookProviderBindingVerificationState.Revoked, MasterCode = "REVOKED", FullName = "Revoked", Description = "Previously verified provider authority has been revoked" }
        };

        var existingIds = await context.WebhookProviderBindingVerificationStates
            .Select(state => state.Id)
            .ToListAsync(cancellationToken);
        var missingStates = states
            .Where(state => !existingIds.Contains(state.Id))
            .ToArray();
        if (missingStates.Length == 0)
        {
            return;
        }

        context.WebhookProviderBindingVerificationStates.AddRange(missingStates);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedWebhookLookupsAsync(
        ExploreDbContext context,
        CancellationToken cancellationToken)
    {
        await AddMissingLookupRowsAsync(context.WebhookConsumerKinds,
        [
            new() { Id = (int)WebhookConsumerKind.Tenant, MasterCode = "TENANT", FullName = "Tenant", Description = "Tenant-owned webhook consumer" },
            new() { Id = (int)WebhookConsumerKind.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Organization-owned webhook consumer" },
            new() { Id = (int)WebhookConsumerKind.Group, MasterCode = "GROUP", FullName = "Group", Description = "Group-owned webhook consumer" },
            new() { Id = (int)WebhookConsumerKind.User, MasterCode = "USER", FullName = "User", Description = "User-owned webhook consumer" },
            new() { Id = (int)WebhookConsumerKind.Instance, MasterCode = "INSTANCE", FullName = "Instance", Description = "Instance-owned webhook consumer" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookConsumerStatuses,
        [
            new() { Id = (int)WebhookConsumerStatus.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Consumer may receive newly materialized webhook work" },
            new() { Id = (int)WebhookConsumerStatus.Disabled, MasterCode = "DISABLED", FullName = "Disabled", Description = "Consumer is disabled for new webhook work" },
            new() { Id = (int)WebhookConsumerStatus.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Consumer is retained as historical evidence" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookProviderModes,
        [
            new() { Id = (int)WebhookProviderMode.Disabled, MasterCode = "DISABLED", FullName = "Disabled", Description = "Webhook delivery is disabled" },
            new() { Id = (int)WebhookProviderMode.Local, MasterCode = "LOCAL", FullName = "Local", Description = "Deliver through the platform Local provider" },
            new() { Id = (int)WebhookProviderMode.Svix, MasterCode = "SVIX", FullName = "Svix", Description = "Publish through a verified Svix binding" },
            new() { Id = (int)WebhookProviderMode.Composite, MasterCode = "COMPOSITE", FullName = "Composite", Description = "Materialize independent Local and provider work" },
            new() { Id = (int)WebhookProviderMode.DryRun, MasterCode = "DRY_RUN", FullName = "Dry run", Description = "Materialize evidence without network delivery" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookProviderKinds,
        [
            new() { Id = (int)WebhookProviderKind.Local, MasterCode = "LOCAL", FullName = "Local", Description = "Platform-owned direct HTTP delivery" },
            new() { Id = (int)WebhookProviderKind.Svix, MasterCode = "SVIX", FullName = "Svix", Description = "Svix application delivery provider" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookProviderCapabilities,
        [
            new() { Id = (int)WebhookProviderCapability.EndpointManagement, MasterCode = "ENDPOINT_MANAGEMENT", FullName = "Endpoint management", Description = "Create and maintain delivery endpoints" },
            new() { Id = (int)WebhookProviderCapability.ProviderAttemptVisibility, MasterCode = "PROVIDER_ATTEMPT_VISIBILITY", FullName = "Provider attempt visibility", Description = "Inspect delivery attempts recorded by the provider" },
            new() { Id = (int)WebhookProviderCapability.Replay, MasterCode = "REPLAY", FullName = "Replay", Description = "Request provider-side replay or recovery" },
            new() { Id = (int)WebhookProviderCapability.PayloadInspection, MasterCode = "PAYLOAD_INSPECTION", FullName = "Payload inspection", Description = "Inspect retained message payload content" },
            new() { Id = (int)WebhookProviderCapability.AppPortal, MasterCode = "APP_PORTAL", FullName = "App portal", Description = "Issue tenant-scoped provider portal sessions" },
            new() { Id = (int)WebhookProviderCapability.EventCatalog, MasterCode = "EVENT_CATALOG", FullName = "Event catalog", Description = "Synchronize webhook event definitions" },
            new() { Id = (int)WebhookProviderCapability.ProviderRetentionControl, MasterCode = "PROVIDER_RETENTION_CONTROL", FullName = "Provider retention control", Description = "Select provider-side payload retention" },
            new() { Id = (int)WebhookProviderCapability.ApplicationThrottling, MasterCode = "APPLICATION_THROTTLING", FullName = "Application throttling", Description = "Apply provider application-level throttling" },
            new() { Id = (int)WebhookProviderCapability.EndpointThrottling, MasterCode = "ENDPOINT_THROTTLING", FullName = "Endpoint throttling", Description = "Apply provider endpoint-level throttling" },
            new() { Id = (int)WebhookProviderCapability.Transformations, MasterCode = "TRANSFORMATIONS", FullName = "Transformations", Description = "Apply provider-managed payload transformations" },
            new() { Id = (int)WebhookProviderCapability.Ordering, MasterCode = "ORDERING", FullName = "Ordering", Description = "Apply provider-managed delivery ordering" },
            new() { Id = (int)WebhookProviderCapability.OperationalCallbacks, MasterCode = "OPERATIONAL_CALLBACKS", FullName = "Operational callbacks", Description = "Receive provider operational status callbacks" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookEndpointStatuses,
        [
            new() { Id = (int)WebhookEndpointStatus.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Endpoint accepts newly materialized Local work" },
            new() { Id = (int)WebhookEndpointStatus.Disabled, MasterCode = "DISABLED", FullName = "Disabled", Description = "Endpoint is administratively disabled" },
            new() { Id = (int)WebhookEndpointStatus.AutoPaused, MasterCode = "AUTO_PAUSED", FullName = "Auto-paused", Description = "Endpoint was paused by bounded failure policy" },
            new() { Id = (int)WebhookEndpointStatus.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Endpoint is retained as historical evidence" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookLocalDeliveryStatuses,
        [
            new() { Id = (int)WebhookLocalDeliveryStatus.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Local target is waiting for its first claim" },
            new() { Id = (int)WebhookLocalDeliveryStatus.Delivering, MasterCode = "DELIVERING", FullName = "Delivering", Description = "Local target has an active fenced delivery claim" },
            new() { Id = (int)WebhookLocalDeliveryStatus.RetryDue, MasterCode = "RETRY_DUE", FullName = "Retry due", Description = "Local target is waiting for a bounded retry" },
            new() { Id = (int)WebhookLocalDeliveryStatus.Succeeded, MasterCode = "SUCCEEDED", FullName = "Succeeded", Description = "Local target completed successfully" },
            new() { Id = (int)WebhookLocalDeliveryStatus.DeadLettered, MasterCode = "DEAD_LETTERED", FullName = "Dead-lettered", Description = "Local target exhausted automatic delivery" },
            new() { Id = (int)WebhookLocalDeliveryStatus.Abandoned, MasterCode = "ABANDONED", FullName = "Abandoned", Description = "Local target was explicitly abandoned" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookBulkReplayStatuses,
        [
            new() { Id = (int)WebhookBulkReplayStatus.Queued, MasterCode = "QUEUED", FullName = "Queued", Description = "Replay operation is waiting for worker execution" },
            new() { Id = (int)WebhookBulkReplayStatus.Executing, MasterCode = "EXECUTING", FullName = "Executing", Description = "Replay operation is re-evaluating and scheduling eligible Local targets" },
            new() { Id = (int)WebhookBulkReplayStatus.Completed, MasterCode = "COMPLETED", FullName = "Completed", Description = "Replay operation completed its bounded Local-target scheduling" },
            new() { Id = (int)WebhookBulkReplayStatus.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled", Description = "Replay operation was cancelled before worker execution" },
            new() { Id = (int)WebhookBulkReplayStatus.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Replay operation could not safely complete" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookPendingWorkDecisions,
        [
            new() { Id = (int)WebhookPendingWorkDecision.PreserveExisting, MasterCode = "PRESERVE_EXISTING", FullName = "Preserve existing", Description = "Keep already materialized work on its immutable configuration snapshots" },
            new() { Id = (int)WebhookPendingWorkDecision.MigrateEligible, MasterCode = "MIGRATE_ELIGIBLE", FullName = "Migrate eligible", Description = "Move only unclaimed pending Local work to the new endpoint configuration" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookRetentionSubjectKinds,
        [
            new() { Id = (int)WebhookRetentionSubjectKind.OutgoingMessage, MasterCode = "OUTGOING_MESSAGE", FullName = "Outgoing message" },
            new() { Id = (int)WebhookRetentionSubjectKind.IncomingMessage, MasterCode = "INCOMING_MESSAGE", FullName = "Incoming message" },
            new() { Id = (int)WebhookRetentionSubjectKind.DeliveryAttempt, MasterCode = "DELIVERY_ATTEMPT", FullName = "Delivery attempt" },
            new() { Id = (int)WebhookRetentionSubjectKind.ProviderPublication, MasterCode = "PROVIDER_PUBLICATION", FullName = "Provider publication" },
            new() { Id = (int)WebhookRetentionSubjectKind.AdministrativeAudit, MasterCode = "ADMINISTRATIVE_AUDIT", FullName = "Administrative audit" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookAuditActions,
        [
            new() { Id = (int)WebhookAuditAction.ConsumerCreated, MasterCode = "CONSUMER_CREATED", FullName = "Consumer created" },
            new() { Id = (int)WebhookAuditAction.ConsumerProviderModeChanged, MasterCode = "CONSUMER_PROVIDER_MODE_CHANGED", FullName = "Consumer provider mode changed" },
            new() { Id = (int)WebhookAuditAction.EndpointCreated, MasterCode = "ENDPOINT_CREATED", FullName = "Endpoint created" },
            new() { Id = (int)WebhookAuditAction.EndpointUpdated, MasterCode = "ENDPOINT_UPDATED", FullName = "Endpoint updated" },
            new() { Id = (int)WebhookAuditAction.EndpointArchived, MasterCode = "ENDPOINT_ARCHIVED", FullName = "Endpoint archived" },
            new() { Id = (int)WebhookAuditAction.EndpointSecretRotated, MasterCode = "ENDPOINT_SECRET_ROTATED", FullName = "Endpoint signing credential rotated" },
            new() { Id = (int)WebhookAuditAction.EndpointTestScheduled, MasterCode = "ENDPOINT_TEST_SCHEDULED", FullName = "Endpoint test scheduled" },
            new() { Id = (int)WebhookAuditAction.ProviderBindingRepairSucceeded, MasterCode = "PROVIDER_BINDING_REPAIR_SUCCEEDED", FullName = "Provider binding repair succeeded" },
            new() { Id = (int)WebhookAuditAction.ProviderBindingRepairRejected, MasterCode = "PROVIDER_BINDING_REPAIR_REJECTED", FullName = "Provider binding repair rejected" },
            new() { Id = (int)WebhookAuditAction.PortalAccessIssued, MasterCode = "PORTAL_ACCESS_ISSUED", FullName = "Portal access issued" },
            new() { Id = (int)WebhookAuditAction.PortalAccessRejected, MasterCode = "PORTAL_ACCESS_REJECTED", FullName = "Portal access rejected" },
            new() { Id = (int)WebhookAuditAction.DeliveryRetryScheduled, MasterCode = "DELIVERY_RETRY_SCHEDULED", FullName = "Delivery retry scheduled" },
            new() { Id = (int)WebhookAuditAction.IncomingRedriveScheduled, MasterCode = "INCOMING_REDRIVE_SCHEDULED", FullName = "Incoming redrive scheduled" },
            new() { Id = (int)WebhookAuditAction.EndpointAutoPaused, MasterCode = "ENDPOINT_AUTO_PAUSED", FullName = "Endpoint automatically paused" },
            new() { Id = (int)WebhookAuditAction.EndpointResumed, MasterCode = "ENDPOINT_RESUMED", FullName = "Endpoint resumed" },
            new() { Id = (int)WebhookAuditAction.ProviderPublicationReconciled, MasterCode = "PROVIDER_PUBLICATION_RECONCILED", FullName = "Provider publication reconciled" },
            new() { Id = (int)WebhookAuditAction.ProviderPublicationAbandoned, MasterCode = "PROVIDER_PUBLICATION_ABANDONED", FullName = "Provider publication abandoned" },
            new() { Id = (int)WebhookAuditAction.BulkReplayScheduled, MasterCode = "BULK_REPLAY_SCHEDULED", FullName = "Bulk replay scheduled" },
            new() { Id = (int)WebhookAuditAction.PendingWorkMigrated, MasterCode = "PENDING_WORK_MIGRATED", FullName = "Pending work migrated" },
            new() { Id = (int)WebhookAuditAction.PayloadViewed, MasterCode = "PAYLOAD_VIEWED", FullName = "Payload viewed" },
            new() { Id = (int)WebhookAuditAction.RetentionPolicyChanged, MasterCode = "RETENTION_POLICY_CHANGED", FullName = "Retention policy changed" },
            new() { Id = (int)WebhookAuditAction.RetentionCleanupCompleted, MasterCode = "RETENTION_CLEANUP_COMPLETED", FullName = "Retention cleanup completed" },
            new() { Id = (int)WebhookAuditAction.EndpointPaused, MasterCode = "ENDPOINT_PAUSED", FullName = "Endpoint manually paused" },
            new() { Id = (int)WebhookAuditAction.BulkReplayCancelled, MasterCode = "BULK_REPLAY_CANCELLED", FullName = "Bulk replay cancelled" },
            new() { Id = (int)WebhookAuditAction.BulkReplayCompleted, MasterCode = "BULK_REPLAY_COMPLETED", FullName = "Bulk replay completed" },
            new() { Id = (int)WebhookAuditAction.BulkReplayFailed, MasterCode = "BULK_REPLAY_FAILED", FullName = "Bulk replay failed" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookAuditOutcomes,
        [
            new() { Id = (int)WebhookAuditOutcome.Succeeded, MasterCode = "SUCCEEDED", FullName = "Succeeded" },
            new() { Id = (int)WebhookAuditOutcome.Rejected, MasterCode = "REJECTED", FullName = "Rejected" },
            new() { Id = (int)WebhookAuditOutcome.Failed, MasterCode = "FAILED", FullName = "Failed" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookAuditPrincipalKinds,
        [
            new() { Id = (int)WebhookAuditPrincipalKind.User, MasterCode = "USER", FullName = "User" },
            new() { Id = (int)WebhookAuditPrincipalKind.Machine, MasterCode = "MACHINE", FullName = "Machine" },
            new() { Id = (int)WebhookAuditPrincipalKind.System, MasterCode = "SYSTEM", FullName = "System" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookAuditScopeKinds,
        [
            new() { Id = (int)WebhookAuditScopeKind.Tenant, MasterCode = "TENANT", FullName = "Tenant" },
            new() { Id = (int)WebhookAuditScopeKind.Instance, MasterCode = "INSTANCE", FullName = "Instance" },
            new() { Id = (int)WebhookAuditScopeKind.Organization, MasterCode = "ORGANIZATION", FullName = "Organization" },
            new() { Id = (int)WebhookAuditScopeKind.Group, MasterCode = "GROUP", FullName = "Group" },
            new() { Id = (int)WebhookAuditScopeKind.User, MasterCode = "USER", FullName = "User" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookAuditTargetKinds,
        [
            new() { Id = (int)WebhookAuditTargetKind.Consumer, MasterCode = "CONSUMER", FullName = "Consumer" },
            new() { Id = (int)WebhookAuditTargetKind.Endpoint, MasterCode = "ENDPOINT", FullName = "Endpoint" },
            new() { Id = (int)WebhookAuditTargetKind.ProviderBinding, MasterCode = "PROVIDER_BINDING", FullName = "Provider binding" },
            new() { Id = (int)WebhookAuditTargetKind.PortalSession, MasterCode = "PORTAL_SESSION", FullName = "Portal session" },
            new() { Id = (int)WebhookAuditTargetKind.DeliveryAttempt, MasterCode = "DELIVERY_ATTEMPT", FullName = "Delivery attempt" },
            new() { Id = (int)WebhookAuditTargetKind.IncomingMessage, MasterCode = "INCOMING_MESSAGE", FullName = "Incoming message" },
            new() { Id = (int)WebhookAuditTargetKind.ProviderPublication, MasterCode = "PROVIDER_PUBLICATION", FullName = "Provider publication" },
            new() { Id = (int)WebhookAuditTargetKind.RetentionPolicy, MasterCode = "RETENTION_POLICY", FullName = "Retention policy" },
            new() { Id = (int)WebhookAuditTargetKind.CleanupRun, MasterCode = "CLEANUP_RUN", FullName = "Cleanup run" },
            new() { Id = (int)WebhookAuditTargetKind.Payload, MasterCode = "PAYLOAD", FullName = "Payload" },
            new() { Id = (int)WebhookAuditTargetKind.BulkReplayOperation, MasterCode = "BULK_REPLAY_OPERATION", FullName = "Bulk replay operation" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookDeliveryAttemptOutcomes,
        [
            new() { Id = (int)WebhookDeliveryAttemptOutcome.Scheduled, MasterCode = "SCHEDULED", FullName = "Scheduled", Description = "Attempt was scheduled for delivery" },
            new() { Id = (int)WebhookDeliveryAttemptOutcome.Sending, MasterCode = "SENDING", FullName = "Sending", Description = "Attempt entered provider handoff" },
            new() { Id = (int)WebhookDeliveryAttemptOutcome.Succeeded, MasterCode = "SUCCEEDED", FullName = "Succeeded", Description = "Attempt received a successful response" },
            new() { Id = (int)WebhookDeliveryAttemptOutcome.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Attempt failed with safe classified evidence" },
            new() { Id = (int)WebhookDeliveryAttemptOutcome.Abandoned, MasterCode = "ABANDONED", FullName = "Abandoned", Description = "Attempt was not eligible for further delivery" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.IncomingWebhookMessageStatuses,
        [
            new() { Id = (int)IncomingWebhookMessageStatus.Verified, MasterCode = "VERIFIED", FullName = "Verified", Description = "Input verification succeeded and is ready to claim" },
            new() { Id = (int)IncomingWebhookMessageStatus.Processing, MasterCode = "PROCESSING", FullName = "Processing", Description = "Inbox row has an active fenced claim" },
            new() { Id = (int)IncomingWebhookMessageStatus.RetryDue, MasterCode = "RETRY_DUE", FullName = "Retry due", Description = "Inbox row is waiting for a bounded retry" },
            new() { Id = (int)IncomingWebhookMessageStatus.Processed, MasterCode = "PROCESSED", FullName = "Processed", Description = "Effect receipt and settlement completed" },
            new() { Id = (int)IncomingWebhookMessageStatus.Ignored, MasterCode = "IGNORED", FullName = "Ignored", Description = "Verified input required no business effect" },
            new() { Id = (int)IncomingWebhookMessageStatus.RejectedPermanent, MasterCode = "REJECTED_PERMANENT", FullName = "Rejected permanently", Description = "Input cannot be processed safely" },
            new() { Id = (int)IncomingWebhookMessageStatus.DeadLettered, MasterCode = "DEAD_LETTERED", FullName = "Dead-lettered", Description = "Input exhausted automatic processing" },
            new() { Id = (int)IncomingWebhookMessageStatus.PayloadConflict, MasterCode = "PAYLOAD_CONFLICT", FullName = "Payload conflict", Description = "Provider identity was reused with different exact bytes" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.IncomingWebhookProcessingAttemptOutcomes,
        [
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.Claimed, MasterCode = "CLAIMED", FullName = "Claimed", Description = "A worker acquired a fenced processing lease" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.Processed, MasterCode = "PROCESSED", FullName = "Processed", Description = "A new business effect and receipt were committed" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.SettledFromReceipt, MasterCode = "SETTLED_FROM_RECEIPT", FullName = "Settled from receipt", Description = "An existing matching effect receipt proved prior completion" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.Ignored, MasterCode = "IGNORED", FullName = "Ignored", Description = "The verified callback required no business effect" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.RejectedPermanent, MasterCode = "REJECTED_PERMANENT", FullName = "Rejected permanently", Description = "The callback could not be processed safely" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.RetryScheduled, MasterCode = "RETRY_SCHEDULED", FullName = "Retry scheduled", Description = "A transient failure scheduled bounded retry work" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.DeadLettered, MasterCode = "DEAD_LETTERED", FullName = "Dead-lettered", Description = "Automatic processing attempts were exhausted" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.PayloadConflict, MasterCode = "PAYLOAD_CONFLICT", FullName = "Payload conflict", Description = "The provider identity was reused with different exact bytes" },
            new() { Id = (int)IncomingWebhookProcessingAttemptOutcome.LeaseExpired, MasterCode = "LEASE_EXPIRED", FullName = "Lease expired", Description = "An unsettled processing lease expired and was recovered" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.IncomingWebhookSettlementSources,
        [
            new() { Id = (int)IncomingWebhookSettlementSource.EffectCommitted, MasterCode = "EFFECT_COMMITTED", FullName = "Effect committed", Description = "The current execution committed the business effect and receipt" },
            new() { Id = (int)IncomingWebhookSettlementSource.ExistingReceipt, MasterCode = "EXISTING_RECEIPT", FullName = "Existing receipt", Description = "A matching prior receipt proved the business effect already committed" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.IncomingWebhookRedriveResults,
        [
            new() { Id = (int)IncomingWebhookRedriveResult.Scheduled, MasterCode = "SCHEDULED", FullName = "Scheduled", Description = "An authorized operator created a new processing generation" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookProviderPublicationStatuses,
        [
            new() { Id = (int)WebhookProviderPublicationStatus.Prepared, MasterCode = "PREPARED", FullName = "Prepared", Description = "Provider publication is durably prepared" },
            new() { Id = (int)WebhookProviderPublicationStatus.Publishing, MasterCode = "PUBLISHING", FullName = "Publishing", Description = "Provider publication has an active fenced claim" },
            new() { Id = (int)WebhookProviderPublicationStatus.ProviderQueued, MasterCode = "PROVIDER_QUEUED", FullName = "Provider queued", Description = "Provider accepted the publication" },
            new() { Id = (int)WebhookProviderPublicationStatus.RetryDue, MasterCode = "RETRY_DUE", FullName = "Retry due", Description = "Provider publication is waiting for a bounded retry" },
            new() { Id = (int)WebhookProviderPublicationStatus.PublicationUnknown, MasterCode = "PUBLICATION_UNKNOWN", FullName = "Publication unknown", Description = "Provider acceptance could not be proven" },
            new() { Id = (int)WebhookProviderPublicationStatus.DeadLettered, MasterCode = "DEAD_LETTERED", FullName = "Dead-lettered", Description = "Provider publication exhausted automatic submission" },
            new() { Id = (int)WebhookProviderPublicationStatus.ManualReconciliation, MasterCode = "MANUAL_RECONCILIATION", FullName = "Manual reconciliation", Description = "Operator evidence is required before settlement" },
            new() { Id = (int)WebhookProviderPublicationStatus.Abandoned, MasterCode = "ABANDONED", FullName = "Abandoned", Description = "Provider publication was explicitly abandoned" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookProviderPublicationAttemptOutcomes,
        [
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.PublishingStarted, MasterCode = "PUBLISHING_STARTED", FullName = "Publishing started", Description = "A worker acquired a fenced provider submission claim" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.ProviderQueued, MasterCode = "PROVIDER_QUEUED", FullName = "Provider queued", Description = "The provider accepted the publication" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.RetryScheduled, MasterCode = "RETRY_SCHEDULED", FullName = "Retry scheduled", Description = "A definitely-not-accepted submission scheduled bounded retry" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.PublicationUnknown, MasterCode = "PUBLICATION_UNKNOWN", FullName = "Publication unknown", Description = "Submission acceptance could not be determined safely" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.DeadLettered, MasterCode = "DEAD_LETTERED", FullName = "Dead-lettered", Description = "Automatic provider submission cannot continue" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationStarted, MasterCode = "AUTOMATIC_RECONCILIATION_STARTED", FullName = "Automatic reconciliation started", Description = "A worker acquired a fenced lookup-only reconciliation claim" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationUnresolved, MasterCode = "AUTOMATIC_RECONCILIATION_UNRESOLVED", FullName = "Automatic reconciliation unresolved", Description = "Provider lookup was temporarily unavailable" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.ManualReconciliationRequired, MasterCode = "MANUAL_RECONCILIATION_REQUIRED", FullName = "Manual reconciliation required", Description = "Automatic evidence was insufficient for a safe decision" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.ReconciledProviderQueued, MasterCode = "RECONCILED_PROVIDER_QUEUED", FullName = "Reconciled provider queued", Description = "Exact provider evidence proved acceptance" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.Abandoned, MasterCode = "ABANDONED", FullName = "Abandoned", Description = "The publication was explicitly abandoned" },
            new() { Id = (int)WebhookProviderPublicationAttemptOutcome.ProviderAbsenceConfirmed, MasterCode = "PROVIDER_ABSENCE_CONFIRMED", FullName = "Provider absence confirmed", Description = "Conformance-proven lookup confirmed absence before unchanged-identity retry" }
        ], cancellationToken);

        await AddMissingLookupRowsAsync(context.WebhookPayloadProvenances,
        [
            new() { Id = (int)WebhookPayloadProvenance.ExactBytes, MasterCode = "EXACT_BYTES", FullName = "Exact bytes", Description = "Persisted bytes are the authoritative received or serialized sequence" },
            new() { Id = (int)WebhookPayloadProvenance.LegacyJsonCanonicalized, MasterCode = "LEGACY_JSON_CANONICALIZED", FullName = "Legacy JSON canonicalized", Description = "Legacy jsonb was canonicalized because original byte formatting cannot be recovered" },
            new() { Id = (int)WebhookPayloadProvenance.NormalizedProviderEnvelope, MasterCode = "NORMALIZED_PROVIDER_ENVELOPE", FullName = "Normalized provider envelope", Description = "Provider callback retained only as a minimal normalized envelope after exact-byte signature verification" }
        ], cancellationToken);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task AddMissingLookupRowsAsync<TLookup>(
        DbSet<TLookup> set,
        IReadOnlyCollection<TLookup> rows,
        CancellationToken cancellationToken)
        where TLookup : class
    {
        var existingIds = await set
            .Select(row => EF.Property<int>(row, "Id"))
            .ToListAsync(cancellationToken);
        set.AddRange(rows.Where(row => !existingIds.Contains(
            (int)(typeof(TLookup).GetProperty("Id")?.GetValue(row)
                ?? throw new InvalidOperationException($"{typeof(TLookup).Name} must expose an integer Id.")))));
    }

    private static async Task SeedAiConversationStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AiConversationStatusLookup>().AnyAsync(ct)) return;

        context.Set<AiConversationStatusLookup>().AddRange(
            new AiConversationStatusLookup { Id = (int)AiConversationStatus.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Conversation is available for user interaction" },
            new AiConversationStatusLookup { Id = (int)AiConversationStatus.Running, MasterCode = "RUNNING", FullName = "Running", Description = "Conversation has an in-flight AI provider run" },
            new AiConversationStatusLookup { Id = (int)AiConversationStatus.Blocked, MasterCode = "BLOCKED", FullName = "Blocked", Description = "Conversation cannot accept more messages" },
            new AiConversationStatusLookup { Id = (int)AiConversationStatus.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Conversation is retained but no longer active" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSupportAccessSessionStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SupportAccessSessionStatus>().AnyAsync(ct)) return;

        context.Set<SupportAccessSessionStatus>().AddRange(
            new SupportAccessSessionStatus { Id = (int)SupportAccessSessionStatusEnum.PendingApproval, MasterCode = "PENDING_APPROVAL", FullName = "Pending approval", Description = "Session is awaiting approval before activation", IsTerminal = false },
            new SupportAccessSessionStatus { Id = (int)SupportAccessSessionStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Session is active and may be validated for support context", IsTerminal = false },
            new SupportAccessSessionStatus { Id = (int)SupportAccessSessionStatusEnum.Stopped, MasterCode = "STOPPED", FullName = "Stopped", Description = "Session was stopped by the actor", IsTerminal = true },
            new SupportAccessSessionStatus { Id = (int)SupportAccessSessionStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Session reached its configured expiry", IsTerminal = true },
            new SupportAccessSessionStatus { Id = (int)SupportAccessSessionStatusEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked", Description = "Session was force-stopped or revoked by policy", IsTerminal = true });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSupportAccessModesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SupportAccessMode>().AnyAsync(ct)) return;

        context.Set<SupportAccessMode>().AddRange(
            new SupportAccessMode { Id = (int)SupportAccessModeEnum.ReadOnly, MasterCode = "READ_ONLY", FullName = "Read only", Description = "Support session may satisfy read-only tenant actions", AllowsWrites = false },
            new SupportAccessMode { Id = (int)SupportAccessModeEnum.Write, MasterCode = "WRITE", FullName = "Write", Description = "Support session may satisfy explicitly allowed mutating tenant actions", AllowsWrites = true });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSupportAccessEndReasonsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SupportAccessEndReason>().AnyAsync(ct)) return;

        context.Set<SupportAccessEndReason>().AddRange(
            new SupportAccessEndReason { Id = (int)SupportAccessEndReasonEnum.UserStopped, MasterCode = "USER_STOPPED", FullName = "User stopped", Description = "Actor stopped the session" },
            new SupportAccessEndReason { Id = (int)SupportAccessEndReasonEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Session expired" },
            new SupportAccessEndReason { Id = (int)SupportAccessEndReasonEnum.ForceStopped, MasterCode = "FORCE_STOPPED", FullName = "Force stopped", Description = "Session was force-stopped by an authorized administrator" },
            new SupportAccessEndReason { Id = (int)SupportAccessEndReasonEnum.RevokedByPolicy, MasterCode = "REVOKED_BY_POLICY", FullName = "Revoked by policy", Description = "Session was revoked by policy or kill switch" },
            new SupportAccessEndReason { Id = (int)SupportAccessEndReasonEnum.Replaced, MasterCode = "REPLACED", FullName = "Replaced", Description = "Session was replaced by another support-access session" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSupportAccessAuditEventTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SupportAccessAuditEventType>().AnyAsync(ct)) return;

        context.Set<SupportAccessAuditEventType>().AddRange(
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.Started, MasterCode = "STARTED", FullName = "Started", Description = "Support session was started", IsLifecycleEvent = true },
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.Stopped, MasterCode = "STOPPED", FullName = "Stopped", Description = "Support session was stopped", IsLifecycleEvent = true },
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Support session expired", IsLifecycleEvent = true },
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked", Description = "Support session was revoked", IsLifecycleEvent = true },
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.Denied, MasterCode = "DENIED", FullName = "Denied", Description = "Support access was denied", IsLifecycleEvent = false },
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.RequestObserved, MasterCode = "REQUEST_OBSERVED", FullName = "Request observed", Description = "Support session observed a request", IsLifecycleEvent = false },
            new SupportAccessAuditEventType { Id = (int)SupportAccessAuditEventTypeEnum.CommandCommitted, MasterCode = "COMMAND_COMMITTED", FullName = "Command committed", Description = "Support session committed a mutating command", IsLifecycleEvent = false });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAiMessageRolesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AiMessageRoleLookup>().AnyAsync(ct)) return;

        context.Set<AiMessageRoleLookup>().AddRange(
            new AiMessageRoleLookup { Id = (int)AiMessageRole.System, MasterCode = "SYSTEM", FullName = "System", Description = "System prompt or platform-authored instruction" },
            new AiMessageRoleLookup { Id = (int)AiMessageRole.User, MasterCode = "USER", FullName = "User", Description = "User-authored assistant message" },
            new AiMessageRoleLookup { Id = (int)AiMessageRole.Assistant, MasterCode = "ASSISTANT", FullName = "Assistant", Description = "AI assistant provider response" },
            new AiMessageRoleLookup { Id = (int)AiMessageRole.Tool, MasterCode = "TOOL", FullName = "Tool", Description = "Tool execution result supplied to the assistant" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAiRunStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AiRunStatusLookup>().AnyAsync(ct)) return;

        context.Set<AiRunStatusLookup>().AddRange(
            new AiRunStatusLookup { Id = (int)AiRunStatus.Queued, MasterCode = "QUEUED", FullName = "Queued", Description = "Provider run has been queued" },
            new AiRunStatusLookup { Id = (int)AiRunStatus.InProgress, MasterCode = "IN_PROGRESS", FullName = "In progress", Description = "Provider run is executing" },
            new AiRunStatusLookup { Id = (int)AiRunStatus.Succeeded, MasterCode = "SUCCEEDED", FullName = "Succeeded", Description = "Provider run completed successfully" },
            new AiRunStatusLookup { Id = (int)AiRunStatus.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Provider run failed" },
            new AiRunStatusLookup { Id = (int)AiRunStatus.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled", Description = "Provider run was cancelled" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAiReferenceKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AiReferenceKindLookup>().AnyAsync(ct)) return;

        context.Set<AiReferenceKindLookup>().AddRange(
            new AiReferenceKindLookup { Id = (int)AiReferenceKind.Event, MasterCode = "EVENT", FullName = "Event", Description = "Conversation references an event" },
            new AiReferenceKindLookup { Id = (int)AiReferenceKind.EventSession, MasterCode = "EVENT_SESSION", FullName = "Event session", Description = "Conversation references an event session" },
            new AiReferenceKindLookup { Id = (int)AiReferenceKind.Actor, MasterCode = "ACTOR", FullName = "Actor", Description = "Conversation references an actor" },
            new AiReferenceKindLookup { Id = (int)AiReferenceKind.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Conversation references an organization" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAiProposedActionKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        var requiredLookups = new[]
        {
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventDraft, MasterCode = "CREATE_EVENT_DRAFT", FullName = "Create event draft", Description = "Create a draft event after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventDraft, MasterCode = "UPDATE_EVENT_DRAFT", FullName = "Update event draft", Description = "Propose draft event changes after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.PublishEvent, MasterCode = "PUBLISH_EVENT", FullName = "Publish event", Description = "Propose publishing an event after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEvent, MasterCode = "DELETE_EVENT", FullName = "Delete event", Description = "Propose deleting an event after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpsertEventIslamicAspect, MasterCode = "UPSERT_EVENT_ISLAMIC_ASPECT", FullName = "Upsert event Islamic aspect", Description = "Propose saving an event Islamic aspect after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventIslamicAspect, MasterCode = "DELETE_EVENT_ISLAMIC_ASPECT", FullName = "Delete event Islamic aspect", Description = "Propose deleting an event Islamic aspect after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpsertEventTechAspect, MasterCode = "UPSERT_EVENT_TECH_ASPECT", FullName = "Upsert event Tech aspect", Description = "Propose saving an event Tech aspect after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventTechAspect, MasterCode = "DELETE_EVENT_TECH_ASPECT", FullName = "Delete event Tech aspect", Description = "Propose deleting an event Tech aspect after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventSession, MasterCode = "CREATE_EVENT_SESSION", FullName = "Create event session", Description = "Propose creating an event session after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventSession, MasterCode = "UPDATE_EVENT_SESSION", FullName = "Update event session", Description = "Propose updating an event session after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventSession, MasterCode = "DELETE_EVENT_SESSION", FullName = "Delete event session", Description = "Propose deleting an event session after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventSessionGroup, MasterCode = "CREATE_EVENT_SESSION_GROUP", FullName = "Create event session group", Description = "Propose creating an event session group after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventSessionGroup, MasterCode = "UPDATE_EVENT_SESSION_GROUP", FullName = "Update event session group", Description = "Propose updating an event session group after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventSessionGroup, MasterCode = "DELETE_EVENT_SESSION_GROUP", FullName = "Delete event session group", Description = "Propose deleting an event session group after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.AssignSessionToEventSessionGroup, MasterCode = "ASSIGN_SESSION_TO_EVENT_SESSION_GROUP", FullName = "Assign session to event session group", Description = "Propose assigning a session to an event session group after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UnassignSessionFromEventSessionGroup, MasterCode = "UNASSIGN_SESSION_FROM_EVENT_SESSION_GROUP", FullName = "Unassign session from event session group", Description = "Propose unassigning a session from an event session group after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventDay, MasterCode = "CREATE_EVENT_DAY", FullName = "Create event day", Description = "Propose creating an event day after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventDay, MasterCode = "UPDATE_EVENT_DAY", FullName = "Update event day", Description = "Propose updating an event day after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventDay, MasterCode = "DELETE_EVENT_DAY", FullName = "Delete event day", Description = "Propose deleting an event day after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventAgendaItem, MasterCode = "CREATE_EVENT_AGENDA_ITEM", FullName = "Create event agenda item", Description = "Propose creating an event agenda item after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventAgendaItem, MasterCode = "UPDATE_EVENT_AGENDA_ITEM", FullName = "Update event agenda item", Description = "Propose updating an event agenda item after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventAgendaItem, MasterCode = "DELETE_EVENT_AGENDA_ITEM", FullName = "Delete event agenda item", Description = "Propose deleting an event agenda item after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventCustomPropertyDefinition, MasterCode = "CREATE_EVENT_CUSTOM_PROPERTY_DEFINITION", FullName = "Create event custom property definition", Description = "Propose creating an event custom property definition after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventCustomPropertyDefinition, MasterCode = "UPDATE_EVENT_CUSTOM_PROPERTY_DEFINITION", FullName = "Update event custom property definition", Description = "Propose updating an event custom property definition after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventCustomPropertyDefinition, MasterCode = "DELETE_EVENT_CUSTOM_PROPERTY_DEFINITION", FullName = "Delete event custom property definition", Description = "Propose deleting an event custom property definition after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.PurgeEventCustomPropertyDefinition, MasterCode = "PURGE_EVENT_CUSTOM_PROPERTY_DEFINITION", FullName = "Purge event custom property definition", Description = "Propose purging an event custom property definition after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.SetEventCustomPropertyValue, MasterCode = "SET_EVENT_CUSTOM_PROPERTY_VALUE", FullName = "Set event custom property value", Description = "Propose setting an event custom property value after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.SetEventCustomPropertyMultiValues, MasterCode = "SET_EVENT_CUSTOM_PROPERTY_MULTI_VALUES", FullName = "Set event custom property multi-values", Description = "Propose replacing event custom property multi-values after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.AssignEventTeamRole, MasterCode = "ASSIGN_EVENT_TEAM_ROLE", FullName = "Assign event team role", Description = "Propose assigning an event team role after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.RevokeEventTeamRole, MasterCode = "REVOKE_EVENT_TEAM_ROLE", FullName = "Revoke event team role", Description = "Propose revoking an event team role after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventTemplate, MasterCode = "CREATE_EVENT_TEMPLATE", FullName = "Create event template", Description = "Propose creating an event template after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventTemplate, MasterCode = "UPDATE_EVENT_TEMPLATE", FullName = "Update event template", Description = "Propose updating an event template after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventTemplate, MasterCode = "DELETE_EVENT_TEMPLATE", FullName = "Delete event template", Description = "Propose deleting an event template after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.CreateEventSessionTemplate, MasterCode = "CREATE_EVENT_SESSION_TEMPLATE", FullName = "Create event session template", Description = "Propose creating an event session template after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UpdateEventSessionTemplate, MasterCode = "UPDATE_EVENT_SESSION_TEMPLATE", FullName = "Update event session template", Description = "Propose updating an event session template after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.DeleteEventSessionTemplate, MasterCode = "DELETE_EVENT_SESSION_TEMPLATE", FullName = "Delete event session template", Description = "Propose deleting an event session template after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.ApplyEventTemplateSync, MasterCode = "APPLY_EVENT_TEMPLATE_SYNC", FullName = "Apply event template sync", Description = "Propose applying event template sync changes after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.ApplyEventSessionTemplateSync, MasterCode = "APPLY_EVENT_SESSION_TEMPLATE_SYNC", FullName = "Apply event session template sync", Description = "Propose applying event session template sync changes after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.LightModerateEvent, MasterCode = "LIGHT_MODERATE_EVENT", FullName = "Light moderate event", Description = "Propose reversible event moderation after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.HeavyModerateEvent, MasterCode = "HEAVY_MODERATE_EVENT", FullName = "Heavy moderate event", Description = "Propose irreversible event heavy moderation after human confirmation" },
            new AiProposedActionKindLookup { Id = (int)AiProposedActionKind.UnmoderateEvent, MasterCode = "UNMODERATE_EVENT", FullName = "Unmoderate event", Description = "Propose unmoderating a reversible event moderation after human confirmation" }
        };

        var existingIds = await context.Set<AiProposedActionKindLookup>()
            .Select(lookup => lookup.Id)
            .ToListAsync(ct);
        var missingLookups = requiredLookups
            .Where(lookup => !existingIds.Contains(lookup.Id))
            .ToArray();
        if (missingLookups.Length == 0) return;

        context.Set<AiProposedActionKindLookup>().AddRange(missingLookups);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAiProposedActionStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AiProposedActionStatusLookup>().AnyAsync(ct)) return;

        context.Set<AiProposedActionStatusLookup>().AddRange(
            new AiProposedActionStatusLookup { Id = (int)AiProposedActionStatus.Proposed, MasterCode = "PROPOSED", FullName = "Proposed", Description = "Action is awaiting human review" },
            new AiProposedActionStatusLookup { Id = (int)AiProposedActionStatus.Confirmed, MasterCode = "CONFIRMED", FullName = "Confirmed", Description = "Action was confirmed by a user" },
            new AiProposedActionStatusLookup { Id = (int)AiProposedActionStatus.Rejected, MasterCode = "REJECTED", FullName = "Rejected", Description = "Action was rejected by a user" },
            new AiProposedActionStatusLookup { Id = (int)AiProposedActionStatus.Executed, MasterCode = "EXECUTED", FullName = "Executed", Description = "Action side effect completed" },
            new AiProposedActionStatusLookup { Id = (int)AiProposedActionStatus.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Action side effect failed" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAiProviderKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        var requiredLookups = new[]
        {
            new AiProviderKindLookup { Id = (int)AiProviderKind.None, MasterCode = "NONE", FullName = "None", Description = "AI provider is disabled" },
            new AiProviderKindLookup { Id = (int)AiProviderKind.Fake, MasterCode = "FAKE", FullName = "Fake", Description = "Deterministic fake provider for testing" },
            new AiProviderKindLookup { Id = (int)AiProviderKind.OpenAiCompatible, MasterCode = "OPENAI_COMPATIBLE", FullName = "OpenAI Compatible", Description = "Any OpenAI-compatible API endpoint" },
            new AiProviderKindLookup { Id = (int)AiProviderKind.AnthropicCompatible, MasterCode = "ANTHROPIC_COMPATIBLE", FullName = "Anthropic Compatible", Description = "Anthropic Messages API endpoint" },
            new AiProviderKindLookup { Id = (int)AiProviderKind.OpenAi, MasterCode = "OPENAI", FullName = "OpenAI", Description = "OpenAI Responses API endpoint" },
            new AiProviderKindLookup { Id = (int)AiProviderKind.AzureOpenAi, MasterCode = "AZURE_OPENAI", FullName = "Azure OpenAI", Description = "Microsoft.Extensions.AI with Azure OpenAI" },
            new AiProviderKindLookup { Id = (int)AiProviderKind.Anthropic, MasterCode = "ANTHROPIC", FullName = "Anthropic", Description = "Anthropic Messages API endpoint at api.anthropic.com" }
        };

        var existingLookups = await context.Set<AiProviderKindLookup>().ToListAsync(ct);
        var existingById = existingLookups.ToDictionary(lookup => lookup.Id);
        var changed = false;

        foreach (var requiredLookup in requiredLookups)
        {
            if (!existingById.TryGetValue(requiredLookup.Id, out var existingLookup))
            {
                context.Set<AiProviderKindLookup>().Add(requiredLookup);
                changed = true;
                continue;
            }

            if (existingLookup.MasterCode == requiredLookup.MasterCode
                && existingLookup.FullName == requiredLookup.FullName
                && existingLookup.Description == requiredLookup.Description)
            {
                continue;
            }

            existingLookup.MasterCode = requiredLookup.MasterCode;
            existingLookup.FullName = requiredLookup.FullName;
            existingLookup.Description = requiredLookup.Description;
            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedRegistrationScopesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<RegistrationScope>().AnyAsync(ct)) return;

        context.Set<RegistrationScope>().AddRange(
            new RegistrationScope { Id = (int)RegistrationScopeEnum.Event, MasterCode = "EVENT", FullName = "Whole event", Description = "User registered for the entire event" },
            new RegistrationScope { Id = (int)RegistrationScopeEnum.Day, MasterCode = "DAY", FullName = "Event day", Description = "User registered for a single event day" },
            new RegistrationScope { Id = (int)RegistrationScopeEnum.SessionSelection, MasterCode = "SESSION_SELECTION", FullName = "Session selection", Description = "User registered for a chosen set of sessions" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventRegistrationPoliciesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<EventRegistrationPolicy>().AnyAsync(ct)) return;

        context.Set<EventRegistrationPolicy>().AddRange(
            new EventRegistrationPolicy { Id = (int)EventRegistrationPolicyEnum.WholeEventOnly, MasterCode = "WHOLE_EVENT_ONLY", FullName = "Whole event only", Description = "Only whole-event registration is accepted" },
            new EventRegistrationPolicy { Id = (int)EventRegistrationPolicyEnum.WholeDayOnly, MasterCode = "WHOLE_DAY_ONLY", FullName = "Whole day only", Description = "Only whole-day registration is accepted" },
            new EventRegistrationPolicy { Id = (int)EventRegistrationPolicyEnum.SessionSelectionOnly, MasterCode = "SESSION_SELECTION_ONLY", FullName = "Session selection only", Description = "Only per-session selection is accepted" },
            new EventRegistrationPolicy { Id = (int)EventRegistrationPolicyEnum.WholeEventOrDay, MasterCode = "WHOLE_EVENT_OR_DAY", FullName = "Whole event or day", Description = "Whole-event and whole-day registrations are accepted" },
            new EventRegistrationPolicy { Id = (int)EventRegistrationPolicyEnum.WholeEventOrSession, MasterCode = "WHOLE_EVENT_OR_SESSION", FullName = "Whole event or session", Description = "Whole-event and per-session registrations are accepted" },
            new EventRegistrationPolicy { Id = (int)EventRegistrationPolicyEnum.Flexible, MasterCode = "FLEXIBLE", FullName = "Flexible", Description = "All registration scopes are accepted" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedScheduleItemKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ScheduleItemKind>().AnyAsync(ct)) return;

        context.Set<ScheduleItemKind>().AddRange(
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Intro, MasterCode = "INTRO", FullName = "Intro", Description = "Opening remarks or welcome block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Talk, MasterCode = "TALK", FullName = "Talk", Description = "Main speaker content block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.QAndA, MasterCode = "Q_AND_A", FullName = "Q&A", Description = "Audience questions and answers block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Break, MasterCode = "BREAK", FullName = "Break", Description = "Refreshment or rest block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Prayer, MasterCode = "PRAYER", FullName = "Prayer", Description = "Scheduled prayer block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Outro, MasterCode = "OUTRO", FullName = "Outro", Description = "Closing remarks or farewell block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Logistics, MasterCode = "LOGISTICS", FullName = "Logistics", Description = "Registration, seating, or housekeeping block" },
            new ScheduleItemKind { Id = (int)ScheduleItemKindEnum.Custom, MasterCode = "CUSTOM", FullName = "Custom", Description = "Tenant-defined block not covered by standard kinds" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventSessionKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<EventSessionKind>().AnyAsync(ct)) return;

        context.Set<EventSessionKind>().AddRange(
            new EventSessionKind { Id = (int)EventSessionKindEnum.Talk, MasterCode = "TALK", FullName = "Talk", Description = "A standard presentation or talk" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Workshop, MasterCode = "WORKSHOP", FullName = "Workshop", Description = "An interactive hands-on session" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Panel, MasterCode = "PANEL", FullName = "Panel", Description = "A moderated discussion with multiple panelists" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Lecture, MasterCode = "LECTURE", FullName = "Lecture", Description = "A formal instructional presentation" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Class, MasterCode = "CLASS", FullName = "Class", Description = "A structured learning session" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Activity, MasterCode = "ACTIVITY", FullName = "Activity", Description = "An activity or participatory program item" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Keynote, MasterCode = "KEYNOTE", FullName = "Keynote", Description = "A featured keynote session" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.LightningTalk, MasterCode = "LIGHTNING_TALK", FullName = "Lightning talk", Description = "A short, focused presentation" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.BOF, MasterCode = "BOF", FullName = "Birds of a feather", Description = "An informal discussion around a shared topic" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Demo, MasterCode = "DEMO", FullName = "Demo", Description = "A demonstration or showcase" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.QAndA, MasterCode = "Q_AND_A", FullName = "Q&A", Description = "A question-and-answer session" },
            new EventSessionKind { Id = (int)EventSessionKindEnum.Other, MasterCode = "OTHER", FullName = "Other", Description = "A program item not covered by standard kinds" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedActorTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        ActorType[] actorTypes =
        [
            new ActorType { Id = (int)ActorTypeEnum.User, MasterCode = "USER", FullName = "User", Description = "Individual user actor" },
            new ActorType { Id = (int)ActorTypeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Organization actor" },
            new ActorType { Id = (int)ActorTypeEnum.Bot, MasterCode = "BOT", FullName = "Bot", Description = "Automated bot actor" },
            new ActorType { Id = (int)ActorTypeEnum.Group, MasterCode = "GROUP", FullName = "Group", Description = "Group actor" },
            new ActorType { Id = (int)ActorTypeEnum.System, MasterCode = "SYSTEM", FullName = "System", Description = "System actor" },
            new ActorType { Id = (int)ActorTypeEnum.ExternalUnclassified, MasterCode = "EXTERNAL_UNCLASSIFIED", FullName = "External unclassified", Description = "Verified external subject awaiting explicit classification" }
        ];

        var existingIds = await context.Set<ActorType>()
            .Select(actorType => actorType.Id)
            .ToHashSetAsync(ct);
        var missingActorTypes = actorTypes
            .Where(actorType => !existingIds.Contains(actorType.Id))
            .ToArray();
        if (missingActorTypes.Length == 0) return;

        context.Set<ActorType>().AddRange(missingActorTypes);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedActorSubscriptionStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ActorSubscriptionStatus>().AnyAsync(ct)) return;

        context.Set<ActorSubscriptionStatus>().AddRange(
            new ActorSubscriptionStatus { Id = (int)ActorSubscriptionStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Subscriber receives fanout notifications for the target actor" },
            new ActorSubscriptionStatus { Id = (int)ActorSubscriptionStatusEnum.Unsubscribed, MasterCode = "UNSUBSCRIBED", FullName = "Unsubscribed", Description = "Subscriber explicitly opted out while preserving history" },
            new ActorSubscriptionStatus { Id = (int)ActorSubscriptionStatusEnum.Blocked, MasterCode = "BLOCKED", FullName = "Blocked", Description = "Subscription is administratively blocked" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedActorSubscriptionNotificationLevelsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ActorSubscriptionNotificationLevel>().AnyAsync(ct)) return;

        context.Set<ActorSubscriptionNotificationLevel>().AddRange(
            new ActorSubscriptionNotificationLevel { Id = (int)ActorSubscriptionNotificationLevelEnum.None, MasterCode = "NONE", FullName = "None", Description = "No notifications are generated for this subscription" },
            new ActorSubscriptionNotificationLevel { Id = (int)ActorSubscriptionNotificationLevelEnum.All, MasterCode = "ALL", FullName = "All", Description = "All V1 fanout notifications are generated for this subscription" },
            new ActorSubscriptionNotificationLevel { Id = (int)ActorSubscriptionNotificationLevelEnum.Personalized, MasterCode = "PERSONALIZED", FullName = "Personalized", Description = "Future personalized fanout policy placeholder" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedRoleScopesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<RoleScope>().AnyAsync(ct)) return;

        context.Set<RoleScope>().AddRange(
            new RoleScope { Id = (int)RoleScopeEnum.Platform, MasterCode = "PLATFORM", FullName = "Platform", Description = "Platform-wide roles and permissions" },
            new RoleScope { Id = (int)RoleScopeEnum.Tenant, MasterCode = "TENANT", FullName = "Tenant", Description = "Tenant-scoped roles and permissions" },
            new RoleScope { Id = (int)RoleScopeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Organization-scoped roles and permissions" },
            new RoleScope { Id = (int)RoleScopeEnum.Group, MasterCode = "GROUP", FullName = "Group", Description = "Group-scoped roles and permissions" },
            new RoleScope { Id = (int)RoleScopeEnum.Event, MasterCode = "EVENT", FullName = "Event", Description = "Event-scoped roles and permissions" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSettingScopesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SettingScopeLookup>().AnyAsync(ct)) return;

        context.Set<SettingScopeLookup>().AddRange(
            new SettingScopeLookup { Id = (int)ConfigurationScopeEnum.System, MasterCode = "SYSTEM", FullName = "System", Description = "Global system configuration scope" },
            new SettingScopeLookup { Id = (int)ConfigurationScopeEnum.Instance, MasterCode = "INSTANCE", FullName = "Instance", Description = "Application instance configuration scope" },
            new SettingScopeLookup { Id = (int)ConfigurationScopeEnum.Tenant, MasterCode = "TENANT", FullName = "Tenant", Description = "Tenant configuration scope" },
            new SettingScopeLookup { Id = (int)ConfigurationScopeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Organization configuration scope" },
            new SettingScopeLookup { Id = (int)ConfigurationScopeEnum.Group, MasterCode = "GROUP", FullName = "Group", Description = "Group configuration scope" },
            new SettingScopeLookup { Id = (int)ConfigurationScopeEnum.User, MasterCode = "USER", FullName = "User", Description = "User configuration scope" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSettingValueTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        var expected = new[]
        {
            new SettingValueTypeLookup { Id = (int)SettingValueType.String, MasterCode = "STRING", FullName = "String", Description = "String setting value" },
            new SettingValueTypeLookup { Id = (int)SettingValueType.Integer, MasterCode = "INTEGER", FullName = "Integer", Description = "Integer setting value" },
            new SettingValueTypeLookup { Id = (int)SettingValueType.Boolean, MasterCode = "BOOLEAN", FullName = "Boolean", Description = "Boolean setting value" },
            new SettingValueTypeLookup { Id = (int)SettingValueType.Decimal, MasterCode = "DECIMAL", FullName = "Decimal", Description = "Decimal setting value" },
            new SettingValueTypeLookup { Id = (int)SettingValueType.Json, MasterCode = "JSON", FullName = "JSON", Description = "JSON setting value" },
            new SettingValueTypeLookup { Id = (int)SettingValueType.DateTime, MasterCode = "DATE_TIME", FullName = "Date/Time", Description = "Date/time setting value" },
            new SettingValueTypeLookup { Id = (int)SettingValueType.Long, MasterCode = "LONG", FullName = "Long Integer", Description = "64-bit integer setting value" }
        };

        var existingIds = await context.Set<SettingValueTypeLookup>()
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(ct);
        var existingIdSet = existingIds.ToHashSet();
        var missing = expected.Where(x => !existingIdSet.Contains(x.Id)).ToList();

        if (missing.Count == 0) return;

        context.Set<SettingValueTypeLookup>().AddRange(missing);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSecretSourceTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SecretSourceTypeLookup>().AnyAsync(ct)) return;

        context.Set<SecretSourceTypeLookup>().AddRange(
            new SecretSourceTypeLookup { Id = (int)SecretSourceType.Infisical, MasterCode = "INFISICAL", FullName = "Infisical", Description = "Secret value is stored in Infisical" },
            new SecretSourceTypeLookup { Id = (int)SecretSourceType.EnvironmentVariable, MasterCode = "ENVIRONMENT_VARIABLE", FullName = "Environment Variable", Description = "Secret value is resolved from an environment variable" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedSecretValidationStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<SecretValidationStatus>().AnyAsync(ct)) return;

        context.Set<SecretValidationStatus>().AddRange(
            new SecretValidationStatus { Id = (int)SecretValidationResult.NotValidated, MasterCode = "NOT_VALIDATED", FullName = "Not Validated", Description = "Secret source has not been validated" },
            new SecretValidationStatus { Id = (int)SecretValidationResult.Success, MasterCode = "SUCCESS", FullName = "Success", Description = "Secret source validation succeeded" },
            new SecretValidationStatus { Id = (int)SecretValidationResult.Failure, MasterCode = "FAILURE", FullName = "Failure", Description = "Secret source validation failed" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedExternalApiKeyOwnerTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ExternalApiKeyOwnerTypeLookup>().AnyAsync(ct)) return;

        context.Set<ExternalApiKeyOwnerTypeLookup>().AddRange(
            new ExternalApiKeyOwnerTypeLookup { Id = (int)ExternalApiKeyOwnerType.User, MasterCode = "USER", FullName = "User", Description = "External API key owned by a user" },
            new ExternalApiKeyOwnerTypeLookup { Id = (int)ExternalApiKeyOwnerType.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "External API key owned by an organization" },
            new ExternalApiKeyOwnerTypeLookup { Id = (int)ExternalApiKeyOwnerType.Group, MasterCode = "GROUP", FullName = "Group", Description = "External API key owned by a group" },
            new ExternalApiKeyOwnerTypeLookup { Id = (int)ExternalApiKeyOwnerType.Tenant, MasterCode = "TENANT", FullName = "Tenant", Description = "External API key owned by a tenant" },
            new ExternalApiKeyOwnerTypeLookup { Id = (int)ExternalApiKeyOwnerType.InstanceAdmin, MasterCode = "INSTANCE_ADMIN", FullName = "Instance Admin", Description = "External API key owned by an instance administrator" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedNotificationScopeTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<NotificationScopeType>().AnyAsync(ct)) return;

        context.Set<NotificationScopeType>().AddRange(
            new NotificationScopeType { Id = (int)ActorTypeEnum.User, MasterCode = "USER", FullName = "User", Description = "Notification targets a single user" },
            new NotificationScopeType { Id = (int)ActorTypeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Notification targets an organization scope" },
            new NotificationScopeType { Id = (int)ActorTypeEnum.Group, MasterCode = "GROUP", FullName = "Group", Description = "Notification targets a group scope" },
            new NotificationScopeType { Id = (int)ActorTypeEnum.System, MasterCode = "SYSTEM", FullName = "System", Description = "Notification targets a system scope" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedNotificationCategoriesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new NotificationCategory[]
            {
                new() { Id = (int)NotificationCategoryEnum.IdentityLifecycle, MasterCode = "IDENTITY_LIFECYCLE", FullName = "Identity lifecycle", Description = "Credential-token lifecycle notifications owned by an account authority" },
                new() { Id = (int)NotificationCategoryEnum.ProductLifecycle, MasterCode = "PRODUCT_LIFECYCLE", FullName = "Product lifecycle", Description = "ISLAMU product-domain lifecycle notifications" },
                new() { Id = (int)NotificationCategoryEnum.EventLifecycle, MasterCode = "EVENT_LIFECYCLE", FullName = "Event lifecycle", Description = "Event publication, reminder, cancellation, and lifecycle notifications" },
                new() { Id = (int)NotificationCategoryEnum.RegistrationLifecycle, MasterCode = "REGISTRATION_LIFECYCLE", FullName = "Registration lifecycle", Description = "Registration, waitlist, approval, and rejection notifications" },
                new() { Id = (int)NotificationCategoryEnum.TrustSafetyReporting, MasterCode = "TRUST_SAFETY_REPORTING", FullName = "Trust and safety reporting", Description = "Report intake and reporter-facing trust and safety notifications" },
                new() { Id = (int)NotificationCategoryEnum.TrustSafetyModeration, MasterCode = "TRUST_SAFETY_MODERATION", FullName = "Trust and safety moderation", Description = "Moderation decision and safety enforcement notifications" },
                new() { Id = (int)NotificationCategoryEnum.ProviderInternal, MasterCode = "PROVIDER_INTERNAL", FullName = "Provider internal", Description = "External provider console or workflow notifications not sent as ISLAMU user-facing email" },
                new() { Id = (int)NotificationCategoryEnum.PlatformOperations, MasterCode = "PLATFORM_OPERATIONS", FullName = "Platform operations", Description = "Platform operational notices and operator-visible lifecycle notifications" },
                new() { Id = (int)NotificationCategoryEnum.Marketing, MasterCode = "MARKETING", FullName = "Marketing", Description = "Consent-controlled product marketing notifications" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedNotificationPreferenceCategoriesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new NotificationPreferenceCategory[]
            {
                new() { Id = (int)NotificationPreferenceCategoryEnum.AccountSecurity, MasterCode = NotificationPreferenceCategoryCodes.AccountSecurity, FullName = "Account security", Description = "Credential, login, and account safety notifications", IsRequired = true, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 10 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.BillingLegal, MasterCode = NotificationPreferenceCategoryCodes.BillingLegal, FullName = "Billing and legal", Description = "Billing, receipt, compliance, and legal notices", IsRequired = true, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 20 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.RegistrationStatus, MasterCode = NotificationPreferenceCategoryCodes.RegistrationStatus, FullName = "Registration status", Description = "Registration, waitlist, approval, cancellation, and attendee status changes", IsRequired = false, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 30 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.EventUpdates, MasterCode = NotificationPreferenceCategoryCodes.EventUpdates, FullName = "Event updates", Description = "Event reminders, updates, cancellations, and organizer announcements", IsRequired = false, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 40 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.OrganizationUpdates, MasterCode = NotificationPreferenceCategoryCodes.OrganizationUpdates, FullName = "Organization updates", Description = "Organization-level announcements and membership updates", IsRequired = false, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 50 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.GroupUpdates, MasterCode = NotificationPreferenceCategoryCodes.GroupUpdates, FullName = "Group updates", Description = "Group-level announcements and membership updates", IsRequired = false, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 60 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.TrustSafety, MasterCode = NotificationPreferenceCategoryCodes.TrustSafety, FullName = "Trust and safety", Description = "Report, moderation, safety, and enforcement notices", IsRequired = false, DefaultEmailEnabled = true, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 70 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.ProductAnnouncements, MasterCode = NotificationPreferenceCategoryCodes.ProductAnnouncements, FullName = "Product announcements", Description = "Platform feature announcements and product education", IsRequired = false, DefaultEmailEnabled = false, DefaultInAppEnabled = true, DefaultPushEnabled = true, SortOrder = 80 },
                new() { Id = (int)NotificationPreferenceCategoryEnum.Marketing, MasterCode = NotificationPreferenceCategoryCodes.Marketing, FullName = "Marketing", Description = "Consent-controlled marketing and promotional communication", IsRequired = false, DefaultEmailEnabled = false, DefaultInAppEnabled = false, DefaultPushEnabled = false, SortOrder = 90 }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedNotificationPreferenceChannelsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await RepairCanonicalLookupRowsAsync(
            context,
            new NotificationPreferenceChannel[]
            {
                new() { Id = (int)NotificationPreferenceChannelEnum.Email, MasterCode = NotificationPreferenceChannelCodes.Email, FullName = "Email", Description = "Email delivery through ISLAMU Event email dispatch infrastructure", SortOrder = 10 },
                new() { Id = (int)NotificationPreferenceChannelEnum.InApp, MasterCode = NotificationPreferenceChannelCodes.InApp, FullName = "In-App", Description = "Durable in-app notification rows surfaced by the notification inbox", SortOrder = 20 },
                new() { Id = (int)NotificationPreferenceChannelEnum.Push, MasterCode = NotificationPreferenceChannelCodes.Push, FullName = "Browser Push", Description = "Browser Web Push delivery through a user-owned subscription", SortOrder = 30 }
            },
            row => row.Id,
            static (existing, canonical) =>
            {
                existing.MasterCode = canonical.MasterCode;
                existing.FullName = canonical.FullName;
                existing.Description = canonical.Description;
                existing.SortOrder = canonical.SortOrder;
            },
            ct);
    }

    private static async Task SeedNotificationOwnershipTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new NotificationOwnershipType[]
            {
                new() { Id = (int)NotificationOwnershipTypeEnum.IslamuEvent, MasterCode = "ISLAMU_EVENT", FullName = "ISLAMU Event", Description = "ISLAMU Event owns the notification decision, delivery audit, and retry lifecycle" },
                new() { Id = (int)NotificationOwnershipTypeEnum.AccountAuthority, MasterCode = "ACCOUNT_AUTHORITY", FullName = "Account authority", Description = "The credential-token owner creates and verifies the lifecycle email" },
                new() { Id = (int)NotificationOwnershipTypeEnum.ExternalWorkflowProvider, MasterCode = "EXTERNAL_WORKFLOW_PROVIDER", FullName = "External workflow provider", Description = "An external workflow provider owns its internal notification workflow" },
                new() { Id = (int)NotificationOwnershipTypeEnum.Disabled, MasterCode = "DISABLED", FullName = "Disabled", Description = "The notification is intentionally disabled for this category and route" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedNotificationIntentStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new NotificationIntentStatus[]
            {
                new() { Id = (int)NotificationIntentStatusEnum.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Intent has been recorded but ownership resolution or dispatch work has not completed" },
                new() { Id = (int)NotificationIntentStatusEnum.Resolved, MasterCode = "RESOLVED", FullName = "Resolved", Description = "Intent ownership was resolved without queueing local delivery" },
                new() { Id = (int)NotificationIntentStatusEnum.DispatchQueued, MasterCode = "DISPATCH_QUEUED", FullName = "Dispatch queued", Description = "Intent has queued a local ISLAMU delivery record" },
                new() { Id = (int)NotificationIntentStatusEnum.Delegated, MasterCode = "DELEGATED", FullName = "Delegated", Description = "Intent has been delegated to an account authority or external workflow provider" },
                new() { Id = (int)NotificationIntentStatusEnum.Skipped, MasterCode = "SKIPPED", FullName = "Skipped", Description = "Intent was safely skipped by policy or preferences" },
                new() { Id = (int)NotificationIntentStatusEnum.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Intent processing failed and requires retry or operator review" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedNotificationRecipientKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new NotificationRecipientKind[]
            {
                new() { Id = (int)NotificationRecipientKindEnum.User, MasterCode = "USER", FullName = "User", Description = "A platform user recipient" },
                new() { Id = (int)NotificationRecipientKindEnum.TenantAdmin, MasterCode = "TENANT_ADMIN", FullName = "Tenant administrator", Description = "A tenant administrator recipient" },
                new() { Id = (int)NotificationRecipientKindEnum.Organizer, MasterCode = "ORGANIZER", FullName = "Organizer", Description = "An event organizer recipient" },
                new() { Id = (int)NotificationRecipientKindEnum.Reporter, MasterCode = "REPORTER", FullName = "Reporter", Description = "A trust and safety report submitter" },
                new() { Id = (int)NotificationRecipientKindEnum.Moderator, MasterCode = "MODERATOR", FullName = "Moderator", Description = "A trust and safety moderator recipient" },
                new() { Id = (int)NotificationRecipientKindEnum.ProviderOperator, MasterCode = "PROVIDER_OPERATOR", FullName = "Provider operator", Description = "An external provider console or workflow operator" },
                new() { Id = (int)NotificationRecipientKindEnum.System, MasterCode = "SYSTEM", FullName = "System", Description = "A system or operational recipient" },
                new() { Id = (int)NotificationRecipientKindEnum.Other, MasterCode = "OTHER", FullName = "Other", Description = "A recipient kind not otherwise classified" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedNotificationDeliveryStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await RepairCanonicalLookupRowsAsync(
            context,
            new NotificationDeliveryStatus[]
            {
                new() { Id = (int)NotificationDeliveryStatusEnum.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Delivery audit row is pending dispatch linkage" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Queued, MasterCode = "QUEUED", FullName = "Queued", Description = "Delivery has durable channel work queued" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Delivered, MasterCode = "DELIVERED", FullName = "Delivered", Description = "Delivery completed successfully" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Skipped, MasterCode = "SKIPPED", FullName = "Skipped", Description = "Delivery was skipped by policy or preference" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Delivery failed and may be retried or reviewed" },
                new() { Id = (int)NotificationDeliveryStatusEnum.DeadLettered, MasterCode = "DEAD_LETTERED", FullName = "Dead lettered", Description = "Delivery exhausted retry policy and is retained for operator review" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Unknown, MasterCode = "UNKNOWN", FullName = "Unknown", Description = "Provider acceptance is uncertain and automatic retry is disabled" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Parked, MasterCode = "PARKED", FullName = "Parked", Description = "Operator parked delivery pending review" },
                new() { Id = (int)NotificationDeliveryStatusEnum.Superseded, MasterCode = "SUPERSEDED", FullName = "Superseded", Description = "Newer authoritative work replaced this unsent delivery" }
            },
            row => row.Id,
            static (existing, canonical) =>
            {
                existing.MasterCode = canonical.MasterCode;
                existing.FullName = canonical.FullName;
                existing.Description = canonical.Description;
            },
            ct);
    }

    private static async Task SeedNotificationDeliveryPoliciesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await RepairCanonicalLookupRowsAsync(
            context,
            new NotificationDeliveryPolicy[]
            {
                new() { Id = (int)NotificationDeliveryPolicyEnum.RegistrationStatusOptional, MasterCode = "REGISTRATION_STATUS_OPTIONAL", FullName = "Registration status optional", Description = "Required in-app registration status with optional email" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.CriticalEventUpdateOptional, MasterCode = "CRITICAL_EVENT_UPDATE_OPTIONAL", FullName = "Critical event update optional", Description = "Required in-app event update with optional email" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.ReportCaseUpdate, MasterCode = "REPORT_CASE_UPDATE", FullName = "Report case update", Description = "Reporter case update gated by case-update consent" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.ReportFollowUpContact, MasterCode = "REPORT_FOLLOW_UP_CONTACT", FullName = "Report follow-up contact", Description = "Reporter clarification request gated by follow-up consent" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.ModerationAvailabilityRequired, MasterCode = "MODERATION_AVAILABILITY_REQUIRED", FullName = "Moderation availability required", Description = "Required operational availability and safety notice" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.ModerationContextOptional, MasterCode = "MODERATION_CONTEXT_OPTIONAL", FullName = "Moderation context optional", Description = "Optional contextual moderation notice" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.ReminderOptional, MasterCode = "REMINDER_OPTIONAL", FullName = "Reminder optional", Description = "Optional reminder delivery" },
                new() { Id = (int)NotificationDeliveryPolicyEnum.TenantAdministrationRequired, MasterCode = "TENANT_ADMINISTRATION_REQUIRED", FullName = "Tenant administration required", Description = "Required tenant administration notification" }
            },
            row => row.Id,
            static (existing, canonical) =>
            {
                existing.MasterCode = canonical.MasterCode;
                existing.FullName = canonical.FullName;
                existing.Description = canonical.Description;
            },
            ct);
    }

    private static async Task SeedNotificationExternalDelegationStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new NotificationExternalDelegationStatus[]
            {
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Delegation is recorded but not requested" },
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Requested, MasterCode = "REQUESTED", FullName = "Requested", Description = "Delegation was requested from the provider" },
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Accepted, MasterCode = "ACCEPTED", FullName = "Accepted", Description = "Provider accepted responsibility for the delegated notification" },
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Delivered, MasterCode = "DELIVERED", FullName = "Delivered", Description = "Provider reported successful delivery" },
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Provider reported failure or the delegated request failed" },
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Rejected, MasterCode = "REJECTED", FullName = "Rejected", Description = "Provider rejected the delegated request" },
                new() { Id = (int)NotificationExternalDelegationStatusEnum.Unknown, MasterCode = "UNKNOWN", FullName = "Unknown", Description = "Provider delivery state is unknown" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedExternalWorkflowProviderKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new ExternalWorkflowProviderKindLookup[]
            {
                new() { Id = (int)ExternalWorkflowProviderKindEnum.None, MasterCode = "NONE", FullName = "None", Description = "No external workflow provider is assigned" },
                new() { Id = (int)ExternalWorkflowProviderKindEnum.Coop, MasterCode = "COOP", FullName = "Coop", Description = "Coop moderation or review workflow provider" },
                new() { Id = (int)ExternalWorkflowProviderKindEnum.Osprey, MasterCode = "OSPREY", FullName = "Osprey", Description = "Osprey safety workflow provider" },
                new() { Id = (int)ExternalWorkflowProviderKindEnum.TicketingProvider, MasterCode = "TICKETING_PROVIDER", FullName = "Ticketing provider", Description = "External ticketing or payment workflow provider" },
                new() { Id = (int)ExternalWorkflowProviderKindEnum.WebhookProvider, MasterCode = "WEBHOOK_PROVIDER", FullName = "Webhook provider", Description = "External webhook workflow provider" },
                new() { Id = (int)ExternalWorkflowProviderKindEnum.Other, MasterCode = "OTHER", FullName = "Other", Description = "External workflow provider not otherwise classified" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedAccountAuthorityKindsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new AccountAuthorityKindLookup[]
            {
                new() { Id = (int)AccountAuthorityKindEnum.Keycloak, MasterCode = "KEYCLOAK", FullName = "Keycloak", Description = "Keycloak account authority lifecycle email owner" },
                new() { Id = (int)AccountAuthorityKindEnum.AtprotoPds, MasterCode = "ATPROTO_PDS", FullName = "ATProto PDS", Description = "AT Protocol personal data server account authority" },
                new() { Id = (int)AccountAuthorityKindEnum.IslamuOperatedPds, MasterCode = "ISLAMU_OPERATED_PDS", FullName = "ISLAMU-operated PDS", Description = "ISLAMU-hosted PDS cell acting as account authority" },
                new() { Id = (int)AccountAuthorityKindEnum.LocalIdentity, MasterCode = "LOCAL_IDENTITY", FullName = "Local identity", Description = "Future local ISLAMU account authority" },
                new() { Id = (int)AccountAuthorityKindEnum.ExternalOidc, MasterCode = "EXTERNAL_OIDC", FullName = "External OIDC", Description = "External OIDC account authority" }
            },
            row => row.Id,
            ct);
    }

    private static async Task SeedMissingLookupRowsAsync<TLookup>(
        ExploreDbContext context,
        IReadOnlyCollection<TLookup> requiredRows,
        Func<TLookup, int> idSelector,
        CancellationToken ct)
        where TLookup : class
    {
        var existingIds = await context.Set<TLookup>()
            .Select(row => EF.Property<int>(row, "Id"))
            .ToListAsync(ct);
        var existingIdSet = existingIds.ToHashSet();
        var missingRows = requiredRows
            .Where(row => !existingIdSet.Contains(idSelector(row)))
            .ToArray();

        if (missingRows.Length == 0) return;

        context.Set<TLookup>().AddRange(missingRows);
        await context.SaveChangesAsync(ct);
    }

    private static async Task RepairCanonicalLookupRowsAsync<TLookup>(
        ExploreDbContext context,
        IReadOnlyCollection<TLookup> canonicalRows,
        Func<TLookup, int> idSelector,
        Action<TLookup, TLookup> repair,
        CancellationToken ct)
        where TLookup : class
    {
        var existingRows = await context.Set<TLookup>().ToListAsync(ct);
        var existingById = existingRows.ToDictionary(idSelector);

        foreach (TLookup canonical in canonicalRows)
        {
            int id = idSelector(canonical);
            if (existingById.TryGetValue(id, out TLookup? existing))
            {
                repair(existing, canonical);
            }
            else
            {
                context.Set<TLookup>().Add(canonical);
            }
        }

        await context.SaveChangesAsync(ct);
    }

    internal static async Task SeedApprovalStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        var existingIds = await context.Set<ApprovalStatus>()
            .Select(status => status.Id)
            .ToListAsync(ct);
        var missingStatuses = new[]
        {
            new ApprovalStatus { Id = (int)ApprovalStatusEnum.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Status is pending approval of Admin verifying the Existence of Legal Entity" },
            new ApprovalStatus { Id = (int)ApprovalStatusEnum.Approved, MasterCode = "APPROVED", FullName = "Approved", Description = "Status has been approved by Admin after verifying the Existence of Legal Entity" },
            new ApprovalStatus { Id = (int)ApprovalStatusEnum.Rejected, MasterCode = "REJECTED", FullName = "Rejected", Description = "Status has been rejected by Admin after failing to verify the Existence of Legal Entity" },
            new ApprovalStatus { Id = (int)ApprovalStatusEnum.Waitlisted, MasterCode = "WAITLISTED", FullName = "Waitlisted", Description = "Registration is waitlisted because the event session is currently at capacity" },
            new ApprovalStatus { Id = (int)ApprovalStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled", Description = "Registration was cancelled by the attendee and is no longer live" },
            new ApprovalStatus { Id = (int)ApprovalStatusEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked", Description = "Registration authority was administratively revoked and cannot be restored" }
        }.Where(status => !existingIds.Contains(status.Id));

        context.Set<ApprovalStatus>().AddRange(missingStatuses);
        await context.SaveChangesAsync(ct);
    }

    internal static async Task SeedAuthenticationProvidersAsync(
        ExploreDbContext context,
        CancellationToken cancellationToken)
    {
        await AddMissingLookupRowsAsync(context.AuthenticationProviders,
        [
            new()
            {
                Id = (int)AuthenticationProviderKind.Keycloak,
                MasterCode = "KEYCLOAK",
                FullName = "Keycloak",
                Description = "External OpenID Connect identity authority",
            },
            new()
            {
                Id = (int)AuthenticationProviderKind.Atproto,
                MasterCode = "ATPROTO",
                FullName = "AT Protocol",
                Description = "Decentralized AT Protocol identity authority",
            },
            new()
            {
                Id = (int)AuthenticationProviderKind.Google,
                MasterCode = "GOOGLE",
                FullName = "Google",
                Description = "Google OpenID Connect identity authority",
            },
            new()
            {
                Id = (int)AuthenticationProviderKind.Local,
                MasterCode = "LOCAL",
                FullName = "Local Identity",
                Description = "Embedded ASP.NET Core Identity authority",
            },
            new()
            {
                Id = (int)AuthenticationProviderKind.Development,
                MasterCode = "DEVELOPMENT",
                FullName = "Development",
                Description = "Development-only bootstrap identity authority",
            },
        ], cancellationToken);

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task SeedAnalyticsProvidersAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AnalyticsProvider>().AnyAsync(ct)) return;

        context.Set<AnalyticsProvider>().AddRange(
            new AnalyticsProvider { Id = (int)AnalyticsProviderEnum.None, MasterCode = "NONE", FullName = "None", Description = "Analytics disabled" },
            new AnalyticsProvider { Id = (int)AnalyticsProviderEnum.Posthog, MasterCode = "POSTHOG", FullName = "PostHog", Description = "PostHog analytics provider" },
            new AnalyticsProvider { Id = (int)AnalyticsProviderEnum.Plausible, MasterCode = "PLAUSIBLE", FullName = "Plausible", Description = "Plausible analytics provider" },
            new AnalyticsProvider { Id = (int)AnalyticsProviderEnum.Rybbit, MasterCode = "RYBBIT", FullName = "Rybbit", Description = "Rybbit analytics provider" },
            new AnalyticsProvider { Id = (int)AnalyticsProviderEnum.RudderStack, MasterCode = "RUDDERSTACK", FullName = "RudderStack", Description = "RudderStack analytics provider" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedTenantStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<TenantStatus>().AnyAsync(ct)) return;

        context.Set<TenantStatus>().AddRange(
            new TenantStatus { Id = (int)TenantStatusEnum.Provisioning, MasterCode = "PROVISIONING", FullName = "Provisioning", Description = "Tenant is being set up", IsActiveState = false },
            new TenantStatus { Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Tenant is active and operational", IsActiveState = true },
            new TenantStatus { Id = (int)TenantStatusEnum.Suspended, MasterCode = "SUSPENDED", FullName = "Suspended", Description = "Tenant is temporarily suspended", IsActiveState = false },
            new TenantStatus { Id = (int)TenantStatusEnum.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Tenant is archived and read-only", IsActiveState = false },
            new TenantStatus { Id = (int)TenantStatusEnum.Purged, MasterCode = "PURGED", FullName = "Purged", Description = "Tenant data has been permanently removed", IsActiveState = false });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedTenantPlanStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<TenantPlanStatus>().AnyAsync(ct)) return;

        context.Set<TenantPlanStatus>().AddRange(
            new TenantPlanStatus { Id = (int)TenantPlanStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft", Description = "Plan version is editable and unavailable for provisioning", AllowsProvisioning = false },
            new TenantPlanStatus { Id = (int)TenantPlanStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published", Description = "Plan version is published and may be used for tenant provisioning", AllowsProvisioning = true },
            new TenantPlanStatus { Id = (int)TenantPlanStatusEnum.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Plan version is retained for audit but unavailable for new provisioning", AllowsProvisioning = false });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedTenantPlanAssignmentStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<TenantPlanAssignmentStatus>().AnyAsync(ct)) return;

        context.Set<TenantPlanAssignmentStatus>().AddRange(
            new TenantPlanAssignmentStatus { Id = (int)TenantPlanAssignmentStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Tenant is currently assigned to this plan version", IsActiveAssignment = true },
            new TenantPlanAssignmentStatus { Id = (int)TenantPlanAssignmentStatusEnum.Superseded, MasterCode = "SUPERSEDED", FullName = "Superseded", Description = "Assignment was replaced by a newer plan version", IsActiveAssignment = false },
            new TenantPlanAssignmentStatus { Id = (int)TenantPlanAssignmentStatusEnum.RolledBack, MasterCode = "ROLLED_BACK", FullName = "Rolled back", Description = "Assignment was rolled back to a previous plan version", IsActiveAssignment = false });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedTenantPlanApplicationStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<TenantPlanApplicationStatus>().AnyAsync(ct)) return;

        context.Set<TenantPlanApplicationStatus>().AddRange(
            new TenantPlanApplicationStatus { Id = (int)TenantPlanApplicationStatusEnum.Succeeded, MasterCode = "SUCCEEDED", FullName = "Succeeded", Description = "Plan application completed successfully", IsSuccessful = true },
            new TenantPlanApplicationStatus { Id = (int)TenantPlanApplicationStatusEnum.Failed, MasterCode = "FAILED", FullName = "Failed", Description = "Plan application failed and requires operator review", IsSuccessful = false });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAudienceAgesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AudienceAge>().AnyAsync(ct)) return;

        context.Set<AudienceAge>().AddRange(
            new AudienceAge { Id = (int)AudienceAgeEnum.AllAges, MasterCode = "ALL_AGES", FullName = "All Ages", MinAge = null, MaxAge = null },
            new AudienceAge { Id = (int)AudienceAgeEnum.AdultsOnly18Plus, MasterCode = "ADULTS_18_PLUS", FullName = "Adults Only (18+)", MinAge = 18, MaxAge = null },
            new AudienceAge { Id = (int)AudienceAgeEnum.Teens16Plus, MasterCode = "TEENS_16_PLUS", FullName = "Teens & Adults (16+)", MinAge = 16, MaxAge = null },
            new AudienceAge { Id = (int)AudienceAgeEnum.Preteens12Plus, MasterCode = "PRETEENS_12_PLUS", FullName = "Preteens & Up (12+)", MinAge = 12, MaxAge = null },
            new AudienceAge { Id = (int)AudienceAgeEnum.ChildrenUnder6, MasterCode = "CHILDREN_UNDER_6", FullName = "Young Children (0-6)", MinAge = null, MaxAge = 6 },
            new AudienceAge { Id = (int)AudienceAgeEnum.YouthUnder12, MasterCode = "YOUTH_UNDER_12", FullName = "Children (0-12)", MinAge = null, MaxAge = 12 },
            new AudienceAge { Id = (int)AudienceAgeEnum.YouthUnder16, MasterCode = "YOUTH_UNDER_16", FullName = "Children & Young Teens (0-16)", MinAge = null, MaxAge = 16 },
            new AudienceAge { Id = (int)AudienceAgeEnum.YouthUnder18, MasterCode = "YOUTH_UNDER_18", FullName = "Youth (0-18)", MinAge = null, MaxAge = 18 });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedAudienceGendersAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<AudienceGender>().AnyAsync(ct)) return;

        context.Set<AudienceGender>().AddRange(
            new AudienceGender { Id = (int)AudienceGenderEnum.Man, MasterCode = "MAN", FullName = "Man", Description = "Only for Man Audience" },
            new AudienceGender { Id = (int)AudienceGenderEnum.Woman, MasterCode = "WOMAN", FullName = "Woman", Description = "Only for Woman Audience" },
            new AudienceGender { Id = (int)AudienceGenderEnum.Both, MasterCode = "BOTH_SEGREGATED", FullName = "Both Segregated", Description = "For Both Man and Woman but Segregated so no free mixing" },
            new AudienceGender { Id = 4, MasterCode = "BOTH_FREE_MIXING", FullName = "Both Free Mixing", Description = "For Both Man and Woman but Free Mixing" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedDidCustodyTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<DidCustodyType>().AnyAsync(ct)) return;

        context.Set<DidCustodyType>().AddRange(
            new DidCustodyType { Id = (int)DidCustodyTypeEnum.Custodial, MasterCode = "CUSTODIAL", FullName = "Custodial", Description = "Platform manages the DID keys" },
            new DidCustodyType { Id = (int)DidCustodyTypeEnum.SelfCustody, MasterCode = "SELF_CUSTODY", FullName = "Self-Custody", Description = "User manages their own DID keys" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventFormatsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<EventFormat>().AnyAsync(ct)) return;

        context.Set<EventFormat>().AddRange(
            new EventFormat { Id = (int)EventFormatEnum.Local, MasterCode = "LOCAL", FullName = "Local (In-Person)", Description = "Event takes place at a physical location" },
            new EventFormat { Id = (int)EventFormatEnum.Digital, MasterCode = "DIGITAL", FullName = "Digital (Online)", Description = "Event takes place online" },
            new EventFormat { Id = (int)EventFormatEnum.Hybrid, MasterCode = "HYBRID", FullName = "Hybrid", Description = "Event takes place both in-person and online" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        var statuses = new EventStatus[]
        {
            new EventStatus { Id = (int)EventStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft", Description = "Event is in draft state and not visible to the public" },
            new EventStatus { Id = (int)EventStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published", Description = "Event is published and visible to the public" },
            new EventStatus { Id = (int)EventStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled", Description = "Event has been cancelled" },
            new EventStatus { Id = (int)EventStatusEnum.Completed, MasterCode = "COMPLETED", FullName = "Completed", Description = "Event has been completed" },
            new EventStatus { Id = (int)EventStatusEnum.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Event has been archived" },
            new EventStatus { Id = (int)EventStatusEnum.Moderated, MasterCode = "MODERATED", FullName = "Moderated", Description = "Event was hidden by administration after moderation" }
        };

        var existingIds = await context.Set<EventStatus>()
            .Select(status => status.Id)
            .ToListAsync(ct);
        var existingIdSet = existingIds.ToHashSet();
        var missingStatuses = statuses
            .Where(status => !existingIdSet.Contains(status.Id))
            .ToArray();

        if (missingStatuses.Length == 0) return;

        context.Set<EventStatus>().AddRange(missingStatuses);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventSessionStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        var statuses = new EventSessionStatus[]
        {
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft", Description = "Session is in draft state and not visible to the public" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Submitted, MasterCode = "SUBMITTED", FullName = "Submitted", Description = "Session has been submitted for review" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.UnderReview, MasterCode = "UNDER_REVIEW", FullName = "Under review", Description = "Session is currently being reviewed" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Approved, MasterCode = "APPROVED", FullName = "Approved", Description = "Session has been approved but is not yet published" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published", Description = "Session is published and visible to the public" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Rejected, MasterCode = "REJECTED", FullName = "Rejected", Description = "Session was rejected during review" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled", Description = "Session has been cancelled" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Archived, MasterCode = "ARCHIVED", FullName = "Archived", Description = "Session has been archived" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Completed, MasterCode = "COMPLETED", FullName = "Completed", Description = "Session has been completed" },
            new EventSessionStatus { Id = (int)EventSessionStatusEnum.Moderated, MasterCode = "MODERATED", FullName = "Moderated", Description = "Session was hidden by event-level moderation" }
        };

        var existingIds = await context.Set<EventSessionStatus>()
            .Select(status => status.Id)
            .ToListAsync(ct);
        var existingIdSet = existingIds.ToHashSet();
        var missingStatuses = statuses
            .Where(status => !existingIdSet.Contains(status.Id))
            .ToArray();

        if (missingStatuses.Length == 0) return;

        context.Set<EventSessionStatus>().AddRange(missingStatuses);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<EventType>().AnyAsync(ct)) return;

        context.Set<EventType>().AddRange(
            new EventType { Id = (int)EventTypeEnum.Conference, MasterCode = "CONFERENCE", FullName = "Conference" },
            new EventType { Id = (int)EventTypeEnum.Webinar, MasterCode = "WEBINAR", FullName = "Webinar" },
            new EventType { Id = (int)EventTypeEnum.Workshop, MasterCode = "WORKSHOP", FullName = "Workshop" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedFileTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<FileType>().AnyAsync(ct)) return;

        context.Set<FileType>().AddRange(
            new FileType { Id = (int)FileTypeEnum.Image, MasterCode = "IMAGE", FullName = "Image", Description = "Image file (PNG, JPG, GIF, etc.)" },
            new FileType { Id = (int)FileTypeEnum.Document, MasterCode = "DOCUMENT", FullName = "Document", Description = "Document file (PDF, DOC, etc.)" },
            new FileType { Id = (int)FileTypeEnum.Video, MasterCode = "VIDEO", FullName = "Video", Description = "Video file (MP4, AVI, etc.)" },
            new FileType { Id = (int)FileTypeEnum.Audio, MasterCode = "AUDIO", FullName = "Audio", Description = "Audio file (MP3, WAV, etc.)" },
            new FileType { Id = (int)FileTypeEnum.Other, MasterCode = "OTHER", FullName = "Other", Description = "Other file type" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedLanguagesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<Language>().AnyAsync(ct)) return;

        context.Set<Language>().AddRange(
            new Language { Id = 1, MasterCode = "AR", FullName = "Arabic", Description = "Arabic language" },
            new Language { Id = 2, MasterCode = "EN", FullName = "English", Description = "English language" },
            new Language { Id = 3, MasterCode = "FR", FullName = "French", Description = "French language" },
            new Language { Id = 4, MasterCode = "TR", FullName = "Turkish", Description = "Turkish language" },
            new Language { Id = 5, MasterCode = "UR", FullName = "Urdu", Description = "Urdu language" },
            new Language { Id = 6, MasterCode = "ID", FullName = "Indonesian", Description = "Indonesian language" },
            new Language { Id = 7, MasterCode = "MS", FullName = "Malay", Description = "Malay language" },
            new Language { Id = 8, MasterCode = "BN", FullName = "Bengali", Description = "Bengali language" },
            new Language { Id = 9, MasterCode = "FA", FullName = "Persian", Description = "Persian/Farsi language" },
            new Language { Id = 10, MasterCode = "DE", FullName = "German", Description = "German language" },
            new Language { Id = 11, MasterCode = "NL", FullName = "Dutch", Description = "Dutch language" },
            new Language { Id = 12, MasterCode = "ES", FullName = "Spanish", Description = "Spanish language" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedMadhabsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<Madhab>().AnyAsync(ct)) return;

        context.Set<Madhab>().AddRange(
            new Madhab { Id = (int)MadhabEnum.Hanafi, MasterCode = "HANAFI", FullName = "Hanafi", Description = "Hanafi school of Islamic jurisprudence" },
            new Madhab { Id = (int)MadhabEnum.Maliki, MasterCode = "MALIKI", FullName = "Maliki", Description = "Maliki school of Islamic jurisprudence" },
            new Madhab { Id = (int)MadhabEnum.Shafii, MasterCode = "SHAFII", FullName = "Shafi'i", Description = "Shafi'i school of Islamic jurisprudence" },
            new Madhab { Id = (int)MadhabEnum.Hanbali, MasterCode = "HANBALI", FullName = "Hanbali", Description = "Hanbali school of Islamic jurisprudence" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedModuleDefinitionsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ModuleDefinition>().AnyAsync(ct)) return;

        var seedTimestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        context.Set<ModuleDefinition>().AddRange(
            new ModuleDefinition { Id = SeedIds.ModuleCoreId, ModuleKey = "Mod_Core", Name = "Core Events", Description = "Basic event functionality - title, description, sessions, locations", IconName = "Event", Category = "Core", DisplayOrder = 0, IsActive = true, CreatedAt = seedTimestamp },
            new ModuleDefinition { Id = SeedIds.ModuleIslamicId, ModuleKey = "Mod_Islamic", Name = "Islamic Events", Description = "Islamic-specific features: Madhab selection, prayer time scheduling, gender segregation", IconName = "Mosque", Category = "Domain", DisplayOrder = 1, IsActive = true, CreatedAt = seedTimestamp },
            new ModuleDefinition { Id = SeedIds.ModuleTechId, ModuleKey = "Mod_Tech", Name = "Tech Events", Description = "Developer event features: GitHub repositories, skill levels, live coding sessions", IconName = "Code", Category = "Domain", DisplayOrder = 2, IsActive = true, CreatedAt = seedTimestamp });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedOrganizationPositionsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<OrganizationPosition>().AnyAsync(ct)) return;

        context.Set<OrganizationPosition>().AddRange(
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Founder, MasterCode = "FOUNDER", FullName = "Founder", Description = "Organization founder" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Director, MasterCode = "DIRECTOR", FullName = "Director", Description = "Organization director" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Manager, MasterCode = "MANAGER", FullName = "Manager", Description = "Organization manager" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Teacher, MasterCode = "TEACHER", FullName = "Teacher", Description = "Teacher or instructor" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Secretary, MasterCode = "SECRETARY", FullName = "Secretary", Description = "Organization secretary" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Treasurer, MasterCode = "TREASURER", FullName = "Treasurer", Description = "Organization treasurer" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Coordinator, MasterCode = "COORDINATOR", FullName = "Coordinator", Description = "Event or activity coordinator" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Volunteer, MasterCode = "VOLUNTEER", FullName = "Volunteer", Description = "Organization volunteer" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Intern, MasterCode = "INTERN", FullName = "Intern", Description = "Organization intern" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Advisor, MasterCode = "ADVISOR", FullName = "Advisor", Description = "Organization advisor" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Consultant, MasterCode = "CONSULTANT", FullName = "Consultant", Description = "Organization consultant" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Supervisor, MasterCode = "SUPERVISOR", FullName = "Supervisor", Description = "Supervisor" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Assistant, MasterCode = "ASSISTANT", FullName = "Assistant", Description = "Assistant" },
            new OrganizationPosition { Id = (int)OrganizationPositionEnum.Staff, MasterCode = "STAFF", FullName = "Staff", Description = "General staff member" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedGroupPositionsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<GroupPosition>().AnyAsync(ct)) return;

        context.Set<GroupPosition>().AddRange(
            new GroupPosition { Id = (int)GroupPositionEnum.Leader, MasterCode = "LEADER", FullName = "Leader", Description = "Group leader" },
            new GroupPosition { Id = (int)GroupPositionEnum.CoLeader, MasterCode = "CO_LEADER", FullName = "Co-Leader", Description = "Group co-leader" },
            new GroupPosition { Id = (int)GroupPositionEnum.Coordinator, MasterCode = "COORDINATOR", FullName = "Coordinator", Description = "Group coordinator" },
            new GroupPosition { Id = (int)GroupPositionEnum.Moderator, MasterCode = "MODERATOR", FullName = "Moderator", Description = "Group moderator" },
            new GroupPosition { Id = (int)GroupPositionEnum.Secretary, MasterCode = "SECRETARY", FullName = "Secretary", Description = "Group secretary" },
            new GroupPosition { Id = (int)GroupPositionEnum.Treasurer, MasterCode = "TREASURER", FullName = "Treasurer", Description = "Group treasurer" },
            new GroupPosition { Id = (int)GroupPositionEnum.Mentor, MasterCode = "MENTOR", FullName = "Mentor", Description = "Group mentor" },
            new GroupPosition { Id = (int)GroupPositionEnum.Facilitator, MasterCode = "FACILITATOR", FullName = "Facilitator", Description = "Group facilitator" },
            new GroupPosition { Id = (int)GroupPositionEnum.Volunteer, MasterCode = "VOLUNTEER", FullName = "Volunteer", Description = "Group volunteer" },
            new GroupPosition { Id = (int)GroupPositionEnum.Member, MasterCode = "MEMBER", FullName = "Member", Description = "General group member" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedRegistrationModesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<RegistrationMode>().AnyAsync(ct)) return;

        context.Set<RegistrationMode>().AddRange(
            new RegistrationMode { Id = (int)RegistrationModeEnum.Open, MasterCode = "OPEN", FullName = "Open", Description = "Anyone can register" },
            new RegistrationMode { Id = (int)RegistrationModeEnum.ApprovalRequired, MasterCode = "APPROVAL_REQUIRED", FullName = "Approval Required", Description = "Registration requires approval" },
            new RegistrationMode { Id = (int)RegistrationModeEnum.InviteOnly, MasterCode = "INVITE_ONLY", FullName = "Invite Only", Description = "Only invited users can register" },
            new RegistrationMode { Id = (int)RegistrationModeEnum.Closed, MasterCode = "CLOSED", FullName = "Closed", Description = "Registration is closed" });
        await context.SaveChangesAsync(ct);
    }

    internal static async Task SeedLocationPrivacyLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new LocationKind[]
            {
                new() { Id = (int)LocationKindEnum.Unclassified, MasterCode = "UNCLASSIFIED", FullName = "Unclassified", Description = "Physical location kind has not been reviewed" },
                new() { Id = (int)LocationKindEnum.CommercialVenue, MasterCode = "COMMERCIAL_VENUE", FullName = "Commercial venue", Description = "Commercially operated event venue" },
                new() { Id = (int)LocationKindEnum.PublicSpace, MasterCode = "PUBLIC_SPACE", FullName = "Public space", Description = "Publicly accessible physical space" },
                new() { Id = (int)LocationKindEnum.CommunityVenue, MasterCode = "COMMUNITY_VENUE", FullName = "Community venue", Description = "Community-operated physical venue" },
                new() { Id = (int)LocationKindEnum.PrivateHome, MasterCode = "PRIVATE_HOME", FullName = "Private home", Description = "Private residential location" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new LocationPrivacyState[]
            {
                new() { Id = (int)LocationPrivacyStateEnum.NotProvided, MasterCode = "NOT_PROVIDED", FullName = "Not provided", Description = "No physical location PII has been provided" },
                new() { Id = (int)LocationPrivacyStateEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Physical location PII is active" },
                new() { Id = (int)LocationPrivacyStateEnum.Erased, MasterCode = "ERASED", FullName = "Erased", Description = "Physical location PII was irreversibly erased" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new LocationDisclosureAudience[]
            {
                new() { Id = (int)LocationDisclosureAudienceEnum.Never, MasterCode = "NEVER", FullName = "Never", Description = "Physical location details are never disclosed" },
                new() { Id = (int)LocationDisclosureAudienceEnum.AnyCurrentRegistrant, MasterCode = "ANY_CURRENT_REGISTRANT", FullName = "Any current registrant", Description = "Eligible current registrations may receive disclosed details" },
                new() { Id = (int)LocationDisclosureAudienceEnum.ConfirmedParticipant, MasterCode = "CONFIRMED_PARTICIPANT", FullName = "Confirmed participant", Description = "Only confirmed eligible participants may receive disclosed details" }
            },
            row => row.Id,
            ct);
    }

    internal static async Task SeedLocationAddressGovernanceLookupsAsync(
        ExploreDbContext context,
        CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new LocationAddressSource[]
            {
                new() { Id = (int)LocationAddressSourceEnum.UnknownLegacy, MasterCode = "UNKNOWN_LEGACY", FullName = "Unknown legacy", Description = "Address provenance predates explicit governance or is unknown" },
                new() { Id = (int)LocationAddressSourceEnum.Manual, MasterCode = "MANUAL", FullName = "Manual", Description = "Address was entered locally without a provider selection" },
                new() { Id = (int)LocationAddressSourceEnum.ProviderSelection, MasterCode = "PROVIDER_SELECTION", FullName = "Provider selection", Description = "Address originated from a protected provider selection" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new LocationAddressVisibility[]
            {
                new() { Id = (int)LocationAddressVisibilityEnum.Quarantined, MasterCode = "QUARANTINED", FullName = "Quarantined", Description = "Address is unavailable for local suggestion reuse" },
                new() { Id = (int)LocationAddressVisibilityEnum.CreatorPrivate, MasterCode = "CREATOR_PRIVATE", FullName = "Creator private", Description = "Address reuse is limited to its creator" },
                new() { Id = (int)LocationAddressVisibilityEnum.OrganizationScoped, MasterCode = "ORGANIZATION_SCOPED", FullName = "Organization scoped", Description = "Address reuse is limited to one tenant organization participation" },
                new() { Id = (int)LocationAddressVisibilityEnum.TenantApproved, MasterCode = "TENANT_APPROVED", FullName = "Tenant approved", Description = "Address is approved for reuse across its tenant" }
            },
            row => row.Id,
            ct);
    }

    internal static async Task SeedEventAuthorityLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new EventProvenanceType[]
            {
                new() { Id = (int)EventProvenanceTypeEnum.OrganizerCreated, MasterCode = "ORGANIZER_CREATED", FullName = "Organizer created", Description = "Created by an actor with organizer authority" },
                new() { Id = (int)EventProvenanceTypeEnum.CommunityReported, MasterCode = "COMMUNITY_REPORTED", FullName = "Community reported", Description = "Submitted by a community member for listing review" },
                new() { Id = (int)EventProvenanceTypeEnum.TenantCurated, MasterCode = "TENANT_CURATED", FullName = "Tenant curated", Description = "Curated by the tenant without organizer authority" },
                new() { Id = (int)EventProvenanceTypeEnum.Imported, MasterCode = "IMPORTED", FullName = "Imported", Description = "Imported from an external source" },
                new() { Id = (int)EventProvenanceTypeEnum.Federated, MasterCode = "FEDERATED", FullName = "Federated", Description = "Materialized from a federated source" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new EventPublicActionKind[]
            {
                new() { Id = (int)EventPublicActionKindEnum.OriginalSource, MasterCode = "ORIGINAL_SOURCE", FullName = "Original source", Description = "Canonical source for the event listing" },
                new() { Id = (int)EventPublicActionKindEnum.ExternalEventPage, MasterCode = "EXTERNAL_EVENT_PAGE", FullName = "External event page", Description = "External page containing event information" },
                new() { Id = (int)EventPublicActionKindEnum.ExternalRegistration, MasterCode = "EXTERNAL_REGISTRATION", FullName = "External registration", Description = "External registration destination" },
                new() { Id = (int)EventPublicActionKindEnum.OptionalQuestionnaire, MasterCode = "OPTIONAL_QUESTIONNAIRE", FullName = "Optional questionnaire", Description = "Optional external questionnaire" },
                new() { Id = (int)EventPublicActionKindEnum.Livestream, MasterCode = "LIVESTREAM", FullName = "Livestream", Description = "External livestream destination" },
                new() { Id = (int)EventPublicActionKindEnum.OrganizerContact, MasterCode = "ORGANIZER_CONTACT", FullName = "Organizer contact", Description = "Organizer-controlled contact destination" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new EventPublicActionHealthState[]
            {
                new() { Id = (int)EventPublicActionHealthStateEnum.PendingReview, MasterCode = "PENDING_REVIEW", FullName = "Pending review", Description = "Action is awaiting moderation review" },
                new() { Id = (int)EventPublicActionHealthStateEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Action is approved and available" },
                new() { Id = (int)EventPublicActionHealthStateEnum.Broken, MasterCode = "BROKEN", FullName = "Broken", Description = "Action destination is unavailable" },
                new() { Id = (int)EventPublicActionHealthStateEnum.Unsafe, MasterCode = "UNSAFE", FullName = "Unsafe", Description = "Action destination failed safety review" },
                new() { Id = (int)EventPublicActionHealthStateEnum.Disabled, MasterCode = "DISABLED", FullName = "Disabled", Description = "Action is intentionally disabled" },
                new() { Id = (int)EventPublicActionHealthStateEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Action destination is no longer current" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new EventOrganizerClaimStatus[]
            {
                new() { Id = (int)EventOrganizerClaimStatusEnum.Pending, MasterCode = "PENDING", FullName = "Pending", Description = "Claim is awaiting review" },
                new() { Id = (int)EventOrganizerClaimStatusEnum.EvidenceRequired, MasterCode = "EVIDENCE_REQUIRED", FullName = "Evidence required", Description = "Reviewer requested additional evidence" },
                new() { Id = (int)EventOrganizerClaimStatusEnum.Approved, MasterCode = "APPROVED", FullName = "Approved", Description = "Claim grants future organizer authority" },
                new() { Id = (int)EventOrganizerClaimStatusEnum.Rejected, MasterCode = "REJECTED", FullName = "Rejected", Description = "Claim was rejected" },
                new() { Id = (int)EventOrganizerClaimStatusEnum.Withdrawn, MasterCode = "WITHDRAWN", FullName = "Withdrawn", Description = "Claimant withdrew the claim" },
                new() { Id = (int)EventOrganizerClaimStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Claim expired before approval" }
            },
            row => row.Id,
            ct);
    }

    internal static async Task SeedEventParticipationLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(
            context,
            new ParticipationHandlingMode[]
            {
                new() { Id = (int)ParticipationHandlingModeEnum.InformationOnly, MasterCode = "INFORMATION_ONLY", FullName = "Information only", Description = "The event provides information without a participation workflow" },
                new() { Id = (int)ParticipationHandlingModeEnum.WalkIn, MasterCode = "WALK_IN", FullName = "Walk-in", Description = "Participation is handled in person without advance registration" },
                new() { Id = (int)ParticipationHandlingModeEnum.ExternalManaged, MasterCode = "EXTERNAL_MANAGED", FullName = "Externally managed", Description = "Participation is managed by an external destination" },
                new() { Id = (int)ParticipationHandlingModeEnum.PlatformManaged, MasterCode = "PLATFORM_MANAGED", FullName = "Platform managed", Description = "Participation is managed by this platform" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new AdvanceRegistrationObligation[]
            {
                new() { Id = (int)AdvanceRegistrationObligationEnum.NotApplicable, MasterCode = "NOT_APPLICABLE", FullName = "Not applicable", Description = "Advance registration does not apply" },
                new() { Id = (int)AdvanceRegistrationObligationEnum.Optional, MasterCode = "OPTIONAL", FullName = "Optional", Description = "Advance registration is optional" },
                new() { Id = (int)AdvanceRegistrationObligationEnum.Required, MasterCode = "REQUIRED", FullName = "Required", Description = "Advance registration is required" }
            },
            row => row.Id,
            ct);

        await SeedMissingLookupRowsAsync(
            context,
            new IdentityAccessMode[]
            {
                new() { Id = (int)IdentityAccessModeEnum.AccountRequired, MasterCode = "ACCOUNT_REQUIRED", FullName = "Account required", Description = "Participation requires an authenticated account" },
                new() { Id = (int)IdentityAccessModeEnum.GuestAllowed, MasterCode = "GUEST_ALLOWED", FullName = "Guest allowed", Description = "Participation allows guest identity" },
                new() { Id = (int)IdentityAccessModeEnum.CapabilityTokenAllowed, MasterCode = "CAPABILITY_TOKEN_ALLOWED", FullName = "Capability token allowed", Description = "Participation allows scoped capability-token access" }
            },
            row => row.Id,
            ct);
    }

    internal static async Task SeedTicketingLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new TicketCatalogStatus { Id = (int)TicketCatalogStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft", Description = "Catalog is editable and not available for checkout" },
            new TicketCatalogStatus { Id = (int)TicketCatalogStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published", Description = "Catalog is immutable and available for checkout" },
            new TicketCatalogStatus { Id = (int)TicketCatalogStatusEnum.Retired, MasterCode = "RETIRED", FullName = "Retired", Description = "Catalog is retained as immutable history" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new TicketPricingMode { Id = (int)TicketPricingModeEnum.Fixed, MasterCode = "FIXED", FullName = "Fixed" },
            new TicketPricingMode { Id = (int)TicketPricingModeEnum.Free, MasterCode = "FREE", FullName = "Free" },
            new TicketPricingMode { Id = (int)TicketPricingModeEnum.Donation, MasterCode = "DONATION", FullName = "Donation" },
            new TicketPricingMode { Id = (int)TicketPricingModeEnum.PayWhatYouCan, MasterCode = "PAY_WHAT_YOU_CAN", FullName = "Pay what you can" },
            new TicketPricingMode { Id = (int)TicketPricingModeEnum.SlidingScale, MasterCode = "SLIDING_SCALE", FullName = "Sliding scale" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new ParticipantDataCollectionMode { Id = (int)ParticipantDataCollectionModeEnum.None, MasterCode = "NONE", FullName = "None" },
            new ParticipantDataCollectionMode { Id = (int)ParticipantDataCollectionModeEnum.LeadBookerOnly, MasterCode = "LEAD_BOOKER_ONLY", FullName = "Lead booker only" },
            new ParticipantDataCollectionMode { Id = (int)ParticipantDataCollectionModeEnum.PerTicketOptional, MasterCode = "PER_TICKET_OPTIONAL", FullName = "Per ticket optional" },
            new ParticipantDataCollectionMode { Id = (int)ParticipantDataCollectionModeEnum.PerTicketRequired, MasterCode = "PER_TICKET_REQUIRED", FullName = "Per ticket required" },
            new ParticipantDataCollectionMode { Id = (int)ParticipantDataCollectionModeEnum.DeferredAssignment, MasterCode = "DEFERRED_ASSIGNMENT", FullName = "Deferred assignment" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new EntitlementScopeType { Id = (int)EntitlementScopeTypeEnum.Event, MasterCode = "EVENT", FullName = "Event" },
            new EntitlementScopeType { Id = (int)EntitlementScopeTypeEnum.EventDay, MasterCode = "EVENT_DAY", FullName = "Event day" },
            new EntitlementScopeType { Id = (int)EntitlementScopeTypeEnum.EventSession, MasterCode = "EVENT_SESSION", FullName = "Event session" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new EntitlementSelectionRule { Id = (int)EntitlementSelectionRuleEnum.AllIncluded, MasterCode = "ALL_INCLUDED", FullName = "All included" },
            new EntitlementSelectionRule { Id = (int)EntitlementSelectionRuleEnum.FixedSelection, MasterCode = "FIXED_SELECTION", FullName = "Fixed selection" },
            new EntitlementSelectionRule { Id = (int)EntitlementSelectionRuleEnum.ChooseOne, MasterCode = "CHOOSE_ONE", FullName = "Choose one" },
            new EntitlementSelectionRule { Id = (int)EntitlementSelectionRuleEnum.ChooseUpToN, MasterCode = "CHOOSE_UP_TO_N", FullName = "Choose up to N" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new CapacityHoldPolicy { Id = (int)CapacityHoldPolicyEnum.NoHoldUntilReady, MasterCode = "NO_HOLD_UNTIL_READY", FullName = "No hold until ready" },
            new CapacityHoldPolicy { Id = (int)CapacityHoldPolicyEnum.TimedHoldOnSelection, MasterCode = "TIMED_HOLD_ON_SELECTION", FullName = "Timed hold on selection" },
            new CapacityHoldPolicy { Id = (int)CapacityHoldPolicyEnum.ApprovalNoHold, MasterCode = "APPROVAL_NO_HOLD", FullName = "Approval without hold" },
            new CapacityHoldPolicy { Id = (int)CapacityHoldPolicyEnum.WaitlistWhenFull, MasterCode = "WAITLIST_WHEN_FULL", FullName = "Waitlist when full" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new CapacityOversellPolicy { Id = (int)CapacityOversellPolicyEnum.Disallow, MasterCode = "DISALLOW", FullName = "Disallow" },
            new CapacityOversellPolicy { Id = (int)CapacityOversellPolicyEnum.Allow, MasterCode = "ALLOW", FullName = "Allow" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedParticipantLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new ParticipantType { Id = (int)ParticipantTypeEnum.Adult, MasterCode = "ADULT", FullName = "Adult" },
            new ParticipantType { Id = (int)ParticipantTypeEnum.Child, MasterCode = "CHILD", FullName = "Child" },
            new ParticipantType { Id = (int)ParticipantTypeEnum.Dependent, MasterCode = "DEPENDENT", FullName = "Dependent" },
            new ParticipantType { Id = (int)ParticipantTypeEnum.Employee, MasterCode = "EMPLOYEE", FullName = "Employee" },
            new ParticipantType { Id = (int)ParticipantTypeEnum.Guest, MasterCode = "GUEST", FullName = "Guest" },
            new ParticipantType { Id = (int)ParticipantTypeEnum.Unnamed, MasterCode = "UNNAMED", FullName = "Unnamed" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new AssignmentStatus { Id = (int)AssignmentStatusEnum.Unassigned, MasterCode = "UNASSIGNED", FullName = "Unassigned" },
            new AssignmentStatus { Id = (int)AssignmentStatusEnum.Assigned, MasterCode = "ASSIGNED", FullName = "Assigned" },
            new AssignmentStatus { Id = (int)AssignmentStatusEnum.Deferred, MasterCode = "DEFERRED", FullName = "Deferred" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedRegistrationWorkflowLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationRequirementCriticality { Id = (int)RegistrationRequirementCriticalityEnum.Required, MasterCode = "REQUIRED", FullName = "Required" },
            new RegistrationRequirementCriticality { Id = (int)RegistrationRequirementCriticalityEnum.Optional, MasterCode = "OPTIONAL", FullName = "Optional" },
            new RegistrationRequirementCriticality { Id = (int)RegistrationRequirementCriticalityEnum.Informational, MasterCode = "INFORMATIONAL", FullName = "Informational" },
            new RegistrationRequirementCriticality { Id = (int)RegistrationRequirementCriticalityEnum.PostRegistration, MasterCode = "POST_REGISTRATION", FullName = "Post-registration" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationFormVersionSourceKind { Id = (int)RegistrationFormVersionSourceKindEnum.Authored, MasterCode = "AUTHORED", FullName = "Authored", Description = "Created directly in ISLAMU form authoring" },
            new RegistrationFormVersionSourceKind { Id = (int)RegistrationFormVersionSourceKindEnum.TemplateClone, MasterCode = "TEMPLATE_CLONE", FullName = "Template clone", Description = "Cloned from a reusable form template" },
            new RegistrationFormVersionSourceKind { Id = (int)RegistrationFormVersionSourceKindEnum.ExternalImported, MasterCode = "EXTERNAL_IMPORTED", FullName = "External imported", Description = "Frozen from an external provider schema snapshot" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationRequirementCompletionEffect { Id = (int)RegistrationRequirementCompletionEffectEnum.BlocksRegistration, MasterCode = "BLOCKS_REGISTRATION", FullName = "Blocks registration" },
            new RegistrationRequirementCompletionEffect { Id = (int)RegistrationRequirementCompletionEffectEnum.EnrichesRegistration, MasterCode = "ENRICHES_REGISTRATION", FullName = "Enriches registration" },
            new RegistrationRequirementCompletionEffect { Id = (int)RegistrationRequirementCompletionEffectEnum.NoRegistrationEffect, MasterCode = "NO_REGISTRATION_EFFECT", FullName = "No registration effect" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationAnswerSyncMode { Id = (int)RegistrationAnswerSyncModeEnum.NONE, MasterCode = "NONE", FullName = "None" },
            new RegistrationAnswerSyncMode { Id = (int)RegistrationAnswerSyncModeEnum.COMPLETION_ONLY, MasterCode = "COMPLETION_ONLY", FullName = "Completion only" },
            new RegistrationAnswerSyncMode { Id = (int)RegistrationAnswerSyncModeEnum.SELECTED_FIELDS, MasterCode = "SELECTED_FIELDS", FullName = "Selected fields" },
            new RegistrationAnswerSyncMode { Id = (int)RegistrationAnswerSyncModeEnum.FULL_CANONICAL, MasterCode = "FULL_CANONICAL", FullName = "Full canonical" },
            new RegistrationAnswerSyncMode { Id = (int)RegistrationAnswerSyncModeEnum.MIRROR_ONLY, MasterCode = "MIRROR_ONLY", FullName = "Mirror only" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationRequirementSubjectType { Id = (int)RegistrationRequirementSubjectTypeEnum.AllOrders, MasterCode = "ALL_ORDERS", FullName = "All orders" },
            new RegistrationRequirementSubjectType { Id = (int)RegistrationRequirementSubjectTypeEnum.SpecificTicketType, MasterCode = "SPECIFIC_TICKET_TYPE", FullName = "Specific ticket type" },
            new RegistrationRequirementSubjectType { Id = (int)RegistrationRequirementSubjectTypeEnum.EveryParticipant, MasterCode = "EVERY_PARTICIPANT", FullName = "Every participant" },
            new RegistrationRequirementSubjectType { Id = (int)RegistrationRequirementSubjectTypeEnum.LeadBookerOnly, MasterCode = "LEAD_BOOKER_ONLY", FullName = "Lead booker only" },
            new RegistrationRequirementSubjectType { Id = (int)RegistrationRequirementSubjectTypeEnum.ChildParticipants, MasterCode = "CHILD_PARTICIPANTS", FullName = "Child participants" },
            new RegistrationRequirementSubjectType { Id = (int)RegistrationRequirementSubjectTypeEnum.SpecificSessionSelection, MasterCode = "SPECIFIC_SESSION_SELECTION", FullName = "Specific session selection" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedRegistrationFormLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationFormStatus { Id = (int)RegistrationFormStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft" },
            new RegistrationFormStatus { Id = (int)RegistrationFormStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published" },
            new RegistrationFormStatus { Id = (int)RegistrationFormStatusEnum.Retired, MasterCode = "RETIRED", FullName = "Retired" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.ShortText, MasterCode = "SHORT_TEXT", FullName = "Short text" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.LongText, MasterCode = "LONG_TEXT", FullName = "Long text" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Integer, MasterCode = "INTEGER", FullName = "Integer" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Decimal, MasterCode = "DECIMAL", FullName = "Decimal" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Boolean, MasterCode = "BOOLEAN", FullName = "Boolean" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Date, MasterCode = "DATE", FullName = "Date" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Time, MasterCode = "TIME", FullName = "Time" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Instant, MasterCode = "INSTANT", FullName = "Instant" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Email, MasterCode = "EMAIL", FullName = "Email" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Phone, MasterCode = "PHONE", FullName = "Phone" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Url, MasterCode = "URL", FullName = "URL" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.CountryCode, MasterCode = "COUNTRY_CODE", FullName = "Country code" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.LanguageTag, MasterCode = "LANGUAGE_TAG", FullName = "Language tag" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.SingleChoice, MasterCode = "SINGLE_CHOICE", FullName = "Single choice" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.MultipleChoice, MasterCode = "MULTIPLE_CHOICE", FullName = "Multiple choice" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Rating, MasterCode = "RATING", FullName = "Rating" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.Consent, MasterCode = "CONSENT", FullName = "Consent" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.File, MasterCode = "FILE", FullName = "File" },
            new RegistrationFieldType { Id = (int)RegistrationFieldTypeEnum.OpaqueExternal, MasterCode = "OPAQUE_EXTERNAL", FullName = "Opaque external" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationOrganizerVisibility { Id = (int)RegistrationOrganizerVisibilityEnum.Hidden, MasterCode = "HIDDEN", FullName = "Hidden" },
            new RegistrationOrganizerVisibility { Id = (int)RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, MasterCode = "AUTHORIZED_ORGANIZERS", FullName = "Authorized organizers" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedRegistrationRetentionLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationRetentionPolicy { Id = (int)RegistrationRetentionPolicyEnum.StandardOperational, MasterCode = "STANDARD_OPERATIONAL", FullName = "Standard operational", DurationDays = 730 },
            new RegistrationRetentionPolicy { Id = (int)RegistrationRetentionPolicyEnum.SensitiveShort, MasterCode = "SENSITIVE_SHORT", FullName = "Sensitive short", DurationDays = 90 },
            new RegistrationRetentionPolicy { Id = (int)RegistrationRetentionPolicyEnum.MarketingConsentEvidence, MasterCode = "MARKETING_CONSENT_EVIDENCE", FullName = "Marketing consent evidence", DurationDays = 2555 },
            new RegistrationRetentionPolicy { Id = (int)RegistrationRetentionPolicyEnum.LegalHold, MasterCode = "LEGAL_HOLD", FullName = "Legal hold", DurationDays = null, IsLegalHold = true }
        ], row => row.Id, ct);
    }

    internal static async Task SeedContactShareLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new ContactShareConsentSubjectType { Id = (int)ContactShareConsentSubjectTypeEnum.User, MasterCode = "USER", FullName = "User" },
            new ContactShareConsentSubjectType { Id = (int)ContactShareConsentSubjectTypeEnum.RegistrationPurchaser, MasterCode = "REGISTRATION_PURCHASER", FullName = "Registration purchaser" },
            new ContactShareConsentSubjectType { Id = (int)ContactShareConsentSubjectTypeEnum.RegistrationParticipant, MasterCode = "REGISTRATION_PARTICIPANT", FullName = "Registration participant" },
            new ContactShareConsentSubjectType { Id = (int)ContactShareConsentSubjectTypeEnum.GuestContact, MasterCode = "GUEST_CONTACT", FullName = "Guest contact" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedRegistrationRuntimeLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationAttemptStatus { Id = (int)RegistrationAttemptStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active" },
            new RegistrationAttemptStatus { Id = (int)RegistrationAttemptStatusEnum.Consumed, MasterCode = "CONSUMED", FullName = "Consumed" },
            new RegistrationAttemptStatus { Id = (int)RegistrationAttemptStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired" },
            new RegistrationAttemptStatus { Id = (int)RegistrationAttemptStatusEnum.Superseded, MasterCode = "SUPERSEDED", FullName = "Superseded" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationSubmissionStatus { Id = (int)RegistrationSubmissionStatusEnum.Received, MasterCode = "RECEIVED", FullName = "Received" },
            new RegistrationSubmissionStatus { Id = (int)RegistrationSubmissionStatusEnum.Finalized, MasterCode = "FINALIZED", FullName = "Finalized" },
            new RegistrationSubmissionStatus { Id = (int)RegistrationSubmissionStatusEnum.EvidenceOnly, MasterCode = "EVIDENCE_ONLY", FullName = "Evidence only" }
        ], row => row.Id, ct);

        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.RegistrationOrder, MasterCode = "REGISTRATION_ORDER", FullName = "Registration order" },
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.Purchaser, MasterCode = "PURCHASER", FullName = "Purchaser" },
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.Participant, MasterCode = "PARTICIPANT", FullName = "Participant" },
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.TicketAssignment, MasterCode = "TICKET_ASSIGNMENT", FullName = "Ticket assignment" },
            new RegistrationAnswerSubjectType { Id = (int)RegistrationAnswerSubjectTypeEnum.SessionSelection, MasterCode = "SESSION_SELECTION", FullName = "Session selection" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedRegistrationProviderLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderKind { Id = (int)RegistrationProviderKindEnum.Native, MasterCode = "NATIVE", FullName = "Native" },
            new RegistrationProviderKind { Id = (int)RegistrationProviderKindEnum.ExternalForm, MasterCode = "EXTERNAL_FORM", FullName = "External form" },
            new RegistrationProviderKind { Id = (int)RegistrationProviderKindEnum.ExternalApi, MasterCode = "EXTERNAL_API", FullName = "External API" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderDeploymentKind { Id = (int)RegistrationProviderDeploymentKindEnum.HostedSaas, MasterCode = "HOSTED_SAAS", FullName = "Hosted SaaS" },
            new RegistrationProviderDeploymentKind { Id = (int)RegistrationProviderDeploymentKindEnum.SelfHosted, MasterCode = "SELF_HOSTED", FullName = "Self-hosted" },
            new RegistrationProviderDeploymentKind { Id = (int)RegistrationProviderDeploymentKindEnum.Native, MasterCode = "NATIVE", FullName = "Native" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderSchemaAuthority { Id = (int)RegistrationProviderSchemaAuthorityEnum.PlatformGenerated, MasterCode = "PLATFORM_GENERATED", FullName = "Platform generated" },
            new RegistrationProviderSchemaAuthority { Id = (int)RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered, MasterCode = "PROVIDER_DISCOVERED", FullName = "Provider discovered" },
            new RegistrationProviderSchemaAuthority { Id = (int)RegistrationProviderSchemaAuthorityEnum.OperatorEntered, MasterCode = "OPERATOR_ENTERED", FullName = "Operator entered" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderPresentationMode { Id = (int)RegistrationProviderPresentationModeEnum.Redirect, MasterCode = "REDIRECT", FullName = "Redirect" },
            new RegistrationProviderPresentationMode { Id = (int)RegistrationProviderPresentationModeEnum.Embed, MasterCode = "EMBED", FullName = "Embed" },
            new RegistrationProviderPresentationMode { Id = (int)RegistrationProviderPresentationModeEnum.Manual, MasterCode = "MANUAL", FullName = "Manual" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderCollectionMode { Id = (int)RegistrationProviderCollectionModeEnum.Native, MasterCode = "NATIVE", FullName = "Native" },
            new RegistrationProviderCollectionMode { Id = (int)RegistrationProviderCollectionModeEnum.ProviderHosted, MasterCode = "PROVIDER_HOSTED", FullName = "Provider hosted" },
            new RegistrationProviderCollectionMode { Id = (int)RegistrationProviderCollectionModeEnum.ProviderApi, MasterCode = "PROVIDER_API", FullName = "Provider API" },
            new RegistrationProviderCollectionMode { Id = (int)RegistrationProviderCollectionModeEnum.MirrorOnly, MasterCode = "MIRROR_ONLY", FullName = "Mirror only" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderCompletionMode { Id = (int)RegistrationProviderCompletionModeEnum.Callback, MasterCode = "CALLBACK", FullName = "Callback" },
            new RegistrationProviderCompletionMode { Id = (int)RegistrationProviderCompletionModeEnum.Polling, MasterCode = "POLLING", FullName = "Polling" },
            new RegistrationProviderCompletionMode { Id = (int)RegistrationProviderCompletionModeEnum.Manual, MasterCode = "MANUAL", FullName = "Manual" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderTrustLevel { Id = (int)RegistrationProviderTrustLevelEnum.Untrusted, MasterCode = "UNTRUSTED", FullName = "Untrusted" },
            new RegistrationProviderTrustLevel { Id = (int)RegistrationProviderTrustLevelEnum.CompletionOnly, MasterCode = "COMPLETION_ONLY", FullName = "Completion only" },
            new RegistrationProviderTrustLevel { Id = (int)RegistrationProviderTrustLevelEnum.SelectedFields, MasterCode = "SELECTED_FIELDS", FullName = "Selected fields" },
            new RegistrationProviderTrustLevel { Id = (int)RegistrationProviderTrustLevelEnum.FullCanonical, MasterCode = "FULL_CANONICAL", FullName = "Full canonical" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.NoDrift, MasterCode = "NO_DRIFT", FullName = "No drift" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.AdditiveOptionalChange, MasterCode = "ADDITIVE_OPTIONAL_CHANGE", FullName = "Additive optional change" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.LabelOnlyChange, MasterCode = "LABEL_ONLY_CHANGE", FullName = "Label-only change" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.MappingRequired, MasterCode = "MAPPING_REQUIRED", FullName = "Mapping required" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.RequiredFieldRemoved, MasterCode = "REQUIRED_FIELD_REMOVED", FullName = "Required field removed" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.TypeChanged, MasterCode = "TYPE_CHANGED", FullName = "Type changed" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.OptionSetChanged, MasterCode = "OPTION_SET_CHANGED", FullName = "Option set changed" },
            new RegistrationProviderDriftClass { Id = (int)RegistrationProviderDriftClassEnum.UnsupportedChange, MasterCode = "UNSUPPORTED_CHANGE", FullName = "Unsupported change" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationProviderBindingState { Id = (int)RegistrationProviderBindingStateEnum.Draft, MasterCode = "DRAFT", FullName = "Draft" },
            new RegistrationProviderBindingState { Id = (int)RegistrationProviderBindingStateEnum.Published, MasterCode = "PUBLISHED", FullName = "Published" },
            new RegistrationProviderBindingState { Id = (int)RegistrationProviderBindingStateEnum.Disabled, MasterCode = "DISABLED", FullName = "Disabled" },
            new RegistrationProviderBindingState { Id = (int)RegistrationProviderBindingStateEnum.DriftBlocked, MasterCode = "DRIFT_BLOCKED", FullName = "Drift blocked" }
        ], row => row.Id, ct);
    }

    private static async Task SeedRegistrationOrderLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new BookingPartyType { Id = (int)BookingPartyTypeEnum.Individual, MasterCode = "INDIVIDUAL", FullName = "Individual" },
            new BookingPartyType { Id = (int)BookingPartyTypeEnum.Household, MasterCode = "HOUSEHOLD", FullName = "Household" },
            new BookingPartyType { Id = (int)BookingPartyTypeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization" },
            new BookingPartyType { Id = (int)BookingPartyTypeEnum.Company, MasterCode = "COMPANY", FullName = "Company" },
            new BookingPartyType { Id = (int)BookingPartyTypeEnum.CommunityGroup, MasterCode = "COMMUNITY_GROUP", FullName = "Community group" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.AwaitingIdentity, MasterCode = "AWAITING_IDENTITY", FullName = "Awaiting identity" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.AwaitingParticipantDetails, MasterCode = "AWAITING_PARTICIPANT_DETAILS", FullName = "Awaiting participant details" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.AwaitingRequirements, MasterCode = "AWAITING_REQUIREMENTS", FullName = "Awaiting requirements" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.ReadyForCheckout, MasterCode = "READY_FOR_CHECKOUT", FullName = "Ready for checkout" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.AwaitingPayment, MasterCode = "AWAITING_PAYMENT", FullName = "Awaiting payment" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.AwaitingApproval, MasterCode = "AWAITING_APPROVAL", FullName = "Awaiting approval" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.Waitlisted, MasterCode = "WAITLISTED", FullName = "Waitlisted" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.Confirmed, MasterCode = "CONFIRMED", FullName = "Confirmed" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.Rejected, MasterCode = "REJECTED", FullName = "Rejected" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled" },
            new RegistrationOrderStatus { Id = (int)RegistrationOrderStatusEnum.NeedsReconciliation, MasterCode = "NEEDS_RECONCILIATION", FullName = "Needs reconciliation" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.Created, MasterCode = "CREATED", FullName = "Created" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.DispatchPending, MasterCode = "DISPATCH_PENDING", FullName = "Dispatch pending" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.RequiresAction, MasterCode = "REQUIRES_ACTION", FullName = "Requires action" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.Processing, MasterCode = "PROCESSING", FullName = "Processing" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.Succeeded, MasterCode = "SUCCEEDED", FullName = "Succeeded" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.Failed, MasterCode = "FAILED", FullName = "Failed" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled" },
            new PaymentAttemptStatus { Id = (int)PaymentAttemptStatusEnum.Unknown, MasterCode = "UNKNOWN", FullName = "Unknown" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new RegistrationInventoryHoldStatus { Id = (int)RegistrationInventoryHoldStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active" },
            new RegistrationInventoryHoldStatus { Id = (int)RegistrationInventoryHoldStatusEnum.Consumed, MasterCode = "CONSUMED", FullName = "Consumed" },
            new RegistrationInventoryHoldStatus { Id = (int)RegistrationInventoryHoldStatusEnum.Released, MasterCode = "RELEASED", FullName = "Released" },
            new RegistrationInventoryHoldStatus { Id = (int)RegistrationInventoryHoldStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired" },
            new RegistrationInventoryHoldStatus { Id = (int)RegistrationInventoryHoldStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled" }
        ], row => row.Id, ct);
    }

    private static async Task SeedAdmissionLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await SeedMissingLookupRowsAsync(context,
        [
            new AdmissionTicketStatus { Id = (int)AdmissionTicketStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active" },
            new AdmissionTicketStatus { Id = (int)AdmissionTicketStatusEnum.Suspended, MasterCode = "SUSPENDED", FullName = "Suspended" },
            new AdmissionTicketStatus { Id = (int)AdmissionTicketStatusEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked" },
            new AdmissionTicketStatus { Id = (int)AdmissionTicketStatusEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled" },
            new AdmissionTicketStatus { Id = (int)AdmissionTicketStatusEnum.Transferred, MasterCode = "TRANSFERRED", FullName = "Transferred" },
            new AdmissionTicketStatus { Id = (int)AdmissionTicketStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new AdmissionTicketCredentialStatus { Id = (int)AdmissionTicketCredentialStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active" },
            new AdmissionTicketCredentialStatus { Id = (int)AdmissionTicketCredentialStatusEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked" }
        ], row => row.Id, ct);
        await SeedMissingLookupRowsAsync(context,
        [
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Issued, MasterCode = "ISSUED", FullName = "Issued" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Suspended, MasterCode = "SUSPENDED", FullName = "Suspended" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Reactivated, MasterCode = "REACTIVATED", FullName = "Reactivated" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Cancelled, MasterCode = "CANCELLED", FullName = "Cancelled" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Transferred, MasterCode = "TRANSFERRED", FullName = "Transferred" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.FullyRefunded, MasterCode = "FULLY_REFUNDED", FullName = "Fully refunded" },
            new AdmissionTicketTransitionReason { Id = (int)AdmissionTicketTransitionReasonEnum.CredentialRotated, MasterCode = "CREDENTIAL_ROTATED", FullName = "Credential rotated" }
        ], row => row.Id, ct);
    }

    internal static async Task SeedPromotionLookupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        await AddMissingLookupRowsAsync(context.PromotionDefinitionStatuses,
        [
            new() { Id = (int)PromotionDefinitionStatusEnum.Draft, MasterCode = "DRAFT", FullName = "Draft", Description = "Promotion definition is editable and not redeemable" },
            new() { Id = (int)PromotionDefinitionStatusEnum.Published, MasterCode = "PUBLISHED", FullName = "Published", Description = "Promotion definition can be redeemed within its configured window" },
            new() { Id = (int)PromotionDefinitionStatusEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked", Description = "Promotion definition is retained historically but no longer redeemable after the effective time" }
        ], ct);

        await AddMissingLookupRowsAsync(context.PromotionReservationStatuses,
        [
            new() { Id = (int)PromotionReservationStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Reservation is holding the order's active promotion slot" },
            new() { Id = (int)PromotionReservationStatusEnum.Consumed, MasterCode = "CONSUMED", FullName = "Consumed", Description = "Reservation was consumed by order finalization" },
            new() { Id = (int)PromotionReservationStatusEnum.Released, MasterCode = "RELEASED", FullName = "Released", Description = "Reservation was explicitly released before finalization" },
            new() { Id = (int)PromotionReservationStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Reservation expired during order recovery" }
        ], ct);
    }

    internal static async Task SeedPlatformMonetizationDefaultsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (!await context.PlatformFeePolicies.AnyAsync(ct))
        {
            context.PlatformFeePolicies.Add(PlatformFeePolicy.CreateDefault());
        }

        if (!await context.PlatformContributionSettings.AnyAsync(ct))
        {
            context.PlatformContributionSettings.Add(PlatformContributionSetting.CreateInitial(false, string.Empty, string.Empty,
            [
                PlatformContributionOption.Create(0, 0, true),
                PlatformContributionOption.Create(500, 1, false),
                PlatformContributionOption.Create(1_000, 2, false),
                PlatformContributionOption.Create(1_500, 3, false),
                PlatformContributionOption.Create(2_000, 4, false)
            ]));
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync(ct);
        }
    }

    private static async Task SeedSystemSettingsAsync(ExploreDbContext context, CancellationToken ct)
    {
        var seedTimestamp = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expectedSettings = new[]
        {
            new SystemSetting { Id = SeedIds.SystemSettingDeploymentModeId, SettingKey = GovernanceSettingKeys.Deployment.Mode, Value = "\"SingleTenant\"", ValueType = SettingValueType.String, IsLocked = true, AllowedValues = "[\"SingleTenant\", \"MultiTenant\"]", Description = "Deployment mode of the application", Category = "System", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingMaxSessionsPerEventId, SettingKey = "events.max_sessions_per_event", Value = "100", ValueType = SettingValueType.Integer, IsLocked = false, Description = "Maximum number of sessions allowed per event", Category = "Events", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingRequireApprovalId, SettingKey = "events.require_approval", Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether events require admin approval before publishing", Category = "Events", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingIslamicModuleId, SettingKey = GovernanceSettingKeys.Modules.IslamicEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Enable Islamic event module", Category = "Modules", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingTechModuleId, SettingKey = GovernanceSettingKeys.Modules.TechEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Enable Tech event module", Category = "Modules", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingTenantSelfServiceRegistrationId, SettingKey = GovernanceSettingKeys.Tenants.SelfServiceRegistration, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether tenants can self-register without manual instance admin invitation", Category = "Tenant", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingTenantWhiteLabelingEnabledId, SettingKey = GovernanceSettingKeys.Tenants.WhiteLabelingEnabled, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether tenant-level white-label branding overrides are enabled in multi-tenant mode", Category = "Tenant", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingRoutingDefaultPublicHomePageId, SettingKey = GovernanceSettingKeys.Routing.DefaultPublicHomePage, Value = "\"EventList\"", ValueType = SettingValueType.String, IsLocked = false, AllowedValues = "[\"EventList\", \"LandingPage\"]", Description = "Default public home page for tenants", Category = "Routing", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingUserSubmissionEnabledId, SettingKey = GovernanceSettingKeys.Events.UserSubmissionEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether tenant users are allowed to submit events", Category = "Events", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingOrganizationVerificationRequiredId, SettingKey = GovernanceSettingKeys.Organizations.VerificationRequired, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether organization verification is required before organizations can operate", Category = "Organizations", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingOrganizationTenantCanOmitVerificationId, SettingKey = GovernanceSettingKeys.Organizations.TenantCanOmitVerification, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether tenant administrators may omit organization verification requirements", Category = "Organizations", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingOrgSubmissionEnabledId, SettingKey = GovernanceSettingKeys.Events.OrganizationSubmissionEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether organizations are allowed to submit events", Category = "Events", DisplayOrder = 4, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingGroupSubmissionEnabledId, SettingKey = GovernanceSettingKeys.Events.GroupSubmissionEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether groups are allowed to submit events", Category = "Events", DisplayOrder = 5, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingOrgSelfRegistrationEnabledId, SettingKey = GovernanceSettingKeys.Organizations.SelfRegistrationEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether users can self-register organizations", Category = "Organizations", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingGroupSelfRegistrationEnabledId, SettingKey = GovernanceSettingKeys.Groups.SelfRegistrationEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether users can self-register groups", Category = "Groups", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEventReportingIntakeEnabledId, SettingKey = EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, Value = EventReportingIntakeSettingDefinitions.IntakeEnabled.DefaultValue, ValueType = EventReportingIntakeSettingDefinitions.IntakeEnabled.ValueType, IsLocked = false, Description = EventReportingIntakeSettingDefinitions.IntakeEnabled.Description, Category = EventReportingIntakeSettingDefinitions.IntakeEnabled.Category, DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingDomainsInstanceBaseDomainId, SettingKey = GovernanceSettingKeys.Domains.InstanceBaseDomain, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Instance base domain used for tenant subdomain generation", Category = "Domains", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingDomainsAllowTenantCustomDomainId, SettingKey = GovernanceSettingKeys.Domains.AllowTenantCustomDomain, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Whether tenant administrators can configure custom domains", Category = "Domains", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingDomainsTenantSubdomainId, SettingKey = GovernanceSettingKeys.Domains.TenantSubdomain, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Tenant subdomain override placeholder", Category = "Domains", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingDomainsTenantCustomDomainId, SettingKey = GovernanceSettingKeys.Domains.TenantCustomDomain, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Tenant custom domain override placeholder", Category = "Domains", DisplayOrder = 4, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingBrandingDisplayNameId, SettingKey = GovernanceSettingKeys.Branding.DisplayName, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default brand display name shown when tenants do not override branding", Category = "Branding", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingBrandingLogoUrlId, SettingKey = GovernanceSettingKeys.Branding.LogoUrl, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default logo URL shown when tenants do not override branding", Category = "Branding", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingBrandingFaviconUrlId, SettingKey = GovernanceSettingKeys.Branding.FaviconUrl, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default favicon URL shown when tenants do not override branding", Category = "Branding", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingBrandingCustomCssUrlId, SettingKey = GovernanceSettingKeys.Branding.CustomCssUrl, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default custom CSS URL applied when tenants do not override branding", Category = "Branding", DisplayOrder = 4, CreatedAt = seedTimestamp },

            // Email / SMTP settings — unlocked by default so tenants can bring their own SMTP
            new SystemSetting { Id = SeedIds.SystemSettingEmailSmtpHostId, SettingKey = GovernanceSettingKeys.Email.SmtpHost, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "SMTP server hostname (e.g., smtp.gmail.com, smtp.mailgun.org)", Category = "Email", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEmailSmtpPortId, SettingKey = GovernanceSettingKeys.Email.SmtpPort, Value = "587", ValueType = SettingValueType.Integer, IsLocked = false, Description = "SMTP server port (587 for StartTLS, 465 for SSL, 25 for unencrypted)", Category = "Email", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEmailSmtpSecurityId, SettingKey = GovernanceSettingKeys.Email.SmtpSecurity, Value = "\"StartTls\"", ValueType = SettingValueType.String, IsLocked = false, AllowedValues = "[\"None\", \"StartTls\", \"SslOnConnect\", \"Auto\"]", Description = "SMTP connection security mode", Category = "Email", DisplayOrder = 5, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEmailFromAddressId, SettingKey = GovernanceSettingKeys.Email.FromAddress, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default sender email address for outbound emails", Category = "Email", DisplayOrder = 6, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEmailFromNameId, SettingKey = GovernanceSettingKeys.Email.FromName, Value = "\"Explore\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default sender display name for outbound emails", Category = "Email", DisplayOrder = 7, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEmailSmtpTimeoutId, SettingKey = GovernanceSettingKeys.Email.SmtpTimeoutSeconds, Value = "30", ValueType = SettingValueType.Integer, IsLocked = false, Description = "SMTP connection timeout in seconds", Category = "Email", DisplayOrder = 8, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingEmailSmtpSkipCertValidationId, SettingKey = GovernanceSettingKeys.Email.SmtpSkipCertValidation, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Skip TLS certificate validation (development/self-signed certs only)", Category = "Email", DisplayOrder = 9, CreatedAt = seedTimestamp },

            // Object Storage - local-first provider policy and optional S3 settings
            new SystemSetting { Id = SeedIds.SystemSettingStorageProviderId, SettingKey = GovernanceSettingKeys.Storage.Provider, Value = $"\"{StorageProviders.Local}\"", ValueType = SettingValueType.String, IsLocked = false, AllowedValues = "[\"local\", \"s3_compatible\", \"legacy_external\"]", Description = "Selected storage provider. Local filesystem is the default; S3-compatible storage is optional.", Category = "ObjectStorage", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingStorageDefaultMaxUploadBytesId, SettingKey = GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, Value = "10485760", ValueType = SettingValueType.Long, IsLocked = false, Description = "Default maximum upload size in bytes for tenant storage policy.", Category = "ObjectStorage", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingStorageDefaultTenantQuotaBytesId, SettingKey = GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, Value = "1073741824", ValueType = SettingValueType.Long, IsLocked = false, Description = "Default tenant storage quota in bytes.", Category = "ObjectStorage", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingStorageInstanceMaxUploadBytesId, SettingKey = GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, Value = "104857600", ValueType = SettingValueType.Long, IsLocked = true, Description = "Instance-wide upload ceiling in bytes; tenant overrides cannot exceed this value.", Category = "ObjectStorage", DisplayOrder = 4, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingS3EndpointId, SettingKey = GovernanceSettingKeys.Storage.Endpoint, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Optional S3-compatible endpoint URL (e.g., https://fsn1.your-objectstorage.com)", Category = "ObjectStorage", DisplayOrder = 5, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingS3PublicEndpointId, SettingKey = GovernanceSettingKeys.Storage.PublicEndpoint, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Optional public S3 endpoint for presigned URLs (if different from internal endpoint)", Category = "ObjectStorage", DisplayOrder = 6, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingS3BucketNameId, SettingKey = GovernanceSettingKeys.Storage.BucketName, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Optional S3 bucket name for object storage", Category = "ObjectStorage", DisplayOrder = 7, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingS3RegionId, SettingKey = GovernanceSettingKeys.Storage.Region, Value = "\"fsn1\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Optional S3 region identifier (e.g., fsn1 for Hetzner, us-east-1 for AWS)", Category = "ObjectStorage", DisplayOrder = 10, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingS3ForcePathStyleId, SettingKey = GovernanceSettingKeys.Storage.ForcePathStyle, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Use path-style URLs for optional S3-compatible storage", Category = "ObjectStorage", DisplayOrder = 11, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingS3UploadUrlExpirationMinutesId, SettingKey = GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes, Value = "60", ValueType = SettingValueType.Integer, IsLocked = false, Description = "Optional S3 presigned upload URL expiration time in minutes", Category = "ObjectStorage", DisplayOrder = 12, CreatedAt = seedTimestamp },

            // Analytics
            new SystemSetting { Id = SeedIds.SystemSettingAnalyticsProviderId, SettingKey = GovernanceSettingKeys.Analytics.Provider, Value = "\"none\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Analytics provider (none, posthog, plausible, rybbit, rudderstack)", AllowedValues = "[\"none\",\"posthog\",\"plausible\",\"rybbit\",\"rudderstack\"]", Category = "Analytics", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingAnalyticsEnabledId, SettingKey = GovernanceSettingKeys.Analytics.Enabled, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Enable analytics tracking", Category = "Analytics", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingAnalyticsApiKeyId, SettingKey = GovernanceSettingKeys.Analytics.ApiKey, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Analytics provider public/write API key", Category = "Analytics", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingAnalyticsEndpointUrlId, SettingKey = GovernanceSettingKeys.Analytics.EndpointUrl, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Analytics provider endpoint URL (supports self-hosted deployments)", Category = "Analytics", DisplayOrder = 4, CreatedAt = seedTimestamp },

            // Localization / TMS
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationDefaultLanguageId, SettingKey = GovernanceSettingKeys.Localization.DefaultLanguage, Value = "\"en\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Default language code (ISO 639-1) for the instance", Category = "Localization", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationTmsProviderId, SettingKey = GovernanceSettingKeys.Localization.TmsProvider, Value = "\"none\"", ValueType = SettingValueType.String, IsLocked = false, AllowedValues = "[\"none\",\"tolgee\",\"weblate\"]", Description = "Translation Management System provider (none uses offline bundles)", Category = "Localization", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationTmsApiUrlId, SettingKey = GovernanceSettingKeys.Localization.TmsApiUrl, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "TMS API base URL (e.g., https://app.tolgee.io or self-hosted URL)", Category = "Localization", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationTmsProjectIdId, SettingKey = GovernanceSettingKeys.Localization.TmsProjectId, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "TMS project identifier", Category = "Localization", DisplayOrder = 4, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationTmsComponentId, SettingKey = GovernanceSettingKeys.Localization.TmsComponent, Value = "\"\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Weblate component slug (Weblate-specific, leave empty for Tolgee)", Category = "Localization", DisplayOrder = 5, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationEnabledLanguagesId, SettingKey = GovernanceSettingKeys.Localization.EnabledLanguages, Value = "\"en,fr,ar\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Comma-separated culture codes the instance has enabled (must be a subset of the compile-time CultureRegistry).", Category = "Localization", DisplayOrder = 6, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationFallbackLanguageId, SettingKey = GovernanceSettingKeys.Localization.FallbackLanguage, Value = "\"en\"", ValueType = SettingValueType.String, IsLocked = false, Description = "Fallback language used when a requested translation key is missing; must be in EnabledLanguages.", Category = "Localization", DisplayOrder = 7, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationClientPickerEnabledId, SettingKey = GovernanceSettingKeys.Localization.ClientPickerEnabled, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Kill-switch: hides the in-app language picker when false, without a redeploy.", Category = "Localization", DisplayOrder = 8, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingLocalizationForceOfflineModeId, SettingKey = GovernanceSettingKeys.Localization.ForceOfflineMode, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = false, Description = "Emergency toggle: routes RuntimeTranslationProvider through OfflineTranslationProvider regardless of tms_provider.", Category = "Localization", DisplayOrder = 9, CreatedAt = seedTimestamp },

            new SystemSetting { Id = SeedIds.SystemSettingSupportAccessEnabledId, SettingKey = GovernanceSettingKeys.SupportAccess.Enabled, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = true, Description = "Global kill switch for admin support-access sessions", Category = "SupportAccess", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingSupportAccessMaxReadOnlyMinutesId, SettingKey = GovernanceSettingKeys.SupportAccess.MaxReadOnlyMinutes, Value = "30", ValueType = SettingValueType.Integer, IsLocked = true, Description = "Maximum duration in minutes for read-only support-access sessions", Category = "SupportAccess", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingSupportAccessMaxWriteMinutesId, SettingKey = GovernanceSettingKeys.SupportAccess.MaxWriteMinutes, Value = "10", ValueType = SettingValueType.Integer, IsLocked = true, Description = "Maximum duration in minutes for write-capable support-access sessions", Category = "SupportAccess", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingSupportAccessAllowWriteModeId, SettingKey = GovernanceSettingKeys.SupportAccess.AllowWriteMode, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = true, Description = "Allow operators to start write-capable support-access sessions", Category = "SupportAccess", DisplayOrder = 4, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingSupportAccessRequireTicketReferenceId, SettingKey = GovernanceSettingKeys.SupportAccess.RequireTicketReference, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = true, Description = "Require a ticket or external reference before starting support access", Category = "SupportAccess", DisplayOrder = 5, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingSupportAccessOneActiveSessionPerActorId, SettingKey = GovernanceSettingKeys.SupportAccess.OneActiveSessionPerActor, Value = "true", ValueType = SettingValueType.Boolean, IsLocked = true, Description = "Restrict each actor to one active support-access session", Category = "SupportAccess", DisplayOrder = 6, CreatedAt = seedTimestamp },

            new SystemSetting { Id = SeedIds.SystemSettingAtprotoEventsEnabledId, SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = true, Description = "Enable ATProto event fetching and eligible outbound publication", Category = "AtprotoFederation", DisplayOrder = 1, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingAtprotoEventValidationProfileId, SettingKey = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile, Value = "\"platform\"", ValueType = SettingValueType.String, IsLocked = true, AllowedValues = "[\"platform\",\"community_lexicon\"]", Description = "Select platform or community-lexicon event publication readiness", Category = "AtprotoFederation", DisplayOrder = 2, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingAtprotoEventsBackfillEnabledId, SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsBackfillEnabled, Value = "false", ValueType = SettingValueType.Boolean, IsLocked = true, Description = "Enable recovery of inbound ATProto events that were missed while processing was unavailable", Category = "AtprotoFederation", DisplayOrder = 3, CreatedAt = seedTimestamp },
            new SystemSetting { Id = SeedIds.SystemSettingAtprotoEventsBackfillModeId, SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsBackfillMode, Value = "\"downtime_only\"", ValueType = SettingValueType.String, IsLocked = true, AllowedValues = "[\"downtime_only\",\"full\"]", Description = "Limit inbound ATProto event recovery to downtime gaps or allow a full replay", Category = "AtprotoFederation", DisplayOrder = 4, CreatedAt = seedTimestamp }
        };

        var existingIds = await context.Set<SystemSetting>()
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(ct);

        var existingIdSet = existingIds.ToHashSet();
        var missingSettings = expectedSettings
            .Where(x => !existingIdSet.Contains(x.Id))
            .ToList();

        if (missingSettings.Count == 0)
        {
            return;
        }

        context.Set<SystemSetting>().AddRange(missingSettings);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedTagTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<TagType>().AnyAsync(ct)) return;

        context.Set<TagType>().AddRange(
            new TagType { Id = 1, MasterCode = "TITLE", FullName = "Title", Description = "Title-based tags for labeling and categorization" },
            new TagType { Id = 2, MasterCode = "PEOPLE", FullName = "People", Description = "People-based tags for associating persons with content" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedVisibilityTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<VisibilityType>().AnyAsync(ct)) return;

        context.Set<VisibilityType>().AddRange(
            new VisibilityType { Id = (int)VisibilityTypeEnum.Public, MasterCode = "PUBLIC", FullName = "Public", Description = "Visible to everyone" },
            new VisibilityType { Id = (int)VisibilityTypeEnum.Private, MasterCode = "PRIVATE", FullName = "Private", Description = "Only visible to invited members" },
            new VisibilityType { Id = (int)VisibilityTypeEnum.Unlisted, MasterCode = "UNLISTED", FullName = "Unlisted", Description = "Not listed publicly but accessible via direct link" },
            new VisibilityType { Id = (int)VisibilityTypeEnum.MembersOnly, MasterCode = "MEMBERS_ONLY", FullName = "Members Only", Description = "Only visible to organization members" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedRolesAsync(ExploreDbContext context, CancellationToken ct)
    {
        var expectedRoles = new[]
        {
            // Platform scope (1-9)
            new Role { Id = (int)RoleEnum.Admin, MasterCode = "platform.admin", FullName = "Admin", Description = "Platform administration", Scope = RoleScopeEnum.Platform, IsSystem = true },
            new Role { Id = (int)RoleEnum.Moderator, MasterCode = "platform.moderator", FullName = "Moderator", Description = "Platform moderation", Scope = RoleScopeEnum.Platform, IsSystem = true },
            new Role { Id = (int)RoleEnum.Member, MasterCode = "platform.member", FullName = "Member", Description = "Platform member", Scope = RoleScopeEnum.Platform, IsSystem = true },

            // Tenant scope (10-19)
            new Role { Id = (int)RoleEnum.TenantAdmin, MasterCode = "tenant.admin", FullName = "Admin", Description = "Tenant administration", Scope = RoleScopeEnum.Tenant, IsSystem = true },
            new Role { Id = (int)RoleEnum.TenantModerator, MasterCode = "tenant.moderator", FullName = "Moderator", Description = "Tenant content moderation", Scope = RoleScopeEnum.Tenant, IsSystem = true },
            new Role { Id = (int)RoleEnum.TenantMember, MasterCode = "tenant.member", FullName = "Member", Description = "Tenant member", Scope = RoleScopeEnum.Tenant, IsSystem = true },

            // Organization scope (20-29)
            new Role { Id = (int)RoleEnum.OrgAdmin, MasterCode = "org.admin", FullName = "Admin", Description = "Organization administrator", Scope = RoleScopeEnum.Organization, IsSystem = true },
            new Role { Id = (int)RoleEnum.OrgModerator, MasterCode = "org.moderator", FullName = "Moderator", Description = "Organization moderator", Scope = RoleScopeEnum.Organization, IsSystem = true },
            new Role { Id = (int)RoleEnum.OrgMember, MasterCode = "org.member", FullName = "Member", Description = "Regular organization member", Scope = RoleScopeEnum.Organization, IsSystem = true },

            // Group scope (30-39)
            new Role { Id = (int)RoleEnum.GroupAdmin, MasterCode = "group.admin", FullName = "Admin", Description = "Group administrator", Scope = RoleScopeEnum.Group, IsSystem = true },
            new Role { Id = (int)RoleEnum.GroupModerator, MasterCode = "group.moderator", FullName = "Moderator", Description = "Group moderator", Scope = RoleScopeEnum.Group, IsSystem = true },
            new Role { Id = (int)RoleEnum.GroupMember, MasterCode = "group.member", FullName = "Member", Description = "Regular group member", Scope = RoleScopeEnum.Group, IsSystem = true },

            // Event scope (40-49) - first-release operational roles only
            new Role { Id = (int)RoleEnum.EventOwner, MasterCode = "event.owner", FullName = "Event Owner", Description = "Owns event team authority and ownership transfer", Scope = RoleScopeEnum.Event, IsSystem = true },
            new Role { Id = (int)RoleEnum.EventManager, MasterCode = "event.manager", FullName = "Event Manager", Description = "Manages day-to-day event operations", Scope = RoleScopeEnum.Event, IsSystem = true },
            new Role { Id = (int)RoleEnum.RegistrationManager, MasterCode = "event.registration_manager", FullName = "Registration Manager", Description = "Manages registrations for one event", Scope = RoleScopeEnum.Event, IsSystem = true },
            new Role { Id = (int)RoleEnum.CheckInStaff, MasterCode = "event.check_in_staff", FullName = "Check-in Staff", Description = "Handles attendee check-in for one event", Scope = RoleScopeEnum.Event, IsSystem = true }
        };

        var existingIds = await context.Roles
            .AsNoTracking()
            .Select(x => x.Id)
            .ToListAsync(ct);

        var existingIdSet = existingIds.ToHashSet();
        var missingRoles = expectedRoles
            .Where(x => !existingIdSet.Contains(x.Id))
            .ToList();

        if (missingRoles.Count == 0) return;

        context.Roles.AddRange(missingRoles);
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedPermissionsAsync(ExploreDbContext context, CancellationToken ct)
    {
        // Permission vocabulary: resource_kind × action pairs for all 18 resource kinds.
        // MasterCode format: "{resource_kind}:{action}" (matches Cerbos resource/action model).
        var expectedPermissions = new List<Permission>();
        var id = 1;

        // Helper to add a permission set for a resource kind
        void AddPermissions(string resourceKind, string groupName, RoleScopeEnum scope, string[] actions, bool isFiltered = false)
        {
            foreach (var action in actions)
            {
                expectedPermissions.Add(new Permission
                {
                    Id = id++,
                    ResourceKind = resourceKind,
                    Action = action,
                    MasterCode = $"{resourceKind}:{action}",
                    FullName = $"{FormatName(action)} {FormatName(resourceKind)}",
                    GroupName = groupName,
                    Scope = scope,
                    IsSystem = true,
                    IsFiltered = isFiltered,
                    IsActive = true
                });
            }
        }

        string[] crud = ["view", "create", "update", "delete"];
        string[] readOnly = ["view"];
        string[] noDelete = ["view", "create", "update"];

        // Events group
        AddPermissions("event", "Events", RoleScopeEnum.Event, crud);
        AddPermissions("event_day", "Events", RoleScopeEnum.Event, crud);
        AddPermissions("event_agenda_item", "Events", RoleScopeEnum.Event, crud);
        AddPermissions("event_session", "Events", RoleScopeEnum.Event, crud);
        AddPermissions("event_session_agenda_item", "Events", RoleScopeEnum.Event, crud);
        AddPermissions("event_registration", "Events", RoleScopeEnum.Event, crud);

        // Organizations group
        AddPermissions("organization", "Organizations", RoleScopeEnum.Organization, crud);
        AddPermissions("organization_member", "Organizations", RoleScopeEnum.Organization, crud);
        AddPermissions("organization_review", "Organizations", RoleScopeEnum.Organization, crud);

        // Content group
        AddPermissions("category", "Content", RoleScopeEnum.Tenant, crud);
        AddPermissions("tag", "Content", RoleScopeEnum.Tenant, crud);
        AddPermissions("location", "Content", RoleScopeEnum.Tenant, crud);
        AddPermissions("storage_object", "Content", RoleScopeEnum.Organization, noDelete);

        // Users group
        AddPermissions("user", "Users", RoleScopeEnum.Platform, readOnly);
        AddPermissions("tenant_user_role_grant", "Users", RoleScopeEnum.Tenant, crud);

        // Tenant management group
        AddPermissions("tenant", "Tenants", RoleScopeEnum.Platform, crud, isFiltered: true);
        AddPermissions("tenant_setting", "Settings", RoleScopeEnum.Tenant, ["view", "update"]);

        // Instance settings (platform-only, filtered from non-super-admins)
        AddPermissions("instance_setting", "Settings", RoleScopeEnum.Platform, ["view", "update"], isFiltered: true);

        // Federation group
        AddPermissions("indexed_did", "Federation", RoleScopeEnum.Platform, noDelete);
        AddPermissions("atproto_record", "Federation", RoleScopeEnum.Platform, noDelete);

        // Event operational roles group (event-scoped v1 vocabulary)
        AddPermissions("event", "Event Operations", RoleScopeEnum.Event,
        [
            "manage-team",
            "manage-owner",
            "transfer-ownership",
            "manage-finance",
            "manage-public-actions",
            "manage-tickets",
            "view-organizer-claims",
            "review-organizer-claim"
        ]);
        AddPermissions("event_registration", "Event Operations", RoleScopeEnum.Event, ["manage"]);
        AddPermissions("event_check_in", "Event Operations", RoleScopeEnum.Event, ["view", "manage"]);

        expectedPermissions.Add(new Permission
        {
            Id = id++,
            ResourceKind = "event",
            Action = "approve-publish",
            MasterCode = PermissionCodes.EventApprovePublish,
            FullName = "Approve Publish Event",
            GroupName = "Event Moderation",
            Scope = RoleScopeEnum.Platform,
            IsSystem = true,
            IsFiltered = true,
            IsActive = true
        });

        var existingCodes = await context.Permissions
            .AsNoTracking()
            .Select(x => x.MasterCode)
            .ToListAsync(ct);

        var existingCodeSet = existingCodes.ToHashSet();
        var missingPermissions = expectedPermissions
            .Where(x => !existingCodeSet.Contains(x.MasterCode))
            .ToList();

        if (missingPermissions.Count > 0)
        {
            context.Permissions.AddRange(missingPermissions);
            await context.SaveChangesAsync(ct);
        }

        await EnsureEventPermissionScopesAsync(context, ct);
    }

    private static async Task EnsureEventPermissionScopesAsync(ExploreDbContext context, CancellationToken ct)
    {
        var eventPermissionCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "event:view",
            PermissionCodes.EventCreate,
            PermissionCodes.EventUpdate,
            PermissionCodes.EventDelete,
            PermissionCodes.EventPublish,
            "event_day:view",
            PermissionCodes.EventDayCreate,
            PermissionCodes.EventDayUpdate,
            PermissionCodes.EventDayDelete,
            "event_agenda_item:view",
            PermissionCodes.EventAgendaItemCreate,
            PermissionCodes.EventAgendaItemUpdate,
            PermissionCodes.EventAgendaItemDelete,
            "event_session:view",
            PermissionCodes.EventSessionCreate,
            PermissionCodes.EventSessionUpdate,
            PermissionCodes.EventSessionDelete,
            "event_session_agenda_item:view",
            "event_session_agenda_item:create",
            "event_session_agenda_item:update",
            "event_session_agenda_item:delete",
            PermissionCodes.EventRegistrationView,
            "event_registration:create",
            "event_registration:update",
            "event_registration:delete",
            PermissionCodes.EventManageTeam,
            PermissionCodes.EventManageOwner,
            PermissionCodes.EventTransferOwnership,
            PermissionCodes.EventManageFinance,
            PermissionCodes.EventManagePublicActions,
            PermissionCodes.EventManageTickets,
            PermissionCodes.EventViewOrganizerClaims,
            PermissionCodes.EventReviewOrganizerClaim,
            PermissionCodes.EventRegistrationManage,
            PermissionCodes.EventCheckInView,
            PermissionCodes.EventCheckInManage
        };

        var eventPermissions = await context.Permissions
            .Where(p => eventPermissionCodes.Contains(p.MasterCode) && p.RoleScopeId != (int)RoleScopeEnum.Event)
            .ToListAsync(ct);

        if (eventPermissions.Count == 0)
        {
            return;
        }

        foreach (var permission in eventPermissions)
        {
            permission.Scope = RoleScopeEnum.Event;
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedEventRolePermissionsAsync(ExploreDbContext context, CancellationToken ct)
    {
        var rolePermissionCodes = new Dictionary<RoleEnum, string[]>
        {
            [RoleEnum.EventOwner] =
            [
                "event:view",
                PermissionCodes.EventCreate,
                PermissionCodes.EventUpdate,
                PermissionCodes.EventDelete,
                PermissionCodes.EventPublish,
                PermissionCodes.EventManageTeam,
                PermissionCodes.EventManageOwner,
                PermissionCodes.EventTransferOwnership,
                PermissionCodes.EventManageFinance,
                PermissionCodes.EventManagePublicActions,
                PermissionCodes.EventManageTickets,
                PermissionCodes.EventViewOrganizerClaims,
                "event_day:view",
                PermissionCodes.EventDayCreate,
                PermissionCodes.EventDayUpdate,
                PermissionCodes.EventDayDelete,
                "event_agenda_item:view",
                PermissionCodes.EventAgendaItemCreate,
                PermissionCodes.EventAgendaItemUpdate,
                PermissionCodes.EventAgendaItemDelete,
                "event_session:view",
                PermissionCodes.EventSessionCreate,
                PermissionCodes.EventSessionUpdate,
                PermissionCodes.EventSessionDelete,
                "event_session_agenda_item:view",
                "event_session_agenda_item:create",
                "event_session_agenda_item:update",
                "event_session_agenda_item:delete",
                PermissionCodes.EventRegistrationView,
                "event_registration:create",
                "event_registration:update",
                "event_registration:delete",
                PermissionCodes.EventRegistrationManage,
                PermissionCodes.EventCheckInView,
                PermissionCodes.EventCheckInManage
            ],
            [RoleEnum.EventManager] =
            [
                "event:view",
                PermissionCodes.EventUpdate,
                PermissionCodes.EventPublish,
                PermissionCodes.EventManageTeam,
                PermissionCodes.EventManagePublicActions,
                PermissionCodes.EventManageTickets,
                PermissionCodes.EventViewOrganizerClaims,
                "event_day:view",
                PermissionCodes.EventDayCreate,
                PermissionCodes.EventDayUpdate,
                PermissionCodes.EventDayDelete,
                "event_agenda_item:view",
                PermissionCodes.EventAgendaItemCreate,
                PermissionCodes.EventAgendaItemUpdate,
                PermissionCodes.EventAgendaItemDelete,
                "event_session:view",
                PermissionCodes.EventSessionCreate,
                PermissionCodes.EventSessionUpdate,
                PermissionCodes.EventSessionDelete,
                "event_session_agenda_item:view",
                "event_session_agenda_item:create",
                "event_session_agenda_item:update",
                "event_session_agenda_item:delete",
                PermissionCodes.EventRegistrationView,
                PermissionCodes.EventRegistrationManage,
                PermissionCodes.EventCheckInView,
                PermissionCodes.EventCheckInManage
            ],
            [RoleEnum.RegistrationManager] =
            [
                "event:view",
                PermissionCodes.EventRegistrationView,
                PermissionCodes.EventRegistrationManage
            ],
            [RoleEnum.CheckInStaff] =
            [
                "event:view",
                PermissionCodes.EventRegistrationView,
                PermissionCodes.EventCheckInView,
                PermissionCodes.EventCheckInManage
            ]
        };

        var requiredPermissionCodes = rolePermissionCodes.Values
            .SelectMany(codes => codes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var permissionIdsByCode = await context.Permissions
            .AsNoTracking()
            .Where(p => requiredPermissionCodes.Contains(p.MasterCode))
            .ToDictionaryAsync(p => p.MasterCode, p => p.Id, ct);

        var roleIds = rolePermissionCodes.Keys
            .Select(role => (int)role)
            .ToArray();

        var existingPairs = await context.RolePermissions
            .AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => new { rp.RoleId, rp.PermissionId })
            .ToListAsync(ct);

        var existingPairSet = existingPairs
            .Select(pair => (pair.RoleId, pair.PermissionId))
            .ToHashSet();

        var grantedAt = DateTime.UtcNow;
        var missingRolePermissions = new List<RolePermission>();

        foreach (var (role, permissionCodes) in rolePermissionCodes)
        {
            foreach (var permissionCode in permissionCodes)
            {
                if (!permissionIdsByCode.TryGetValue(permissionCode, out var permissionId) ||
                    existingPairSet.Contains(((int)role, permissionId)))
                {
                    continue;
                }

                missingRolePermissions.Add(new RolePermission
                {
                    RoleId = (int)role,
                    PermissionId = permissionId,
                    GrantedAt = grantedAt,
                    Role = null!,
                    Permission = null!
                });
            }
        }

        if (missingRolePermissions.Count == 0)
        {
            return;
        }

        context.RolePermissions.AddRange(missingRolePermissions);
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Formats a snake_case identifier to Title Case for display.
    /// </summary>
    private static string FormatName(string identifier)
    {
        return string.Join(' ', identifier.Split('_')
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static async Task SeedNotificationTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<NotificationType>().AnyAsync(ct)) return;

        context.Set<NotificationType>().AddRange(
            new NotificationType { Id = (int)NotificationTypeEnum.RegistrationConfirmed, MasterCode = "REGISTRATION_CONFIRMED", FullName = "Registration Confirmed", Description = "RSVP or registration was confirmed" },
            new NotificationType { Id = (int)NotificationTypeEnum.ApprovalGranted, MasterCode = "APPROVAL_GRANTED", FullName = "Approval Granted", Description = "An approval request was granted" },
            new NotificationType { Id = (int)NotificationTypeEnum.ApprovalRejected, MasterCode = "APPROVAL_REJECTED", FullName = "Approval Rejected", Description = "An approval request was rejected" },
            new NotificationType { Id = (int)NotificationTypeEnum.WaitlistPromoted, MasterCode = "WAITLIST_PROMOTED", FullName = "Waitlist Promoted", Description = "Promoted from waitlist to confirmed" },
            new NotificationType { Id = (int)NotificationTypeEnum.EventCreated, MasterCode = "EVENT_CREATED", FullName = "Event Created", Description = "A new event was created" },
            new NotificationType { Id = (int)NotificationTypeEnum.EventUpdated, MasterCode = "EVENT_UPDATED", FullName = "Event Updated", Description = "An event was updated" },
            new NotificationType { Id = (int)NotificationTypeEnum.EventCancelled, MasterCode = "EVENT_CANCELLED", FullName = "Event Cancelled", Description = "An event was cancelled" },
            new NotificationType { Id = (int)NotificationTypeEnum.MemberInvited, MasterCode = "MEMBER_INVITED", FullName = "Member Invited", Description = "Invited to join an organization or group" },
            new NotificationType { Id = (int)NotificationTypeEnum.MemberRemoved, MasterCode = "MEMBER_REMOVED", FullName = "Member Removed", Description = "Removed from an organization or group" },
            new NotificationType { Id = (int)NotificationTypeEnum.General, MasterCode = "GENERAL", FullName = "General", Description = "General purpose notification" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedNotificationEntityTypesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<NotificationEntityType>().AnyAsync(ct)) return;

        context.Set<NotificationEntityType>().AddRange(
            new NotificationEntityType { Id = (int)NotificationEntityTypeEnum.Event, MasterCode = "EVENT", FullName = "Event", Description = "Links to an event" },
            new NotificationEntityType { Id = (int)NotificationEntityTypeEnum.Organization, MasterCode = "ORGANIZATION", FullName = "Organization", Description = "Links to an organization" },
            new NotificationEntityType { Id = (int)NotificationEntityTypeEnum.Group, MasterCode = "GROUP", FullName = "Group", Description = "Links to a group" },
            new NotificationEntityType { Id = (int)NotificationEntityTypeEnum.EventRegistration, MasterCode = "EVENT_REGISTRATION", FullName = "Event Registration", Description = "Links to an event registration" },
            new NotificationEntityType { Id = (int)NotificationEntityTypeEnum.EventSession, MasterCode = "EVENT_SESSION", FullName = "Event Session", Description = "Links to an event session" },
            new NotificationEntityType { Id = (int)NotificationEntityTypeEnum.User, MasterCode = "USER", FullName = "User", Description = "Links to a user" });
        await context.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Seeds default instance-level footer link groups (TenantId = null) with standard navigation links.
    /// Only runs if no instance-level footer link groups exist yet.
    /// </summary>
    private static async Task SeedDefaultFooterLinkGroupsAsync(ExploreDbContext context, CancellationToken ct)
    {
        // Only seed if no instance-level (TenantId = null) footer link groups exist
        if (await context.Set<TenantFooterLinkGroup>().AnyAsync(g => g.TenantId == null, ct)) return;

        var now = DateTime.UtcNow;

        // Group 1: Quick Links
        var quickLinksGroup = new TenantFooterLinkGroup
        {
            Id = Guid.Parse("019573a0-0001-7000-8000-000000000001"),
            TenantId = null,
            Title = "Quick Links",
            Order = 0,
            IsActive = true,
            CreatedAt = now,
        };

        // Group 2: Legal
        var legalGroup = new TenantFooterLinkGroup
        {
            Id = Guid.Parse("019573a0-0002-7000-8000-000000000001"),
            TenantId = null,
            Title = "Legal",
            Order = 1,
            IsActive = true,
            CreatedAt = now,
        };

        context.Set<TenantFooterLinkGroup>().AddRange(quickLinksGroup, legalGroup);
        await context.SaveChangesAsync(ct);

        // Quick Links
        context.Set<TenantFooterLink>().AddRange(
            new TenantFooterLink
            {
                Id = Guid.Parse("019573a0-0003-7000-8000-000000000001"),
                FooterLinkGroupId = quickLinksGroup.Id,
                Label = "About Us",
                Url = "/about",
                OpenInNewTab = false,
                Order = 0,
                IsActive = true,
                CreatedAt = now,
            },
            new TenantFooterLink
            {
                Id = Guid.Parse("019573a0-0004-7000-8000-000000000001"),
                FooterLinkGroupId = quickLinksGroup.Id,
                Label = "Events",
                Url = "/events",
                OpenInNewTab = false,
                Order = 1,
                IsActive = true,
                CreatedAt = now,
            },
            new TenantFooterLink
            {
                Id = Guid.Parse("019573a0-0005-7000-8000-000000000001"),
                FooterLinkGroupId = quickLinksGroup.Id,
                Label = "Contact",
                Url = "/contact",
                OpenInNewTab = false,
                Order = 2,
                IsActive = true,
                CreatedAt = now,
            });

        // Legal
        context.Set<TenantFooterLink>().AddRange(
            new TenantFooterLink
            {
                Id = Guid.Parse("019573a0-0006-7000-8000-000000000001"),
                FooterLinkGroupId = legalGroup.Id,
                Label = "Terms of Service",
                Url = "/terms",
                OpenInNewTab = false,
                Order = 0,
                IsActive = true,
                CreatedAt = now,
            },
            new TenantFooterLink
            {
                Id = Guid.Parse("019573a0-0007-7000-8000-000000000001"),
                FooterLinkGroupId = legalGroup.Id,
                Label = "Privacy Policy",
                Url = "/privacy",
                OpenInNewTab = false,
                Order = 1,
                IsActive = true,
                CreatedAt = now,
            });

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedExternalApiKeyStatusesAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ExternalApiKeyStatus>().AnyAsync(ct)) return;

        context.Set<ExternalApiKeyStatus>().AddRange(
            new ExternalApiKeyStatus { Id = (int)ExternalApiKeyStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", Description = "Key is active and can authenticate requests", IsUsable = true },
            new ExternalApiKeyStatus { Id = (int)ExternalApiKeyStatusEnum.Revoked, MasterCode = "REVOKED", FullName = "Revoked", Description = "Key has been permanently revoked by owner or admin", IsUsable = false },
            new ExternalApiKeyStatus { Id = (int)ExternalApiKeyStatusEnum.Expired, MasterCode = "EXPIRED", FullName = "Expired", Description = "Key has passed its expiration date", IsUsable = false },
            new ExternalApiKeyStatus { Id = (int)ExternalApiKeyStatusEnum.Suspended, MasterCode = "SUSPENDED", FullName = "Suspended", Description = "Key is temporarily suspended due to credit exhaustion or policy violation", IsUsable = false },
            new ExternalApiKeyStatus { Id = (int)ExternalApiKeyStatusEnum.PendingRotation, MasterCode = "PENDING_ROTATION", FullName = "Pending Rotation", Description = "Key is in rotation overlap window; still usable until new key is confirmed", IsUsable = true });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedExternalApiKeyCreditPeriodsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<ExternalApiKeyCreditPeriod>().AnyAsync(ct)) return;

        context.Set<ExternalApiKeyCreditPeriod>().AddRange(
            new ExternalApiKeyCreditPeriod { Id = (int)ExternalApiKeyCreditPeriodEnum.None, MasterCode = "NONE", FullName = "None", Description = "No credit tracking; unlimited usage" },
            new ExternalApiKeyCreditPeriod { Id = (int)ExternalApiKeyCreditPeriodEnum.Daily, MasterCode = "DAILY", FullName = "Daily", Description = "Credit quota resets every day" },
            new ExternalApiKeyCreditPeriod { Id = (int)ExternalApiKeyCreditPeriodEnum.Weekly, MasterCode = "WEEKLY", FullName = "Weekly", Description = "Credit quota resets every week" },
            new ExternalApiKeyCreditPeriod { Id = (int)ExternalApiKeyCreditPeriodEnum.Monthly, MasterCode = "MONTHLY", FullName = "Monthly", Description = "Credit quota resets every month" },
            new ExternalApiKeyCreditPeriod { Id = (int)ExternalApiKeyCreditPeriodEnum.Yearly, MasterCode = "YEARLY", FullName = "Yearly", Description = "Credit quota resets every year" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedNotificationReasonsAsync(ExploreDbContext context, CancellationToken ct)
    {
        if (await context.Set<NotificationReason>().AnyAsync(ct)) return;

        context.Set<NotificationReason>().AddRange(
            new NotificationReason { Id = (int)NotificationReasonEnum.Direct, MasterCode = "DIRECT", FullName = "Direct", Description = "Notification sent directly to the user" },
            new NotificationReason { Id = (int)NotificationReasonEnum.Mention, MasterCode = "MENTION", FullName = "Mention", Description = "User was mentioned" },
            new NotificationReason { Id = (int)NotificationReasonEnum.Assignment, MasterCode = "ASSIGNMENT", FullName = "Assignment", Description = "User was assigned a task or role" },
            new NotificationReason { Id = (int)NotificationReasonEnum.Subscription, MasterCode = "SUBSCRIPTION", FullName = "Subscription", Description = "User is subscribed to the source" },
            new NotificationReason { Id = (int)NotificationReasonEnum.Membership, MasterCode = "MEMBERSHIP", FullName = "Membership", Description = "User is a member of the related entity" },
            new NotificationReason { Id = (int)NotificationReasonEnum.System, MasterCode = "SYSTEM", FullName = "System", Description = "System-generated notification" });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedUiThemePresetsAsync(ExploreDbContext context, CancellationToken ct)
    {
        var currentSeedVersion = 6;
        var existingPresets = await context.UiThemePresets
            .Where(p => p.IsSystem && p.TenantId == null)
            .ToListAsync(ct);

        var alreadySeeded = existingPresets.Any(p => p.SeedVersion >= currentSeedVersion);
        if (alreadySeeded) return;

        var enterpriseBlue = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-1111-1111-1111-111111111111"),
            TenantId = null,
            ThemeKey = "enterprise-blue",
            DisplayName = "Enterprise Blue",
            Description = "Default professional theme with a blue accent palette.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#18181B",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#52525B",
                SecondaryContrastText = "#FFFFFF",
                Background = "#F5F5F7",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#18181B",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#18181B",
                DrawerIcon = "#52525B",
                TextPrimary = "#18181B",
                TextSecondary = "#404040",
                Info = "#52525B",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                LinesDefault = "#A1A1AA",
                Divider = "#E4E4E7"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#FAFAFA",
                PrimaryContrastText = "#1A1A1A",
                Secondary = "#A1A1AA",
                SecondaryContrastText = "#1A1A1A",
                Background = "#1A1A1A",
                Surface = "#242424",
                AppbarBackground = "rgba(26,26,26,0.92)",
                AppbarText = "#FAFAFA",
                DrawerBackground = "#1A1A1A",
                DrawerText = "#FAFAFA",
                DrawerIcon = "#A1A1AA",
                TextPrimary = "#FAFAFA",
                TextSecondary = "#A1A1AA",
                Info = "#A1A1AA",
                Success = "#34D399",
                Warning = "#FBBF24",
                Error = "#F87171",
                LinesDefault = "#3F3F46",
                Divider = "#2E2E2E"
            }
        };

        var emeraldGreen = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-2222-2222-2222-222222222222"),
            TenantId = null,
            ThemeKey = "emerald-green",
            DisplayName = "Emerald Green",
            Description = "Fresh and natural theme with green accents, ideal for Islamic event branding.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#16A34A",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#52525B",
                SecondaryContrastText = "#FFFFFF",
                Background = "#F5F5F7",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#18181B",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#18181B",
                DrawerIcon = "#52525B",
                TextPrimary = "#18181B",
                TextSecondary = "#52525B",
                Info = "#2563EB",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                LinesDefault = "#D4D4D8",
                Divider = "#E4E4E7"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#22C55E",
                PrimaryContrastText = "#18181B",
                Secondary = "#E4E4E7",
                SecondaryContrastText = "#18181B",
                Background = "#121212",
                Surface = "#1E1E1E",
                AppbarBackground = "rgba(18,18,18,0.92)",
                AppbarText = "#FAFAFA",
                DrawerBackground = "#121212",
                DrawerText = "#FAFAFA",
                DrawerIcon = "#A1A1AA",
                TextPrimary = "#FAFAFA",
                TextSecondary = "#A1A1AA",
                Info = "#60A5FA",
                Success = "#22C55E",
                Warning = "#FBBF24",
                Error = "#F87171",
                LinesDefault = "#3F3F46",
                Divider = "#27272A"
            }
        };

        var abyssalDark = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-3333-3333-3333-333333333333"),
            TenantId = null,
            ThemeKey = "abyssal-dark",
            DisplayName = "Abyssal Dark",
            Description = "Dark-first theme with deep charcoal tones, still offering a light palette.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#0F62FE",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#52525B",
                SecondaryContrastText = "#FFFFFF",
                Background = "#F5F5F7",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#18181B",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#18181B",
                DrawerIcon = "#71717A",
                TextPrimary = "#18181B",
                TextSecondary = "#71717A",
                Info = "#2563EB",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                LinesDefault = "#D4D4D8",
                Divider = "#E4E4E7"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#60A5FA",
                PrimaryContrastText = "#18181B",
                Secondary = "#A1A1AA",
                SecondaryContrastText = "#18181B",
                Background = "#09090B",
                Surface = "#18181B",
                AppbarBackground = "rgba(9,9,11,0.95)",
                AppbarText = "#FAFAFA",
                DrawerBackground = "#09090B",
                DrawerText = "#FAFAFA",
                DrawerIcon = "#71717A",
                TextPrimary = "#FAFAFA",
                TextSecondary = "#A1A1AA",
                Info = "#60A5FA",
                Success = "#34D399",
                Warning = "#FBBF24",
                Error = "#F87171",
                LinesDefault = "#27272A",
                Divider = "#27272A"
            }
        };

        var pureWhite = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-4444-4444-4444-444444444444"),
            TenantId = null,
            ThemeKey = "pure-white",
            DisplayName = "Pure White",
            Description = "Minimal clean theme with subtle neutral boundaries for maximum clarity.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#3B82F6",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#71717A",
                SecondaryContrastText = "#FFFFFF",
                Background = "#FFFFFF",
                Surface = "#FAFAFA",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#18181B",
                DrawerBackground = "#FAFAFA",
                DrawerText = "#18181B",
                DrawerIcon = "#71717A",
                TextPrimary = "#18181B",
                TextSecondary = "#71717A",
                Info = "#3B82F6",
                Success = "#10B981",
                Warning = "#F59E0B",
                Error = "#EF4444",
                LinesDefault = "#E4E4E7",
                Divider = "#F4F4F5"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#60A5FA",
                PrimaryContrastText = "#18181B",
                Secondary = "#A1A1AA",
                SecondaryContrastText = "#18181B",
                Background = "#121212",
                Surface = "#1E1E1E",
                AppbarBackground = "#121212",
                AppbarText = "#FAFAFA",
                DrawerBackground = "#121212",
                DrawerText = "#FAFAFA",
                DrawerIcon = "#71717A",
                TextPrimary = "#FAFAFA",
                TextSecondary = "#A1A1AA",
                Info = "#60A5FA",
                Success = "#34D399",
                Warning = "#FBBF24",
                Error = "#F87171",
                LinesDefault = "#3F3F46",
                Divider = "#3F3F46"
            }
        };

        var lightHighContrast = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-5555-5555-5555-555555555555"),
            TenantId = null,
            ThemeKey = "light-hc",
            DisplayName = "Light High Contrast",
            Description = "WCAG AAA-compliant light theme with maximum text contrast for accessibility.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#0050D8",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#1E293B",
                SecondaryContrastText = "#FFFFFF",
                Background = "#FFFFFF",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#000000",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#000000",
                DrawerIcon = "#000000",
                TextPrimary = "#000000",
                TextSecondary = "#1E293B",
                Info = "#0050D8",
                Success = "#006600",
                Warning = "#B45309",
                Error = "#B91C1C",
                LinesDefault = "#000000",
                Divider = "#000000"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#0050D8",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#1E293B",
                SecondaryContrastText = "#FFFFFF",
                Background = "#F8FAFC",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#000000",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#000000",
                DrawerIcon = "#000000",
                TextPrimary = "#000000",
                TextSecondary = "#1E293B",
                Info = "#0050D8",
                Success = "#006600",
                Warning = "#B45309",
                Error = "#B91C1C",
                LinesDefault = "#000000",
                Divider = "#000000"
            }
        };

        var darkHighContrast = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-6666-6666-6666-666666666666"),
            TenantId = null,
            ThemeKey = "dark-hc",
            DisplayName = "Dark High Contrast",
            Description = "WCAG AAA-compliant dark theme with pure white text on black backgrounds for maximum readability.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#93C5FD",
                PrimaryContrastText = "#000000",
                Secondary = "#F8FAFC",
                SecondaryContrastText = "#000000",
                Background = "#FFFFFF",
                Surface = "#F9FAFB",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#000000",
                DrawerBackground = "#F9FAFB",
                DrawerText = "#000000",
                DrawerIcon = "#000000",
                TextPrimary = "#000000",
                TextSecondary = "#1E293B",
                Info = "#2563EB",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                LinesDefault = "#1E293B",
                Divider = "#E2E8F0"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#93C5FD",
                PrimaryContrastText = "#000000",
                Secondary = "#F8FAFC",
                SecondaryContrastText = "#000000",
                Background = "#000000",
                Surface = "#0A0A0A",
                AppbarBackground = "#000000",
                AppbarText = "#FFFFFF",
                DrawerBackground = "#000000",
                DrawerText = "#FFFFFF",
                DrawerIcon = "#FFFFFF",
                TextPrimary = "#FFFFFF",
                TextSecondary = "#E2E8F0",
                Info = "#93C5FD",
                Success = "#6EE7B7",
                Warning = "#FCD34D",
                Error = "#FCA5A5",
                LinesDefault = "#FFFFFF",
                Divider = "#FFFFFF"
            }
        };

        var white = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-7777-7777-7777-777777777777"),
            TenantId = null,
            ThemeKey = "classic-white",
            DisplayName = "White",
            Description = "Clean, bright theme with pure white surfaces and crisp blue accents for a professional look.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#2563EB",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#71717A",
                SecondaryContrastText = "#FFFFFF",
                Background = "#FFFFFF",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#18181B",
                DrawerBackground = "#FAFAFA",
                DrawerText = "#18181B",
                DrawerIcon = "#71717A",
                TextPrimary = "#18181B",
                TextSecondary = "#52525B",
                Info = "#2563EB",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                LinesDefault = "#D4D4D8",
                Divider = "#E4E4E7"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#60A5FA",
                PrimaryContrastText = "#18181B",
                Secondary = "#A1A1AA",
                SecondaryContrastText = "#18181B",
                Background = "#121212",
                Surface = "#1E1E1E",
                AppbarBackground = "rgba(18,18,18,0.92)",
                AppbarText = "#FAFAFA",
                DrawerBackground = "#121212",
                DrawerText = "#FAFAFA",
                DrawerIcon = "#A1A1AA",
                TextPrimary = "#FAFAFA",
                TextSecondary = "#A1A1AA",
                Info = "#60A5FA",
                Success = "#34D399",
                Warning = "#FBBF24",
                Error = "#F87171",
                LinesDefault = "#3F3F46",
                Divider = "#27272A"
            }
        };

        var dark = new UiThemePreset
        {
            Id = Guid.Parse("a1b2c3d4-8888-8888-8888-888888888888"),
            TenantId = null,
            ThemeKey = "classic-dark",
            DisplayName = "Dark",
            Description = "Refined dark theme with deep charcoal surfaces and vibrant accents for comfortable extended use.",
            IsSystem = true,
            IsEditable = false,
            IsActive = true,
            SeedVersion = currentSeedVersion,
            LightPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#2563EB",
                PrimaryContrastText = "#FFFFFF",
                Secondary = "#64748B",
                SecondaryContrastText = "#FFFFFF",
                Background = "#F8FAFC",
                Surface = "#FFFFFF",
                AppbarBackground = "#FFFFFF",
                AppbarText = "#0F172A",
                DrawerBackground = "#FFFFFF",
                DrawerText = "#0F172A",
                DrawerIcon = "#64748B",
                TextPrimary = "#0F172A",
                TextSecondary = "#475569",
                Info = "#2563EB",
                Success = "#16A34A",
                Warning = "#D97706",
                Error = "#DC2626",
                LinesDefault = "#E2E8F0",
                Divider = "#E2E8F0"
            },
            DarkPalette = new Domain.ValueObjects.UiThemePalette
            {
                Primary = "#818CF8",
                PrimaryContrastText = "#0F172A",
                Secondary = "#A1A1AA",
                SecondaryContrastText = "#0F172A",
                Background = "#09090B",
                Surface = "#18181B",
                AppbarBackground = "rgba(9,9,11,0.92)",
                AppbarText = "#FAFAFA",
                DrawerBackground = "#09090B",
                DrawerText = "#FAFAFA",
                DrawerIcon = "#A1A1AA",
                TextPrimary = "#FAFAFA",
                TextSecondary = "#A1A1AA",
                Info = "#60A5FA",
                Success = "#4ADE80",
                Warning = "#FACC15",
                Error = "#F87171",
                LinesDefault = "#27272A",
                Divider = "#27272A"
            }
        };

        var presets = new[] { enterpriseBlue, emeraldGreen, abyssalDark, pureWhite, lightHighContrast, darkHighContrast, white, dark };

        foreach (var preset in presets)
        {
            var existing = existingPresets.FirstOrDefault(p => p.ThemeKey == preset.ThemeKey);
            if (existing is not null)
            {
                existing.DisplayName = preset.DisplayName;
                existing.Description = preset.Description;
                existing.LightPalette = preset.LightPalette;
                existing.DarkPalette = preset.DarkPalette;
                existing.SeedVersion = currentSeedVersion;
                existing.IsActive = true;
                existing.UpdatedAt = DateTime.UtcNow;
                context.UiThemePresets.Update(existing);
            }
            else
            {
                preset.CreatedAt = DateTime.UtcNow;
                context.UiThemePresets.Add(preset);
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
