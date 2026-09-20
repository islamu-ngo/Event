using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Features.EventCategories.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Handlers.Queries;

public class GetEventCategoriesDetailsRequestHandler : IQueryHandler<GetEventCategoriesDetailsRequest, EventCategoriesDto>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;

    public GetEventCategoriesDetailsRequestHandler(IEventCategoriesRepository eventCategoriesRepository)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
    }

    public async Task<EventCategoriesDto> QueryAsync(GetEventCategoriesDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventCategories = await _eventCategoriesRepository.GetById(request.Id);
        // The existing request contract is non-nullable; a missing relationship still returns null.
        return EventMapper.ToDetail(eventCategories)!;
    }
}
