namespace Explore.Application.Features.Admissions;

public static class ParticipantAdmissionFailureCodes
{
    public const string ParticipantUnavailable =
        "participant_admission_unavailable";
    public const string SubjectAuthorityRequired =
        "participant_subject_authority_required";
    public const string CompletionEvidenceIncomplete =
        "participant_completion_evidence_incomplete";
    public const string ConsentEvidenceRequired =
        "participant_consent_evidence_required";
    public const string ApprovalUnavailable =
        "participant_approval_unavailable";
    public const string AdmissionRevoked =
        "participant_admission_revoked";
}
