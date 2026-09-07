using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class EmailDispatchAttempt : ITenantEntity, IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid EmailDispatchOutboxId { get; set; }
    public EmailDispatchOutbox? EmailDispatchOutbox { get; set; }

    public int AttemptNumber { get; set; }
    public string Transport { get; set; } = "smtp";
    public string? Provider { get; set; }
    public EmailDispatchAttemptOutcome Outcome { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureCategory { get; set; }
    public string? SanitizedErrorMessage { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? CorrelationId { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public enum EmailDispatchAttemptOutcome
{
    Succeeded = 1,
    Failed = 2,
    Unknown = 3,
    Skipped = 4
}
