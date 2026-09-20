using System.Threading;
using System.Threading.Tasks;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Madhab;
using Explore.Application.Features.Madhabs.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.Madhabs.Handlers.Queries;

public class GetMadhabDetailsRequestHandler : IQueryHandler<GetMadhabDetailsRequest, MadhabDto?>
{
    private readonly IMadhabRepository _madhabRepository;

    public GetMadhabDetailsRequestHandler(IMadhabRepository madhabRepository)
    {
        _madhabRepository = madhabRepository;
    }

    public async Task<MadhabDto?> QueryAsync(GetMadhabDetailsRequest request, CancellationToken cancellationToken)
    {
        var madhab = await _madhabRepository.GetById(request.Id);
        return MadhabMapper.ToDetail(madhab);
    }
}
