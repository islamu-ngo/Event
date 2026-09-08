using Explore.Application.DTOs.OrganizationReview;
using MediatR;

namespace Explore.Application.Features.OrganizationReviews.Queries.GetOrganizationReviews;

public sealed record GetOrganizationReviewsQuery(Guid OrganizationId = default) : IRequest<List<OrganizationReviewDto>>;
