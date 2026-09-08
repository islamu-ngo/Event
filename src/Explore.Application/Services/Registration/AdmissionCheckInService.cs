using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Services.Registration;

public sealed class AdmissionCheckInService(
    IAdmissionCheckInTransaction transaction,
    IAdmissionCheckInCredentialDigestService credentialDigestService,
    IAdmissionCheckInAuthority authority,
    IAdmissionCheckInTelemetry telemetry,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public const int MaximumBatchSize = 100;
    private const int MaximumCredentialLength = 512;
    private const int MaximumDigestCandidates = 8;

    public async Task<AdmissionCheckInResult> ProcessAsync(
        AdmissionCheckInRequest request,
        CancellationToken cancellationToken)
    {
        long startedAt = timeProvider.GetTimestamp();
        if (!IsValid(request))
        {
            return Complete(
                Rejected(request?.TargetId ?? Guid.Empty),
                request,
                null,
                startedAt);
        }

        DateTimeOffset occurredAtUtc = timeProvider.GetUtcNow();
        try
        {
            AdmissionCheckInAuthorizationDecision decision = await authority.AuthorizeAsync(
                new AdmissionCheckInAuthorizationRequest(
                    request.TenantId,
                    request.EventId,
                    request.TargetId,
                    request.Action,
                    request.StaffActorId,
                    request.ScannerCapabilityId,
                    occurredAtUtc),
                cancellationToken);
            if (decision.Outcome != AdmissionCheckInAuthorizationOutcome.Authorized)
            {
                return Complete(Rejected(request.TargetId), request, decision.TargetType, startedAt);
            }

            AdmissionCheckInCredentialDigest digest = await credentialDigestService.DigestAsync(
                new AdmissionCheckInCredentialDigestRequest(request.TenantId, request.Credential),
                cancellationToken);
            if (!ValidCandidates(digest.Candidates))
            {
                return Complete(Rejected(request.TargetId), request, decision.TargetType, startedAt);
            }

            AdmissionCheckInDecision? persisted = await unitOfWork.ExecuteInTransactionAsync(
                token => transaction.ExecuteAsync(
                    new AdmissionCheckInTransactionRequest(
                        request.TenantId,
                        request.EventId,
                        request.TargetId,
                        digest.Candidates.ToArray(),
                        request.Action,
                        request.ReasonCode,
                        request.StaffActorId,
                        request.ScannerCapabilityId,
                        occurredAtUtc,
                        request.CheckInId),
                    token),
                cancellationToken);

            AdmissionCheckInOutcome outcome = ToPublicOutcome(persisted?.ResultCode);
            Guid? checkInId = outcome switch
            {
                AdmissionCheckInOutcome.CheckedIn or AdmissionCheckInOutcome.AlreadyCheckedIn =>
                    persisted?.NextState.ActiveCheckInEventId,
                AdmissionCheckInOutcome.Undone => request.CheckInId,
                _ => null
            };
            return Complete(
                new AdmissionCheckInResult(
                    outcome,
                    request.TargetId,
                    occurredAtUtc,
                    checkInId),
                request,
                decision.TargetType,
                startedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AdmissionCheckInUnavailableException)
        {
            RecordUnavailable(request, null, startedAt);
            throw;
        }
        catch (Exception)
        {
            RecordUnavailable(request, null, startedAt);
            throw new AdmissionCheckInUnavailableException();
        }
    }

    public async Task<AdmissionCheckInBatchResult> ProcessBatchAsync(
        AdmissionCheckInBatchRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Items is null || request.Items.Count is < 1 or > MaximumBatchSize)
        {
            telemetry.RecordSaturation(
                AdmissionCheckInSaturationKind.BatchLimit,
                AdmissionCheckInTelemetryOutcome.Rejected);
            return new AdmissionCheckInBatchResult(
                AdmissionCheckInBatchOutcome.BatchLimitExceeded,
                []);
        }

        AdmissionCheckInAuthorityKind authorityKind =
            AuthorityKind(request.StaffActorId, request.ScannerCapabilityId);
        telemetry.RecordBatch(authorityKind, null, request.Items.Count);
        long remaining = request.Items.Count;
        telemetry.RecordBacklog(AdmissionCheckInBacklogKind.Transaction, null, remaining);
        var results = new List<AdmissionCheckInBatchItemResult>(request.Items.Count);
        try
        {
            foreach (AdmissionCheckInBatchItem item in request.Items)
            {
                AdmissionCheckInResult result = await ProcessAsync(
                    new AdmissionCheckInRequest(
                        request.TenantId,
                        request.EventId,
                        request.TargetId,
                        item.Credential,
                        item.Action,
                        item.ReasonCode,
                        request.StaffActorId,
                        request.ScannerCapabilityId,
                        item.CheckInId),
                    cancellationToken);
                results.Add(new AdmissionCheckInBatchItemResult(
                    item.Index,
                    result.Outcome,
                    result.TargetId,
                    result.OccurredAtUtc,
                    result.CheckInId));
                telemetry.RecordBacklog(
                    AdmissionCheckInBacklogKind.Transaction,
                    null,
                    --remaining);
            }
        }
        finally
        {
            telemetry.RecordBacklog(AdmissionCheckInBacklogKind.Transaction, null, 0);
        }

        return new AdmissionCheckInBatchResult(AdmissionCheckInBatchOutcome.Completed, results);
    }

    private static bool IsValid(AdmissionCheckInRequest? request) =>
        request is not null &&
        request.TenantId != Guid.Empty &&
        request.EventId != Guid.Empty &&
        request.TargetId != Guid.Empty &&
        !string.IsNullOrWhiteSpace(request.Credential) &&
        request.Credential.Length <= MaximumCredentialLength &&
        Enum.IsDefined(request.Action) &&
        request.StaffActorId.HasValue != request.ScannerCapabilityId.HasValue &&
        (request.Action == AdmissionCheckInAction.CheckIn
            ? request.CheckInId is null
            : request.CheckInId is Guid checkInId &&
              checkInId != Guid.Empty &&
              checkInId.Version == 7) &&
        (request.Action == AdmissionCheckInAction.CheckIn
            ? request.ReasonCode is null
            : request.ReasonCode.HasValue && Enum.IsDefined(request.ReasonCode.Value));

    private static bool ValidCandidates(IReadOnlyList<AdmissionCheckInCredentialDigestCandidate>? candidates) =>
        candidates is { Count: > 0 and <= MaximumDigestCandidates } &&
        candidates.All(candidate =>
            candidate.KeyVersion > 0 &&
            !string.IsNullOrWhiteSpace(candidate.LookupDigest) &&
            candidate.LookupDigest.Length <= 256) &&
        candidates.Select(candidate => (candidate.KeyVersion, candidate.LookupDigest))
            .Distinct()
            .Count() == candidates.Count;

    private static AdmissionCheckInOutcome ToPublicOutcome(AdmissionCheckInResultCodeEnum? resultCode) =>
        resultCode switch
        {
            AdmissionCheckInResultCodeEnum.CheckedIn or AdmissionCheckInResultCodeEnum.ReEntered =>
                AdmissionCheckInOutcome.CheckedIn,
            AdmissionCheckInResultCodeEnum.AlreadyCheckedIn => AdmissionCheckInOutcome.AlreadyCheckedIn,
            AdmissionCheckInResultCodeEnum.Undone => AdmissionCheckInOutcome.Undone,
            AdmissionCheckInResultCodeEnum.NotCheckedIn => AdmissionCheckInOutcome.NotCheckedIn,
            _ => AdmissionCheckInOutcome.Rejected
        };

    private AdmissionCheckInResult Complete(
        AdmissionCheckInResult result,
        AdmissionCheckInRequest? request,
        AdmissionTargetTypeEnum? targetType,
        long startedAt)
    {
        telemetry.RecordOperation(
            request?.Action ?? default,
            AuthorityKind(request?.StaffActorId, request?.ScannerCapabilityId),
            targetType,
            ToTelemetryOutcome(result.Outcome),
            timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);
        return result;
    }

    private void RecordUnavailable(
        AdmissionCheckInRequest? request,
        AdmissionTargetTypeEnum? targetType,
        long startedAt) => telemetry.RecordOperation(
        request?.Action ?? default,
        AuthorityKind(request?.StaffActorId, request?.ScannerCapabilityId),
        targetType,
        AdmissionCheckInTelemetryOutcome.Unavailable,
        timeProvider.GetElapsedTime(startedAt).TotalMilliseconds);

    private static AdmissionCheckInAuthorityKind AuthorityKind(Guid? staffActorId, Guid? scannerCapabilityId) =>
        staffActorId.HasValue && !scannerCapabilityId.HasValue
            ? AdmissionCheckInAuthorityKind.Staff
            : !staffActorId.HasValue && scannerCapabilityId.HasValue
                ? AdmissionCheckInAuthorityKind.Scanner
                : AdmissionCheckInAuthorityKind.Unknown;

    private static AdmissionCheckInTelemetryOutcome ToTelemetryOutcome(AdmissionCheckInOutcome outcome) => outcome switch
    {
        AdmissionCheckInOutcome.CheckedIn => AdmissionCheckInTelemetryOutcome.CheckedIn,
        AdmissionCheckInOutcome.AlreadyCheckedIn => AdmissionCheckInTelemetryOutcome.AlreadyCheckedIn,
        AdmissionCheckInOutcome.Undone => AdmissionCheckInTelemetryOutcome.Undone,
        AdmissionCheckInOutcome.NotCheckedIn => AdmissionCheckInTelemetryOutcome.NotCheckedIn,
        _ => AdmissionCheckInTelemetryOutcome.Rejected
    };

    private static AdmissionCheckInResult Rejected(Guid targetId) =>
        new(AdmissionCheckInOutcome.Rejected, targetId, default, null);
}
