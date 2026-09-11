using Explore.Application.DTOs.Role;
using Explore.Application.Lookups;
using Explore.Domain;
using Riok.Mapperly.Abstractions;

namespace Explore.Application.Mappings;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Both, AutoUserMappings = false)]
public static partial class RoleMapper
{
    // Persisted scope IDs own normalized metadata, not navigation labels or enum wrappers.
    [MapperIgnoreSource(nameof(Role.RoleScope))]
    [MapperIgnoreSource(nameof(Role.Scope))]
    [MapProperty(nameof(Role.RoleScopeId), nameof(RoleDto.RoleScopeCode), Use = nameof(ScopeCode))]
    [MapProperty(nameof(Role.RoleScopeId), nameof(RoleDto.RoleScopeName), Use = nameof(ScopeName))]
    public static partial RoleDto? ToDetail(Role? source);

    [MapperIgnoreSource(nameof(Role.Description))]
    [MapperIgnoreSource(nameof(Role.RoleScope))]
    [MapperIgnoreSource(nameof(Role.Scope))]
    [MapProperty(nameof(Role.RoleScopeId), nameof(RoleListDto.RoleScopeCode), Use = nameof(ScopeCode))]
    [MapProperty(nameof(Role.RoleScopeId), nameof(RoleListDto.RoleScopeName), Use = nameof(ScopeName))]
    public static partial RoleListDto ToListItem(Role source);

    private static string ScopeCode(int id) => NormalizedLookupMetadata.RoleScope(id).Code;
    private static string ScopeName(int id) => NormalizedLookupMetadata.RoleScope(id).Name;
}
