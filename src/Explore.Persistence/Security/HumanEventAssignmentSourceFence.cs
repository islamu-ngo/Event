using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Captures the original source selection before the transaction. Uses persisted, tracker-free
/// facts only; physical locks last until the borrowed transaction ends. Exceptions propagate
/// to whole-attempt rollback/reprepare, never an independent retry.
/// </summary>
public sealed class HumanEventAssignmentSourceFence : IHumanEventAssignmentSourceFence
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly Guid? _selectedTenant;
    private readonly PolicySourceSetPurpose _purpose;
    private readonly HumanEventAssignmentSourceQueries _queries;

    public HumanEventAssignmentSourceFence(ExploreDbContext context, HumanCommitSubject subject,
        PolicySourceSetPurpose purpose, Guid tenantId, IReadOnlyCollection<Guid> eventIds,
        PostgresNativeQueueEnrollment? enrollment = null, DbContext? selectedIdentityContext = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(eventIds);
        if (!Enum.IsDefined(purpose) || !context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_assignment_requires_selected_idle_owner");
        if (tenantId == Guid.Empty || eventIds.Count > HumanEventAssignmentSourceFacts.MaximumEvents
            || eventIds.Any(id => id == Guid.Empty))
            throw new ArgumentException("human_assignment_targets_invalid", nameof(eventIds));
        _selectedTenant = context.BindTenantLease();
        if ((purpose == PolicySourceSetPurpose.RuntimeTenant) != _selectedTenant.HasValue
            || _selectedTenant.HasValue && _selectedTenant != tenantId
            || (purpose == PolicySourceSetPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_assignment_purpose_mismatch");
        if (subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local
            && !ReferenceEquals(selectedIdentityContext, context))
            throw new NotSupportedException("human_assignment_external_identity_requires_original_owner_source_contract");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        var events = eventIds.Distinct().ToImmutableArray();
        _purpose = purpose;
        _queries = new(context, _connection, tenantId, subject.PrincipalId, events, purpose, enrollment);
    }

    public async Task<HumanEventAssignmentSourceFacts> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_assignment_preparation_requires_no_transaction");
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
        try { return await ReadAsync(null, cancellationToken); }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanEventAssignmentSourceLease> AcquireAsync(
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        DbTransaction transaction = RequireOwner();
        // This is the actual selected owner's lease, not a permission or a substitute lease.
        await sourceSet.ValidateAsync(cancellationToken);
        await HumanSourceProviderOperations.LockAsync(_context, _queries, transaction, cancellationToken);
        var facts = await ReadAsync(transaction, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("human_assignment_transaction_changed");
        return new Lease(this, sourceSet, transaction, facts);
    }

    private void RequireSelection()
    {
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextLease || _context.TenantFilterTenantId != _selectedTenant
            || _context.IsTenantFilterBypassed
            || (_purpose == PolicySourceSetPurpose.ConfigurationImport) != _context.IsConfigurationInstanceImportOwner
            || !_connection.Matches(_context))
            throw new InvalidOperationException("human_assignment_original_selection_changed");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_assignment_requires_original_serializable_owner");
        return transaction;
    }

    private Task<HumanEventAssignmentSourceFacts> ReadAsync(DbTransaction? transaction, CancellationToken ct) =>
        _queries.ReadAsync(transaction, RequireSelection, ct);

    private sealed class Lease(HumanEventAssignmentSourceFence source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, HumanEventAssignmentSourceFacts facts) : IHumanEventAssignmentSourceLease
    {
        public HumanEventAssignmentSourceFacts Facts { get; } = facts;

        public async Task<HumanEventAssignmentSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_assignment_lease_expired");
            await sourceSet.ValidateAsync(cancellationToken);
            var current = await source.ReadAsync(transaction, cancellationToken);
            await sourceSet.ValidateAsync(cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_assignment_lease_expired");
            return current;
        }
    }
}
