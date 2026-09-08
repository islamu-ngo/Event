using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.Extensions.Caching.Memory;

namespace Explore.Infrastructure.Services;

public class ResolverConfigService : IResolverConfigService
{
    private const string CacheKey = "ResolverConfigService.Configuration";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly ISystemSettingRepository _systemSettingRepository;
    private readonly IMemoryCache _cache;

    public ResolverConfigService(
        ISystemSettingRepository systemSettingRepository,
        IMemoryCache cache)
    {
        _systemSettingRepository = systemSettingRepository;
        _cache = cache;
    }

    public async Task<ResolverConfigurationDto> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(CacheKey, out ResolverConfigurationDto? cached) && cached != null)
        {
            return Copy(cached);
        }

        var configuration = new ResolverConfigurationDto
        {
            HeaderEnabled = DeserializeBoolean((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Routing.ResolverHeaderEnabled, cancellationToken))?.Value, true),
            SubdomainEnabled = DeserializeBoolean((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Routing.ResolverSubdomainEnabled, cancellationToken))?.Value, false),
            CustomDomainEnabled = DeserializeBoolean((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled, cancellationToken))?.Value, false),
            PathEnabled = DeserializeBoolean((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Routing.ResolverPathEnabled, cancellationToken))?.Value, true),
            PathPrefix = NormalizePathPrefix(DeserializeString((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Routing.PathPrefix, cancellationToken))?.Value, "/t")),
            InstanceBaseDomain = NormalizeHost(DeserializeString((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Domains.InstanceBaseDomain, cancellationToken))?.Value, string.Empty)),
            AllowTenantCustomDomains = DeserializeBoolean((await _systemSettingRepository.GetByKey(GovernanceSettingKeys.Domains.AllowTenantCustomDomain, cancellationToken))?.Value, true)
        };

        configuration.HeaderEnabled = true;
        _cache.Set(CacheKey, configuration, CacheDuration);
        return Copy(configuration);
    }

    public async Task ApplyConfigurationAsync(
        PatchResolverConfigurationDto patch,
        ResolverConfigurationDto configuration,
        Guid? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(configuration);

        if (patch.HeaderEnabled.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Routing.ResolverHeaderEnabled,
                JsonSerializer.Serialize(normalized.HeaderEnabled),
                SettingValueType.Boolean,
                true,
                "Routing",
                20,
                "Whether the header tenant resolver is enabled. Must remain enabled for YARP propagation.",
                actorUserId);
        }

        if (patch.CustomDomainEnabled.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
                JsonSerializer.Serialize(normalized.CustomDomainEnabled),
                SettingValueType.Boolean,
                false,
                "Routing",
                21,
                "Whether the custom-domain tenant resolver is enabled.",
                actorUserId);
        }

        if (patch.SubdomainEnabled.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Routing.ResolverSubdomainEnabled,
                JsonSerializer.Serialize(normalized.SubdomainEnabled),
                SettingValueType.Boolean,
                false,
                "Routing",
                22,
                "Whether the subdomain tenant resolver is enabled.",
                actorUserId);
        }

        if (patch.PathEnabled.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Routing.ResolverPathEnabled,
                JsonSerializer.Serialize(normalized.PathEnabled),
                SettingValueType.Boolean,
                false,
                "Routing",
                23,
                "Whether the path-based tenant resolver is enabled.",
                actorUserId);
        }

        if (patch.PathPrefix.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Routing.PathPrefix,
                JsonSerializer.Serialize(normalized.PathPrefix),
                SettingValueType.String,
                false,
                "Routing",
                24,
                "Path prefix used by the path-based tenant resolver.",
                actorUserId);
        }

        if (patch.InstanceBaseDomain.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Domains.InstanceBaseDomain,
                JsonSerializer.Serialize(normalized.InstanceBaseDomain),
                SettingValueType.String,
                false,
                "Domains",
                1,
                "Base domain used for tenant subdomain generation.",
                actorUserId);
        }

        if (patch.AllowTenantCustomDomains.HasValue)
        {
            await UpsertSystemSettingAsync(
                GovernanceSettingKeys.Domains.AllowTenantCustomDomain,
                JsonSerializer.Serialize(normalized.AllowTenantCustomDomains),
                SettingValueType.Boolean,
                false,
                "Domains",
                2,
                "Whether tenant administrators may configure a custom domain.",
                actorUserId);
        }

        if (patch.HasChanges())
        {
            InvalidateCache();
        }
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }

    private static ResolverConfigurationDto Copy(ResolverConfigurationDto source)
    {
        return new ResolverConfigurationDto
        {
            HeaderEnabled = source.HeaderEnabled,
            SubdomainEnabled = source.SubdomainEnabled,
            CustomDomainEnabled = source.CustomDomainEnabled,
            PathEnabled = source.PathEnabled,
            PathPrefix = source.PathPrefix,
            InstanceBaseDomain = source.InstanceBaseDomain,
            AllowTenantCustomDomains = source.AllowTenantCustomDomains
        };
    }

    private async Task UpsertSystemSettingAsync(
        string settingKey,
        string value,
        SettingValueType valueType,
        bool isLocked,
        string category,
        int displayOrder,
        string description,
        Guid? actorUserId)
    {
        await _systemSettingRepository.UpsertAsync(new SystemSetting
        {
            SettingKey = settingKey,
            Value = value,
            ValueType = valueType,
            IsLocked = isLocked,
            Description = description,
            Category = category,
            DisplayOrder = displayOrder,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actorUserId,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = actorUserId
        });
    }

    private static ResolverConfigurationDto Normalize(ResolverConfigurationDto configuration)
    {
        return new ResolverConfigurationDto
        {
            HeaderEnabled = true,
            SubdomainEnabled = configuration.SubdomainEnabled,
            CustomDomainEnabled = configuration.CustomDomainEnabled,
            PathEnabled = configuration.PathEnabled,
            PathPrefix = NormalizePathPrefix(configuration.PathPrefix),
            InstanceBaseDomain = NormalizeHost(configuration.InstanceBaseDomain),
            AllowTenantCustomDomains = configuration.AllowTenantCustomDomains
        };
    }

    private static string NormalizePathPrefix(string? pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return "/t";
        }

        var normalized = pathPrefix.Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        return normalized.TrimEnd('/');
    }

    private static string NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value.Trim().ToLowerInvariant();
        sanitized = sanitized.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase);
        sanitized = sanitized.Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase);
        return sanitized.Trim('/');
    }

    private static bool DeserializeBoolean(string? rawValue, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            return JsonSerializer.Deserialize<bool>(rawValue);
        }
        catch
        {
            return bool.TryParse(rawValue, out var parsed) ? parsed : defaultValue;
        }
    }

    private static string DeserializeString(string? rawValue, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return defaultValue;
        }

        try
        {
            var deserialized = JsonSerializer.Deserialize<string>(rawValue);
            return string.IsNullOrWhiteSpace(deserialized) ? defaultValue : deserialized;
        }
        catch
        {
            return rawValue.Trim('"');
        }
    }
}
