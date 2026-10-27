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
    private static bool IsClassificationTaxonomyMutation(ModerationQualificationMutation request) =>
        request is RecordModerationClassificationTaxonomySource or AdoptModerationClassificationTaxonomySource
            or WithdrawModerationClassificationTaxonomyAdoption;

    private async Task<ModerationConsumerEvidenceSession?> AcquireClassificationTaxonomyWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationClassificationTaxonomySource or AdoptModerationClassificationTaxonomySource)) return null;
        if (deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var transaction = context.Database.CurrentTransaction!;
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var recording = request as RecordModerationClassificationTaxonomySource;
        ModerationClassificationTaxonomySource? source = null;
        if (request is AdoptModerationClassificationTaxonomySource adopting)
        {
            source = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
                .Include(row => row.Categories.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == adopting.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            source.ValidatePersistedState();
            if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
                || source.TenantId != authority.Tenant.Id || source.ReviewerHumanId != authority.UserId
                || source.OwnerHumanId == authority.UserId || source.Revision != adopting.ExpectedSourceRevision
                || source.WriterGeneration != adopting.ExpectedSourceWriterGeneration
                || adopting.ValidUntil <= now || adopting.ValidUntil > source.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var definition = await definitions.ReadDefinitionAsync(authority, transaction.TransactionId, request.Target,
            recording?.PolicyId ?? source!.PolicyId, recording?.PublicationId ?? source!.PublicationId, now, token);
        if (definition is null || recording is not null && (recording.ExpectedPolicyRevision != definition.Policy.Publication.ProposedRevision
            || recording.Mandate.OwnerHumanId != authority.UserId || recording.ValidUntil <= now
            || recording.ValidUntil > definition.ValidUntil || recording.Categories.IsDefaultOrEmpty || recording.Categories.Length > 64))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        return new(context, transaction, authority, definition.Policy,
            cancellation => ClassificationTaxonomyAuthorityCurrentAsync(authority, transaction, cancellation),
            publications, deploymentInventory, consumerSources, measurementReceipts, clock, null);
    }

    private async Task<bool> ClassificationTaxonomyAuthorityCurrentAsync(
        ModerationSourceAuthority authority, IDbContextTransaction transaction, CancellationToken token)
    {
        if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || machine.IsMachineCaller
            || await admin.ResolveUserIdAsync(token) != authority.UserId
            || !await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                && row.Revision == authority.Epoch.Revision, token)) return false;
        var reader = new ModerationConstitutionSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock);
        return await reader.AdministratorAsync(authority, authority.UserId, authority.Tenant, token)
            && ReferenceEquals(context.Database.CurrentTransaction, transaction);
    }

    private ModerationClassificationTaxonomySourceOwner ClassificationTaxonomyOwner() =>
        new(context, new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock);

    private async Task<QualifiedSource> RecordClassificationTaxonomyAsync(RecordModerationClassificationTaxonomySource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var live = await ClassificationTaxonomyOwner().ObservePrerequisitesAsync(session, session.TransactionId,
            request.ProfileId, request.Mode, request.Mandate, now, token);
        if (live is null || request.ValidUntil > live.ValidUntil || request.Mandate.OwnerHumanId != authority.UserId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationClassificationTaxonomySourceOwner.Slot(authority, request.TaxonomyId), token);
        var previous = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                && row.TaxonomyId == request.TaxonomyId).OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
        if ((previous?.WriterGeneration ?? 0) != request.ExpectedTaxonomyGeneration)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationTaxonomySource>(context, request.SourceId, token);
        if (await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        foreach (var category in request.Categories)
        {
            category.Validate(live.Age.DebateMinimumAge);
            // Stable lookup/code identity across every historical generation, even if a category
            // was omitted from the latest snapshot. Meanings/floors change only in new versions.
            if (await (from item in context.Set<ModerationClassificationTaxonomyCategory>().AsNoTracking()
                       join retained in context.Set<ModerationClassificationTaxonomySource>().AsNoTracking() on item.SourceId equals retained.Id
                       where retained.HostBindingId == authority.Binding.Id && retained.TenantId == authority.Tenant.Id
                           && retained.TaxonomyId == request.TaxonomyId
                           && (item.CategoryId == category.CategoryId && item.Code != category.Code
                               || item.Code == category.Code && item.CategoryId != category.CategoryId)
                       select item.Id).AnyAsync(token))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        }
        var source = ModerationClassificationTaxonomySource.Record(request.SourceId, request.TaxonomyId,
            checked((previous?.TaxonomyRevision ?? 0) + 1), request.ExpectedTaxonomyGeneration,
            live.Age, live.AgeAdoption, live.Governance, live.GovernanceAdoption, request.Mandate, request.Categories,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationClassificationTaxonomySource>().Add(source);
        return new(ModerationQualificationSourceKind.ClassificationTaxonomySource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptClassificationTaxonomyAsync(AdoptModerationClassificationTaxonomySource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .SingleAsync(row => row.Id == request.SourceId, token);
        var source = await ClassificationTaxonomyOwner().AcquireSourceAsync(session, session.TransactionId,
            discovered.TaxonomyId, discovered.ProfileId, (ModerationNativeMode)discovered.ModeCode, now, token);
        if (source?.Id != request.SourceId) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationTaxonomyAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationClassificationTaxonomyAdoption.Adopt(request.AdoptionId, source, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationClassificationTaxonomyAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.ClassificationTaxonomyAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawClassificationTaxonomyAsync(WithdrawModerationClassificationTaxonomyAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .SingleAsync(row => row.Id == discovered.SourceId, token);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationClassificationTaxonomySourceOwner.Slot(authority, source.TaxonomyId), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationTaxonomyAdoption>(context, request.AdoptionId, token);
        var adoption = await context.Set<ModerationClassificationTaxonomyAdoption>().SingleAsync(row => row.Id == request.AdoptionId, token);
        await RequireClassificationTaxonomyProvenanceAsync(request, adoption, authority, token);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ClassificationTaxonomyAdoption, adoption.Id, adoption.Revision);
    }

    private async Task RequireClassificationTaxonomyProvenanceAsync(WithdrawModerationClassificationTaxonomyAdoption request,
        ModerationClassificationTaxonomyAdoption adoption, ModerationSourceAuthority authority, CancellationToken token)
    {
        adoption.ValidatePersistedState();
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationClassificationTaxonomySource>(context, adoption.SourceId, token);
        var source = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .Include(row => row.Categories.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == adoption.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        var definition = await context.ModerationPolicyPublications.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.DefinitionId, token);
        if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.TenantId != authority.Tenant.Id || adoption.HostBindingId != authority.Binding.Id
            || adoption.TrustDomainId != authority.Binding.TrustDomainId || adoption.TenantId != authority.Tenant.Id
            || source.Id != adoption.SourceId || source.Revision != adoption.SourceRevision
            || source.WriterGeneration != adoption.SourceWriterGeneration || adoption.AdoptedByHumanId != source.ReviewerHumanId
            || definition?.Jurisdiction != request.Target || !authority.FencedHumans.Contains(source.OwnerHumanId)
            || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // Correctors use current scoped authority and immutable provenance, never expired owner
        // permissions. Withdrawal cannot restore categories, collect PII or rewrite birth ownership.
        await RequireTargetSourceReceiptAsync(ModerationClassificationTaxonomySourceOwner.RecordRequest(source, request.Target),
            ModerationQualificationSourceKind.ClassificationTaxonomySource, source.Id, 1, source.OwnerHumanId,
            source.WriterGeneration, source.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationClassificationTaxonomySourceOwner.AdoptionRequest(adoption, request.Target),
            ModerationQualificationSourceKind.ClassificationTaxonomyAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
            adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
    }

    private async Task VerifyClassificationTaxonomyWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsClassificationTaxonomyMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !await ClassificationTaxonomyAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationClassificationTaxonomyAdoption withdrawal)
        {
            var current = await new ModerationQualificationAuthority(context, admin, machine).AcquireAsync(request, token);
            var row = await context.Set<ModerationClassificationTaxonomyAdoption>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == withdrawal.AdoptionId, token);
            if (current is null || current.UserId != authority.UserId || current.Binding.Id != authority.Binding.Id
                || current.Tenant != authority.Tenant || current.Epoch.Revision != authority.Epoch.Revision
                || row is null || row.Revision != withdrawal.ExpectedAdoptionRevision + 1
                || row.WithdrawnAt is null || row.WithdrawnByHumanId != authority.UserId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireClassificationTaxonomyProvenanceAsync(withdrawal, row, authority, token);
            var receipt = await ReadReceiptAsync(request.OperationId, token);
            if (receipt is null || receipt.SourceId != row.Id || receipt.ResultRevision != row.Revision
                || receipt.PrincipalId != authority.UserId || receipt.CompletedAt != row.WithdrawnAt)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireTargetSourceReceiptAsync(request, ModerationQualificationSourceKind.ClassificationTaxonomyAdoption,
                row.Id, row.Revision, authority.UserId, receipt.QualificationRevision, row.WithdrawnAt.Value, authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var sourceId = request is RecordModerationClassificationTaxonomySource record ? record.SourceId
            : ((AdoptModerationClassificationTaxonomySource)request).SourceId;
        var source = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == sourceId, token);
        if (source is null || (await ClassificationTaxonomyOwner().AcquireSourceAsync(session, session.TransactionId,
            source.TaxonomyId, source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token))?.Id != sourceId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AdoptModerationClassificationTaxonomySource adopt
            && (await ClassificationTaxonomyOwner().AcquireCurrentAsync(session, session.TransactionId, source.TaxonomyId,
                source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token))?.Adoption.Id != adopt.AdoptionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task VerifyClassificationTaxonomyBeforeSaveAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsClassificationTaxonomyMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !await ClassificationTaxonomyAuthorityCurrentAsync(authority, transaction, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationClassificationTaxonomyAdoption withdrawal)
        {
            var retained = context.ChangeTracker.Entries<ModerationClassificationTaxonomyAdoption>()
                .Single(row => row.Entity.Id == withdrawal.AdoptionId).Entity;
            await RequireClassificationTaxonomyProvenanceAsync(withdrawal, retained, authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is RecordModerationClassificationTaxonomySource record)
        {
            var retained = context.ChangeTracker.Entries<ModerationClassificationTaxonomySource>()
                .Single(row => row.Entity.Id == record.SourceId).Entity;
            var live = await ClassificationTaxonomyOwner().ObservePrerequisitesAsync(session, session.TransactionId,
                record.ProfileId, record.Mode, record.Mandate, clock.GetUtcNow(), token);
            if (live is null || !retained.MatchesPrerequisites(live.Age, live.AgeAdoption,
                live.Governance, live.GovernanceAdoption, clock.GetUtcNow()))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        else
        {
            var adopt = (AdoptModerationClassificationTaxonomySource)request;
            var retained = context.ChangeTracker.Entries<ModerationClassificationTaxonomyAdoption>()
                .Single(row => row.Entity.Id == adopt.AdoptionId).Entity;
            var source = await context.Set<ModerationClassificationTaxonomySource>().AsNoTracking()
                .SingleAsync(row => row.Id == adopt.SourceId, token);
            var live = await ClassificationTaxonomyOwner().AcquireSourceAsync(session, session.TransactionId,
                source.TaxonomyId, source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token);
            if (live is null || !retained.MatchesCurrent(live, clock.GetUtcNow()))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
    }

    private async Task VerifyClassificationTaxonomyAtCommitAsync(ModerationQualificationMutation request,
        ModerationSourceAuthority authority, IDbContextTransaction transaction, CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || !ReferenceEquals(context.Database.CurrentTransaction, transaction))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        // Fresh clean session, same original transaction: retained observations do not survive
        // final identity-fence flushes, source replacement, role loss or deadline changes.
        using var current = await AcquireClassificationTaxonomyWriteAsync(request, authority, clock.GetUtcNow(), token);
        await VerifyClassificationTaxonomyWriteAsync(request, current, authority, token);
    }
}
