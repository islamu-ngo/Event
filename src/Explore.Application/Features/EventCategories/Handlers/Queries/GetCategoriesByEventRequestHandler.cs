using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.EventCategories.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventCategories.Handlers.Queries;

public class GetCategoriesByEventRequestHandler : IQueryHandler<GetCategoriesByEventRequest, List<CategoryListDto>>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;

    public GetCategoriesByEventRequestHandler(IEventCategoriesRepository eventCategoriesRepository)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
    }

    public async Task<List<CategoryListDto>> QueryAsync(GetCategoriesByEventRequest request, CancellationToken cancellationToken)
    {
        var categories = await _eventCategoriesRepository.GetCategoriesByEvent(request.EventId);
        return categories.Select(CustomPropertyMapper.ToListItem).ToList();
    }
}
