using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventResource;
using Explore.Domain.Services;

namespace Explore.Application.Services;

public enum EventResourceAudienceFailure
{
    None, InvalidRequest, AuthenticationRequired, Forbidden, NotFound, Unavailable
}

public sealed record EventResourceAudienceDetailResult(
    EventResourceAudienceFailure Failure,
    [property: JsonIgnore] EventResourceReadProjection<EventResourceAudienceDetailDto>? Projection = null,
    [property: JsonIgnore] EventResourceAudienceDisclosureProof? Proof = null);

public sealed record EventResourceAudiencePageResult(
    EventResourceAudienceFailure Failure,
    [property: JsonIgnore] EventResourceReadProjection<EventResourcePageDto>? Projection = null,
    [property: JsonIgnore] EventResourceAudienceDisclosureProof? Proof = null);

/// <summary>Non-wire projection binding, never a grant. Every release requires fresh authority.</summary>
public sealed class EventResourceAudienceDisclosureProof
{
    internal EventResourceCursorScope Scope { get; }
    internal EventResourceAuthorityRequest? Parent { get; }
    internal ImmutableArray<EventResourceAuthorityRequest> Checks { get; }
    internal ImmutableArray<EventResourceAuthorityRequest> ActionChecks { get; }
    internal ImmutableArray<EventResourceAuthorityOutcome> ActionOutcomes { get; }

    internal EventResourceAudienceDisclosureProof(EventResourceCursorScope scope,
        EventResourceAuthorityRequest? parent, IEnumerable<EventResourceAuthorityRequest> checks,
        IEnumerable<EventResourceAuthorityRequest> actionChecks,
        IEnumerable<EventResourceAuthorityOutcome> actionOutcomes)
    {
        Scope = scope;
        Parent = parent;
        Checks = checks.DistinctBy(check => check.ResourceId).ToImmutableArray();
        ActionChecks = actionChecks.ToImmutableArray();
        ActionOutcomes = actionOutcomes.ToImmutableArray();
    }
}

internal sealed record EventResourceAudienceDecision(EventResourceAuthorityOutcome Outcome,
    Guid? Version, EventResourceAccessDecision? Disclosure);
