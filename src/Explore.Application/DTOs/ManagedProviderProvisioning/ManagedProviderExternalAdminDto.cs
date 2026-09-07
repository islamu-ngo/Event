namespace Explore.Application.DTOs.ManagedProviderProvisioning;

public sealed record ManagedProviderExternalAdminDto
{
    public required string IdentityProvider { get; init; }
    public required string Subject { get; init; }
    public required string Email { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool EmailVerified { get; init; }
    public string? DisplayName { get; init; }
}
