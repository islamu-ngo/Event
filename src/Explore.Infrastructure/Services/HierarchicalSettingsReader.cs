namespace Explore.Infrastructure.Services;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.Extensions.Caching.Memory;

/// <summary>
/// Reads the five-tier settings hierarchy without acquiring a human mutation capability.
/// Cache invalidation changes only the local read snapshot, never persisted settings.
/// </summary>
public class HierarchicalSettingsReader : IHierarchicalSettingsReader
{
    protected readonly ISystemSettingRepository _systemSettingRepository;
    protected readonly ITenantSettingRepository _tenantSettingRepository;
    protected readonly IOrganizationSettingRepository _organizationSettingRepository;
    protected readonly IGroupSettingRepository _groupSettingRepository;
    private readonly IUserPreferenceRepository _userPreferenceRepository;
    private readonly IMemoryCache _cache;
    private readonly IInstanceTenantSettingsReader? _instanceTenantReader;
    private readonly IAtprotoFederationSettingRepository _federationSettings;
    protected readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

    private const string SystemCacheKey = "HierSettings:System";
    private const string TenantCachePrefix = "HierSettings:Tenant:";
    private const string OrgCachePrefix = "HierSettings:Org:";
    private const string GroupCachePrefix = "HierSettings:Group:";
    private const string UserCachePrefix = "HierSettings:User:";

    public HierarchicalSettingsReader(
        ISystemSettingRepository systemSettingRepository,
        ITenantSettingRepository tenantSettingRepository,
        IOrganizationSettingRepository organizationSettingRepository,
        IGroupSettingRepository groupSettingRepository,
        IUserPreferenceRepository userPreferenceRepository,
        IMemoryCache cache,
        IAtprotoFederationSettingRepository federationSettings,
        IInstanceTenantSettingsReader? instanceTenantReader = null)
    {
        _systemSettingRepository = systemSettingRepository;
        _tenantSettingRepository = tenantSettingRepository;
        _organizationSettingRepository = organizationSettingRepository;
        _groupSettingRepository = groupSettingRepository;
        _userPreferenceRepository = userPreferenceRepository;
        _cache = cache;
        _instanceTenantReader = instanceTenantReader;
        _federationSettings = federationSettings;
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

        string[] federationKeys = keyList.Where(AtprotoFederationSettingPolicy.IsDedicatedKey)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (federationKeys.Length > 0)
        {
            var instances = (await _federationSettings.GetInstanceAsync(federationKeys, ct))
                .ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
            IReadOnlyList<AtprotoFederationTenantSetting> overrides = context.TenantId is { } tenantId
                ? await _federationSettings.GetForTenantAsync(tenantId, federationKeys, ct) : [];
            var tenants = overrides.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
            var resolvedByKey = new Dictionary<string, ResolvedSetting>(StringComparer.Ordinal);
            foreach (string key in federationKeys)
            {
                instances.TryGetValue(key, out var instance);
                tenants.TryGetValue(key, out var tenant);
                var facts = AtprotoFederationSettingFacts.Create(key, instance, tenant, context.TenantId);
                resolvedByKey.Add(key, HierarchicalSettingMerge.Resolve(key, facts)
                    ?? throw new InvalidOperationException("Dedicated federation metadata is unavailable."));
            }

            string[] ordinaryKeys = keyList.Where(key => !AtprotoFederationSettingPolicy.IsDedicatedKey(key)).ToArray();
            if (ordinaryKeys.Length > 0)
            {
                // Federation has always made the entire mixed observation authoritative.
                foreach (var resolved in await ResolveBatchAsync(ordinaryKeys,
                    context with { RequireAuthoritativeReads = true }, ct))
                    resolvedByKey[resolved.Key] = resolved;
            }
            return keyList.Where(resolvedByKey.ContainsKey).Select(key => resolvedByKey[key]).ToArray();
        }

        // Current policy boundaries cannot depend on another replica invalidating this process's cache.
        var requiresAuthoritativePolicy = context.RequireAuthoritativeReads || keyList.Any(key => IsSmtpSetting(key)
            || key == GovernanceSettingKeys.TenantDelegation.LockSmtp
            || key.StartsWith("public_experience.", StringComparison.Ordinal)
            || key.StartsWith("event_discovery.", StringComparison.Ordinal)
            || key.StartsWith("federation.", StringComparison.Ordinal));
        var current = _instanceTenantReader is null ? null
            : ((IReadOnlyList<SystemSetting> System, IReadOnlyList<TenantSetting> Tenant)?)
                await _instanceTenantReader.ReadAsync(context.TenantId,
                    keyList.Distinct(StringComparer.Ordinal).ToArray(), ct);
        if (!requiresAuthoritativePolicy && current is { } image)
        {
            if (!_cache.TryGetValue<List<SystemSetting>>(SystemCacheKey, out var cachedSystem))
                cachedSystem = _cache.Set(SystemCacheKey, image.System.ToList(), _cacheExpiration);
            IReadOnlyList<TenantSetting> cachedTenant = image.Tenant;
            if (context.TenantId is { } selectedTenant)
            {
                string cacheKey = $"{TenantCachePrefix}{selectedTenant}";
                if (!_cache.TryGetValue<List<TenantSetting>>(cacheKey, out var tenantRows))
                    tenantRows = _cache.Set(cacheKey, image.Tenant.ToList(), _cacheExpiration);
                cachedTenant = tenantRows!;
            }
            current = (cachedSystem!, cachedTenant);
        }
        var systemSettings = current?.System ?? (requiresAuthoritativePolicy
            ? await _systemSettingRepository.GetAllSettings(cancellationToken: ct)
            : await GetSystemSettingsAsync(ct));
        var systemDict = systemSettings.ToDictionary(s => s.SettingKey, s => s);

        // Load tenant settings if context has tenant
        Dictionary<string, TenantSetting>? tenantDict = null;
        if (context.TenantId.HasValue)
        {
            var tenantSettings = current?.Tenant ?? (requiresAuthoritativePolicy
                ? await _tenantSettingRepository.GetByTenantAndKeys(
                    context.TenantId.Value, keyList.Distinct(StringComparer.Ordinal).ToArray(), ct)
                : await GetTenantSettingsAsync(context.TenantId.Value, ct));
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

        // Resolve each key through the full cascade
        var results = new List<ResolvedSetting>(keyList.Count);
        foreach (var key in keyList)
        {
            var resolved = HierarchicalSettingMerge.Resolve(key, systemDict, tenantDict, orgDict, groupDict, userDict);
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

    protected static bool IsSmtpSetting(string key) =>
        EmailSettingDefinitions.All.Any(definition => definition.Key == key);

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

    private async Task<List<SystemSetting>> GetSystemSettingsAsync(CancellationToken ct)
    {
        return await _cache.GetOrCreateAsync(SystemCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _systemSettingRepository.GetAllSettings(cancellationToken: ct);
        }) ?? [];
    }

    private async Task<List<TenantSetting>> GetTenantSettingsAsync(Guid tenantId, CancellationToken ct)
    {
        var cacheKey = $"{TenantCachePrefix}{tenantId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheExpiration;
            return await _tenantSettingRepository.GetAllForTenant(tenantId, ct);
        }) ?? [];
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
