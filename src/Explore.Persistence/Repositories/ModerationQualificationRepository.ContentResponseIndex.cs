using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    public async Task<ModerationQualificationWriteOutcome> DiscloseContentResponseIndexAsync(
        Guid tenantId, Guid caseId, Guid? afterId, int limit,
        Func<IReadOnlyList<ModerationContentResponseSummary>, CancellationToken, Task<ReadOnlyMemory<byte>>> prepare,
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prepare);
        ArgumentNullException.ThrowIfNull(write);
        if (caseId == Guid.Empty || afterId == Guid.Empty || limit is < 1 or > 64
            || !ContentResponseEnvironment(tenantId) || responseProtection is null)
            return ModerationQualificationWriteOutcome.Unavailable;
        var deliveryStarted = false;
        try
        {
            return await privacy.ExecuteSerializedAsync(async token =>
            {
                if (context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return ModerationQualificationWriteOutcome.Unavailable;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction ? null
                    : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var transaction = context.Database.CurrentTransaction;
                if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) is not { } user)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Unauthenticated);
                if (await privacy.IsSubjectFencedAsync(user, token))
                    return ModerationQualificationWriteOutcome.NotFound;
                var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
                if (host is null) return ModerationQualificationWriteOutcome.NotFound;
                context.ModerationTrustDomainId = host.TrustDomainId;
                var at = clock.GetUtcNow();
                var candidates = await context.Set<ModerationContentResponse>().AsNoTracking()
                    .Where(row => row.HostBindingId == host.Id && row.CaseId == caseId
                        && row.RetainUntil > at && (afterId == null || row.Id.CompareTo(afterId.Value) > 0))
                    .OrderBy(row => row.Id).Take(limit)
                    .Select(row => new { row.Id, row.RepresentativeHumanId }).ToArrayAsync(token);
                if (candidates.Length == 0) return ModerationQualificationWriteOutcome.NotFound;
                // Discover the entire page's human set before the first primary authority acquisition.
                // Every repeated proof uses that same sorted set, rather than locking a later author
                // only after the host, tenant, epoch and an earlier original source were acquired.
                var representatives = candidates.Select(row => row.RepresentativeHumanId).Distinct().ToArray();
                var proofs = new List<ContentResponseReviewProof>(candidates.Length);
                foreach (var candidate in candidates)
                {
                    var proof = await ReadContentResponseReviewProofAsync(
                        tenantId, candidate.Id, token, false, representatives);
                    if (proof is null || proof.Summary.CaseId != caseId)
                        return ModerationQualificationWriteOutcome.NotFound;
                    proofs.Add(proof);
                }
                var summaries = proofs.Select(proof => proof.Summary).ToImmutableArray();
                var prepared = await prepare(summaries, token);
                foreach (var proof in proofs)
                {
                    var current = await ReadContentResponseReviewProofAsync(
                        tenantId, proof.Summary.Id, token, false, representatives);
                    if (current is null || current.Summary != proof.Summary
                        || current.Authority.UserId != proof.Authority.UserId
                        || current.Authority.Binding.Id != proof.Authority.Binding.Id
                        || current.QualificationRevision != proof.QualificationRevision
                        || current.Reviewer.Id != proof.Reviewer.Id || current.Reviewer.Revision != proof.Reviewer.Revision
                        || current.PolicySourceId != proof.PolicySourceId || current.PolicyRevision != proof.PolicyRevision
                        || current.PolicyAdoptionId != proof.PolicyAdoptionId
                        || clock.GetUtcNow() < proof.ObservedAt || clock.GetUtcNow() >= proof.ValidUntil)
                        return ModerationQualificationWriteOutcome.Unavailable;
                }
                if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                    || context.ChangeTracker.HasChanges() || context.IsTenantFilterBypassed
                    || context.TenantFilterTenantId != tenantId || context.ModerationTrustDomainId != host.TrustDomainId
                    || proofs.Any(proof => clock.GetUtcNow() < proof.ObservedAt || clock.GetUtcNow() >= proof.ValidUntil))
                    return ModerationQualificationWriteOutcome.Unavailable;
                token.ThrowIfCancellationRequested();
                deliveryStarted = true;
                await write(prepared, token);
                return ModerationQualificationWriteOutcome.Known;
            }, cancellationToken);
        }
        catch (AbortQualification aborted) when (!deliveryStarted) { return aborted.Outcome; }
        catch (ArgumentException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (JsonException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        catch (DbException) when (!deliveryStarted) { return ModerationQualificationWriteOutcome.Unavailable; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
        }
    }
}
