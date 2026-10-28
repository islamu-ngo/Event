using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<(ModerationConsumerInstallation Installation, ModerationConsumerContract Contract, Guid DefinitionId)>
        ReadMeasurementPreparationAsync(PrepareModerationConsumerMeasurementSource source,
            ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (source.Target != authority.Tenant || source.ValidUntil <= now
            || source.Binding.Jurisdiction != authority.Tenant && source.Binding.Jurisdiction != authority.Instance)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var original = source.OriginalEffect is { } anchor
            ? await ModerationOriginalRecipeAuthority.AcquireAsync(context, authority,
                context.Database.CurrentTransaction!.TransactionId, anchor, privacy, clock, token) : null;
        if (source.OriginalEffect is not null && (original is null || source.ValidUntil > original.ValidUntil
            || source.Binding != original.Binding))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var candidate = original is null ? await publications.AcquireCandidateAsync(context.Database.CurrentTransaction!.TransactionId,
            authority.Binding.Id, source.Binding.Jurisdiction, source.Binding.PolicyId,
            source.Binding.PublicationId, now, token) : null;
        if (original is null && candidate is null && await context.ModerationPolicyPublicationCandidates.AnyAsync(
            row => row.Id == source.Binding.PublicationId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var publication = original is not null ? null : candidate is null
            ? await publications.AcquireSelectedAsync(context.Database.CurrentTransaction!.TransactionId,
                authority.Binding.Id, source.Binding.Jurisdiction, source.Binding.PolicyId, false, token)
            : candidate.Definition;
        var policy = original?.Policy ?? (candidate is null
            ? publication is null ? null : ModerationPolicyPublicationProjection.RestoreHistory(publication)
            : ModerationPolicyPublicationProjection.RestoreCandidate(candidate, null));
        if (policy is null || original is null && publication is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        if (policy.Publication.PublicationId != source.Binding.PublicationId
            || policy.Publication.ProposedRevision != source.Binding.PolicyRevision
            || policy.Publication.Jurisdiction != source.Binding.Jurisdiction
            || policy.Publication.HostBindingId != authority.Binding.Id
            || (original is null ? publication!.InventoryFingerprint : original.Policy.Publication.InventoryFingerprint)
                != source.ExpectedInventoryFingerprint
            || candidate is not null && (source.ValidUntil > candidate.ValidUntil
                || !authority.FencedHumans.Contains(candidate.PreparedByHumanId)
                || !authority.Administrators.Contains(candidate.PreparedByHumanId)))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var contract = policy.Consumers.SingleOrDefault(row => row.Binding == source.Binding)?
            .Consumers.SingleOrDefault(row => row.Id == source.ConsumerId)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.consumer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{source.ConsumerId}:{(int)source.Binding.Mode}", token);
        var installed = await context.ModerationConsumerInstallations.AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.ConsumerId == source.ConsumerId
                && row.ModeCode == (int)source.Binding.Mode)
            .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerInstallation>(context, installed.Id, token);
        installed = await context.ModerationConsumerInstallations.AsNoTracking().SingleAsync(row => row.Id == installed.Id, token);
        installed.ValidatePersistedState();
        if (installed.Id != source.InstallationId || installed.Revision != source.ExpectedInstallationRevision
            || installed.HostBindingId != authority.Binding.Id || installed.TrustDomainId != authority.Binding.TrustDomainId
            || installed.TenantId != authority.Tenant.Id || installed.ConsumerId != source.ConsumerId
            || installed.ModeCode != (int)source.Binding.Mode || installed.WithdrawnAt is not null
            || installed.InstalledAt > now || installed.ValidUntil <= now || source.ValidUntil > installed.ValidUntil
            || installed.WriterGeneration > authority.Epoch.Revision
            || installed.RuntimeGeneration != source.ExpectedRuntimeGeneration
            || installed.InventoryFingerprint != source.ExpectedInventoryFingerprint
            || !authority.FencedHumans.Contains(installed.InstalledByHumanId)
            || !authority.Administrators.Contains(installed.InstalledByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireConsumerInstallationReceiptAsync(installed, authority, token);
        RequireConsumerRuntime(source);
        return (installed, contract, original?.DefinitionId ?? publication!.Id);
    }

    private async Task<QualifiedSource> PrepareConsumerMeasurementAsync(
        PrepareModerationConsumerMeasurementSource source, ModerationSourceAuthority authority,
        DateTimeOffset now, string nonce, CancellationToken token)
    {
        var (installed, contract, definitionId) = await ReadMeasurementPreparationAsync(source, authority, now, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerMeasurementChallenge>(context, source.ChallengeId, token);
        if (await context.ModerationConsumerMeasurementChallenges.AnyAsync(row => row.Id == source.ChallengeId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var challenge = ModerationConsumerMeasurementChallenge.Prepare(source.ChallengeId, installed,
            source.Binding.PublicationId, definitionId, ModerationPolicyPublicationProjection.Digest(source.Binding),
            ModerationPolicyPublicationProjection.Digest(contract), nonce, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, source.ValidUntil,
            source.OriginalEffect?.EffectId, source.OriginalEffect?.SnapshotHash, source.OriginalEffect?.EffectBindingHash);
        context.ModerationConsumerMeasurementChallenges.Add(challenge);
        return new(ModerationQualificationSourceKind.ConsumerMeasurementChallenge, challenge.Id, 1,
            MutationValidUntil: challenge.ValidUntil);
    }
}
