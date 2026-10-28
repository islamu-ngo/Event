using System.Data;
using System.Data.Common;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Moderation;

/// <summary>
/// The primary native intake transaction. An accepted history pointer is not current permission
/// to disclose a target or case; that requires the separately qualified ReadAllegation path.
/// </summary>
public sealed class ModerationCaseIntakeCommitter(
    ExploreDbContext context, IModerationCaseIntakeRepository intake,
    IModerationCaseTargetIssuer issuer,
    ICurrentUserService currentUser, IMachinePrincipalAccessor machine, TimeProvider clock,
    IModerationCaseNarrativeCustody narratives, IAdminContext admin)
    : IModerationCaseIntakeCommitter
{
    public async Task<ModerationCaseIssuedReceipt?> IssueSubmissionReceiptAsync(
        ModerationCaseTargetIssueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OccurrenceMaterial.Length is < 1 or > 65536 || request.Material.Length is < 1 or > 65536)
            return null;
        var ownedOccurrence = request.OccurrenceMaterial.ToArray();
        var ownedMaterial = request.Material.ToArray();
        request = request with { OccurrenceMaterial = ownedOccurrence, Material = ownedMaterial };
        try
        {
            return await CommitReceiptAsync(token => issuer.IssueSubmissionAsync(request, token),
                readPurpose: false, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ownedOccurrence);
            CryptographicOperations.ZeroMemory(ownedMaterial);
        }
    }

    public Task<ModerationCaseIssuedReceipt?> IssueReadReceiptAsync(
        Guid readGrantId, CancellationToken cancellationToken) =>
        IsUuidV7(readGrantId)
            ? CommitReceiptAsync(token => issuer.IssueReadAsync(readGrantId, token), readPurpose: true, cancellationToken)
            : Task.FromResult<ModerationCaseIssuedReceipt?>(null);

    private async Task<ModerationCaseIssuedReceipt?> CommitReceiptAsync(
        Func<CancellationToken, Task<ModerationCaseIssuedReceipt?>> mint, bool readPurpose,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || System.Transactions.Transaction.Current is not null
            || context.GetService<IDbContextTransactionManager>() is ITransactionEnlistmentManager { EnlistedTransaction: not null }
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is not null
            || context.ChangeTracker.Entries().Any() || !currentUser.IsAuthenticated || machine.Current is not null
            || currentUser.UserId is not { } callerId || callerId == Guid.Empty
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return null;

        ModerationCaseIssuedReceipt? staged = null;
        var published = false;
        try
        {
            // Like the native unit of work, open the manual transaction inside the provider
            // strategy envelope. Unlike idempotent intake, a minted bearer cannot be replayed
            // or recovered through verifySucceeded after an ambiguous commit.
            var committed = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                try
                {
                    await using var transaction = await context.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable, cancellationToken);
                    if (!CallerUnchanged(tenantId, callerId)) return false;
                    staged = await mint(cancellationToken);
                    if (staged is null || staged.Receipt.TenantId != tenantId
                        || staged.Receipt.CallerUserId != callerId
                        || (staged.Receipt.RelationshipKind == Explore.Domain.ModerationCaseRelationshipKind.CaseReadGrant) != readPurpose
                        || HasForeignIssuanceWork(staged))
                        return false;
                    await context.SaveChangesAsync(cancellationToken);
                    await context.FlushDisclosureAsync(cancellationToken);
                    if (!await issuer.RecheckIssuanceAsync(cancellationToken)
                        || !CallerUnchanged(tenantId, callerId)
                        || HasForeignIssuanceWork(staged) || context.ChangeTracker.HasChanges()
                        || System.Transactions.Transaction.Current is not null
                        || clock.GetUtcNow() < staged.Receipt.IssuedAt
                        || clock.GetUtcNow() >= staged.Receipt.ExpiresAt
                        || staged.Receipt.ExpiresAt == DateTimeOffset.MaxValue)
                        return false;
                    await transaction.CommitAsync(cancellationToken);
                    return true;
                }
                // Settle database failures inside the envelope so the provider cannot retry
                // this non-idempotent mint. In particular, a lost commit acknowledgement
                // must not release a token even if the receipt actually reached storage.
                catch (DbUpdateConcurrencyException) { return false; }
                catch (DbException) { return false; }
                catch (DbUpdateException exception) when (exception.InnerException is DbException or TimeoutException)
                {
                    return false;
                }
                catch (TimeoutException) { return false; }
            });
            if (!committed) return null;
            published = true;
            return staged;
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
            if (!published && staged is not null)
                CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(staged.Token).Span);
        }
    }

    public async Task<ModerationCaseIntakeCommitResult> SubmitAsync(
        ModerationCaseAccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Purpose != ModerationCaseAccessPurpose.SubmitAllegation
            || request.CaseId != Guid.Empty || !IsUuidV7(request.OperationId) || !IsUuidV7(request.ReceiptId)
            || request.ExpectedCaseRevision != 0 || request.PresentedToken.Length != 32)
            throw new ArgumentException("Submission requires exact native intake intent and opaque token coordinates.");
        if (!context.Database.IsRelational() || context.Database.CurrentTransaction is not null
            || context.IsTenantFilterBypassed || context.ModerationTrustDomainId is not null
            || System.Transactions.Transaction.Current is not null
            || context.GetService<IDbContextTransactionManager>() is ITransactionEnlistmentManager { EnlistedTransaction: not null }
            || context.ChangeTracker.Entries().Any() || !currentUser.IsAuthenticated || machine.Current is not null
            || currentUser.UserId is not { } callerId || callerId == Guid.Empty
            || context.TenantFilterTenantId is not { } tenantId || tenantId == Guid.Empty)
            return new(ModerationCaseIntakeCommitOutcome.Unavailable);

        var ownedToken = request.PresentedToken.ToArray();
        var ownedNarrative = request.Narrative.ToArray();
        // Only this local owned buffer reaches the protector. Native receipt/proof caches
        // retain payload-free request coordinates, not another copy of the statement.
        request = request with { PresentedToken = ownedToken, Narrative = default };
        var acknowledgementId = Guid.CreateVersion7();
        var digest = HMACSHA256.HashData(ownedToken, Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"moderation.case.intake.v3|{(int)request.Purpose}|{request.OperationId:N}|{request.ReceiptId:N}|{Convert.ToHexString(SHA256.HashData(ownedNarrative))}")));
        Guid? committedAcknowledgementId = null;
        var budgets = new ModerationOperationalBudgetStore(context, admin, machine, clock);
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(
                operation: async token =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    // Discovery selects only the historical-read path. New intake must leave
                    // target-specific human/source fence ordering to StageAsync.
                    if (await context.ModerationCaseIntakeAcknowledgements
                            .IgnoreQueryFilters(["ModerationAuthority"]).AsNoTracking().AnyAsync(
                                row => row.TenantId == tenantId && row.CallerUserId == callerId
                                    && row.OperationId == request.OperationId, token))
                    {
                        if (!await AcquireCallerAsync(tenantId, callerId, token))
                            throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                        var original = await ReadOriginalAsync(request, callerId, digest, token);
                        if (original is null)
                            throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Conflict));
                        if (!CallerUnchanged(tenantId, callerId))
                            throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                        committedAcknowledgementId = original.Id;
                        return new ModerationCaseIntakeCommitResult(ModerationCaseIntakeCommitOutcome.Accepted, original);
                    }
                    if (ownedNarrative.Length is < 1 or > 65536 || !await narratives.AcquireAsync(request.ReceiptId, token))
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    if (!await budgets.AcquireAsync(request.ReceiptId, token))
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    var staged = await intake.StageAsync(request, token);
                    if (staged is null)
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    if (staged.Receipt.ConsumedCaseId is not null)
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    if (!await budgets.AssessAsync(staged, token))
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    if (!await narratives.StageAsync(staged, ownedNarrative, token))
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    var receipt = staged.Receipt;
                    receipt.Consume(tenantId, callerId, receipt.HostBindingId, receipt.QualificationEpochId,
                        receipt.QualificationRevision, receipt.RelationshipSourceId, receipt.RelationshipGeneration,
                        receipt.Evidence, SHA256.HashData(request.PresentedToken.Span), staged.Case.Id,
                        request.OperationId, receipt.Revision, clock.GetUtcNow());
                    var acknowledgement = budgets.Capture(acknowledgementId, staged, digest);
                    context.ModerationCaseIntakeAcknowledgements.Add(acknowledgement);
                    await context.SaveChangesAsync(token);
                    await context.FlushDisclosureAsync(token);
                    if (!await intake.RecheckStagedAsync(token) || !await narratives.RecheckAsync(token)
                        || !await budgets.RecheckAsync(token)
                        || !CallerUnchanged(tenantId, callerId) || clock.GetUtcNow() >= receipt.ExpiresAt)
                        throw new AbortIntake(new(ModerationCaseIntakeCommitOutcome.Unavailable));
                    committedAcknowledgementId = acknowledgement.Id;
                    return new ModerationCaseIntakeCommitResult(ModerationCaseIntakeCommitOutcome.Accepted, acknowledgement);
                },
                verifySucceeded: async token =>
                {
                    context.ChangeTracker.Clear();
                    context.ModerationTrustDomainId = null;
                    if (committedAcknowledgementId is null) return false;
                    await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                    if (!await AcquireCallerAsync(tenantId, callerId, token)) return false;
                    var original = await ReadOriginalAsync(request, callerId, digest, token);
                    return original?.Id == committedAcknowledgementId && CallerUnchanged(tenantId, callerId);
                },
                isolationLevel: IsolationLevel.Serializable,
                cancellationToken: cancellationToken);
        }
        catch (AbortIntake aborted) { return aborted.Result; }
        catch (DbUpdateConcurrencyException) { return new(ModerationCaseIntakeCommitOutcome.Conflict); }
        catch (DbException) { return new(ModerationCaseIntakeCommitOutcome.Unavailable); }
        catch (DbUpdateException exception) when (exception.InnerException is DbException)
        {
            return new(ModerationCaseIntakeCommitOutcome.Unavailable);
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
            CryptographicOperations.ZeroMemory(ownedToken);
            CryptographicOperations.ZeroMemory(ownedNarrative);
        }
    }

    private async Task<bool> AcquireCallerAsync(Guid tenantId, Guid callerId, CancellationToken token)
    {
        if (!CallerUnchanged(tenantId, callerId)) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, callerId, token);
        if (!await context.Users.AsNoTracking().AnyAsync(row => row.Id == callerId && !row.IsDeleted, token)
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => row.SubjectId == callerId, token))
            return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, tenantId, token);
        if (!await context.Tenants.AsNoTracking().AnyAsync(row => row.Id == tenantId, token)) return false;
        var discovered = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
        if (discovered is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationHostBinding>(context, discovered.Id, token);
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (host is null) return false;
        context.ModerationTrustDomainId = host.TrustDomainId;
        return CallerUnchanged(tenantId, callerId);
    }

    private async Task<ModerationCaseIntakeAcknowledgement?> ReadOriginalAsync(
        ModerationCaseAccessRequest request, Guid callerId, byte[] digest, CancellationToken token)
    {
        var acknowledgement = await context.ModerationCaseIntakeAcknowledgements.AsNoTracking().SingleOrDefaultAsync(
            row => row.CallerUserId == callerId && row.OperationId == request.OperationId, token);
        if (acknowledgement is null) return null;
        acknowledgement.ValidatePersistedState();
        if (!acknowledgement.MatchesRequest(callerId, request.OperationId, request.ReceiptId, digest))
            return null;
        var receipt = await context.ModerationCaseTargetReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == acknowledgement.ReceiptId && row.CallerUserId == callerId, token);
        if (receipt is null) return null;
        receipt.ValidatePersistedState();
        if (receipt.Revision != acknowledgement.ReceiptRevision || receipt.ConsumedCaseId != acknowledgement.CaseId
            || receipt.ConsumedOperationId != acknowledgement.OperationId || receipt.ConsumedAt != acknowledgement.AcceptedAt
            || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(receipt.TokenDigest),
                SHA256.HashData(request.PresentedToken.Span)))
            return null;
        var allegation = await context.ModerationCases.Include(row => row.Evidence).AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == acknowledgement.CaseId, token);
        if (allegation is null) return null;
        allegation.ValidatePersistedState();
        return allegation.Target is { } target && allegation.Revision >= acknowledgement.CaseRevision
            && allegation.Evidence.Any(row => row.CaseRevision == acknowledgement.EvidenceCaseRevision
                && row.ToReference(target, allegation.OccurrenceId) == receipt.Evidence)
            ? acknowledgement : null;
    }

    private bool CallerUnchanged(Guid tenantId, Guid callerId) =>
        currentUser.IsAuthenticated && currentUser.UserId == callerId && machine.Current is null
        && context.TenantFilterTenantId == tenantId && !context.IsTenantFilterBypassed;

    private bool HasForeignIssuanceWork(ModerationCaseIssuedReceipt issued) =>
        context.ChangeTracker.Entries().Any(entry => entry.Entity switch
        {
            ModerationCaseTargetReceipt receipt => !ReferenceEquals(receipt, issued.Receipt),
            ModerationCaseTargetSource source => source.Id != issued.Receipt.SourceId,
            _ => true
        });

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;

    private sealed class AbortIntake(ModerationCaseIntakeCommitResult result) : Exception
    {
        public ModerationCaseIntakeCommitResult Result { get; } = result;
    }
}
