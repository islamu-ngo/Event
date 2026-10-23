using System.Data;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database;

/// <summary>Current import source reads retain the live owner and its restricted native purpose.</summary>
internal sealed class ConfigurationImportHumanSourceReader(ExploreDbContext context)
    : IHumanConfigurationImportSourceReader
{
    public Task<ConfigurationImportSession?> ReadSessionAsync(
        Guid sessionId, ConfigurationImportTarget target, CancellationToken cancellationToken)
    {
        RequireOwner(target);
        if (context.Database.IsNpgsql() && context.IsConfigurationInstanceImportOwner)
            return PostgresConfigurationImportStateContract.ReadCurrentSessionAsync(context, sessionId, cancellationToken);
        string storageKey = target.GetSessionStorageKey();
        return context.Set<ConfigurationImportSession>().AsNoTracking().SingleOrDefaultAsync(
            row => row.SessionId == sessionId && row.TargetAuthorityKey == storageKey, cancellationToken);
    }

    public Task<ConfigurationManagedApplySchedule?> ReadScheduleAsync(
        Guid scheduleId, ConfigurationImportTarget target, CancellationToken cancellationToken)
    {
        RequireOwner(target);
        return context.Set<ConfigurationManagedApplySchedule>().AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == scheduleId && row.TargetAuthorityKey == target.AuthorityKey, cancellationToken);
    }

    private void RequireOwner(ConfigurationImportTarget target)
    {
        context.ValidateTenantLease();
        if (!context.HasHumanCommitContextLease || context.IsTenantFilterBypassed
            || context.TenantFilterTenantId != target.TenantId
            || context.IsConfigurationInstanceImportOwner != (target.Scope == ConfigurationImportScope.Instance)
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !RelationalConnectionIdentity.Capture(context).Owns(transaction))
            throw new InvalidOperationException("Import source reads require the original selected Serializable owner.");
    }
}
