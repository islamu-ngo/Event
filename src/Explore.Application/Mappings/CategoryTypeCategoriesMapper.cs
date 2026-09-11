using Explore.Application.DTOs.CategoryTypeCategories;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class CategoryTypeCategoriesMapper
{
    // Navigation is read only for labels, never for tenant or parent-category graphs.
    [MapperIgnoreSource(nameof(CategoryTypeCategories.Tenant))]
    [MapProperty(nameof(CategoryTypeCategories.Category), nameof(CategoryTypeCategoriesDto.CategoryFullName), Use = nameof(CategoryName))]
    [MapProperty(nameof(CategoryTypeCategories.Category), nameof(CategoryTypeCategoriesDto.CategoryMasterCode), Use = nameof(CategoryCode))]
    [MapProperty(nameof(CategoryTypeCategories.CategoryType), nameof(CategoryTypeCategoriesDto.CategoryTypeFullName), Use = nameof(TypeName))]
    [MapProperty(nameof(CategoryTypeCategories.CategoryType), nameof(CategoryTypeCategoriesDto.CategoryTypeMasterCode), Use = nameof(TypeCode))]
    public static partial CategoryTypeCategoriesDto? ToDetail(CategoryTypeCategories? source);

    [MapperIgnoreSource(nameof(CategoryTypeCategories.TenantId))]
    [MapperIgnoreSource(nameof(CategoryTypeCategories.Tenant))]
    [MapProperty(nameof(CategoryTypeCategories.Category), nameof(CategoryTypeCategoriesListDto.CategoryFullName), Use = nameof(CategoryName))]
    [MapProperty(nameof(CategoryTypeCategories.Category), nameof(CategoryTypeCategoriesListDto.CategoryMasterCode), Use = nameof(CategoryCode))]
    [MapProperty(nameof(CategoryTypeCategories.CategoryType), nameof(CategoryTypeCategoriesListDto.CategoryTypeFullName), Use = nameof(TypeName))]
    [MapProperty(nameof(CategoryTypeCategories.CategoryType), nameof(CategoryTypeCategoriesListDto.CategoryTypeMasterCode), Use = nameof(TypeCode))]
    public static partial CategoryTypeCategoriesListDto ToListItem(CategoryTypeCategories source);

    // Body TenantId is not authoritative. Identity and navigation remain persistence-owned.
    public static CategoryTypeCategories Create(CreateCategoryTypeCategoriesDto source, Guid tenantId) => new()
    {
        CategoryId = source.CategoryId,
        CategoryTypeId = source.CategoryTypeId,
        TenantId = tenantId,
        Category = null!,
        CategoryType = null!,
        Tenant = null!
    };

    private static string? CategoryName(Category? category) => category?.FullName;
    private static string? CategoryCode(Category? category) => category?.MasterCode;
    private static string? TypeName(CategoryType? type) => type?.FullName;
    private static string? TypeCode(CategoryType? type) => type?.MasterCode;
}
