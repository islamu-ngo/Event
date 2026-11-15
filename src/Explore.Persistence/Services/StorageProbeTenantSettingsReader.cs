using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>Retains an independent Runtime read without changing the probe's source owner.</summary>
public sealed class StorageProbeTenantSettingsReader(
    ITenantExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null) : IStorageProbeTenantSettingsReader
{
    public async Task<IReadOnlyList<TenantSetting>> ReadAsync(
        Guid tenantId, IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        string[] selectedKeys = keys.Distinct(StringComparer.Ordinal).ToArray();
        await using var context = await factory.CreateDbContextAsync(tenantId, cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        var connection = RelationalConnectionIdentity.Capture(context);
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change)
        {
            if (change.CurrentState != ConnectionState.Open) interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || tenantId == Guid.Empty || context.TenantFilterTenantId != tenantId
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || context.Database.CurrentTransaction is not null
                || System.Transactions.Transaction.Current is not null
                || interrupted || !connection.Matches(context))
                throw new UnauthorizedAccessException("The original probe transport settings owner changed.");
        }
        RequireOriginal();
        await context.Database.OpenConnectionAsync(cancellationToken);
        int? backend = connection.BackendProcessId;
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            IReadOnlyList<TenantSetting> observed;
            if (context.Database.IsNpgsql())
            {
                if (enrollment is null)
                    throw new UnauthorizedAccessException("Probe transport settings enrollment is required.");
                var image = await PostgresOrdinarySettingsContract.ReadHierarchyAsync(
                    context, enrollment, tenantId, selectedKeys, cancellationToken);
                observed = image.Tenant;
            }
            else
                observed = await context.Set<TenantSetting>().AsNoTracking()
                    .Where(row => row.TenantId == tenantId && selectedKeys.Contains(row.SettingKey))
                    .ToArrayAsync(cancellationToken);
            RequireOriginal();
            if (connection.State != ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("The original probe transport settings connection changed.");
            return observed;
        }
        finally
        {
            connection.StateChange -= Changed;
            if (context.ContextId == contextId && !interrupted && connection.Matches(context)
                && connection.State == ConnectionState.Open && connection.BackendProcessId == backend)
                await context.Database.CloseConnectionAsync();
        }
    }
}
