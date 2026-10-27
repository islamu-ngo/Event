using System.Linq;
using System.Globalization;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Organization.Validators;
using Explore.Application.Features.Organizations.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Application.Contracts.Operations;
using Microsoft.Extensions.Caching.Hybrid;

namespace Explore.Application.Features.Organizations.Handlers.Commands;

public class CreateOrganizationCommandHandler : ICommandHandler<CreateOrganizationCommand, BaseCommandResponse<Guid>>
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IOrganizationTenantRepository _organizationTenantRepository;
    private readonly IOrganizationMemberRepository _organizationMemberRepository;
    private readonly IActorRepository _actorRepository;
    private readonly IStorageObjectRepository _storageObjectRepository;
    private readonly ITenantContext _tenantContext;
    private readonly HybridCache _cache;
    private readonly BusinessMetrics _metrics;
    private readonly IOrganizationCreationMutationBoundary _creation;

    public CreateOrganizationCommandHandler(
        IOrganizationRepository organizationRepository,
        IOrganizationTenantRepository organizationTenantRepository,
        IOrganizationMemberRepository organizationMemberRepository,
        IActorRepository actorRepository,
        IStorageObjectRepository storageObjectRepository,
        ITenantContext tenantContext,
        HybridCache cache,
        BusinessMetrics metrics,
        IOrganizationCreationMutationBoundary creation)
    {
        _organizationRepository = organizationRepository;
        _organizationTenantRepository = organizationTenantRepository;
        _organizationMemberRepository = organizationMemberRepository;
        _actorRepository = actorRepository;
        _storageObjectRepository = storageObjectRepository;
        _tenantContext = tenantContext;
        _cache = cache;
        _metrics = metrics;
        _creation = creation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateOrganizationCommand request, CancellationToken cancellationToken)
    {
        var ids = new OrganizationCreationIds(Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7());
        var result = await _creation.ExecuteCreationAsync(_tenantContext.TenantId, ids,
            (creator, approved, token) => CreateAsync(request, ids, creator, approved, token), cancellationToken);
        if (result.IsSuccess)
        {
            _metrics.RecordOrganizationCreated(_tenantContext.TenantId.ToString());
            await _cache.RemoveAsync($"organization:detail:{result.Id}", cancellationToken);
        }
        return result;
    }

    private async Task<BaseCommandResponse<Guid>> CreateAsync(CreateOrganizationCommand request,
        OrganizationCreationIds ids, Guid currentUserId, bool isTenantAdmin, CancellationToken cancellationToken)
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

        var organization = new Organization
        {
            Id = ids.OrganizationId,
            WebsiteUrl = request.OrganizationDto.WebsiteUrl,
            Pii = new OrganizationPii
            {
                OrganizationId = ids.OrganizationId,
                FullName = request.OrganizationDto.FullName,
                Email = request.OrganizationDto.Email,
                Country = request.OrganizationDto.Country,
                City = request.OrganizationDto.City,
                Postcode = request.OrganizationDto.Postcode.ToString(CultureInfo.CurrentCulture),
                Address = request.OrganizationDto.Address
            }
        };
        var tenantId = _tenantContext.TenantId;
        var createdAt = DateTime.UtcNow;

        organization.CreatedAt = createdAt;
        var organizationActor = new Actor
        {
            Id = ids.ActorId,
            ActorTypeId = (int)ActorTypeEnum.Organization,
            ActorType = null!,
            Pii = new ActorPii { ActorId = ids.ActorId, DisplayName = organization.FullName },
            Description = null,
            Organization = organization
        };

        var participation = new OrganizationTenant
        {
            Id = ids.ParticipationId,
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
            Id = ids.MemberId,
            OrganizationTenant = participation,
            UserId = currentUserId,
            User = null!,
            RoleId = (int)RoleEnum.OrgAdmin,
            Role = null!,
            OrganizationPositionId = null,
            TenantId = tenantId,
            Tenant = null!
        };

        organization = await _organizationRepository.Create(organization);
        organizationActor.OrganizationId = organization.Id;
        await _actorRepository.Create(organizationActor);
        participation.OrganizationId = organization.Id;
        participation = await _organizationTenantRepository.Create(participation);
        organizationMember.OrganizationTenantId = participation.Id;
        await _organizationMemberRepository.Create(organizationMember);

        return BaseCommandResponse.Success(
            organization.Id,
            "Organization created successfully. You are now the creator and admin of this organization.");
    }

}
