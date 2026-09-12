using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.DidCustodyType;
using Explore.Application.Features.DidCustodyTypes.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.DidCustodyTypes.Handlers.Queries;

public class GetDidCustodyTypeListRequestHandler : IQueryHandler<GetDidCustodyTypeListRequest, List<DidCustodyTypeListDto>>
{
    private readonly IDidCustodyTypeRepository _didCustodyTypeRepository;

    public GetDidCustodyTypeListRequestHandler(IDidCustodyTypeRepository didCustodyTypeRepository)
    {
        _didCustodyTypeRepository = didCustodyTypeRepository;
    }

    public async Task<List<DidCustodyTypeListDto>> QueryAsync(GetDidCustodyTypeListRequest request, CancellationToken cancellationToken)
    {
        var didCustodyTypes = await _didCustodyTypeRepository.GetAll();
        return didCustodyTypes.Select(DidCustodyTypeMapper.ToListItem).ToList();
    }
}
