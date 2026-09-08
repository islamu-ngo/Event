using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Admissions;

public enum AdmissionCheckInAction
{
    CheckIn = 1,
    Undo = 2
}

public enum AdmissionCheckInOutcome
{
    CheckedIn = 1,
    AlreadyCheckedIn = 2,
    Undone = 3,
    NotCheckedIn = 4,
    Rejected = 5,
    Unavailable = 6
}

public enum AdmissionCheckInBatchOutcome
{
    Completed = 1,
    BatchLimitExceeded = 2
}

public enum AdmissionCheckInAuthorizationOutcome
{
    Authorized = 1,
    Denied = 2
}

public sealed record AdmissionCheckInRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    string Credential,
    AdmissionCheckInAction Action,
    AdmissionCheckInUndoReasonCodeEnum? ReasonCode,
    Guid? StaffActorId,
    Guid? ScannerCapabilityId,
    Guid? CheckInId = null)
{
    public override string ToString() =>
        $"AdmissionCheckInRequest(action={Action}, tenant={TenantId}, event={EventId}, target={TargetId}, <redacted>)";
}

public sealed record AdmissionCheckInBatchItem(
    int Index,
    string Credential,
    AdmissionCheckInAction Action,
    AdmissionCheckInUndoReasonCodeEnum? ReasonCode,
    Guid? CheckInId = null)
{
    public override string ToString() =>
        $"AdmissionCheckInBatchItem(index={Index}, action={Action}, <redacted>)";
}

public sealed record AdmissionCheckInBatchRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    Guid? StaffActorId,
    Guid? ScannerCapabilityId,
    IReadOnlyList<AdmissionCheckInBatchItem> Items)
{
    public override string ToString() =>
        $"AdmissionCheckInBatchRequest(tenant={TenantId}, event={EventId}, target={TargetId}, items={Items.Count}, <redacted>)";
}

public sealed record AdmissionCheckInResult(
    AdmissionCheckInOutcome Outcome,
    Guid TargetId,
    DateTimeOffset OccurredAtUtc,
    Guid? CheckInId);

public sealed record AdmissionCheckInBatchItemResult(
    int Index,
    AdmissionCheckInOutcome Outcome,
    Guid TargetId,
    DateTimeOffset OccurredAtUtc,
    Guid? CheckInId);

public sealed record AdmissionCheckInBatchResult(
    AdmissionCheckInBatchOutcome Outcome,
    IReadOnlyList<AdmissionCheckInBatchItemResult> Items);

public sealed record AdmissionCheckInCredentialDigestRequest(
    Guid TenantId,
    string Credential)
{
    public override string ToString() =>
        $"AdmissionCheckInCredentialDigestRequest(tenant={TenantId}, <redacted>)";
}

public sealed record AdmissionCheckInCredentialDigestCandidate(
    string LookupDigest,
    int KeyVersion)
{
    public override string ToString() =>
        $"AdmissionCheckInCredentialDigestCandidate(keyVersion={KeyVersion}, <redacted>)";
}

public sealed record AdmissionCheckInCredentialDigest(
    IReadOnlyList<AdmissionCheckInCredentialDigestCandidate> Candidates)
{
    public override string ToString() =>
        $"AdmissionCheckInCredentialDigest(candidates={Candidates.Count}, <redacted>)";
}

public sealed record AdmissionCheckInAuthorizationRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    AdmissionCheckInAction Action,
    Guid? StaffActorId,
    Guid? ScannerCapabilityId,
    DateTimeOffset OccurredAtUtc)
{
    public override string ToString() =>
        $"AdmissionCheckInAuthorizationRequest(action={Action}, tenant={TenantId}, event={EventId}, target={TargetId}, <redacted>)";
}

public sealed record AdmissionCheckInAuthorizationDecision(
    AdmissionCheckInAuthorizationOutcome Outcome,
    AdmissionTargetTypeEnum? TargetType = null);

public sealed record AdmissionCheckInTransactionRequest(
    Guid TenantId,
    Guid EventId,
    Guid TargetId,
    IReadOnlyList<AdmissionCheckInCredentialDigestCandidate> CredentialDigestCandidates,
    AdmissionCheckInAction Action,
    AdmissionCheckInUndoReasonCodeEnum? ReasonCode,
    Guid? StaffActorId,
    Guid? ScannerCapabilityId,
    DateTimeOffset OccurredAtUtc,
    Guid? CheckInId = null)
{
    public override string ToString() =>
        $"AdmissionCheckInTransactionRequest(action={Action}, tenant={TenantId}, event={EventId}, target={TargetId}, candidates={CredentialDigestCandidates.Count}, <redacted>)";
}

public interface IAdmissionCheckInCredentialDigestService
{
    Task<AdmissionCheckInCredentialDigest> DigestAsync(
        AdmissionCheckInCredentialDigestRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionCheckInAuthority
{
    Task<AdmissionCheckInAuthorizationDecision> AuthorizeAsync(
        AdmissionCheckInAuthorizationRequest request,
        CancellationToken cancellationToken);
}

public interface IAdmissionCheckInTransaction
{
    Task<AdmissionCheckInDecision?> ExecuteAsync(
        AdmissionCheckInTransactionRequest request,
        CancellationToken cancellationToken);
}
