using Explore.Domain;

namespace Explore.Application.Contracts.Moderation;

public enum ModerationCaseIntakeCommitOutcome
{
    Accepted = 1,
    Conflict = 2,
    Unavailable = 3
}

public sealed record ModerationCaseIntakeCommitResult(
    ModerationCaseIntakeCommitOutcome Outcome, ModerationCaseIntakeAcknowledgement? Acknowledgement = null)
{
    public override string ToString() => nameof(ModerationCaseIntakeCommitResult);
}

/// <summary>Commits qualified allegation ownership, receipt consumption and original acknowledgement together.</summary>
public interface IModerationCaseIntakeCommitter
{
    /// <summary>
    /// Issues an account-bound submission receipt through the native target owner, saves and
    /// rechecks its original authority, then commits before exposing its opaque token.
    /// A failed or uncertain commit returns no receipt and grants no case-read permission.
    /// </summary>
    Task<ModerationCaseIssuedReceipt?> IssueSubmissionReceiptAsync(
        ModerationCaseTargetIssueRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Commits a read token only for the caller's independently issued current case/material grant.
    /// Submission acceptance is never a substitute for that grant or current native read authority.
    /// </summary>
    Task<ModerationCaseIssuedReceipt?> IssueReadReceiptAsync(
        Guid readGrantId, CancellationToken cancellationToken);

    Task<ModerationCaseIntakeCommitResult> SubmitAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken);
}
