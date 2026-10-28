using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    public async Task<ModerationClassificationMaterialInventoryReadAcquisition> AcquireInventoryReadAsync(
        ModerationClassificationMaterialInventoryReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.EventGenerationId == Guid.Empty || request.TaxonomyId == Guid.Empty
            || request.ProfileId == Guid.Empty || !Enum.IsDefined(request.Mode) || request.Mandate is null)
            return new(ModerationCurrentPolicyReadOutcome.Invalid);
        try { request.Mandate.Validate(); }
        catch (ArgumentException) { return new(ModerationCurrentPolicyReadOutcome.Invalid); }
        if (machine.IsMachineCaller) return new(ModerationCurrentPolicyReadOutcome.Denied);
        var human = await admin.ResolveUserIdAsync(cancellationToken);
        if (human is null) return new(ModerationCurrentPolicyReadOutcome.Unauthenticated);
        if (human != request.HumanId || human != request.Mandate.OwnerHumanId)
            return new(ModerationCurrentPolicyReadOutcome.Denied);

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
            if (session is null || authoring.Publication is null)
                return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var sourceOwner = new ModerationClassificationMaterialSourceOwner(context, definitions, clock);
            var observation = await sourceOwner.AcquireInventoryAsync(session, session.TransactionId,
                request.EventGenerationId, request.TaxonomyId, request.ProfileId, request.Mode,
                request.Mandate, clock.GetUtcNow(), cancellationToken);
            if (observation is null) return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var writerGeneration = await ReadMaterialWriterGenerationAsync(session, observation.Generation.EventId, cancellationToken);
            if (writerGeneration is null) return new(ModerationCurrentPolicyReadOutcome.Unavailable);
            var lease = new MaterialInventoryReadLease(this, context, authoring, session, sourceOwner, request,
                observation, writerGeneration.Value, clock, context.Database.ProviderName);
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

    private async Task<long?> ReadMaterialWriterGenerationAsync(
        ModerationConsumerEvidenceSession session, Guid eventId, CancellationToken token)
    {
        var native = await session.AcquireNativeAsync(session.TransactionId, clock.GetUtcNow(), token);
        if (native is null) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationClassificationMaterialSourceOwner.Slot(native.Authority, eventId), token);
        // The actual Event slot head includes expired, unaccepted and wrong-profile sources.
        var generation = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id
                && row.TenantId == native.Authority.Tenant.Id && row.EventId == eventId)
            .OrderByDescending(row => row.WriterGeneration)
            .Select(row => (long?)row.WriterGeneration).FirstOrDefaultAsync(token);
        return await session.AcquireNativeAsync(session.TransactionId, clock.GetUtcNow(), token) is not null
            ? generation ?? 0 : null;
    }

    private sealed class MaterialInventoryReadLease(
        ModerationPolicyPublicationRepository owner, ExploreDbContext context, CurrentAuthoringReadLease authoring,
        ModerationConsumerEvidenceSession session, ModerationClassificationMaterialSourceOwner sourceOwner,
        ModerationClassificationMaterialInventoryReadRequest request,
        ModerationClassificationMaterialInventoryObservation observation, long writerGeneration, TimeProvider clock,
        string? providerName)
        : IModerationClassificationMaterialInventoryReadLease
    {
        private bool disposed;
        public AuthorityScope Jurisdiction => authoring.Jurisdiction;
        public ModerationQualificationEpoch Epoch => authoring.Epoch;
        public ModerationPolicyPublication Publication => authoring.Publication!;
        public ModerationClassificationMaterialInventoryObservation Observation { get; } = observation;
        public long MaterialWriterGeneration { get; } = writerGeneration;

        public async Task<ModerationCurrentPolicyReadOutcome> RevalidateAsync(CancellationToken cancellationToken)
        {
            if (disposed) return ModerationCurrentPolicyReadOutcome.Unavailable;
            if (context.Database.ProviderName != providerName)
                return ModerationCurrentPolicyReadOutcome.Unavailable;
            var outcome = await authoring.RevalidateAsync(cancellationToken);
            if (outcome != ModerationCurrentPolicyReadOutcome.Allowed) return outcome;
            if (clock.GetUtcNow() < Observation.ObservedAt || clock.GetUtcNow() >= Observation.ValidUntil)
                return ModerationCurrentPolicyReadOutcome.Unavailable;
            var current = await sourceOwner.AcquireInventoryAsync(session, Observation.TransactionId,
                request.EventGenerationId, request.TaxonomyId, request.ProfileId, request.Mode,
                request.Mandate, clock.GetUtcNow(), cancellationToken);
            if (current is null || !SameInventory(current)
                || await owner.ReadMaterialWriterGenerationAsync(session, Observation.Generation.EventId, cancellationToken)
                    != MaterialWriterGeneration)
                return ModerationCurrentPolicyReadOutcome.Unavailable;
            outcome = await authoring.RevalidateAsync(cancellationToken);
            return outcome == ModerationCurrentPolicyReadOutcome.Allowed
                && (context.Database.ProviderName != providerName
                    || clock.GetUtcNow() < Observation.ObservedAt || clock.GetUtcNow() >= Observation.ValidUntil)
                ? ModerationCurrentPolicyReadOutcome.Unavailable : outcome;
        }

        private bool SameInventory(ModerationClassificationMaterialInventoryObservation current) =>
            current.TransactionId == Observation.TransactionId && current.ValidUntil == Observation.ValidUntil
            && current.Generation.Id == Observation.Generation.Id
            && current.Generation.Revision == Observation.Generation.Revision
            && current.Generation.WriterGeneration == Observation.Generation.WriterGeneration
            && current.Generation.ObservedAt == Observation.Generation.ObservedAt
            && current.Generation.ValidUntil == Observation.Generation.ValidUntil
            && current.Generation.HasSameNativeVersion(Observation.Generation)
            && current.Taxonomy.TransactionId == Observation.Taxonomy.TransactionId
            && current.Taxonomy.ValidUntil == Observation.Taxonomy.ValidUntil
            && current.Taxonomy.Source.Id == Observation.Taxonomy.Source.Id
            && current.Taxonomy.Source.Revision == Observation.Taxonomy.Source.Revision
            && current.Taxonomy.Source.TaxonomyRevision == Observation.Taxonomy.Source.TaxonomyRevision
            && current.Taxonomy.Source.WriterGeneration == Observation.Taxonomy.Source.WriterGeneration
            && current.Taxonomy.Source.DefinitionId == Observation.Taxonomy.Source.DefinitionId
            && current.Taxonomy.Source.ValidUntil == Observation.Taxonomy.Source.ValidUntil
            && current.Taxonomy.Source.PolicyId == Publication.PolicyId
            && current.Taxonomy.Source.PublicationId == Publication.Id
            && current.Taxonomy.Source.PolicyRevision == Publication.Revision
            && current.Taxonomy.Source.ProfileId == request.ProfileId
            && current.Taxonomy.Source.ModeCode == (int)request.Mode
            && current.Taxonomy.Source.ConstitutionSourceId == Observation.Taxonomy.Source.ConstitutionSourceId
            && current.Taxonomy.Source.ConstitutionSourceRevision == Observation.Taxonomy.Source.ConstitutionSourceRevision
            && current.Taxonomy.Source.ConstitutionSourceGeneration == Observation.Taxonomy.Source.ConstitutionSourceGeneration
            && current.Taxonomy.Source.ConstitutionAdoptionId == Observation.Taxonomy.Source.ConstitutionAdoptionId
            && current.Taxonomy.Source.ConstitutionAdoptionRevision == Observation.Taxonomy.Source.ConstitutionAdoptionRevision
            && current.Taxonomy.Source.ConstitutionAdoptionGeneration == Observation.Taxonomy.Source.ConstitutionAdoptionGeneration
            && current.Taxonomy.Adoption.Id == Observation.Taxonomy.Adoption.Id
            && current.Taxonomy.Adoption.Revision == Observation.Taxonomy.Adoption.Revision
            && current.Taxonomy.Adoption.WriterGeneration == Observation.Taxonomy.Adoption.WriterGeneration
            && current.Taxonomy.Adoption.ValidUntil == Observation.Taxonomy.Adoption.ValidUntil
            && current.AgeSource.Id == Observation.AgeSource.Id
            && current.AgeSource.Revision == Observation.AgeSource.Revision
            && current.AgeSource.WriterGeneration == Observation.AgeSource.WriterGeneration
            && current.AgeSource.ValidUntil == Observation.AgeSource.ValidUntil
            && current.AgeAdoption.Id == Observation.AgeAdoption.Id
            && current.AgeAdoption.Revision == Observation.AgeAdoption.Revision
            && current.AgeAdoption.WriterGeneration == Observation.AgeAdoption.WriterGeneration
            && current.AgeAdoption.ValidUntil == Observation.AgeAdoption.ValidUntil
            && current.Taxonomy.Categories.Length == Observation.Taxonomy.Categories.Length
            && current.Taxonomy.Categories.ToHashSet().SetEquals(Observation.Taxonomy.Categories)
            && current.Materials.Length == Observation.Materials.Length
            && current.Materials.ToHashSet().SetEquals(Observation.Materials)
            && current.Components.Length == Observation.Components.Length
            && current.Components.ToHashSet().SetEquals(Observation.Components);

        public async ValueTask DisposeAsync()
        {
            if (disposed) return;
            disposed = true;
            session.Dispose();
            await authoring.DisposeAsync();
        }
    }
}
