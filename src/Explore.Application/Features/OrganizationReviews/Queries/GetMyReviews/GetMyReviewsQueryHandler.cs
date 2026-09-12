using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;

public class GetMyReviewsQueryHandler : IQueryHandler<GetMyReviewsQuery, List<OrganizationReviewDto>>
{
    private readonly IOrganizationReviewRepository _organizationReviewRepository;

    public GetMyReviewsQueryHandler(IOrganizationReviewRepository organizationReviewRepository)
    {
        _organizationReviewRepository = organizationReviewRepository;
    }

    public async Task<List<OrganizationReviewDto>> QueryAsync(GetMyReviewsQuery request, CancellationToken cancellationToken)
    {
        var reviews = await _organizationReviewRepository.GetByUserId(request.UserId);
        return reviews.Select(OrganizationMapper.ToOrganizationReview).ToList();
    }
}
