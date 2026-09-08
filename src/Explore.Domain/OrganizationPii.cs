// Uses a 1:1 shared primary-key relationship with Organization for hard-deleteable PII.

namespace Explore.Domain;

public class OrganizationPii
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public required string FullName { get; set; }
    public string? Email { get; set; }
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? Address { get; set; }
    public string? Postcode { get; set; }
}
