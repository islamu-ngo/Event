using Explore.Application.DTOs.InstanceAdmin;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.InstanceAdmin;

internal static class InstanceTenantPlanMapper
{
    public static InstanceTenantPlanListItemDto ToListItem(TenantPlan plan)
    {
        TenantPlanVersion? latest = plan.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();
        TenantPlanVersion? published = plan.Versions
            .Where(version => version.TenantPlanStatusId == (int)TenantPlanStatusEnum.Published)
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();
        TenantPlanVersion? pricingSource = published ?? latest;

        return new InstanceTenantPlanListItemDto
        {
            Id = plan.Id,
            Key = plan.Key,
            DisplayName = plan.DisplayName,
            Description = plan.Description,
            LatestVersionNumber = latest?.VersionNumber ?? 0,
            PublishedVersionNumber = published?.VersionNumber,
            PriceAmount = pricingSource?.PriceAmount ?? 0m,
            CurrencyCode = pricingSource?.CurrencyCode ?? string.Empty,
            BillingPeriod = pricingSource?.BillingPeriod ?? string.Empty,
            IsActiveForProvisioning = published?.IsActiveForProvisioning ?? false
        };
    }

    public static InstanceTenantPlanDetailDto ToDetail(TenantPlan plan) => new()
    {
        Id = plan.Id,
        Key = plan.Key,
        DisplayName = plan.DisplayName,
        Description = plan.Description,
        Versions = plan.Versions
            .OrderByDescending(version => version.VersionNumber)
            .Select(ToVersion)
            .ToArray()
    };

    public static InstanceTenantPlanAssignmentDto ToAssignment(TenantPlanAssignment assignment) => new()
    {
        Id = assignment.Id,
        TenantId = assignment.TenantId,
        PlanId = assignment.TenantPlanId != Guid.Empty ? assignment.TenantPlanId : assignment.TenantPlan.Id,
        PlanKey = assignment.TenantPlan.Key,
        PlanVersionId = assignment.TenantPlanVersionId != Guid.Empty ? assignment.TenantPlanVersionId : assignment.TenantPlanVersion.Id,
        VersionNumber = assignment.TenantPlanVersion.VersionNumber,
        StatusId = assignment.TenantPlanAssignmentStatusId,
        StatusCode = assignment.TenantPlanAssignmentStatus?.MasterCode ?? string.Empty,
        AssignedAt = assignment.AssignedAt,
        AssignedByUserId = assignment.AssignedByUserId
    };

    private static InstanceTenantPlanVersionDto ToVersion(TenantPlanVersion version) => new()
    {
        Id = version.Id,
        VersionNumber = version.VersionNumber,
        StatusId = version.TenantPlanStatusId,
        StatusCode = version.TenantPlanStatus?.MasterCode ?? string.Empty,
        PriceAmount = version.PriceAmount,
        CurrencyCode = version.CurrencyCode,
        BillingPeriod = version.BillingPeriod,
        IsActiveForProvisioning = version.IsActiveForProvisioning,
        Settings = version.Settings
            .OrderBy(setting => setting.SettingKey, StringComparer.Ordinal)
            .Select(setting => new InstanceTenantPlanSettingDto
            {
                Key = setting.SettingKey,
                JsonValue = setting.JsonValue,
                IsLocked = setting.IsLocked
            })
            .ToArray(),
        Quotas = version.Quotas
            .OrderBy(quota => quota.QuotaKey, StringComparer.Ordinal)
            .Select(quota => new InstanceTenantPlanQuotaDto
            {
                Key = quota.QuotaKey,
                Limit = quota.Limit
            })
            .ToArray()
    };
}
