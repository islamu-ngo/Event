using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Models;
using Explore.Application.Features.Federation.Atproto.Requests.Commands;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Features.Federation.Atproto.Validators;
using Explore.Application.Models.Storage;
using Explore.Application.Services.Federation;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.Federation.Atproto.Handlers.Commands;

public sealed class ReconcileAtprotoPdsSnapshotsCommandHandler(
    AtprotoPdsRecoveryPolicyResolver policyResolver,
    AtprotoJetstreamTenantPresentationResolver presentationResolver,
    IAtprotoPdsSnapshotGateway gateway,
    IAtprotoThumbnailBlobGateway thumbnailGateway,
    IAtprotoPdsSnapshotRepository repository,
    IAtprotoFederationArchiveProbe archiveProbe,
    TimeProvider timeProvider)
    : IRequestHandler<ReconcileAtprotoPdsSnapshotsCommand, AtprotoPdsRecoveryResult>
{
    public const int MaximumRecoveryDids = 100;

    public async Task<AtprotoPdsRecoveryResult> Handle(
        ReconcileAtprotoPdsSnapshotsCommand request,
        CancellationToken cancellationToken)
    {
        var validator = new ReconcileAtprotoPdsSnapshotsCommandValidator();
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        string[] normalizedDids = request.AllowedDids
            .Select(did => did.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (normalizedDids.Length > MaximumRecoveryDids)
        {
            return new(AtprotoPdsRecoveryOutcome.ScopeRejected, Hash(string.Join('\n', normalizedDids)));
        }

        AtprotoPdsRecoveryPolicy policy = await policyResolver.ResolveAsync(cancellationToken);
        string recoveryScope = $"{policy.AudienceFingerprint}\n{string.Join('\n', normalizedDids)}";
        string fingerprint = Hash(recoveryScope);
        if (!policy.IsEnabled)
        {
            return new(AtprotoPdsRecoveryOutcome.Disabled, fingerprint);
        }

        if (policy.Mode == AtprotoPdsRecoveryMode.DowntimeOnly)
        {
            return new(AtprotoPdsRecoveryOutcome.DowntimeOnly, fingerprint);
        }

        if (normalizedDids.Length == 0)
        {
            return new(AtprotoPdsRecoveryOutcome.ScopeRejected, fingerprint);
        }

        if (normalizedDids.Any(did => !IsSupportedPdsDid(did)))
        {
            return new(AtprotoPdsRecoveryOutcome.ScopeRejected, fingerprint);
        }

        IReadOnlyList<Guid> enabledPresentationTenantIds = await presentationResolver
            .ResolveEnabledTenantIdsAsync(cancellationToken);
        Guid[] presentationTenantIds = policy.EffectiveTenantIds
            .Intersect(enabledPresentationTenantIds)
            .Distinct()
            .Order()
            .ToArray();
        fingerprint = Hash(
            $"{recoveryScope}\npresentation:{string.Join(',', presentationTenantIds.Select(id => id.ToString("N")))}");
        if (string.Equals(request.LastCompletedFingerprint, fingerprint, StringComparison.Ordinal))
        {
            return new(AtprotoPdsRecoveryOutcome.Unchanged, fingerprint);
        }

        // Ask the sealed archive which repositories actually committed calendar records since the consumer
        // cursor. A conclusive answer lets a restart skip re-fetching and re-verifying quiet repositories
        // entirely; an inconclusive one leaves the full configured scope untouched. The fingerprint stays
        // keyed on the configured scope, not the narrowed one, so memoisation does not churn.
        AtprotoArchiveChangeScope changeScope = await archiveProbe.ResolveChangedDidsAsync(
            request.Claim.Cursor,
            normalizedDids,
            cancellationToken);
        string[] recoveryDids = normalizedDids;
        if (changeScope.IsConclusive)
        {
            var changed = new HashSet<string>(changeScope.ChangedDids, StringComparer.Ordinal);
            recoveryDids = [.. normalizedDids.Where(changed.Contains)];
            if (recoveryDids.Length == 0)
            {
                return new(AtprotoPdsRecoveryOutcome.Unchanged, fingerprint);
            }
        }

        long snapshotVersion = ToUnixMicroseconds(request.SnapshotStartedAt);
        var snapshots = new List<AtprotoPdsSnapshot>(recoveryDids.Length);
        int failed = 0;
        foreach (string did in recoveryDids)
        {
            AtprotoPdsSnapshotFetchResult fetched = await gateway.FetchAsync(
                did,
                snapshotVersion,
                cancellationToken);
            if (!fetched.IsComplete
                || fetched.Snapshot is null
                || !IsValidSnapshot(did, fetched.Snapshot))
            {
                failed++;
                continue;
            }

            snapshots.Add(fetched.Snapshot);
        }

        if (failed > 0)
        {
            return new(AtprotoPdsRecoveryOutcome.PartialFailure, fingerprint, FailedDids: failed);
        }

        var importPlans = new List<AtprotoFederatedEventImportPlan>();
        foreach (AtprotoPdsSnapshotItem item in snapshots.SelectMany(snapshot => snapshot.Items))
        {
            if (item.EventProjection is null)
            {
                continue;
            }

            IReadOnlyList<AtprotoFederatedEventImportPlan> itemPlans =
                await AtprotoFederatedEventImportPlanFactory.CreateAsync(
                    item.Record,
                    item.EventProjection,
                    presentationTenantIds,
                    cancellationToken);
            importPlans.AddRange(itemPlans);
        }

        IReadOnlyList<AtprotoFederatedEventImportPlan> stagedPlans =
            await StageThumbnailsAsync(importPlans, cancellationToken);
        // Must be the narrowed set, not the configured one: persistence pairs it one-to-one with the
        // fetched snapshots, and it is the scope over which absent records get tombstoned. Passing DIDs
        // that were never fetched would make them look emptied.
        var applyRequest = new AtprotoPdsSnapshotApplyRequest(
            request.Claim,
            recoveryDids,
            snapshots,
            presentationTenantIds,
            snapshotVersion,
            timeProvider.GetUtcNow().UtcDateTime)
        {
            EventImports = stagedPlans
        };
        AtprotoPersistenceApplyResult result;
        try
        {
            result = await repository.TryReconcileWithResultAsync(
                applyRequest,
                cancellationToken);
        }
        catch
        {
            await CleanupUnconsumedAsync(stagedPlans, []);
            throw;
        }

        await CleanupUnconsumedAsync(stagedPlans, result.ConsumedStagedThumbnails);
        return result.Applied
            ? new(AtprotoPdsRecoveryOutcome.Completed, fingerprint, recoveryDids.Length)
            : new(AtprotoPdsRecoveryOutcome.FenceRejected, fingerprint);
    }

    private async Task<IReadOnlyList<AtprotoFederatedEventImportPlan>> StageThumbnailsAsync(
        IReadOnlyList<AtprotoFederatedEventImportPlan> plans,
        CancellationToken cancellationToken)
    {
        var stagedPlans = new List<AtprotoFederatedEventImportPlan>(plans.Count);
        try
        {
            foreach (AtprotoFederatedEventImportPlan plan in plans)
            {
                FileStorageWriteResult? staged = await thumbnailGateway.FetchAndStageAsync(
                    plan.Thumbnail,
                    plan.TenantId,
                    cancellationToken);
                stagedPlans.Add(plan with { StagedThumbnail = staged });
            }

            return stagedPlans;
        }
        catch
        {
            await CleanupUnconsumedAsync(stagedPlans, []);
            throw;
        }
    }

    private async Task CleanupUnconsumedAsync(
        IEnumerable<AtprotoFederatedEventImportPlan> plans,
        IReadOnlyList<FileStorageWriteResult> consumed)
    {
        foreach (FileStorageWriteResult staged in plans
                     .Select(plan => plan.StagedThumbnail)
                     .OfType<FileStorageWriteResult>()
                     .Where(staged => !consumed.Contains(staged)))
        {
            await thumbnailGateway.CleanupAsync(staged, CancellationToken.None);
        }
    }

    private static bool IsValidSnapshot(string expectedDid, AtprotoPdsSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Did, expectedDid, StringComparison.Ordinal))
        {
            return false;
        }

        var present = new HashSet<(string Collection, string RecordKey)>();
        foreach (AtprotoPdsSnapshotIdentity identity in snapshot.PresentIdentities)
        {
            if (!IsSupportedCollection(identity.Collection)
                || string.IsNullOrWhiteSpace(identity.RecordKey)
                || !present.Add((identity.Collection, identity.RecordKey)))
            {
                return false;
            }
        }

        return snapshot.Items.All(item =>
            string.Equals(item.Record.Did, expectedDid, StringComparison.Ordinal)
            && IsSupportedCollection(item.Record.Collection)
            && present.Contains((item.Record.Collection, item.Record.RecordKey))
            && (item.Record.Collection == AtprotoEventPublicationPlanner.EventCollection)
                == (item.EventProjection is not null));
    }

    private static bool IsSupportedCollection(string collection) =>
        collection == AtprotoEventPublicationPlanner.EventCollection;

    private static bool IsSupportedPdsDid(string did) =>
        did.StartsWith("did:plc:", StringComparison.Ordinal)
        || did.StartsWith("did:web:", StringComparison.Ordinal);

    private static long ToUnixMicroseconds(DateTime value) =>
        checked((value - DateTime.UnixEpoch).Ticks / 10);

    private static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
