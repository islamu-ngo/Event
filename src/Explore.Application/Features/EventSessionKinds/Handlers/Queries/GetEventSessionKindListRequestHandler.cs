using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.EventSessionKind;
using Explore.Application.Features.EventSessionKinds.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionKinds.Handlers.Queries;

public class GetEventSessionKindListRequestHandler : IQueryHandler<GetEventSessionKindListRequest, List<EventSessionKindListDto>>
{
    private readonly IEventSessionKindRepository _eventSessionKindRepository;

    public GetEventSessionKindListRequestHandler(IEventSessionKindRepository eventSessionKindRepository)
    {
        _eventSessionKindRepository = eventSessionKindRepository;
    }

    public async Task<List<EventSessionKindListDto>> QueryAsync(GetEventSessionKindListRequest request, CancellationToken cancellationToken)
    {
        var kinds = await _eventSessionKindRepository.GetAll();
        return kinds.Select(RegistrationMapper.ToListItem).ToList();
    }
}
