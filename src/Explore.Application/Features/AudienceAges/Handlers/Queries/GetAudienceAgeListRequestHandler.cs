using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.AudienceAges.Handlers.Queries;

public class GetAudienceAgeListRequestHandler : IQueryHandler<GetAudienceAgeListRequest, List<AudienceAgeListDto>>
{
    private readonly IAudienceAgeRepository _audienceAgeRepository;

    public GetAudienceAgeListRequestHandler(IAudienceAgeRepository audienceAgeRepository)
    {
        _audienceAgeRepository = audienceAgeRepository;
    }

    public async Task<List<AudienceAgeListDto>> QueryAsync(GetAudienceAgeListRequest query, CancellationToken cancellationToken)
    {
        var audienceAges = await _audienceAgeRepository.GetAll();
        return audienceAges.Select(EventMapper.ToListItem).ToList();
    }
}
