using System.Security.Cryptography;

namespace Explore.Domain;

/// <summary>
/// Protected correction proof has its own calendar floor and finite ceiling, independent of
/// the shorter replay receipt. Only the original request receipt or independently qualified
/// owner may read it. It contains no token, provider input, allegation or sanction copy.
/// Independent legal authorization grants the original receipt only this bounded correction
/// purpose after ordinary status expiry; it never renews account or enrollment credentials.
/// </summary>
public sealed class ModerationRecognitionDispositionRemedy
{
    private ModerationRecognitionDispositionRemedy() { }
    public Guid Id { get; private set; }
    public Guid IntentId { get; private set; }
    public Guid SourceId { get; private set; }
    public long PendingRevision { get; private set; }
    public string? OriginalMaterialReceipt { get; private set; }
    public string ObservedMaterialCommitment { get; private set; } = string.Empty;
    public string GroundsReceipt { get; private set; } = string.Empty;
    public string LegalMandateReceipt { get; private set; } = string.Empty;
    public string CoverageReceipt { get; private set; } = string.Empty;
    public Guid PrivacyOwnerHumanId { get; private set; }
    public Guid RecoveryOwnerHumanId { get; private set; }
    public ModerationRecognitionDispositionGrounds Grounds { get; private set; }
    public byte[] ReceiptHash { get; private set; } = [];
    public DateTimeOffset ProtectedAt { get; private set; }
    public DateTimeOffset RemedyUntil { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }

    public static ModerationRecognitionDispositionRemedy Protect(ModerationRecognitionDisposition disposition,
        ModerationRecognitionPending pending, ModerationRecognitionPrivacySource source, DateTimeOffset now)
    {
        disposition.ValidatePersistedState();
        pending.ValidatePersistedState();
        source.ValidatePersistedState();
        if (disposition.Revision != 2 || disposition.AuthorizedAt != now
            || disposition.IntentId != pending.IntentId || disposition.PendingRevision != pending.Revision
            || disposition.SourceId != source.Id || disposition.SourceRevision != source.Revision
            || disposition.SourceGeneration != source.WriterGeneration)
            throw new InvalidOperationException("Protected remedy proof belongs to the exact independent legal authorization.");
        var result = new ModerationRecognitionDispositionRemedy
        {
            Id = disposition.Id, IntentId = disposition.IntentId, SourceId = source.Id,
            PendingRevision = pending.Revision, PrivacyOwnerHumanId = disposition.PrivacyOwnerHumanId,
            OriginalMaterialReceipt = disposition.OriginalMaterialReceipt,
            ObservedMaterialCommitment = disposition.ObservedMaterialCommitment,
            GroundsReceipt = disposition.GroundsReceipt, LegalMandateReceipt = disposition.LegalMandateReceipt,
            CoverageReceipt = disposition.CoverageReceipt,
            RecoveryOwnerHumanId = disposition.RecoveryOwnerHumanId, Grounds = disposition.Grounds,
            ReceiptHash = pending.ReceiptHash.ToArray(), ProtectedAt = now,
            RemedyUntil = now.AddMonths(source.ProtectedRemedyCalendarMonths),
            RetainUntil = now.AddTicks(source.Retention.Single(row =>
                row.RecordClass == ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence).MaximumRetentionTicks)
        };
        result.ValidatePersistedState();
        return result;
    }

    public bool Authenticates(ReadOnlySpan<byte> receiptHash, DateTimeOffset now) =>
        now >= ProtectedAt && now < RemedyUntil && now < RetainUntil && receiptHash.Length == 32
        && CryptographicOperations.FixedTimeEquals(receiptHash, ReceiptHash);

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, IntentId, SourceId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        if (PendingRevision < 1 || PrivacyOwnerHumanId == Guid.Empty || RecoveryOwnerHumanId == Guid.Empty
            || PrivacyOwnerHumanId == RecoveryOwnerHumanId || !Enum.IsDefined(Grounds)
            || ObservedMaterialCommitment.Length != 64 || !ObservedMaterialCommitment.All(char.IsAsciiHexDigitLower)
            || new[] { GroundsReceipt, LegalMandateReceipt, CoverageReceipt }
                .Any(value => value.Length != 64 || !value.All(char.IsAsciiHexDigitLower))
            || OriginalMaterialReceipt is { } receipt && (receipt.Length != 64 || !receipt.All(char.IsAsciiHexDigitLower))
            || ReceiptHash is not { Length: 32 } || ProtectedAt.Offset != TimeSpan.Zero
            || RemedyUntil <= ProtectedAt || RetainUntil < RemedyUntil || RetainUntil == DateTimeOffset.MaxValue)
            throw new InvalidOperationException("Protected correction evidence requires finite independently bounded remedy proof.");
    }

    public override string ToString() => nameof(ModerationRecognitionDispositionRemedy);
}
