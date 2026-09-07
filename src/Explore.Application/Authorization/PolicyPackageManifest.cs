namespace Explore.Application.Authorization;

/// <summary>
/// Provider-neutral manifest describing an authorization policy package.
/// </summary>
/// <param name="PackageId">Stable product-owned package identifier.</param>
/// <param name="Version">Content or semantic version for the package.</param>
/// <param name="ContentHash">Lowercase SHA-256 hex digest over the canonical artifact set.</param>
/// <param name="GeneratedAt">UTC timestamp when this manifest was generated.</param>
/// <param name="Artifacts">Artifacts included in the package.</param>
public sealed record PolicyPackageManifest(
    string PackageId,
    string Version,
    string ContentHash,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PolicyPackageArtifact> Artifacts);
