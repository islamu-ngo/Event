using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<ModerationConsumerEvidenceSession?> AcquireConstitutionWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request is not (RecordModerationConstitutionSource or AdoptModerationConstitutionSource)) return null;
        if (deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var owner = new ModerationConstitutionSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock),
            clock);
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
        ModerationConstitutionSource? retained = null;
        if (request is AdoptModerationConstitutionSource adopting)
        {
            retained = await context.Set<ModerationConstitutionSource>().AsNoTracking()
                .Include(row => row.Reviewers.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == adopting.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            retained.ValidatePersistedState();
            if (retained.HostBindingId != authority.Binding.Id || retained.TrustDomainId != authority.Binding.TrustDomainId
                || retained.TenantId != authority.Tenant.Id || retained.RecordedByHumanId == authority.UserId
                || retained.Reviewers.Any(row => row.ReviewerHumanId == authority.UserId || row.ControllerHumanId == authority.UserId)
                || retained.Revision != adopting.ExpectedSourceRevision || retained.WriterGeneration != adopting.ExpectedSourceWriterGeneration
                || ModerationConstitutionSourceOwner.OriginalAnchor(retained) != adopting.OriginalEffect
                || adopting.ValidUntil <= now || adopting.ValidUntil > retained.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var record = request as RecordModerationConstitutionSource;
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var anchor = OriginalConstitutionAnchor(request);
        var original = anchor is not null ? await ModerationOriginalRecipeAuthority.AcquireAsync(
            context, authority, transaction.TransactionId, anchor, privacy, clock, token) : null;
        if (anchor is not null && (original is null || request.Target != original.Policy.Publication.Jurisdiction
            || (record?.PublicationId ?? retained!.PublicationId) != original.Policy.Publication.PublicationId
            || (record?.PolicyId ?? retained!.PolicyId) != original.Policy.Publication.PolicyId
            || (record?.ProfileId ?? retained!.ProfileId) != original.Policy.Profiles
                .Single(row => row.Definition.Rule == original.Binding.Rule).Definition.Id
            || (record?.Mode ?? (ModerationNativeMode)retained!.ModeCode) != original.Binding.Mode
            || (record?.ValidUntil ?? ((AdoptModerationConstitutionSource)request).ValidUntil) > original.ValidUntil))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var definition = original is null
            ? await definitions.ReadDefinitionAsync(authority, transaction.TransactionId, request.Target,
                record?.PolicyId ?? retained!.PolicyId, record?.PublicationId ?? retained!.PublicationId, now, token)
            : new ModerationPolicyDefinitionSourceOwner.Definition(original.Policy, original.DefinitionId, original.ValidUntil);
        if (definition is null || !await Current(token)) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (record is not null && (record.ExpectedPolicyRevision != definition.Policy.Publication.ProposedRevision
            || record.ValidUntil <= now || record.ValidUntil > definition.ValidUntil))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var session = new ModerationConsumerEvidenceSession(context, transaction, authority, definition.Policy,
            Current, publications, deploymentInventory, consumerSources, measurementReceipts, clock, null, original);
        try
        {
            if (retained is not null)
            {
                var current = await owner.AcquireSourceAsync(session, session.TransactionId,
                    retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token);
                if (current?.Source.Id != retained.Id) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task<QualifiedSource> RecordConstitutionAsync(RecordModerationConstitutionSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var owner = new ModerationConstitutionSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock),
            clock);
        var live = await owner.ObserveAsync(session, session.TransactionId, request.ProfileId, request.Mode,
            request.Terms, request.Reviewers, authority.UserId, now, token);
        if (live is null || request.ValidUntil > live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationConstitutionSourceOwner.Slot(authority, request.PublicationId, request.ProfileId, request.Mode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionSource>(context, request.SourceId, token);
        if (await context.Set<ModerationConstitutionSource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var native = await session.AcquireNativeAsync(session.TransactionId, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var source = ModerationConstitutionSource.Record(request.SourceId, authority.Binding.Id, authority.Binding.TrustDomainId,
            authority.Tenant.Id, request.PolicyId, request.ExpectedPolicyRevision, request.PublicationId, live.DefinitionId,
            request.ProfileId, (int)request.Mode, ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed),
            request.Terms, request.Reviewers, authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil,
            native.OriginalRecipe?.Anchor.EffectId, native.OriginalRecipe?.Anchor.SnapshotHash,
            native.OriginalRecipe?.Anchor.EffectBindingHash, native.OriginalRecipe?.ValidUntil);
        context.Set<ModerationConstitutionSource>().Add(source);
        return new(ModerationQualificationSourceKind.ConstitutionSource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptConstitutionAsync(AdoptModerationConstitutionSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationConstitutionSource>().AsNoTracking()
            .SingleAsync(row => row.Id == request.SourceId, token);
        var owner = new ModerationConstitutionSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock),
            clock);
        var current = await owner.AcquireSourceAsync(session, session.TransactionId, discovered.ProfileId,
            (ModerationNativeMode)discovered.ModeCode, now, token);
        if (current?.Source.Id != request.SourceId) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationConstitutionAdoption.Adopt(request.AdoptionId, current.Source, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationConstitutionAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.ConstitutionAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawConstitutionAsync(WithdrawModerationConstitutionAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationConstitutionAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationConstitutionSource>().AsNoTracking()
            .Include(row => row.Reviewers.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == discovered.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationConstitutionSourceOwner.Slot(authority, source.PublicationId, source.ProfileId, (ModerationNativeMode)source.ModeCode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionAdoption>(context, request.AdoptionId, token);
        var adoption = await context.Set<ModerationConstitutionAdoption>().SingleAsync(row => row.Id == request.AdoptionId, token);
        adoption.ValidatePersistedState();
        var definition = await context.ModerationPolicyPublications.AsNoTracking().SingleOrDefaultAsync(row => row.Id == source.DefinitionId, token);
        if (adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != authority.Binding.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || definition?.Jurisdiction != request.Target
            || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RequireTargetSourceReceiptAsync(ModerationConstitutionSourceOwner.AdoptionRequest(adoption, request.Target, source),
            ModerationQualificationSourceKind.ConstitutionAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
            adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ConstitutionAdoption, adoption.Id, adoption.Revision);
    }

    private async Task<QualifiedSource> RecordConstitutionAvailabilityAsync(RecordModerationConstitutionAvailability request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (request.Target != authority.Tenant || request.ValidUntil <= now)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationConstitutionSourceOwner.AvailabilitySlot(authority, authority.UserId), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionAvailability>(context, request.AvailabilityId, token);
        if (await context.Set<ModerationConstitutionAvailability>().AsNoTracking().AnyAsync(row => row.Id == request.AvailabilityId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var row = ModerationConstitutionAvailability.Record(request.AvailabilityId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, authority.UserId, checked(authority.Epoch.Revision + 1),
            request.MaximumCaseload, now, request.ValidUntil);
        context.Set<ModerationConstitutionAvailability>().Add(row);
        return new(ModerationQualificationSourceKind.ConstitutionAvailability, row.Id, 1, MutationValidUntil: row.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawConstitutionAvailabilityAsync(WithdrawModerationConstitutionAvailability request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationConstitutionAvailability>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AvailabilityId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationConstitutionSourceOwner.AvailabilitySlot(authority, discovered.ReviewerHumanId), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConstitutionAvailability>(context, request.AvailabilityId, token);
        var row = await context.Set<ModerationConstitutionAvailability>().SingleAsync(item => item.Id == request.AvailabilityId, token);
        row.ValidatePersistedState();
        if (request.Target != authority.Tenant || row.HostBindingId != authority.Binding.Id
            || row.TrustDomainId != authority.Binding.TrustDomainId || row.TenantId != authority.Tenant.Id
            || !authority.FencedHumans.Contains(row.ReviewerHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RequireTargetSourceReceiptAsync(ModerationConstitutionSourceOwner.AvailabilityRequest(row, authority.Tenant),
            ModerationQualificationSourceKind.ConstitutionAvailability, row.Id, 1, row.ReviewerHumanId,
            row.WriterGeneration, row.RecordedAt, authority, token);
        if (row.Revision != request.ExpectedAvailabilityRevision || row.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        row.Withdraw(request.ExpectedAvailabilityRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ConstitutionAvailability, row.Id, row.Revision);
    }

    private async Task VerifyConstitutionWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is not (RecordModerationConstitutionSource or AdoptModerationConstitutionSource
            or WithdrawModerationConstitutionAdoption or RecordModerationConstitutionAvailability
            or WithdrawModerationConstitutionAvailability)) return;
        var reader = new ModerationQualificationAuthority(context, admin, machine);
        if (machine.IsMachineCaller || await admin.ResolveUserIdAsync(token) != authority.UserId
            || !await reader.ProviderTenantMatchesAsync(authority.Tenant.Id, token)
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        // Terminal withdrawals do not depend on the expired/replaced definition, staffing or adoption.
        if (request is WithdrawModerationConstitutionAdoption or WithdrawModerationConstitutionAvailability)
        {
            var current = await reader.AcquireAsync(request, token);
            if (current is null || current.UserId != authority.UserId || current.Binding.Id != authority.Binding.Id
                || current.Tenant != authority.Tenant || current.Epoch.Id != authority.Epoch.Id
                || current.Epoch.Revision != authority.Epoch.Revision)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
        if (request is WithdrawModerationConstitutionAdoption withdrawal)
        {
            var row = await context.Set<ModerationConstitutionAdoption>().AsNoTracking().SingleOrDefaultAsync(item => item.Id == withdrawal.AdoptionId, token);
            if (row is null || row.Revision != withdrawal.ExpectedAdoptionRevision + 1 || row.WithdrawnAt is null
                || row.WithdrawnByHumanId != authority.UserId || row.HostBindingId != authority.Binding.Id || row.TenantId != authority.Tenant.Id)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (request is WithdrawModerationConstitutionAvailability withdrawalAvailability)
        {
            var row = await context.Set<ModerationConstitutionAvailability>().AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == withdrawalAvailability.AvailabilityId, token);
            if (row is null || row.Revision != withdrawalAvailability.ExpectedAvailabilityRevision + 1 || row.WithdrawnAt is null
                || row.WithdrawnByHumanId != authority.UserId || row.HostBindingId != authority.Binding.Id || row.TenantId != authority.Tenant.Id)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (deploymentInventory is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var owner = new ModerationConstitutionSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock),
            clock);
        if (!await owner.AdministratorAsync(authority, authority.UserId, request.Target, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is RecordModerationConstitutionAvailability availability)
        {
            if (await owner.ReadAvailabilityAsync(authority, availability.AvailabilityId, clock.GetUtcNow(), token) is null)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (session is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var id = request is RecordModerationConstitutionSource record ? record.SourceId : ((AdoptModerationConstitutionSource)request).SourceId;
        var source = await context.Set<ModerationConstitutionSource>().AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, token);
        if (source is null || (await owner.AcquireSourceAsync(session, session.TransactionId,
            source.ProfileId, (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token))?.Source.Id != id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AdoptModerationConstitutionSource adopt
            && (await owner.ReadAdoptionAsync(session, session.TransactionId, source.ProfileId,
                (ModerationNativeMode)source.ModeCode, clock.GetUtcNow(), token))?.Adoption.Id != adopt.AdoptionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
