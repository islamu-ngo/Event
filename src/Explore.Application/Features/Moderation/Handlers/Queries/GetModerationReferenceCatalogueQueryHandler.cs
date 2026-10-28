using System.Collections.Immutable;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Queries;
using ISLAMU.Moderation.Domain.Policies;

namespace Explore.Application.Features.Moderation.Handlers.Queries;

public sealed class GetModerationReferenceCatalogueQueryHandler
    : IQueryHandler<GetModerationReferenceCatalogueQuery, ModerationReferenceCatalogueView>
{
    public Task<ModerationReferenceCatalogueView> QueryAsync(
        GetModerationReferenceCatalogueQuery request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ModerationReferenceCatalogueView(ModerationReferenceCatalogue.Revision,
            ModerationReferenceCatalogue.Profiles.Values.OrderBy(profile => profile.Id).ToImmutableArray()));
    }
}
