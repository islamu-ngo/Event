using Explore.Domain.Federation;

namespace Explore.Application.Contracts.Infrastructure;

public sealed record AtprotoPdsDeliveryRequest(
    Guid TenantId,
    Guid UserId,
    string Did,
    Uri PdsHost,
    string Collection,
    string RecordKey,
    PdsSyncOperation Operation,
    string? Payload,
    string? ExpectedCid,
    IReadOnlyList<string>? CompensationBasePayloads = null,
    IReadOnlyList<string>? CompensationBaseCids = null,
    bool CompensationEvidenceComplete = true);

public sealed record AtprotoPdsDeliveryResult(
    bool Succeeded,
    string? Uri,
    string? Cid,
    bool Retryable,
    string? FailureCode,
    string? ObservedBaseCid)
{
    public const string AbsentRecordCid = "atproto-record-absent";

    public static AtprotoPdsDeliveryResult Success(string uri, string cid, string? observedBaseCid = null) =>
        new(true, uri, cid, false, null, observedBaseCid);

    public static AtprotoPdsDeliveryResult SuccessAbsent(string uri) =>
        new(true, uri, AbsentRecordCid, false, null, null);

    public static AtprotoPdsDeliveryResult Failed(string failureCode, bool retryable) =>
        new(false, null, null, retryable, failureCode, null);
}

public interface IAtprotoPdsDeliveryGateway
{
    Task<AtprotoPdsDeliveryResult> DeliverAsync(
        AtprotoPdsDeliveryRequest command,
        CancellationToken cancellationToken);
}
