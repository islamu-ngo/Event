using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    public async Task<ModerationClassificationMaterialReviewReadAcquisition> AcquireReviewReadAsync(
        ModerationClassificationMaterialReviewReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Version7(request.SourceId)) return new(ModerationCurrentPolicyReadOutcome.Invalid);
        if (machine.IsMachineCaller) return new(ModerationCurrentPolicyReadOutcome.Denied);
        var human = await admin.ResolveUserIdAsync(cancellationToken);
        if (human is null) return new(ModerationCurrentPolicyReadOutcome.Unauthenticated);
        if (human != request.HumanId) return new(ModerationCurrentPolicyReadOutcome.Denied);
        var acquired = await AcquireCurrentAuthoringAsync(
            new(ModerationScopeKind.Tenant, request.RequestTenantId, request.HumanId), cancellationToken);
        if (acquired.Lease is not CurrentAuthoringReadLease authoring)
        {
            if (acquired.Lease is not null) await acquired.Lease.DisposeAsync();
            return new(acquired.Outcome);
        }
        var handedOff = false;
        ModerationConsumerEvidenceSession? session = null;
        try
        {
            session = await authoring.AcquireMaterialEvidenceAsync(clock.GetUtcNow(), cancellationToken);
            if (session is null) return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var discovered = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == request.SourceId, cancellationToken);
            if (discovered is null || discovered.TenantId != request.RequestTenantId
                || discovered.HostBindingId != authoring.Host.Id
                || discovered.TrustDomainId != authoring.Host.TrustDomainId)
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            if (discovered.ReviewerHumanId != human || discovered.OwnerHumanId == human
                || discovered.OwnerControllerHumanId == human)
                return new(ModerationCurrentPolicyReadOutcome.Denied);
            var sourceOwner = new ModerationClassificationMaterialSourceOwner(context, definitions, clock);
            var source = await sourceOwner.AcquireSourceAsync(session, session.TransactionId, discovered.EventId,
                discovered.ProfileId, (ModerationNativeMode)discovered.ModeCode, clock.GetUtcNow(), cancellationToken);
            if (source?.Id != request.SourceId || source.ReviewerHumanId != human
                || await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
                    .AnyAsync(row => row.SourceId == source.Id, cancellationToken))
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var observation = await sourceOwner.AcquireInventoryAsync(session, session.TransactionId,
                source.EventGenerationId, source.TaxonomyId, source.ProfileId, (ModerationNativeMode)source.ModeCode,
                source.Mandate, clock.GetUtcNow(), cancellationToken);
            var writerGeneration = await ReadMaterialWriterGenerationAsync(session, source.EventId, cancellationToken);
            if (observation is null || writerGeneration is null || writerGeneration.Value != source.WriterGeneration)
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var original = new MaterialInventoryReadLease(this, context, authoring, session, sourceOwner,
                new(request.RequestTenantId, request.HumanId, source.EventGenerationId, source.TaxonomyId,
                    source.ProfileId, (ModerationNativeMode)source.ModeCode, source.Mandate),
                observation, writerGeneration.Value, clock, context.Database.ProviderName);
            var lease = new MaterialReviewReadLease(context, original, session, sourceOwner, source, request, clock);
            var outcome = await lease.RevalidateAsync(cancellationToken);
            if (outcome != ModerationCurrentPolicyReadOutcome.Allowed) return new(outcome);
            handedOff = true;
            return new(ModerationCurrentPolicyReadOutcome.Allowed, lease);
        }
        finally
        {
            if (!handedOff)
            {
                session?.Dispose();
                await authoring.DisposeAsync();
            }
        }
    }

    private sealed class MaterialReviewReadLease(
        ExploreDbContext context, MaterialInventoryReadLease original,
        ModerationConsumerEvidenceSession session, ModerationClassificationMaterialSourceOwner sourceOwner,
        ModerationClassificationMaterialSource source, ModerationClassificationMaterialReviewReadRequest request,
        TimeProvider clock) : IModerationClassificationMaterialReviewReadLease
    {
        public AuthorityScope Jurisdiction => original.Jurisdiction;
        public ModerationQualificationEpoch Epoch => original.Epoch;
        public ModerationPolicyPublication Publication => original.Publication;
        public ModerationClassificationMaterialInventoryObservation Observation => original.Observation;
        public long MaterialWriterGeneration => original.MaterialWriterGeneration;
        public ModerationClassificationMaterialSource Source { get; } = source;

        public async Task<ModerationCurrentPolicyReadOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            var outcome = await original.RevalidateAsync(cancellationToken);
            if (outcome != ModerationCurrentPolicyReadOutcome.Allowed) return outcome;
            var current = await sourceOwner.AcquireSourceAsync(session, Observation.TransactionId, Source.EventId,
                Source.ProfileId, (ModerationNativeMode)Source.ModeCode, clock.GetUtcNow(), cancellationToken);
            if (current is null || current.Id != Source.Id || current.Revision != Source.Revision
                || current.WriterGeneration != Source.WriterGeneration || current.PreviousGeneration != Source.PreviousGeneration
                || current.ReviewerHumanId != request.HumanId || current.Mandate != Source.Mandate
                || current.RecordedAt != Source.RecordedAt || current.ValidUntil != Source.ValidUntil
                || current.Materials.Count != Source.Materials.Count
                || !current.Materials.Zip(Source.Materials).All(pair =>
                    pair.First.Id == pair.Second.Id && pair.First.Material == pair.Second.Material
                    && pair.First.Categories.Select(row => row.VersionId)
                        .SequenceEqual(pair.Second.Categories.Select(row => row.VersionId)))
                || await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
                    .AnyAsync(row => row.SourceId == Source.Id, cancellationToken))
                return ModerationCurrentPolicyReadOutcome.Unavailable;
            return await original.RevalidateAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => original.DisposeAsync();
    }
}
