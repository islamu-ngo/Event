using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Category;
using Explore.Application.Features.EventCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventCategories.Handlers.Queries;

public class GetCategoriesByEventRequestHandler : IRequestHandler<GetCategoriesByEventRequest, List<CategoryListDto>>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IMapper _mapper;

    public GetCategoriesByEventRequestHandler(IEventCategoriesRepository eventCategoriesRepository, IMapper mapper)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
        _mapper = mapper;
    }

    public async Task<List<CategoryListDto>> Handle(GetCategoriesByEventRequest request, CancellationToken cancellationToken)
    {
        var categories = await _eventCategoriesRepository.GetCategoriesByEvent(request.EventId);
        return _mapper.Map<List<CategoryListDto>>(categories);
    }
}
