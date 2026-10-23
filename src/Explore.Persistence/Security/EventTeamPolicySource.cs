using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Borrows only the selected Runtime owner. SQLite's S reservation covers these reads;
/// PostgreSQL requires the extended, attested assignment source vocabulary, never scalar grants.
/// </summary>
public sealed class EventTeamPolicySource : IEventTeamPolicySource
{
    private readonly ExploreDbContext context;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly Guid tenant;
    private readonly Guid principal;
    private readonly PostgresNativeQueueEnrollment? enrollment;

    public EventTeamPolicySource(ExploreDbContext context, HumanCommitSubject subject,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "event_team_original_source_provider_not_supported");
        if (context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Event team sources require their original idle Runtime owner.");
        this.context = context;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        tenant = context.BindTenantLease()
            ?? throw new InvalidOperationException("Event team sources require the selected tenant.");
        principal = subject.PrincipalId;
        this.enrollment = enrollment;
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
    }

    public Task<EventTeamPolicyFacts> ObserveAsync(EventTeamSourceTarget target, CancellationToken token)
    {
        RequireSelection();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Original team observation precedes its transaction.");
        return ReadAsync(target, token);
    }

    public async Task<IEventTeamPolicyLease> AcquireAsync(EventTeamSourceTarget target,
        IPolicySourceSetLease sources, CancellationToken token)
    {
        var owner = RequireOwner();
        await sources.ValidateAsync(token);
        var facts = await ReadAsync(target, token);
        if (!ReferenceEquals(owner, RequireOwner()))
            throw new InvalidOperationException("Event team source owner changed.");
        return new Lease(this, target, sources, owner, facts);
    }

    private void RequireSelection()
    {
        context.ValidateTenantLease();
        if (context.ContextId != contextId || context.TenantFilterTenantId != tenant
            || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || !connection.Matches(context))
            throw new InvalidOperationException("Original Event team source selection changed.");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (!context.HumanCommitAttemptOwnsTransaction || System.Transactions.Transaction.Current is not null
            || connection.State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !connection.Owns(transaction))
            throw new InvalidOperationException("Event team custody requires its non-replaying Serializable owner.");
        return transaction;
    }

    private async Task<EventTeamPolicyFacts> ReadAsync(EventTeamSourceTarget target, CancellationToken token)
    {
        if (target.EventId == Guid.Empty || target.UserId is not null && target.Email is not null)
            throw new InvalidOperationException("Invalid closed Event team target.");
        var facts = await HumanEventTeamDefinitionQueries.ReadAsync(
            context, connection, enrollment, principal, tenant, target, token);
        // Ordinary target lookup retains UserRepository's Pii.Email equality and
        // soft-delete filtering. It does not infer a human credential from email.
        Guid? resolved = target.Email is { } email
            ? await context.Users.AsNoTracking().Where(row => row.Pii != null && row.Pii.Email == email)
                .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token)
            : target.UserId is { } user
                ? await context.Users.AsNoTracking().Where(row => row.Id == user)
                    .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token)
                : null;
        RequireSelection();
        return facts with { ResolvedUserId = resolved };
    }

    private sealed class Lease(EventTeamPolicySource source, EventTeamSourceTarget target,
        IPolicySourceSetLease sources, DbTransaction owner, EventTeamPolicyFacts facts) : IEventTeamPolicyLease
    {
        public EventTeamPolicyFacts Facts { get; } = facts;
        public void RequireOwner()
        {
            if (!ReferenceEquals(owner, source.RequireOwner()))
                throw new InvalidOperationException("The original Event team lease ended.");
        }
        public async Task<EventTeamPolicyFacts> ValidateAsync(CancellationToken token)
        {
            RequireOwner();
            await sources.ValidateAsync(token);
            var current = await source.ReadAsync(target, token);
            if (!ReferenceEquals(owner, source.RequireOwner()))
                throw new InvalidOperationException("The original Event team lease ended.");
            return current;
        }
    }
}
