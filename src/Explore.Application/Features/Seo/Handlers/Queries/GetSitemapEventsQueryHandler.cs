using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Seo;
using Explore.Application.Features.Seo.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Seo.Handlers.Queries;

public sealed class GetSitemapEventsQueryHandler(IEventRepository eventRepository)
    : IRequestHandler<GetSitemapEventsQuery, IReadOnlyList<SitemapEventEntryDto>>
{
    private const int SitemapProtocolUrlLimit = 50_000;

    public async Task<IReadOnlyList<SitemapEventEntryDto>> Handle(
        GetSitemapEventsQuery request,
        CancellationToken cancellationToken)
    {
        int maxCount = Math.Clamp(request.MaxCount, 1, SitemapProtocolUrlLimit);
        var events = await eventRepository.GetPublishedPublicEventsForSitemap(maxCount, cancellationToken);

        return events
            .Select(e => new SitemapEventEntryDto(e.Id, e.UpdatedAt ?? e.CreatedAt))
            .ToList();
    }
}
