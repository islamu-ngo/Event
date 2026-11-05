using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Requests.Queries;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetEventCalendarExportRequestHandler(
    IModerationPublicEventCalendarRepository repository, ITenantContext tenant,
    IEventLocationDisclosureService locations, TimeProvider clock,
    IVisitorTenantReadAccessPolicy visitorReadAccess)
    : IQueryHandler<GetEventCalendarExportRequest, PublicEventCalendarProjection?>
{
    public async Task<PublicEventCalendarProjection?> QueryAsync(
        GetEventCalendarExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var tenantId = tenant.TenantId;
        if (request.EventId == Guid.Empty || tenantId == Guid.Empty)
            return null;

        await visitorReadAccess.RequireAsync(tenantId, cancellationToken);
        return new(repository, new(tenantId, request.EventId), locations, clock, cancellationToken);
    }
}
