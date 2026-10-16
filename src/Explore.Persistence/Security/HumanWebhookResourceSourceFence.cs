using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>One captured context/connection/tenant/human; no new owner, independent retry or provider I/O.</summary>
public sealed class HumanWebhookResourceSourceFence : IHumanWebhookResourceSourceFence
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextLease;
    private readonly Guid? _tenant;
    private readonly HumanCommitSubject _subject;
    private readonly PolicySourceSetPurpose _purpose;
    private readonly HumanWebhookResourceSourceQueries _queries;

    public HumanWebhookResourceSourceFence(ExploreDbContext context, HumanCommitSubject subject,
        HumanWebhookResourceTarget target, PolicySourceSetPurpose purpose,
        PostgresNativeQueueEnrollment? enrollment = null, DbContext? selectedIdentityContext = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);
        if (!context.Database.IsRelational() || context.IsTenantFilterBypassed
            || purpose is not (PolicySourceSetPurpose.RuntimeTenant or PolicySourceSetPurpose.Global or PolicySourceSetPurpose.ConfigurationImport)
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_webhook_requires_original_idle_owner");
        _tenant = context.BindTenantLease();
        if ((purpose == PolicySourceSetPurpose.RuntimeTenant) != _tenant.HasValue
            || (purpose == PolicySourceSetPurpose.ConfigurationImport) != context.IsConfigurationInstanceImportOwner
            || target.RequestedTenantId.HasValue && target.RequestedTenantId != _tenant)
            throw new InvalidOperationException("human_webhook_purpose_or_tenant_mismatch");
        if (subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local
            && !ReferenceEquals(context, selectedIdentityContext))
            throw new NotSupportedException("human_webhook_external_identity_requires_original_owner_contract");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextLease = context.ContextId;
        _subject = subject;
        _purpose = purpose;
        _queries = new(context, _connection, _tenant, subject, target, purpose, enrollment);
    }

    public async Task<HumanWebhookResourceSourceFacts> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireIdle();
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var facts = await ReadAsync(null, cancellationToken);
            RequireIdle();
            return facts;
        }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanWebhookResourceSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
        IHumanPrincipalSourceLease principal, IHumanSupportAccessSourceLease support,
        IHumanEventAssignmentSourceLease? assignments = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(support);
        var transaction = RequireOwner();
        await sourceSet.ValidateAsync(cancellationToken);
        var human = await principal.ValidateAsync(cancellationToken);
        if (human.PrincipalId != _subject.PrincipalId || human.SelectedTenantId != _tenant)
            throw new InvalidOperationException("human_webhook_principal_selection_mismatch");
        if (assignments is not null)
        {
            var assignmentFacts = await assignments.ValidateAsync(cancellationToken);
            if (assignmentFacts.PrincipalId != _subject.PrincipalId || assignmentFacts.TenantId != _tenant)
                throw new InvalidOperationException("human_webhook_assignment_selection_mismatch");
        }
        var supportFacts = await support.ValidateAsync(cancellationToken);
        if (!ReferenceEquals(supportFacts.Forwarding.Subject, _subject)
            || _tenant.HasValue && supportFacts.Forwarding.ResolvedTenantId != _tenant)
            throw new InvalidOperationException("human_webhook_support_selection_mismatch");
        RequireSameOwner(transaction);
        await HumanSourceProviderOperations.LockAsync(_context, _queries, transaction, cancellationToken);
        var facts = await ReadAsync(transaction, cancellationToken);
        await sourceSet.ValidateAsync(cancellationToken);
        RequireSameOwner(transaction);
        return new Lease(this, sourceSet, transaction, facts);
    }

    private void RequireSelection()
    {
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextLease || _context.TenantFilterTenantId != _tenant
            || _context.IsTenantFilterBypassed || !_connection.Matches(_context)
            || (_purpose == PolicySourceSetPurpose.ConfigurationImport) != _context.IsConfigurationInstanceImportOwner)
            throw new InvalidOperationException("human_webhook_original_selection_changed");
    }

    private void RequireIdle()
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("human_webhook_observation_requires_no_transaction");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !_connection.Owns(transaction))
            throw new InvalidOperationException("human_webhook_requires_original_serializable_owner");
        return transaction;
    }

    private void RequireSameOwner(DbTransaction transaction)
    {
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("human_webhook_original_transaction_changed");
    }

    private Task<HumanWebhookResourceSourceFacts> ReadAsync(DbTransaction? transaction, CancellationToken ct) =>
        _queries.ReadAsync(transaction, RequireSelection, ct);

    private sealed class Lease(HumanWebhookResourceSourceFence source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, HumanWebhookResourceSourceFacts facts) : IHumanWebhookResourceSourceLease
    {
        public HumanWebhookResourceSourceFacts Facts { get; } = facts;
        public async Task<HumanWebhookResourceSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            source.RequireSameOwner(transaction);
            await sourceSet.ValidateAsync(cancellationToken);
            var fresh = await source.ReadAsync(transaction, cancellationToken);
            await sourceSet.ValidateAsync(cancellationToken);
            source.RequireSameOwner(transaction);
            return fresh;
        }
    }
}
