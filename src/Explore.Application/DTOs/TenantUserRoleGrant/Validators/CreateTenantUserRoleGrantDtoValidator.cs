using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using FluentValidation;

namespace Explore.Application.DTOs.TenantUserRoleGrant.Validators;

public class CreateTenantUserRoleGrantDtoValidator : AbstractValidator<CreateTenantUserRoleGrantDto>
{
    private readonly ITenantUserRepository _tenantUserRepository;
    private readonly IRoleRepository _roleRepository;

    public CreateTenantUserRoleGrantDtoValidator(
        ITenantUserRepository tenantUserRepository,
        IRoleRepository roleRepository)
    {
        _tenantUserRepository = tenantUserRepository;
        _roleRepository = roleRepository;

        RuleFor(x => x.TenantUserId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Tenant user ID is required")
            .MustAsync(TenantUserExists)
            .WithMessage("Tenant user does not exist");

        RuleFor(x => x.RoleId)
            .Cascade(CascadeMode.Stop)
            .NotEmpty().WithMessage("Role ID is required")
            .CustomAsync(ValidateTenantScopedRole);
    }

    private async Task<bool> TenantUserExists(Guid tenantUserId, CancellationToken cancellationToken)
    {
        return await _tenantUserRepository.Exists(tenantUserId);
    }

    private async Task ValidateTenantScopedRole(
        int roleId,
        ValidationContext<CreateTenantUserRoleGrantDto> context,
        CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(roleId);
        if (role is null)
        {
            context.AddFailure("Role does not exist");
            return;
        }

        if (role.Scope != RoleScopeEnum.Tenant)
        {
            context.AddFailure("Role must be a tenant-scoped role");
        }
    }
}
