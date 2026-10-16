using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Borrows the original selected connection and context lease. PostgreSQL reads through a
/// separately attested fixed projection, never through a replacement Global context or table grant.
/// </summary>
public sealed class HumanAuthorizationRouteSource : IHumanAuthorizationRouteSource
{
    private readonly ExploreDbContext _context;
    private readonly PolicySourceSetPurpose _purpose;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly Guid? _tenant;
    private readonly HumanAuthorizationRouteQueries _queries;

    public HumanAuthorizationRouteSource(ExploreDbContext context, HumanCommitSubject subject,
        PolicySourceSetPurpose purpose, PostgresNativeQueueEnrollment? enrollment = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        if (subject.PrincipalId == Guid.Empty || subject.AccountKey is null)
            throw new InvalidOperationException("human_route_requires_resolved_provider_account");
        if (!Enum.IsDefined(purpose) || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("human_route_requires_selected_idle_owner");
        _tenant = context.BindTenantLease();
        if ((purpose == PolicySourceSetPurpose.RuntimeTenant) != _tenant.HasValue
            || (purpose == PolicySourceSetPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_route_purpose_mismatch");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _purpose = purpose;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        _queries = new(context, subject, purpose, enrollment, _connection, _tenant);
    }

    public async Task<HumanAuthorizationRouteFacts> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_route_observation_requires_no_transaction");
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var facts = await _queries.ReadAsync(null, cancellationToken);
            RequireSelection();
            if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
                throw new InvalidOperationException("human_route_observation_transaction_changed");
            return facts;
        }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanAuthorizationRouteLease> AcquireAsync(
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        DbTransaction transaction = RequireOwner();
        // As with the principal source port, the parent supplies THIS owner's preceding source-set lease.
        await sourceSet.ValidateAsync(cancellationToken);
        var facts = await _queries.ReadAsync(transaction, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("human_route_original_transaction_changed");
        await sourceSet.ValidateAsync(cancellationToken);
        return new Lease(this, sourceSet, transaction, facts);
    }

    private void RequireSelection()
    {
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextLease || _context.IsTenantFilterBypassed
            || _context.TenantFilterTenantId != _tenant
            || (_purpose == PolicySourceSetPurpose.ConfigurationImport) != _context.IsConfigurationInstanceImportOwner
            || !_connection.Matches(_context))
            throw new InvalidOperationException("human_route_original_selection_changed");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_route_requires_original_serializable_owner");
        return transaction;
    }

    private sealed class Lease(HumanAuthorizationRouteSource source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, HumanAuthorizationRouteFacts facts) : IHumanAuthorizationRouteLease
    {
        public HumanAuthorizationRouteFacts Facts { get; } = facts;

        public async Task<HumanAuthorizationRouteFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_route_original_transaction_changed");
            await sourceSet.ValidateAsync(cancellationToken);
            var current = await source._queries.ReadAsync(transaction, cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_route_original_transaction_changed");
            await sourceSet.ValidateAsync(cancellationToken);
            return current;
        }
    }
}
