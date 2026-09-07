namespace Explore.Infrastructure.Services;

using System.Text.Json;
using IGroupSettingRepository = Explore.Application.Contracts.Persistence.IGroupSettingRepository;
using IGroupTenantRepository = Explore.Application.Contracts.Persistence.IGroupTenantRepository;
using IOrganizationSettingRepository = Explore.Application.Contracts.Persistence.IOrganizationSettingRepository;
using ISettingMutationLock = Explore.Application.Contracts.Persistence.ISettingMutationLock;
using ISystemSettingRepository = Explore.Application.Contracts.Persistence.ISystemSettingRepository;
using ITenantSettingRepository = Explore.Application.Contracts.Persistence.ITenantSettingRepository;
using IUserPreferenceRepository = Explore.Application.Contracts.Persistence.IUserPreferenceRepository;
using IHierarchicalSettingsResolver = Explore.Application.Contracts.Infrastructure.IHierarchicalSettingsResolver;
using ISettingGroup = Explore.Application.Contracts.Infrastructure.ISettingGroup;
using ITenantContext = Explore.Application.Contracts.Infrastructure.ITenantContext;
using ResolvedSetting = Explore.Application.Contracts.Infrastructure.ResolvedSetting;
using SettingSource = Explore.Application.Contracts.Infrastructure.SettingSource;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves settings through a 5-tier hierarchy with batch loading.
/// Loads all settings for requested scopes in ≤2 queries (system + scoped),
/// then merges with lock precedence.
/// </summary>
public class HierarchicalSettingsResolver : IHierarchicalSettingsResolver
{
    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly ITenantSettingRepository _tenantSettingRepository;
    private readonly IOrganizationSettingRepository _organizationSettingRepository;
    private readonly IGroupSettingRepository _groupSettingRepository;
    private readonly IGroupTenantRepository _groupTenantRepository;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HierarchicalSettingsResolver> _logger;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    private const string SystemCacheKey = "HierSettings:System";
    private const string TenantCachePrefix = "HierSettings:Tenant:";
    private const string OrgCachePrefix = "HierSettings:Org:";
    private const string GroupCachePrefix = "HierSettings:Group:";
    private const string UserCachePrefix = "HierSettings:User:";

