using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private static bool IsClassificationMaterialMutation(ModerationQualificationMutation request) =>
        request is RecordModerationClassificationMaterialSource or AcceptModerationClassificationMaterialSource
            or EndModerationClassificationMaterialReview;

    private ModerationClassificationMaterialSourceOwner ClassificationMaterialOwner() =>
        new(context, new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock);

    private async Task<ModerationConsumerEvidenceSession?> AcquireClassificationMaterialWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationClassificationMaterialSource or AcceptModerationClassificationMaterialSource)) return null;
        if (deploymentInventory is null || request.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var transaction = context.Database.CurrentTransaction!;
        var record = request as RecordModerationClassificationMaterialSource;
        ModerationClassificationMaterialSource? source = null;
        if (request is AcceptModerationClassificationMaterialSource accept)
        {
            source = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == accept.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
                || source.TenantId != authority.Tenant.Id || source.ReviewerHumanId != authority.UserId
                || source.OwnerHumanId == authority.UserId || source.Revision != accept.ExpectedSourceRevision
                || source.WriterGeneration != accept.ExpectedSourceWriterGeneration || accept.ValidUntil <= now
                || accept.ValidUntil > source.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var definition = await definitions.ReadDefinitionAsync(authority, transaction.TransactionId, request.Target,
            record?.PolicyId ?? source!.PolicyId, record?.PublicationId ?? source!.PublicationId, now, token);
        if (definition is null || record is not null && (record.ExpectedPolicyRevision != definition.Policy.Publication.ProposedRevision
            || record.Mandate.OwnerHumanId != authority.UserId || record.ValidUntil <= now || record.ValidUntil > definition.ValidUntil
            || record.Materials.IsDefaultOrEmpty || record.Materials.Length > 4096
            || record.Components.IsDefaultOrEmpty || record.Components.Length > 4096))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return new(context, transaction, authority, definition.Policy,
            cancellation => ClassificationMaterialAuthorityCurrentAsync(authority, transaction, cancellation),
            publications, deploymentInventory, consumerSources, measurementReceipts, clock, null);
    }

    private async Task<bool> ClassificationMaterialAuthorityCurrentAsync(
        ModerationSourceAuthority authority, IDbContextTransaction transaction, CancellationToken token) =>
        ReferenceEquals(context.Database.CurrentTransaction, transaction) && !machine.IsMachineCaller
        && await admin.ResolveUserIdAsync(token) == authority.UserId
        && await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(authority.Tenant.Id, token)
        && context.ModerationTrustDomainId == authority.Binding.TrustDomainId
        && await context.Users.AsNoTracking().AnyAsync(row => row.Id == authority.UserId && !row.IsDeleted, token)
        && await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
            && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
            && row.Revision == authority.Epoch.Revision, token)
        && ReferenceEquals(context.Database.CurrentTransaction, transaction);

    private async Task<QualifiedSource> RecordClassificationMaterialAsync(RecordModerationClassificationMaterialSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var live = await ClassificationMaterialOwner().AcquireInventoryAsync(session, session.TransactionId, request.EventGenerationId,
            request.TaxonomyId, request.ProfileId, request.Mode, request.Mandate, now, token);
        if (live is null || request.Mandate.OwnerHumanId != authority.UserId || request.ValidUntil > live.ValidUntil
            || live.Taxonomy.Source.Id != request.ExpectedTaxonomySourceId || live.Taxonomy.Adoption.Id != request.ExpectedTaxonomyAdoptionId
            || !live.Materials.ToHashSet().SetEquals(request.Materials.Select(row => row.Material))
            || request.Materials.Length != live.Materials.Length || request.Components.Length != live.Components.Length
            || request.Materials.Any(row => row.CategoryVersionIds.Any(id => live.Taxonomy.Categories.All(category => category.Id != id)))
            || !live.Components.ToHashSet().SetEquals(request.Components))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationClassificationMaterialSourceOwner.Slot(authority, live.Generation.EventId), token);
        var previous = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                && row.EventId == live.Generation.EventId).OrderByDescending(row => row.WriterGeneration)
            .Select(row => (long?)row.WriterGeneration).FirstOrDefaultAsync(token);
        if ((previous ?? 0) != request.ExpectedMaterialGeneration)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialSource>(context, request.SourceId, token);
        if (await context.Set<ModerationClassificationMaterialSource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var source = ModerationClassificationMaterialSource.Record(request.SourceId, request.ExpectedMaterialGeneration,
            live.Generation, live.Taxonomy.Source, live.Taxonomy.Adoption, request.Mandate,
            request.Materials, request.Components, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationClassificationMaterialSource>().Add(source);
        return new(ModerationQualificationSourceKind.ClassificationMaterialSource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AcceptClassificationMaterialAsync(AcceptModerationClassificationMaterialSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking().SingleAsync(row => row.Id == request.SourceId, token);
        var source = await ClassificationMaterialOwner().AcquireSourceAsync(session, session.TransactionId, discovered.EventId,
            discovered.ProfileId, (ModerationNativeMode)discovered.ModeCode, now, token);
        if (source?.Id != request.SourceId) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialReview>(context, request.ReviewId, token);
        if (await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
            .AnyAsync(row => row.Id == request.ReviewId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var review = ModerationClassificationMaterialReview.Accept(request.ReviewId, source, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationClassificationMaterialReview>().Add(review);
        return new(ModerationQualificationSourceKind.ClassificationMaterialReview, review.Id, 1, MutationValidUntil: review.ValidUntil);
    }

    private async Task<QualifiedSource> EndClassificationMaterialAsync(EndModerationClassificationMaterialReview request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.ReviewId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking().SingleAsync(row => row.Id == discovered.SourceId, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationClassificationMaterialSourceOwner.Slot(authority, source.EventId), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialReview>(context, request.ReviewId, token);
        var review = await context.Set<ModerationClassificationMaterialReview>().SingleAsync(row => row.Id == request.ReviewId, token);
        await RequireClassificationMaterialProvenanceAsync(request, review, authority, token);
        if (review.Revision != request.ExpectedReviewRevision || review.EndKind is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        review.End(request.ExpectedReviewRevision, request.End, authority.UserId, checked(authority.Epoch.Revision + 1), request.Grounds, now);
        return new(ModerationQualificationSourceKind.ClassificationMaterialReview, review.Id, review.Revision);
    }

    private async Task RequireClassificationMaterialProvenanceAsync(EndModerationClassificationMaterialReview request,
        ModerationClassificationMaterialReview review, ModerationSourceAuthority authority, CancellationToken token)
    {
        review.ValidatePersistedState();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationMaterialSource>(context, review.SourceId, token);
        var source = await ClassificationMaterialOwner().Graph().SingleOrDefaultAsync(row => row.Id == review.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        if (request.Target != authority.Tenant || source.HostBindingId != authority.Binding.Id
            || source.TrustDomainId != authority.Binding.TrustDomainId || source.TenantId != authority.Tenant.Id
            || review.HostBindingId != authority.Binding.Id || review.TrustDomainId != authority.Binding.TrustDomainId
            || review.TenantId != authority.Tenant.Id || review.SourceRevision != source.Revision
            || review.SourceWriterGeneration != source.WriterGeneration || review.ReviewerHumanId != source.ReviewerHumanId
            || !authority.FencedHumans.Contains(source.OwnerHumanId) || !authority.FencedHumans.Contains(review.ReviewerHumanId)
            || !await new ModerationConstitutionSourceOwner(context,
                new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock)
                .AdministratorAsync(authority, authority.UserId, authority.Tenant, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // No renewed owner mandate, expired taxonomy or material payload is required to
        // end retained acceptance. Only immutable exact birth receipts and current corrector.
        await RequireTargetSourceReceiptAsync(ModerationClassificationMaterialSourceOwner.RecordRequest(source, request.Target),
            ModerationQualificationSourceKind.ClassificationMaterialSource, source.Id, 1, source.OwnerHumanId,
            source.WriterGeneration, source.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationClassificationMaterialSourceOwner.AcceptanceRequest(review, request.Target),
            ModerationQualificationSourceKind.ClassificationMaterialReview, review.Id, 1, review.ReviewerHumanId,
            review.WriterGeneration, review.AcceptedAt, authority, token);
    }

    private async Task VerifyClassificationMaterialBeforeSaveAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsClassificationMaterialMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !await ClassificationMaterialAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is EndModerationClassificationMaterialReview ending)
        {
            await RequireClassificationMaterialProvenanceAsync(ending,
                context.ChangeTracker.Entries<ModerationClassificationMaterialReview>().Single(row => row.Entity.Id == ending.ReviewId).Entity,
                authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is RecordModerationClassificationMaterialSource record)
        {
            var retained = context.ChangeTracker.Entries<ModerationClassificationMaterialSource>().Single(row => row.Entity.Id == record.SourceId).Entity;
            var live = await ClassificationMaterialOwner().ObserveInventoryAsync(session, session.TransactionId, record.EventGenerationId,
                record.TaxonomyId, record.ProfileId, record.Mode, record.Mandate, clock.GetUtcNow(), true, token);
            if (live is null || retained.ValidUntil > live.ValidUntil || !retained.MatchesCurrent(live.Generation, live.Taxonomy.Source,
                live.Taxonomy.Adoption, live.Materials, live.Components, clock.GetUtcNow()))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        else
        {
            var accept = (AcceptModerationClassificationMaterialSource)request;
            var retained = context.ChangeTracker.Entries<ModerationClassificationMaterialReview>().Single(row => row.Entity.Id == accept.ReviewId).Entity;
            var source = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking().SingleAsync(row => row.Id == accept.SourceId, token);
            var live = await ClassificationMaterialOwner().AcquireSourceAsync(session, session.TransactionId, source.EventId,
                source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token, pendingWrite: true);
            if (live is null || !retained.MatchesCurrent(live, clock.GetUtcNow()))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
    }

    private async Task VerifyClassificationMaterialWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsClassificationMaterialMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (context.ChangeTracker.HasChanges() || transaction is null
            || !await ClassificationMaterialAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is EndModerationClassificationMaterialReview ending)
        {
            var review = await context.Set<ModerationClassificationMaterialReview>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == ending.ReviewId, token);
            if (review is null || review.Revision != ending.ExpectedReviewRevision + 1 || review.EndKind != ending.End
                || review.EndedByHumanId != authority.UserId || review.EndGrounds != ending.Grounds
                || review.EndedAt is null || review.EndWriterGeneration is null)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireClassificationMaterialProvenanceAsync(ending, review, authority, token);
            await RequireTargetSourceReceiptAsync(request, ModerationQualificationSourceKind.ClassificationMaterialReview,
                review.Id, review.Revision, authority.UserId, review.EndWriterGeneration.Value, review.EndedAt.Value, authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var sourceId = request is RecordModerationClassificationMaterialSource record ? record.SourceId
            : ((AcceptModerationClassificationMaterialSource)request).SourceId;
        var source = await context.Set<ModerationClassificationMaterialSource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == sourceId, token);
        if (source is null || (await ClassificationMaterialOwner().AcquireSourceAsync(session, session.TransactionId,
            source.EventId, source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token))?.Id != sourceId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AcceptModerationClassificationMaterialSource accept
            && (await ClassificationMaterialOwner().AcquireCurrentAsync(session, session.TransactionId, source.EventId,
                source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token))?.Acceptance.Id != accept.ReviewId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task VerifyClassificationMaterialAtCommitAsync(ModerationQualificationMutation request,
        ModerationSourceAuthority authority, IDbContextTransaction transaction, CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || !ReferenceEquals(context.Database.CurrentTransaction, transaction))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        using var current = await AcquireClassificationMaterialWriteAsync(request, authority, clock.GetUtcNow(), token);
        await VerifyClassificationMaterialWriteAsync(request, current, authority, token);
    }
}
