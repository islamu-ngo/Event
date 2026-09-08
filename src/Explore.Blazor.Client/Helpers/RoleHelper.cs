using Explore.Blazor.Client.Clients;
using MudBlazor;

namespace Explore.Blazor.Client.Helpers;

/// <summary>
/// Provides consistent role display logic using unified Role IDs.
/// IDs match the RoleEnum values from the Domain layer (Organization scope: 20-25).
/// </summary>
public static class RoleHelper
{
    // Organization-scope role IDs matching RoleEnum
    public const int OrgCreator = 20;
    public const int OrgCoOwner = 21;
    public const int OrgAdmin = 22;
    public const int OrgModerator = 23;
    public const int OrgMember = 24;
    public const int OrgViewer = 25;

    // Event-scope role IDs matching RoleEnum
    public const int EventOwner = 41;
    public const int EventManager = 42;
    public const int RegistrationManager = 43;
    public const int CheckInStaff = 44;

    // Group-scope role IDs matching RoleEnum
    public const int GroupCreator = 30;
    public const int GroupAdmin = 31;
    public const int GroupModerator = 32;
    public const int GroupMember = 33;

    /// <summary>
    /// Checks if the given role ID has management permissions (Creator, CoOwner, or Admin).
    /// </summary>
    public static bool CanManage(int? roleId)
    {
        if (!roleId.HasValue) return false;
        return roleId.Value is OrgCreator or OrgCoOwner or OrgAdmin;
    }

    public static bool CanManage(RoleEnum? role) => ToRoleId(role) is { } roleId && CanManage(roleId);

    /// <summary>
    /// Checks if the given role ID has group management permissions (Creator or Admin).
    /// </summary>
    public static bool CanManageGroup(int? roleId)
    {
        if (!roleId.HasValue) return false;
        return roleId.Value is GroupCreator or GroupAdmin;
    }

    public static bool CanManageGroup(RoleEnum? role) => ToRoleId(role) is { } roleId && CanManageGroup(roleId);

    /// <summary>
    /// Returns a human-readable name for the organization role.
    /// </summary>
    public static string GetRoleName(int? roleId) => roleId switch
    {
        OrgCreator => "Creator",
        OrgCoOwner => "Co-Owner",
        OrgAdmin => "Admin",
        OrgModerator => "Moderator",
        OrgMember => "Member",
        OrgViewer => "Viewer",
        _ => "Unknown"
    };

    public static string GetRoleName(RoleEnum? role) => GetRoleName(ToRoleId(role));

    /// <summary>
    /// Returns a MudBlazor Color for the organization role badge.
    /// </summary>
    public static Color GetRoleColor(int? roleId) => roleId switch
    {
        OrgCreator => Color.Primary,
        OrgCoOwner => Color.Secondary,
        OrgAdmin => Color.Info,
        OrgModerator => Color.Warning,
        OrgMember => Color.Success,
        OrgViewer => Color.Default,
        _ => Color.Default
    };

    public static Color GetRoleColor(RoleEnum? role) => GetRoleColor(ToRoleId(role));

    /// <summary>
    /// Returns all assignable organization roles (excludes Creator).
    /// </summary>
    public static IReadOnlyList<(int Id, string Name)> GetAssignableOrgRoles() =>
    [
        (OrgAdmin, "Admin"),
        (OrgModerator, "Moderator"),
        (OrgMember, "Member")
    ];

    /// <summary>
    /// Returns all organization roles (including Creator) for filter dropdowns.
    /// </summary>
    public static IReadOnlyList<(int Id, string Name)> GetAllOrgRoles() =>
    [
        (OrgCreator, "Creator"),
        (OrgCoOwner, "Co-Owner"),
        (OrgAdmin, "Admin"),
        (OrgModerator, "Moderator"),
        (OrgMember, "Member"),
        (OrgViewer, "Viewer")
    ];

    /// <summary>
    /// Returns a human-readable name for the group role.
    /// </summary>
    public static string GetGroupRoleName(int? roleId) => roleId switch
    {
        GroupCreator => "Creator",
        GroupAdmin => "Admin",
        GroupModerator => "Moderator",
        GroupMember => "Member",
        _ => "Unknown"
    };

    public static string GetGroupRoleName(RoleEnum? role) => GetGroupRoleName(ToRoleId(role));

    /// <summary>
    /// Returns a MudBlazor Color for the group role badge.
    /// </summary>
    public static Color GetGroupRoleColor(int? roleId) => roleId switch
    {
        GroupCreator => Color.Primary,
        GroupAdmin => Color.Info,
        GroupModerator => Color.Warning,
        GroupMember => Color.Success,
        _ => Color.Default
    };

    public static Color GetGroupRoleColor(RoleEnum? role) => GetGroupRoleColor(ToRoleId(role));

    /// <summary>
    /// Returns assignable group roles for group membership administration.
    /// </summary>
    public static IReadOnlyList<(int Id, string Name)> GetAssignableGroupRoles() =>
    [
        (GroupAdmin, "Admin"),
        (GroupModerator, "Moderator"),
        (GroupMember, "Member")
    ];

    public static string GetEventRoleName(int? roleId) => roleId switch
    {
        EventOwner => "Owner",
        EventManager => "Manager",
        RegistrationManager => "Registration Manager",
        CheckInStaff => "Check-in Staff",
        _ => "Unknown"
    };

    public static Color GetEventRoleColor(int? roleId) => roleId switch
    {
        EventOwner => Color.Primary,
        EventManager => Color.Info,
        RegistrationManager => Color.Success,
        CheckInStaff => Color.Warning,
        _ => Color.Default
    };

    public static IReadOnlyList<(int Id, string Name)> GetAllEventRoles() =>
    [
        (EventOwner, "Owner"),
        (EventManager, "Manager"),
        (RegistrationManager, "Registration Manager"),
        (CheckInStaff, "Check-in Staff")
    ];

    public static RoleEnum? ToRoleEnum(int? roleId) => roleId switch
    {
        OrgAdmin => RoleEnum.OrgAdmin,
        OrgModerator => RoleEnum.OrgModerator,
        OrgMember => RoleEnum.OrgMember,
        GroupAdmin => RoleEnum.GroupAdmin,
        GroupModerator => RoleEnum.GroupModerator,
        GroupMember => RoleEnum.GroupMember,
        _ => null
    };

    public static int? ToRoleId(RoleEnum? role) => role switch
    {
        RoleEnum.Admin => 1,
        RoleEnum.Moderator => 2,
        RoleEnum.Member => 4,
        RoleEnum.TenantAdmin => 11,
        RoleEnum.TenantModerator => 12,
        RoleEnum.TenantMember => 13,
        RoleEnum.OrgAdmin => OrgAdmin,
        RoleEnum.OrgModerator => OrgModerator,
        RoleEnum.OrgMember => OrgMember,
        RoleEnum.GroupAdmin => GroupAdmin,
        RoleEnum.GroupModerator => GroupModerator,
        RoleEnum.GroupMember => GroupMember,
        RoleEnum.EventOwner => 41,
        RoleEnum.EventManager => 42,
        RoleEnum.RegistrationManager => 43,
        RoleEnum.CheckInStaff => 44,
        _ => null
    };
}