    public HierarchicalSettingsResolver(
        ISystemSettingRepository systemSettingRepository,
        ITenantSettingRepository tenantSettingRepository,
        IOrganizationSettingRepository organizationSettingRepository,
        IGroupSettingRepository groupSettingRepository,
        IGroupTenantRepository groupTenantRepository,
        IUserPreferenceRepository userPreferenceRepository,
        ITenantContext tenantContext,
        ISettingMutationLock mutationLock,
        IMemoryCache cache,
        ILogger<HierarchicalSettingsResolver> logger)
    {
        _systemSettingRepository = systemSettingRepository;
        _tenantSettingRepository = tenantSettingRepository;
        _organizationSettingRepository = organizationSettingRepository;
        _groupSettingRepository = groupSettingRepository;
        _groupTenantRepository = groupTenantRepository;
        _userPreferenceRepository = userPreferenceRepository;
        _tenantContext = tenantContext;
        _mutationLock = mutationLock;
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> ResolveAsync<T>(string key, SettingContext context, CancellationToken ct = default)
    {
        var resolved = await ResolveWithMetadataAsync(key, context, ct);
        if (resolved is null)
            return default;

        return SettingValueSerializer.Deserialize(resolved.Value, default(T)!);
    }

    public async Task<ResolvedSetting?> ResolveWithMetadataAsync(
        string key, SettingContext context, CancellationToken ct = default)
    {
        var batch = await ResolveBatchAsync([key], context, ct);
        return batch.Count > 0 ? batch[0] : null;
    }

    public async Task<IReadOnlyList<ResolvedSetting>> ResolveBatchAsync(
        IEnumerable<string> keys, SettingContext context, CancellationToken ct = default)
    {
        var keyList = keys.ToList();
        if (keyList.Count == 0)
            return [];

        // Load system settings (batch)
        var systemSettings = await GetSystemSettingsAsync(ct);
        var systemDict = systemSettings.ToDictionary(s => s.SettingKey, s => s);

        // Load tenant settings if context has tenant
        Dictionary<string, TenantSetting>? tenantDict = null;
        if (context.TenantId.HasValue)
        {
            var tenantSettings = await GetTenantSettingsAsync(context.TenantId.Value, ct);
            tenantDict = tenantSettings.ToDictionary(s => s.SettingKey, s => s);
        }

        // Organization settings belong to a tenant-specific participation, not the global organization.
        Dictionary<string, OrganizationSetting>? orgDict = null;
        if (context.TenantId is { } organizationTenantId && organizationTenantId != Guid.Empty &&
            context.OrganizationId is { } organizationId && organizationId != Guid.Empty)
        {
            var orgSettings = await GetOrganizationSettingsAsync(
                organizationTenantId,
                organizationId,
                ct);
            orgDict = orgSettings.ToDictionary(s => s.SettingKey, s => s);
        }

        // Load group settings if context has group
        Dictionary<string, GroupSetting>? groupDict = null;
        if (context.GroupId.HasValue)
        {
            var groupSettings = await GetGroupSettingsAsync(context.GroupId.Value, ct);
            groupDict = groupSettings.ToDictionary(s => s.SettingKey, s => s);
        }

        // Load user preferences if context has user
        Dictionary<string, UserPreference>? userDict = null;
        if (context.UserId.HasValue && context.TenantId.HasValue)
        {
            var userPrefs = await GetUserPreferencesAsync(context.TenantId.Value, context.UserId.Value, ct);
            userDict = userPrefs.ToDictionary(s => s.SettingKey, s => s);
        }

        // Determine deployment mode to bypass system locks for single-tenant
        systemDict.TryGetValue(Explore.Domain.Constants.GovernanceSettingKeys.Deployment.Mode, out var deploymentModeSetting);
        var isMultiTenant = false;
        if (deploymentModeSetting != null)
        {
            var value = SettingValueSerializer.DeserializeString(deploymentModeSetting.Value);
            if (!string.IsNullOrWhiteSpace(value))
            {
                isMultiTenant = string.Equals(value, "MultiTenant", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Resolve each key through the full cascade
        var results = new List<ResolvedSetting>(keyList.Count);
        foreach (var key in keyList)
        {
            var resolved = ResolveSingleKey(key, systemDict, tenantDict, orgDict, groupDict, userDict, isMultiTenant);
            if (resolved is not null)
                results.Add(resolved);
        }

        return results;
    }

    public async Task<TGroup> ResolveGroupAsync<TGroup>(SettingContext context, CancellationToken ct = default)
        where TGroup : ISettingGroup, new()
    {
        var keys = TGroup.SettingKeys;
        var resolved = await ResolveBatchAsync(keys, context, ct);
        var dict = resolved.ToDictionary(r => r.Key, r => r);

        var group = new TGroup();
        group.Populate(dict);
        return group;
    }

    public async Task SetValueAsync(
        string key, string value, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        var definition = SettingRegistry.Get(key);
        if (definition is not null)
        {
            if (scope < definition.MinScope || scope > definition.MaxScope)
            {
                throw new InvalidOperationException(
                    $"Setting '{key}' cannot be set at scope {scope}. Allowed range: {definition.MinScope}–{definition.MaxScope}.");
            }
        }

        switch (scope)
        {
            case SettingScope.Instance:
                await _mutationLock.ExecuteAsync(
                    key,
                    async token =>
                    {
                        await UpsertSystemSettingAsync(key, value, actorId, token);
                        return true;
                    },
                    ct);
                break;

            case SettingScope.Tenant:
                await _mutationLock.ExecuteAsync(
                    key,
                    async token =>
                    {
                        if (await _systemSettingRepository.IsLocked(key, token))
                        {
                            throw new InvalidOperationException($"Setting '{key}' is locked at Instance scope.");
                        }

                        await _tenantSettingRepository.SetValueAsync(scopeId, key, value, token, actorId);
                        return true;
                    },
                    ct);
                break;

            case SettingScope.Organization:
                await UpsertOrganizationSettingAsync(key, value, scopeId, actorId, ct);
                break;

            case SettingScope.Group:
                await UpsertGroupSettingAsync(key, value, scopeId, actorId, ct);
                break;

            case SettingScope.User:
                throw new NotSupportedException(
                    "User scope requires tenant context. Use SetUserValueAsync for user preferences.");

            default:
                throw new NotSupportedException($"Scope {scope} is not supported.");
        }

        if (scope == SettingScope.Organization)
        {
            InvalidateOrganizationCache(RequireAmbientTenantId(), scopeId);
        }
        else
        {
            InvalidateCache(scope, scopeId);
        }
    }

    public async Task RemoveOverrideAsync(
        string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        switch (scope)
        {
            case SettingScope.Tenant:
                await _mutationLock.ExecuteAsync(
                    key,
                    async token =>
                    {
                        if (await _systemSettingRepository.IsLocked(key, token))
                        {
                            throw new SettingSystemLockedException(key);
                        }

                        return await _tenantSettingRepository.RemoveOverrideAsync(scopeId, key, token);
                    },
                    ct);
                break;

            case SettingScope.Organization:
                await _organizationSettingRepository.RemoveOverride(
                    RequireAmbientTenantId(),
                    scopeId,
                    key,
                    ct);
                break;

            case SettingScope.Group:
                await _groupSettingRepository.RemoveOverride(scopeId, key);
                break;

            default:
                throw new NotSupportedException(
                    $"RemoveOverride for scope {scope} is not supported.");
        }

        if (scope == SettingScope.Organization)
        {
            InvalidateOrganizationCache(RequireAmbientTenantId(), scopeId);
        }
        else
        {
            InvalidateCache(scope, scopeId);
        }
    }

    public async Task LockAsync(
        string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        switch (scope)
        {
            case SettingScope.Instance:
                {
                    await _mutationLock.ExecuteAsync(
                        key,
                        async token =>
                        {
                            SystemSetting? setting = await _systemSettingRepository.GetByKey(key, token);
                            if (setting is null)
                                throw new InvalidOperationException($"Setting '{key}' does not exist at Instance scope.");

                            setting.IsLocked = true;
                            setting.UpdatedAt = DateTime.UtcNow;
                            setting.UpdatedBy = actorId;
                            await _systemSettingRepository.UpsertAsync(setting, token);
                            return true;
                        },
                        ct);
                    InvalidateCache(SettingScope.Instance);
                    break;
                }

            case SettingScope.Tenant:
                {
                    bool locked = await _mutationLock.ExecuteAsync(
                        key,
                        async token =>
                        {
                            if (await _systemSettingRepository.IsLocked(key, token))
                            {
                                throw new InvalidOperationException($"Setting '{key}' is locked at Instance scope.");
                            }

                            return await _tenantSettingRepository.LockAsync(scopeId, key, actorId, token);
                        },
                        ct);
                    if (!locked)
                        throw new InvalidOperationException($"Setting '{key}' does not exist for tenant '{scopeId}'.");

                    InvalidateCache(SettingScope.Tenant, scopeId);
                    _logger.LogInformation(
                        "Tenant setting locked: {SettingKey} for tenant {TenantId}. User caches refresh within {CacheTtlMinutes}m.",
                        key, scopeId, _cacheExpiration.TotalMinutes);
                    break;
                }

            default:
                throw new NotSupportedException(
                    $"Lock is only supported at Instance and Tenant scopes, not {scope}.");
        }
    }

    public async Task UnlockAsync(
        string key, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
    {
        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        switch (scope)
        {
            case SettingScope.Instance:
                {
                    await _mutationLock.ExecuteAsync(
                        key,
                        async token =>
                        {
                            SystemSetting? setting = await _systemSettingRepository.GetByKey(key, token);
                            if (setting is null)
                                throw new InvalidOperationException($"Setting '{key}' does not exist at Instance scope.");

                            setting.IsLocked = false;
                            setting.UpdatedAt = DateTime.UtcNow;
                            setting.UpdatedBy = actorId;
                            await _systemSettingRepository.UpsertAsync(setting, token);
                            return true;
                        },
                        ct);
                    InvalidateCache(SettingScope.Instance);
                    break;
                }

            case SettingScope.Tenant:
                {
                    bool unlocked = await _mutationLock.ExecuteAsync(
                        key,
                        async token =>
                        {
                            if (await _systemSettingRepository.IsLocked(key, token))
                            {
                                throw new InvalidOperationException($"Setting '{key}' is locked at Instance scope.");
                            }

                            return await _tenantSettingRepository.UnlockAsync(scopeId, key, actorId, token);
                        },
                        ct);
                    if (!unlocked)
                        throw new InvalidOperationException($"Setting '{key}' does not exist for tenant '{scopeId}'.");

                    InvalidateCache(SettingScope.Tenant, scopeId);
                    _logger.LogInformation(
                        "Tenant setting unlocked: {SettingKey} for tenant {TenantId}. Cascade restored. User caches refresh within {CacheTtlMinutes}m.",
                        key, scopeId, _cacheExpiration.TotalMinutes);
                    break;
                }

            default:
                throw new NotSupportedException(
                    $"Unlock is only supported at Instance and Tenant scopes, not {scope}.");
        }
    }

    public void InvalidateCache(SettingScope? scope = null, Guid? scopeId = null)
    {
        if (scope is null)
        {
            _cache.Remove(SystemCacheKey);
            return;
        }

        switch (scope.Value)
        {
            case SettingScope.Instance:
                _cache.Remove(SystemCacheKey);
                break;
            case SettingScope.Tenant when scopeId.HasValue:
                _cache.Remove($"{TenantCachePrefix}{scopeId.Value}");
                break;
            case SettingScope.Organization:
                throw new InvalidOperationException(
                    "Organization cache invalidation requires both tenant and organization scope.");
            case SettingScope.Group when scopeId.HasValue:
                _cache.Remove($"{GroupCachePrefix}{scopeId.Value}");
                break;
            case SettingScope.User when scopeId.HasValue:
                _cache.Remove($"{UserCachePrefix}{scopeId.Value}");
                break;
        }
    }

    public void InvalidateOrganizationCache(Guid tenantId, Guid organizationId)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("Tenant scope is required.", nameof(tenantId));
        }

        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization scope is required.", nameof(organizationId));
        }

        _cache.Remove(OrganizationCacheKey(tenantId, organizationId));
    }

    public void InvalidateUserCache(Guid tenantId, Guid userId)
    {
        if (tenantId == Guid.Empty || userId == Guid.Empty)
        {
            return;
        }

        _cache.Remove($"{UserCachePrefix}{tenantId}:{userId}");
    }

    private ResolvedSetting? ResolveSingleKey(
        string key,
        Dictionary<string, SystemSetting> systemDict,
        Dictionary<string, TenantSetting>? tenantDict,
        Dictionary<string, OrganizationSetting>? orgDict,
        Dictionary<string, GroupSetting>? groupDict,
        Dictionary<string, UserPreference>? userDict,
        bool isMultiTenant)
    {
        systemDict.TryGetValue(key, out var systemSetting);
        var definition = SettingRegistry.Get(key);

        if (systemSetting is null && definition is null)
            return null;

        var effectiveValue = systemSetting?.Value ?? definition?.DefaultValue ?? "";
        var valueType = systemSetting?.ValueType ?? definition?.ValueType ?? SettingValueType.String;
        var description = systemSetting?.Description ?? definition?.Description;
        var category = systemSetting?.Category ?? definition?.Category;
        var allowedValues = systemSetting?.AllowedValues;

        // Cascade: Instance → Tenant → Organization → Group → User
        // Lock precedence: Instance locked > Tenant locked > unlocked cascade

        // Instance lock stops ALL overrides — highest precedence.
        // Honored in both SingleTenant and MultiTenant modes so that explicitly locked
        // instance settings (e.g. AI provider config seeded via bootstrap worker or saved
        // via the instance admin UI with LockTenantAiAssistant=true) cannot be shadowed
        // by stale tenant overrides in single-tenant deployments.
        var isInstanceLocked = systemSetting?.IsLocked ?? false;
        if (isInstanceLocked)
        {
            return new ResolvedSetting
            {
                Key = key,
                Value = effectiveValue,
                ValueType = valueType,
                Source = SettingSource.SystemLocked,
                IsLocked = true,
                Description = description,
                Category = category,
                AllowedValues = allowedValues
            };
        }

        // Tenant override
        var source = SettingSource.SystemDefault;
        var isTenantLocked = false;
        if (AllowsScope(definition, SettingScope.Tenant)
            && tenantDict is not null
            && tenantDict.TryGetValue(key, out var tenantOverride))
        {
            effectiveValue = tenantOverride.Value;
            source = SettingSource.TenantOverride;
            isTenantLocked = tenantOverride.IsLocked;
        }

        // Tenant lock stops child overrides (org, group, user)
        // Lower-scope values remain in storage — lock only affects resolution
        if (isTenantLocked)
        {
            return new ResolvedSetting
            {
                Key = key,
                Value = effectiveValue,
                ValueType = valueType,
                Source = SettingSource.TenantLocked,
                IsLocked = true,
                Description = description,
                Category = category,
                AllowedValues = allowedValues
            };
        }

        // Organization override (not locked at instance or tenant)
        if (AllowsScope(definition, SettingScope.Organization)
            && orgDict is not null
            && orgDict.TryGetValue(key, out var orgOverride))
        {
            effectiveValue = orgOverride.Value;
            source = SettingSource.OrganizationOverride;
        }

        // Group override (not locked at instance or tenant)
        if (AllowsScope(definition, SettingScope.Group)
            && groupDict is not null
            && groupDict.TryGetValue(key, out var groupOverride))
        {
            effectiveValue = groupOverride.Value;
            source = SettingSource.GroupOverride;
        }

        // User preference (not locked at instance or tenant, and definition allows User scope)
        if (userDict is not null && userDict.TryGetValue(key, out var userPref))
        {
            var maxScope = definition?.MaxScope ?? SettingScope.Tenant;
            if (maxScope >= SettingScope.User)
            {
                effectiveValue = userPref.Value;
                source = SettingSource.UserPreference;
            }
        }

        return new ResolvedSetting
        {
            Key = key,
            Value = effectiveValue,
            ValueType = valueType,
            Source = source,
            IsLocked = false,
            Description = description,
            Category = category,
            AllowedValues = allowedValues
        };
    }

    private static bool AllowsScope(SettingDefinition? definition, SettingScope scope) =>
        definition is null || scope >= definition.MinScope && scope <= definition.MaxScope;

    private async Task<List<SystemSetting>> GetSystemSettingsAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(SystemCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _systemSettingRepository.GetAllSettings();
        }) ?? [];
    }

