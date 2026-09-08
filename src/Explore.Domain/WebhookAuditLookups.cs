namespace Explore.Domain;

public sealed class WebhookAuditActionLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookAuditAction
{
    ConsumerCreated = 1,
    ConsumerProviderModeChanged = 2,
    EndpointCreated = 3,
    EndpointUpdated = 4,
    EndpointArchived = 5,
    EndpointSecretRotated = 6,
    EndpointTestScheduled = 7,
    ProviderBindingRepairSucceeded = 8,
    ProviderBindingRepairRejected = 9,
    PortalAccessIssued = 10,
    PortalAccessRejected = 11,
    DeliveryRetryScheduled = 12,
    IncomingRedriveScheduled = 13,
    EndpointAutoPaused = 14,
    EndpointResumed = 15,
    ProviderPublicationReconciled = 16,
    ProviderPublicationAbandoned = 17,
    BulkReplayScheduled = 18,
    PendingWorkMigrated = 19,
    PayloadViewed = 20,
    RetentionPolicyChanged = 21,
    RetentionCleanupCompleted = 22,
    EndpointPaused = 23,
    BulkReplayCancelled = 24,
    BulkReplayCompleted = 25,
    BulkReplayFailed = 26
}

public sealed class WebhookAuditOutcomeLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookAuditOutcome
{
    Succeeded = 1,
    Rejected = 2,
    Failed = 3
}

public sealed class WebhookAuditPrincipalKindLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookAuditPrincipalKind
{
    User = 1,
    Machine = 2,
    System = 3
}

public sealed class WebhookAuditScopeKindLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookAuditScopeKind
{
    Tenant = 1,
    Instance = 2,
    Organization = 3,
    Group = 4,
    User = 5
}

public sealed class WebhookAuditTargetKindLookup
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
}

public enum WebhookAuditTargetKind
{
    Consumer = 1,
    Endpoint = 2,
    ProviderBinding = 3,
    PortalSession = 4,
    DeliveryAttempt = 5,
    IncomingMessage = 6,
    ProviderPublication = 7,
    RetentionPolicy = 8,
    CleanupRun = 9,
    Payload = 10,
    BulkReplayOperation = 11
}
