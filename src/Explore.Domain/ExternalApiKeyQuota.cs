using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class ExternalApiKeyQuota : IAuditableEntity
{
    public Guid Id { get; set; }

    public Guid ExternalApiKeyId { get; set; }
    public required ExternalApiKey ExternalApiKey { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public int CreditLimit { get; set; }
    public int CreditsUsed { get; set; }
    public int RolloverCredits { get; set; }
    public long RequestCount { get; set; }

    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
