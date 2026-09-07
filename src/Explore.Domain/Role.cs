using System;
using System.Collections.Generic;
using System.Text;
using Explore.Domain.Enums;

namespace Explore.Domain;

public class Role
{
    public int Id { get; set; }
    public required string MasterCode { get; set; }
    public required string FullName { get; set; }
    public string? Description { get; set; }

    /// <summary>
    /// The scope at which this role applies: Platform, Tenant, Organization, Group, or Event.
    /// </summary>
    public int RoleScopeId { get; set; }
    public RoleScope RoleScope { get; set; } = null!;

    public RoleScopeEnum Scope
    {
        get => (RoleScopeEnum)RoleScopeId;
        set => RoleScopeId = (int)value;
    }

    /// <summary>
    /// Prevents deletion of built-in system roles.
    /// Custom roles created by admins have IsSystem = false.
    /// </summary>
    public bool IsSystem { get; set; }
}
