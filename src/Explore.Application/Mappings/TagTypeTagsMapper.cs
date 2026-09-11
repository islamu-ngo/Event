using Explore.Application.DTOs.TagTypeTags;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class TagTypeTagsMapper
{
    // Select navigation labels only; tenant and identity graphs never enter the response.
    [MapperIgnoreSource(nameof(TagTypeTags.Tenant))]
    [MapProperty(nameof(TagTypeTags.Tag), nameof(TagTypeTagsDto.TagFullName), Use = nameof(TagName))]
    [MapProperty(nameof(TagTypeTags.Tag), nameof(TagTypeTagsDto.TagMasterCode), Use = nameof(TagCode))]
    [MapProperty(nameof(TagTypeTags.TagType), nameof(TagTypeTagsDto.TagTypeFullName), Use = nameof(TypeName))]
    [MapProperty(nameof(TagTypeTags.TagType), nameof(TagTypeTagsDto.TagTypeMasterCode), Use = nameof(TypeCode))]
    public static partial TagTypeTagsDto? ToDetail(TagTypeTags? source);

    [MapperIgnoreSource(nameof(TagTypeTags.TenantId))]
    [MapperIgnoreSource(nameof(TagTypeTags.Tenant))]
    [MapProperty(nameof(TagTypeTags.Tag), nameof(TagTypeTagsListDto.TagFullName), Use = nameof(TagName))]
    [MapProperty(nameof(TagTypeTags.Tag), nameof(TagTypeTagsListDto.TagMasterCode), Use = nameof(TagCode))]
    [MapProperty(nameof(TagTypeTags.TagType), nameof(TagTypeTagsListDto.TagTypeFullName), Use = nameof(TypeName))]
    [MapProperty(nameof(TagTypeTags.TagType), nameof(TagTypeTagsListDto.TagTypeMasterCode), Use = nameof(TypeCode))]
    public static partial TagTypeTagsListDto ToListItem(TagTypeTags source);

    // Body TenantId is not authoritative. Identity and navigation remain persistence-owned.
    public static TagTypeTags Create(CreateTagTypeTagsDto source, Guid tenantId) => new()
    {
        TagId = source.TagId,
        TagTypeId = source.TagTypeId,
        TenantId = tenantId,
        Tag = null!,
        TagType = null!,
        Tenant = null!
    };

    private static string? TagName(Tag? tag) => tag?.FullName;
    private static string? TagCode(Tag? tag) => tag?.MasterCode;
    private static string? TypeName(TagType? type) => type?.FullName;
    private static string? TypeCode(TagType? type) => type?.MasterCode;
}
