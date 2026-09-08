using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Features.AudienceGenders.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AudienceGenders.Handlers.Queries;

public class GetAudienceGenderDetailsRequestHandler : IRequestHandler<GetAudienceGenderDetailsRequest, AudienceGenderDto>
{
    private readonly IAudienceGenderRepository _audienceGenderRepository;
    private readonly IMapper _mapper;

    public GetAudienceGenderDetailsRequestHandler(IAudienceGenderRepository audienceGenderRepository, IMapper mapper)
    {
        _audienceGenderRepository = audienceGenderRepository;
        _mapper = mapper;
    }

    public async Task<AudienceGenderDto> Handle(GetAudienceGenderDetailsRequest request, CancellationToken cancellationToken)
    {
        var audienceGender = await _audienceGenderRepository.GetById(request.Id);
        if (audienceGender == null)
        {
            return null;
        }

        return _mapper.Map<AudienceGenderDto>(audienceGender);
    }
}
