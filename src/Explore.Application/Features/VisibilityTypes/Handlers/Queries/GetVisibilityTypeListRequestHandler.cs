using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Features.VisibilityTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.VisibilityTypes.Handlers.Queries;

public class GetVisibilityTypeListRequestHandler : IRequestHandler<GetVisibilityTypeListRequest, List<VisibilityTypeListDto>>
{
    private readonly IVisibilityTypeRepository _visibilityTypeRepository;

    public GetVisibilityTypeListRequestHandler(IVisibilityTypeRepository visibilityTypeRepository)
    {
        _visibilityTypeRepository = visibilityTypeRepository;
    }

    public async Task<List<VisibilityTypeListDto>> Handle(GetVisibilityTypeListRequest request, CancellationToken cancellationToken)
    {
        var visibilityTypes = await _visibilityTypeRepository.GetAll();
        return visibilityTypes.Select(VisibilityTypeMapper.ToListItem).ToList();
    }
}
