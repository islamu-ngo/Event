using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;

namespace Explore.Application.Features.ControlPlane.Plans;

public sealed class TenantPlanStorageQuotaCeilingPolicy(ISystemSettingRepository systemSettingRepository)
{
    public async Task<string?> ValidateAsync(
        IEnumerable<TenantPlanVersionQuota> quotas,
        CancellationToken cancellationToken = default)
    {
        TenantPlanVersionQuota? storageQuota = quotas
            .FirstOrDefault(quota => quota.QuotaKey == TenantPlanQuotaKeys.StorageBytes);
        if (storageQuota is null)
        {
            return null;
        }

        SystemSetting? ceilingSetting = await systemSettingRepository.GetByKey(
            GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
            cancellationToken);
        if (ceilingSetting is null || !TryParseLong(ceilingSetting.Value, out long ceiling))
        {
            return null;
        }

        return storageQuota.Limit > ceiling ? "tenant_plan_quota_ceiling_exceeded" : null;
    }

    private static bool TryParseLong(string value, out long parsed)
    {
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return true;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind == JsonValueKind.Number)
            {
                return document.RootElement.TryGetInt64(out parsed);
            }

            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return long.TryParse(
                    document.RootElement.GetString(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out parsed);
            }
        }
        catch (JsonException)
        {
        }

        parsed = 0;
        return false;
    }
}
