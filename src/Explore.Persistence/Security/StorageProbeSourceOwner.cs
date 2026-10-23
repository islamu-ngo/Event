using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Persistence.Security;

/// <summary>Source-only transactions on one retained selection, never a remote-operation transaction.</summary>
internal sealed class StorageProbeSourceOwner(
    ExploreDbContext context, ExploreDbContextExecutionLease selection,
    ISerializableAttemptOwner attempts, IHumanActionCommitFinalizer finalizer,
    IConfiguration configuration) : IStorageProbeSourceOwner, IDisposable
{
    private Action? requireSelection;
    private DbTransaction? active;
    private RelationalConnectionIdentity? nativeConnection;
    private bool released;
    private PostgresNativeQueueEnrollment? enrollment;
    private long? generation;

    public async Task CaptureAsync(Guid? tenantId, CancellationToken token)
    {
        selection.RequireOriginalOwner(context);
        if (requireSelection is not null || selection.PolicyPurpose != PolicyMutationPurpose.Standard
            || selection.SelectedTenantId != tenantId || context.IsConfigurationInstanceImportOwner
            || context.IsTenantFilterBypassed || attempts is not EfCoreUnitOfWork)
            throw new UnauthorizedAccessException("Storage probe requires its original ordinary source selection.");
        // SQLite has a physical writer reservation. PostgreSQL requires this probe's
        // independently enrolled source exclusion in addition to human S/P/H custody.
        StorageProbeSourceProviderSession.RequireSupported(context);
        var contextId = context.ContextId;
        var connection = RelationalConnectionIdentity.Capture(context);
        int? backend = null;
        var subject = selection.HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("The original probe subject is absent.");
        requireSelection = () =>
        {
            selection.RequireOriginalOwner(context);
            if (context.ContextId != contextId || !connection.Matches(context)
                || !ReferenceEquals(selection.HumanScope?.Subject, subject)
                || selection.SelectedTenantId != tenantId || context.TenantFilterTenantId != tenantId
                || selection.PolicyPurpose != PolicyMutationPurpose.Standard
                || released || backend.HasValue && (connection.State != ConnectionState.Open
                    || connection.BackendProcessId != backend.Value)
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner)
                throw new UnauthorizedAccessException("The original probe source selection changed.");
        };
        RequireIdle();
        generation = await StorageProbeSourceProviderSession.ObserveAsync(context, configuration,
            connection, processId =>
        {
            // One initial open is retained through all source attempts and remote work.
            // A later close/reopen is irreversible loss, even if pooling returns the same PID.
            backend = processId;
            nativeConnection = connection;
            nativeConnection.StateChange += ConnectionChanged;
        }, bound => enrollment = bound, token);
        if (enrollment is not null) RequireIdle();
    }

    private void ConnectionChanged(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState is ConnectionState.Closed or ConnectionState.Broken) released = true;
    }

    public void Dispose()
    {
        released = true;
        if (nativeConnection is not null) nativeConnection.StateChange -= ConnectionChanged;
    }

    public void RequireIdle()
    {
        (requireSelection ?? throw new UnauthorizedAccessException("Probe source custody was not captured."))();
        if (active is not null || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("Remote storage work cannot hold a source transaction.");
        RequireNoEntityWrites();
    }

    public async Task CheckAsync(Func<CancellationToken, Task> sources, Action time, CancellationToken token)
    {
        RequireIdle();
        try
        {
            await attempts.ExecuteAsync(async ct =>
            {
                active = context.Database.CurrentTransaction?.GetDbTransaction();
                RequireActive();
                await sources(ct);
                await RequireNativeSourcesAsync(ct);
                RequireActive();
                finalizer.Register(async finalToken =>
                {
                    RequireActive();
                    await sources(finalToken);
                    await RequireNativeSourcesAsync(finalToken);
                    RequireActive();
                }, _ => { RequireActive(); time(); });
                time();
                return true;
            }, token);
        }
        finally { active = null; }
        RequireIdle();
        time();
    }

    private async Task RequireNativeSourcesAsync(CancellationToken token)
    {
        if (enrollment is null) return;
        RequireActive();
        long current = await PostgresStorageProbeSourceContract.AcquireAsync(context, enrollment, token);
        RequireActive();
        if (generation != current)
            throw new UnauthorizedAccessException("The original storage probe source generation changed.");
    }

    private void RequireActive()
    {
        requireSelection!();
        RequireNoEntityWrites();
        if (active is null || !context.HumanCommitAttemptOwnsTransaction
            || active.IsolationLevel != IsolationLevel.Serializable
            || !ReferenceEquals(active, context.Database.CurrentTransaction?.GetDbTransaction())
            || !RelationalConnectionIdentity.Capture(context).Owns(active))
            throw new UnauthorizedAccessException("Probe sources require the selected original physical owner.");
    }

    private void RequireNoEntityWrites()
    {
        if (context.ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            throw new UnauthorizedAccessException("A storage probe source attempt cannot carry entity writes.");
    }
}

public static class StorageProbeSourceOwnerRegistration
{
    public static IServiceCollection AddStorageProbeSourceOwner(this IServiceCollection services)
    {
        services.TryAddScoped<IStorageProbeSourceOwner, StorageProbeSourceOwner>();
        return services;
    }
}
