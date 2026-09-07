using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.Features.EventSessionKinds.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionKinds.Handlers.Queries;

public class GetEventSessionKindListRequestHandler : IRequestHandler<GetEventSessionKindListRequest, List<EventSessionKindListDto>>
{
    private readonly IEventSessionKindRepository _eventSessionKindRepository;
    private readonly IMapper _mapper;

    public GetEventSessionKindListRequestHandler(IEventSessionKindRepository eventSessionKindRepository, IMapper mapper)
    {
        _eventSessionKindRepository = eventSessionKindRepository;
        _mapper = mapper;
    }

    public async Task<List<EventSessionKindListDto>> Handle(GetEventSessionKindListRequest request, CancellationToken cancellationToken)
    {
        var kinds = await _eventSessionKindRepository.GetAll();
        return _mapper.Map<List<EventSessionKindListDto>>(kinds);
    }
}
