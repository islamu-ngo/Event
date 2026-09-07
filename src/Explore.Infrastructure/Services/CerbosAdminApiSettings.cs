namespace Explore.Infrastructure.Services;

/// <summary>
/// Configuration for Cerbos Admin API access. Used by the policy package publisher
/// to upload policy/schema bundles and broadcast reload commands to Cerbos instances.
/// </summary>
public class CerbosAdminApiSettings
{
    public const string SectionName = "Cerbos:AdminApi";

    /// <summary>
    /// List of all Cerbos instance URLs for reload broadcast.
    /// Each entry is a base URL (e.g., "http://cerbos-1:3592").
    /// </summary>
    public List<string> Endpoints { get; set; } = [];

    /// <summary>
    /// Admin API username for Basic Auth.
    /// </summary>
    public string AdminUsername { get; set; } = string.Empty;

    /// <summary>
    /// Admin API password for Basic Auth.
    /// </summary>
    public string AdminPassword { get; set; } = string.Empty;
}
