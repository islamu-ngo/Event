namespace Explore.Persistence.Repositories;

using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using Explore.Persistence.Services;
using Explore.Persistence.Security;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;

public class SystemSettingRepository : ISystemSettingRepository
{
    private readonly ExploreDbContext _dbContext;
    private readonly ISettingMutationLock _mutationLock;
    private readonly HumanSettingsDataBoundary? _humanBoundary;

    private SystemSettingRepository(ExploreDbContext dbContext, ISettingMutationLock mutationLock)
    {
        _dbContext = dbContext;
        _mutationLock = mutationLock;
    }

    /// <summary>Creates an observation-only repository; every mutation requires a separate admitted owner.</summary>
    public static SystemSettingRepository CreateReadOnly(
        ExploreDbContext context, ISettingMutationLock mutationLock) => new(context, mutationLock);

    public SystemSettingRepository(
        ExploreDbContext dbContext,
        ISettingMutationLock mutationLock,
        HumanSettingsDataBoundary humanBoundary)
    {
        _dbContext = dbContext;
        _mutationLock = mutationLock;
        _humanBoundary = humanBoundary;
    }

    public async Task<SystemSetting?> GetByKey(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);
    }

    public async Task<string?> UpsertAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        setting = Snapshot(setting);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue, cancellationToken);
        Explore.Domain.Ai.AiCredentialReference.RejectRetiredSettingMutation(setting.SettingKey);
        EventResourceSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EventResourceProviderBindingDocument.RejectGenericMutation(setting.SettingKey);
        VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        InstanceOperatorIdentitySettingKeys.RejectGenericMutation(setting.SettingKey);
        if (PublicationPolicySettingKeys.All.Contains(setting.SettingKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return await _mutationLock.ExecuteAsync(
            setting.SettingKey,
            token => UpsertCoreAsync(setting, token),
            cancellationToken);
    }

    public async Task<string?> UpsertInCurrentTransactionAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        EventResourceProviderBindingDocument.RejectGenericMutation(setting.SettingKey);
        setting = Snapshot(setting);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue, cancellationToken);
        Explore.Domain.Ai.AiCredentialReference.RejectRetiredSettingMutation(setting.SettingKey);
        EventResourceSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        if (_dbContext.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Caller-owned system-setting writes require an active transaction.");
        }

        if (PublicationPolicySettingKeys.All.Contains(
                setting.SettingKey,
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                "Guarded publication-policy settings require coordinated mutation.");
        }

        if (_dbContext.IsConfigurationInstanceImportOwner
            && ConfigurationImportRepositoryNativeOperations.For(_dbContext) is { } native)
            return await native.UpsertSystemSettingAsync(setting, cancellationToken);

        return RelationalSettingMutationLock.RequiresEmailDeliveryFence([setting.SettingKey])
            ? await _mutationLock.ExecuteAsync(setting.SettingKey,
                token => UpsertCoreAsync(setting, token), cancellationToken)
            : await UpsertCoreAsync(setting, cancellationToken);
    }

    public async Task<string?> UpsertLockAsync(
        SystemSetting setting,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);
        setting = Snapshot(setting);
        await RequireAsync(setting, HumanSettingsMutationKind.SetLock, cancellationToken);
        Explore.Domain.Ai.AiCredentialReference.RejectRetiredSettingMutation(setting.SettingKey);
        EventResourceSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EventResourceProviderBindingDocument.RejectGenericMutation(setting.SettingKey);
        VisitorAccessSettingMutationGuard.RejectGenericMutation(setting.SettingKey);
        EmailDeliverySettingKeys.RejectGenericMutation(setting.SettingKey);
        InstanceOperatorIdentitySettingKeys.RejectGenericMutation(setting.SettingKey);
        if (PublicationPolicySettingKeys.All.Contains(setting.SettingKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Guarded publication-policy settings require coordinated mutation.");
        }

        return await _mutationLock.ExecuteAsync(
            setting.SettingKey,
            async token =>
            {
                await RequireAsync(setting, HumanSettingsMutationKind.SetLock, token);
                if (_humanBoundary!.OrdinarySource is { } ordinary)
                    return await _humanBoundary.OrdinaryStore.WriteSystemAsync(
                        ordinary, setting, HumanSettingsMutationKind.SetLock, token);
                var policyBefore = RelationalSettingMutationLock.RequiresEmailDeliveryFence([setting.SettingKey])
                    ? await EmailDeliveryPolicyReader.ReadAllAsync(_dbContext, token)
                    : null;
                DetachTrackedSmtpSetting(setting.SettingKey);
                SystemSetting? existing = await _dbContext.SystemSettings
                    .FirstOrDefaultAsync(candidate => candidate.SettingKey == setting.SettingKey, token);
                string? previousValue = existing?.Value;

                if (existing is null)
                {
                    // A lock proof cannot create a caller-supplied fallback value.
                    await RequireAsync(setting, HumanSettingsMutationKind.SetValue, token);
                    _dbContext.SystemSettings.Add(setting);
                }
                else
                {
                    existing.IsLocked = setting.IsLocked;
                    existing.UpdatedAt = setting.UpdatedAt ?? DateTime.UtcNow;
                    existing.UpdatedBy = setting.UpdatedBy;
                }

                await _dbContext.SaveChangesAsync(token);
                if (policyBefore is not null)
                    await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(_dbContext, policyBefore,
                        setting.UpdatedBy ?? setting.CreatedBy, token);
                await RequireAsync(setting, HumanSettingsMutationKind.SetLock, token);
                return previousValue;
            },
            cancellationToken);
    }

    public async Task<List<SystemSetting>> GetAllSettings(
        string? category = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SystemSettings.AsNoTracking();

        if (!string.IsNullOrEmpty(category))
        {
            query = query.Where(s => s.Category == category);
        }

        return await query
            .OrderBy(s => s.Category)
            .ThenBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsLocked(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.SystemSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SettingKey == key, cancellationToken);

        return setting?.IsLocked ?? false;
    }

    private async Task<string?> UpsertCoreAsync(
        SystemSetting setting,
        CancellationToken cancellationToken)
    {
        if (_humanBoundary is { OrdinarySource: { } ordinary } boundary)
            return await boundary.RequireOriginalOrdinaryWriter(
                    _dbContext, ordinary, setting.UpdatedBy ?? setting.CreatedBy)
                .WriteSystemAsync(ordinary, setting, HumanSettingsMutationKind.SetValue, cancellationToken);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue, cancellationToken);
        var policyBefore = RelationalSettingMutationLock.RequiresEmailDeliveryFence([setting.SettingKey])
            ? await EmailDeliveryPolicyReader.ReadAllAsync(_dbContext, cancellationToken)
            : null;
        DetachTrackedSmtpSetting(setting.SettingKey);
        SystemSetting? existing = await _dbContext.SystemSettings
            .FirstOrDefaultAsync(
                candidate => candidate.SettingKey == setting.SettingKey,
                cancellationToken);
        string? previousValue = existing?.Value;
        if (setting.IsLocked != (existing?.IsLocked ?? false))
            await RequireAsync(setting, HumanSettingsMutationKind.SetLock, cancellationToken);

        if (existing is null)
        {
            _dbContext.SystemSettings.Add(setting);
        }
        else
        {
            existing.Value = setting.Value;
            existing.ValueType = setting.ValueType;
            existing.IsLocked = setting.IsLocked;
            existing.AllowedValues = setting.AllowedValues;
            existing.Description = setting.Description;
            existing.Category = setting.Category;
            existing.DisplayOrder = setting.DisplayOrder;
            existing.UpdatedAt = setting.UpdatedAt ?? DateTime.UtcNow;
            existing.UpdatedBy = setting.UpdatedBy;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        if (policyBefore is not null)
            await EmailDeliveryPolicyRevisionTracker.RecordInstanceAsync(_dbContext, policyBefore,
                setting.UpdatedBy ?? setting.CreatedBy, cancellationToken);
        await RequireAsync(setting, HumanSettingsMutationKind.SetValue, cancellationToken);
        return previousValue;
    }

    private Task RequireAsync(SystemSetting setting, HumanSettingsMutationKind kind, CancellationToken token) =>
        (_humanBoundary ?? throw new InvalidOperationException("This settings observation cannot mutate data."))
            .RequireAsync(_dbContext, SettingScope.Instance, null, null, setting.SettingKey, kind,
            value: kind == HumanSettingsMutationKind.SetValue ? setting.Value : null,
            isLocked: kind == HumanSettingsMutationKind.SetLock ? setting.IsLocked : null,
            auditActorId: setting.UpdatedBy ?? setting.CreatedBy, cancellationToken: token);

    private static SystemSetting Snapshot(SystemSetting setting) => new()
    {
        Id = setting.Id,
        SettingKey = setting.SettingKey,
        Value = setting.Value,
        SettingValueTypeId = setting.SettingValueTypeId,
        IsLocked = setting.IsLocked,
        AllowedValues = setting.AllowedValues,
        Description = setting.Description,
        Category = setting.Category,
        DisplayOrder = setting.DisplayOrder,
        CreatedAt = setting.CreatedAt,
        CreatedBy = setting.CreatedBy,
        UpdatedAt = setting.UpdatedAt,
        UpdatedBy = setting.UpdatedBy
    };

    private void DetachTrackedSmtpSetting(string key)
    {
        if (!RelationalSettingMutationLock.RequiresEmailDeliveryFence([key]))
            return;

        // The policy lock protects the next read, but EF's identity map can still contain
        // an earlier transaction's row. Detaching preserves a same-instance request's values.
        string normalizedKey = RelationalSettingMutationLock.NormalizeSettingKey(key);
        var entries = _dbContext.ChangeTracker.Entries<SystemSetting>()
            .Where(entry => RelationalSettingMutationLock.NormalizeSettingKey(entry.Entity.SettingKey) == normalizedKey)
            .ToArray();
        foreach (var entry in entries)
            entry.State = EntityState.Detached;
    }
}
