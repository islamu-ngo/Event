using System.Linq;
using AutoMapper;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization.Validators;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Organizations.Handlers.Commands;

public class CreateOrganizationCommandHandler : IRequestHandler<CreateOrganizationCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly IAdminContext _adminContext;
    private readonly IAdminCacheInvalidator _adminCacheInvalidator;
    private readonly IMapper _mapper;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;
    private readonly BusinessMetrics _metrics;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IActorRepository actorRepository,
        IStorageObjectRepository storageObjectRepository,
        IAdminContext adminContext,
        IAdminCacheInvalidator adminCacheInvalidator,
        IMapper mapper,
        ITenantContext tenantContext,
        HybridCache cache,
        BusinessMetrics metrics,
        IUnitOfWork unitOfWork)
    {
        _organizationRepository = organizationRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _actorRepository = actorRepository;
        _storageObjectRepository = storageObjectRepository;
        _adminContext = adminContext;
        _adminCacheInvalidator = adminCacheInvalidator;
        _mapper = mapper;
        _tenantContext = tenantContext;
        _cache = cache;
        _metrics = metrics;
        _unitOfWork = unitOfWork;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var validator = new CreateOrganizationDtoValidator();
        var validationResult = await validator.ValidateAsync(request.OrganizationDto, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Organization creation failed due to validation errors.");
        }

        if (!await ImageReferenceEligibility.AreEligibleAsync(
                _storageObjectRepository,
                _tenantContext.TenantId,
                request.OrganizationDto.ProfilePictureId))
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Profile picture must be an active public safe-raster object in the current tenant."],
                "Organization creation failed due to validation errors.");
        }

        var currentUserId = request.CreatorUserId;

        var organization = _mapper.Map<Organization>(request.OrganizationDto);
        var tenantId = _tenantContext.TenantId;
        var createdAt = DateTime.UtcNow;
        var isTenantAdmin = await _adminContext.IsTenantAdminAsync(tenantId, cancellationToken);

        organization.CreatedAt = createdAt;
        var organizationActor = new Actor
        {
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = organization.FullName },
            Description = null,
            Organization = organization
        };

        var participation = new OrganizationTenant
        {
            TenantId = tenantId,
            Tenant = null!,
            Organization = organization,
            ApprovalStatusId = isTenantAdmin ? (int)ApprovalStatusEnum.Approved : (int)ApprovalStatusEnum.Pending,
            ApprovalStatus = null!,
            IsVisible = isTenantAdmin,
            IsOrganizerEligible = isTenantAdmin,
            ProfilePictureId = request.OrganizationDto.ProfilePictureId,
            ApprovedAt = isTenantAdmin ? createdAt : null,
            ApprovedBy = isTenantAdmin ? currentUserId : null,
            CreatedAt = createdAt
        };

        var organizationMember = new OrganizationMember
        {
            OrganizationTenant = participation,
            UserId = currentUserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            OrganizationPositionId = null,
            TenantId = tenantId,
            Tenant = null!
        };

        await _unitOfWork.ExecuteInTransactionAsync(async transactionToken =>
        {
            organization = await _organizationRepository.Create(organization);
            organizationActor.OrganizationId = organization.Id;
            await _actorRepository.Create(organizationActor);
            participation.OrganizationId = organization.Id;
            participation = await _organizationTenantRepository.Create(participation);
            organizationMember.OrganizationTenantId = participation.Id;
            await _organizationMemberRepository.Create(organizationMember);
        }, cancellationToken);
        _adminCacheInvalidator.InvalidateUser(currentUserId);

        _metrics.RecordOrganizationCreated(_tenantContext.TenantId.ToString());

        await _cache.RemoveAsync($"organization:detail:{organization.Id}", cancellationToken);

        return BaseCommandResponse.Success(
            organization.Id,
            "Organization created successfully. You are now the creator and admin of this organization.");
    }

}
