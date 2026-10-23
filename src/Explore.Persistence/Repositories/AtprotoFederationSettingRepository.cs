using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Security;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoFederationSettingRepository(
    ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment) : IAtprotoFederationSettingRepository
{
    public async Task<IReadOnlyList<AtprotoFederationInstanceSetting>> GetInstanceAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        string[] selected = AtprotoFederationReadAdmission.CopyKeys(keys);
        AtprotoFederationReadAdmission.ValidateScopedInstance(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Length == 0) return [];
        await using var admission = await AtprotoFederationReadAdmission.OpenScopedInstanceAsync(
            context, enrollment, cancellationToken);
        var rows = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
            .Where(setting => selected.Contains(setting.SettingKey)).ToArrayAsync(cancellationToken);
        await admission.ValidateAsync(cancellationToken);
        return rows;
    }

    public async Task<IReadOnlyList<AtprotoFederationTenantSetting>> GetForTenantAsync(
        Guid tenantId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        string[] selected = AtprotoFederationReadAdmission.CopyKeys(keys);
        AtprotoFederationReadAdmission.ValidateScopedTenant(context, tenantId);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Length == 0) return [];
        await using var admission = await AtprotoFederationReadAdmission.OpenScopedTenantAsync(
            context, tenantId, enrollment, cancellationToken);
        var rows = await context.Set<AtprotoFederationTenantSetting>().AsNoTracking()
            .Where(setting => setting.TenantId == tenantId && selected.Contains(setting.SettingKey))
            .ToArrayAsync(cancellationToken);
        await admission.ValidateAsync(cancellationToken);
        return rows;
    }
}
