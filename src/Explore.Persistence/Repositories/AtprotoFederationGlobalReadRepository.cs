using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Schema;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoFederationGlobalReadRepository(
    IGlobalExploreDbContextFactory factory, PostgresNativeQueueEnrollment? enrollment)
    : IAtprotoFederationGlobalReadRepository
{
    public async Task<IReadOnlyList<AtprotoFederationInstanceSetting>> GetInstanceAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        string[] selected = AtprotoFederationReadAdmission.CopyKeys(keys);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Length == 0) return [];
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var admission = await AtprotoFederationReadAdmission.OpenGlobalAsync(
                context, enrollment, cancellationToken);
            var rows = await context.Set<AtprotoFederationInstanceSetting>().AsNoTracking()
                .Where(setting => selected.Contains(setting.SettingKey)).ToArrayAsync(cancellationToken);
            await admission.ValidateAsync(cancellationToken);
            return rows;
        });
    }

    public async Task<IReadOnlyList<AtprotoFederationTenantSetting>> GetOverridesAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        string[] selected = AtprotoFederationReadAdmission.CopyKeys(keys);
        cancellationToken.ThrowIfCancellationRequested();
        if (selected.Length == 0) return [];
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var admission = await AtprotoFederationReadAdmission.OpenGlobalAsync(
                context, enrollment, cancellationToken);
            var rows = await context.Set<AtprotoFederationTenantSetting>()
                .IgnoreTenantFilter(TenantFilterBypassReasons.AtprotoJetstreamGovernanceResolution)
                .AsNoTracking().Where(setting => selected.Contains(setting.SettingKey)).ToArrayAsync(cancellationToken);
            await admission.ValidateAsync(cancellationToken);
            return rows;
        });
    }
}