    private async Task<List<TenantSetting>> GetTenantSettingsAsync(Guid tenantId, CancellationToken ct)
    {
        var cacheKey = $"{TenantCachePrefix}{tenantId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _tenantSettingRepository.GetAllForTenant(tenantId);
        }) ?? [];
    }

    private async Task UpsertSystemSettingAsync(
        string key,
        string value,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        SystemSetting? existing = await _systemSettingRepository.GetByKey(key, cancellationToken);
        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = actorId;
            await _systemSettingRepository.UpsertAsync(existing, cancellationToken);
        }
        else
        {
            var definition = SettingRegistry.Get(key);
            await _systemSettingRepository.UpsertAsync(new SystemSetting
            {
                SettingKey = key,
                Value = value,
                ValueType = definition?.ValueType ?? SettingValueType.String,
                IsLocked = false,
                Description = definition?.Description,
                Category = definition?.Category ?? "Unknown",
                DisplayOrder = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actorId
            }, cancellationToken);
        }
    }

    private async Task UpsertOrganizationSettingAsync(
        string key,
        string value,
        Guid organizationId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        await _organizationSettingRepository.SetValueAsync(
            RequireAmbientTenantId(),
            organizationId,
            key,
            value,
            actorId,
            cancellationToken);
    }

    private async Task UpsertGroupSettingAsync(
        string key,
        string value,
        Guid groupId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        var existing = await _groupSettingRepository.GetByGroupAndKey(groupId, key);
        if (existing is not null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = actorId;
            await _groupSettingRepository.Update(existing);
        }
        else
        {
            var participation = await _groupTenantRepository.GetByGroupAndTenant(
                groupId,
                _tenantContext.TenantId,
                cancellationToken)
                ?? throw new InvalidOperationException("Group is not available in the current tenant.");
            await _groupSettingRepository.Create(new GroupSetting
            {
                GroupTenantId = participation.Id,
                GroupTenant = participation,
                TenantId = participation.TenantId,
                Tenant = null!,
                SettingKey = key,
                Value = value,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = actorId
            });
        }
    }

    private async Task<List<OrganizationSetting>> GetOrganizationSettingsAsync(
        Guid tenantId,
        Guid organizationId,
        CancellationToken ct)
    {
        string cacheKey = OrganizationCacheKey(tenantId, organizationId);
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _organizationSettingRepository.GetAllForOrganization(
                tenantId,
                organizationId,
                ct);
        }) ?? [];
    }

    private static string OrganizationCacheKey(Guid tenantId, Guid organizationId) =>
        $"{OrgCachePrefix}{tenantId}:{organizationId}";

    private Guid RequireAmbientTenantId()
    {
        Guid tenantId = _tenantContext.TenantId;
        return tenantId != Guid.Empty
            ? tenantId
            : throw new InvalidOperationException("Tenant context is required for organization settings.");
    }

    private async Task<List<GroupSetting>> GetGroupSettingsAsync(Guid groupId, CancellationToken ct)
    {
        var cacheKey = $"{GroupCachePrefix}{groupId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _groupSettingRepository.GetAllForGroup(groupId);
        }) ?? [];
    }

    private async Task<List<UserPreference>> GetUserPreferencesAsync(Guid tenantId, Guid userId, CancellationToken ct)
    {
        var cacheKey = $"{UserCachePrefix}{tenantId}:{userId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _userPreferenceRepository.GetAllForUser(tenantId, userId);
        }) ?? [];
    }
}
