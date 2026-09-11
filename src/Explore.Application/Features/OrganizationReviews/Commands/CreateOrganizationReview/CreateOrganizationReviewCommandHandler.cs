using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.OrganizationReview;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.OrganizationReviews.Commands.CreateOrganizationReview;

public class CreateOrganizationReviewCommandHandler : IRequestHandler<CreateOrganizationReviewCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationReviewRepository _organizationReviewRepository;
    private readonly ITenantContext _tenantContext;

    public CreateOrganizationReviewCommandHandler(IOrganizationReviewRepository organizationReviewRepository, ITenantContext tenantContext)
    {
        _organizationReviewRepository = organizationReviewRepository;
        _tenantContext = tenantContext;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateOrganizationReviewCommand request, CancellationToken cancellationToken)
    {
        var organizationReview = new OrganizationReview
        {
            OrganizationId = request.CreateOrganizationReviewDto.OrganizationId,
            Organization = null!,
            EventId = request.CreateOrganizationReviewDto.ProgramId,
            Event = null!,
            ReviewerName = request.CreateOrganizationReviewDto.ReviewerName,
            Rating = request.CreateOrganizationReviewDto.Rating,
            Comment = request.CreateOrganizationReviewDto.Comment,
            Tenant = null!
        };

        organizationReview.UserId = request.ReviewerUserId;
        organizationReview.CreatedAt = DateTime.UtcNow;
        organizationReview.UpdatedAt = DateTime.UtcNow;
        organizationReview.CreatedBy = request.ReviewerUserId;
        organizationReview.UpdatedBy = request.ReviewerUserId;

        // Set TenantId from the request context
        organizationReview.TenantId = _tenantContext.TenantId;

        organizationReview = await _organizationReviewRepository.Create(organizationReview);

        return BaseCommandResponse.Success(organizationReview.Id, "Review Created Successfully");
    }
}
