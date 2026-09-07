using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventCategories;
using Explore.Application.Features.EventCategories.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventCategories.Handlers.Queries;

public class GetEventCategoriesDetailsRequestHandler : IRequestHandler<GetEventCategoriesDetailsRequest, EventCategoriesDto>
{
    private readonly IEventCategoriesRepository _eventCategoriesRepository;
    private readonly IMapper _mapper;

    public GetEventCategoriesDetailsRequestHandler(IEventCategoriesRepository eventCategoriesRepository, IMapper mapper)
    {
        _eventCategoriesRepository = eventCategoriesRepository;
        _mapper = mapper;
    }

    public async Task<EventCategoriesDto> Handle(GetEventCategoriesDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventCategories = await _eventCategoriesRepository.GetById(request.Id);
        return _mapper.Map<EventCategoriesDto>(eventCategories);
    }
}
