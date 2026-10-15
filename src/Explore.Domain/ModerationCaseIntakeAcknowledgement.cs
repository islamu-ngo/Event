using System.Security.Cryptography;
using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

/// <summary>
/// Immutable acceptance of one confidential allegation request. It retains original receipt and
/// material ownership, not current case status, attributed guilt or permission to disclose a target.
/// </summary>
public sealed class ModerationCaseIntakeAcknowledgement
{
    private ModerationCaseIntakeAcknowledgement() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid TrustDomainId { get; private set; }
    public Guid CallerUserId { get; private set; }
    public Guid CaseId { get; private set; }
    public long CaseRevision { get; private set; }
    public Guid ReceiptId { get; private set; }
    public long ReceiptRevision { get; private set; }
    public Guid OperationId { get; private set; }
    public long EvidenceCaseRevision { get; private set; }
    public Guid BudgetSourceId { get; private set; }
    public Guid BudgetAdoptionId { get; private set; }
    public long BudgetSourceWriterGeneration { get; private set; }
    public long BudgetAdoptionWriterGeneration { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;
    public DateTimeOffset AcceptedAt { get; private set; }

    /// <summary>
    /// Captures the exact already-consumed receipt and staged owned material. The native writer
    /// derives a keyed request digest, then commits case, receipt and acknowledgement together
    /// after final source requalification. This constructor does not establish native authority.
    /// </summary>
    public static ModerationCaseIntakeAcknowledgement Capture(
        Guid id, ModerationCase allegation, ModerationCaseTargetReceipt receipt,
        ReadOnlySpan<byte> requestDigest, ModerationOperationalBudgetSource budget,
        ModerationOperationalBudgetAdoption adoption)
    {
        ArgumentNullException.ThrowIfNull(allegation);
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(adoption);
        ModerationHostBinding.RequireUuidV7(id, nameof(id));
        allegation.ValidatePersistedState();
        receipt.ValidatePersistedState();
        budget.ValidatePersistedState();
        adoption.ValidatePersistedState();
        var evidence = allegation.Target is { } target
            ? allegation.Evidence.SingleOrDefault(item => item.ToReference(target, allegation.OccurrenceId) == receipt.Evidence)
            : null;
        if (allegation.Subject is not null
            || allegation.Target != receipt.Evidence.Target || evidence is null
            || allegation.Scope.TrustDomainId != receipt.TargetOriginTrustDomainId
            || receipt.ConsumedCaseId != allegation.Id || receipt.ConsumedOperationId is null
            || receipt.ConsumedAt is null || receipt.ConsumedAt < allegation.UpdatedAt
            || budget.Action != ModerationOperationalAction.SubmitReport
            || budget.TenantId != receipt.TenantId || budget.HostBindingId != receipt.HostBindingId
            || budget.TrustDomainId != receipt.TargetOriginTrustDomainId
            || !adoption.MatchesCurrent(budget, receipt.ConsumedAt.Value)
            || requestDigest.Length != 32)
            throw new InvalidOperationException("Acknowledgement requires the original consumed receipt and owned allegation material.");

        return new ModerationCaseIntakeAcknowledgement
        {
            Id = id, TenantId = receipt.TenantId, TrustDomainId = receipt.TargetOriginTrustDomainId,
            CallerUserId = receipt.CallerUserId, CaseId = allegation.Id, CaseRevision = allegation.Revision,
            ReceiptId = receipt.Id, ReceiptRevision = receipt.Revision, OperationId = receipt.ConsumedOperationId.Value,
            EvidenceCaseRevision = evidence.CaseRevision, RequestDigest = Convert.ToHexStringLower(requestDigest),
            BudgetSourceId = budget.Id, BudgetAdoptionId = adoption.Id,
            BudgetSourceWriterGeneration = budget.WriterGeneration, BudgetAdoptionWriterGeneration = adoption.WriterGeneration,
            AcceptedAt = receipt.ConsumedAt.Value
        };
    }

    public bool MatchesRequest(Guid callerUserId, Guid operationId, Guid receiptId, ReadOnlySpan<byte> digest) =>
        CallerUserId == callerUserId && OperationId == operationId && ReceiptId == receiptId
        && digest.Length == 32 && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(RequestDigest), digest);

    public void ValidatePersistedState()
    {
        if (Id == Guid.Empty || Id.Version != 7 || Id.Variant is < 8 or > 11
            || TenantId == Guid.Empty || TrustDomainId == Guid.Empty || CallerUserId == Guid.Empty
            || CaseId == Guid.Empty || CaseRevision < 1 || ReceiptId == Guid.Empty
            || ReceiptRevision != 2 || OperationId == Guid.Empty
            || EvidenceCaseRevision < 1 || EvidenceCaseRevision > CaseRevision
            || BudgetSourceId == Guid.Empty || BudgetAdoptionId == Guid.Empty || BudgetSourceId == BudgetAdoptionId
            || BudgetSourceWriterGeneration < 1 || BudgetAdoptionWriterGeneration <= BudgetSourceWriterGeneration
            || BudgetAdoptionWriterGeneration == long.MaxValue
            || RequestDigest.Length != 64 || RequestDigest.Any(character => !char.IsAsciiHexDigitLower(character))
            || AcceptedAt == default || AcceptedAt == DateTimeOffset.MaxValue
            || AcceptedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("The persisted acknowledgement source binding is inconsistent.");
    }

    public override string ToString() => nameof(ModerationCaseIntakeAcknowledgement);
}
