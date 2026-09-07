namespace Explore.Application.DTOs.ManagedProviderProvisioning;

using Explore.Application.DTOs.TenantSettings;

public sealed record ManagedProviderClientProvisioningDto
{
    public required string ProviderKey { get; init; }
    public required string ExternalSystem { get; init; }
    public required string ExternalCustomerId { get; init; }
    public required string TenantFullName { get; init; }
    public required string TenantSlug { get; init; }
    public bool ActivateTenant { get; init; } = true;
    public TenantDirectoryOperatorIdentityInputDto? DirectoryOperatorIdentity { get; init; }
    public required ManagedProviderExternalAdminDto ExternalAdmin { get; init; }
    public ManagedProviderOrganizerDto? Organizer { get; init; }
}
