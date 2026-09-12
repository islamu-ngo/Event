using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetMyReviews;

public sealed record GetMyReviewsQuery(Guid UserId = default) : IQuery<List<OrganizationReviewDto>>;
