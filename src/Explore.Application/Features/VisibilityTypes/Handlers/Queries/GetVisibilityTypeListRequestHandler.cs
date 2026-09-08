using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Features.VisibilityTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.VisibilityTypes.Handlers.Queries;

public class GetVisibilityTypeListRequestHandler : IRequestHandler<GetVisibilityTypeListRequest, List<VisibilityTypeListDto>>
{
    private readonly IVisibilityTypeRepository _visibilityTypeRepository;
    private readonly IMapper _mapper;

    public GetVisibilityTypeListRequestHandler(IVisibilityTypeRepository visibilityTypeRepository, IMapper mapper)
    {
        _visibilityTypeRepository = visibilityTypeRepository;
        _mapper = mapper;
    }

    public async Task<List<VisibilityTypeListDto>> Handle(GetVisibilityTypeListRequest request, CancellationToken cancellationToken)
    {
        var visibilityTypes = await _visibilityTypeRepository.GetAll();
        return _mapper.Map<List<VisibilityTypeListDto>>(visibilityTypes);
    }
}
