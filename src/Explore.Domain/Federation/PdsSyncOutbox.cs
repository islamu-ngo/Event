using Explore.Domain.Interfaces;

namespace Explore.Domain.Federation;

public sealed class PdsSyncOutbox : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; init; }
    public required string Did { get; init; }
    public required string Collection { get; init; }
    public required string RecordKey { get; init; }
    public PdsSyncOperation Operation { get; init; }
    public string? Payload { get; init; }
    public required string PayloadHash { get; init; }
    public required string IdempotencyKey { get; init; }
    public required string PdsHost { get; init; }
    public required string SourceEntityType { get; init; }
    public Guid SourceEntityId { get; init; }
    public Guid SourceVersion { get; init; }
    public Guid? AtprotoRecordId { get; set; }
    public Guid? DependsOnAtprotoRecordId { get; set; }
    public string? DependsOnCid { get; init; }
    public string? ExpectedCid { get; init; }
    public PdsSyncStatus Status { get; set; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public int MaxRetries { get; init; }
    public DateTime? DeadLetteredAt { get; set; }
    public string? LeaseOwner { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAt { get; set; }
    public long LeaseFence { get; set; }
    public Guid? SupersededById { get; set; }
    public DateTime? SupersededAt { get; set; }
    public string? SettledUri { get; set; }
    public string? SettledCid { get; set; }

    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
    public TenantUser? TenantUser { get; set; }
    public AtprotoRecord? AtprotoRecord { get; set; }
    public AtprotoRecord? DependsOnAtprotoRecord { get; set; }
    public PdsSyncOutbox? SupersededBy { get; set; }
}

public enum PdsSyncOperation
{
    Create = 1,
    Update = 2,
    Delete = 3
}

public enum PdsSyncStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    DeadLettered = 5,
    Superseded = 6
}
