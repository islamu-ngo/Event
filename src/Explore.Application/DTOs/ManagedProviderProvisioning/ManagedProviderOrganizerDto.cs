namespace Explore.Application.DTOs.ManagedProviderProvisioning;

public sealed record ManagedProviderOrganizerDto
{
    public ManagedProviderOrganizerKindDto Kind { get; init; } = ManagedProviderOrganizerKindDto.Organization;
    public required string FullName { get; init; }
    public string? Email { get; init; }
    public string? WebsiteUrl { get; init; }
    public string? Country { get; init; }
    public string? City { get; init; }
    public string? Address { get; init; }
    public string? Postcode { get; init; }
    public string? Description { get; init; }
}
