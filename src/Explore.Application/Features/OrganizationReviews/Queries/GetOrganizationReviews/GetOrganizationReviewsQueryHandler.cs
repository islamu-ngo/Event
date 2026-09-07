using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationReview;
using MediatR;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;

public class GetOrganizationReviewsQueryHandler : IRequestHandler<GetOrganizationReviewsQuery, List<OrganizationReviewDto>>
{
    private readonly IOrganizationReviewRepository _organizationReviewRepository;
    private readonly IMapper _mapper;

    public GetOrganizationReviewsQueryHandler(IOrganizationReviewRepository organizationReviewRepository, IMapper mapper)
    {
        _organizationReviewRepository = organizationReviewRepository;
        _mapper = mapper;
    }

    public async Task<List<OrganizationReviewDto>> Handle(GetOrganizationReviewsQuery request, CancellationToken cancellationToken)
    {
        var reviews = await _organizationReviewRepository.GetByOrganizationId(request.OrganizationId);
        return _mapper.Map<List<OrganizationReviewDto>>(reviews);
    }
}
