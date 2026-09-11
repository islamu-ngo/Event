using Explore.Application.DTOs.Tenant;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class TenantMapper
{
    [MapperIgnoreSource(nameof(Tenant.Description))]
    [MapperIgnoreSource(nameof(Tenant.TenantStatusId))]
    [MapperIgnoreSource(nameof(Tenant.TenantStatus))]
    [MapperIgnoreSource(nameof(Tenant.CreatedAt))]
    [MapperIgnoreSource(nameof(Tenant.CreatedBy))]
    [MapperIgnoreSource(nameof(Tenant.UpdatedAt))]
    [MapperIgnoreSource(nameof(Tenant.UpdatedBy))]
    [MapperIgnoreSource(nameof(Tenant.NavigationLinks))]
    public static partial TenantDto ToDetail(Tenant source);

    [MapperIgnoreSource(nameof(Tenant.Description))]
    [MapperIgnoreSource(nameof(Tenant.TenantStatusId))]
    [MapperIgnoreSource(nameof(Tenant.TenantStatus))]
    [MapperIgnoreSource(nameof(Tenant.CreatedAt))]
    [MapperIgnoreSource(nameof(Tenant.CreatedBy))]
    [MapperIgnoreSource(nameof(Tenant.UpdatedAt))]
    [MapperIgnoreSource(nameof(Tenant.UpdatedBy))]
    [MapperIgnoreSource(nameof(Tenant.NavigationLinks))]
    public static partial TenantListDto ToListItem(Tenant source);
}
