namespace Explore.Application.Authorization;

/// <summary>
/// Lifecycle phase markers carried in authorization resource attributes.
/// </summary>
public static class AuthorizationPhases
{
    /// <summary>
    /// The resource row does not exist yet; the check is authorized against parent/context attributes.
    /// </summary>
    public const string PreCreate = "pre_create";
}
