using System;
using System.Collections.Generic;
using System.Text;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Features.AudienceGenders.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AudienceGenders.Handlers.Queries;

public class GetAudienceGenderListRequestHandler : IRequestHandler<GetAudienceGenderListRequest, List<AudienceGenderListDto>>
{
    private readonly IAudienceGenderRepository _audienceGenderRepository;
    private readonly IMapper _mapper;

    public GetAudienceGenderListRequestHandler(IAudienceGenderRepository audienceGenderRepository, IMapper mapper)
    {
        _audienceGenderRepository = audienceGenderRepository;
        _mapper = mapper;
    }

    public async Task<List<AudienceGenderListDto>> Handle(GetAudienceGenderListRequest request, CancellationToken cancellationToken)
    {
        var audienceGenders = await _audienceGenderRepository.GetAll();
        return _mapper.Map<List<AudienceGenderListDto>>(audienceGenders);
    }
}
