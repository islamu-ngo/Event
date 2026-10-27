using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Events.Requests.Queries;

namespace Explore.Application.Features.Events.Handlers.Queries;

public sealed class GetEventCalendarExportRequestHandler(
    IModerationPublicEventCalendarRepository repository, ITenantContext tenant,
    IEventLocationDisclosureService locations, TimeProvider clock)
    : IQueryHandler<GetEventCalendarExportRequest, PublicEventCalendarProjection?>
{
    public Task<PublicEventCalendarProjection?> QueryAsync(
        GetEventCalendarExportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        PublicEventCalendarProjection? projection = request.EventId == Guid.Empty || tenant.TenantId == Guid.Empty
            ? null : new(repository, new(tenant.TenantId, request.EventId), locations, clock, cancellationToken);
        return Task.FromResult(projection);
    }
}
