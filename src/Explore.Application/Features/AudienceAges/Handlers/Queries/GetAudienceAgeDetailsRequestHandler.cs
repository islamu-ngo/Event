using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AudienceAges.Handlers.Queries;

public class GetAudienceAgeDetailsRequestHandler : IRequestHandler<GetAudienceAgeDetailsRequest, AudienceAgeDto>
{
    private readonly IAudienceAgeRepository _audienceAgeRepository;

    public GetAudienceAgeDetailsRequestHandler(IAudienceAgeRepository audienceAgeRepository)
    {
        _audienceAgeRepository = audienceAgeRepository;
    }

    public async Task<AudienceAgeDto> Handle(GetAudienceAgeDetailsRequest request, CancellationToken cancellationToken)
    {
        var audienceAge = await _audienceAgeRepository.GetById(request.Id);
        if (audienceAge == null)
        {
            return null;
        }

        return EventMapper.ToDetail(audienceAge);
    }
}
