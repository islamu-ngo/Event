namespace Explore.Domain;

public class TenantPlanAssignmentStatus
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
    public bool IsActiveAssignment { get; set; }
}
