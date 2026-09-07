using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Features.ActorTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ActorTypes.Handlers.Queries;

public class GetActorTypeDetailsRequestHandler : IRequestHandler<GetActorTypeDetailsRequest, ActorTypeDto>
{
    private readonly IActorTypeRepository _actorTypeRepository;
    private readonly IMapper _mapper;

    public GetActorTypeDetailsRequestHandler(IActorTypeRepository actorTypeRepository, IMapper mapper)
    {
        _actorTypeRepository = actorTypeRepository;
        _mapper = mapper;
    }

    public async Task<ActorTypeDto> Handle(GetActorTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var actorType = await _actorTypeRepository.GetById(request.Id);
        return _mapper.Map<ActorTypeDto>(actorType);
    }
}
