using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Fixed principal reads on one preselected context. Never resolves a Global factory, provider,
/// ambient subject, independent transaction, or authorization decision.
/// </summary>
public sealed class HumanPrincipalSourceFence : IHumanPrincipalSourceFence
{
    private readonly ExploreDbContext _context;
    private readonly HumanCommitSubject _subject;
    private readonly PolicySourceSetPurpose _purpose;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly Guid? _tenant;
    private readonly HumanPrincipalSourceQueries _queries;
    private readonly LocalIdentityExecutionLease? _selectedExecution;
    private readonly LocalSessionAuthority? _selectedLocalSession;

    internal HumanPrincipalSourceFence(LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        LocalSessionAuthority? localSession = null)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(subject);
        _context = execution.Application ?? throw new UnauthorizedAccessException("The selected application owner is required.");
        var identity = execution.Identity ?? throw new UnauthorizedAccessException("The selected identity owner is required.");
        execution.RequireCurrent(_context, identity);
        if (!_context.Database.IsRelational() || _context.Database.CurrentTransaction is not null
            || identity.Database.CurrentTransaction is not null
            || _context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_principal_requires_selected_idle_owner");
        bool localBinding = subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local;
        if (localBinding != (localSession is not null))
            throw new InvalidOperationException("human_principal_local_session_observation_required");
        if (localSession is not null && (localSession.LocalSubjectId != subject.PrincipalId
            || subject.AccountKey!.Value != localSession.LocalSubjectId.ToString("D")))
            throw new InvalidOperationException("human_principal_local_binding_mismatch");
        _subject = subject;
        _tenant = _context.TenantFilterTenantId;
        _purpose = _tenant.HasValue ? PolicySourceSetPurpose.RuntimeTenant : PolicySourceSetPurpose.Global;
        _connection = RelationalConnectionIdentity.Capture(_context);
        _contextLease = _context.ContextId;
        _selectedExecution = execution;
        _selectedLocalSession = localSession;
        _queries = new(_context, subject, _purpose, localSession, null, _connection, _tenant, execution);
        RequireSelection();
    }

    public HumanPrincipalSourceFence(
        ExploreDbContext context, HumanCommitSubject subject, PolicySourceSetPurpose purpose,
        LocalSessionAuthority? localSession = null, DbContext? selectedIdentityContext = null,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        if (!Enum.IsDefined(purpose) || !context.Database.IsRelational()
            || context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("human_principal_requires_selected_idle_owner");
        _tenant = context.BindTenantLease();
        if ((purpose == PolicySourceSetPurpose.RuntimeTenant) != _tenant.HasValue
            || (purpose == PolicySourceSetPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_principal_purpose_mismatch");
        bool localBinding = subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local;
        if (localBinding != (localSession is not null))
            throw new InvalidOperationException("human_principal_local_session_observation_required");
        if (localSession is not null)
        {
            if (!ReferenceEquals(selectedIdentityContext, context))
                throw new NotSupportedException("human_principal_external_identity_requires_original_owner_source_contract");
            if (localSession.LocalSubjectId != subject.PrincipalId
                || subject.AccountKey!.Value != localSession.LocalSubjectId.ToString("D"))
                throw new InvalidOperationException("human_principal_local_binding_mismatch");
        }
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _subject = subject;
        _purpose = purpose;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        _queries = new(context, subject, purpose, localSession, enrollment, _connection, _tenant);
    }

    public async Task<HumanPrincipalSourceFacts> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || _selectedExecution?.Identity?.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("human_principal_preparation_requires_no_transaction");
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
        try { return await ReadAsync(null, cancellationToken); }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanPrincipalSourceLease> AcquireAsync(
        IPolicySourceSetLease sourceSet, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        DbTransaction transaction = RequireOwner();
        if (_selectedExecution is not null)
            PolicySourceSetFence.RequireSelectedLease(sourceSet, _selectedExecution, _context, transaction);
        // The parent must supply the lease from THIS selected owner's source-set port, acquired FIRST.
        await sourceSet.ValidateAsync(cancellationToken);
        if (_selectedExecution is null)
            await HumanSourceProviderOperations.LockAsync(_context, _queries, transaction, cancellationToken);
        HumanPrincipalSourceFacts facts = await ReadAsync(transaction, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("human_principal_transaction_changed");
        if (_selectedExecution is null && _context.HasHumanCommitContextLease
            && _tenant is { } tenantId && facts.Tenants.Any(tenant => tenant.Id == tenantId))
            HumanSourceProviderOperations.RetainNativeTenantOwner(_context, _subject, tenantId);
        return new Lease(this, sourceSet, transaction, facts);
    }

    private void RequireSelection()
    {
        if (_selectedExecution is not null)
        {
            var identity = _selectedExecution.Identity
                ?? throw new UnauthorizedAccessException("The selected identity owner is required.");
            _selectedExecution.RequireCurrent(_context, identity);
            HumanSecuritySourceGate.RequireCurrent(_context);
            if (_selectedLocalSession is not null)
                LocalIdentityLifecycleGateCustody.RequireHeld(identity);
        }
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextLease || _context.IsTenantFilterBypassed
            || _context.TenantFilterTenantId != _tenant
            || (_purpose == PolicySourceSetPurpose.ConfigurationImport) != _context.IsConfigurationInstanceImportOwner
            || !_connection.Matches(_context))
            throw new InvalidOperationException("human_principal_original_selection_changed");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (_connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_principal_requires_original_serializable_owner");
        return transaction;
    }

    private Task<HumanPrincipalSourceFacts> ReadAsync(DbTransaction? transaction, CancellationToken ct) =>
        _queries.ReadAsync(transaction, RequireSelection, ct);

    private sealed class Lease(HumanPrincipalSourceFence source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, HumanPrincipalSourceFacts facts) : IHumanPrincipalSourceLease
    {
        public HumanPrincipalSourceFacts Facts { get; } = facts;
        public async Task<HumanPrincipalSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_principal_lease_expired");
            if (source._selectedExecution is not null)
                PolicySourceSetFence.RequireSelectedLease(sourceSet, source._selectedExecution, source._context, transaction);
            await sourceSet.ValidateAsync(cancellationToken);
            var current = await source.ReadAsync(transaction, cancellationToken);
            await sourceSet.ValidateAsync(cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("human_principal_lease_expired");
            return current;
        }
    }
}
