using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

[Flags]
public enum ModerationRecognitionPrivacyPurpose
{
    ScopedActionContinuity = 1,
    CoveredReplacementAdmissionExclusion = 2
}

public enum ModerationRecognitionPrivacyLinkage { ExactFinalActiveSanctionScopeRevisionAndInterval = 1 }
public enum ModerationRecognitionPrivacyOrdinaryErasure { IndependentExistingAuthority = 1 }
public enum ModerationRecognitionPrivacyCorrection { RevisionBoundRemoveOnlyAffectedContributionAndPreserveRemedies = 1 }
public enum ModerationRecognitionPrivacyCatastrophicDisposition { QualifiedRecoveryOrLegalErasureByDeadline = 1 }
public enum ModerationRecognitionPrivacyKeyObligations { OriginalComputedTokensAndAcknowledgedReplicaBackupHorizon = 1 }
public enum ModerationRecognitionPrivacyHold { None = 1, IndividuallyJustifiedLegalClaim = 2 }

public enum ModerationRecognitionPrivacyRecordClass
{
    PendingComputedRecognition = 1,
    EligibleMatchingMaterial = 2,
    ProtectedCorrectionEvidence = 3,
    DispositionAndReplayReceipt = 4
}

public enum ModerationRecognitionPrivacyRetentionStart
{
    OriginalCapture = 1,
    LatestQualifiedRemedyNotification = 2,
    QualifiedDisposition = 3
}

public enum ModerationRecognitionPrivacyErasureRule
{
    SettleOrQualifiedDispositionByLawfulDeadline = 1,
    EarlierSanctionExpiryReversalPurposeLossOrRetentionCeiling = 2,
    ProtectedCalendarRemedyFloorThenFiniteJustifiedHold = 3,
    BackupReplayFloorThenFiniteJustifiedHold = 4
}

public enum ModerationRecognitionPrivacyRecordCorrection
{
    DropReversedOrExpiredContributionsBeforeSettlement = 1,
    RemoveOnlyInvalidatedMatchingContribution = 2,
    CorrectMinimizedEvidenceWithoutRestoringErasedHistory = 3,
    AppendQualifiedDispositionAndPreserveReplayFloor = 4
}

/// <summary>
/// Adopted obligations only. Recognition needs exact provider-qualified proof and separately
/// qualified final active sanction revisions, never warnings, allegations, age or shared contact.
/// Action continuity restores only still-valid contributions; only admission exclusions reject
/// covered replacement provisioning. Neither purpose restores an erased profile or history.
/// </summary>
public sealed record ModerationRecognitionPrivacyTerms(
    ModerationRecognitionPrivacyPurpose Purposes,
    ModerationScopeKind ScopeCeiling,
    ModerationRecognitionPrivacyLinkage Linkage,
    ModerationRecognitionPrivacyOrdinaryErasure OrdinaryErasure,
    ModerationRecognitionPrivacyCorrection Correction,
    ModerationRecognitionPrivacyCatastrophicDisposition CatastrophicDisposition,
    ModerationRecognitionPrivacyKeyObligations KeyObligations,
    int ProtectedRemedyCalendarMonths,
    TimeSpan EscalationAfter,
    TimeSpan LawfulRequestDeadline,
    TimeSpan MaximumBackupHorizon,
    TimeSpan BackupSafetyMargin)
{
    public void Validate()
    {
        if ((int)Purposes is < 1 or > 3 || !Enum.IsDefined(ScopeCeiling)
            || Linkage != ModerationRecognitionPrivacyLinkage.ExactFinalActiveSanctionScopeRevisionAndInterval
            || OrdinaryErasure != ModerationRecognitionPrivacyOrdinaryErasure.IndependentExistingAuthority
            || Correction != ModerationRecognitionPrivacyCorrection.RevisionBoundRemoveOnlyAffectedContributionAndPreserveRemedies
            || CatastrophicDisposition != ModerationRecognitionPrivacyCatastrophicDisposition.QualifiedRecoveryOrLegalErasureByDeadline
            || KeyObligations != ModerationRecognitionPrivacyKeyObligations.OriginalComputedTokensAndAcknowledgedReplicaBackupHorizon
            || ProtectedRemedyCalendarMonths is < 1 or > 1200
            || !FinitePositive(EscalationAfter) || !FinitePositive(LawfulRequestDeadline)
            || EscalationAfter >= LawfulRequestDeadline
            || !FinitePositive(MaximumBackupHorizon) || !FinitePositive(BackupSafetyMargin)
            || MaximumBackupHorizon.Ticks >= long.MaxValue - BackupSafetyMargin.Ticks)
            throw new ArgumentException("Recognition privacy requires explicit purposes, protected erasure, correction, finite disposition and backup/key obligations.");
    }

    internal static bool FinitePositive(TimeSpan value) => value > TimeSpan.Zero && value < TimeSpan.MaxValue;
}

