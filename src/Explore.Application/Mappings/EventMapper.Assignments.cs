using Explore.Application.DTOs.EventCategories;
using Explore.Application.DTOs.EventTags;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

public static partial class EventMapper
{
    // Junction responses flatten labels only, never tenant, event or classification graphs/audit.
    [MapperIgnoreSource(nameof(EventTags.Tenant))]
    [MapperIgnoreSource(nameof(EventTags.CreatedAt))]
    [MapperIgnoreSource(nameof(EventTags.CreatedBy))]
    [MapperIgnoreSource(nameof(EventTags.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventTags.UpdatedBy))]
    [MapProperty(nameof(EventTags.Event), nameof(EventTagsDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventTags.Tag), nameof(EventTagsDto.TagFullName), Use = nameof(TagName))]
    [MapProperty(nameof(EventTags.Tag), nameof(EventTagsDto.TagMasterCode), Use = nameof(TagCode))]
    public static partial EventTagsDto? ToDetail(EventTags? source);

    [MapperIgnoreSource(nameof(EventTags.Tenant))]
    [MapperIgnoreSource(nameof(EventTags.CreatedAt))]
    [MapperIgnoreSource(nameof(EventTags.CreatedBy))]
    [MapperIgnoreSource(nameof(EventTags.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventTags.UpdatedBy))]
    [MapProperty(nameof(EventTags.Event), nameof(EventTagsListDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventTags.Tag), nameof(EventTagsListDto.TagFullName), Use = nameof(TagName))]
    [MapProperty(nameof(EventTags.Tag), nameof(EventTagsListDto.TagMasterCode), Use = nameof(TagCode))]
    public static partial EventTagsListDto ToListItem(EventTags source);

    [MapperIgnoreSource(nameof(EventCategories.Tenant))]
    [MapperIgnoreSource(nameof(EventCategories.CreatedAt))]
    [MapperIgnoreSource(nameof(EventCategories.CreatedBy))]
    [MapperIgnoreSource(nameof(EventCategories.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventCategories.UpdatedBy))]
    [MapProperty(nameof(EventCategories.Event), nameof(EventCategoriesDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventCategories.Category), nameof(EventCategoriesDto.CategoryFullName), Use = nameof(CategoryName))]
    [MapProperty(nameof(EventCategories.Category), nameof(EventCategoriesDto.CategoryMasterCode), Use = nameof(CategoryCode))]
    public static partial EventCategoriesDto? ToDetail(EventCategories? source);

    [MapperIgnoreSource(nameof(EventCategories.Tenant))]
    [MapperIgnoreSource(nameof(EventCategories.CreatedAt))]
    [MapperIgnoreSource(nameof(EventCategories.CreatedBy))]
    [MapperIgnoreSource(nameof(EventCategories.UpdatedAt))]
    [MapperIgnoreSource(nameof(EventCategories.UpdatedBy))]
    [MapProperty(nameof(EventCategories.Event), nameof(EventCategoriesListDto.EventTitle), Use = nameof(EventTitle))]
    [MapProperty(nameof(EventCategories.Category), nameof(EventCategoriesListDto.CategoryFullName), Use = nameof(CategoryName))]
    [MapProperty(nameof(EventCategories.Category), nameof(EventCategoriesListDto.CategoryMasterCode), Use = nameof(CategoryCode))]
    public static partial EventCategoriesListDto ToListItem(EventCategories source);

    private static string? EventTitle(Event? source) => source?.Title;
    private static string? TagName(Tag? source) => source?.FullName;
    private static string? TagCode(Tag? source) => source?.MasterCode;
    private static string? CategoryName(Category? source) => source?.FullName;
    private static string? CategoryCode(Category? source) => source?.MasterCode;
}
