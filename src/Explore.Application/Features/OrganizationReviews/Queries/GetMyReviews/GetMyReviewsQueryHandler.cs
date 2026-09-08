using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationReview;
using MediatR;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;

public class GetMyReviewsQueryHandler : IRequestHandler<GetMyReviewsQuery, List<OrganizationReviewDto>>
{
    private readonly IOrganizationReviewRepository _organizationReviewRepository;
    private readonly IMapper _mapper;

    public GetMyReviewsQueryHandler(IOrganizationReviewRepository organizationReviewRepository, IMapper mapper)
    {
        _organizationReviewRepository = organizationReviewRepository;
        _mapper = mapper;
    }

    public async Task<List<OrganizationReviewDto>> Handle(GetMyReviewsQuery request, CancellationToken cancellationToken)
    {
        var reviews = await _organizationReviewRepository.GetByUserId(request.UserId);
        return _mapper.Map<List<OrganizationReviewDto>>(reviews);
    }
}
