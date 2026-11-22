using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>Owns an independent instance read; never promotes the requesting Runtime connection.</summary>
public sealed class InstanceSystemSettingsReader(
    IGlobalExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null) : IInstanceSystemSettingsReader
{
    public async Task<IReadOnlyList<SystemSetting>> ReadAsync(
        IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        string[] selectedKeys = keys.Distinct(StringComparer.Ordinal).ToArray();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("instance_settings_original_global_required");
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        var connection = RelationalConnectionIdentity.Capture(context);
        await context.Database.OpenConnectionAsync(cancellationToken);
        int? backend = connection.BackendProcessId;
        bool interrupted = false;
        void Changed(object? sender, System.Data.StateChangeEventArgs change)
        {
            if (change.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null
                || interrupted || !connection.Matches(context)
                || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("instance_settings_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            IReadOnlyList<SystemSetting> observed;
            if (context.Database.IsNpgsql())
            {
                if (enrollment is null)
                    throw new UnauthorizedAccessException("instance_settings_read_enrollment_required");
                var image = await PostgresOrdinarySettingsContract.ReadHierarchyAsync(
                    context, enrollment, null, selectedKeys, cancellationToken);
                RequireOriginal();
                await PostgresOrdinarySettingsContract.RequireReadyAsync(context, enrollment, cancellationToken);
                observed = image.System.Where(row => selectedKeys.Contains(row.SettingKey, StringComparer.Ordinal)).ToArray();
            }
            else
            {
                observed = await context.SystemSettings.AsNoTracking()
                    .Where(row => selectedKeys.Contains(row.SettingKey)).ToArrayAsync(cancellationToken);
            }
            RequireOriginal();
            return observed;
        }
        finally
        {
            connection.StateChange -= Changed;
            if (context.ContextId == contextId && !interrupted && connection.Matches(context)
                && connection.State == System.Data.ConnectionState.Open && connection.BackendProcessId == backend)
                await context.Database.CloseConnectionAsync();
        }
    }
}
