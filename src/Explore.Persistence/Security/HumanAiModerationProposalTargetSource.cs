using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// One exact cross-tenant proposal target on the original Runtime owner.
/// No target tenant argument, tracker reads, query-filter bypass, or secondary data connection.
/// </summary>
public sealed class HumanAiModerationProposalTargetSource : IHumanAiModerationProposalTargetSource
{
    private readonly ExploreDbContext _context;
    private readonly RelationalConnectionIdentity _connection;
    private readonly DbContextId _contextId;
    private readonly Guid _selectedTenant;
    private readonly Guid _human;
    private readonly HumanAiModerationProposalTargetQueries _queries;

    public HumanAiModerationProposalTargetSource(ExploreDbContext context, HumanCommitSubject subject, Guid eventId,
        PostgresNativeQueueEnrollment? enrollment = null, DbContext? selectedIdentityContext = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(subject);
        if (!context.Database.IsRelational() || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
            || context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null
            || context.BindTenantLease() is not { } selected || selected == Guid.Empty || eventId == Guid.Empty)
            throw new InvalidOperationException("ai_moderation_target_requires_original_idle_runtime_owner");
        if (subject.AccountKey?.ProviderKind == AuthenticationProviderKind.Local && !ReferenceEquals(context, selectedIdentityContext))
            throw new NotSupportedException("ai_moderation_target_requires_colocated_local_identity");
        HumanSourceProviderOperations.RequireEnrollment(context, enrollment);
        _context = context;
        _connection = RelationalConnectionIdentity.Capture(context);
        _contextId = context.ContextId;
        _selectedTenant = selected;
        _human = subject.PrincipalId;
        _queries = new(context, _connection, selected, subject.PrincipalId, eventId, enrollment);
    }

    public async Task<HumanEventAssignmentSourceFacts?> ObserveAsync(CancellationToken cancellationToken = default)
    {
        RequireSelection();
        if (_context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("ai_moderation_target_observation_requires_no_transaction");
        bool opened = _connection.State != ConnectionState.Open;
        if (opened) await _context.Database.OpenConnectionAsync(cancellationToken);
        try { return await ReadAsync(null, cancellationToken); }
        finally { if (opened) await _context.Database.CloseConnectionAsync(); }
    }

    public async Task<IHumanEventAssignmentSourceLease> AcquireAsync(IPolicySourceSetLease sourceSet,
        IHumanPrincipalSourceLease principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceSet);
        ArgumentNullException.ThrowIfNull(principal);
        var owner = RequireOwner();
        await sourceSet.ValidateAsync(cancellationToken);
        RequirePrincipal(await principal.ValidateAsync(cancellationToken));
        await HumanSourceProviderOperations.LockAsync(_context, _queries, owner, cancellationToken);
        var facts = await ReadAsync(owner, cancellationToken)
            ?? throw new UnauthorizedAccessException("The original moderation target is unavailable.");
        if (!ReferenceEquals(owner, RequireOwner()))
            throw new UnauthorizedAccessException("The original moderation target owner changed.");
        return new Lease(this, sourceSet, principal, owner, facts);
    }

    private void RequirePrincipal(HumanPrincipalSourceFacts facts)
    {
        if (facts.PrincipalId != _human || facts.SelectedTenantId != _selectedTenant
            || facts.Users is not [var user] || user.Id != _human || user.IsDeleted
            || !facts.PlatformGrants.Any(grant => facts.Roles.Any(role => role.Id == grant.RoleId
                && role.RoleScopeId == (int)RoleScopeEnum.Platform && role.MatchesPlatformAdministratorCode)))
            throw new UnauthorizedAccessException("A current original human instance administrator is required.");
    }

    private void RequireSelection()
    {
        _context.ValidateTenantLease();
        if (_context.ContextId != _contextId || _context.TenantFilterTenantId != _selectedTenant
            || _context.IsTenantFilterBypassed || _context.IsConfigurationInstanceImportOwner
            || !_connection.Matches(_context))
            throw new InvalidOperationException("ai_moderation_target_original_selection_changed");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (System.Transactions.Transaction.Current is not null || _connection.State != ConnectionState.Open
            || _context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable || !_connection.Owns(transaction))
            throw new InvalidOperationException("ai_moderation_target_requires_original_serializable_owner");
        return transaction;
    }

    private Task<HumanEventAssignmentSourceFacts?> ReadAsync(DbTransaction? owner, CancellationToken token) =>
        _queries.ReadAsync(owner, RequireSelection, token);

    private sealed class Lease(HumanAiModerationProposalTargetSource source, IPolicySourceSetLease exclusion,
        IHumanPrincipalSourceLease principal, DbTransaction owner, HumanEventAssignmentSourceFacts facts)
        : IHumanEventAssignmentSourceLease
    {
        public HumanEventAssignmentSourceFacts Facts { get; } = facts;
        public async Task<HumanEventAssignmentSourceFacts> ValidateAsync(CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(owner, source.RequireOwner()))
                throw new UnauthorizedAccessException("The moderation target source owner expired.");
            await exclusion.ValidateAsync(cancellationToken);
            source.RequirePrincipal(await principal.ValidateAsync(cancellationToken));
            var current = await source.ReadAsync(owner, cancellationToken)
                ?? throw new UnauthorizedAccessException("The moderation target is no longer available.");
            await exclusion.ValidateAsync(cancellationToken);
            if (!ReferenceEquals(owner, source.RequireOwner()))
                throw new UnauthorizedAccessException("The moderation target source owner expired.");
            return current;
        }
    }
}
