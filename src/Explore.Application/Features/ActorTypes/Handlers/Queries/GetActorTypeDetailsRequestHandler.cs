using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Features.ActorTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.ActorTypes.Handlers.Queries;

public class GetActorTypeDetailsRequestHandler : IQueryHandler<GetActorTypeDetailsRequest, ActorTypeDto?>
{
    private readonly IActorTypeRepository _actorTypeRepository;

    public GetActorTypeDetailsRequestHandler(IActorTypeRepository actorTypeRepository)
    {
        _actorTypeRepository = actorTypeRepository;
    }

    public async Task<ActorTypeDto?> QueryAsync(GetActorTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var actorType = await _actorTypeRepository.GetById(request.Id);
        return ActorTypeMapper.ToDetail(actorType);
    }
}
