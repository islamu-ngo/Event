using AutoMapper;
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
    private readonly IMapper _mapper;

    public CreateOrganizationReviewCommandHandler(IOrganizationReviewRepository organizationReviewRepository, ITenantContext tenantContext, IMapper mapper)
    {
        _organizationReviewRepository = organizationReviewRepository;
        _tenantContext = tenantContext;
        _mapper = mapper;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateOrganizationReviewCommand request, CancellationToken cancellationToken)
    {
        var organizationReview = _mapper.Map<OrganizationReview>(request.CreateOrganizationReviewDto);

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
