using System.Data;
using System.Data.Common;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Database.ProviderPrimitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>
/// Original role-definition custody on SQLite's S write reservation or the existing
/// PostgreSQL original-login P capability. The latter requires its account-definition
/// vocabulary and attestation to be installed. No Global fallback exists.
/// </summary>
public sealed class AccountMembershipPolicySource : IAccountMembershipPolicySource
{
    private readonly ExploreDbContext context;
    private readonly DbContextId contextId;
    private readonly RelationalConnectionIdentity connection;
    private readonly Guid? tenant;
    private readonly HumanAccountMembershipDefinitionQueries definitions;

    public AccountMembershipPolicySource(ExploreDbContext context,
        HumanCommitSubject? subject = null, HumanAuthenticationLifetime? lifetime = null,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        HumanSourceProviderOperations.RequirePostgresOrSqlite(context, "account_membership_original_policy_source_provider_not_supported");
        if (context.Database.CurrentTransaction is not null || context.IsTenantFilterBypassed)
            throw new InvalidOperationException("Account policy preparation requires the original idle owner.");
        this.context = context;
        contextId = context.ContextId;
        connection = RelationalConnectionIdentity.Capture(context);
        tenant = context.BindTenantLease();
        if (tenant is null) throw new InvalidOperationException("Account membership requires a selected tenant owner.");
        definitions = new(context, connection, tenant, subject, lifetime, enrollment);
    }

    public Task<AccountMembershipPolicyFacts> ObserveAsync(AccountMembershipTarget target, CancellationToken cancellationToken)
    {
        RequireSelection();
        if (context.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null)
            throw new InvalidOperationException("Account policy observation must precede the transaction.");
        return ReadAsync(target, cancellationToken);
    }

    public async Task<IAccountMembershipPolicyLease> AcquireAsync(AccountMembershipTarget target, IPolicySourceSetLease sourceSet,
        CancellationToken cancellationToken)
    {
        var transaction = RequireOwner();
        await sourceSet.ValidateAsync(cancellationToken);
        var facts = await ReadAsync(target, cancellationToken);
        if (!ReferenceEquals(transaction, RequireOwner()))
            throw new InvalidOperationException("The account policy owner changed.");
        return new Lease(this, target, sourceSet, transaction, facts);
    }

    private void RequireSelection()
    {
        context.ValidateTenantLease();
        if (context.ContextId != contextId || context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
            || !connection.Matches(context))
            throw new InvalidOperationException("The original account policy selection changed.");
    }

    private DbTransaction RequireOwner()
    {
        RequireSelection();
        if (!context.HumanCommitAttemptOwnsTransaction || connection.State != ConnectionState.Open
            || context.Database.CurrentTransaction?.GetDbTransaction() is not { } transaction
            || transaction.IsolationLevel != IsolationLevel.Serializable
            || !connection.Owns(transaction))
            throw new InvalidOperationException("Account policy custody requires its non-replaying Serializable owner.");
        return transaction;
    }

    private async Task<AccountMembershipPolicyFacts> ReadAsync(AccountMembershipTarget target, CancellationToken token)
    {
        var (roles, permissions) = await definitions.ReadAsync(token);
        var targetFacts = await ReadTarget(target, token);
        RequireSelection();
        return new(roles, permissions, targetFacts);
    }

    private Task<AccountMembershipTargetFacts?> ReadTarget(AccountMembershipTarget target, CancellationToken token) =>
        target.Kind switch
        {
            AccountMembershipTargetKind.None when target.Id == Guid.Empty => Task.FromResult<AccountMembershipTargetFacts?>(null),
            AccountMembershipTargetKind.TenantGrant => context.TenantUserRoleGrants.AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(row => row.Id == target.Id && row.TenantId == tenant)
                .Select(row => new AccountMembershipTargetFacts(row.Id, row.TenantId, row.TenantUser.UserId,
                    row.TenantUserId, row.TenantId, row.RoleId, row.RoleScopeId, false,
                    row.TenantUser.IsDeleted, false, row.RevokedAt, null)).SingleOrDefaultAsync(token),
            AccountMembershipTargetKind.OrganizationMember => context.OrganizationMembers.AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(row => row.Id == target.Id && row.TenantId == tenant)
                .Select(row => new AccountMembershipTargetFacts(row.Id, row.TenantId, row.UserId,
                    row.OrganizationTenantId, row.OrganizationTenant.OrganizationId, row.RoleId, 0, row.IsDeleted,
                    row.OrganizationTenant.IsDeleted, row.OrganizationTenant.Organization.IsDeleted,
                    null, row.OrganizationPositionId)).SingleOrDefaultAsync(token),
            AccountMembershipTargetKind.GroupMember => context.GroupMembers.AsNoTracking()
                .IgnoreQueryFilters([QueryFilterNames.SoftDelete])
                .Where(row => row.Id == target.Id && row.TenantId == tenant)
                .Select(row => new AccountMembershipTargetFacts(row.Id, row.TenantId, row.UserId,
                    row.GroupTenantId, row.GroupTenant.GroupId, row.RoleId, 0, row.IsDeleted,
                    row.GroupTenant.IsDeleted, row.GroupTenant.Group.IsDeleted,
                    null, row.GroupPositionId)).SingleOrDefaultAsync(token),
            _ => throw new InvalidOperationException("Unknown original account source target.")
        };

    private sealed class Lease(AccountMembershipPolicySource source, AccountMembershipTarget target, IPolicySourceSetLease sourceSet,
        DbTransaction transaction, AccountMembershipPolicyFacts facts) : IAccountMembershipPolicyLease
    {
        public AccountMembershipPolicyFacts Facts { get; } = facts;
        public async Task<AccountMembershipPolicyFacts> ValidateAsync(CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("The original account policy lease ended.");
            await sourceSet.ValidateAsync(cancellationToken);
            var current = await source.ReadAsync(target, cancellationToken);
            if (!ReferenceEquals(transaction, source.RequireOwner()))
                throw new InvalidOperationException("The original account policy lease ended.");
            return current;
        }
    }
}
