using Explore.Application.DTOs.Footer;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class FooterMapper
{
    [MapperIgnoreSource(nameof(TenantFooterLink.FooterLinkGroupId))]
    [MapperIgnoreSource(nameof(TenantFooterLink.CreatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLink.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLink.UpdatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLink.UpdatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLink.Group))]
    public static partial FooterLinkItemDto ToLinkItem(TenantFooterLink source);

    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.TenantId))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.IsActive))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.CreatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.UpdatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.UpdatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.Tenant))]
    [MapProperty(nameof(TenantFooterLinkGroup.Links), nameof(FooterLinkGroupDto.Links), Use = nameof(ToLinks))]
    public static partial FooterLinkGroupDto ToPublicGroup(TenantFooterLinkGroup source);

    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.CreatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.UpdatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.UpdatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.Tenant))]
    [MapProperty(nameof(TenantFooterLinkGroup.Links), nameof(FooterLinkGroupDetailsDto.Links), Use = nameof(ToLinks))]
    public static partial FooterLinkGroupDetailsDto ToDetail(TenantFooterLinkGroup source);

    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.CreatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.CreatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.UpdatedAt))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.UpdatedBy))]
    [MapperIgnoreSource(nameof(TenantFooterLinkGroup.Tenant))]
    [MapProperty("Links.Count", nameof(FooterLinkGroupListDto.LinkCount))]
    public static partial FooterLinkGroupListDto ToListItem(TenantFooterLinkGroup source);

    private static IReadOnlyList<FooterLinkItemDto> ToLinks(IReadOnlyList<TenantFooterLink> links) =>
        Array.AsReadOnly(links.Select(ToLinkItem).ToArray());
}
