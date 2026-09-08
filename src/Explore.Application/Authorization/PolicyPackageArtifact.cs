namespace Explore.Application.Authorization;

/// <summary>
/// Metadata for a single artifact in an authorization policy package.
/// </summary>
/// <param name="LogicalId">Stable package-local artifact identifier.</param>
/// <param name="Kind">Artifact classification.</param>
/// <param name="Sha256">Lowercase SHA-256 hex digest of the canonical artifact content.</param>
/// <param name="SizeInBytes">Canonical artifact content size in bytes.</param>
/// <param name="Metadata">Additional provider-neutral metadata for diagnostics.</param>
public sealed record PolicyPackageArtifact(
    string LogicalId,
    PolicyArtifactKind Kind,
    string Sha256,
    long SizeInBytes,
    IReadOnlyDictionary<string, string> Metadata);
