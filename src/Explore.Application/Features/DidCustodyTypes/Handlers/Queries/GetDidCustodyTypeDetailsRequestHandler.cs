using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.Features.DidCustodyTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.DidCustodyTypes.Handlers.Queries;

public class GetDidCustodyTypeDetailsRequestHandler : IQueryHandler<GetDidCustodyTypeDetailsRequest, DidCustodyTypeDto?>
{
    private readonly IDidCustodyTypeRepository _didCustodyTypeRepository;

    public GetDidCustodyTypeDetailsRequestHandler(IDidCustodyTypeRepository didCustodyTypeRepository)
    {
        _didCustodyTypeRepository = didCustodyTypeRepository;
    }

    public async Task<DidCustodyTypeDto?> QueryAsync(GetDidCustodyTypeDetailsRequest request, CancellationToken cancellationToken)
    {
        var didCustodyType = await _didCustodyTypeRepository.GetById(request.Id);
        return DidCustodyTypeMapper.ToDetail(didCustodyType);
    }
}
