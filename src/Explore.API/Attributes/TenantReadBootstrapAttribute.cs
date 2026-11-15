namespace Explore.API.Attributes;

/// <summary>
/// Identifies an exact bootstrap or capability action granting no general tenant-content authority.
/// Authentication, lifecycle and the action's own capability checks remain authoritative.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class TenantReadBootstrapAttribute : Attribute;
