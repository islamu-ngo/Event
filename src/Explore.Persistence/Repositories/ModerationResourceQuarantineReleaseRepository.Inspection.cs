using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Moderation.Inspection;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationResourceQuarantineReleaseRepository
{
    internal async Task<bool> InspectMaterialAsync(
        ModerationResourceQuarantineInspectionIntent intent, IPrivacyIdentityFenceAuthority privacy,
        ModerationResourceInspectionMaterialReader material, ModerationResourceInspectionOutput write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(write);
        if (!new ModerationResourceQuarantineInspectionValidator().Validate(intent).IsValid
            || !Clean() || System.Transactions.Transaction.Current is not null)
            return false;

        var tenantId = context.TenantFilterTenantId!.Value;
        var policyLocks = new List<IAsyncDisposable>();
        var entered = false;
        var outputStarted = false;
        try
        {
            // Same outer order as native setting writers. Do not use the setting service's
            // execution strategy: a terminal transport operation must never be replayed.
            var keys = EventResourceSettingMutationGuard.Keys.Concat([
                GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
                GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
                GovernanceSettingKeys.TenantDelegation.LockStorage,
                GovernanceSettingKeys.Deployment.Mode
            ]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var key in keys)
                policyLocks.Add(await RelationalNamedLock.AcquireSessionAsync(
                    context, $"explore:setting-mutation:{key}", cancellationToken));

            return await privacy.ExecuteSerializedAsync(async token =>
            {
                if (entered) return false;
                entered = true;
                if (context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
                    || context.ModerationTrustDomainId is not null || context.ChangeTracker.HasChanges()
                    || context.Database.CurrentTransaction is not null && !context.IdentityFenceOwnsTransaction)
                    return false;
                await using IDbContextTransaction? owned = context.IdentityFenceOwnsTransaction
                    ? null : await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var transaction = context.Database.CurrentTransaction;
                if (transaction is null || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable)
                    return false;
                var chain = await ReadChainAsync(intent.AdoptionId, token);
                if (chain.Count != 2 || chain[1].Step != ResourceSafetyReviewStep.AdoptPurpose
                    || !chain.Any(row => row.ReviewerAssignmentId == intent.ReviewerAssignmentId))
                    return false;

                // Only opaque ancestry/human coordinates are discovered here. Neither resource
                // ciphertext nor provider object keys nor any original allegation is projected.
                var humans = await DiscoverResourceSafetyHumansAsync(
                    intent.ResourceId, intent.CaseId, intent.ReviewerAssignmentId, chain, token);
                foreach (var human in humans)
                    await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, human, token);
                await RequireInspectionHumansAsync(humans, privacy, token);
                var acquiredAt = clock.GetUtcNow();
                var qualified = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                    intent.CaseId, intent.ExpectedCaseRevision, chain, intent.ReviewerAssignmentId, acquiredAt, intent, token);
                RequireInspectionReviewer(intent, chain, qualified, acquiredAt);
                var epoch = qualified.Authority.Epoch.Revision;
                var userId = qualified.Authority.UserId;
                var trustId = qualified.Authority.Binding.TrustDomainId;
                var until = qualified.ValidUntil;
                var remaining = until - clock.GetUtcNow();
                if (remaining <= TimeSpan.Zero) return false;
                using var deadline = new CancellationTokenSource(remaining, clock);
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
                token = lifetime.Token;
                var policy = await material.ReadPolicyAsync(tenantId, token) ?? throw new SafetyUnavailable();
                var audienceIds = await context.Set<EventResourceAudienceRule>().AsNoTracking()
                    .Where(row => row.EventResourceId == intent.ResourceId && row.TenantId == tenantId)
                    .OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(token);
                foreach (var id in audienceIds)
                    await RelationalEntityRowFence.AcquireGlobalAsync<EventResourceAudienceRule>(context, id, token);
                var audienceDigest = Digest(await context.Set<EventResourceAudienceRule>().AsNoTracking()
                    .Where(row => row.EventResourceId == intent.ResourceId && row.TenantId == tenantId)
                    .OrderBy(row => row.Id).ToArrayAsync(token));

                async Task<bool> CurrentAsync()
                {
                    token.ThrowIfCancellationRequested();
                    if (!ReferenceEquals(context.Database.CurrentTransaction, transaction)
                        || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
                        || System.Transactions.Transaction.Current is not null || context.ChangeTracker.HasChanges()
                        || context.IsTenantFilterBypassed || context.TenantFilterTenantId != tenantId
                        || context.ModerationTrustDomainId != trustId || machine.IsMachineCaller
                        || clock.GetUtcNow() < acquiredAt || clock.GetUtcNow() >= until)
                        return false;
                    var currentHumans = await DiscoverResourceSafetyHumansAsync(
                        intent.ResourceId, intent.CaseId, intent.ReviewerAssignmentId, chain, token);
                    if (!humans.SequenceEqual(currentHumans)) return false;
                    var current = await QualifyAsync(intent.QuarantineId, intent.ResourceId, intent.MaterialVersionId,
                        intent.CaseId, intent.ExpectedCaseRevision, chain, intent.ReviewerAssignmentId, clock.GetUtcNow(), intent, token);
                    RequireInspectionReviewer(intent, chain, current, clock.GetUtcNow());
                    if (current.Authority.UserId != userId || current.Authority.Epoch.Revision != epoch
                        || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(
                            row => row.Id == qualified.Authority.Epoch.Id && row.Revision == epoch, token)
                        || !qualified.Native.HasSameNativeVersion(current.Native)
                        || !humans.SequenceEqual(currentHumans)
                        || audienceDigest != Digest(await context.Set<EventResourceAudienceRule>().AsNoTracking()
                            .Where(row => row.EventResourceId == intent.ResourceId && row.TenantId == tenantId)
                            .OrderBy(row => row.Id).ToArrayAsync(token))
                        || !policy.Equals(await material.ReadPolicyAsync(tenantId, token)))
                        return false;
                    await RequireInspectionHumansAsync(humans, privacy, token);
                    return clock.GetUtcNow() >= acquiredAt && clock.GetUtcNow() < until
                        && clock.GetUtcNow() < current.ValidUntil
                        && await RecheckResourceSafetyProvenanceAsync(qualified, current, token);
                }

                // Qualification precedes the first full resource/storage projection.
                if (!await CurrentAsync()) return false;
                var resource = await context.EventResources.AsNoTracking().Include(row => row.AudienceRules)
                    .SingleOrDefaultAsync(row => row.Id == intent.ResourceId && row.TenantId == tenantId, token)
                    ?? throw new SafetyUnavailable();
                if (resource.IsDeleted || resource.ConcurrencyStamp != qualified.Native.ResourceStamp
                    || resource.MaterialVersionId != intent.MaterialVersionId || !resource.HasModerationQuarantine)
                    return false;
                var storage = resource.StorageObjectId is { } storageId
                    ? await context.Set<StorageObject>().AsNoTracking().SingleOrDefaultAsync(
                        row => row.Id == storageId && row.TenantId == tenantId, token) : null;
                if (resource.StorageObjectId is not null && (storage is null
                    || storage.ConcurrencyStamp != qualified.Native.StorageObjectStamp))
                    return false;
                using var prepared = await material.PrepareAsync(resource, storage, policy, CurrentAsync, token);
                if (prepared is null) return false;
                // Read/verify provider bytes privately first; slow providers never extend a
                // purpose. Requalify immediately before every actual output chunk.
                for (var offset = 0; offset < prepared.Bytes.Length;)
                {
                    if (!await CurrentAsync())
                    {
                        if (outputStarted) throw new InvalidOperationException("Inspection authority ended during output.");
                        return false;
                    }
                    var chunk = prepared.Bytes.Slice(offset, Math.Min(65_536, prepared.Bytes.Length - offset));
                    outputStarted = true;
                    await write(chunk, prepared.IsDestination, token);
                    offset += chunk.Length;
                }
                return outputStarted;
            }, cancellationToken);
        }
        catch (SafetyUnavailable) when (!outputStarted) { return false; }
        catch (SafetyUnavailable exception) when (outputStarted)
        { throw new InvalidOperationException("Inspection authority ended during output.", exception); }
        catch (CryptographicException) when (!outputStarted) { return false; }
        catch (DbException) when (!outputStarted) { return false; }
        catch (IOException) when (!outputStarted) { return false; }
        finally
        {
            context.ChangeTracker.Clear();
            context.ModerationTrustDomainId = null;
            for (var index = policyLocks.Count - 1; index >= 0; index--)
                await policyLocks[index].DisposeAsync();
        }
    }

    private static void RequireInspectionReviewer(ModerationResourceQuarantineInspectionIntent intent,
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, QualifiedSafety qualified, DateTimeOffset now)
    {
        RequireIndependentChain(chain, qualified, now);
        var reviewer = qualified.Reviewers[intent.ReviewerAssignmentId];
        if (qualified.Cause.ReleaseId is not null || qualified.Authority.Epoch.Revision != intent.ExpectedQualificationRevision
            || reviewer.ReviewerHumanId != qualified.Authority.UserId || reviewer.Revision != intent.ExpectedReviewerRevision
            || !chain.Any(row => row.ReviewerAssignmentId == reviewer.Id
                && row.HumanId == qualified.Authority.UserId && row.ControllerHumanId == reviewer.ControllerHumanId))
            throw new SafetyUnavailable();
    }

    private async Task<Guid[]> DiscoverResourceSafetyHumansAsync(Guid resourceId, Guid caseId, Guid? assignmentId,
        IReadOnlyList<EventResourceModerationQuarantineReview> chain, CancellationToken token,
        bool includeMaterialHumans = true)
    {
        var tenant = context.TenantFilterTenantId;
        var host = await context.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token)
            ?? throw new SafetyUnavailable();
        context.ModerationTrustDomainId = host.TrustDomainId;
        var humans = chain.SelectMany(row => new[] { row.HumanId, row.ControllerHumanId }).ToHashSet();
        if (await admin.ResolveUserIdAsync(token) is not { } caller) throw new SafetyUnavailable();
        humans.Add(caller);
        var ids = chain.Select(row => row.ReviewerAssignmentId)
            .Concat(assignmentId is { } requested ? [requested] : Array.Empty<Guid>()).ToArray();
        var reviewers = await context.ModerationReviewerAssignments.AsNoTracking()
            .Where(row => ids.Contains(row.Id)).Select(row => new
            {
                row.ReviewerHumanId, row.AssignedByHumanId, row.ControllerHumanId, row.QualifiedByHumanId, row.AssessedByHumanId
            }).ToArrayAsync(token);
        foreach (var row in reviewers)
            Add([row.ReviewerHumanId, row.AssignedByHumanId, row.ControllerHumanId, row.QualifiedByHumanId, row.AssessedByHumanId]);
        var policy = await context.ModerationQualificationPolicySources.AsNoTracking()
            .Where(row => row.CaseId == caseId && row.HostBindingId == host.Id)
            .Select(row => new { row.Id, row.Revision, row.PublishedByHumanId }).SingleOrDefaultAsync(token)
            ?? throw new SafetyUnavailable();
        humans.Add(policy.PublishedByHumanId);
        Add(await context.ModerationQualificationPolicyAdoptions.AsNoTracking()
            .Where(row => row.SourceId == policy.Id && row.SourceRevision == policy.Revision)
            .Select(row => (Guid?)row.AdoptedByHumanId).ToArrayAsync(token));
        Add(await ModerationCaseTargetProofOwner.DiscoverResourceSafetyAttributionHumansAsync(context, caseId, token));
        if (!includeMaterialHumans) return humans.Order().ToArray();
        var resource = await context.EventResources.AsNoTracking()
            .Where(row => row.Id == resourceId && row.TenantId == tenant)
            .Select(row => new { row.EventId, row.CreatedBy, row.StorageObjectId }).SingleOrDefaultAsync(token)
            ?? throw new SafetyUnavailable();
        var owningEvent = await context.Events.AsNoTracking().Where(row => row.Id == resource.EventId && row.TenantId == tenant)
            .Select(row => new { row.ActorId, row.OrganizerActorId, row.SubmittedByUserId }).SingleOrDefaultAsync(token)
            ?? throw new SafetyUnavailable();
        Add([resource.CreatedBy, owningEvent.SubmittedByUserId]);
        var storage = resource.StorageObjectId is { } storageId
            ? await context.Set<StorageObject>().AsNoTracking().Where(row => row.Id == storageId && row.TenantId == tenant)
                .Select(row => new { row.CreatedBy, row.ActorId }).SingleOrDefaultAsync(token) : null;
        Add([storage?.CreatedBy]);
        var actors = new[] { (Guid?)owningEvent.ActorId, owningEvent.OrganizerActorId, storage?.ActorId };
        Add(await context.Actors.AsNoTracking().Where(row => actors.Contains(row.Id)).Select(row => row.UserId).ToArrayAsync(token));
        return humans.Order().ToArray();

        void Add(IEnumerable<Guid?> values)
        {
            foreach (var value in values)
                if (value is { } id && id != Guid.Empty) humans.Add(id);
        }
    }

    private async Task RequireInspectionHumansAsync(
        Guid[] humans, IPrivacyIdentityFenceAuthority privacy, CancellationToken token)
    {
        if (await context.Users.AsNoTracking().CountAsync(row => humans.Contains(row.Id) && !row.IsDeleted, token) != humans.Length
            || await context.PrivacyErasureSagas.AsNoTracking().AnyAsync(row => humans.Contains(row.SubjectId), token))
            throw new SafetyUnavailable();
        foreach (var human in humans)
            if (await privacy.IsSubjectFencedAsync(human, token)) throw new SafetyUnavailable();
    }
}
