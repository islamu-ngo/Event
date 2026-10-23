using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    public async Task<ModerationQualificationWriteOutcome> DisclosePreFindingAsync(
        Guid tenantId, Guid noticeId, bool reviewResponse,
        Func<ModerationPreFindingView, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        if (context.TenantFilterTenantId is not { } tenant || tenant != tenantId || !ContentResponseEnvironment(tenant)
            || responseProtection is null || caseAttribution is null || noticeId == Guid.Empty)
            return ModerationQualificationWriteOutcome.Unavailable;
        try
        {
            return await privacy.ExecuteSerializedAsync(async token =>
            {
                _attributionAcquired = false;
                _preFindingUntil = DateTimeOffset.MaxValue;
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return ModerationQualificationWriteOutcome.Unavailable;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var transaction = context.Database.CurrentTransaction
                    ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                var nomination = await EventAuthorityProviderOperations.ReadWithoutRetainingLocksAsync(context,
                    async source =>
                    {
                        var host = await source.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                        if (host is null) return (Host: (ModerationHostBinding?)null, CaseId: (Guid?)null);
                        source.ModerationTrustDomainId = host.TrustDomainId;
                        var nominatedCase = await source.Set<ModerationPreFindingNotice>().AsNoTracking()
                            .Where(row => row.Id == noticeId && row.TenantId == tenant && row.HostBindingId == host.Id)
                            .Select(row => (Guid?)row.CaseId).SingleOrDefaultAsync(token);
                        return (Host: (ModerationHostBinding?)host, CaseId: nominatedCase);
                    }, token);
                if (nomination.Host is null) return ModerationQualificationWriteOutcome.Unavailable;
                context.ModerationTrustDomainId = nomination.Host.TrustDomainId;
                var caseId = nomination.CaseId;
                if (caseId is null) return ModerationQualificationWriteOutcome.NotFound;
                var authority = await new ModerationQualificationAuthority(context, admin, machine)
                    .AcquirePreFindingAsync(caseId.Value, [], token)
                    ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
                var owner = await ReadCaseAsync(caseId.Value, authority.Tenant, null, authority, token);
                var notice = await ReadPreFindingNoticeAsync(noticeId, authority, owner, token);
                await RequirePreFindingReaderAsync(owner, notice, reviewResponse, authority, token);
                var plaintext = PreFindingProtector(notice.Id, notice.CaseId, notice.CustodyId,
                    notice.AttributionSourceId, notice.RecipientHumanId, notice.RetainUntil, "notice")
                    .Unprotect(notice.ProtectedNotice.ToArray());
                ModerationPreFindingText text;
                try
                {
                    text = JsonSerializer.Deserialize<ModerationPreFindingText>(plaintext)
                        ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
                string? response = null;
                if (notice.ResponseId is not null)
                {
                    var statement = PreFindingProtector(notice.Id, notice.CaseId, notice.CustodyId,
                        notice.AttributionSourceId, notice.RecipientHumanId, notice.RetainUntil, "response")
                        .Unprotect(notice.ProtectedResponse.ToArray());
                    try { response = Encoding.UTF8.GetString(statement); }
                    finally { CryptographicOperations.ZeroMemory(statement); }
                }
                var started = clock.GetUtcNow();
                var prepared = await prepare(new(notice.Id, notice.Revision, owner.Id, owner.Revision, text, notice.RetainUntil,
                    notice.AcknowledgedAt, notice.RespondedAt, response), token);
                var current = await ReadPreFindingNoticeAsync(notice.Id, authority, owner, token);
                await RequirePreFindingReaderAsync(owner, current, reviewResponse, authority, token);
                await RequirePreFindingTerminalAsync(authority, transaction, token);
                if (current.Revision != notice.Revision || clock.GetUtcNow() < started
                    || clock.GetUtcNow() >= _preFindingUntil
                    || !CryptographicOperations.FixedTimeEquals(current.ProtectedNotice.Span, notice.ProtectedNotice.Span)
                    || !CryptographicOperations.FixedTimeEquals(current.ProtectedResponse.Span, notice.ProtectedResponse.Span))
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
                await write(prepared, token);
                return ModerationQualificationWriteOutcome.Known;
            }, cancellationToken);
        }
        catch (AbortQualification aborted) { return aborted.Outcome; }
        catch (DbUpdateConcurrencyException) { return ModerationQualificationWriteOutcome.Conflict; }
        catch (CryptographicException) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (JsonException) { return ModerationQualificationWriteOutcome.Unavailable; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }

    private async Task RequirePreFindingReaderAsync(ModerationCase owner, ModerationPreFindingNotice notice,
        bool reviewResponse, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (reviewResponse)
            await RequirePreFindingReviewerAsync(owner, authority.UserId, authority, token);
        else if (authority.UserId != notice.RecipientHumanId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }

    /// <summary>
    /// Parent final-finding integration calls this instead of manufacturing human response evidence.
    /// The caller must retain the same authority and run RecheckPreFindingResponseAsync after flush
    /// and at the retained privacy authority's actual primary-commit checkpoint.
    /// </summary>
    private async Task<ModerationFindingEvidenceReference?> AcquirePreFindingResponseAsync(
        ModerationCase owner, Guid responseId, ModerationSourceAuthority authority, CancellationToken token,
        bool requireObserverReview = true)
    {
        _preFindingUntil = DateTimeOffset.MaxValue;
        var noticeId = await context.Set<ModerationPreFindingNotice>().AsNoTracking()
            .Where(row => row.CaseId == owner.Id && row.ResponseId == responseId)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (noticeId is null) return null;
        if (requireObserverReview)
            await RequirePreFindingReviewerAsync(owner, authority.UserId, authority, token);
        var notice = await ReadPreFindingNoticeAsync(noticeId.Value, authority, owner, token,
            requireReader: requireObserverReview);
        if (!notice.HasResponseAt(clock.GetUtcNow()) || notice.ResponseId != responseId) return null;
        var receipt = await context.Set<ModerationPreFindingReceipt>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == responseId, token);
        if (receipt is null || receipt.CaseId != owner.Id || receipt.ResultId != notice.Id
            || receipt.ResultRevision != notice.Revision || receipt.ActorHumanId != notice.RecipientHumanId
            || receipt.HostBindingId != authority.Binding.Id || receipt.TenantId != authority.Tenant.Id
            || receipt.TrustDomainId != authority.Binding.TrustDomainId
            || receipt.CompletedAt < notice.RespondedAt || receipt.CompletedAt > clock.GetUtcNow())
            return null;
        var bytes = PreFindingProtector(notice.Id, notice.CaseId, notice.CustodyId, notice.AttributionSourceId,
            notice.RecipientHumanId, notice.RetainUntil, "response").Unprotect(notice.ProtectedResponse.ToArray());
        try
        {
            var original = new RespondToModerationPreFindingNotice(responseId, owner.Id, owner.Revision,
                notice.Id, notice.Revision - 1, Encoding.UTF8.GetString(bytes));
            if (PreFindingDigest(original) != receipt.RequestDigest) return null;
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        return ModerationFindingEvidenceReference.Response(responseId, owner.Id, owner.Revision, notice.RespondedAt!.Value);
    }

    private async Task RecheckPreFindingResponseAsync(ModerationCase owner, Guid responseId,
        ModerationSourceAuthority authority, CancellationToken token)
    {
        if (await AcquirePreFindingResponseAsync(owner, responseId, authority, token) is null
            || context.Database.CurrentTransaction is not { } transaction)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequirePreFindingTerminalAsync(authority, transaction, token);
        if (clock.GetUtcNow() >= _preFindingUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task RequirePreFindingFindingAtCommitAsync(
        ModerationQualificationMutation mutation, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (mutation is not FinalizeModerationFindingSource source) return;
        var owner = await ReadCaseAsync(source.CaseId, source.Target, source.ExpectedCaseRevision, authority, token);
        if (owner.AttributionSourceId is null) return;
        var finding = await context.ModerationFindings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.FindingId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        finding.ValidatePersistedState();
        var attribution = await ReadCurrentCaseAttributionAsync(owner, authority, token);
        if (finding.CaseId != owner.Id || finding.CaseRevision != owner.Revision
            || finding.Scope != source.Target
            || finding.AttributionSourceId != attribution.Id || finding.AttributionGeneration != attribution.Generation
            || finding.EvidenceReferenceId != source.EvidenceId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (finding.EvidencePurpose == ModerationFindingEvidencePurpose.Response)
            await RecheckPreFindingResponseAsync(owner, source.EvidenceId, authority, token);
    }

    public async Task<int> PurgeExpiredPreFindingAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (context.TenantFilterTenantId is not { } tenant || !ContentResponseEnvironment(tenant))
            throw new InvalidOperationException("Pre-finding cleanup requires a clean selected-tenant transaction.");
        // Monotone internal expiry cleanup needs no live subject, reviewer, decryptor or policy.
        // This is not a public administrative endpoint and cannot erase unexpired custody.
        try
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
                if (host is null || !await new ModerationQualificationAuthority(context, admin, machine)
                        .ProviderTenantMatchesAsync(tenant, cancellationToken))
                    throw new InvalidOperationException("Native pre-finding cleanup authority is unavailable.");
                context.ModerationTrustDomainId = host.TrustDomainId;
                var now = PreFindingTime();
                var sqlite = context.Database.ProviderName == RelationalNamedLock.SqliteProvider;
                var candidates = sqlite
                    ? context.Set<ModerationPreFindingNotice>().FromSqlInterpolated($"""
                        SELECT n.* FROM moderation_pre_finding_notices AS n
                        WHERE n.purged_at IS NULL AND julianday(n.retain_until) <= julianday({now})
                        """)
                    : context.Set<ModerationPreFindingNotice>().Where(row => row.PurgedAt == null && row.RetainUntil <= now);
                var metadata = await candidates.AsNoTracking()
                    .Where(row => row.HostBindingId == host.Id && row.TenantId == tenant && row.TrustDomainId == host.TrustDomainId)
                    .OrderBy(row => row.Id).Take(batchSize).Select(row => new { row.Id, row.Revision, row.RetainUntil })
                    .ToArrayAsync(cancellationToken);
                var count = 0;
                foreach (var row in metadata.Where(row => row.RetainUntil <= now))
                {
                    await RelationalEntityRowFence.AcquireGlobalAsync<ModerationPreFindingNotice>(context, row.Id, cancellationToken);
                    var purged = await context.Set<ModerationPreFindingNotice>()
                        .Where(candidate => candidate.Id == row.Id && candidate.Revision == row.Revision
                            && candidate.PurgedAt == null && candidate.RetainUntil == row.RetainUntil)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(candidate => EF.Property<byte[]>(candidate, "protectedNotice"), Array.Empty<byte>())
                            .SetProperty(candidate => EF.Property<byte[]>(candidate, "protectedResponse"), Array.Empty<byte>())
                            .SetProperty(candidate => candidate.PurgedAt, (DateTimeOffset?)now)
                            .SetProperty(candidate => candidate.Revision, candidate => candidate.Revision + 1), cancellationToken);
                    if (purged != 0)
                        await ModerationPreFindingNotificationCleanup.StageAsync(
                            context, tenant, null, row.Id, cancellationToken);
                    count += purged;
                }
                if (clock.GetUtcNow() < now || context.TenantFilterTenantId != tenant || context.IsTenantFilterBypassed
                    || context.ModerationTrustDomainId != host.TrustDomainId
                    || !await new ModerationQualificationAuthority(context, admin, machine)
                        .ProviderTenantMatchesAsync(tenant, cancellationToken))
                    throw new InvalidOperationException("Native pre-finding cleanup authority changed.");
                if (count > 0)
                    await context.RecordModerationRecoveryBulkMutationAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return count;
            });
        }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }
}
