using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Madhab;
using Explore.Application.Features.Madhabs.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Madhabs.Handlers.Queries;

public class GetMadhabDetailsRequestHandler : IRequestHandler<GetMadhabDetailsRequest, MadhabDto>
{
    private readonly IMadhabRepository _madhabRepository;
    private readonly IMapper _mapper;

    public GetMadhabDetailsRequestHandler(IMadhabRepository madhabRepository, IMapper mapper)
    {
        _madhabRepository = madhabRepository;
        _mapper = mapper;
    }

    public async Task<MadhabDto> Handle(GetMadhabDetailsRequest request, CancellationToken cancellationToken)
    {
        var madhab = await _madhabRepository.GetById(request.Id);
        return _mapper.Map<MadhabDto>(madhab);
    }
}
