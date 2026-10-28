using System.Data;
using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Atomically commits native qualification and the separate subject-owned case. No reporter
/// acknowledgement, finding, sanction, notice or external effect is created by attribution.
/// </summary>
public sealed class ModerationCaseAttributionRepository(
    ExploreDbContext context, IModerationCaseAttributionOwner owner,
    ICurrentUserService caller, IMachinePrincipalAccessor machine) : IModerationCaseAttributionRepository
{
    public Task<ModerationCase?> AttributeAsync(
        ModerationCaseAttributionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsUuidV7(request.OperationId) || !IsUuidV7(request.OccurrenceCaseId)
            || !IsUuidV7(request.SubjectId) || !IsUuidV7(request.ReadGrantId))
            return Task.FromResult<ModerationCase?>(null);
        var caseId = Guid.CreateVersion7();
        return CommitAsync(token => owner.StageAttributionAsync(request, caseId, token),
            (result, entity) => entity switch
            {
                ModerationCase row => row.Id == result.Id,
                ModerationCaseAttributionSource row => row.Id == result.AttributionSourceId && row.CaseId == result.Id,
                ModerationCaseAttributionHead row => row.CaseId == result.Id
                    && row.OpeningSourceId == result.AttributionSourceId && row.SourceId == result.AttributionSourceId
                    && row.Generation == 1 && row.Revision == 1,
                _ => false
            }, cancellationToken);
    }

    public Task<ModerationCaseAttributionSource?> RenewAsync(
        ModerationCaseAttributionRenewalRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Qualification);
        var qualification = request.Qualification;
        if (!IsUuidV7(request.CaseId) || !IsUuidV7(request.ExpectedSourceId)
            || !IsUuidV7(qualification.OperationId) || !IsUuidV7(qualification.OccurrenceCaseId)
            || !IsUuidV7(qualification.ReadGrantId) || !IsUuidV7(qualification.SubjectId)
            || request.ExpectedCaseRevision < 1 || request.ExpectedGeneration < 1
            || request.ExpectedGeneration == long.MaxValue || request.ExpectedHeadRevision != request.ExpectedGeneration)
            return Task.FromResult<ModerationCaseAttributionSource?>(null);
        return CommitAsync(token => owner.StageAttributionRenewalAsync(request, token),
            (result, entity) => entity switch
            {
                ModerationCaseAttributionSource row => row.Id == result.Id && row.CaseId == request.CaseId
                    && row.Generation == request.ExpectedGeneration + 1,
                ModerationCaseAttributionHead row => row.CaseId == request.CaseId && row.SourceId == result.Id
                    && row.Generation == result.Generation && row.Revision == request.ExpectedHeadRevision + 1,
                ModerationCaseAttributionRenewalReceipt row => row.Id == result.Id && row.CaseId == request.CaseId
                    && row.ExpectedCaseRevision == request.ExpectedCaseRevision
                    && row.ExpectedSourceId == request.ExpectedSourceId
                    && row.ExpectedGeneration == request.ExpectedGeneration
                    && row.ExpectedHeadRevision == request.ExpectedHeadRevision,
                _ => false
            }, cancellationToken);
    }

    public Task<ModerationCaseAttributionSource?> WithdrawAsync(
        Guid sourceId, Guid operationId, long expectedRevision, CancellationToken cancellationToken) =>
        IsUuidV7(sourceId) && IsUuidV7(operationId) && expectedRevision == 1
            ? CommitAsync(token => owner.StageAttributionWithdrawalAsync(sourceId, operationId, expectedRevision, token),
                (result, entity) => entity is ModerationCaseAttributionSource row && row.Id == result.Id, cancellationToken)
            : Task.FromResult<ModerationCaseAttributionSource?>(null);

    private async Task<TEntity?> CommitAsync<TEntity>(
        Func<CancellationToken, Task<TEntity?>> stage, Func<TEntity, object, bool> owned, CancellationToken token) where TEntity : class
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
            // A provider retry reacquires native evidence before matching the immutable source.
            // A committed operation is returned exactly; an expired/withdrawn source is never
            // renewed to recover an ambiguous acknowledgement.
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                context.ModerationTrustDomainId = null;
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                if (!CallerMatches()) return null;
                var result = await stage(token);
                if (result is null || context.ChangeTracker.Entries().Any(row => !owned(result, row.Entity))) return null;
                await context.SaveChangesAsync(token);
                if (!await owner.RecheckAttributionAsync(token) || !CallerMatches()
                    || context.ChangeTracker.HasChanges() || context.ChangeTracker.Entries().Any(row => !owned(result, row.Entity))
                    || System.Transactions.Transaction.Current is not null)
                    return null;
                await transaction.CommitAsync(token);
                return result;
            });
        }
        catch (DbUpdateConcurrencyException) { return null; }
        catch (DbException) { return null; }
        catch (DbUpdateException exception) when (exception.InnerException is DbException) { return null; }
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
