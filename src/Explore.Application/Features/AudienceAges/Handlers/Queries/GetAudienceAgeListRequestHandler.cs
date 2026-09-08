using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceAge;
using Explore.Application.Features.AudienceAges.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AudienceAges.Handlers.Queries;

public class GetAudienceAgeListRequestHandler : IRequestHandler<GetAudienceAgeListRequest, List<AudienceAgeListDto>>
{
    private readonly IAudienceAgeRepository _audienceAgeRepository;
    private readonly IMapper _mapper;

    public GetAudienceAgeListRequestHandler(IAudienceAgeRepository audienceAgeRepository, IMapper mapper)
    {
        _audienceAgeRepository = audienceAgeRepository;
        _mapper = mapper;
    }

    public async Task<List<AudienceAgeListDto>> Handle(GetAudienceAgeListRequest request, CancellationToken cancellationToken)
    {
        var audienceAges = await _audienceAgeRepository.GetAll();
        return _mapper.Map<List<AudienceAgeListDto>>(audienceAges);
    }
}