/// <summary>
/// The governing writer records minimized mandate and canonical-controller qualification grounds.
/// The named privacy owner independently accepts them through adoption. Native roles are necessary
/// but not sufficient: missing grounds or independent controllers cannot become an AG02 approval.
/// These are governance statements, not professional certification or subject evidence.
/// </summary>
public sealed record ModerationRecognitionPrivacyOwnership(
    Guid PrivacyOwnerHumanId, Guid PrivacyControllerHumanId,
    Guid RecoveryOwnerHumanId, Guid RecoveryControllerHumanId, Guid GovernanceControllerHumanId,
    string IdentityAndIndependenceBasis, string PrivacyLegalMandateBasis, string RecoveryMandateBasis)
{
    public void Validate(Guid recorder)
    {
        var controllers = new[] { PrivacyControllerHumanId, RecoveryControllerHumanId, GovernanceControllerHumanId };
        if (recorder == Guid.Empty || PrivacyOwnerHumanId == Guid.Empty || RecoveryOwnerHumanId == Guid.Empty
            || controllers.Any(id => id == Guid.Empty) || controllers.Distinct().Count() != 3
            || PrivacyOwnerHumanId == RecoveryOwnerHumanId || PrivacyOwnerHumanId == recorder || RecoveryOwnerHumanId == recorder
            || PrivacyOwnerHumanId == RecoveryControllerHumanId || PrivacyOwnerHumanId == GovernanceControllerHumanId
            || RecoveryOwnerHumanId == PrivacyControllerHumanId || RecoveryOwnerHumanId == GovernanceControllerHumanId
            || recorder == PrivacyControllerHumanId || recorder == RecoveryControllerHumanId
            || !Grounds(IdentityAndIndependenceBasis) || !Grounds(PrivacyLegalMandateBasis) || !Grounds(RecoveryMandateBasis))
            throw new ArgumentException("Privacy and recovery ownership require independently qualified canonical controllers and substantive mandate grounds.");
    }

    private static bool Grounds(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
    public override string ToString() => nameof(ModerationRecognitionPrivacyOwnership);
}

/// <summary>
/// Maximum retention starts at capture for matching material, latest qualified remedy notification
/// for correction evidence, and disposition for replay receipts. Eligibility ends earlier on
/// reversal, expiry or loss of purpose. A hold never authorizes matching or extends a sanction:
/// only separately justified correction/receipt records may be held, with a finite end and review.
/// No automatic hold renewal or received-but-unsettled exception is available.
/// </summary>
public sealed record ModerationRecognitionPrivacyRetentionTerms(
    ModerationRecognitionPrivacyRecordClass RecordClass, TimeSpan MaximumRetention,
    ModerationRecognitionPrivacyHold Hold, TimeSpan MaximumHold, TimeSpan HoldReviewWithin)
{
    public ModerationRecognitionPrivacyRetentionStart RetentionStart => RecordClass switch
    {
        ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition
            or ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial => ModerationRecognitionPrivacyRetentionStart.OriginalCapture,
        ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence => ModerationRecognitionPrivacyRetentionStart.LatestQualifiedRemedyNotification,
        ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt => ModerationRecognitionPrivacyRetentionStart.QualifiedDisposition,
        _ => throw new ArgumentException("Unknown recognition privacy record class.")
    };

    public ModerationRecognitionPrivacyErasureRule Erasure => RecordClass switch
    {
        ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition => ModerationRecognitionPrivacyErasureRule.SettleOrQualifiedDispositionByLawfulDeadline,
        ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial => ModerationRecognitionPrivacyErasureRule.EarlierSanctionExpiryReversalPurposeLossOrRetentionCeiling,
        ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence => ModerationRecognitionPrivacyErasureRule.ProtectedCalendarRemedyFloorThenFiniteJustifiedHold,
        ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt => ModerationRecognitionPrivacyErasureRule.BackupReplayFloorThenFiniteJustifiedHold,
        _ => throw new ArgumentException("Unknown recognition privacy record class.")
    };

    public ModerationRecognitionPrivacyRecordCorrection Correction => RecordClass switch
    {
        ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition => ModerationRecognitionPrivacyRecordCorrection.DropReversedOrExpiredContributionsBeforeSettlement,
        ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial => ModerationRecognitionPrivacyRecordCorrection.RemoveOnlyInvalidatedMatchingContribution,
        ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence => ModerationRecognitionPrivacyRecordCorrection.CorrectMinimizedEvidenceWithoutRestoringErasedHistory,
        ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt => ModerationRecognitionPrivacyRecordCorrection.AppendQualifiedDispositionAndPreserveReplayFloor,
        _ => throw new ArgumentException("Unknown recognition privacy record class.")
    };

    public void Validate()
    {
        if (!Enum.IsDefined(RecordClass) || !ModerationRecognitionPrivacyTerms.FinitePositive(MaximumRetention)
            || !Enum.IsDefined(Hold)
            || Hold == ModerationRecognitionPrivacyHold.None && (MaximumHold != TimeSpan.Zero || HoldReviewWithin != TimeSpan.Zero)
            || Hold == ModerationRecognitionPrivacyHold.IndividuallyJustifiedLegalClaim
                && (RecordClass is ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition
                    or ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial
                    || !ModerationRecognitionPrivacyTerms.FinitePositive(MaximumHold)
                    || !ModerationRecognitionPrivacyTerms.FinitePositive(HoldReviewWithin)
                    || HoldReviewWithin > MaximumHold))
            throw new ArgumentException("Each record class requires finite purpose-limited retention and explicitly reviewed non-enforcement holds.");
    }
}
