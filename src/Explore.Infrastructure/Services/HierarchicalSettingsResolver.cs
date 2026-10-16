namespace Explore.Infrastructure.Services;

using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using IGroupSettingRepository = Explore.Application.Contracts.Persistence.IGroupSettingRepository;
using IGroupTenantRepository = Explore.Application.Contracts.Persistence.IGroupTenantRepository;
using IOrganizationSettingRepository = Explore.Application.Contracts.Persistence.IOrganizationSettingRepository;
using ISettingMutationLock = Explore.Application.Contracts.Persistence.ISettingMutationLock;
using ISystemSettingRepository = Explore.Application.Contracts.Persistence.ISystemSettingRepository;
using ITenantSettingRepository = Explore.Application.Contracts.Persistence.ITenantSettingRepository;
using IUserPreferenceRepository = Explore.Application.Contracts.Persistence.IUserPreferenceRepository;
using IHierarchicalSettingsResolver = Explore.Application.Contracts.Infrastructure.IHierarchicalSettingsResolver;
using ITenantContext = Explore.Application.Contracts.Infrastructure.ITenantContext;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

/// <summary>
/// Resolves settings through a 5-tier hierarchy with batch loading.
/// Loads all settings for requested scopes in ≤2 queries (system + scoped),
/// then merges with lock precedence.
/// </summary>
public class HierarchicalSettingsResolver : HierarchicalSettingsReader, IHierarchicalSettingsResolver
{
    private readonly IGroupTenantRepository _groupTenantRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ISettingMutationLock _mutationLock;
    private readonly IEmailDeliverySettingsWriter _emailSettingsWriter;
    private readonly IEventResourceSettingsWriter _eventResourceSettingsWriter;
    private readonly ILogger<HierarchicalSettingsResolver> _logger;

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
        ILogger<HierarchicalSettingsResolver> logger,
        IEmailDeliverySettingsWriter emailSettingsWriter,
        IEventResourceSettingsWriter eventResourceSettingsWriter,
        IInstanceTenantSettingsReader? instanceTenantReader = null)
        : base(systemSettingRepository, tenantSettingRepository, organizationSettingRepository,
            groupSettingRepository, userPreferenceRepository, cache, instanceTenantReader)
    {
        _groupTenantRepository = groupTenantRepository;
        _tenantContext = tenantContext;
        _mutationLock = mutationLock;
        _logger = logger;
        _emailSettingsWriter = emailSettingsWriter;
        _eventResourceSettingsWriter = eventResourceSettingsWriter;
    }

    public async Task SetValueAsync(
        string key, string value, SettingScope scope, Guid scopeId, Guid actorId, CancellationToken ct = default)
    {
        if (EmailDeliverySettingKeys.Contains(key))
        {
            await ApplySmtpMutationAsync(new EmailDeliverySettingMutation(
                TenantId: scope == SettingScope.Tenant ? scopeId : null, Key: key,
                Kind: EmailDeliverySettingMutationKind.SetValue, Value: value), scope, actorId, ct);
            return;
        }

        if (EventResourceSettingMutationGuard.Handles(key))
        {
            await ApplyEventResourceMutationAsync(new EventResourceSettingMutation(
                scope == SettingScope.Tenant ? scopeId : null, key,
                EventResourceSettingMutationKind.SetValue, value), scope, actorId, ct);
            return;
        }

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
                await _mutationLock.ExecuteManyAsync(
                    TenantMutationKeys(key),
                    async token =>
                    {
                        await EnsureTenantMutationAllowedAsync(key, token);

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
        if (EmailDeliverySettingKeys.Contains(key))
        {
            await ApplySmtpMutationAsync(new EmailDeliverySettingMutation(
                TenantId: scope == SettingScope.Tenant ? scopeId : null, Key: key,
                Kind: EmailDeliverySettingMutationKind.Remove), scope, actorId, ct);
            return;
        }

        if (EventResourceSettingMutationGuard.Handles(key))
        {
            await ApplyEventResourceMutationAsync(new EventResourceSettingMutation(
                scope == SettingScope.Tenant ? scopeId : null, key,
                EventResourceSettingMutationKind.Remove), scope, actorId, ct);
            return;
        }

        if (PublicationPolicySettingKeys.All.Contains(key, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        switch (scope)
        {
            case SettingScope.Tenant:
                await _mutationLock.ExecuteManyAsync(
                    TenantMutationKeys(key),
                    async token =>
                    {
                        await EnsureTenantMutationAllowedAsync(key, token);

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
        if (EmailDeliverySettingKeys.Contains(key))
        {
            await ApplySmtpMutationAsync(new EmailDeliverySettingMutation(
                TenantId: scope == SettingScope.Tenant ? scopeId : null, Key: key,
                Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: true), scope, actorId, ct);
            return;
        }

        if (EventResourceSettingMutationGuard.Handles(key))
        {
            await ApplyEventResourceMutationAsync(new EventResourceSettingMutation(
                scope == SettingScope.Tenant ? scopeId : null, key,
                EventResourceSettingMutationKind.SetLock, IsLocked: true), scope, actorId, ct);
            return;
        }

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
                    bool locked = await _mutationLock.ExecuteManyAsync(
                        TenantMutationKeys(key),
                        async token =>
                        {
                            await EnsureTenantMutationAllowedAsync(key, token);

                            return await _tenantSettingRepository.LockAsync(scopeId, key, actorId, token);
                        },
                        ct);
                    if (!locked)
                        throw new InvalidOperationException($"Setting '{key}' does not exist for tenant '{scopeId}'.");

                    InvalidateCache(SettingScope.Tenant, scopeId);
                    _logger.LogInformation(
                        "Tenant setting locked. User caches refresh within {CacheTtlMinutes}m.",
                        _cacheExpiration.TotalMinutes);
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
        if (EmailDeliverySettingKeys.Contains(key))
        {
            await ApplySmtpMutationAsync(new EmailDeliverySettingMutation(
                TenantId: scope == SettingScope.Tenant ? scopeId : null, Key: key,
                Kind: EmailDeliverySettingMutationKind.SetLock, IsLocked: false), scope, actorId, ct);
            return;
        }

        if (EventResourceSettingMutationGuard.Handles(key))
        {
            await ApplyEventResourceMutationAsync(new EventResourceSettingMutation(
                scope == SettingScope.Tenant ? scopeId : null, key,
                EventResourceSettingMutationKind.SetLock, IsLocked: false), scope, actorId, ct);
            return;
        }

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
                    bool unlocked = await _mutationLock.ExecuteManyAsync(
                        TenantMutationKeys(key),
                        async token =>
                        {
                            await EnsureTenantMutationAllowedAsync(key, token);

                            return await _tenantSettingRepository.UnlockAsync(scopeId, key, actorId, token);
                        },
                        ct);
                    if (!unlocked)
                        throw new InvalidOperationException($"Setting '{key}' does not exist for tenant '{scopeId}'.");

                    InvalidateCache(SettingScope.Tenant, scopeId);
                    _logger.LogInformation(
                        "Tenant setting unlocked. Cascade restored. User caches refresh within {CacheTtlMinutes}m.",
                        _cacheExpiration.TotalMinutes);
                    break;
                }

            default:
                throw new NotSupportedException(
                    $"Unlock is only supported at Instance and Tenant scopes, not {scope}.");
        }
    }

    private async Task ApplySmtpMutationAsync(
        EmailDeliverySettingMutation mutation, SettingScope scope, Guid actorId, CancellationToken cancellationToken)
    {
        if (scope is not SettingScope.Instance and not SettingScope.Tenant)
        {
            new EmailDeliverySettingsWriteResult(Status: EmailDeliverySettingsWriteStatus.InvalidMutation, Changes: [])
                .EnsureAccepted();
        }
        var result = await _emailSettingsWriter.ApplyAsync([mutation], actorId, cancellationToken);
        result.EnsureAccepted();
        InvalidateCache(scope, mutation.TenantId);
    }

    private async Task ApplyEventResourceMutationAsync(
        EventResourceSettingMutation mutation, SettingScope scope, Guid actorId,
        CancellationToken cancellationToken)
    {
        if (scope is not SettingScope.Instance and not SettingScope.Tenant)
            throw new InvalidOperationException(
                "Resource governance settings can only be mutated at Instance or Tenant scope.");
        EventResourceSettingsWriteResult result = await _eventResourceSettingsWriter.ApplyAsync(
            [mutation], actorId, cancellationToken);
        result.EnsureAccepted();
        InvalidateCache(scope, mutation.TenantId);
    }

    private static string[] TenantMutationKeys(string key) =>
        IsSmtpSetting(key) ? [key, GovernanceSettingKeys.TenantDelegation.LockSmtp] : [key];

    private async Task EnsureTenantMutationAllowedAsync(string key, CancellationToken ct)
    {
        if (await _systemSettingRepository.IsLocked(key, ct))
            throw new SettingSystemLockedException(key);

        if (IsSmtpSetting(key))
        {
            var delegation = await _systemSettingRepository.GetByKey(GovernanceSettingKeys.TenantDelegation.LockSmtp, ct);
            if (SettingValueSerializer.DeserializeBool(delegation?.Value, true))
                throw new SettingSystemLockedException(key);
        }
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

    private Guid RequireAmbientTenantId()
    {
        Guid tenantId = _tenantContext.TenantId;
        return tenantId != Guid.Empty
            ? tenantId
            : throw new InvalidOperationException("Tenant context is required for organization settings.");
    }

}
