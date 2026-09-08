using Explore.Application.DTOs.ControlPlane;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Features.ControlPlane;

internal static class ControlPlaneTenantPlanMapper
{
    public static ControlPlaneTenantPlanListItemDto ToListItem(TenantPlan plan)
    {
        TenantPlanVersion? latest = plan.Versions
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();
        TenantPlanVersion? published = plan.Versions
            .Where(version => version.TenantPlanStatusId == (int)TenantPlanStatusEnum.Published)
            .OrderByDescending(version => version.VersionNumber)
            .FirstOrDefault();
        TenantPlanVersion? pricingSource = published ?? latest;

        return new ControlPlaneTenantPlanListItemDto
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

    public static ControlPlaneTenantPlanDetailDto ToDetail(TenantPlan plan) => new()
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

    public static ControlPlaneTenantPlanAssignmentDto ToAssignment(TenantPlanAssignment assignment) => new()
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

    private static ControlPlaneTenantPlanVersionDto ToVersion(TenantPlanVersion version) => new()
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
            .Select(setting => new ControlPlaneTenantPlanSettingDto
            {
                Key = setting.SettingKey,
                JsonValue = setting.JsonValue,
                IsLocked = setting.IsLocked
            })
            .ToArray(),
        Quotas = version.Quotas
            .OrderBy(quota => quota.QuotaKey, StringComparer.Ordinal)
            .Select(quota => new ControlPlaneTenantPlanQuotaDto
            {
                Key = quota.QuotaKey,
                Limit = quota.Limit
            })
            .ToArray()
    };
}
