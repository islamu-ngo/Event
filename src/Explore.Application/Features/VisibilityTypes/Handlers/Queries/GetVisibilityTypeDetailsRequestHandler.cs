using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Features.VisibilityTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.VisibilityTypes.Handlers.Queries;

public class GetVisibilityTypeDetailsRequestHandler : IRequestHandler<GetVisibilityTypeDetailsRequest, VisibilityTypeDto>
{
    private readonly IVisibilityTypeRepository _visibilityTypeRepository;
    private readonly IMapper _mapper;

    public GetVisibilityTypeDetailsRequestHandler(IVisibilityTypeRepository visibilityTypeRepository, IMapper mapper)
    {
        _visibilityTypeRepository = visibilityTypeRepository;
        _mapper = mapper;
    }

    public async Task<VisibilityTypeDto> Handle(GetVisibilityTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var visibilityType = await _visibilityTypeRepository.GetById(request.Id);
        return _mapper.Map<VisibilityTypeDto>(visibilityType);
    }
}
