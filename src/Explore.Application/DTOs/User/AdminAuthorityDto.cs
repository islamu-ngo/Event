namespace Explore.Application.DTOs.User;

/// <summary>
/// Represents the admin authority of a user across the instance, tenant, organization, and group hierarchy.
/// Returned by the admin-authority API endpoint and consumed by the BFF's claims transformation.
/// </summary>
public sealed record AdminAuthorityDto
{
    private IReadOnlyList<Guid> _adminTenantIds = Array.AsReadOnly(Array.Empty<Guid>());
    private IReadOnlyList<Guid> _adminOrganizationIds = Array.AsReadOnly(Array.Empty<Guid>());
    private IReadOnlyList<Guid> _adminGroupIds = Array.AsReadOnly(Array.Empty<Guid>());

    /// <summary>Whether the user is an Instance Administrator (platform-scoped).</summary>
    public bool IsInstanceAdmin { get; init; }

    /// <summary>Tenant IDs where the user has tenant-level admin rights.</summary>
    public IReadOnlyList<Guid> AdminTenantIds
    {
        get => _adminTenantIds;
        init => _adminTenantIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>Organization IDs where the user has organization-level admin rights (Creator, CoOwner, Admin).</summary>
    public IReadOnlyList<Guid> AdminOrganizationIds
    {
        get => _adminOrganizationIds;
        init => _adminOrganizationIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>Group IDs where the user has group-level admin rights (Creator or Admin).</summary>
    public IReadOnlyList<Guid> AdminGroupIds
    {
        get => _adminGroupIds;
        init => _adminGroupIds = value is null ? null! : Array.AsReadOnly(value.ToArray());
    }

    /// <summary>True if the user has any admin authority at any level.</summary>
    public bool HasAnyAuthority => IsInstanceAdmin
        || AdminTenantIds.Count > 0
        || AdminOrganizationIds.Count > 0
        || AdminGroupIds.Count > 0;
}
