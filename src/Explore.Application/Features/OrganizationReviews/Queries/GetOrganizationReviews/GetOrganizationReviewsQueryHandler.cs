using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationReview;
using MediatR;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;

public class GetOrganizationReviewsQueryHandler : IRequestHandler<GetOrganizationReviewsQuery, List<OrganizationReviewDto>>
{
    private readonly IOrganizationReviewRepository _organizationReviewRepository;

    public GetOrganizationReviewsQueryHandler(IOrganizationReviewRepository organizationReviewRepository)
    {
        _organizationReviewRepository = organizationReviewRepository;
    }

    public async Task<List<OrganizationReviewDto>> Handle(GetOrganizationReviewsQuery request, CancellationToken cancellationToken)
    {
        var reviews = await _organizationReviewRepository.GetByOrganizationId(request.OrganizationId);
        return reviews.Select(OrganizationMapper.ToOrganizationReview).ToList();
    }
}
