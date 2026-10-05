using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Events.Discovery;

public sealed record GetEventDuplicateCandidatesQuery(Guid EventId) : IQuery<EventDuplicateCandidatesDto>;

public sealed record EventDuplicateCandidateDto(
    Guid Id, string Title, string PublicCode, string SourceKind,
    string? SourcePublisherName, Guid? MatchingSessionId,
    DateTimeOffset? StartsAtUtc, DateTimeOffset? EndsAtUtc);

public sealed record EventDuplicateCandidatesDto(
    Guid EventId, long ExpectedRevision,
    ImmutableArray<EventDuplicateCandidateDto> Candidates, bool IsBounded);
