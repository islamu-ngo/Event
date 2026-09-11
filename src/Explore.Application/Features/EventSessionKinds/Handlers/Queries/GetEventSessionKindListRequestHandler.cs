using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.Features.EventSessionKinds.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.EventSessionKinds.Handlers.Queries;

public class GetEventSessionKindListRequestHandler : IRequestHandler<GetEventSessionKindListRequest, List<EventSessionKindListDto>>
{
    private readonly IEventSessionKindRepository _eventSessionKindRepository;

    public GetEventSessionKindListRequestHandler(IEventSessionKindRepository eventSessionKindRepository)
    {
        _eventSessionKindRepository = eventSessionKindRepository;
    }

    public async Task<List<EventSessionKindListDto>> Handle(GetEventSessionKindListRequest request, CancellationToken cancellationToken)
    {
        var kinds = await _eventSessionKindRepository.GetAll();
        return kinds.Select(RegistrationMapper.ToListItem).ToList();
    }
}
