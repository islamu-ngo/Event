namespace Explore.Application.Authorization;

/// <summary>
/// Marks an operation with authorization requirements.
/// The native authorization decorator reads this attribute to determine
/// the resource kind and action for the authorization check.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuthorizeResourceAttribute : Attribute
{
    /// <summary>
    /// The resource kind (e.g., <see cref="ResourceKinds.InstanceSetting"/>, <see cref="ResourceKinds.TenantSetting"/>).
    /// </summary>
    public string Resource { get; }

    /// <summary>
    /// The action being performed (e.g., "update", "delete").
    /// </summary>
    public string Action { get; }

    public AuthorizeResourceAttribute(string resource, string action)
    {
        Resource = resource;
        Action = action;
    }

}
