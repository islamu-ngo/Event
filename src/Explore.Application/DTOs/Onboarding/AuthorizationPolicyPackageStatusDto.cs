namespace Explore.Application.DTOs.Onboarding;

/// <summary>
/// What an operator needs to answer "is authorization enforcing the policy I think it is?".
/// </summary>
/// <param name="Provider">Which provider decides — <c>local</c> or <c>cerbos</c>.</param>
/// <param name="PackageId">Identifier of the policy package this deployment ships.</param>
/// <param name="PackageContentHash">Hash of the package this deployment believes it published.</param>
/// <param name="ObservedRevision">
/// Revision read from the provider's policy store, or <c>null</c> when it could not be established.
/// Compare against the previously observed value: a change nobody published is drift.
/// </param>
/// <param name="RevisionCertain">
/// Whether <paramref name="ObservedRevision"/> was actually observed for operator drift diagnostics.
/// </param>
/// <param name="IsHealthy">Whether the package is verifiably in force.</param>
/// <param name="IssueCode">Machine-readable classification of the problem, if any.</param>
/// <param name="Message">Human-readable summary of what was found.</param>
/// <param name="Warnings">Caveats that do not by themselves make the package unhealthy.</param>
/// <param name="RecoveryAction">The next thing an operator should actually do.</param>
/// <param name="CheckedAt">When the store was inspected.</param>
public sealed record AuthorizationPolicyPackageStatusDto(
    string Provider,
    string PackageId,
    string PackageContentHash,
    string? ObservedRevision,
    bool RevisionCertain,
    bool IsHealthy,
    string IssueCode,
    string Message,
    IReadOnlyList<string> Warnings,
    string RecoveryAction,
    DateTimeOffset CheckedAt);
