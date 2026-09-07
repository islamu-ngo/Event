using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionSpeaker;
using Explore.Application.Features.EventSessionSpeakers.Requests.Queries;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.EventSessionSpeakers.Handlers.Queries;

public class GetEventSessionSpeakerListRequestHandler : IRequestHandler<GetEventSessionSpeakerListRequest, PaginatedResult<EventSessionSpeakerListDto>>
{
    private readonly IEventSessionSpeakerRepository _speakerRepository;
    private readonly IMapper _mapper;

    public GetEventSessionSpeakerListRequestHandler(
        IEventSessionSpeakerRepository speakerRepository,
        IMapper mapper)
    {
        _speakerRepository = speakerRepository;
        _mapper = mapper;
    }

    public async Task<PaginatedResult<EventSessionSpeakerListDto>> Handle(GetEventSessionSpeakerListRequest request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = PaginatedResult<EventSessionSpeakerListDto>.NormalizeParameters(request.PageNumber, request.PageSize);
        var (speakers, totalCount) = await _speakerRepository.GetSpeakersWithDetailsPaged(
            pageNumber,
            pageSize,
            cancellationToken);
        var dtos = _mapper.Map<List<EventSessionSpeakerListDto>>(speakers);
        return PaginatedResult<EventSessionSpeakerListDto>.Create(dtos, totalCount, pageNumber, pageSize);
    }
}
