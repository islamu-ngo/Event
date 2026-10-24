using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<ModerationConsumerEvidenceSession?> AcquireRecognitionPrivacyWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationRecognitionPrivacySource or AdoptModerationRecognitionPrivacySource)) return null;
        if (deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var owner = new ModerationRecognitionPrivacySourceOwner(context, definitions, clock);
        var transaction = context.Database.CurrentTransaction!;
        async Task<bool> Current(CancellationToken cancellation)
        {
            if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || machine.IsMachineCaller
                || await admin.ResolveUserIdAsync(cancellation) != authority.UserId
                || !await new ModerationQualificationAuthority(context, admin, machine)
                    .ProviderTenantMatchesAsync(authority.Tenant.Id, cancellation)
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
                || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                    && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                    && row.Revision == authority.Epoch.Revision, cancellation)) return false;
            foreach (var human in authority.Administrators)
                if (!await owner.AdministratorAsync(authority, human, request.Target, cancellation)) return false;
            return ReferenceEquals(context.Database.CurrentTransaction, transaction);
        }
        if (!await Current(token)) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        ModerationRecognitionPrivacySource? retained = null;
        if (request is AdoptModerationRecognitionPrivacySource adopting)
        {
            retained = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking().Include(row => row.Retention)
                .SingleOrDefaultAsync(row => row.Id == adopting.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            retained.ValidatePersistedState();
            if (retained.HostBindingId != authority.Binding.Id || retained.TrustDomainId != authority.Binding.TrustDomainId
                || retained.TenantId != authority.Tenant.Id || retained.PrivacyOwnerHumanId != authority.UserId
                || retained.RecordedByHumanId == authority.UserId || retained.Revision != adopting.ExpectedSourceRevision
                || retained.WriterGeneration != adopting.ExpectedSourceWriterGeneration
                || adopting.ValidUntil <= now || adopting.ValidUntil > retained.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var recording = request as RecordModerationRecognitionPrivacySource;
        var definition = await definitions.ReadDefinitionAsync(authority, transaction.TransactionId, request.Target,
            recording?.PolicyId ?? retained!.PolicyId, recording?.PublicationId ?? retained!.PublicationId, now, token);
        if (definition is null || !await Current(token)) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (recording is not null && (recording.ExpectedPolicyRevision != definition.Policy.Publication.ProposedRevision
            || recording.ValidUntil <= now || recording.ValidUntil > definition.ValidUntil))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var session = new ModerationConsumerEvidenceSession(context, transaction, authority, definition.Policy,
            Current, publications, deploymentInventory, consumerSources, measurementReceipts, clock, null);
        try
        {
            if (retained is not null && (await owner.AcquireSourceAsync(session, session.TransactionId,
                retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token))?.Source.Id != retained.Id)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task<QualifiedSource> RecordRecognitionPrivacyAsync(RecordModerationRecognitionPrivacySource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        if (request.Retention.IsDefaultOrEmpty || !request.Retention.Select(row => row.RecordClass)
            .SequenceEqual(Enum.GetValues<ModerationRecognitionPrivacyRecordClass>()))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var owner = new ModerationRecognitionPrivacySourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock);
        var live = await owner.ObserveAsync(session, session.TransactionId, request.ProfileId, request.Mode,
            request.Terms, request.Ownership, authority.UserId, now, token);
        if (live is null || request.ValidUntil > live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationRecognitionPrivacySourceOwner.Slot(authority, request.PublicationId, request.ProfileId, request.Mode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacySource>(context, request.SourceId, token);
        if (await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var native = await session.AcquireNativeAsync(session.TransactionId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var source = ModerationRecognitionPrivacySource.Record(request.SourceId, authority.Binding.Id, authority.Binding.TrustDomainId,
            authority.Tenant.Id, request.PolicyId, request.ExpectedPolicyRevision, request.PublicationId, live.DefinitionId,
            request.ProfileId, (int)request.Mode, ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed),
            request.Terms, request.Ownership, request.Retention, authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationRecognitionPrivacySource>().Add(source);
        return new(ModerationQualificationSourceKind.RecognitionPrivacySource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptRecognitionPrivacyAsync(AdoptModerationRecognitionPrivacySource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking()
            .SingleAsync(row => row.Id == request.SourceId, token);
        var owner = new ModerationRecognitionPrivacySourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock), clock);
        var current = await owner.AcquireSourceAsync(session, session.TransactionId, discovered.ProfileId,
            (ModerationNativeMode)discovered.ModeCode, now, token);
        if (current?.Source.Id != request.SourceId) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacyAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationRecognitionPrivacyAdoption.Adopt(request.AdoptionId, current.Source, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationRecognitionPrivacyAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawRecognitionPrivacyAsync(WithdrawModerationRecognitionPrivacyAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking().Include(row => row.Retention)
            .SingleOrDefaultAsync(row => row.Id == discovered.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationRecognitionPrivacySourceOwner.Slot(authority, source.PublicationId, source.ProfileId, (ModerationNativeMode)source.ModeCode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationRecognitionPrivacyAdoption>(context, request.AdoptionId, token);
        var adoption = await context.Set<ModerationRecognitionPrivacyAdoption>().SingleAsync(row => row.Id == request.AdoptionId, token);
        adoption.ValidatePersistedState();
        var definition = await context.ModerationPolicyPublications.AsNoTracking().SingleOrDefaultAsync(row => row.Id == source.DefinitionId, token);
        if (source.HostBindingId != authority.Binding.Id || source.TrustDomainId != authority.Binding.TrustDomainId
            || source.TenantId != authority.Tenant.Id || adoption.HostBindingId != authority.Binding.Id
            || adoption.TrustDomainId != authority.Binding.TrustDomainId || adoption.TenantId != authority.Tenant.Id
            || adoption.SourceId != source.Id || definition?.Jurisdiction != request.Target
            || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        // Verify provenance without requiring an unexpired source, current selection or departed owner's role.
        await RequireTargetSourceReceiptAsync(ModerationRecognitionPrivacySourceOwner.RecordRequest(source, request.Target),
            ModerationQualificationSourceKind.RecognitionPrivacySource, source.Id, 1, source.RecordedByHumanId,
            source.WriterGeneration, source.RecordedAt, authority, token);
        await RequireTargetSourceReceiptAsync(ModerationRecognitionPrivacySourceOwner.AdoptionRequest(adoption, request.Target),
            ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
            adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.RecognitionPrivacyAdoption, adoption.Id, adoption.Revision);
    }

    private async Task VerifyRecognitionPrivacyWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is not (RecordModerationRecognitionPrivacySource or AdoptModerationRecognitionPrivacySource
            or WithdrawModerationRecognitionPrivacyAdoption)) return;
        var reader = new ModerationQualificationAuthority(context, admin, machine);
        if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) != authority.UserId
            || !await reader.ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is WithdrawModerationRecognitionPrivacyAdoption withdrawal)
        {
            var current = await reader.AcquireAsync(request, token);
            if (current is null || current.UserId != authority.UserId || current.Binding.Id != authority.Binding.Id
                || current.Tenant != authority.Tenant || current.Epoch.Id != authority.Epoch.Id
                || current.Epoch.Revision != authority.Epoch.Revision)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            var row = await context.Set<ModerationRecognitionPrivacyAdoption>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == withdrawal.AdoptionId, token);
            if (row is null || row.Revision != withdrawal.ExpectedAdoptionRevision + 1 || row.WithdrawnAt is null
                || row.WithdrawnByHumanId != authority.UserId || row.HostBindingId != authority.Binding.Id
                || row.TrustDomainId != authority.Binding.TrustDomainId || row.TenantId != authority.Tenant.Id)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (session is null || deploymentInventory is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var id = request is RecordModerationRecognitionPrivacySource record ? record.SourceId
            : ((AdoptModerationRecognitionPrivacySource)request).SourceId;
        var retained = await context.Set<ModerationRecognitionPrivacySource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        var owner = new ModerationRecognitionPrivacySourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock), clock);
        if (retained is null || (await owner.AcquireSourceAsync(session, session.TransactionId,
            retained.ProfileId, (ModerationNativeMode)retained.ModeCode, clock.GetUtcNow(), token))?.Source.Id != id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AdoptModerationRecognitionPrivacySource adopt
            && (await owner.ReadAdoptionAsync(session, session.TransactionId, retained.ProfileId,
                (ModerationNativeMode)retained.ModeCode, clock.GetUtcNow(), token))?.Adoption.Id != adopt.AdoptionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
