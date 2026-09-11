using Explore.Application.DTOs.Permission;
using Explore.Application.Lookups;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class PermissionMapper
{
    // Filtering remains in the registry/repository; list output omits policy and audit internals.
    [MapperIgnoreSource(nameof(Permission.Description))]
    [MapperIgnoreSource(nameof(Permission.FieldScope))]
    [MapperIgnoreSource(nameof(Permission.RoleScope))]
    [MapperIgnoreSource(nameof(Permission.Scope))]
    [MapperIgnoreSource(nameof(Permission.IsSystem))]
    [MapperIgnoreSource(nameof(Permission.IsFiltered))]
    [MapperIgnoreSource(nameof(Permission.IsActive))]
    [MapperIgnoreSource(nameof(Permission.CreatedAt))]
    [MapperIgnoreSource(nameof(Permission.CreatedBy))]
    [MapperIgnoreSource(nameof(Permission.UpdatedAt))]
    [MapperIgnoreSource(nameof(Permission.UpdatedBy))]
    [MapProperty(nameof(Permission.RoleScopeId), nameof(PermissionListDto.RoleScopeCode), Use = nameof(ScopeCode))]
    [MapProperty(nameof(Permission.RoleScopeId), nameof(PermissionListDto.RoleScopeName), Use = nameof(ScopeName))]
    public static partial PermissionListDto ToListItem(Permission source);

    private static string ScopeCode(int id) => NormalizedLookupMetadata.RoleScope(id).Code;
    private static string ScopeName(int id) => NormalizedLookupMetadata.RoleScope(id).Name;
}
