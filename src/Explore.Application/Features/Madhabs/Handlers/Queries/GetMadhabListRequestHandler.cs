using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Madhab;
using Explore.Application.Features.Madhabs.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Madhabs.Handlers.Queries;

public class GetMadhabListRequestHandler : IRequestHandler<GetMadhabListRequest, List<MadhabListDto>>
{
    private readonly IMadhabRepository _madhabRepository;
    private readonly IMapper _mapper;

    public GetMadhabListRequestHandler(IMadhabRepository madhabRepository, IMapper mapper)
    {
        _madhabRepository = madhabRepository;
        _mapper = mapper;
    }

    public async Task<List<MadhabListDto>> Handle(GetMadhabListRequest request, CancellationToken cancellationToken)
    {
        var madhabs = await _madhabRepository.GetAll();
        return _mapper.Map<List<MadhabListDto>>(madhabs);
    }
}
