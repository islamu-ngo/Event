using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Requests.Queries;

namespace Explore.Application.Features.Moderation.Handlers.Queries;

public sealed class GetModerationClassificationMaterialInventoryQueryHandler(
    IModerationClassificationMaterialInventoryReadRepository repository, IAdminContext identity, ITenantContext tenant)
    : IQueryHandler<GetModerationClassificationMaterialInventoryQuery, ModerationClassificationMaterialInventoryReadResult>
{
    public async Task<ModerationClassificationMaterialInventoryReadResult> QueryAsync(
        GetModerationClassificationMaterialInventoryQuery query, CancellationToken cancellationToken = default)
    {
        if (new[] { query.EventGenerationId, query.TaxonomyId, query.ProfileId }
                .Any(id => id == Guid.Empty || id.Version != 7 || id.Variant is < 8 or > 11)
            || !Enum.IsDefined(query.Mode) || query.Mandate is null)
            return new(ModerationCurrentPolicyReadOutcome.Invalid);
        try { query.Mandate.Validate(); }
        catch (ArgumentException) { return new(ModerationCurrentPolicyReadOutcome.Invalid); }
        // These are server identity coordinates, not an assertion of permission or complete material.
        var human = await identity.ResolveUserIdAsync(cancellationToken);
        return new(repository, new(tenant.TenantId, human, query.EventGenerationId,
            query.TaxonomyId, query.ProfileId, query.Mode, query.Mandate), cancellationToken);
    }
}
