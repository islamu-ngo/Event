using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Tenant.Validators;
using Explore.Application.Exceptions;
using Explore.Application.Features.Management;
using Explore.Application.Features.Tenants.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Tenants.Handlers.Commands.CreateTenant;

public class CreateTenantCommandHandler : IRequestHandler<CreateTenantCommand, BaseCommandResponse<Guid>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ITenantCreationService _tenantCreationService;
    private readonly ITypedSettingsDocumentResolver _typedSettingsDocumentResolver;
    private readonly ILogger<CreateTenantCommandHandler> _logger;
    private readonly ISettingMutationLock _mutationLock;
    private readonly TenantActivationCapacityPolicy _capacityPolicy;

    public CreateTenantCommandHandler(
        ITenantRepository tenantRepository,
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        ITenantUserRepository tenantUserRepository,
        IRoleRepository roleRepository,
        ITenantCreationService tenantCreationService,
        ITypedSettingsDocumentResolver typedSettingsDocumentResolver,
        ILogger<CreateTenantCommandHandler> logger,
        ISettingMutationLock mutationLock,
        TenantActivationCapacityPolicy capacityPolicy)
    {
        _tenantRepository = tenantRepository;
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _tenantUserRepository = tenantUserRepository;
        _roleRepository = roleRepository;
        _tenantCreationService = tenantCreationService;
        _typedSettingsDocumentResolver = typedSettingsDocumentResolver;
        _logger = logger;
        _mutationLock = mutationLock;
        _capacityPolicy = capacityPolicy;
    }

    public async Task<BaseCommandResponse<Guid>> Handle(CreateTenantCommand request, CancellationToken cancellationToken)
    {
        var dto = request.TenantDto;

        var validator = new CreateTenantDtoValidator();
        var validation = await validator.ValidateAsync(dto, cancellationToken);
        if (!validation.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validation.Errors.Select(e => e.ErrorMessage),
                "Invalid tenant data.");
        }

        var existingTenant = await _tenantRepository.GetTenantBySlug(dto.Slug);
        if (existingTenant != null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Slug must be unique across all tenants."],
                "A tenant with this slug already exists.");
        }

        var statusId = dto.IsActive ? (int)TenantStatusEnum.Active : (int)TenantStatusEnum.Provisioning;
        var assignAdmin = dto.AssignCurrentUserAsTenantAdmin && request.RequestingUserId.HasValue;
        Guid plannedTenantId = Guid.CreateVersion7();
        Guid plannedBrandingDocumentId = Guid.CreateVersion7();
        Guid plannedDirectoryOperatorIdentityDocumentId = Guid.CreateVersion7();
        DateTime occurredAt = DateTime.UtcNow;
        TenantSettingsDocument defaultBranding =
            TenantBrandingSettingsDocumentDefaults.Create(plannedTenantId, dto.FullName);
        TenantSettingsDocument defaultIdentity = dto.DirectoryOperatorIdentity is null
            ? TenantDirectoryOperatorIdentityDocumentDefaults.Create(plannedTenantId, dto.FullName)
            : TenantDirectoryOperatorIdentityDocumentDefaults.Create(
                plannedTenantId,
                dto.DirectoryOperatorIdentity.ToPayload());

        async Task<BaseCommandResponse<Guid>> CreateAsync(CancellationToken ct)
        {
            if (await _tenantRepository.GetTenantBySlug(dto.Slug) is not null)
            {
                return BaseCommandResponse.Validation<Guid>(
                    ["Tenant with this slug already exists"],
                    "Tenant with this slug already exists");
            }

            if (dto.IsActive)
            {
                TenantActivationCapacityAssessment capacity = await _capacityPolicy.EvaluateAsync(
                    requireMultiTenant: false,
                    cancellationToken: ct);
                if (!capacity.Allowed)
                {
                    return BaseCommandResponse.Failure<Guid>(
                        capacity.FailureCode!,
                        capacity.Error,
                        [capacity.Error!]);
                }
            }

            TenantCreationOutcome creation;
            try
            {
                creation = await _tenantCreationService.CreateInCurrentTransactionAsync(
                    new TenantCreationRequest(
                        plannedTenantId,
                        dto.FullName,
                        dto.Slug,
                        statusId,
                        request.RequestingUserId,
                        occurredAt,
                        new TenantBrandingDocumentSeed(
                            plannedBrandingDocumentId,
                            defaultBranding.SchemaVersion,
                            defaultBranding.DefaultsVersion,
                            defaultBranding.PayloadJson),
                        new TenantDirectoryOperatorIdentityDocumentSeed(
                            plannedDirectoryOperatorIdentityDocumentId,
                            defaultIdentity.SchemaVersion,
                            defaultIdentity.DefaultsVersion,
                            defaultIdentity.PayloadJson)),
                    ct);
            }
            catch (TenantDirectoryOperatorIdentityReadinessException exception)
            {
                return BaseCommandResponse.Failure<Guid>(
                    exception.FailureCode,
                    exception.Message,
                    exception.ReasonCodes);
            }

            if (assignAdmin)
            {
                await AssignRequestingUserAsTenantAdminAsync(
                    creation.Tenant.Id,
                    request.RequestingUserId!.Value,
                    ct);
            }

            return BaseCommandResponse.Success(
                creation.Tenant.Id,
                assignAdmin
                    ? "Tenant created successfully. You have been assigned as tenant administrator."
                    : "Tenant created successfully.");
        }

        string slugLockKey = TenantMutationLockKeys.ForSlug(dto.Slug);
        BaseCommandResponse<Guid> result = _capacityPolicy.IsEnforced
            ? await _mutationLock.ExecuteManyAsync(
                [GovernanceSettingKeys.Deployment.Mode, slugLockKey],
                CreateAsync,
                cancellationToken)
            : await _mutationLock.ExecuteAsync(
                slugLockKey,
                CreateAsync,
                cancellationToken);
        if (result.IsSuccess && result.Id is Guid tenantId)
        {
            _typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
                tenantId,
                SettingsDocumentKeys.Tenant.Branding);
            _typedSettingsDocumentResolver.InvalidateTenantDocumentCache(
                tenantId,
                SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity);
        }

        return result;
    }

    private async Task AssignRequestingUserAsTenantAdminAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var tenantAdminRole = await _roleRepository.GetByMasterCodeAsync("tenant.admin")
            ?? await _roleRepository.GetByIdAsync((int)RoleEnum.TenantAdmin);

        if (tenantAdminRole == null)
        {
            _logger.LogWarning(
                "tenant.admin role not found; skipping automatic admin assignment for UserId={UserId} on TenantId={TenantId}.",
                userId, tenantId);
            return;
        }

        var existing = await _tenantUserRoleGrantRepository.GetByTenantAndUser(tenantId, userId);
        if (existing != null) return;

        var tenantUser = await _tenantUserRepository.GetByTenantAndUserAsync(tenantId, userId, ct)
            ?? await _tenantUserRepository.Create(new TenantUser
            {
                TenantId = tenantId,
                Tenant = null!,
                UserId = userId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = userId
            });

        await _tenantUserRoleGrantRepository.Create(new TenantUserRoleGrant
        {
            TenantId = tenantId,
            Tenant = null!,
            TenantUserId = tenantUser.Id,
            TenantUser = null!,
            RoleId = tenantAdminRole.Id,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = DateTime.UtcNow,
            GrantedBy = userId
        });
    }
}
