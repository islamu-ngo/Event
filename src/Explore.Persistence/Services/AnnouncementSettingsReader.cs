using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Services;

public sealed class AnnouncementSettingsReader(
    ExploreDbContext context, IServiceProvider services, TimeProvider clock) : IAnnouncementSettingsReader
{
    private const string PublicationKey = GovernanceSettingKeys.PublicExperience.AnnouncementBarPublication;
    private const string MarkerKey = GovernanceSettingKeys.PublicExperiencePreferences.AnnouncementBarDismissedEdition;

    public async Task<(SystemSetting? Instance, TenantSetting? Tenant)> ReadPublicAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A public announcement requires an actual tenant.", nameof(tenantId));
        var rows = await ReadAsync(tenantId, null, cancellationToken);
        return (rows.Instance, rows.Tenant);
    }

    public async Task<(Guid TenantId, SystemSetting? Instance, TenantSetting? Tenant, UserPreference? Marker)> ReadSelfAsync(
        CancellationToken cancellationToken)
    {
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        Guid tenant = lease.SelectedTenantId
            ?? throw new UnauthorizedAccessException("A self announcement read requires its selected tenant.");
        Guid user = RequireHuman(tenant);
        var rows = await ReadAsync(tenant, user, cancellationToken);
        if (RequireHuman(tenant) != user)
            throw new UnauthorizedAccessException("The announcement read subject changed.");
        return (tenant, rows.Instance, rows.Tenant, rows.Marker);
    }

    public async Task<SystemSetting?> ReadInstanceSourceAsync(CancellationToken cancellationToken)
    {
        Guid user = RequireHuman(null);
        var rows = await ReadAsync(null, null, cancellationToken);
        if (RequireHuman(null) != user)
            throw new UnauthorizedAccessException("The announcement editor subject changed.");
        return rows.Instance;
    }

    public async Task<TenantSetting?> ReadTenantSourceAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Guid user = RequireHuman(tenantId);
        var rows = await ReadAsync(tenantId, null, cancellationToken);
        if (RequireHuman(tenantId) != user)
            throw new UnauthorizedAccessException("The announcement editor subject changed.");
        return rows.Tenant;
    }

    public async Task<TenantSetting?> ReadManagedTenantSourceAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Guid user = RequireHuman(tenantId);
        var rows = await ReadAsync(tenantId, null, cancellationToken);
        if (RequireHuman(tenantId) != user)
            throw new UnauthorizedAccessException("The instance-managed announcement subject changed.");
        return rows.Tenant;
    }

    private Guid RequireHuman(Guid? tenant)
    {
        var lease = services.GetRequiredService<ExploreDbContextExecutionLease>();
        lease.RequireOriginalOwner(context);
        if (tenant == Guid.Empty || lease.SelectedTenantId != tenant
            || lease.HumanScope?.Subject is not { } subject || lease.HumanLifetime is not { } lifetime)
            throw new UnauthorizedAccessException("An announcement source read requires its original human selection.");
        lease.RequireOriginalCredential(services.GetRequiredService<IHttpContextAccessor>().HttpContext, lifetime);
        if (!lifetime.IsValidAt(clock.GetUtcNow()))
            throw new UnauthorizedAccessException("The announcement read credential expired.");
        return subject.PrincipalId;
    }

    private async Task<(SystemSetting? Instance, TenantSetting? Tenant, UserPreference? Marker)> ReadAsync(
        Guid? tenant, Guid? user, CancellationToken token)
    {
        context.ValidateTenantLease();
        if (context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("Announcement reads cannot retarget or borrow another persistence owner.");
        if (context.Database.IsNpgsql())
        {
            var image = await PostgresOrdinarySettingsContract.ReadAnnouncementImageAsync(context,
                services.GetRequiredService<PostgresNativeQueueEnrollment>(), tenant, user,
                user.HasValue ? [PublicationKey, MarkerKey] : [PublicationKey], token);
            return (image.System[PublicationKey]?.Entity(), image.Tenant[PublicationKey]?.Entity(),
                user.HasValue ? image.User[MarkerKey]?.Entity() : null);
        }

        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
        if (context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel is not IsolationLevel.Serializable)
            throw new UnauthorizedAccessException("Announcement sources require one consistent read transaction.");
        var system = await context.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == PublicationKey, token);
        var selected = tenant.HasValue ? await context.Set<TenantSetting>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenant && row.SettingKey == PublicationKey, token) : null;
        var marker = user.HasValue ? await context.UserPreferences.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TenantId == tenant && row.UserId == user && row.SettingKey == MarkerKey, token) : null;
        if (owned is not null)
            await owned.CommitAsync(token);
        return (system, selected, marker);
    }
}
