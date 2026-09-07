using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Features.EventTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetEventTagsDetailsRequestHandler : IRequestHandler<GetEventTagsDetailsRequest, EventTagsDto>
{
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IMapper _mapper;

    public GetEventTagsDetailsRequestHandler(IEventTagsRepository eventTagsRepository, IMapper mapper)
    {
        _eventTagsRepository = eventTagsRepository;
        _mapper = mapper;
    }

    public async Task<EventTagsDto> Handle(GetEventTagsDetailsRequest request, CancellationToken cancellationToken)
    {
        var eventTags = await _eventTagsRepository.GetById(request.Id);
        return _mapper.Map<EventTagsDto>(eventTags);
    }
}
