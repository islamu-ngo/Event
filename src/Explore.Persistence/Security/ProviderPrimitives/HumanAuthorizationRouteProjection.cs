using System.Collections.Immutable;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Reads only the declared authorization-route keys on the caller's original store.</summary>
internal static class HumanAuthorizationRouteProjection
{
    internal static async Task<HumanAuthorizationRouteFacts> ReadAsync(
        ExploreDbContext context, HumanCommitSubject subject, Guid? tenant,
        Action requireCurrent, CancellationToken cancellationToken)
    {
        requireCurrent();
        if (subject.PrincipalId == Guid.Empty)
            throw new UnauthorizedAccessException("The original principal is required.");
        string[] keys = HumanAuthorizationRouteFacts.Keys.ToArray();
        var system = await context.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey))
            .Select(row => new
            {
                row.SettingKey, row.Value, row.SettingValueTypeId, row.IsLocked,
                row.Description, row.Category, row.AllowedValues
            }).ToArrayAsync(cancellationToken);
        requireCurrent();
        var systemByKey = system.ToDictionary(row => row.SettingKey, StringComparer.Ordinal);
        var tenantByKey = new Dictionary<string, TenantSettingFacts>(StringComparer.Ordinal);
        if (tenant is { } selected)
        {
            var overrides = await context.TenantSettingOverrides.AsNoTracking()
                .Where(row => row.TenantId == selected && keys.Contains(row.SettingKey))
                .Select(row => new { row.SettingKey, row.Value, row.IsLocked })
                .ToArrayAsync(cancellationToken);
            requireCurrent();
            tenantByKey = overrides.ToDictionary(row => row.SettingKey,
                row => new TenantSettingFacts(row.Value, row.IsLocked), StringComparer.Ordinal);
        }
        requireCurrent();
        return new(subject.PrincipalId, tenant, keys.Select(key =>
        {
            systemByKey.TryGetValue(key, out var instance);
            tenantByKey.TryGetValue(key, out var scoped);
            return new HumanAuthorizationRouteSettingFact(key, instance is null ? null :
                new SystemSettingFacts(instance.Value, (SettingValueType)instance.SettingValueTypeId, instance.IsLocked,
                    instance.Description, instance.Category, instance.AllowedValues), scoped);
        }).ToImmutableArray());
    }
}
