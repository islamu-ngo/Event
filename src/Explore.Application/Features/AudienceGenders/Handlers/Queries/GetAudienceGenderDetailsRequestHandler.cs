using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Features.AudienceGenders.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.AudienceGenders.Handlers.Queries;

public class GetAudienceGenderDetailsRequestHandler : IQueryHandler<GetAudienceGenderDetailsRequest, AudienceGenderDto?>
{
    private readonly IAudienceGenderRepository _audienceGenderRepository;

    public GetAudienceGenderDetailsRequestHandler(IAudienceGenderRepository audienceGenderRepository)
    {
        _audienceGenderRepository = audienceGenderRepository;
    }

    public async Task<AudienceGenderDto?> QueryAsync(GetAudienceGenderDetailsRequest request, CancellationToken cancellationToken)
    {
        var audienceGender = await _audienceGenderRepository.GetById(request.Id);
        if (audienceGender == null)
        {
            return null;
        }

        return EventMapper.ToDetail(audienceGender);
    }
}
