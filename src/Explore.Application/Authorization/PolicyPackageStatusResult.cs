namespace Explore.Application.Authorization;

/// <param name="ContentHash">Hash of the package this deployment believes it published.</param>
/// <param name="ObservedRevision">
/// Revision actually observed in the provider's policy store, or <c>null</c> when it could not be read.
/// This is what makes drift visible: <paramref name="ContentHash"/> is what we shipped,
/// <paramref name="ObservedRevision"/> is what the PDP is serving.
/// </param>
public sealed record PolicyPackageStatusResult(
    string PackageId,
    string ContentHash,
    DateTimeOffset CheckedAt,
    PolicyPackageIssueCode IssueCode,
    string Message,
    IReadOnlyList<string> Warnings,
    string? ObservedRevision = null)
{
    /// <summary>
    /// Whether the package is verifiably in force.
    /// <para>
    /// <see cref="PolicyPackageIssueCode.PackageStatusUnknown"/> is deliberately <em>not</em> healthy.
    /// It used to be, back when a local evaluator answered around an unreachable store. Nothing answers
    /// around it now, so "we could not check" is an unresolved risk, not a clean bill of health.
    /// </para>
    /// </summary>
    public bool IsHealthy => IssueCode is PolicyPackageIssueCode.None;
}
