using Explore.Domain.Enums;
using ExternalApiKeyOwnerTypeEnum = Explore.Domain.Enums.ExternalApiKeyOwnerType;

namespace Explore.Application.Lookups;

public static class NormalizedLookupMetadata
{
    public static LookupReference RoleScope(int id)
    {
        return id switch
        {
            (int)RoleScopeEnum.Platform => new(id, "PLATFORM", "Platform"),
            (int)RoleScopeEnum.Tenant => new(id, "TENANT", "Tenant"),
            (int)RoleScopeEnum.Organization => new(id, "ORGANIZATION", "Organization"),
            (int)RoleScopeEnum.Group => new(id, "GROUP", "Group"),
            (int)RoleScopeEnum.Event => new(id, "EVENT", "Event"),
            _ => Unknown(id)
        };
    }

    public static LookupReference SettingValueType(int id)
    {
        return id switch
        {
            (int)Explore.Domain.SettingValueType.String => new(id, "STRING", "String"),
            (int)Explore.Domain.SettingValueType.Integer => new(id, "INTEGER", "Integer"),
            (int)Explore.Domain.SettingValueType.Boolean => new(id, "BOOLEAN", "Boolean"),
            (int)Explore.Domain.SettingValueType.Decimal => new(id, "DECIMAL", "Decimal"),
            (int)Explore.Domain.SettingValueType.Json => new(id, "JSON", "JSON"),
            (int)Explore.Domain.SettingValueType.DateTime => new(id, "DATE_TIME", "Date/Time"),
            _ => Unknown(id)
        };
    }

    public static LookupReference LocationAddressSource(int id)
    {
        return id switch
        {
            (int)LocationAddressSourceEnum.UnknownLegacy => new(id, "UNKNOWN_LEGACY", "Unknown legacy"),
            (int)LocationAddressSourceEnum.Manual => new(id, "MANUAL", "Manual"),
            (int)LocationAddressSourceEnum.ProviderSelection => new(id, "PROVIDER_SELECTION", "Provider selection"),
            _ => Unknown(id)
        };
    }

    public static LookupReference LocationAddressVisibility(int id)
    {
        return id switch
        {
            (int)LocationAddressVisibilityEnum.Quarantined => new(id, "QUARANTINED", "Quarantined"),
            (int)LocationAddressVisibilityEnum.CreatorPrivate => new(id, "CREATOR_PRIVATE", "Creator private"),
            (int)LocationAddressVisibilityEnum.OrganizationScoped => new(id, "ORGANIZATION_SCOPED", "Organization scoped"),
            (int)LocationAddressVisibilityEnum.TenantApproved => new(id, "TENANT_APPROVED", "Tenant approved"),
            _ => Unknown(id)
        };
    }

    public static LookupReference ExternalApiKeyOwnerType(int id)
    {
        return id switch
        {
            (int)ExternalApiKeyOwnerTypeEnum.User => new(id, "USER", "User"),
            (int)ExternalApiKeyOwnerTypeEnum.Organization => new(id, "ORGANIZATION", "Organization"),
            (int)ExternalApiKeyOwnerTypeEnum.Group => new(id, "GROUP", "Group"),
            (int)ExternalApiKeyOwnerTypeEnum.Tenant => new(id, "TENANT", "Tenant"),
            (int)ExternalApiKeyOwnerTypeEnum.InstanceAdmin => new(id, "INSTANCE_ADMIN", "Instance Admin"),
            _ => Unknown(id)
        };
    }

    public static LookupReference ExternalApiKeyStatus(int id)
    {
        return id switch
        {
            (int)ExternalApiKeyStatusEnum.Active => new(id, "ACTIVE", "Active"),
            (int)ExternalApiKeyStatusEnum.Revoked => new(id, "REVOKED", "Revoked"),
            (int)ExternalApiKeyStatusEnum.Expired => new(id, "EXPIRED", "Expired"),
            (int)ExternalApiKeyStatusEnum.Suspended => new(id, "SUSPENDED", "Suspended"),
            (int)ExternalApiKeyStatusEnum.PendingRotation => new(id, "PENDING_ROTATION", "Pending Rotation"),
            _ => Unknown(id)
        };
    }

