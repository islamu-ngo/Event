using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Features.ActorTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ActorTypes.Handlers.Queries;

public class GetActorTypeListRequestHandler : IQueryHandler<GetActorTypeListRequest, List<ActorTypeListDto>>
{
    private readonly IActorTypeRepository _actorTypeRepository;

    public GetActorTypeListRequestHandler(IActorTypeRepository actorTypeRepository)
    {
        _actorTypeRepository = actorTypeRepository;
    }

    public async Task<List<ActorTypeListDto>> QueryAsync(GetActorTypeListRequest request, CancellationToken cancellationToken)
    {
        var actorTypes = await _actorTypeRepository.GetAll();
        return actorTypes.Select(ActorTypeMapper.ToListItem).ToList();
    }
}
