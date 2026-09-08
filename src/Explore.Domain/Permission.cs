using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public class Permission : IAuditableEntity
{
    public int Id { get; set; }

    /// <summary>
    /// The resource type this permission applies to (e.g., "event", "organization").
    /// Matches Cerbos resource kind identifiers.
    /// </summary>
    public required string ResourceKind { get; set; }

    /// <summary>
    /// The action this permission grants (e.g., "create", "update", "delete", "read").
    /// </summary>
    public required string Action { get; set; }

    /// <summary>
    /// Optional field-level scope for granular permissions (e.g., "description", "title").
    /// Null means the permission applies to the entire resource.
    /// </summary>
    public string? FieldScope { get; set; }

    /// <summary>
    /// Unique identifier following the format: {resource_kind}:{action} or {resource_kind}:{action}:{field}.
    /// Examples: "event:update", "event:update:description", "organization_member:create".
    /// </summary>
    public required string MasterCode { get; set; }

    /// <summary>
    /// Human-readable display name (e.g., "Edit Event Description").
    /// </summary>
    public required string FullName { get; set; }

    /// <summary>
    /// Optional detailed description of what this permission grants.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// UI grouping category (e.g., "Events", "Organizations", "Settings").
    /// </summary>
    public required string GroupName { get; set; }

    /// <summary>
    /// Which scope level can use this permission (Platform, Tenant, Organization, Group, or Event).
    /// </summary>
    public int RoleScopeId { get; set; }
    public RoleScope RoleScope { get; set; } = null!;

    public RoleScopeEnum Scope
    {
        get => (RoleScopeEnum)RoleScopeId;
        set => RoleScopeId = (int)value;
    }

    /// <summary>
    /// Prevents deletion of built-in system permissions.
    /// </summary>
    public bool IsSystem { get; set; }

    /// <summary>
    /// Hides this permission from tenant/org admins (capability ceiling).
    /// Only super-admins can see and assign filtered permissions.
    /// </summary>
    public bool IsFiltered { get; set; }

    /// <summary>
    /// Soft-disable without deletion. Inactive permissions are not evaluated.
    /// </summary>
    public bool IsActive { get; set; }

    // Audit fields (IAuditableEntity)
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
