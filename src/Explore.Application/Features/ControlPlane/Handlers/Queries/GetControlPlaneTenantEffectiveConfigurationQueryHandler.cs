using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.ControlPlane.Plans;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using Explore.Application.Lookups;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Settings;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantEffectiveConfigurationQueryHandler(
    IHierarchicalSettingsResolver settingsResolver,
    ITenantPlanRepository tenantPlanRepository,
    ITenantStorageSettingService tenantStorageSettingService)
    : IRequestHandler<GetControlPlaneTenantEffectiveConfigurationQuery, ControlPlaneTenantEffectiveConfigurationDto>
{
    public async Task<ControlPlaneTenantEffectiveConfigurationDto> Handle(
        GetControlPlaneTenantEffectiveConfigurationQuery request,
        CancellationToken cancellationToken)
    {
        SettingDefinition[] definitions = SettingRegistry.All
            .OrderBy(definition => definition.Category, StringComparer.Ordinal)
            .ThenBy(definition => definition.Key, StringComparer.Ordinal)
            .ToArray();
        var resolvedSettings = await settingsResolver.ResolveBatchAsync(
            definitions.Select(definition => definition.Key),
            new SettingContext(TenantId: request.TenantId),
            cancellationToken);

        TenantPlanAssignment? assignment = await tenantPlanRepository.GetActiveAssignmentForTenantAsync(
            request.TenantId,
            cancellationToken);
        TenantPlanAssignment? rollbackAssignment = assignment is null
            ? null
            : await tenantPlanRepository.GetPreviousEligibleAssignmentForTenantAsync(
                request.TenantId,
                assignment.Id,
                cancellationToken);
        TenantPlanVersion? assignedVersion = assignment is null
            ? null
            : await tenantPlanRepository.GetVersionAsync(assignment.TenantPlanVersionId, cancellationToken);
        TenantStorageSettingsDto storage = await tenantStorageSettingService.ReadSettingsAsync(
            request.TenantId,
            cancellationToken);

        return new ControlPlaneTenantEffectiveConfigurationDto
        {
            TenantId = request.TenantId,
            PlanAssignment = assignment is null ? null : ControlPlaneTenantPlanMapper.ToAssignment(assignment),
            RollbackAssignment = rollbackAssignment is null
                ? null
                : ControlPlaneTenantPlanMapper.ToAssignment(rollbackAssignment),
            Settings = MapSettings(definitions, resolvedSettings),
            Quotas = MapQuotas(assignedVersion, storage)
        };
    }

    private static IReadOnlyList<ControlPlaneTenantEffectiveSettingDto> MapSettings(
        IReadOnlyList<SettingDefinition> definitions,
        IReadOnlyList<ResolvedSetting> resolvedSettings)
    {
        var settings = new List<ControlPlaneTenantEffectiveSettingDto>(definitions.Count);

        for (var index = 0; index < definitions.Count; index++)
        {
            SettingDefinition definition = definitions[index];
            ResolvedSetting resolved = resolvedSettings[index];
            LookupReference valueType = NormalizedLookupMetadata.SettingValueType((int)definition.ValueType);

            settings.Add(new ControlPlaneTenantEffectiveSettingDto
            {
                Key = definition.Key,
                Category = definition.Category,
                Value = definition.IsSensitive
                    ? string.Empty
                    : SettingValueSerializer.ToDisplayValue(
                        resolved.Value,
                        definition.ValueType,
                        definition.DefaultValue),
                SettingValueTypeId = valueType.Id,
                SettingValueTypeCode = valueType.Code,
                SettingValueTypeName = valueType.Name,
                ValueSource = resolved.Source.ToString(),
                IsLocked = resolved.IsLocked,
                LockSource = GetLockSource(resolved),
                Description = resolved.Description ?? definition.Description,
                IsSensitive = definition.IsSensitive,
                AllowedValues = definition.AllowedValues?.ToArray() ?? []
            });
        }

        return settings;
    }

    private static string? GetLockSource(ResolvedSetting setting)
    {
        return setting.IsLocked || setting.Source is SettingSource.SystemLocked or SettingSource.TenantLocked
            ? setting.Source.ToString()
            : null;
    }

    private static IReadOnlyList<ControlPlaneTenantQuotaUsageDto> MapQuotas(
        TenantPlanVersion? assignedVersion,
        TenantStorageSettingsDto storage)
    {
        var quotas = assignedVersion?.Quotas
            .OrderBy(quota => quota.QuotaKey, StringComparer.Ordinal)
            .Select(quota => MapPlanQuota(quota, storage))
            .ToList() ?? [];

        if (quotas.All(quota => !string.Equals(quota.Key, TenantPlanQuotaKeys.StorageBytes, StringComparison.Ordinal)))
        {
            quotas.Add(MapStorageQuota(storage));
        }

        return quotas;
    }

    private static ControlPlaneTenantQuotaUsageDto MapPlanQuota(
        TenantPlanVersionQuota quota,
        TenantStorageSettingsDto storage)
    {
        if (string.Equals(quota.QuotaKey, TenantPlanQuotaKeys.StorageBytes, StringComparison.Ordinal))
        {
            return MapStorageQuota(storage, quota.Limit, "TenantPlan");
        }

        return new ControlPlaneTenantQuotaUsageDto
        {
            Key = quota.QuotaKey,
            Limit = quota.Limit,
            Available = quota.Limit,
            Source = "TenantPlan"
        };
    }

    private static ControlPlaneTenantQuotaUsageDto MapStorageQuota(
        TenantStorageSettingsDto storage,
        long? limit = null,
        string? source = null)
    {
        long effectiveLimit = limit ?? storage.TenantQuotaBytes;
        long used = storage.Usage.UsedBytes;
        long reserved = storage.Usage.ReservedBytes;

        return new ControlPlaneTenantQuotaUsageDto
        {
            Key = TenantPlanQuotaKeys.StorageBytes,
            Limit = effectiveLimit,
            Used = used,
            Reserved = reserved,
            Quarantined = storage.Usage.QuarantinedBytes,
            Available = Math.Max(0, effectiveLimit - used - reserved),
            ObjectCount = storage.Usage.ObjectCount,
            Provider = storage.Provider,
            Source = source ?? storage.EffectivePolicy.QuotaSource,
            LastRecalculatedAt = storage.Usage.LastRecalculatedAt
        };
    }
}
