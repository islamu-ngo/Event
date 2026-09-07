using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tag;
using Explore.Application.Features.EventTags.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventTags.Handlers.Queries;

public class GetTagsByEventRequestHandler : IRequestHandler<GetTagsByEventRequest, List<TagListDto>>
{
    private readonly IEventTagsRepository _eventTagsRepository;
    private readonly IMapper _mapper;

    public GetTagsByEventRequestHandler(IEventTagsRepository eventTagsRepository, IMapper mapper)
    {
        _eventTagsRepository = eventTagsRepository;
        _mapper = mapper;
    }

    public async Task<List<TagListDto>> Handle(GetTagsByEventRequest request, CancellationToken cancellationToken)
    {
        var tags = await _eventTagsRepository.GetTagsByEvent(request.EventId);
        return _mapper.Map<List<TagListDto>>(tags);
    }
}
