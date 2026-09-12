using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.AudienceAges.Handlers.Queries;

public class GetAudienceAgeDetailsRequestHandler : IQueryHandler<GetAudienceAgeDetailsRequest, AudienceAgeDto>
{
    private readonly IAudienceAgeRepository _audienceAgeRepository;

    public GetAudienceAgeDetailsRequestHandler(IAudienceAgeRepository audienceAgeRepository)
    {
        _audienceAgeRepository = audienceAgeRepository;
    }

    public async Task<AudienceAgeDto> QueryAsync(GetAudienceAgeDetailsRequest query, CancellationToken cancellationToken)
    {
        var audienceAge = await _audienceAgeRepository.GetById(query.Id);
        if (audienceAge == null)
        {
            return null;
        }

        return EventMapper.ToDetail(audienceAge);
    }
}
