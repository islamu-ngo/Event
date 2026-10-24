using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<ModerationConsumerEvidenceSession?> AcquireDeploymentWriteAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority authority,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (request is not (RecordModerationDeploymentSource or AdoptModerationDeploymentSource)) return null;
        if (deploymentInventory is null || consumerSources is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var transaction = context.Database.CurrentTransaction!;
        var authorityReader = new ModerationQualificationAuthority(context, admin, machine);
        async Task<bool> Current(CancellationToken cancellation)
        {
            if (!ReferenceEquals(context.Database.CurrentTransaction, transaction) || machine.IsMachineCaller
                || await admin.ResolveUserIdAsync(cancellation) != authority.UserId
                || !await authorityReader.ProviderTenantMatchesAsync(authority.Tenant.Id, cancellation)
                || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
                || !await context.ModerationQualificationEpochs.AsNoTracking().AnyAsync(row => row.Id == authority.Epoch.Id
                    && row.HostBindingId == authority.Binding.Id && row.TenantId == authority.Tenant.Id
                    && row.Revision == authority.Epoch.Revision, cancellation)) return false;
            var ids = authority.Administrators.ToArray();
            var platform = await context.PlatformUserRoles.AsNoTracking()
                .Where(row => ids.Contains(row.UserId) && !row.User.IsDeleted
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform && row.Role.MasterCode == "platform.admin")
                .Select(row => row.UserId).Distinct().ToListAsync(cancellation);
            var tenant = await context.TenantUserRoleGrants.AsNoTracking()
                .Where(row => ids.Contains(row.TenantUser.UserId) && row.TenantId == authority.Tenant.Id && row.RevokedAt == null
                    && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                    && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                    && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                    && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted)
                .Select(row => row.TenantUser.UserId).Distinct().ToListAsync(cancellation);
            return ids.All(id => platform.Contains(id) || request.Target == authority.Tenant && tenant.Contains(id))
                && ReferenceEquals(context.Database.CurrentTransaction, transaction);
        }
        if (!await Current(token)) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var definitions = new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory, clock);
        var owner = new ModerationDeploymentSourceOwner(context, definitions, deploymentInventory, consumerSources, clock);
        ModerationDeploymentSource? retained = null;
        if (request is AdoptModerationDeploymentSource adopting)
        {
            retained = await context.Set<ModerationDeploymentSource>().AsNoTracking()
                .Include(row => row.Consumers.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == adopting.SourceId, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
            retained.ValidatePersistedState();
            if (retained.HostBindingId != authority.Binding.Id || retained.TrustDomainId != authority.Binding.TrustDomainId
                || retained.TenantId != authority.Tenant.Id || retained.RecordedByHumanId == authority.UserId
                || retained.Revision != adopting.ExpectedSourceRevision || retained.WriterGeneration != adopting.ExpectedSourceWriterGeneration
                || adopting.ValidUntil <= observedAt || adopting.ValidUntil > retained.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        var recording = request as RecordModerationDeploymentSource;
        var publicationId = recording?.PublicationId ?? retained!.PublicationId;
        var policyId = recording?.PolicyId ?? retained!.PolicyId;
        var profileId = recording?.ProfileId ?? retained!.ProfileId;
        var mode = recording?.Mode ?? (ModerationNativeMode)retained!.ModeCode;
        var definition = await definitions.ReadDefinitionAsync(authority, transaction.TransactionId,
            request.Target, policyId, publicationId, observedAt, token);
        if (definition is null || !await Current(token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (recording is not null && (recording.ExpectedPolicyRevision != definition.Policy.Publication.ProposedRevision
            || recording.ValidUntil <= observedAt || recording.ValidUntil > definition.ValidUntil))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var session = new ModerationConsumerEvidenceSession(context, transaction, authority, definition.Policy,
            Current, publications, deploymentInventory, consumerSources, measurementReceipts, clock, null);
        try
        {
            var live = await owner.ObserveAsync(session, transaction.TransactionId, recording?.SourceId ?? retained!.Id,
                profileId, mode, observedAt, token)
                ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            if (recording is not null && recording.ValidUntil > live.ValidUntil)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
            if (retained is not null)
            {
                var source = await owner.AcquireSourceAsync(session, transaction.TransactionId, profileId, mode, observedAt, token);
                if (source is null || source.Source.Id != retained.Id)
                    throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            }
            return session;
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    private async Task<QualifiedSource> RecordDeploymentAsync(RecordModerationDeploymentSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var owner = new ModerationDeploymentSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock),
            deploymentInventory!, consumerSources!, clock);
        var live = await owner.ObserveAsync(session, session.TransactionId, request.SourceId, request.ProfileId, request.Mode, now, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request.ValidUntil > live.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationDeploymentSourceOwner.Slot(authority, request.PublicationId, request.ProfileId, request.Mode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDeploymentSource>(context, request.SourceId, token);
        if (await context.Set<ModerationDeploymentSource>().AsNoTracking().AnyAsync(row => row.Id == request.SourceId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var source = ModerationDeploymentSource.Record(request.SourceId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, request.PolicyId, request.ExpectedPolicyRevision,
            request.PublicationId, live.Definition.DefinitionId, request.ProfileId, (int)request.Mode,
            ModerationPolicyPublicationProjection.ProposedDigest(live.Definition.Policy.Proposed), deploymentInventory!.Fingerprint,
            live.Database.ConfigurationDigest, live.Database.ProviderCode, live.Database.ProviderVersion, live.Consumers,
            authority.UserId, checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationDeploymentSource>().Add(source);
        return new(ModerationQualificationSourceKind.DeploymentSource, source.Id, 1, MutationValidUntil: source.ValidUntil);
    }

    private async Task<QualifiedSource> AdoptDeploymentAsync(AdoptModerationDeploymentSource request,
        ModerationSourceAuthority authority, ModerationConsumerEvidenceSession session, DateTimeOffset now, CancellationToken token)
    {
        var source = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .Include(row => row.Consumers.OrderBy(item => item.Ordinal)).SingleAsync(row => row.Id == request.SourceId, token);
        var owner = new ModerationDeploymentSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock),
            deploymentInventory!, consumerSources!, clock);
        var current = await owner.AcquireSourceAsync(session, session.TransactionId, source.ProfileId,
            (ModerationNativeMode)source.ModeCode, now, token);
        if (current is null || current.Source.Id != source.Id)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        source = current.Source;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationDeploymentSourceOwner.Slot(authority, source.PublicationId, source.ProfileId, (ModerationNativeMode)source.ModeCode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDeploymentAdoption>(context, request.AdoptionId, token);
        if (await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
            .AnyAsync(row => row.Id == request.AdoptionId || row.SourceId == source.Id, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var adoption = ModerationDeploymentAdoption.Adopt(request.AdoptionId, source, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, request.ValidUntil);
        context.Set<ModerationDeploymentAdoption>().Add(adoption);
        return new(ModerationQualificationSourceKind.DeploymentAdoption, adoption.Id, 1, MutationValidUntil: adoption.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawDeploymentAsync(WithdrawModerationDeploymentAdoption request,
        ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        var discovered = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == request.AdoptionId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        var source = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .Include(row => row.Consumers.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == discovered.SourceId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        source.ValidatePersistedState();
        // Withdrawal must remain possible after artifact drift, source expiry or loss of an active pointer.
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            ModerationDeploymentSourceOwner.Slot(authority, source.PublicationId, source.ProfileId, (ModerationNativeMode)source.ModeCode), token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDeploymentAdoption>(context, request.AdoptionId, token);
        var adoption = await context.Set<ModerationDeploymentAdoption>().SingleAsync(row => row.Id == request.AdoptionId, token);
        adoption.ValidatePersistedState();
        if (adoption.HostBindingId != authority.Binding.Id || adoption.TrustDomainId != authority.Binding.TrustDomainId
            || adoption.TenantId != authority.Tenant.Id || adoption.SourceId != source.Id
            || !authority.FencedHumans.Contains(adoption.AdoptedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var definition = await context.ModerationPolicyPublications.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.DefinitionId, token);
        if (definition is null || definition.Jurisdiction != request.Target)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await RequireTargetSourceReceiptAsync(ModerationDeploymentSourceOwner.AdoptionRequest(adoption, request.Target),
            ModerationQualificationSourceKind.DeploymentAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
            adoption.WriterGeneration, adoption.AdoptedAt, authority, token);
        if (adoption.Revision != request.ExpectedAdoptionRevision || adoption.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        adoption.Withdraw(request.ExpectedAdoptionRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.DeploymentAdoption, adoption.Id, adoption.Revision);
    }

    private async Task VerifyDeploymentWriteAsync(ModerationQualificationMutation request,
        ModerationConsumerEvidenceSession? session, ModerationSourceAuthority authority, CancellationToken token)
    {
        if (request is WithdrawModerationDeploymentAdoption withdrawal)
        {
            var adoption = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
                .SingleOrDefaultAsync(row => row.Id == withdrawal.AdoptionId, token);
            if (adoption is null || adoption.Revision != withdrawal.ExpectedAdoptionRevision + 1
                || adoption.WithdrawnByHumanId != authority.UserId || adoption.WithdrawnAt is null
                || adoption.HostBindingId != authority.Binding.Id || adoption.TenantId != authority.Tenant.Id
                || adoption.TrustDomainId != authority.Binding.TrustDomainId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            return;
        }
        if (session is null) return;
        var now = clock.GetUtcNow();
        var expected = request is RecordModerationDeploymentSource record ? record.SourceId
            : ((AdoptModerationDeploymentSource)request).SourceId;
        var retained = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == expected, token);
        if (retained is null) throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var owner = new ModerationDeploymentSourceOwner(context,
            new ModerationPolicyDefinitionSourceOwner(context, admin, machine, deploymentInventory!, clock),
            deploymentInventory!, consumerSources!, clock);
        var current = await owner.AcquireSourceAsync(session, session.TransactionId,
            retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token);
        if (current is null || current.Source.Id != expected)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (request is AdoptModerationDeploymentSource adopt)
        {
            var approval = await owner.ReadAdoptionAsync(session, session.TransactionId,
                retained.ProfileId, (ModerationNativeMode)retained.ModeCode, now, token);
            if (approval is null || approval.Adoption.Id != adopt.AdoptionId)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        }
    }

}
