using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.TenantUserRoleGrant.Validators;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;

public class CreateTenantUserRoleGrantCommandHandler : ICommandHandler<CreateTenantUserRoleGrantCommand, BaseCommandResponse<Guid>>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantUserRoleGrantMutationBoundary _boundary;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;

    public CreateTenantUserRoleGrantCommandHandler(
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        ITenantUserRepository tenantUserRepository,
        IRoleRepository roleRepository,
        ITenantContext tenantContext,
        ITenantUserRoleGrantMutationBoundary boundary,
        IHumanAccountAdministrativeActionPreparation preparation)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _tenantUserRepository = tenantUserRepository;
        _roleRepository = roleRepository;
        _tenantContext = tenantContext;
        _boundary = boundary;
        _preparation = preparation;
    }

    public async Task<BaseCommandResponse<Guid>> ExecuteAsync(CreateTenantUserRoleGrantCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        var grantId = Guid.CreateVersion7();
        return await _boundary.ExecuteCreateAsync(request.TenantId, request.TenantUserRoleGrantDto.TenantUserId,
            request.TenantUserRoleGrantDto.RoleId,
            async (actor, token) =>
        {
            if (actor != prepared.PrincipalId)
                return BaseCommandResponse.Authorization<Guid>();
            await prepared.AcquireAsync(request, token);
            var result = await ExecuteBodyAsync(request, grantId, prepared, token);
            if (result.IsSuccess) prepared.Complete(result.Id);
            return result;
        }, cancellationToken);
    }

    private async Task<BaseCommandResponse<Guid>> ExecuteBodyAsync(CreateTenantUserRoleGrantCommand request,
        Guid grantId, IPreparedHumanAccountAdministrativeAction prepared, CancellationToken cancellationToken)
    {
        var dto = request.TenantUserRoleGrantDto;

        var validator = new CreateTenantUserRoleGrantDtoValidator(_tenantUserRepository, _roleRepository);
        var validationResult = await validator.ValidateAsync(dto, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BaseCommandResponse.Validation<Guid>(
                validationResult.Errors.Select(e => e.ErrorMessage),
                "Tenant user role grant creation failed.");
        }

        var tenantUser = await _tenantUserRepository.GetById(dto.TenantUserId);
        if (tenantUser is null || tenantUser.TenantId != _tenantContext.TenantId)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Tenant-local user state is required before a role can be granted."],
                "Tenant user role grant creation failed.");
        }

        if (tenantUser.StatusId != (int)TenantUserStatusEnum.Active || tenantUser.IsDeleted)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["Tenant-local user must be active before a role can be granted."],
                "Tenant user role grant creation failed.");
        }

        var existingGrant = await _tenantUserRoleGrantRepository.GetByTenantUserAndRole(
            _tenantContext.TenantId,
            dto.TenantUserId,
            dto.RoleId);

        if (existingGrant is not null)
        {
            return BaseCommandResponse.Validation<Guid>(
                ["An active grant for this tenant user and role already exists."],
                "Tenant user role grant creation failed.");
        }

        var tenantUserRoleGrant = new TenantUserRoleGrant
        {
            Id = grantId,
            TenantId = _tenantContext.TenantId,
            Tenant = null!,
            TenantUserId = tenantUser.Id,
            TenantUser = null!,
            RoleId = dto.RoleId,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant,
            GrantedAt = prepared.ChangedAt,
            GrantedBy = prepared.PrincipalId
        };

        tenantUserRoleGrant = await _tenantUserRoleGrantRepository.Create(tenantUserRoleGrant);

        return BaseCommandResponse.Success(
            tenantUserRoleGrant.Id,
            "Tenant user role grant created successfully.");
    }
}
