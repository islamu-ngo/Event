using System.Collections.Immutable;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Domain.Services;

namespace Explore.Application.Services;

public enum EventResourceAudienceFailure
{
    None, InvalidRequest, AuthenticationRequired, Forbidden, NotFound, Unavailable
}

public sealed record EventResourceAudienceDetailResult(
    EventResourceAudienceFailure Failure, EventResourceAudienceDetailDto? Value = null,
    EventResourceAudienceDisclosureProof? Proof = null);

public sealed record EventResourceAudiencePageResult(
    EventResourceAudienceFailure Failure, EventResourcePageDto? Value = null,
    EventResourceAudienceDisclosureProof? Proof = null);

/// <summary>Non-wire projection binding, never a grant. Every release requires fresh authority.</summary>
public sealed class EventResourceAudienceDisclosureProof
{
    internal EventResourceCursorScope Scope { get; }
    internal EventResourceAuthorityRequest? Parent { get; }
    internal ImmutableArray<EventResourceAuthorityRequest> Checks { get; }

    internal EventResourceAudienceDisclosureProof(EventResourceCursorScope scope,
        EventResourceAuthorityRequest? parent, IEnumerable<EventResourceAuthorityRequest> checks)
    {
        Scope = scope;
        Parent = parent;
        Checks = checks.DistinctBy(check => check.ResourceId).ToImmutableArray();
    }
}

internal sealed record EventResourceAudienceDecision(EventResourceAuthorityOutcome Outcome,
    Guid? Version, EventResourceAccessDecision? Disclosure);
