using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Authorization;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview;

[AuthorizeResource(ResourceKinds.OrganizationReview, AuthorizationActions.OrganizationReviews.Create)]
public sealed record CreateOrganizationReviewCommand : IRequest<BaseCommandResponse<Guid>>
{
    public required CreateOrganizationReviewDto CreateOrganizationReviewDto { get; init; }
    public Guid ReviewerUserId { get; init; }
}
