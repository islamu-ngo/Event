using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// One private producer on the original scoped execution lease. SQL role possession supplies
/// facts, not this producer, request identity, credential observation or an action admission.
/// </summary>
internal sealed class HumanEmailSourceProtocol : IHumanEmailSourceProtocol
{
    private readonly ExploreDbContext _context;
    private readonly ExploreDbContextExecutionLease _selection;
    private readonly HumanAdministratorCommitScope _human;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly PostgresNativeQueueEnrollment? _enrollment;
    private Preparation? _preparation;
    private Lease? _active;
    private int _state;

    internal HumanEmailSourceProtocol(ExploreDbContext context, ExploreDbContextExecutionLease selection,
        HumanSupportForwardingObservation forwarding)
    {
        _context = context;
        _selection = selection;
        _human = selection.HumanScope ?? throw new UnauthorizedAccessException("human_email_original_subject_required");
        Subject = _human.Subject;
        Lifetime = selection.HumanLifetime ?? throw new UnauthorizedAccessException("human_email_original_credential_required");
        Forwarding = forwarding;
        Purpose = _human.Purpose switch
        {
            HumanAdministratorCommitPurpose.EmailInstance => HumanEmailSourcePurpose.Instance,
            HumanAdministratorCommitPurpose.EmailTenant => HumanEmailSourcePurpose.Tenant,
            _ => throw new InvalidOperationException("human_email_dedicated_purpose_required")
        };
        SelectedTenantId = selection.SelectedTenantId;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        _enrollment = _human.Enrollment;
        HumanEmailSourceQueries.RequireProvider(context, _enrollment);
        RequireSelection();
        RequireIdle();
    }

    public HumanCommitSubject Subject { get; }
    public HumanAuthenticationLifetime Lifetime { get; }
    public HumanSupportForwardingObservation Forwarding { get; }
    public HumanEmailSourcePurpose Purpose { get; }
    public Guid? SelectedTenantId { get; }

    public async Task<IHumanEmailSourcePreparation> PrepareAsync(
        bool includeSelectedRoute, CancellationToken cancellationToken = default)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("human_email_preparation_already_attempted");
        RequireSelection();
        RequireIdle();
        bool opened = _connection.State != ConnectionState.Open;
        try
        {
            if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
            var facts = await ReadAsync(null, includeSelectedRoute, cancellationToken);
            RequireIdle();
            _preparation = new(this, facts, includeSelectedRoute);
            return _preparation;
        }
        catch
        {
            _state = 4;
            throw;
        }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanEmailSourceLease> AcquireAsync(
        IHumanEmailSourcePreparation preparation, CancellationToken cancellationToken = default)
    {
        // The attempt is consumed before any blocking work, including a forged preparation.
        if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
            throw new InvalidOperationException("human_email_acquisition_already_attempted");
        try
        {
            if (preparation is not Preparation owned || !ReferenceEquals(owned, _preparation)
                || !ReferenceEquals(owned.Producer, this))
                throw new UnauthorizedAccessException("human_email_foreign_preparation");
            var transaction = RequireOwner();
            var facts = await ReadAsync(transaction, owned.IncludeSelectedRoute, cancellationToken);
            if (!ReferenceEquals(transaction, RequireOwner()))
                throw new InvalidOperationException("human_email_original_transaction_changed");
            _active = new(this, transaction, owned.IncludeSelectedRoute, facts);
            _state = 3;
            return _active;
        }
        catch
        {
            _state = 4;
            throw;
        }
    }

    public async Task<HumanEmailSourceFacts> RequireOwnedAsync(IHumanEmailSourceLease lease,
        HumanCommitSubject originalSubject, HumanAuthenticationLifetime originalLifetime,
        HumanEmailSourcePurpose purpose, Guid? selectedTenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_state != 3 || lease is not Lease owned || !ReferenceEquals(owned, _active)
                || !ReferenceEquals(owned.Producer, this)
                || !ReferenceEquals(originalSubject, Subject) || !ReferenceEquals(originalLifetime, Lifetime)
                || purpose != Purpose || selectedTenantId != SelectedTenantId
                || !ReferenceEquals(owned.Transaction, RequireOwner()))
                throw new UnauthorizedAccessException("human_email_foreign_or_expired_source_lease");
            var facts = await ReadAsync(owned.Transaction, owned.IncludeSelectedRoute, cancellationToken);
            if (!ReferenceEquals(owned.Transaction, RequireOwner()))
                throw new InvalidOperationException("human_email_original_transaction_changed");
            return facts;
        }
        catch
        {
            _state = 4;
            throw;
        }
    }

    private void RequireSelection()
    {
        _selection.RequireOriginalOwner(_context);
        if (_selection.PolicyPurpose != PolicyMutationPurpose.EmailDelivery
            || !ReferenceEquals(_selection.HumanScope, _human) || !ReferenceEquals(_selection.HumanLifetime, Lifetime)
            || !ReferenceEquals(_human.Subject, Subject) || !ReferenceEquals(Forwarding.Subject, Subject)
            || Subject.AccountKey is null || Subject.AccountKey != Lifetime.AccountKey
            || (Subject.AccountKey.ProviderKind == AuthenticationProviderKind.Local) != (Lifetime.LocalSession is not null)
            || Lifetime.LocalSession is { } local && (local.LocalSubjectId != Subject.PrincipalId
                || Subject.AccountKey.Value != local.LocalSubjectId.ToString("D"))
            || _context.ContextId != _contextLease || !_connection.Matches(_context)
            || _context.HumanCommitContextLease != _contextLease || !ReferenceEquals(_context.HumanCommitAuditSubject, Subject)
            || _context.TenantFilterTenantId != SelectedTenantId || _context.IsTenantFilterBypassed
            || _context.IsConfigurationInstanceImportOwner
            || (Purpose == HumanEmailSourcePurpose.Tenant) != SelectedTenantId.HasValue
            || SelectedTenantId == Guid.Empty || SelectedTenantId.HasValue && Forwarding.ResolvedTenantId != SelectedTenantId)
            throw new InvalidOperationException("human_email_original_selection_changed");
    }

    private void RequireIdle()
    {
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_email_preparation_requires_idle_owner");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_email_requires_original_serializable_owner");
        return transaction;
    }

    private Task<HumanEmailSourceFacts> ReadAsync(DbTransaction? transaction, bool route, CancellationToken ct)
    {
        RequireSelection();
        return HumanEmailSourceQueries.ReadAsync(_context, _connection, transaction, _enrollment,
            Subject, Lifetime, Forwarding, Purpose, SelectedTenantId, route, RequireSelection, ct);
    }

    private sealed record Preparation(HumanEmailSourceProtocol Producer, HumanEmailSourceFacts Facts,
        bool IncludeSelectedRoute) : IHumanEmailSourcePreparation;

    private sealed record Lease(HumanEmailSourceProtocol Producer, DbTransaction Transaction,
        bool IncludeSelectedRoute, HumanEmailSourceFacts Facts) : IHumanEmailSourceLease;
}
