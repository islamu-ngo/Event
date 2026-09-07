namespace Explore.Application.Authorization;

/// <summary>
/// Result of publishing an authorization policy package to the configured provider.
/// </summary>
/// <param name="Succeeded">Whether every required publish phase succeeded.</param>
/// <param name="PackageId">Published package identifier.</param>
/// <param name="ContentHash">Content hash that was attempted.</param>
/// <param name="Message">Operator-safe status message.</param>
/// <param name="PublishedAt">UTC timestamp when publishing completed.</param>
/// <param name="Warnings">Operator-safe warnings from non-fatal phases.</param>
public sealed record PolicyPackagePublishResult(
    bool Succeeded,
    string PackageId,
    string ContentHash,
    string Message,
    DateTimeOffset PublishedAt,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Provider-neutral issue code for the most important publish outcome.
    /// </summary>
    public PolicyPackageIssueCode IssueCode { get; init; } = PolicyPackageIssueCode.None;
}
