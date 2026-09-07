namespace Explore.Application.Models.Tenants;

public sealed class TenantLookupRecord
{
    public Guid TenantId { get; set; }

    public required string Slug { get; set; }

    public string? Subdomain { get; set; }

    public string? CustomDomain { get; set; }
}
