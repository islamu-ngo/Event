using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>Commits finite native read purposes, never bearer tokens or protected case material.</summary>
public sealed class ModerationCaseReadGrantRepository(
    ExploreDbContext context, IModerationCaseTargetIssuer owner,
    ICurrentUserService caller, IMachinePrincipalAccessor machine) : IModerationCaseReadGrantRepository
{
    public Task<ModerationCaseReadGrant?> GrantAsync(
        ModerationCaseReadGrantRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsUuidV7(request.OperationId) || !IsUuidV7(request.CaseId)
            || !IsUuidV7(request.SourceId) || request.CallerUserId == Guid.Empty
            || request.ExpectedCaseRevision < 1 || request.ValidUntil == default
            || request.ValidUntil == DateTimeOffset.MaxValue
            || request.ValidUntil.UtcTicks % TimeSpan.TicksPerMicrosecond != 0)
            return Task.FromResult<ModerationCaseReadGrant?>(null);
        return CommitAsync(token => owner.GrantReadAsync(request, token),
            (grant, entity) => entity is ModerationCaseReadGrant row && row.Id == grant.Id,
            cancellationToken);
    }

    public Task<ModerationCaseReadGrant?> RevokeAsync(
        Guid grantId, Guid operationId, long expectedRevision, CancellationToken cancellationToken) =>
        IsUuidV7(grantId) && IsUuidV7(operationId) && expectedRevision == 1
            ? CommitAsync(token => owner.RevokeReadAsync(grantId, operationId, expectedRevision, token),
                (grant, entity) => entity switch
                {
                    ModerationCaseReadGrant row => row.Id == grant.Id,
                    ModerationCaseReadGrantRevocationReceipt row => row.Id == operationId
                        && row.GrantId == grant.Id && row.ExpectedRevision == expectedRevision,
                    _ => false
                }, cancellationToken)
            : Task.FromResult<ModerationCaseReadGrant?>(null);

    private async Task<ModerationCaseReadGrant?> CommitAsync(
        Func<CancellationToken, Task<ModerationCaseReadGrant?>> stage,
        Func<ModerationCaseReadGrant, object, bool> owned, CancellationToken token)
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null
            || context.GetService<IDbContextTransactionManager>() is ITransactionEnlistmentManager { EnlistedTransaction: not null }
            || context.ChangeTracker.Entries().Any() || context.IsTenantFilterBypassed
            || context.ModerationTrustDomainId is not null || !caller.IsAuthenticated || machine.Current is not null
            || caller.UserId is not { } userId || context.TenantFilterTenantId is not { } tenantId
            || userId == Guid.Empty || tenantId == Guid.Empty)
            return null;
        try
        {
            // An ambiguous commit retries the same immutable operation, never a new grant.
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                if (!CallerMatches()) return null;
                var result = await stage(token);
                if (result is null || context.ChangeTracker.Entries().Any(row => !owned(result, row.Entity))) return null;
                using var settlement = context.BindModerationReadGrantSettlement(result,
                    context.ChangeTracker.Entries<ModerationCaseReadGrantRevocationReceipt>().SingleOrDefault()?.Entity);
                var expected = (result.Id, result.TenantId, result.TrustDomainId, result.CaseId, result.CaseRevision,
                    result.SourceId, result.CallerUserId, result.IssuerUserId, result.IssuerRoleGrantId,
                    result.IssuerPlatformGrant, result.IssuedAt, result.ExpiresAt, result.RevokedAt, result.Revision);
                await context.SaveChangesAsync(token);
                if (!await owner.RecheckIssuanceAsync(token) || !CallerMatches()
                    || expected != (result.Id, result.TenantId, result.TrustDomainId, result.CaseId, result.CaseRevision,
                        result.SourceId, result.CallerUserId, result.IssuerUserId, result.IssuerRoleGrantId,
                        result.IssuerPlatformGrant, result.IssuedAt, result.ExpiresAt, result.RevokedAt, result.Revision)
                    || context.ChangeTracker.HasChanges()
                    || System.Transactions.Transaction.Current is not null)
                    return null;
                context.ValidateModerationReadGrantSettlement();
                await transaction.CommitAsync(token);
                return result;
            });
        }
        catch (ExploreDbContext.ModerationReadGrantSettlementException) { return null; }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbException) { return null; }
        catch (DbUpdateException exception) when (exception.InnerException is DbException or TimeoutException) { return null; }
        catch (TimeoutException) { return null; }
        catch (RetryLimitExceededException exception) when (exception.InnerException is DbException or TimeoutException) { return null; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }

        bool CallerMatches() => caller.IsAuthenticated && caller.UserId == userId && machine.Current is null
            && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == tenantId;
    }

    private static bool IsUuidV7(Guid id) => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11;
}
