using System.Security.Cryptography;
using System.Text;

namespace Explore.Domain;

public enum ModerationRecognitionDispositionGrounds
{
    OriginalKeyRecoveryUnavailable = 1,
    OriginalStoreRecoveryUnavailable = 2,
    CompleteOriginalMaterialUnavailable = 3
}

/// <summary>
/// Two named independent owners authorize legal erasure of one exact received request,
/// not successful recognition, a new sanction, or permission to retain identifiers.
/// Native qualification must be reacquired before use; this record alone grants nothing.
/// </summary>
public sealed class ModerationRecognitionDisposition
{
    private ModerationRecognitionDisposition() { }
    public Guid Id { get; private set; }
    public Guid IntentId { get; private set; }
    public Guid SubjectId { get; private set; }
    public int PolicyVersion { get; private set; }
    public PrivacyErasureReasonCode ReasonCode { get; private set; }
    public long PendingRevision { get; private set; }
    public string? OriginalMaterialReceipt { get; private set; }
    public int OriginalMaterialCount { get; private set; }
    public string ObservedMaterialCommitment { get; private set; } = string.Empty;
    public string CoverageReceipt { get; private set; } = string.Empty;
    public int CoverageCount { get; private set; }
    public ModerationRecognitionPendingFailure ObservedFailure { get; private set; }
    public string GroundsReceipt { get; private set; } = string.Empty;
    public string LegalMandateReceipt { get; private set; } = string.Empty;
    public Guid SourceId { get; private set; }
    public long SourceRevision { get; private set; }
    public long SourceGeneration { get; private set; }
    public Guid AdoptionId { get; private set; }
    public long AdoptionRevision { get; private set; }
    public long AdoptionGeneration { get; private set; }
    public Guid PrivacyOwnerHumanId { get; private set; }
    public Guid RecoveryOwnerHumanId { get; private set; }
    public ModerationRecognitionDispositionGrounds Grounds { get; private set; }
    public DateTimeOffset OriginalDeadline { get; private set; }
    public DateTimeOffset AssessedAt { get; private set; }
    public DateTimeOffset ValidUntil { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public long? AuthorityHighWaterBeforeAuthorization { get; private set; }
    public long Revision { get; private set; }
    public long? AuthoritySequence { get; private set; }
    public DateTime? AuthorityRecordedAtUtc { get; private set; }
    public DateTimeOffset ReplayUntil { get; private set; }
    public DateTimeOffset RetainUntil { get; private set; }

    public static ModerationRecognitionDisposition Assess(Guid id, ModerationRecognitionPending pending,
        ModerationRecognitionPrivacySource source, ModerationRecognitionPrivacyAdoption adoption,
        Guid recoveryOwner, ModerationRecognitionDispositionGrounds grounds,
        string coverageReceipt, int coverageCount, DateTimeOffset now, DateTimeOffset validUntil)
    {
        pending.ValidatePersistedState();
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        if (!Enum.IsDefined(grounds) || pending.AuthoritySequence is not null || now < pending.EscalateAt
            || recoveryOwner != source.RecoveryOwnerHumanId || pending.SubjectId == recoveryOwner
            || pending.SubjectId == source.PrivacyOwnerHumanId || source.RecordedAt > pending.ReceivedAt
            || !adoption.MatchesCurrent(source, now) || validUntil > adoption.ValidUntil
            || validUntil > source.ValidUntil
            || grounds == ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable
                && pending.Failure != ModerationRecognitionPendingFailure.KeyUnavailable
            || grounds == ModerationRecognitionDispositionGrounds.OriginalStoreRecoveryUnavailable
                && pending.Failure != ModerationRecognitionPendingFailure.StoreUnavailable
            || grounds == ModerationRecognitionDispositionGrounds.CompleteOriginalMaterialUnavailable
                && (pending.HasCompleteMaterial || pending.Failure is not
                    (ModerationRecognitionPendingFailure.IncompleteMaterial or ModerationRecognitionPendingFailure.OriginalMaterialChanged))
            || pending.MaterialReceipt is not null && !pending.HasCompleteMaterial
                && grounds != ModerationRecognitionDispositionGrounds.CompleteOriginalMaterialUnavailable)
            throw new InvalidOperationException("Legal disposition requires the original named recovery owner and exact current qualified grounds.");
        ModerationQualificationPolicySource.RequireValidity(now, validUntil);
        var receipt = source.Retention.Single(row =>
            row.RecordClass == ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt);
        var result = new ModerationRecognitionDisposition
        {
            Id = id, IntentId = pending.IntentId, SubjectId = pending.SubjectId,
            PolicyVersion = pending.PolicyVersion, ReasonCode = pending.ReasonCode, PendingRevision = pending.Revision,
            OriginalMaterialReceipt = pending.MaterialReceipt, OriginalMaterialCount = pending.MaterialCount,
            ObservedMaterialCommitment = MaterialCommitment(pending),
            CoverageReceipt = coverageReceipt, CoverageCount = coverageCount, ObservedFailure = pending.Failure,
            GroundsReceipt = FailureReceipt(pending), LegalMandateReceipt = MandateReceipt(source),
            SourceId = source.Id, SourceRevision = source.Revision, SourceGeneration = source.WriterGeneration,
            AdoptionId = adoption.Id, AdoptionRevision = adoption.Revision, AdoptionGeneration = adoption.WriterGeneration,
            PrivacyOwnerHumanId = source.PrivacyOwnerHumanId, RecoveryOwnerHumanId = recoveryOwner,
            Grounds = grounds, OriginalDeadline = pending.SettleBy, AssessedAt = now, ValidUntil = validUntil,
            ReplayUntil = validUntil.AddTicks(checked(source.MaximumBackupHorizonTicks + source.BackupSafetyMarginTicks)),
            RetainUntil = now.AddTicks(receipt.MaximumRetentionTicks), Revision = 1
        };
        result.ValidatePersistedState();
        return result;
    }

    public void Authorize(long expectedRevision, Guid privacyOwner, ModerationRecognitionPending pending,
        ModerationRecognitionPrivacySource source, ModerationRecognitionPrivacyAdoption adoption, DateTimeOffset now,
        long authorityHighWaterBeforeAuthorization)
    {
        ValidatePersistedState();
        if (Revision != expectedRevision || Revision != 1 || authorityHighWaterBeforeAuthorization < 0
            || privacyOwner != PrivacyOwnerHumanId
            || privacyOwner == RecoveryOwnerHumanId || !MatchesCurrent(pending, source, adoption, now))
            throw new InvalidOperationException("The independent privacy owner must authorize the exact current recovery assessment.");
        AuthorizedAt = now;
        AuthorityHighWaterBeforeAuthorization = authorityHighWaterBeforeAuthorization;
        ReplayUntil = ValidUntil.AddTicks(checked(source.MaximumBackupHorizonTicks + source.BackupSafetyMarginTicks));
        RetainUntil = now.AddTicks(source.Retention.Single(row =>
            row.RecordClass == ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt).MaximumRetentionTicks);
        Revision = 2;
    }

    public bool Authorizes(ModerationRecognitionPending pending, ModerationRecognitionPrivacySource source,
        ModerationRecognitionPrivacyAdoption adoption, DateTimeOffset now)
    {
        ValidatePersistedState();
        return Revision == 2 && AuthorizedAt <= now && AuthoritySequence is null
            && MatchesCurrent(pending, source, adoption, now);
    }

    private bool MatchesCurrent(ModerationRecognitionPending pending, ModerationRecognitionPrivacySource source,
        ModerationRecognitionPrivacyAdoption adoption, DateTimeOffset now) =>
        PreservesRequest(pending) && pending.AuthoritySequence is null
        && MandateReceipt(source) == LegalMandateReceipt
        && source.Id == SourceId && source.Revision == SourceRevision && source.WriterGeneration == SourceGeneration
        && source.PrivacyOwnerHumanId == PrivacyOwnerHumanId && source.RecoveryOwnerHumanId == RecoveryOwnerHumanId
        && adoption.Id == AdoptionId && adoption.Revision == AdoptionRevision && adoption.WriterGeneration == AdoptionGeneration
        && adoption.MatchesCurrent(source, now) && now >= AssessedAt && now < ValidUntil && now.Offset == TimeSpan.Zero;

    public bool PreservesRequest(ModerationRecognitionPending pending) =>
        pending.IntentId == IntentId && pending.SubjectId == SubjectId && pending.PolicyVersion == PolicyVersion
        && pending.ReasonCode == ReasonCode && pending.Revision == PendingRevision
        && pending.MaterialReceipt == OriginalMaterialReceipt && pending.MaterialCount == OriginalMaterialCount
        && MaterialCommitment(pending) == ObservedMaterialCommitment
        && pending.Failure == ObservedFailure && FailureReceipt(pending) == GroundsReceipt
        && pending.SettleBy == OriginalDeadline;

    public void Bind(PrivacyErasureIntent fact, DateTimeOffset now)
    {
        ValidatePersistedState();
        if (AuthoritySequence is not null)
        {
            if (!MatchesFact(fact)) throw new InvalidOperationException("A different authority fact cannot consume this disposition.");
            return;
        }
        if (Revision != 2 || AuthorizedAt is not { } authorized || now < authorized
            || now.Offset != TimeSpan.Zero || now == DateTimeOffset.MaxValue
            || !AcceptsCommittedFact(fact))
            throw new InvalidOperationException("Legal disposition binds only its exact authorized original request.");
        AuthoritySequence = fact.AuthoritySequence;
        AuthorityRecordedAtUtc = fact.RecordedAtUtc;
        Revision = 3;
    }

    public bool MatchesFact(PrivacyErasureIntent fact) => MatchesRequest(fact)
        && AuthoritySequence == fact.AuthoritySequence && AuthorityRecordedAtUtc == fact.RecordedAtUtc;

    public bool AcceptsCommittedFact(PrivacyErasureIntent fact) => MatchesRequest(fact)
        && AuthorizedAt is { } authorized && AuthorityHighWaterBeforeAuthorization is { } cursor
        && fact.AuthoritySequence > cursor && fact.RecordedAtUtc >= authorized.UtcDateTime
        && fact.RecordedAtUtc < ValidUntil.UtcDateTime && fact.IdentityFences.Count == 0;

    public bool MatchesAcceptedSource(ModerationRecognitionPrivacySource source,
        ModerationRecognitionPrivacyAdoption adoption, PrivacyErasureIntent fact)
    {
        source.ValidatePersistedState();
        adoption.ValidatePersistedState();
        var recorded = new DateTimeOffset(fact.RecordedAtUtc);
        return AcceptsCommittedFact(fact) && source.Id == SourceId && source.Revision == SourceRevision
            && source.WriterGeneration == SourceGeneration && MandateReceipt(source) == LegalMandateReceipt
            && source.PrivacyOwnerHumanId == PrivacyOwnerHumanId && source.RecoveryOwnerHumanId == RecoveryOwnerHumanId
            && source.RecordedAt <= AssessedAt && recorded < source.ValidUntil
            && adoption.Id == AdoptionId && adoption.WriterGeneration == AdoptionGeneration
            && adoption.SourceId == SourceId && adoption.SourceRevision == SourceRevision
            && adoption.SourceWriterGeneration == SourceGeneration && adoption.AdoptedByHumanId == PrivacyOwnerHumanId
            && adoption.AdoptedAt <= AssessedAt && recorded < adoption.ValidUntil
            // Equal instants are ordered by the original native/source fences, not clock resolution.
            && (adoption.WithdrawnAt is null || adoption.WithdrawnAt >= recorded);
    }

    private bool MatchesRequest(PrivacyErasureIntent fact) => fact.IntentId == IntentId
        && fact.SubjectId == SubjectId && fact.SubjectKind == PrivacyErasureSubjectKind.User
        && fact.PolicyVersion == PolicyVersion && fact.ReasonCode == ReasonCode && !fact.IsLegalHoldPseudonymized
        && fact.IdentityFences.Count == 0;

    public void ValidatePersistedState()
    {
        foreach (var id in new[] { Id, IntentId, SourceId, AdoptionId })
            ModerationHostBinding.RequireUuidV7(id, nameof(Id));
        ModerationQualificationPolicySource.RequireValidity(AssessedAt, ValidUntil);
        if (SubjectId == Guid.Empty || PolicyVersion < 1 || !Enum.IsDefined(ReasonCode)
            || PendingRevision is < 1 or long.MaxValue || SourceRevision != 1 || AdoptionRevision != 1
            || OriginalMaterialCount < 0 || (OriginalMaterialReceipt is null) != (OriginalMaterialCount == 0)
            || OriginalMaterialReceipt is { } receipt && !Digest(receipt) || !Digest(ObservedMaterialCommitment)
            || CoverageCount < 1 || !Digest(CoverageReceipt) || !Digest(GroundsReceipt) || !Digest(LegalMandateReceipt)
            || ObservedFailure == ModerationRecognitionPendingFailure.None || !Enum.IsDefined(ObservedFailure)
            || SourceGeneration < 1 || AdoptionGeneration <= SourceGeneration
            || PrivacyOwnerHumanId == Guid.Empty || RecoveryOwnerHumanId == Guid.Empty
            || PrivacyOwnerHumanId == RecoveryOwnerHumanId || !Enum.IsDefined(Grounds)
            || OriginalDeadline.Offset != TimeSpan.Zero || OriginalDeadline == DateTimeOffset.MaxValue
            || ReplayUntil <= AssessedAt || RetainUntil < ReplayUntil || RetainUntil == DateTimeOffset.MaxValue
            || !(Revision == 1 && AuthorizedAt is null && AuthorityHighWaterBeforeAuthorization is null
                    && AuthoritySequence is null && AuthorityRecordedAtUtc is null
                || Revision == 2 && AuthorizedAt >= AssessedAt && AuthorizedAt < ValidUntil
                    && AuthorityHighWaterBeforeAuthorization >= 0 && AuthoritySequence is null && AuthorityRecordedAtUtc is null
                || Revision == 3 && AuthorizedAt >= AssessedAt && AuthorizedAt < ValidUntil
                    && AuthorityHighWaterBeforeAuthorization >= 0 && AuthoritySequence > AuthorityHighWaterBeforeAuthorization
                    && AuthorityRecordedAtUtc >= AuthorizedAt.Value.UtcDateTime
                    && AuthorityRecordedAtUtc < ValidUntil.UtcDateTime))
            throw new InvalidOperationException("Recognition legal disposition must preserve exact request, independent owners and finite proof.");
    }

    public override string ToString() => nameof(ModerationRecognitionDisposition);

    private static bool Digest(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower);

    private static string MaterialCommitment(ModerationRecognitionPending pending) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            pending.Material.OrderBy(item => item.Id).Select(ModerationRecognitionPending.ComputeItemReceipt)))));

    public static string ComputeCoverageReceipt(IEnumerable<Guid> sourceIds) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",
            sourceIds.Distinct().Order().Select(id => id.ToString("D"))))));

    private static string FailureReceipt(ModerationRecognitionPending pending) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            pending.IntentId, pending.Revision, pending.Failure, pending.MaterialCount,
            pending.MaterialReceipt, pending.ReceivedAt, pending.SettleBy, pending.EscalateAt
        })));

    private static string MandateReceipt(ModerationRecognitionPrivacySource source) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.Id, source.Revision, source.WriterGeneration, source.PrivacyLegalMandateBasis,
            source.RecoveryMandateBasis, source.IdentityAndIndependenceBasis, source.Terms, source.Ownership
        })));
}
