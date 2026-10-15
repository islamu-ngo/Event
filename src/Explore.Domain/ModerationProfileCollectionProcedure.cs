using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

public enum ModerationProfilePreliminaryFieldKind
{
    ApplicabilityDeclaration = 1,
    AccountEligibilityReference = 2,
    SubjectConsentReference = 3,
    GuardianRelationshipReference = 4,
    GuardianConsentReference = 5
}

public enum ModerationProfileNormalFieldKind { DateOfBirth = 101, FirstName = 102, LastName = 103, Email = 104 }

public enum ModerationProfileCollectionApplicability { NativeIndependentlyAcceptedAssessment = 1 }

/// <summary>
/// Executable field vocabulary for existing-Human ProfileCompletion only, not a collection
/// receipt or native applicability decision. Preliminary declarations and reference coordinates
/// confer no authority. An authenticated, independently accepted native assessment determines
/// required prerequisites; declared age, client flags and this allowed field set cannot do so.
/// Guardian relationship and guardian consent references are distinct; neither supplies the
/// other, and their actual ownership/currentness must be independently authenticated.
/// Normal fields require the actual subject's current native collection receipt before body
/// acceptance, logging, persistence or provider ingestion. Names/email are explicitly selected
/// optional fields, not defaults. Unknown procedure/applicability keeps collection closed without
/// gating catalogue, remedy or ordinary erasure.
/// Grounds describe reviewed guardian/manual-issuer criteria only, never private evidence,
/// relationship proof or professional/legal certification. No vendor consent, purchaser
/// inheritance or universal identity/face requirement is supplied by this procedure.
/// </summary>
public sealed class ModerationProfileCollectionProcedure
{
    public ModerationProfileCollectionProcedure(ModerationIndividualPurpose purpose,
        ModerationProfileCollectionApplicability applicability,
        IReadOnlyList<ModerationProfilePreliminaryFieldKind> preliminaryFields,
        IReadOnlyList<ModerationProfileNormalFieldKind> normalFields,
        string guardianRelationshipReviewGrounds, string manualIssuerReviewGrounds)
    {
        ArgumentNullException.ThrowIfNull(preliminaryFields);
        ArgumentNullException.ThrowIfNull(normalFields);
        var gates = preliminaryFields.Take(6).Order().ToArray();
        var normal = normalFields.Take(5).Order().ToArray();
        if (purpose != ModerationIndividualPurpose.ProfileCompletion
            || applicability != ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment
            || gates.Length is < 1 or > 5 || gates.Any(kind => !Enum.IsDefined(kind))
            || gates.Distinct().Count() != gates.Length
            || !gates.Contains(ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration)
            || normal.Length is < 1 or > 4 || normal.Any(kind => !Enum.IsDefined(kind))
            || normal.Distinct().Count() != normal.Length
            || !normal.Contains(ModerationProfileNormalFieldKind.DateOfBirth)
            || !ModerationChildAgePolicyTerms.Grounds(guardianRelationshipReviewGrounds)
            || !ModerationChildAgePolicyTerms.Grounds(manualIssuerReviewGrounds))
            throw new ArgumentException("Profile completion requires finite distinct known gate/profile fields, explicit DOB and reviewed criteria.");
        Purpose = purpose;
        Applicability = applicability;
        PreliminaryFields = Array.AsReadOnly(gates);
        NormalFields = Array.AsReadOnly(normal);
        GuardianRelationshipReviewGrounds = guardianRelationshipReviewGrounds;
        ManualIssuerReviewGrounds = manualIssuerReviewGrounds;
    }

    public ModerationIndividualPurpose Purpose { get; }
    public ModerationProfileCollectionApplicability Applicability { get; }
    public IReadOnlyList<ModerationProfilePreliminaryFieldKind> PreliminaryFields { get; }
    public IReadOnlyList<ModerationProfileNormalFieldKind> NormalFields { get; }
    public string GuardianRelationshipReviewGrounds { get; }
    public string ManualIssuerReviewGrounds { get; }

    /// <summary>Vocabulary compatibility only; a native Human account and all authority remain independently authenticated.</summary>
    public bool Supports(ModerationIndividualBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return binding.Target.Kind == ModerationIndividualTargetKind.Human && binding.Purpose == Purpose;
    }

    public bool AllowsPreliminaryField(ModerationProfilePreliminaryFieldKind kind) => PreliminaryFields.Contains(kind);
    public bool AllowsNormalField(ModerationProfileNormalFieldKind kind) => NormalFields.Contains(kind);

    internal bool Matches(ModerationProfileCollectionProcedure other) =>
        Purpose == other.Purpose && Applicability == other.Applicability
        && PreliminaryFields.SequenceEqual(other.PreliminaryFields) && NormalFields.SequenceEqual(other.NormalFields)
        && GuardianRelationshipReviewGrounds == other.GuardianRelationshipReviewGrounds
        && ManualIssuerReviewGrounds == other.ManualIssuerReviewGrounds;

    public override string ToString() => nameof(ModerationProfileCollectionProcedure);
}
