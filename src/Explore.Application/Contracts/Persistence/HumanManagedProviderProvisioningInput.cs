using Explore.Domain.Settings.Documents.Payloads;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Frozen provisioning values captured by Application. The original command remains
/// the custody identity; this input is neither an authorization grant nor a write plan.
/// Property names and organizer values are shared with the native provisioning compiler.
/// </summary>
public sealed record HumanManagedProviderProvisioningInput(
    string ProviderKey,
    string ExternalSystem,
    string ExternalCustomerId,
    string TenantFullName,
    string TenantSlug,
    bool ActivateTenant,
    TenantDirectoryOperatorIdentitySettings? DirectoryOperatorIdentity,
    HumanManagedProviderExternalAdminInput? ExternalAdmin,
    HumanManagedProviderLocalIdentityInput? LocalIdentity,
    HumanManagedProviderOrganizerInput? Organizer)
{
    public override string ToString() => nameof(HumanManagedProviderProvisioningInput);
}

public sealed record HumanManagedProviderExternalAdminInput(
    string IdentityProvider,
    string Subject,
    string Email,
    string FirstName,
    string LastName,
    bool EmailVerified,
    string DisplayName)
{
    public override string ToString() => nameof(HumanManagedProviderExternalAdminInput);
}

public sealed record HumanManagedProviderLocalIdentityInput(Guid LocalSubjectId);

public sealed record HumanManagedProviderOrganizerInput(
    ManagedProviderOrganizerKind Kind,
    string FullName,
    string? Email,
    string? WebsiteUrl,
    string? Country,
    string? City,
    string? Address,
    string? Postcode,
    string? Description)
{
    public override string ToString() => nameof(HumanManagedProviderOrganizerInput);
}

public enum ManagedProviderOrganizerKind
{
    Organization = 1,
    Group = 2
}
