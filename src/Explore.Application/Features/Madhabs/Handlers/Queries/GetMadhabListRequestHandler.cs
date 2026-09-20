using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Madhab;
using Explore.Application.Features.Madhabs.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Madhabs.Handlers.Queries;

public class GetMadhabListRequestHandler : IQueryHandler<GetMadhabListRequest, List<MadhabListDto>>
{
    private readonly IMadhabRepository _madhabRepository;

    public GetMadhabListRequestHandler(IMadhabRepository madhabRepository)
    {
        _madhabRepository = madhabRepository;
    }

    public async Task<List<MadhabListDto>> QueryAsync(GetMadhabListRequest request, CancellationToken cancellationToken)
    {
        var madhabs = await _madhabRepository.GetAll();
        return madhabs.Select(MadhabMapper.ToListItem).ToList();
    }
}
