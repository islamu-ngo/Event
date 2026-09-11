using System;
using System.Collections.Generic;
using System.Text;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.AudienceGender;
using Explore.Application.Features.AudienceGenders.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.AudienceGenders.Handlers.Queries;

public class GetAudienceGenderListRequestHandler : IRequestHandler<GetAudienceGenderListRequest, List<AudienceGenderListDto>>
{
    private readonly IAudienceGenderRepository _audienceGenderRepository;

    public GetAudienceGenderListRequestHandler(IAudienceGenderRepository audienceGenderRepository)
    {
        _audienceGenderRepository = audienceGenderRepository;
    }

    public async Task<List<AudienceGenderListDto>> Handle(GetAudienceGenderListRequest request, CancellationToken cancellationToken)
    {
        var audienceGenders = await _audienceGenderRepository.GetAll();
        return audienceGenders.Select(EventMapper.ToListItem).ToList();
    }
}
