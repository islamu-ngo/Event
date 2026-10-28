using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Responses;
using Explore.Application.Services.Moderation;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Requests.Queries;

public sealed record GetModerationCurrentPolicyQuery(
    string Scope = "tenant", Guid? ProspectivePolicyId = null) : IQuery<ModerationCurrentPolicyReadResult>;

/// <summary>
/// The selected next-write coordinates are current request-tenant observations, not historical
/// publication authority. An uninitialized PolicyId is prospective and unreserved. The authoring
/// bounds are compilation limits, not legal/deployment approval or active readiness.
/// </summary>
public sealed record ModerationCurrentPolicyView(
    Guid PolicyId, string Scope, string State, AuthorityScope Jurisdiction,
    Guid ExpectedHostBindingId, Guid ExpectedAuthoritySourceId, long ExpectedQualificationRevision,
    string ExpectedWriterGeneration, long ExpectedRevision, Guid? ExpectedPublicationId,
    long InstalledCatalogueRevision, long InstalledInventoryRevision, string InstalledInventoryFingerprint,
    ModerationPolicyBounds AuthoringBounds, ModerationCurrentPolicyAuthoringState? Authoring)
{
    [JsonIgnore] public bool NativeAuthoringQualified { get; internal init; }
    public override string ToString() => nameof(ModerationCurrentPolicyView);
}

public sealed record ModerationCurrentPolicyProfileView(
    ModerationPolicyProfileDefinition Definition, ImmutableArray<ModerationConsequenceModes> Consequences);

/// <summary>
/// Complete retained definitions, including disabled ones and every base/additive plan. Historical
/// inventory and expected revision remain historical inputs, separate from current write coordinates.
/// SelectedProfiles describes the declaration, never currently deployed support.
/// </summary>
public sealed record ModerationCurrentPolicyAuthoringState(
    Guid PublicationId, long HistoricalExpectedRevision,
    long CatalogueRevision, long InventoryRevision, string InventoryFingerprint,
    ImmutableArray<ModerationNativeMode> Modes,
    ImmutableArray<ModerationPolicyClassDefinition> Classes,
    ImmutableArray<ModerationPolicyReasonDefinition> Reasons,
    ImmutableArray<ModerationCurrentPolicyProfileView> Profiles,
    ImmutableArray<ModerationRuleReference> SelectedProfiles)
{
    public static ModerationCurrentPolicyAuthoringState FromHistory(ModerationPolicyCompiledDraft history) =>
        new(history.Proposed.PublicationId, history.Proposed.ExpectedRevision,
            history.Proposed.CatalogueRevision, history.Proposed.InventoryRevision, history.Proposed.InventoryFingerprint,
            history.Proposed.Modes, history.Proposed.Classes, history.Proposed.Reasons,
            history.Profiles.Select(profile => new ModerationCurrentPolicyProfileView(
                profile.Definition, profile.Consequences)).ToImmutableArray(), history.Proposed.SupportedProfiles);

    public override string ToString() => nameof(ModerationCurrentPolicyAuthoringState);
}

/// <summary>Only terminal consumption can acquire and expose a view; the handler retains no policy data.</summary>
public sealed class ModerationCurrentPolicyReadResult : IAsyncDisposable
{
    public ModerationCurrentPolicyReadResult(ModerationCurrentPolicyReadOutcome outcome) => Outcome = outcome;

    internal ModerationCurrentPolicyReadResult(
        IModerationPolicyPublicationRepository repository, ModerationCurrentPolicyReadRequest request,
        CancellationToken originCancellation, Guid? prospectivePolicyId = null)
    {
        Outcome = ModerationCurrentPolicyReadOutcome.Allowed;
        Projection = new ModerationCurrentPolicyReadProjection(repository, request, originCancellation, prospectivePolicyId);
    }

    public ModerationCurrentPolicyReadOutcome Outcome { get; }
    [JsonIgnore] public ModerationCurrentPolicyReadProjection? Projection { get; }
    public ValueTask DisposeAsync() => Projection?.DisposeAsync() ?? ValueTask.CompletedTask;
    public override string ToString() => nameof(ModerationCurrentPolicyReadResult);

    public static BaseCommandResponse<Guid> Failure(ModerationCurrentPolicyReadOutcome outcome) => outcome switch
    {
        ModerationCurrentPolicyReadOutcome.Invalid => BaseCommandResponse.Validation<Guid>(["Invalid policy read scope or prospective identity."]),
        ModerationCurrentPolicyReadOutcome.Unauthenticated => BaseCommandResponse.Authentication<Guid>(),
        ModerationCurrentPolicyReadOutcome.Denied => BaseCommandResponse.Authorization<Guid>(),
        _ => BaseCommandResponse.Failure<Guid>("ModerationCurrentPolicyUnavailable")
    };
}
