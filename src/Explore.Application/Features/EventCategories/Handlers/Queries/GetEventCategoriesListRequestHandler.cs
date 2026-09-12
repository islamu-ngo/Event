using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Features.EventCategories.Requests.Queries;
using MediatR;
using EventCategoriesEntity = Explore.Domain.EventCategories;

namespace Explore.Application.Features.EventCategories.Handlers.Queries;

public class GetEventCategoriesListRequestHandler : IRequestHandler<GetEventCategoriesListRequest, List<EventCategoriesListDto>>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;

    public GetEventCategoriesListRequestHandler(IEventCategoriesRepository eventCategoriesRepository)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
    }

    public async Task<List<EventCategoriesListDto>> Handle(GetEventCategoriesListRequest request, CancellationToken cancellationToken)
    {
        var eventCategories = await _eventCategoriesRepository.GetAll();
        return eventCategories.Select(EventMapper.ToListItem).ToList();
    }
}
