using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class TenantOnboardingState : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required Tenant Tenant { get; set; }
    public bool IsCompleted { get; set; }
    public int CurrentStep { get; set; }
    public int TotalSteps { get; set; }
    public string? CompletedStepsJson { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? CompletedByUserId { get; set; }
}
