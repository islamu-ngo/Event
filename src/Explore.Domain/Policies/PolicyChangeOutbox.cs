using Explore.Domain.Settings;

namespace Explore.Domain.Policies;

public class PolicyChangeOutbox
{
    public Guid Id { get; set; }
    public SettingScope Scope { get; set; }
    public Guid? ScopeId { get; set; }
    public PolicyChangeOperation Operation { get; set; }
    public PolicyChangeStatus Status { get; set; } = PolicyChangeStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? NextRetryAt { get; set; }
}

public enum PolicyChangeOperation
{
    Created = 1,
    Updated = 2,
    Deleted = 3
}

public enum PolicyChangeStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4
}
