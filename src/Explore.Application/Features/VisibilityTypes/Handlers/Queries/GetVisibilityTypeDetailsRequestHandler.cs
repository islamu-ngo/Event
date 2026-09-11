using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.VisibilityType;
using Explore.Application.Features.VisibilityTypes.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.VisibilityTypes.Handlers.Queries;

public class GetVisibilityTypeDetailsRequestHandler : IRequestHandler<GetVisibilityTypeDetailsRequest, VisibilityTypeDto>
{
    private readonly IVisibilityTypeRepository _visibilityTypeRepository;

    public GetVisibilityTypeDetailsRequestHandler(IVisibilityTypeRepository visibilityTypeRepository)
    {
        _visibilityTypeRepository = visibilityTypeRepository;
    }

    public async Task<VisibilityTypeDto> Handle(GetVisibilityTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var visibilityType = await _visibilityTypeRepository.GetById(request.Id);
        return VisibilityTypeMapper.ToDetail(visibilityType)!;
    }
}
