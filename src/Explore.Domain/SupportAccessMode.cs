namespace Explore.Domain;

public class SupportAccessMode
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }
    public bool AllowsWrites { get; set; }
}
