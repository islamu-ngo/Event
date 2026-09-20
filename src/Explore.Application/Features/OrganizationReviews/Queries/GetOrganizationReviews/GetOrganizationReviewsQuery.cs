using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;

public sealed record GetOrganizationReviewsQuery(Guid OrganizationId = default) : IQuery<List<OrganizationReviewDto>>;
