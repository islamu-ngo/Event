namespace Explore.Domain;

public sealed class AssignmentStatus
{
    public int Id { get; set; }

    public string MasterCode { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Description { get; set; }
}
