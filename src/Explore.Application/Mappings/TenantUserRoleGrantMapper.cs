using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class TenantUserRoleGrantMapper
{
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.RoleScopeId))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.UpdatedBy))]
    [MapProperty("TenantUser.UserId", nameof(TenantUserRoleGrantDto.UserId))]
    [MapProperty(nameof(TenantUserRoleGrant.TenantUser), nameof(TenantUserRoleGrantDto.UserEmail), Use = nameof(Email))]
    [MapProperty(nameof(TenantUserRoleGrant.TenantUser), nameof(TenantUserRoleGrantDto.UserFullName), Use = nameof(FullName))]
    [MapProperty("Tenant.FullName", nameof(TenantUserRoleGrantDto.TenantFullName))]
    [MapProperty("Role.FullName", nameof(TenantUserRoleGrantDto.RoleName))]
    public static partial TenantUserRoleGrantDto ToDetail(TenantUserRoleGrant source);

    [MapperIgnoreSource(nameof(TenantUserRoleGrant.RoleScopeId))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.GrantedBy))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.RevokedBy))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.RevocationReason))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.CreatedAt))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.UpdatedAt))]
    [MapperIgnoreSource(nameof(TenantUserRoleGrant.UpdatedBy))]
    [MapProperty("TenantUser.UserId", nameof(TenantUserRoleGrantListDto.UserId))]
    [MapProperty(nameof(TenantUserRoleGrant.TenantUser), nameof(TenantUserRoleGrantListDto.UserEmail), Use = nameof(Email))]
    [MapProperty(nameof(TenantUserRoleGrant.TenantUser), nameof(TenantUserRoleGrantListDto.UserFullName), Use = nameof(FullName))]
    [MapProperty("Tenant.FullName", nameof(TenantUserRoleGrantListDto.TenantFullName))]
    [MapProperty("Role.FullName", nameof(TenantUserRoleGrantListDto.RoleName))]
    public static partial TenantUserRoleGrantListDto ToListItem(TenantUserRoleGrant source);

    private static string? Email(TenantUser tenantUser) => tenantUser.User?.Pii?.Email;

    private static string? FullName(TenantUser tenantUser) =>
        tenantUser.User?.Pii is { } pii ? $"{pii.FirstName} {pii.LastName}" : null;
}
