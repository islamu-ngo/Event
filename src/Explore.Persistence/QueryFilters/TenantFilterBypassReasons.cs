// ABOUTME: Centralizes approved tenant-filter bypass reason strings for persistence queries.
// ABOUTME: Keeps system, admin, authentication, and worker cross-tenant reads auditable in code review.

namespace Explore.Persistence.QueryFilters;

public static class TenantFilterBypassReasons
{
    public const string ConfigurationManifestOperationReplay =
        "Manifest retry reconstructs post-commit effects for one exact operation id across its bounded tenant results.";

    public const string InstanceConfigurationManifestExport =
        "Instance-authorized configuration manifest export reads every active tenant for one whole-instance artifact.";

    public const string VisitorPolicyAccountRequiredSafetyRead =
        "Visitor policy mutation evaluates AccountRequired configurations across affected tenant scopes; optional tenant selection uses an exact predicate.";

    public const string VisitorPolicyAuthorityInheritanceSafetyRead =
        "Visitor policy mutation composes final inheritance across affected tenants using only canonical authority keys.";

    public const string TenantScopedRepositoryExactTenantPredicate =
        "Repository bypasses the ambient tenant filter only after applying an explicit tenant predicate.";

    public const string OrganizerPaymentExternalAccountOwnershipCheck =
        "Organizer payment connection repository checks historical external-account ownership across tenants using the exact provider identity tuple.";

    public const string UserTenantMembershipEnumeration =
        "Repository enumerates all tenant memberships for one global user using an explicit user predicate.";

    public const string UserPrivacyErasure =
        "User privacy erasure applies compiled dispositions using one exact subject predicate across tenants.";

    public const string InstanceLocationPrivacyGovernance =
        "Instance location-privacy governance versions active EventLocations across tenants after an authorized ceiling change.";

    public const string InstanceLocationPrivacyReviewQueueDepth =
        "Instance readiness counts the EventLocation privacy remediation backlog across tenants and projects no rows.";

    public const string UserExternalLoginAuthentication =
        "External-login authentication must locate a provider subject before tenant binding.";

    public const string ExternalApiKeyAuthentication =
        "API-key authentication must locate a key by globally unique key id before tenant binding.";

    public const string ExternalApiKeyPlatformManagement =
        "Platform API-key management uses privileged owner/id predicates across tenant and instance keys.";

    public const string ExternalApiKeyPlatformUsageReport =
        "Platform usage reporting intentionally aggregates API-key quota rows across tenants.";

    public const string InstanceStorageAdministration =
        "Instance storage administration intentionally reports and reconciles storage usage across all tenants.";

    public const string ControlPlaneModerationReportingOperations =
        "Control-plane moderation reporting operations intentionally aggregate provider sync state across tenants without reading provider payloads.";

    public const string TenantCapabilityResolution =
        "Tenant capability resolution reads module flags by explicit tenant id before ambient context is guaranteed.";

    public const string TicketingRecoveryTenantOperation =
        "Ticketing recovery validates and fences one explicit tenant and recovery operation before request tenant context exists.";

    public const string ScannerCapabilityAuthentication =
        "Scanner capability authentication resolves a globally opaque digest before tenant authority is established.";

    public const string EventAuthorizationTargetResolution =
        "MCP and AI proposal authorization resolve the target event tenant by explicit event id before provider authorization.";

    public const string TenantLookupCacheWarmup =
        "Tenant lookup cache warmup reads active tenants and domain settings before a request tenant exists.";

    public const string ManagedTenantDomainUniqueness =
        "Managed tenant provisioning checks one normalized domain host across all tenant domain settings.";

    public const string LegacyTenantResolutionLookup =
        "Legacy tenant resolver performs pre-tenant host lookup using explicit setting predicates.";

    public const string DatabaseSeeding =
        "Database seeding performs system-scoped idempotency checks before any request tenant exists.";

    public const string EmailDispatchWorkerCrossTenantQueue =
        "Email dispatch worker polls and updates durable outbox rows across tenants using explicit id/status predicates.";

