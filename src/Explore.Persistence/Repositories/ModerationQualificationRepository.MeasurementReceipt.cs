using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<(ModerationMeasurementReceipt Assertion, ModerationConsumerMeasurementChallenge Challenge,
        ModerationMeasurementIssuerEnrollment Issuer, DateTimeOffset ValidUntil)> ReadMeasurementAcceptanceAsync(
        AcceptModerationConsumerMeasurementSource source, ModerationSourceAuthority authority,
        DateTimeOffset now, CancellationToken token)
    {
        if (source.Target != authority.Tenant || measurementReceipts is null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var challenge = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.ChallengeId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        challenge.ValidatePersistedState();
        if (challenge.HostBindingId != authority.Binding.Id || challenge.TrustDomainId != authority.Binding.TrustDomainId
            || challenge.TenantId != authority.Tenant.Id || challenge.PublicationId != source.Binding.PublicationId
            || challenge.BindingDigest != ModerationPolicyPublicationProjection.Digest(source.Binding)
            || challenge.OriginalEffectId is { } originalEffectId
                && source.OriginalEffect != new ModerationOriginalEffectAnchor(
                    originalEffectId, challenge.OriginalSnapshotHash!, challenge.OriginalEffectBindingHash!)
            || challenge.PreparedAt > now || challenge.ValidUntil <= now || challenge.WriterGeneration > authority.Epoch.Revision
            || !authority.FencedHumans.Contains(challenge.PreparedByHumanId)
            || !authority.Administrators.Contains(challenge.PreparedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        var preparation = new PrepareModerationConsumerMeasurementSource
        {
            OriginalEffect = source.OriginalEffect,
            OperationId = Guid.Empty,
            Target = authority.Tenant,
            ExpectedQualificationRevision = challenge.WriterGeneration - 1,
            ChallengeId = challenge.Id,
            InstallationId = challenge.InstallationId,
            ExpectedInstallationRevision = challenge.InstallationRevision,
            ConsumerId = challenge.ConsumerId,
            Binding = source.Binding,
            ExpectedRuntimeGeneration = challenge.RuntimeGeneration,
            ExpectedInventoryFingerprint = challenge.InventoryFingerprint,
            ValidUntil = challenge.ValidUntil
        };
        var (installed, contract, definitionId) = await ReadMeasurementPreparationAsync(preparation, authority, now, token);
        if (challenge.DefinitionId != definitionId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerMeasurementChallenge>(context, challenge.Id, token);
        var retainedChallenge = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
            .SingleAsync(row => row.Id == challenge.Id, token);
        retainedChallenge.ValidatePersistedState();
        if (retainedChallenge.Nonce != challenge.Nonce || retainedChallenge.WriterGeneration != challenge.WriterGeneration
            || retainedChallenge.HostBindingId != challenge.HostBindingId || retainedChallenge.TrustDomainId != challenge.TrustDomainId
            || retainedChallenge.TenantId != challenge.TenantId || retainedChallenge.ConsumerId != challenge.ConsumerId
            || retainedChallenge.ModeCode != challenge.ModeCode || retainedChallenge.RuntimeGeneration != challenge.RuntimeGeneration
            || retainedChallenge.InventoryFingerprint != challenge.InventoryFingerprint
            || retainedChallenge.DefinitionId != challenge.DefinitionId
            || retainedChallenge.OriginalEffectId != challenge.OriginalEffectId
            || retainedChallenge.OriginalSnapshotHash != challenge.OriginalSnapshotHash
            || retainedChallenge.OriginalEffectBindingHash != challenge.OriginalEffectBindingHash
            || retainedChallenge.PublicationId != challenge.PublicationId || retainedChallenge.BindingDigest != challenge.BindingDigest
            || retainedChallenge.PreparedByHumanId != challenge.PreparedByHumanId
            || retainedChallenge.PreparedAt != challenge.PreparedAt || retainedChallenge.ValidUntil != challenge.ValidUntil
            || retainedChallenge.InstallationId != installed.Id || retainedChallenge.InstallationRevision != installed.Revision
            || retainedChallenge.InstallationWriterGeneration != installed.WriterGeneration
            || retainedChallenge.ConsumerContractDigest != ModerationPolicyPublicationProjection.Digest(contract))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireTargetSourceReceiptAsync(preparation, ModerationQualificationSourceKind.ConsumerMeasurementChallenge,
            challenge.Id, 1, challenge.PreparedByHumanId, challenge.WriterGeneration, challenge.PreparedAt, authority, token);
        var discoveredIssuer = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.IssuerEnrollmentId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var issuerSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.measurement-issuer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{discoveredIssuer.KeyId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasurementIssuerEnrollment>(context, discoveredIssuer.Id, token);
        var issuer = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .SingleAsync(row => row.Id == discoveredIssuer.Id, token);
        issuer.ValidatePersistedState();
        if (issuer.HostBindingId != authority.Binding.Id || issuer.TrustDomainId != authority.Binding.TrustDomainId
            || issuer.TenantId != authority.Tenant.Id || issuer.KeyId != discoveredIssuer.KeyId
            || issuer.ApprovedByHumanId != discoveredIssuer.ApprovedByHumanId
            || issuer.RevokedAt is not null || issuer.Revision != 1 || issuer.ApprovedAt > now || issuer.ValidUntil <= now
            || issuer.WriterGeneration > authority.Epoch.Revision || !authority.FencedHumans.Contains(issuer.ApprovedByHumanId)
            || !await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == issuer.ApprovedByHumanId
                && !row.User.IsDeleted && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                && row.Role.MasterCode == "platform.admin", token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        await RequireTargetSourceReceiptAsync(new EnrollModerationMeasurementIssuerSource
        {
            OperationId = Guid.Empty,
            Target = authority.Tenant,
            ExpectedQualificationRevision = issuer.WriterGeneration - 1,
            EnrollmentId = issuer.Id,
            KeyId = issuer.KeyId,
            Issuer = issuer.Issuer,
            PublicKeySpkiBase64 = issuer.PublicKeySpkiBase64,
            RunnerDigest = issuer.RunnerDigest,
            CatalogueDigest = issuer.CatalogueDigest,
            TestBundleDigest = issuer.TestBundleDigest,
            ValidUntil = issuer.ValidUntil
        }, ModerationQualificationSourceKind.MeasurementIssuerEnrollment, issuer.Id, 1, issuer.ApprovedByHumanId,
            issuer.WriterGeneration, issuer.ApprovedAt, authority, token);
        var trust = new ModerationMeasurementTrustKey
        {
            Issuer = issuer.Issuer,
            KeyId = issuer.KeyId,
            SubjectPublicKeyInfo = Convert.FromBase64String(issuer.PublicKeySpkiBase64).ToImmutableArray(),
            HostBindingId = issuer.HostBindingId,
            TrustDomainId = issuer.TrustDomainId,
            TenantId = issuer.TenantId,
            ApprovedRunnerDigest = issuer.RunnerDigest,
            ApprovedCatalogueDigest = issuer.CatalogueDigest,
            ApprovedTestBundleDigest = issuer.TestBundleDigest,
            ValidUntil = issuer.ValidUntil
        };
        var verified = measurementReceipts.Verify(source.SignedToken, trust, source.Binding, now);
        if (verified is not { Outcome: ModerationMeasurementVerificationOutcome.Known, Receipt: { } assertion })
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (assertion.ReceiptId != source.ReceiptId || assertion.ChallengeId != challenge.Id
            || assertion.ChallengeNonce != challenge.Nonce || assertion.ConsumerId != challenge.ConsumerId
            || assertion.InstallationId != installed.Id || assertion.InstallationRevision != installed.Revision
            || assertion.InstallationWriterGeneration != installed.WriterGeneration
            || assertion.RuntimeGeneration != installed.RuntimeGeneration
            || !StringComparer.OrdinalIgnoreCase.Equals(assertion.InventoryFingerprint, installed.InventoryFingerprint)
            || !StringComparer.OrdinalIgnoreCase.Equals(assertion.ConsumerContractDigest, retainedChallenge.ConsumerContractDigest)
            || assertion.StartedAt < challenge.PreparedAt || assertion.StartedAt < issuer.ApprovedAt
            || assertion.ValidUntil > challenge.ValidUntil || assertion.ValidUntil > issuer.ValidUntil)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        return (assertion, challenge, issuer, assertion.ValidUntil);
    }

    private async Task<QualifiedSource> AcceptConsumerMeasurementAsync(
        AcceptModerationConsumerMeasurementSource source, ModerationSourceAuthority authority,
        DateTimeOffset now, CancellationToken token)
    {
        var (assertion, challenge, issuer, until) = await ReadMeasurementAcceptanceAsync(source, authority, now, token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerMeasurementReceipt>(context, assertion.ReceiptId, token);
        if (await context.ModerationConsumerMeasurementReceipts.AsNoTracking().AnyAsync(row =>
            row.Id == assertion.ReceiptId || row.ChallengeId == challenge.Id || row.RunId == assertion.RunId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var receipt = ModerationConsumerMeasurementReceipt.Accept(assertion.ReceiptId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, challenge.Id, issuer.Id, issuer.KeyId, assertion.RunId,
            source.SignedToken, Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(source.SignedToken))),
            authority.UserId, checked(authority.Epoch.Revision + 1), now, until);
        context.ModerationConsumerMeasurementReceipts.Add(receipt);
        return new(ModerationQualificationSourceKind.ConsumerMeasurementReceipt, receipt.Id, 1, MutationValidUntil: until);
    }

    private async Task RequireAcceptedMeasurementReceiptAsync(AcceptModerationConsumerMeasurementSource source,
        ModerationQualificationMutationReceipt original, ModerationSourceAuthority authority, CancellationToken token)
    {
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerMeasurementReceipt>(context, source.ReceiptId, token);
        var receipt = await context.ModerationConsumerMeasurementReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.ReceiptId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        receipt.ValidatePersistedState();
        if (receipt.HostBindingId != authority.Binding.Id || receipt.TrustDomainId != authority.Binding.TrustDomainId
            || receipt.TenantId != authority.Tenant.Id || receipt.ChallengeId != source.ChallengeId
            || receipt.IssuerEnrollmentId != source.IssuerEnrollmentId || receipt.SignedToken != source.SignedToken
            || receipt.AcceptedByHumanId != original.PrincipalId
            || receipt.WriterGeneration != original.QualificationRevision || receipt.AcceptedAt != original.CompletedAt)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }
}
