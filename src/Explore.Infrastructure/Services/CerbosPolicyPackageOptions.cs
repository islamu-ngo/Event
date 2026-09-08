namespace Explore.Infrastructure.Services;

/// <summary>
/// Options for reading the bundled Cerbos policy package from disk.
/// </summary>
public sealed class CerbosPolicyPackageOptions
{
    public const string SectionName = "Cerbos:PolicyPackage";

    /// <summary>
    /// Directory containing Cerbos policy files and the _schemas directory.
    /// Relative paths are resolved against the current process directory.
    /// </summary>
    public string PoliciesPath { get; set; } = "cerbos/policies";

    /// <summary>
    /// Required prefix for product-owned policy and schema artifacts.
    /// </summary>
    public string ProductNamespacePrefix { get; set; } = "islamuevent_";

    /// <summary>
    /// Stable provider-neutral package identifier surfaced in manifests and publish results.
    /// </summary>
    public string PackageId { get; set; } = "islamuevent-authorization-policies";

    /// <summary>
    /// Maximum policy documents sent in one Admin API request.
    /// </summary>
    public int MaxPoliciesPerRequest { get; set; } = 100;

    /// <summary>
    /// Allows tenant-supplied Admin API endpoints to use http rather than https.
    /// Intended for local development only; production BYO endpoints should remain HTTPS-only.
    /// </summary>
    public bool AllowInsecureByoAdminEndpoints { get; set; }

    /// <summary>
    /// Allows tenant-supplied Admin API endpoints to target loopback, private, or link-local addresses.
    /// Intended for controlled tests or local development only.
    /// </summary>
    public bool AllowPrivateByoAdminEndpoints { get; set; }
}
