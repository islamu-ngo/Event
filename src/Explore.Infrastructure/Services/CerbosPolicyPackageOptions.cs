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

    public CerbosPublicationOptions Publication { get; set; } = new();
}

/// <summary>Deployment-owned public verification material. Never contains the operator signing key.</summary>
public sealed class CerbosPublicationOptions
{
    public string ReceiptBase64 { get; set; } = string.Empty;
    public string SignatureBase64 { get; set; } = string.Empty;
    public string PublicKeyPem { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
}
