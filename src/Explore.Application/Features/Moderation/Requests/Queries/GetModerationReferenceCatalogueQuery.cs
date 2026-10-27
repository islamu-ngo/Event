using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Requests.Queries;

public sealed record GetModerationReferenceCatalogueQuery : IQuery<ModerationReferenceCatalogueView>;

/// <summary>
/// Immutable public reference definitions, not adopted policy, current support or deployment evidence.
/// Nested profiles are immutable Domain values without operational subjects or private case data.
/// </summary>
public sealed record ModerationReferenceCatalogueView(
    long Revision, ImmutableArray<ModerationReferenceProfile> Profiles);
