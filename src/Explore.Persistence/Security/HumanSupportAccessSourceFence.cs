using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Captures one idle owner. Reads tracker-free facts and borrows its Serializable transaction.
/// No retries, provider decisions, clock samples, context creation or connection substitution.
/// </summary>
public sealed class HumanSupportAccessSourceFence : IHumanSupportAccessSourceFence
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly Guid? _tenant;
    private readonly HumanSupportForwardingObservation _forwarding;
    private readonly PolicySourceSetPurpose _purpose;
    private readonly HumanSupportAccessSourceQueries _queries;

    public HumanSupportAccessSourceFence(ExploreDbContext context, HumanCommitSubject subject,
        HumanSupportForwardingObservation forwarding, PolicySourceSetPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment = null, DbContext? selectedIdentityContext = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(forwarding);
        if (!ReferenceEquals(forwarding.Subject, subject) || !Enum.IsDefined(purpose)
            || !context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_support_requires_original_idle_owner");
        _tenant = context.BindTenantLease();
        if ((purpose == PolicySourceSetPurpose.RuntimeTenant) != _tenant.HasValue
            || _tenant.HasValue && forwarding.ResolvedTenantId != _tenant
            || (purpose == PolicySourceSetPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_support_purpose_mismatch");
        if (subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local
            && !ReferenceEquals(context, selectedIdentityContext))
            throw new NotSupportedException("human_support_external_identity_requires_original_owner_contract");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        _forwarding = forwarding;
        _purpose = purpose;
        _queries = new(context, _connection, _tenant, forwarding, purpose, enrollment);
    }

    public async Task<HumanSupportAccessSourceFacts> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_support_observation_requires_no_transaction");
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
        try { return await ReadAsync(null, cancellationToken); }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanSupportAccessSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
        IHumanPrincipalSourceLease principal, IHumanEventAssignmentSourceLease? assignments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        ArgumentNullException.ThrowIfNull(principal);
        DbTransaction transaction = RequireOwner();
        await sourceSet.ValidateAsync(cancellationToken);
        var human = await principal.ValidateAsync(cancellationToken);
        if (human.PrincipalId != _forwarding.Subject.PrincipalId || human.SelectedTenantId != _tenant)
            throw new InvalidOperationException("human_support_principal_selection_mismatch");
        if (assignments is not null)
        {
            var assignmentFacts = await assignments.ValidateAsync(cancellationToken);
            if (assignmentFacts.PrincipalId != human.PrincipalId)
                throw new InvalidOperationException("human_support_assignment_selection_mismatch");
        }
        await HumanSourceProviderOperations.LockAsync(_context, _queries, transaction, cancellationToken);
        var facts = await ReadAsync(transaction, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("human_support_owner_changed");
        return new Lease(this, sourceSet, transaction, facts);
    }

    private void RequireSelection()
    {
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextLease || _context.TenantFilterTenantId != _tenant
            || _context.IsTenantFilterBypassed || !_connection.Matches(_context)
            || (_purpose == PolicySourceSetPurpose.ConfigurationImport) != _context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_support_original_selection_changed");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_support_requires_original_serializable_owner");
        return transaction;
    }

    private Task<HumanSupportAccessSourceFacts> ReadAsync(DbTransaction? transaction, CancellationToken ct) =>
        _queries.ReadAsync(transaction, RequireSelection, ct);

    private sealed class Lease(HumanSupportAccessSourceFence source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, HumanSupportAccessSourceFacts facts) : IHumanSupportAccessSourceLease
    {
        public HumanSupportAccessSourceFacts Facts { get; } = facts;
        public async Task<HumanSupportAccessSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_support_lease_expired");
            await sourceSet.ValidateAsync(cancellationToken);
            var current = await source.ReadAsync(transaction, cancellationToken);
            await sourceSet.ValidateAsync(cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_support_lease_expired");
            return current;
        }
    }
}
