using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Explore.Application.Features.SupportAccess.Requests.Commands;
using Explore.Application.Features.SupportAccess.Requests.Queries;

namespace Explore.Persistence.Security;

/// <summary>
/// SQLite target custody under the acquired S/H write reservation. PostgreSQL uses
/// the separate closed DATA adapter; these ordinary table reads never serve native Runtime.
/// </summary>
internal sealed class SupportAdministrationSource : ISupportAdministrationSource
{
    private readonly ExploreDbContext context;
    private readonly ExploreDbContextExecutionLease execution;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly Guid? tenant;
    private readonly HumanCommitSubject subject;
    private object? selectedRequest;

    public void Select(StartSupportAccessSessionCommand request) => SelectRequest(request);
    public void Select(StopSupportAccessSessionCommand request) => SelectRequest(request);
    public void Select(ForceStopSupportAccessSessionCommand request) => SelectRequest(request);
    public void Select(ListSupportAccessSessionsQuery request) => SelectRequest(request);
    public void Select(GetSupportAccessAuditEventsQuery request) => SelectRequest(request);

    private void SelectRequest(object request)
    {
        RequireSelection();
        if (selectedRequest is not null)
            throw new InvalidOperationException("Support selection cannot replay.");
        selectedRequest = request;
    }

    public SupportAdministrationSource(ExploreDbContext context, ExploreDbContextExecutionLease execution)
    {
        execution.RequireOriginalOwner(context);
        if (!context.Database.IsSqlite())
            throw new NotSupportedException("support_administration_sqlite_owner_required");
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || execution.PolicyPurpose is not null)
            throw new InvalidOperationException("Support administration requires its ordinary idle selected owner.");
        subject = execution.HumanScope?.Subject
            ?? throw new UnauthorizedAccessException("Support administration requires the original human.");
        if (execution.HumanLifetime?.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("Support administration requires original credential custody.");
        this.context = context;
        this.execution = execution;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        tenant = context.BindTenantLease();
    }

    public Task<SupportAdministrationSessionFact?> ObserveAsync(Guid? sessionId, CancellationToken cancellationToken)
    {
        RequireSelection();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Support target observation must precede the physical attempt.");
        return ReadSession(sessionId, cancellationToken);
    }

    public async Task<ISupportAdministrationLease> AcquireAsync(Guid? sessionId, IPolicySourceSetLease sourceSet,
        CancellationToken cancellationToken)
    {
        var transaction = RequireOwner();
        await sourceSet.ValidateAsync(cancellationToken);
        var original = await ReadSession(sessionId, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("The support target owner changed.");
        return new Lease(this, sourceSet, transaction, original);
    }

    private void RequireSelection()
    {
        execution.RequireOriginalOwner(context);
        context.ValidateTenantLease();
        if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || execution.PolicyPurpose is not null || !ReferenceEquals(execution.HumanScope?.Subject, subject)
            || !connection.Matches(context))
            throw new InvalidOperationException("Original support physical selection changed.");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (!context.HumanCommitAttemptOwnsTransaction || System.Transactions.Transaction.Current is not null
            || connection.State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !connection.Owns(transaction))
            throw new InvalidOperationException("Support custody requires its non-replaying Serializable owner.");
        return transaction;
    }

    private async Task<SupportAdministrationSessionFact?> ReadSession(Guid? id, CancellationToken token)
    {
        if (id is null) return null;
        var row = await context.SupportAccessSessions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        RequireSelection();
        return row is null ? null : SupportAdministrationSessionFact.From(row);
    }

    private sealed class Lease(SupportAdministrationSource source, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, SupportAdministrationSessionFact? original) : ISupportAdministrationLease
    {
        public SupportAdministrationSessionFact? Original { get; } = original;
        public ISupportAdministrationData? NativeData => null;

        public async Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("The support target lease ended.");
            await sourceSet.ValidateAsync(cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("The support target lease ended.");
        }

        public async Task<SupportAdministrationPersistedFacts> ValidateAsync(Guid? sessionId, Guid? auditId,
            CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("The support target lease ended.");
            await sourceSet.ValidateAsync(cancellationToken);
            var session = await source.ReadSession(sessionId, cancellationToken);
            var audit = auditId is null ? null : await source.context.SupportAccessAuditEvents.AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == auditId, cancellationToken);
            await sourceSet.ValidateAsync(cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("The support target lease ended.");
            return new(session, audit is null ? null : SupportAdministrationAuditFact.From(audit));
        }
    }
}
