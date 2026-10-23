using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

/// <summary>
/// Observes policy metadata on the original native acquisition. The accepted child-age
/// owner and reviewer supply substantive mandates; neither this observation nor the
/// procedure vocabulary authorizes individual applicability, consent or body collection.
/// </summary>
public sealed class ModerationProfileCollectionProcedureSourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions, TimeProvider clock)
{
    internal async Task<ProcedureParents?> AcquireParentsAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var parent = new ModerationChildAgePolicySourceOwner(context, definitions, clock);
        ModerationChildAgePolicySourceObservation? source;
        ModerationChildAgePolicyAdoptionObservation? adoption;
        try
        {
            source = await parent.AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
            adoption = await parent.ReadAdoptionAsync(session, transactionId, profileId, mode, observedAt, token);
        }
        catch (ArgumentException)
        {
            // A malformed persisted parent is unavailable, never an older-source fallback.
            return null;
        }
        if (source is null || adoption is null
            || !adoption.Adoption.MatchesCurrent(source.Source, clock.GetUtcNow())
            || source.Source.Ownership.CollectionControllerHumanId == source.Source.Ownership.ReviewerControllerHumanId
            || source.Source.CollectionOwnerHumanId == source.Source.ReviewerHumanId
            || source.Source.ReviewerHumanId == source.Source.CollectionControllerHumanId
            || source.Source.CollectionOwnerHumanId == source.Source.ReviewerControllerHumanId
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || !adoption.Adoption.MatchesCurrent(source.Source, clock.GetUtcNow())) return null;
        return new(source.Source, adoption.Adoption);
    }

    public async Task<ModerationProfileCollectionProcedureSource?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(native.Authority, native.Policy.Publication.PublicationId, profileId, mode), token);
        // Select first, then qualify. A latest retired or unaccepted source never falls back.
        var id = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.PublicationId == native.Policy.Publication.PublicationId && row.ProfileId == profileId
                && row.ModeCode == (int)mode).OrderByDescending(row => row.WriterGeneration)
            .Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureSource>(context, id.Value, token);
        var source = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null) return null;
        try { source.ValidatePersistedState(); }
        catch (ArgumentException) { return null; }
        var parents = await AcquireParentsAsync(session, transactionId, profileId, mode, observedAt, token);
        if (parents is null
            || source.RecordedByHumanId != parents.Source.CollectionOwnerHumanId
            || source.Revision != 1 || source.WriterGeneration <= parents.Adoption.WriterGeneration
            || source.WriterGeneration > native.Authority.Epoch.Revision
            || source.RecordedAt < parents.Adoption.AdoptedAt
            || !source.MatchesCurrent(parents.Source, parents.Adoption, observedAt)
            || !source.MatchesCurrent(parents.Source, parents.Adoption, clock.GetUtcNow())
            || !await HasReceiptAsync(native.Authority, RecordRequest(source, native.Policy.Publication.Jurisdiction),
                ModerationQualificationSourceKind.ProfileCollectionProcedureSource, source.Id, 1,
                source.RecordedByHumanId, source.WriterGeneration, source.RecordedAt, token)
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || !source.MatchesCurrent(parents.Source, parents.Adoption, clock.GetUtcNow())) return null;
        return source;
    }

    public async Task<ModerationProfileCollectionProcedureObservation?> AcquireCurrentAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var source = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        if (source is null) return null;
        var id = await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
            .Where(row => row.SourceId == source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (id is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureAdoption>(context, id.Value, token);
        var adoption = await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
            .Include(row => row.Source).SingleOrDefaultAsync(row => row.Id == id, token);
        if (adoption is null) return null;
        try { adoption.ValidatePersistedState(); }
        catch (ArgumentException) { return null; }
        var parents = await AcquireParentsAsync(session, transactionId, profileId, mode, observedAt, token);
        if (parents is null || adoption.Source is null
            || adoption.AdoptedByHumanId != source.ReviewerHumanId
            || adoption.AdoptedByHumanId == source.RecordedByHumanId
            || adoption.WriterGeneration <= source.WriterGeneration
            || adoption.WriterGeneration > native.Authority.Epoch.Revision
            || adoption.ValidUntil > source.ValidUntil
            || !adoption.MatchesCurrent(source, parents.Source, parents.Adoption, observedAt)
            || !adoption.MatchesCurrent(source, parents.Source, parents.Adoption, clock.GetUtcNow())
            || !await HasReceiptAsync(native.Authority, AdoptionRequest(adoption, native.Policy.Publication.Jurisdiction),
                ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption, adoption.Id, 1,
                adoption.AdoptedByHumanId, adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
        var completed = await AcquireSourceAsync(session, transactionId, profileId, mode, observedAt, token);
        var currentParents = await AcquireParentsAsync(session, transactionId, profileId, mode, observedAt, token);
        if (completed is null || currentParents is null
            || !adoption.MatchesCurrent(completed, currentParents.Source, currentParents.Adoption, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || !adoption.MatchesCurrent(completed, currentParents.Source, currentParents.Adoption, clock.GetUtcNow())) return null;
        return new(transactionId, completed, adoption, observedAt, adoption.ValidUntil);
    }

    private async Task<bool> HasReceiptAsync(ModerationSourceAuthority authority, ModerationQualificationMutation template,
        ModerationQualificationSourceKind kind, Guid id, long revision, Guid human, long generation,
        DateTimeOffset completedAt, CancellationToken token)
    {
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == id && row.SourceKind == kind && row.ResultRevision == revision)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (receiptId is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId.Value, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId, token);
        if (receipt is null) return false;
        receipt.ValidatePersistedState();
        var request = template with { OperationId = receipt.Id };
        // This binds stored immutable intent, not human claims or substantive qualification.
        // Those are independently authenticated through the actual accepted native parent.
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.RequestDigest == digest && receipt.PrincipalId == human
            && receipt.SourceId == id && receipt.SourceKind == kind && receipt.ResultRevision == revision
            && receipt.QualificationRevision == generation && receipt.CompletedAt == completedAt
            && generation <= authority.Epoch.Revision && receipt.HostBindingId == authority.Binding.Id
            && receipt.TrustDomainId == authority.Binding.TrustDomainId && receipt.TenantId == authority.Tenant.Id;
    }

    internal static RecordModerationProfileCollectionProcedureSource RecordRequest(
        ModerationProfileCollectionProcedureSource source, AuthorityScope target) => new()
        {
            OperationId = Guid.Empty,
            Target = target,
            ExpectedQualificationRevision = source.WriterGeneration - 1,
            SourceId = source.Id,
            ChildAgeSourceId = source.ChildAgeSourceId,
            ExpectedChildAgeSourceRevision = source.ChildAgeSourceRevision,
            ExpectedChildAgeSourceWriterGeneration = source.ChildAgeSourceWriterGeneration,
            ChildAgeAdoptionId = source.ChildAgeAdoptionId,
            ExpectedChildAgeAdoptionRevision = source.ChildAgeAdoptionRevision,
            ExpectedChildAgeAdoptionWriterGeneration = source.ChildAgeAdoptionWriterGeneration,
            Procedure = source.Procedure,
            ValidUntil = source.ValidUntil
        };

    internal static AdoptModerationProfileCollectionProcedureSource AdoptionRequest(
        ModerationProfileCollectionProcedureAdoption adoption, AuthorityScope target) => new()
        {
            OperationId = Guid.Empty,
            Target = target,
            ExpectedQualificationRevision = adoption.WriterGeneration - 1,
            SourceId = adoption.SourceId,
            ExpectedSourceRevision = adoption.SourceRevision,
            ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration,
            AdoptionId = adoption.Id,
            ValidUntil = adoption.ValidUntil
        };

    internal static string Slot(ModerationSourceAuthority authority, Guid publication, Guid profile, ModerationNativeMode mode) =>
        $"moderation.profile-procedure:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{publication:N}:{profile:N}:{(int)mode}";

    internal sealed record ProcedureParents(ModerationChildAgePolicySource Source, ModerationChildAgePolicyAdoption Adoption);
}
