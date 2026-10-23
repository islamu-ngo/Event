using System.Text.Json.Serialization;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Requests.Commands;

/// <summary>
/// Incident-specific investigator intent, not a finding, admission, sanction or authority assertion.
/// The native writer independently qualifies the current assessor and protects the four grounds.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecordModerationParticipantInvestigationCommand : ModerationQualificationCommand
{
    public required Guid SourceId { get; init; }
    public required Guid OccurrenceCaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required long EvidenceCaseRevision { get; init; }
    public required Guid ReadGrantId { get; init; }
    public required Guid EvidenceSourceId { get; init; }
    public required Guid EvidenceSourceVersion { get; init; }
    public required Guid SubjectHumanId { get; init; }
    public required Guid SessionId { get; init; }
    public required Guid ExpectedSessionGeneration { get; init; }
    public required DateTimeOffset IncidentWindowFrom { get; init; }
    public required DateTimeOffset IncidentWindowUntil { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    public required DateTimeOffset PurgeAt { get; init; }
    public required string IdentificationGrounds { get; init; }
    public required string AllegedConduct { get; init; }
    public required string EvidenceProvenance { get; init; }
    public required string ContradictionsAndCredentialMisuse { get; init; }

    internal override ModerationQualificationMutation ToMutation() =>
        new RecordModerationParticipantIncidentResponsibilitySource
        {
            OperationId = OperationId, Target = Target, ExpectedQualificationRevision = ExpectedQualificationRevision,
            SourceId = SourceId, OccurrenceCaseId = OccurrenceCaseId, ExpectedCaseRevision = ExpectedCaseRevision,
            EvidenceCaseRevision = EvidenceCaseRevision, ReadGrantId = ReadGrantId,
            EvidenceSourceId = EvidenceSourceId, EvidenceSourceVersion = EvidenceSourceVersion,
            SubjectHumanId = SubjectHumanId, SessionId = SessionId, ExpectedSessionGeneration = ExpectedSessionGeneration,
            IncidentWindowFrom = IncidentWindowFrom, IncidentWindowUntil = IncidentWindowUntil,
            ValidUntil = ValidUntil, PurgeAt = PurgeAt,
            Grounds = new(IdentificationGrounds, AllegedConduct, EvidenceProvenance, ContradictionsAndCredentialMisuse)
        };
}
