using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Moderation;

/// <summary>
/// Investigator intent awaiting native material, independent authority and protected custody.
/// These identifiers and asserted grounds are not guilt, findings, strikes or permission.
/// </summary>
public sealed record RecordModerationParticipantIncidentResponsibilitySource : ModerationQualificationMutation
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
    public required ModerationParticipantInvestigationGrounds Grounds { get; init; }
}

public static class ModerationParticipantInvestigationQualification
{
    public static bool IsValid(RecordModerationParticipantIncidentResponsibilitySource request) =>
        request.Target?.Kind == ModerationScopeKind.Tenant
        && request.ExpectedQualificationRevision is > 0 and < long.MaxValue - 1
        && Uuid(request.OperationId) && Uuid(request.SourceId) && Uuid(request.OccurrenceCaseId)
        && Uuid(request.ReadGrantId) && Uuid(request.EvidenceSourceId)
        && Uuid(request.SubjectHumanId) && Uuid(request.SessionId)
        && request.ExpectedCaseRevision is > 0 and < long.MaxValue
        && request.EvidenceCaseRevision > 0 && request.EvidenceCaseRevision <= request.ExpectedCaseRevision
        && request.EvidenceSourceVersion != Guid.Empty && request.ExpectedSessionGeneration != Guid.Empty
        && Instant(request.IncidentWindowFrom) && Instant(request.IncidentWindowUntil)
        && request.IncidentWindowFrom < request.IncidentWindowUntil
        && Instant(request.ValidUntil) && Instant(request.PurgeAt)
        && request.IncidentWindowUntil < request.ValidUntil && request.ValidUntil <= request.PurgeAt
        && request.Grounds?.IsValid() == true;

    private static bool Uuid(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
    private static bool Instant(DateTimeOffset value) => value.Offset == TimeSpan.Zero
        && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue
        && value.UtcTicks % TimeSpan.TicksPerMicrosecond == 0;
}
