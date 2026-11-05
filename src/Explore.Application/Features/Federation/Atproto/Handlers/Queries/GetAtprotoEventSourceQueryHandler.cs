using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Federation.Atproto.Requests.Queries;
using Explore.Application.Services.Federation;
using Explore.Domain.Federation;

namespace Explore.Application.Features.Federation.Atproto.Handlers.Queries;

public sealed class GetAtprotoEventSourceQueryHandler(
    IAtprotoEventProjectionRepository projectionRepository,
    AtprotoEventGovernanceResolver governanceResolver,
    Explore.Application.Contracts.Infrastructure.ITenantContext tenantContext,
    IVisitorTenantReadAccessPolicy visitorReadAccess)
    : IQueryHandler<GetAtprotoEventSourceQuery, string?>
{
    public async Task<string?> QueryAsync(GetAtprotoEventSourceQuery request, CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId;
        await visitorReadAccess.RequireAsync(tenantId, cancellationToken);
        AtprotoEventGovernance governance = await governanceResolver.ResolveAsync(
            tenantId,
            null,
            cancellationToken);
        if (!governance.EventsEnabled)
        {
            return null;
        }

        AtprotoEventProjection? projection = await projectionRepository.GetVisibleByRecordIdAsync(
            request.AtprotoRecordId,
            cancellationToken);
        return AtprotoExternalUriPolicy.Normalize(projection?.SourceUrl);
    }
}
