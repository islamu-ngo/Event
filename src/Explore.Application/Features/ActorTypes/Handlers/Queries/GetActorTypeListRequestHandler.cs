using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ActorType;
using Explore.Application.Features.ActorTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ActorTypes.Handlers.Queries;

public class GetActorTypeListRequestHandler : IRequestHandler<GetActorTypeListRequest, List<ActorTypeListDto>>
{
    private readonly IActorTypeRepository _actorTypeRepository;
    private readonly IMapper _mapper;

    public GetActorTypeListRequestHandler(IActorTypeRepository actorTypeRepository, IMapper mapper)
    {
        _actorTypeRepository = actorTypeRepository;
        _mapper = mapper;
    }

    public async Task<List<ActorTypeListDto>> Handle(GetActorTypeListRequest request, CancellationToken cancellationToken)
    {
        var actorTypes = await _actorTypeRepository.GetAll();
        return _mapper.Map<List<ActorTypeListDto>>(actorTypes);
    }
}
