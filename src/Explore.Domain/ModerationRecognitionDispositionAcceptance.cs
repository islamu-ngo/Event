using System.Security.Cryptography;
using System.Text.Json;

namespace Explore.Domain;

public enum ModerationRecognitionDispositionAcceptanceRole { RecoveryOwner = 1, PrivacyOwner = 2 }

/// <summary>
/// Immutable native acceptance receipt for one independently authenticated owner operation.
/// It binds the entire original request, coverage, material and substantive mandate commitment.
/// Together with the exact retained fact it supports recovery after the accepting lease ends;
/// neither this receipt nor a disposition state marker independently grants erasure permission.
/// </summary>
public sealed class ModerationRecognitionDispositionAcceptance
{
    private ModerationRecognitionDispositionAcceptance() { }
    public Guid DispositionId { get; private set; }
    public ModerationRecognitionDispositionAcceptanceRole Role { get; private set; }
    public Guid HumanId { get; private set; }
    public long AcceptedRevision { get; private set; }
    public DateTimeOffset AcceptedAt { get; private set; }
    public string RequestDigest { get; private set; } = string.Empty;

    public static ModerationRecognitionDispositionAcceptance Record(ModerationRecognitionDisposition disposition,
        ModerationRecognitionDispositionAcceptanceRole role, Guid human)
    {
        disposition.ValidatePersistedState();
        var expected = role == ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner
            ? disposition.RecoveryOwnerHumanId : disposition.PrivacyOwnerHumanId;
        if (!Enum.IsDefined(role) || human != expected || disposition.Revision != (long)role)
            throw new InvalidOperationException("Acceptance belongs to the exact independently authenticated owner operation.");
        return new()
        {
            DispositionId = disposition.Id, Role = role, HumanId = human, AcceptedRevision = (long)role,
            AcceptedAt = role == ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner
                ? disposition.AssessedAt : disposition.AuthorizedAt!.Value,
            RequestDigest = Digest(disposition, role)
        };
    }

    public bool Matches(ModerationRecognitionDisposition disposition)
    {
        ValidatePersistedState();
        return DispositionId == disposition.Id
            && HumanId == (Role == ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner
                ? disposition.RecoveryOwnerHumanId : disposition.PrivacyOwnerHumanId)
            && AcceptedAt == (Role == ModerationRecognitionDispositionAcceptanceRole.RecoveryOwner
                ? disposition.AssessedAt : disposition.AuthorizedAt)
            && RequestDigest == Digest(disposition, Role);
    }

    public void ValidatePersistedState()
    {
        ModerationHostBinding.RequireUuidV7(DispositionId, nameof(DispositionId));
        if (!Enum.IsDefined(Role) || HumanId == Guid.Empty || AcceptedRevision != (long)Role
            || AcceptedAt.Offset != TimeSpan.Zero || AcceptedAt == DateTimeOffset.MinValue
            || AcceptedAt == DateTimeOffset.MaxValue || RequestDigest.Length != 64
            || !RequestDigest.All(char.IsAsciiHexDigitLower))
            throw new InvalidOperationException("Native owner acceptance receipt is malformed.");
    }

    private static string Digest(ModerationRecognitionDisposition row, ModerationRecognitionDispositionAcceptanceRole role) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            row.Id, row.IntentId, row.SubjectId, row.PolicyVersion, row.ReasonCode, row.PendingRevision,
            row.SourceId, row.SourceRevision, row.SourceGeneration, row.AdoptionId, row.AdoptionRevision,
            row.AdoptionGeneration, row.PrivacyOwnerHumanId, row.RecoveryOwnerHumanId,
            row.OriginalMaterialReceipt, row.OriginalMaterialCount, row.ObservedMaterialCommitment,
            row.CoverageReceipt, row.CoverageCount, row.Grounds, row.ObservedFailure, row.GroundsReceipt,
            row.LegalMandateReceipt, row.OriginalDeadline, row.AssessedAt, row.ValidUntil,
            Role = role,
            AuthorizedAt = role == ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner ? row.AuthorizedAt : null,
            AuthorityHighWater = role == ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner
                ? row.AuthorityHighWaterBeforeAuthorization : null,
            ReplayUntil = role == ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner
                ? (DateTimeOffset?)row.ReplayUntil : null,
            RetainUntil = role == ModerationRecognitionDispositionAcceptanceRole.PrivacyOwner
                ? (DateTimeOffset?)row.RetainUntil : null
        })));

    public override string ToString() => nameof(ModerationRecognitionDispositionAcceptance);
}
