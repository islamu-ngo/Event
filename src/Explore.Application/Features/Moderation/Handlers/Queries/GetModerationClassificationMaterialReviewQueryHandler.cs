using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;

namespace Explore.Application.Features.Moderation.Handlers.Queries;

public sealed class GetModerationClassificationMaterialReviewQueryHandler(
    IModerationClassificationMaterialReviewReadRepository repository, IAdminContext identity, ITenantContext tenant)
    : IQueryHandler<GetModerationClassificationMaterialReviewQuery, ModerationClassificationMaterialReviewReadResult>
{
    public async Task<ModerationClassificationMaterialReviewReadResult> QueryAsync(
        GetModerationClassificationMaterialReviewQuery query, CancellationToken cancellationToken = default)
    {
        if (query.SourceId == Guid.Empty || query.SourceId.Version != 7 || query.SourceId.Variant is < 8 or > 11)
            return new(ModerationCurrentPolicyReadOutcome.Invalid);
        var human = await identity.ResolveUserIdAsync(cancellationToken);
        return new(repository, new(tenant.TenantId, human, query.SourceId), cancellationToken);
    }
}
