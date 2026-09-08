using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AudienceAges.Handlers.Queries;

public class GetAudienceAgeDetailsRequestHandler : IRequestHandler<GetAudienceAgeDetailsRequest, AudienceAgeDto>
{
    private readonly IAudienceAgeRepository _audienceAgeRepository;
    private readonly IMapper _mapper;

    public GetAudienceAgeDetailsRequestHandler(IAudienceAgeRepository audienceAgeRepository, IMapper mapper)
    {
        _audienceAgeRepository = audienceAgeRepository;
        _mapper = mapper;
    }

    public async Task<AudienceAgeDto> Handle(GetAudienceAgeDetailsRequest request, CancellationToken cancellationToken)
    {
        var audienceAge = await _audienceAgeRepository.GetById(request.Id);
        if (audienceAge == null)
        {
            return null;
        }

        return _mapper.Map<AudienceAgeDto>(audienceAge);
    }
}
