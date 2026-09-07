namespace Explore.Application.Authorization;

/// <summary>
/// Summary metadata for the current set of dynamic Cerbos policies generated from
/// <c>Role</c> and <c>RolePermission</c> tables. Used for observability, staleness
/// detection, and operator diagnostics.
/// </summary>
/// <param name="RoleCount">Total number of roles evaluated (including those with zero permissions).</param>
/// <param name="PolicyCount">Number of derived role policies actually generated (roles with at least one permission).</param>
/// <param name="TotalPermissionCount">Sum of all permissions across all generated policies.</param>
/// <param name="ContentHash">SHA-256 hex digest of the serialized policy bundle. Changes when any permission changes.</param>
/// <param name="GeneratedAt">UTC timestamp when this summary was computed.</param>
public sealed record PolicyPackageInfo(
    int RoleCount,
    int PolicyCount,
    int TotalPermissionCount,
    string ContentHash,
    DateTimeOffset GeneratedAt);
