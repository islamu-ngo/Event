using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Tracker-free W sources belonging to one original context, human, tenant and transaction.</summary>
public sealed class HumanEventProviderSourceFence : IHumanEventProviderSourceFence
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextId;
    private readonly HumanCommitSubject _subject;
    private readonly HumanEventProviderSourceTarget _target;
    private readonly IRegistrationProviderRegistry _registry;
    private readonly HumanEventProviderSourceQueries _queries;
    private HumanEventProviderSourceFacts? _observed;
    private HumanEventProviderSourceWriteGuard? _writeGuard;
    private bool _acquired;

    public HumanEventProviderSourceFence(ExploreDbContext context, HumanCommitSubject subject,
        HumanEventProviderSourceTarget target, IRegistrationProviderRegistry registry, PostgresNativeQueueEnrollment? enrollment = null,
        DbContext? selectedIdentityContext = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(registry);
        if (!context.Database.IsRelational() || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.BindTenantLease() != target.TenantId
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_event_provider_requires_original_idle_tenant_owner");
        if (subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local
            && !ReferenceEquals(context, selectedIdentityContext))
            throw new NotSupportedException("human_event_provider_external_identity_requires_original_owner_contract");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextId = context.ContextId;
        _subject = subject;
        _target = target;
        _registry = registry;
        _queries = new(context, _connection, subject, target, enrollment);
    }

    public async Task<HumanEventProviderSourceFacts> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireIdle();
        if (_observed is not null)
            throw new InvalidOperationException("human_event_provider_observation_already_captured");
        return _observed = await ReadOutsideOwnerAsync(cancellationToken);
    }

    public async Task ValidateObservationAsync(HumanEventProviderSourceFacts expected, CancellationToken cancellationToken = default)
    {
        RequireExpected(expected);
        RequireIdle();
        HumanEventProviderSourceTransitions.RequireUnchanged(expected, await ReadOutsideOwnerAsync(cancellationToken));
    }

    private async Task<HumanEventProviderSourceFacts> ReadOutsideOwnerAsync(CancellationToken ct)
    {
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(ct);
        try
        {
            var facts = await ReadAsync(null, ct);
            RequireIdle();
            return facts;
        }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanEventProviderSourceLease> AcquireAsync(HumanEventProviderSourceFacts expected,
        IPolicySourceSetLease sourceSet, IHumanPrincipalSourceLease principal,
        IHumanAuthorizationRouteLease route, IHumanEventAssignmentSourceLease assignments,
        IHumanSupportAccessSourceLease support, CancellationToken cancellationToken = default)
    {
        RequireExpected(expected);
        if (_acquired) throw new InvalidOperationException("human_event_provider_source_already_acquired");
        _acquired = true;
        var transaction = RequireOwner();
        await RequireAuthorityAsync(sourceSet, principal, route, assignments, support, cancellationToken);
        RequireSameOwner(transaction);
        await HumanSourceProviderOperations.LockAsync(_context, _queries, transaction, cancellationToken);
        var facts = await ReadAsync(transaction, cancellationToken);
        HumanEventProviderSourceTransitions.RequireUnchanged(expected, facts);
        HumanEventProviderSourceTransitions.RequireTarget(facts);
        await sourceSet.ValidateAsync(cancellationToken);
        RequireSameOwner(transaction);
        _writeGuard = new HumanEventProviderSourceWriteGuard(_context, transaction, facts);
        _writeGuard.Attach();
        return new Lease(this, transaction, facts, sourceSet, principal, route, assignments, support);
    }

    internal void ReleaseScopedWriteGuard() => _writeGuard?.Detach();

    private async Task RequireAuthorityAsync(IPolicySourceSetLease sourceSet, IHumanPrincipalSourceLease principal,
        IHumanAuthorizationRouteLease route, IHumanEventAssignmentSourceLease assignments,
        IHumanSupportAccessSourceLease support, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(support);
        await sourceSet.ValidateAsync(ct);
        var p = await principal.ValidateAsync(ct);
        var r = await route.ValidateAsync(ct);
        var a = await assignments.ValidateAsync(ct);
        var h = await support.ValidateAsync(ct);
        if (p.PrincipalId != _subject.PrincipalId || p.SelectedTenantId != _target.TenantId
            || r.PrincipalId != _subject.PrincipalId || r.SelectedTenantId != _target.TenantId
            || a.PrincipalId != _subject.PrincipalId || a.TenantId != _target.TenantId
            || !a.Events.Any(row => row.Id == _target.EventId)
            || !ReferenceEquals(h.Forwarding.Subject, _subject) || h.Forwarding.ResolvedTenantId != _target.TenantId)
            throw new UnauthorizedAccessException("human_event_provider_original_authority_selection_changed");
    }

    private void RequireExpected(HumanEventProviderSourceFacts expected)
    {
        if (!ReferenceEquals(expected, _observed) || !ReferenceEquals(expected.Target, _target))
            throw new InvalidOperationException("human_event_provider_observation_custody_changed");
    }

    private void RequireSelection()
    {
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextId || _context.TenantFilterTenantId != _target.TenantId
            || _context.IsTenantFilterBypassed || _context.IsConfigurationInstanceImportOwner
            || !_connection.Matches(_context))
            throw new InvalidOperationException("human_event_provider_selected_context_changed");
    }

    private void RequireIdle()
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_event_provider_observation_requires_idle_owner");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_event_provider_requires_original_serializable_owner");
        return transaction;
    }

    private void RequireSameOwner(DbTransaction transaction)
    {
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("human_event_provider_original_transaction_changed");
    }

    private Task<HumanEventProviderSourceFacts> ReadAsync(DbTransaction? transaction, CancellationToken ct, bool final = false) =>
        _queries.ReadAsync(transaction, ct, final, _observed, () => _writeGuard?.CreatedRows ?? [], RequireSelection);

    private sealed class Lease(HumanEventProviderSourceFence source, DbTransaction transaction,
        HumanEventProviderSourceFacts facts, IPolicySourceSetLease sourceSet, IHumanPrincipalSourceLease principal,
        IHumanAuthorizationRouteLease route, IHumanEventAssignmentSourceLease assignments,
        IHumanSupportAccessSourceLease support) : IHumanEventProviderSourceLease
    {
        public HumanEventProviderSourceFacts Facts { get; } = facts;
        public async Task<HumanEventProviderSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            source.RequireSameOwner(transaction);
            await source.RequireAuthorityAsync(sourceSet, principal, route, assignments, support, cancellationToken);
            await HumanSourceProviderOperations.LockAsync(source._context, source._queries, transaction, cancellationToken);
            var fresh = await source.ReadAsync(transaction, cancellationToken, final: true);
            HumanEventProviderSourceTransitions.RequirePermittedTransition(Facts, fresh, source._registry);
            await sourceSet.ValidateAsync(cancellationToken);
            source.RequireSameOwner(transaction);
            return fresh;
        }
    }
}
