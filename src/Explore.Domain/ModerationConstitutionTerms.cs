using ISLAMU.Moderation.Domain;

namespace Explore.Domain;

public enum ModerationConstitutionEvidence { QualifiedFindingsAndResponse = 1 }
public enum ModerationConstitutionRemedies { RestrictedAuthenticationAndProofBoundRecovery = 1 }
public enum ModerationConstitutionIndependence { DistinctControllersAndImplicatedPartyExclusion = 1 }
public enum ModerationConstitutionExposure { ConcealedPurposeAuthorizedReveal = 1 }
public enum ModerationConstitutionOverload { AcknowledgeAndEscalate = 1 }

/// <summary>
/// Substantive adopted obligations, not readiness flags. Case findings, response opportunity,
/// conflict assessment and purpose-authorized disclosure still require their own native authority.
/// Complaint access is free, survives enforcement expiry and retains independently owned redress.
/// </summary>
public sealed record ModerationConstitutionTerms(
    ModerationScopeKind ScopeCeiling,
    ModerationConstitutionEvidence Evidence,
    ModerationConstitutionRemedies Remedies,
    ModerationConstitutionIndependence Independence,
    ModerationConstitutionExposure Exposure,
    ModerationConstitutionOverload Overload,
    int MinimumIndependentReviewers,
    int ProtectedComplaintCalendarMonths,
    TimeSpan MaximumReviewInterval,
    TimeSpan EscalationAfter,
    string SupportRoute,
    string IndependentRedressRoute,
    Guid CoordinatorHumanId,
    Guid FallbackHumanId)
{
    public void Validate()
    {
        if (!Enum.IsDefined(ScopeCeiling) || ScopeCeiling < ModerationScopeKind.Instance
            || Evidence != ModerationConstitutionEvidence.QualifiedFindingsAndResponse
            || Remedies != ModerationConstitutionRemedies.RestrictedAuthenticationAndProofBoundRecovery
            || Independence != ModerationConstitutionIndependence.DistinctControllersAndImplicatedPartyExclusion
            || Exposure != ModerationConstitutionExposure.ConcealedPurposeAuthorizedReveal
            || Overload != ModerationConstitutionOverload.AcknowledgeAndEscalate
            || MinimumIndependentReviewers is < 1 or > 32 || ProtectedComplaintCalendarMonths < 1
            || MaximumReviewInterval <= TimeSpan.Zero || MaximumReviewInterval == TimeSpan.MaxValue
            || EscalationAfter <= TimeSpan.Zero || EscalationAfter == TimeSpan.MaxValue
            || !Route(SupportRoute) || !Route(IndependentRedressRoute)
            || CoordinatorHumanId == Guid.Empty || FallbackHumanId == Guid.Empty
            || CoordinatorHumanId == FallbackHumanId)
            throw new ArgumentException("Constitution requires bounded jurisdiction, evidence, free protected remedies, independent review and owned escalation.");
    }

    private static bool Route(string value) => value is { Length: > 0 and <= 2048 }
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);
}

/// <summary>
/// Accountable reviewers qualify canonical controller identity, competence and independence in
/// substantive statements. Availability is separately self-recorded, not inferred from this roster.
/// </summary>
public sealed record ModerationConstitutionReviewerTerms(
    Guid AvailabilityId, long AvailabilityRevision, long AvailabilityWriterGeneration,
    Guid ReviewerHumanId, Guid ControllerHumanId, string IdentityBasis,
    string QualificationBasis, string IndependenceBasis)
{
    public void Validate()
    {
        ModerationHostBinding.RequireUuidV7(AvailabilityId, nameof(AvailabilityId));
        if (AvailabilityRevision != 1 || AvailabilityWriterGeneration is < 1 or long.MaxValue
            || ReviewerHumanId == Guid.Empty || ControllerHumanId == Guid.Empty
            || !Statement(IdentityBasis) || !Statement(QualificationBasis) || !Statement(IndependenceBasis))
            throw new ArgumentException("Reviewer governance requires actual identity, qualification and conflict grounds with exact availability.");
    }

    private static bool Statement(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
}
