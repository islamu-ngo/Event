using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Seo;
using Explore.Application.Exceptions;
using Explore.Application.Features.Seo.Requests.Queries;

namespace Explore.Application.Features.Seo.Handlers.Queries;

public sealed class GetSitemapEventsQueryHandler(IEventRepository eventRepository,
    ITenantContext tenantContext, IVisitorTenantReadAccessPolicy visitorRead)
    : IQueryHandler<GetSitemapEventsQuery, IReadOnlyList<SitemapEventEntryDto>>
{
    private const int SitemapProtocolUrlLimit = 50_000;

    public async Task<IReadOnlyList<SitemapEventEntryDto>> QueryAsync(
        GetSitemapEventsQuery request,
        CancellationToken cancellationToken)
    {
        var decision = await visitorRead.CheckPublicAsync(tenantContext.TenantId, cancellationToken);
        if (decision != VisitorTenantReadAccessDecision.Allowed)
            throw new TenantVisitorReadAccessException(decision);

        int maxCount = Math.Clamp(request.MaxCount, 1, SitemapProtocolUrlLimit);
        var events = await eventRepository.GetPublishedPublicEventsForSitemap(maxCount, cancellationToken);

        return events
            .Select(e => new SitemapEventEntryDto(e.Id, e.UpdatedAt ?? e.CreatedAt))
            .ToList();
    }
}