    public const string OrganizerPaymentReadinessWorkerCrossTenantQueue =
        "Organizer payment readiness worker polls bounded due connection rows across tenants while preserving soft-delete filtering.";

    public const string IntegrationSyncWorkerCrossTenantQueue =
        "Integration sync worker polls and updates durable integration outbox rows across tenants using explicit id/status predicates.";

    public const string RegistrationFinalizationWorkerCrossTenantQueue =
        "Registration finalization worker polls and settles tenant-owned effects across tenants using explicit id/status/fence predicates.";

    public const string RefundReconciliationHealthCrossTenantQueue =
        "Refund readiness aggregates bounded non-identifying status counts across tenant-owned refund attempts and campaigns.";

    public const string RegistrationProviderSubmissionWriteWorkerCrossTenantQueue =
        "Registration provider submission write worker polls and settles tenant-owned effects across tenants using explicit id/status/fence predicates.";

    public const string RegistrationProviderSubscriptionStateWorkerCrossTenantQueue =
        "Registration provider subscription workers poll and settle tenant-owned subscription state across tenants using explicit lease/fence predicates.";

    public const string AtprotoPdsWorkerCrossTenantQueue =
        "AT Protocol PDS worker polls and settles tenant-owned outbox rows across tenants using explicit id/status/fence predicates.";

    public const string AtprotoTenantOperation =
        "AT Protocol tenant operation bypasses ambient filtering only after applying an exact tenant predicate.";

    public const string AtprotoJetstreamGlobalMaterialization =
        "The single fenced Jetstream consumer materializes global records and explicit tenant presentation rows atomically.";

    public const string AtprotoPdsSnapshotGlobalReconciliation =
        "The fenced AT Protocol recovery worker reconciles bounded current snapshots and explicit tenant presentations across configured DIDs.";

    public const string AtprotoJetstreamGovernanceResolution =
        "The single Jetstream consumer resolves one exact governance key across active tenants before presentation materialization.";

    public const string AtprotoActorConsolidation =
        "Verified AT Protocol Actor consolidation moves active operational references across tenants by exact source and canonical Actor ids.";

    public const string AtprotoGlobalActorModeration =
        "Instance-global AT Protocol Actor moderation reconciles live owned Event records across tenants by exact Actor and DID predicates.";

    public const string EmailDispatchTenantOperation =
        "Email dispatch tenant operation bypasses ambient context only after applying an explicit tenant predicate.";

    public const string EmailDeliveryPolicyReconciliation =
        "SMTP policy reconciliation accesses non-secret settings and control rows for selected tenants under delivery-policy coordination.";

    public const string WebhookWorkerCrossTenantQueue =
        "Webhook delivery worker polls and updates durable webhook rows across tenants using explicit id/status predicates.";

    public const string WebhookTenantOperation =
        "Webhook tenant operation bypasses ambient context only after applying an explicit tenant predicate.";

    public const string WebhookOwnerOperation =
        "Webhook owner operation bypasses ambient tenant filtering only with an exact typed owner and resource predicate.";

    public const string IncomingWebhookProviderAuthorityResolution =
        "Incoming webhook verification resolves one globally unique normalized provider application identity before tenant binding.";

    public const string NotificationFanoutWorkerCrossTenantQueue =
        "Notification fanout worker polls durable fanout runs across tenants using explicit status and batch-size predicates.";

    public const string WebPushWorkerCrossTenantQueue =
        "Web Push worker polls and updates durable dispatch rows across tenants using explicit id/status predicates.";

    public const string WebPushTenantOperation =
        "Web Push tenant operation bypasses ambient context only after applying an explicit tenant predicate.";

    public const string WebPushSubscriptionEndpointOwnership =
        "Web Push subscription endpoint ownership lookup uses the globally unique endpoint as a bounded predicate.";

    public const string RegistrationFormTemplateSourceVersion =
        "Registration form authoring reads one shared template source version across tenants using the exact event, form, and version id tuple.";
}
