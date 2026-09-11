using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Features.ActorTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ActorTypes.Handlers.Queries;

public class GetActorTypeDetailsRequestHandler : IRequestHandler<GetActorTypeDetailsRequest, ActorTypeDto>
{
    private readonly IActorTypeRepository _actorTypeRepository;

    public GetActorTypeDetailsRequestHandler(IActorTypeRepository actorTypeRepository)
    {
        _actorTypeRepository = actorTypeRepository;
    }

    public async Task<ActorTypeDto> Handle(GetActorTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var actorType = await _actorTypeRepository.GetById(request.Id);
        return ActorTypeMapper.ToDetail(actorType)!;
    }
}
