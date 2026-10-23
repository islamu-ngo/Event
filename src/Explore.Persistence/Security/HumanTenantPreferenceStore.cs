using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

internal sealed class HumanTenantPreferenceStore(
    ExploreDbContext context,
    ExploreDbContextExecutionLease lease,
    IHumanTenantPreferencePreparation preparation,
    IServiceProvider services) : IHumanTenantPreferenceStore, IDisposable
{
    private IHumanTenantPreferenceMutation? _completed;
    private int _used;
    private RelationalConnectionIdentity? _connection;
    private IDbContextTransaction? _transaction;
    private DbContextId _contextId;
    private int? _backend;
    private bool _interrupted;

    public async Task<bool> WriteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        RequireOwner(mutation);
        if (Interlocked.Exchange(ref _used, 1) != 0)
            throw new InvalidOperationException("A preference writer permits one original effect.");
        _contextId = context.ContextId;
        _transaction = context.Database.CurrentTransaction;
        if (context.Database.IsRelational())
        {
            _connection = RelationalConnectionIdentity.Capture(context);
            _backend = _connection.BackendProcessId;
            _connection.StateChange += ConnectionChanged;
        }
        void RequireOriginal() { token.ThrowIfCancellationRequested(); RequireOriginalOwner(mutation); }
        RequireOriginal();
        await mutation.RequireCurrentAsync(token);
        RequireOriginal();
        bool written = await HumanTenantPreferenceDataOperations.WriteAsync(context, services, mutation, RequireOriginal, token);
        if (!written) return false;
        _completed = mutation;
        await RequireCompleteAsync(mutation, token);
        return true;
    }

    public async Task RequireCompleteAsync(IHumanTenantPreferenceMutation mutation, CancellationToken token)
    {
        RequireOwner(mutation);
        if (!ReferenceEquals(_completed, mutation))
            throw new UnauthorizedAccessException("The original preference effect is incomplete.");
        void RequireOriginal() { token.ThrowIfCancellationRequested(); RequireOriginalOwner(mutation); }
        bool matches = await HumanTenantPreferenceDataOperations.MatchesAsync(context, services, mutation, RequireOriginal, token);
        if (!matches) throw new UnauthorizedAccessException("The original preference image changed before settlement.");
    }

    private void RequireOwner(IHumanTenantPreferenceMutation mutation)
    {
        context.RequireBoundTenantLease();
        lease.RequireOriginalOwner(context);
        var human = lease.HumanScope
            ?? throw new UnauthorizedAccessException("Preference data requires the original human scope.");
        if (!ReferenceEquals(preparation.CurrentMutation, mutation)
            || human.Subject.PrincipalId != mutation.SubjectId
            || human.Purpose != HumanAdministratorCommitPurpose.GlobalInstance
            || lease.SelectedTenantId is not null || lease.PolicyPurpose != PolicyMutationPurpose.Standard
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.ChangeTracker.HasChanges())
            throw new UnauthorizedAccessException("Preference data requires the original prepared Serializable owner.");
    }

    private void RequireOriginalOwner(IHumanTenantPreferenceMutation mutation)
    {
        RequireOwner(mutation);
        if (context.ContextId != _contextId || !ReferenceEquals(_transaction, context.Database.CurrentTransaction)
            || _interrupted || _connection is not null && (!_connection.Matches(context)
                || _connection.State != ConnectionState.Open || _connection.BackendProcessId != _backend))
            throw new UnauthorizedAccessException("The original preference connection or transaction changed.");
    }

    private void ConnectionChanged(object? sender, StateChangeEventArgs change)
    {
        if (change.CurrentState != ConnectionState.Open) _interrupted = true;
    }

    public void Dispose()
    {
        if (_connection is not null) _connection.StateChange -= ConnectionChanged;
    }
}
