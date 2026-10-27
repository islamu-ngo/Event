using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private static bool IsProfileCollectionProcedureMutation(ModerationQualificationMutation request) =>
        request is RecordModerationProfileCollectionProcedureSource or AdoptModerationProfileCollectionProcedureSource
            or WithdrawModerationProfileCollectionProcedureAdoption;

    private ModerationProfileCollectionProcedureSourceOwner ProfileCollectionProcedureOwner() =>
        new(context, new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock);

    private async Task<bool> ProfileCollectionProcedureAuthorityCurrentAsync(
        ModerationSourceAuthority authority, IDbContextTransaction transaction, AuthorityScope target, CancellationToken token)
    {
        if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || machine.IsMachineCaller
            || await admin.ResolveUserIdAsync(token) != authority.UserId
            || !await new ModerationQualificationAuthority(context, admin, machine).ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
            || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                && row.Revision == authority.Epoch.Revision, token)) return false;
        return await new ModerationChildAgePolicySourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock)
            .AdministratorAsync(authority, authority.UserId, target, token)
            && ReferenceEquals(context.Database.CurrentTransaction, transaction);
    }

    private async Task<ModerationConsumerEvidenceSession?> AcquireProfileCollectionProcedureWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationProfileCollectionProcedureSource or AdoptModerationProfileCollectionProcedureSource)) return null;
        if (deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var transaction = context.Database.CurrentTransaction!;
        ModerationProfileCollectionProcedureSource? retained = null;
        var recording = request as RecordModerationProfileCollectionProcedureSource;
        if (request is AdoptModerationProfileCollectionProcedureSource adopting)
        {
            retained = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == adopting.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            try { retained.ValidatePersistedState(); }
            catch (ArgumentException) { throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable); }
            if (retained.ReviewerHumanId != authority.UserId || retained.RecordedByHumanId == authority.UserId
                || retained.Revision != adopting.ExpectedSourceRevision
                || retained.WriterGeneration != adopting.ExpectedSourceWriterGeneration
                || adopting.ValidUntil <= now || adopting.ValidUntil > retained.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var parent = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == (recording == null ? retained!.ChildAgeSourceId : recording.ChildAgeSourceId), token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var definition = await definitions.ReadDefinitionAsync(authority, transaction.TransactionId, request.Target,
            parent.PolicyId, parent.PublicationId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var session = new ModerationConsumerEvidenceSession(context, transaction, authority, definition.Policy,
            cancellation => ProfileCollectionProcedureAuthorityCurrentAsync(authority, transaction, request.Target, cancellation),
            publications, deploymentInventory, consumerSources, measurementReceipts, clock, null);
        try
        {
            var live = await ProfileCollectionProcedureOwner().AcquireParentsAsync(session, session.TransactionId,
                parent.ProfileId, (ModerationNativeMode)parent.ModeCode, now, token);
            if (live is null || recording is not null && (!MatchesProcedureParents(recording, live)
                || authority.UserId != live.Source.CollectionOwnerHumanId || recording.ValidUntil <= now
                || recording.ValidUntil > live.Adoption.ValidUntil || recording.ValidUntil > definition.ValidUntil))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            if (retained is not null && (await ProfileCollectionProcedureOwner().AcquireSourceAsync(session, session.TransactionId,
                retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token))?.Id != retained.Id)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return session;
        }
        catch { session.Dispose(); throw; }
    }

    private static bool MatchesProcedureParents(RecordModerationProfileCollectionProcedureSource request,
        ModerationProfileCollectionProcedureSourceOwner.ProcedureParents parents) =>
        request.ChildAgeSourceId == parents.Source.Id && request.ExpectedChildAgeSourceRevision == parents.Source.Revision
        && request.ExpectedChildAgeSourceWriterGeneration == parents.Source.WriterGeneration
        && request.ChildAgeAdoptionId == parents.Adoption.Id && request.ExpectedChildAgeAdoptionRevision == parents.Adoption.Revision
        && request.ExpectedChildAgeAdoptionWriterGeneration == parents.Adoption.WriterGeneration;

    private async Task<QualifiedSource> RecordProfileCollectionProcedureAsync(RecordModerationProfileCollectionProcedureSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var parent = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .SingleAsync(row => row.Id == request.ChildAgeSourceId, token);
        var live = await ProfileCollectionProcedureOwner().AcquireParentsAsync(session, session.TransactionId,
            parent.ProfileId, (ModerationNativeMode)parent.ModeCode, now, token);
        if (live is null || !MatchesProcedureParents(request, live) || authority.UserId != live.Source.CollectionOwnerHumanId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationProfileCollectionProcedureSourceOwner.Slot(authority, parent.PublicationId, parent.ProfileId,
                (ModerationNativeMode)parent.ModeCode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureSource>(context, request.SourceId, token);
        if (await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var source = ModerationProfileCollectionProcedureSource.Record(request.SourceId, live.Source, live.Adoption,
            request.Procedure, authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationProfileCollectionProcedureSource>().Add(source);
        return new(ModerationQualificationSourceKind.ProfileCollectionProcedureSource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptProfileCollectionProcedureAsync(AdoptModerationProfileCollectionProcedureSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var retained = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .SingleAsync(row => row.Id == request.SourceId, token);
        var source = await ProfileCollectionProcedureOwner().AcquireSourceAsync(session, session.TransactionId,
            retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token);
        var live = await ProfileCollectionProcedureOwner().AcquireParentsAsync(session, session.TransactionId,
            retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token);
        if (source?.Id != request.SourceId || live is null || source.Revision != request.ExpectedSourceRevision
            || source.WriterGeneration != request.ExpectedSourceWriterGeneration || authority.UserId != source.ReviewerHumanId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationProfileCollectionProcedureAdoption.Adopt(request.AdoptionId, source, live.Source, live.Adoption,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Attach(source);
        context.Set<ModerationProfileCollectionProcedureAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawProfileCollectionProcedureAsync(WithdrawModerationProfileCollectionProcedureAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
            .Include(row => row.Source).SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationProfileCollectionProcedureSourceOwner.Slot(authority, discovered.Source.PublicationId,
                discovered.Source.ProfileId, (ModerationNativeMode)discovered.Source.ModeCode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureAdoption>(context, request.AdoptionId, token);
        var adoption = await context.Set<ModerationProfileCollectionProcedureAdoption>()
            .Include(row => row.Source).SingleAsync(row => row.Id == request.AdoptionId, token);
        await RequireProcedureProvenanceAsync(request, adoption, authority, token);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption, adoption.Id, adoption.Revision);
    }

    private async Task RequireProcedureProvenanceAsync(WithdrawModerationProfileCollectionProcedureAdoption request,
        ModerationProfileCollectionProcedureAdoption adoption, ModerationSourceAuthority authority, CancellationToken token)
    {
        try { adoption.ValidatePersistedState(); }
        catch (ArgumentException) { throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable); }
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationProfileCollectionProcedureSource>(context, adoption.SourceId, token);
        var source = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == adoption.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        try { source.ValidatePersistedState(); }
        catch (ArgumentException) { throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable); }
        var definition = await context.ModerationPolicyPublications.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.DefinitionId, token);
        if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.TenantId != authority.Tenant.Id || adoption.HostBindingId != source.HostBindingId
            || adoption.TrustDomainId != source.TrustDomainId || adoption.TenantId != source.TenantId
            || adoption.SourceId != source.Id || adoption.SourceRevision != source.Revision
            || adoption.SourceWriterGeneration != source.WriterGeneration
            || adoption.AdoptedByHumanId != source.ReviewerHumanId || source.RecordedByHumanId != source.OwnerHumanId
            || definition?.Jurisdiction != request.Target
            || !authority.FencedHumans.Contains(source.OwnerHumanId) || !authority.FencedHumans.Contains(source.ReviewerHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // Corrective withdrawal uses retained request-bound provenance, not expired mandates.
        await RequireTargetSourceReceiptAsync(ModerationProfileCollectionProcedureSourceOwner.RecordRequest(source, request.Target),
            ModerationQualificationSourceKind.ProfileCollectionProcedureSource, source.Id, 1, source.RecordedByHumanId,
            source.WriterGeneration, source.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationProfileCollectionProcedureSourceOwner.AdoptionRequest(adoption, request.Target),
            ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
            adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationChildAgePolicySource>(context, source.ChildAgeSourceId, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationChildAgePolicyAdoption>(context, source.ChildAgeAdoptionId, token);
        var parent = await context.Set<ModerationChildAgePolicySource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.ChildAgeSourceId, token);
        var parentAdoption = await context.Set<ModerationChildAgePolicyAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.ChildAgeAdoptionId, token);
        if (parent is null || parentAdoption is null || parentAdoption.SourceId != parent.Id
            || parent.Revision != source.ChildAgeSourceRevision || parent.WriterGeneration != source.ChildAgeSourceWriterGeneration
            || parent.RecordedByHumanId != source.ChildAgeRecordedByHumanId || parent.RecordedAt != source.ChildAgeRecordedAt
            || parent.ValidUntil != source.ChildAgeValidUntil || parent.Terms != source.ChildAgeTerms
            || parent.Ownership != source.ChildAgeOwnership || parentAdoption.SourceRevision != parent.Revision
            || parentAdoption.SourceWriterGeneration != parent.WriterGeneration
            || parentAdoption.WriterGeneration != source.ChildAgeAdoptionWriterGeneration
            || parentAdoption.AdoptedAt != source.ChildAgeAdoptedAt || parentAdoption.ValidUntil != source.ChildAgeAdoptionValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        // Parent withdrawal/expiry/supersession closes acquisition but not accountable correction.
        await RequireTargetSourceReceiptAsync(ModerationChildAgePolicySourceOwner.RecordRequest(parent, request.Target),
            ModerationQualificationSourceKind.ChildAgePolicySource, parent.Id, 1, parent.RecordedByHumanId,
            parent.WriterGeneration, parent.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationChildAgePolicySourceOwner.AdoptionRequest(parentAdoption, request.Target),
            ModerationQualificationSourceKind.ChildAgePolicyAdoption, parentAdoption.Id, 1, parentAdoption.AdoptedByHumanId,
            parentAdoption.WriterGeneration, parentAdoption.AdoptedAt, authority, token);
    }

    private async Task VerifyProfileCollectionProcedureBeforeSaveAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsProfileCollectionProcedureMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !await ProfileCollectionProcedureAuthorityCurrentAsync(authority, transaction, request.Target, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationProfileCollectionProcedureAdoption withdrawal)
        {
            await RequireProcedureProvenanceAsync(withdrawal, context.ChangeTracker.Entries<ModerationProfileCollectionProcedureAdoption>()
                .Single(row => row.Entity.Id == withdrawal.AdoptionId).Entity, authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var source = request is RecordModerationProfileCollectionProcedureSource record
            ? context.ChangeTracker.Entries<ModerationProfileCollectionProcedureSource>().Single(row => row.Entity.Id == record.SourceId).Entity
            : await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
                .SingleAsync(row => row.Id == ((AdoptModerationProfileCollectionProcedureSource)request).SourceId, token);
        var live = await ProfileCollectionProcedureOwner().AcquireParentsAsync(session, session.TransactionId,
            source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token);
        if (live is null || !source.MatchesCurrent(live.Source, live.Adoption, clock.GetUtcNow()))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AdoptModerationProfileCollectionProcedureSource adopt
            && !context.ChangeTracker.Entries<ModerationProfileCollectionProcedureAdoption>()
                .Single(row => row.Entity.Id == adopt.AdoptionId).Entity.MatchesCurrent(source, live.Source, live.Adoption, clock.GetUtcNow()))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task VerifyProfileCollectionProcedureWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (!IsProfileCollectionProcedureMutation(request)) return;
        var transaction = context.Database.CurrentTransaction;
        if (transaction is null || !await ProfileCollectionProcedureAuthorityCurrentAsync(authority, transaction, request.Target, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationProfileCollectionProcedureAdoption withdrawal)
        {
            var row = await context.Set<ModerationProfileCollectionProcedureAdoption>().AsNoTracking()
                .Include(item => item.Source).SingleOrDefaultAsync(item => item.Id == withdrawal.AdoptionId, token);
            if (row is null || row.Revision != withdrawal.ExpectedAdoptionRevision + 1
                || row.WithdrawnAt is null || row.WithdrawnByHumanId != authority.UserId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireProcedureProvenanceAsync(withdrawal, row, authority, token);
            var receipt = await ReadReceiptAsync(request.OperationId, token);
            if (receipt is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireTargetSourceReceiptAsync(request, ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption,
                row.Id, row.Revision, authority.UserId, receipt.QualificationRevision, row.WithdrawnAt.Value, authority, token);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var id = request is RecordModerationProfileCollectionProcedureSource recording ? recording.SourceId
            : ((AdoptModerationProfileCollectionProcedureSource)request).SourceId;
        var retained = await context.Set<ModerationProfileCollectionProcedureSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (retained is null || (await ProfileCollectionProcedureOwner().AcquireSourceAsync(session, session.TransactionId,
            retained.ProfileId, (ModerationNativeMode)retained.ModeCode, clock.GetUtcNow(), token))?.Id != id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is RecordModerationProfileCollectionProcedureSource record)
        {
            await RequireTargetSourceReceiptAsync(record, ModerationQualificationSourceKind.ProfileCollectionProcedureSource,
                retained.Id, retained.Revision, authority.UserId, retained.WriterGeneration, retained.RecordedAt, authority, token);
            if (clock.GetUtcNow() >= retained.ValidUntil || clock.GetUtcNow() >= retained.ChildAgeValidUntil
                || clock.GetUtcNow() >= retained.ChildAgeAdoptionValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        if (request is AdoptModerationProfileCollectionProcedureSource adopt)
        {
            var live = await ProfileCollectionProcedureOwner().AcquireCurrentAsync(session, session.TransactionId,
                retained.ProfileId, (ModerationNativeMode)retained.ModeCode, clock.GetUtcNow(), token);
            if (live?.Adoption.Id != adopt.AdoptionId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireTargetSourceReceiptAsync(adopt, ModerationQualificationSourceKind.ProfileCollectionProcedureAdoption,
                live.Adoption.Id, live.Adoption.Revision, authority.UserId, live.Adoption.WriterGeneration,
                live.Adoption.AdoptedAt, authority, token);
            if (clock.GetUtcNow() >= live.ValidUntil || clock.GetUtcNow() >= retained.ChildAgeValidUntil
                || clock.GetUtcNow() >= retained.ChildAgeAdoptionValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
    }

    private async Task VerifyProfileCollectionProcedureAtCommitAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? original, ModerationSourceAuthority authority,
        IDbContextTransaction transaction, CancellationToken token)
    {
        if (context.ChangeTracker.HasChanges() || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || original is not null && original.TransactionId != transaction.TransactionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        // Reread on the original session: never refresh and replace the observed policy intent.
        await VerifyProfileCollectionProcedureWriteAsync(request, original, authority, token);
    }
}
