namespace Explore.Blazor.Client.Models;

/// <summary>
/// Represents the current tenant context for the application.
/// This is provided via cascading parameter throughout the component tree.
/// </summary>
public class TenantContext
{
    /// <summary>
    /// The current tenant ID (Guid). Null if user is not authenticated.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Indicates whether the user is currently authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

}
