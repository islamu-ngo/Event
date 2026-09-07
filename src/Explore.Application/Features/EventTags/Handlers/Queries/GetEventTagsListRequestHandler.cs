using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventTags;
using Explore.Application.Features.EventTags.Requests.Queries;
using MediatR;
using EventTagsEntity = Explore.Domain.EventTags;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetEventTagsListRequestHandler : IRequestHandler<GetEventTagsListRequest, List<EventTagsListDto>>
{
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IMapper _mapper;

    public GetEventTagsListRequestHandler(IEventTagsRepository eventTagsRepository, IMapper mapper)
    {
        _eventTagsRepository = eventTagsRepository;
        _mapper = mapper;
    }

    public async Task<List<EventTagsListDto>> Handle(GetEventTagsListRequest request, CancellationToken cancellationToken)
    {
        var eventTags = await _eventTagsRepository.GetAll();
        return _mapper.Map<List<EventTagsListDto>>(eventTags);
    }
}
