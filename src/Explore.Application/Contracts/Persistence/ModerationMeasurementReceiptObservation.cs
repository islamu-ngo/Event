namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Cryptographic authenticity and current native source binding of one retained accepted assertion.
/// The receipt's claims remain issuer assertions: this observation supplies no passing verdict,
/// catalogue completeness, honest-execution inference or permission to activate or restrict.
/// Original source identities and generations are preserved independently of the current epoch.
/// Only public trust metadata is exposed; the retained signed token stays with its native owner.
/// </summary>
public sealed record ModerationMeasurementReceiptObservation(
    Guid TransactionId, ModerationMeasurementReceipt Receipt, string TokenDigest,
    long Revision, long WriterGeneration, Guid AcceptedByHumanId, DateTimeOffset AcceptedAt,
    Guid PreparedByHumanId, long ChallengeWriterGeneration, DateTimeOffset PreparedAt,
    Guid IssuerEnrollmentId, long IssuerWriterGeneration, Guid ApprovedByHumanId,
    DateTimeOffset ApprovedAt, ModerationMeasurementTrustKey TrustKey, ModerationConsumerRuntimeObservation Runtime,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(ModerationMeasurementReceiptObservation);
}
