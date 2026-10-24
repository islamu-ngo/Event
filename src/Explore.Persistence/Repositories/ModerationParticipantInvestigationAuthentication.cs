using System.Security.Cryptography;
using System.Text;
using Explore.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Exact encrypted assessment binding shared by its native capture and attribution owners.
/// This primitive grants no permission; both callers independently acquire current native facts.
/// </summary>
internal static class ModerationParticipantInvestigationAuthentication
{
    internal static IDataProtector Protector(IDataProtectionProvider protection,
        ModerationParticipantIncidentResponsibilitySource row, string authorityDigest) =>
        protection.CreateProtector("ISLAMU.Moderation.ParticipantInvestigation.v1")
            .CreateProtector(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                Binding(row) + "|" + authorityDigest))));

    internal static string Binding(ModerationParticipantIncidentResponsibilitySource row) =>
        FormattableString.Invariant(
            $"{row.Id:N}|{row.HostBindingId:N}|{row.TrustDomainId:N}|{row.TenantId:N}|{row.OccurrenceCaseId:N}|{row.OccurrenceId:N}|{row.OccurrenceCaseRevision}|{row.EvidenceCaseRevision}|{row.EvidenceSourceId:N}|{row.EvidenceSourceVersion:N}|{row.MaterialFactId:N}|{row.OriginalNarrativeId:N}|{row.OriginalNarrativePurgeAt.UtcTicks}|{row.EventId:N}|{row.EventVersion:N}|{row.SessionId:N}|{row.SessionGeneration:N}|{row.IncidentWindowFrom.UtcTicks}|{row.IncidentWindowUntil.UtcTicks}|{row.OrderId:N}|{row.OrderGeneration:N}|{row.ParticipantId:N}|{row.ParticipantGeneration:N}|{(int)row.SubjectKind}|{row.SubjectId:N}|{row.AssessorHumanId:N}|{row.AssessorRoleGrantId:N}|{row.ReadIssuerHumanId:N}|{row.ReadGrantId:N}|{row.ReadGrantRevision}|{row.ReadGrantExpiresAt.UtcTicks}|{row.QualificationPolicyId:N}|{row.QualificationPolicyRevision}|{row.MaximumQualificationLifetimeTicks}|{(int)row.TargetKind}|{(int)row.Purpose}|{row.CustodySourceId:N}|{row.CustodySourceRevision}|{row.CustodySourceWriterGeneration}|{row.CustodySourceValidUntil.UtcTicks}|{row.MaximumRetentionTicks}|{row.CustodyAdoptionId:N}|{row.CustodyAdoptionRevision}|{row.CustodyAdoptionWriterGeneration}|{row.CustodyAdoptionValidUntil.UtcTicks}|{row.QualifiedAt.UtcTicks}|{row.ValidUntil.UtcTicks}|{row.PurgeAt.UtcTicks}|{row.AssessmentDigest}");
}