    public static LookupReference ExternalApiKeyCreditPeriod(int id)
    {
        return id switch
        {
            (int)ExternalApiKeyCreditPeriodEnum.None => new(id, "NONE", "None"),
            (int)ExternalApiKeyCreditPeriodEnum.Daily => new(id, "DAILY", "Daily"),
            (int)ExternalApiKeyCreditPeriodEnum.Weekly => new(id, "WEEKLY", "Weekly"),
            (int)ExternalApiKeyCreditPeriodEnum.Monthly => new(id, "MONTHLY", "Monthly"),
            (int)ExternalApiKeyCreditPeriodEnum.Yearly => new(id, "YEARLY", "Yearly"),
            _ => Unknown(id)
        };
    }

    public static LookupReference WebhookConsumerKind(int id) => id switch
    {
        (int)Explore.Domain.WebhookConsumerKind.Tenant => new(id, "TENANT", "Tenant"),
        (int)Explore.Domain.WebhookConsumerKind.Organization => new(id, "ORGANIZATION", "Organization"),
        (int)Explore.Domain.WebhookConsumerKind.Group => new(id, "GROUP", "Group"),
        (int)Explore.Domain.WebhookConsumerKind.User => new(id, "USER", "User"),
        (int)Explore.Domain.WebhookConsumerKind.Instance => new(id, "INSTANCE", "Instance"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookConsumerStatus(int id) => id switch
    {
        (int)Explore.Domain.WebhookConsumerStatus.Active => new(id, "ACTIVE", "Active"),
        (int)Explore.Domain.WebhookConsumerStatus.Disabled => new(id, "DISABLED", "Disabled"),
        (int)Explore.Domain.WebhookConsumerStatus.Archived => new(id, "ARCHIVED", "Archived"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookProviderMode(int id) => id switch
    {
        (int)Explore.Domain.WebhookProviderMode.Disabled => new(id, "DISABLED", "Disabled"),
        (int)Explore.Domain.WebhookProviderMode.Local => new(id, "LOCAL", "Local"),
        (int)Explore.Domain.WebhookProviderMode.Svix => new(id, "SVIX", "Svix"),
        (int)Explore.Domain.WebhookProviderMode.Composite => new(id, "COMPOSITE", "Composite"),
        (int)Explore.Domain.WebhookProviderMode.DryRun => new(id, "DRY_RUN", "Dry run"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookProviderKind(int id) => id switch
    {
        (int)Explore.Domain.WebhookProviderKind.Local => new(id, "LOCAL", "Local"),
        (int)Explore.Domain.WebhookProviderKind.Svix => new(id, "SVIX", "Svix"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookProviderCapability(int id) => id switch
    {
        (int)Explore.Domain.WebhookProviderCapability.EndpointManagement => new(id, "ENDPOINT_MANAGEMENT", "Endpoint management"),
        (int)Explore.Domain.WebhookProviderCapability.ProviderAttemptVisibility => new(id, "PROVIDER_ATTEMPT_VISIBILITY", "Provider attempt visibility"),
        (int)Explore.Domain.WebhookProviderCapability.Replay => new(id, "REPLAY", "Replay"),
        (int)Explore.Domain.WebhookProviderCapability.PayloadInspection => new(id, "PAYLOAD_INSPECTION", "Payload inspection"),
        (int)Explore.Domain.WebhookProviderCapability.AppPortal => new(id, "APP_PORTAL", "App portal"),
        (int)Explore.Domain.WebhookProviderCapability.EventCatalog => new(id, "EVENT_CATALOG", "Event catalog"),
        (int)Explore.Domain.WebhookProviderCapability.ProviderRetentionControl => new(id, "PROVIDER_RETENTION_CONTROL", "Provider retention control"),
        (int)Explore.Domain.WebhookProviderCapability.ApplicationThrottling => new(id, "APPLICATION_THROTTLING", "Application throttling"),
        (int)Explore.Domain.WebhookProviderCapability.EndpointThrottling => new(id, "ENDPOINT_THROTTLING", "Endpoint throttling"),
        (int)Explore.Domain.WebhookProviderCapability.Transformations => new(id, "TRANSFORMATIONS", "Transformations"),
        (int)Explore.Domain.WebhookProviderCapability.Ordering => new(id, "ORDERING", "Ordering"),
        (int)Explore.Domain.WebhookProviderCapability.OperationalCallbacks => new(id, "OPERATIONAL_CALLBACKS", "Operational callbacks"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookEndpointStatus(int id) => id switch
    {
        (int)Explore.Domain.WebhookEndpointStatus.Active => new(id, "ACTIVE", "Active"),
        (int)Explore.Domain.WebhookEndpointStatus.Disabled => new(id, "DISABLED", "Disabled"),
        (int)Explore.Domain.WebhookEndpointStatus.AutoPaused => new(id, "AUTO_PAUSED", "Auto-paused"),
        (int)Explore.Domain.WebhookEndpointStatus.Archived => new(id, "ARCHIVED", "Archived"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookLocalDeliveryStatus(int id) => id switch
    {
        (int)Explore.Domain.WebhookLocalDeliveryStatus.Pending => new(id, "PENDING", "Pending"),
        (int)Explore.Domain.WebhookLocalDeliveryStatus.Delivering => new(id, "DELIVERING", "Delivering"),
        (int)Explore.Domain.WebhookLocalDeliveryStatus.RetryDue => new(id, "RETRY_DUE", "Retry due"),
        (int)Explore.Domain.WebhookLocalDeliveryStatus.Succeeded => new(id, "SUCCEEDED", "Succeeded"),
        (int)Explore.Domain.WebhookLocalDeliveryStatus.DeadLettered => new(id, "DEAD_LETTERED", "Dead-lettered"),
        (int)Explore.Domain.WebhookLocalDeliveryStatus.Abandoned => new(id, "ABANDONED", "Abandoned"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookBulkReplayStatus(int id) => id switch
    {
        (int)Explore.Domain.WebhookBulkReplayStatus.Queued => new(id, "QUEUED", "Queued"),
        (int)Explore.Domain.WebhookBulkReplayStatus.Executing => new(id, "EXECUTING", "Executing"),
        (int)Explore.Domain.WebhookBulkReplayStatus.Completed => new(id, "COMPLETED", "Completed"),
        (int)Explore.Domain.WebhookBulkReplayStatus.Cancelled => new(id, "CANCELLED", "Cancelled"),
        (int)Explore.Domain.WebhookBulkReplayStatus.Failed => new(id, "FAILED", "Failed"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookPendingWorkDecision(int id) => id switch
    {
        (int)Explore.Domain.WebhookPendingWorkDecision.PreserveExisting => new(id, "PRESERVE_EXISTING", "Preserve existing"),
        (int)Explore.Domain.WebhookPendingWorkDecision.MigrateEligible => new(id, "MIGRATE_ELIGIBLE", "Migrate eligible"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookDeliveryAttemptOutcome(int id) => id switch
    {
        (int)Explore.Domain.WebhookDeliveryAttemptOutcome.Scheduled => new(id, "SCHEDULED", "Scheduled"),
        (int)Explore.Domain.WebhookDeliveryAttemptOutcome.Sending => new(id, "SENDING", "Sending"),
        (int)Explore.Domain.WebhookDeliveryAttemptOutcome.Succeeded => new(id, "SUCCEEDED", "Succeeded"),
        (int)Explore.Domain.WebhookDeliveryAttemptOutcome.Failed => new(id, "FAILED", "Failed"),
        (int)Explore.Domain.WebhookDeliveryAttemptOutcome.Abandoned => new(id, "ABANDONED", "Abandoned"),
        _ => Unknown(id)
    };

    public static LookupReference IncomingWebhookMessageStatus(int id) => id switch
    {
        (int)Explore.Domain.IncomingWebhookMessageStatus.Verified => new(id, "VERIFIED", "Verified"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.Processing => new(id, "PROCESSING", "Processing"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.RetryDue => new(id, "RETRY_DUE", "Retry due"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.Processed => new(id, "PROCESSED", "Processed"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.Ignored => new(id, "IGNORED", "Ignored"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.RejectedPermanent => new(id, "REJECTED_PERMANENT", "Rejected permanently"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.DeadLettered => new(id, "DEAD_LETTERED", "Dead-lettered"),
        (int)Explore.Domain.IncomingWebhookMessageStatus.PayloadConflict => new(id, "PAYLOAD_CONFLICT", "Payload conflict"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookProviderPublicationStatus(int id) => id switch
    {
        (int)Explore.Domain.WebhookProviderPublicationStatus.Prepared => new(id, "PREPARED", "Prepared"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.Publishing => new(id, "PUBLISHING", "Publishing"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.ProviderQueued => new(id, "PROVIDER_QUEUED", "Provider queued"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.RetryDue => new(id, "RETRY_DUE", "Retry due"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.PublicationUnknown => new(id, "PUBLICATION_UNKNOWN", "Publication unknown"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.DeadLettered => new(id, "DEAD_LETTERED", "Dead-lettered"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.ManualReconciliation => new(id, "MANUAL_RECONCILIATION", "Manual reconciliation"),
        (int)Explore.Domain.WebhookProviderPublicationStatus.Abandoned => new(id, "ABANDONED", "Abandoned"),
        _ => Unknown(id)
    };

    public static LookupReference WebhookProviderPublicationAttemptOutcome(int id) => id switch
    {
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.PublishingStarted => new(id, "PUBLISHING_STARTED", "Publishing started"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.ProviderQueued => new(id, "PROVIDER_QUEUED", "Provider queued"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.RetryScheduled => new(id, "RETRY_SCHEDULED", "Retry scheduled"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.PublicationUnknown => new(id, "PUBLICATION_UNKNOWN", "Publication unknown"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.DeadLettered => new(id, "DEAD_LETTERED", "Dead-lettered"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationStarted => new(id, "AUTOMATIC_RECONCILIATION_STARTED", "Automatic reconciliation started"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.AutomaticReconciliationUnresolved => new(id, "AUTOMATIC_RECONCILIATION_UNRESOLVED", "Automatic reconciliation unresolved"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.ManualReconciliationRequired => new(id, "MANUAL_RECONCILIATION_REQUIRED", "Manual reconciliation required"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.ReconciledProviderQueued => new(id, "RECONCILED_PROVIDER_QUEUED", "Reconciled provider queued"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.Abandoned => new(id, "ABANDONED", "Abandoned"),
        (int)Explore.Domain.WebhookProviderPublicationAttemptOutcome.ProviderAbsenceConfirmed => new(id, "PROVIDER_ABSENCE_CONFIRMED", "Provider absence confirmed"),
        _ => Unknown(id)
    };

    public static bool IsRoleScopeId(int id)
    {
        return Enum.IsDefined(typeof(RoleScopeEnum), id);
    }

    public static bool IsExternalApiKeyOwnerTypeId(int id)
    {
        return Enum.IsDefined(typeof(ExternalApiKeyOwnerTypeEnum), id);
    }

    private static LookupReference Unknown(int id)
    {
        return new LookupReference(id, "UNKNOWN", "Unknown");
    }
}
