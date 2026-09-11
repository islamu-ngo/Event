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

    [MapperIgnoreSource(nameof(TenantNavigationLink.TenantId))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.IsActive))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.CreatedAt))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.UpdatedAt))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.UpdatedBy))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.IsDeleted))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.DeletedAt))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.DeletedBy))]
    [MapperIgnoreSource(nameof(TenantNavigationLink.Tenant))]
    public static partial TenantNavigationLinkDto ToNavigationLink(TenantNavigationLink source);
}
