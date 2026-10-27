using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Explore.Persistence.Repositories;

/// <summary>
/// Current consumer observations on an original lifecycle or publication acquisition.
/// The caller supplies its actual authority and currentness check, never a fabricated lifecycle grant.
/// </summary>
internal sealed class ModerationConsumerEvidenceSession(
    ExploreDbContext context, IDbContextTransaction transaction, ModerationSourceAuthority authority,
    ModerationPolicyCompiledDraft policy, Func<CancellationToken, Task<bool>> currentAcquisition,
    IModerationPolicyPublicationRepository publications, ModerationCapabilityRegistry inventory,
    IEnumerable<IModerationConsumerRuntimeSource>? consumerSources,
    IModerationMeasurementReceiptVerifier? measurementReceipts, TimeProvider clock,
    Func<ModerationPolicyPublication, ModerationDecision, DateTimeOffset, CancellationToken,
        Task<ModerationActivationProfileObservation?>>? readProfileConfiguration,
    ModerationOriginalRecipeAuthority? originalRecipe = null)
    : IModerationConsumerEvidenceSession
{
    private bool disposed;
    private readonly long qualificationRevision = authority.Epoch.Revision;
    public Guid TransactionId => disposed ? Guid.Empty : transaction.TransactionId;

    public void Dispose() => disposed = true;

    /// <summary>Returns only the original native acquisition after checking its live transaction and authority.</summary>
    internal async Task<ModerationNativeEvidenceAcquisition?> AcquireNativeAsync(
        Guid transactionId, DateTimeOffset observedAt, CancellationToken token)
    {
        if (transactionId == Guid.Empty || transactionId != TransactionId
            || observedAt.Offset != TimeSpan.Zero || observedAt == DateTimeOffset.MinValue
            || observedAt == DateTimeOffset.MaxValue || !await CurrentAsync(token)
            || clock.GetUtcNow() < observedAt)
            return null;
        return new(context, transaction, authority, policy, originalRecipe);
    }

    /// <summary>Rereads profile adoption on the exact lifecycle acquisition, including before participant publication.</summary>
    internal async Task<ModerationActivationProfileObservation?> ReadProfileConfigurationAsync(
        Guid transactionId, ModerationPolicyPublication publication, ModerationDecision decision,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (readProfileConfiguration is null || await AcquireNativeAsync(transactionId, observedAt, token) is null)
            return null;
        var profile = await readProfileConfiguration(publication, decision, observedAt, token);
        if (profile is null || profile.TransactionId != transactionId || profile.ObservedAt != observedAt
            || profile.HostBindingId != authority.Binding.Id
            || profile.PolicyId != policy.Publication.PolicyId || profile.PolicyRevision != policy.Publication.ProposedRevision
            || profile.PublicationId != policy.Publication.PublicationId
            || profile.DecisionId != decision.Id || profile.DecisionRevision != decision.Revision
            || await AcquireNativeAsync(transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= profile.ValidUntil)
            return null;
        return profile;
    }

    public async Task<ModerationMeasurementDatabaseObservation?> AcquirePrimaryDatabaseAsync(
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (observedAt.Offset != TimeSpan.Zero || observedAt == DateTimeOffset.MinValue
            || observedAt == DateTimeOffset.MaxValue || !await CurrentAsync(token))
            return null;
        var composed = context.GetService<IDbContextOptions>()
            .FindExtension<PrimaryDatabasePublicIdentityOptionsExtension>();
        var provider = composed?.Provider switch
        {
            PrimaryDatabaseProvider.PostgreSql => RelationalNamedLock.PostgreSqlProvider,
            PrimaryDatabaseProvider.Sqlite => RelationalNamedLock.SqliteProvider,
            PrimaryDatabaseProvider.SqlServer => RelationalNamedLock.SqlServerProvider,
            PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql => RelationalNamedLock.MySqlProvider,
            _ => null
        };
        var connection = context.Database.GetDbConnection();
        if (composed is null || provider is null || context.Database.ProviderName != provider
            || connection.State != ConnectionState.Open || transaction.GetDbTransaction().Connection != connection)
            return null;
        var version = connection.ServerVersion;
        if (string.IsNullOrWhiteSpace(version) || version.Length > 256 || !await CurrentAsync(token)
            || connection.State != ConnectionState.Open || connection.ServerVersion != version)
            return null;
        return clock.GetUtcNow() >= observedAt
            ? new(TransactionId, composed.PublicDigest, composed.Provider.ToString(), version, observedAt)
            : null;
    }

    public async Task<ModerationCapabilitySourceObservation?> AcquireInstallationAsync(
        ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt, CancellationToken token)
    {
        if (!Matches(binding, consumerId, observedAt) || !await CurrentAsync(token)) return null;
        var owner = consumerSources?.SingleOrDefault(row => row.ConsumerId == consumerId && row.Mode == binding.Mode);
        var runtime = owner?.ReadCurrent();
        if (runtime is null || runtime.ConsumerId != consumerId || runtime.Mode != binding.Mode
            || runtime.InventoryFingerprint != inventory.Fingerprint) return null;
        var installed = await ReadInstallationAsync(binding, consumerId, runtime, observedAt, token);
        if (installed is null || !await CurrentAsync(token) || owner!.ReadCurrent() != runtime) return null;
        var completed = clock.GetUtcNow();
        return completed >= observedAt && completed < installed.ValidUntil
            ? new(binding, consumerId, installed.Id, installed.Revision, installed.RuntimeGeneration,
                observedAt, installed.ValidUntil) : null;
    }

    public async Task<ModerationMeasurementReceiptObservation?> AcquireMeasurementAsync(
        ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt, CancellationToken token)
    {
        if (!Matches(binding, consumerId, observedAt) || !await CurrentAsync(token)) return null;
        var bindingDigest = ModerationPolicyPublicationProjection.Digest(binding);
        var receiptId = await (
            from receipt in context.ModerationConsumerMeasurementReceipts.AsNoTracking()
            join challenge in context.ModerationConsumerMeasurementChallenges.AsNoTracking()
                on receipt.ChallengeId equals challenge.Id
            where receipt.HostBindingId == authority.Binding.Id && receipt.TenantId == authority.Tenant.Id
                && challenge.HostBindingId == authority.Binding.Id && challenge.TenantId == authority.Tenant.Id
                && challenge.BindingDigest == bindingDigest && challenge.ConsumerId == consumerId
            orderby receipt.WriterGeneration descending
            select (Guid?)receipt.Id).FirstOrDefaultAsync(token);
        // A latest assertion that fails current qualification never falls back to an older one.
        return receiptId is { } id
            ? await AcquireMeasurementAsync(binding, consumerId, id, observedAt, token) : null;
    }

    internal async Task<ModerationMeasurementReceiptObservation?> AcquireMeasurementAsync(
        ModerationActivationBinding binding, string consumerId, Guid receiptId,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (measurementReceipts is null || !Matches(binding, consumerId, observedAt)
            || !await CurrentAsync(token)) return null;
        var contract = policy.Consumers.SingleOrDefault(row => row.Binding == binding)?
            .Consumers.SingleOrDefault(row => row.Id == consumerId);
        var owner = consumerSources?.SingleOrDefault(row => row.ConsumerId == consumerId && row.Mode == binding.Mode);
        var runtime = owner?.ReadCurrent();
        if (contract is null || runtime is null || runtime.ConsumerId != consumerId || runtime.Mode != binding.Mode
            || runtime.InventoryFingerprint != inventory.Fingerprint) return null;
        var discovered = await context.ModerationConsumerMeasurementReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId, token);
        if (discovered is null || discovered.HostBindingId != authority.Binding.Id
            || discovered.TrustDomainId != authority.Binding.TrustDomainId || discovered.TenantId != authority.Tenant.Id
            || !await AdministratorAsync(discovered.AcceptedByHumanId, token)) return null;
        var installed = await ReadInstallationAsync(binding, consumerId, runtime, observedAt, token);
        if (installed is null) return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerMeasurementChallenge>(
            context, discovered.ChallengeId, token);
        var challenge = await context.ModerationConsumerMeasurementChallenges.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.ChallengeId, token);
        if (challenge is null) return null;
        challenge.ValidatePersistedState();
        if (challenge.HostBindingId != authority.Binding.Id || challenge.TrustDomainId != authority.Binding.TrustDomainId
            || challenge.TenantId != authority.Tenant.Id || challenge.InstallationId != installed.Id
            || challenge.InstallationRevision != installed.Revision || challenge.InstallationWriterGeneration != installed.WriterGeneration
            || challenge.ConsumerId != consumerId || challenge.ModeCode != (int)binding.Mode
            || challenge.RuntimeGeneration != runtime.RuntimeGeneration || challenge.InventoryFingerprint != runtime.InventoryFingerprint
            || challenge.PublicationId != binding.PublicationId
            || challenge.BindingDigest != ModerationPolicyPublicationProjection.Digest(binding)
            || challenge.ConsumerContractDigest != ModerationPolicyPublicationProjection.Digest(contract)
            || challenge.PreparedAt < installed.InstalledAt || challenge.PreparedAt > observedAt
            || challenge.ValidUntil <= observedAt || challenge.ValidUntil > installed.ValidUntil
            || challenge.WriterGeneration > qualificationRevision
            || !await AdministratorAsync(challenge.PreparedByHumanId, token)
            || !await HasReceiptAsync(operationId => new PrepareModerationConsumerMeasurementSource
            {
                OriginalEffect = challenge.OriginalEffectId is { } originalEffectId
                    ? new(originalEffectId, challenge.OriginalSnapshotHash!, challenge.OriginalEffectBindingHash!) : null,
                OperationId = operationId,
                Target = authority.Tenant,
                ExpectedQualificationRevision = challenge.WriterGeneration - 1,
                ChallengeId = challenge.Id,
                InstallationId = installed.Id,
                ExpectedInstallationRevision = installed.Revision,
                ConsumerId = consumerId,
                Binding = binding,
                ExpectedRuntimeGeneration = challenge.RuntimeGeneration,
                ExpectedInventoryFingerprint = challenge.InventoryFingerprint,
                ValidUntil = challenge.ValidUntil
            }, ModerationQualificationSourceKind.ConsumerMeasurementChallenge, challenge.Id, 1,
                challenge.PreparedByHumanId, challenge.WriterGeneration, challenge.PreparedAt, token))
            return null;
        if (!await DefinitionCurrentAsync(binding, challenge, observedAt, token)) return null;

        var discoveredIssuer = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.IssuerEnrollmentId, token);
        if (discoveredIssuer is null || !authority.FencedHumans.Contains(discoveredIssuer.ApprovedByHumanId)
            || !authority.Administrators.Contains(discoveredIssuer.ApprovedByHumanId)) return null;
        await using var issuerSlot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.measurement-issuer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{discoveredIssuer.KeyId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasurementIssuerEnrollment>(context, discoveredIssuer.Id, token);
        var issuer = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discoveredIssuer.Id, token);
        if (issuer is null) return null;
        issuer.ValidatePersistedState();
        if (issuer.HostBindingId != authority.Binding.Id || issuer.TrustDomainId != authority.Binding.TrustDomainId
            || issuer.TenantId != authority.Tenant.Id || issuer.KeyId != discoveredIssuer.KeyId
            || issuer.ApprovedByHumanId != discoveredIssuer.ApprovedByHumanId || issuer.KeyId != discovered.IssuerKeyId
            || issuer.RevokedAt is not null || issuer.Revision != 1 || issuer.ApprovedAt > observedAt
            || issuer.ValidUntil <= observedAt || issuer.WriterGeneration > qualificationRevision
            || !await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == issuer.ApprovedByHumanId
                && !row.User.IsDeleted && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                && row.Role.MasterCode == "platform.admin", token)
            || !await HasReceiptAsync(operationId => new EnrollModerationMeasurementIssuerSource
            {
                OperationId = operationId,
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
            }, ModerationQualificationSourceKind.MeasurementIssuerEnrollment, issuer.Id, 1,
                issuer.ApprovedByHumanId, issuer.WriterGeneration, issuer.ApprovedAt, token))
            return null;

        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerMeasurementReceipt>(context, receiptId, token);
        var accepted = await context.ModerationConsumerMeasurementReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId, token);
        if (accepted is null) return null;
        accepted.ValidatePersistedState();
        if (accepted.HostBindingId != authority.Binding.Id || accepted.TrustDomainId != authority.Binding.TrustDomainId
            || accepted.TenantId != authority.Tenant.Id || accepted.ChallengeId != challenge.Id
            || accepted.IssuerEnrollmentId != issuer.Id || accepted.IssuerKeyId != issuer.KeyId
            || accepted.RunId != discovered.RunId || accepted.SignedToken != discovered.SignedToken
            || accepted.TokenDigest != discovered.TokenDigest || accepted.AcceptedByHumanId != discovered.AcceptedByHumanId
            || accepted.WriterGeneration != discovered.WriterGeneration || accepted.AcceptedAt != discovered.AcceptedAt
            || accepted.ValidUntil != discovered.ValidUntil || accepted.Revision != discovered.Revision
            || accepted.WriterGeneration <= challenge.WriterGeneration || accepted.WriterGeneration <= issuer.WriterGeneration
            || accepted.WriterGeneration > qualificationRevision
            || accepted.AcceptedAt < challenge.PreparedAt || accepted.AcceptedAt < issuer.ApprovedAt
            || accepted.AcceptedAt > observedAt || accepted.ValidUntil <= observedAt
            || accepted.ValidUntil > installed.ValidUntil || accepted.ValidUntil > challenge.ValidUntil
            || accepted.ValidUntil > issuer.ValidUntil) return null;
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
        var verified = measurementReceipts.Verify(accepted.SignedToken, trust, binding, observedAt);
        if (verified is not { Outcome: ModerationMeasurementVerificationOutcome.Known, Receipt: { } assertion }
            || assertion.ReceiptId != accepted.Id || assertion.RunId != accepted.RunId || assertion.ChallengeId != challenge.Id
            || assertion.ChallengeNonce != challenge.Nonce || assertion.ConsumerId != consumerId
            || assertion.InstallationId != installed.Id || assertion.InstallationRevision != installed.Revision
            || assertion.InstallationWriterGeneration != installed.WriterGeneration
            || assertion.RuntimeGeneration != runtime.RuntimeGeneration
            || !StringComparer.OrdinalIgnoreCase.Equals(assertion.InventoryFingerprint, runtime.InventoryFingerprint)
            || !StringComparer.OrdinalIgnoreCase.Equals(assertion.ConsumerContractDigest, challenge.ConsumerContractDigest)
            || assertion.StartedAt < challenge.PreparedAt || assertion.StartedAt < issuer.ApprovedAt
            || assertion.IssuedAt > accepted.AcceptedAt || assertion.ValidUntil != accepted.ValidUntil
            || assertion.ValidUntil > challenge.ValidUntil || assertion.ValidUntil > issuer.ValidUntil
            || !await HasReceiptAsync(operationId => new AcceptModerationConsumerMeasurementSource
            {
                OperationId = operationId,
                Target = authority.Tenant,
                ExpectedQualificationRevision = accepted.WriterGeneration - 1,
                ReceiptId = accepted.Id,
                ChallengeId = challenge.Id,
                IssuerEnrollmentId = issuer.Id,
                Binding = binding,
                SignedToken = accepted.SignedToken
            }, ModerationQualificationSourceKind.ConsumerMeasurementReceipt, accepted.Id, 1,
                accepted.AcceptedByHumanId, accepted.WriterGeneration, accepted.AcceptedAt, token))
            return null;
        var until = new[] { installed.ValidUntil, challenge.ValidUntil, issuer.ValidUntil,
            assertion.ValidUntil, accepted.ValidUntil }.Min();
        if (!await DefinitionCurrentAsync(binding, challenge, observedAt, token)
            || !await CurrentAsync(token) || owner!.ReadCurrent() != runtime) return null;
        var completed = clock.GetUtcNow();
        return completed >= observedAt && completed < until
            ? new(TransactionId, assertion, accepted.TokenDigest, accepted.Revision, accepted.WriterGeneration,
                accepted.AcceptedByHumanId, accepted.AcceptedAt, challenge.PreparedByHumanId, challenge.WriterGeneration,
                challenge.PreparedAt, issuer.Id, issuer.WriterGeneration, issuer.ApprovedByHumanId,
                issuer.ApprovedAt, trust, runtime, observedAt, until) : null;
    }

    private async Task<ModerationConsumerInstallation?> ReadInstallationAsync(
        ModerationActivationBinding binding, string consumerId, ModerationConsumerRuntimeObservation runtime,
        DateTimeOffset observedAt, CancellationToken token)
    {
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.consumer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{consumerId}:{(int)binding.Mode}", token);
        var discovered = await context.ModerationConsumerInstallations.AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.ConsumerId == consumerId
                && row.ModeCode == (int)binding.Mode)
            .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
        if (discovered is null || !await AdministratorAsync(discovered.InstalledByHumanId, token)) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerInstallation>(context, discovered.Id, token);
        var installed = await context.ModerationConsumerInstallations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered.Id, token);
        if (installed is null) return null;
        installed.ValidatePersistedState();
        if (installed.HostBindingId != authority.Binding.Id || installed.TrustDomainId != authority.Binding.TrustDomainId
            || installed.TenantId != authority.Tenant.Id || installed.InstalledByHumanId != discovered.InstalledByHumanId
            || installed.ConsumerId != consumerId || installed.ModeCode != (int)binding.Mode
            || installed.RuntimeGeneration != runtime.RuntimeGeneration || installed.InventoryFingerprint != runtime.InventoryFingerprint
            || installed.WithdrawnAt is not null || installed.Revision != 1
            || installed.InstalledAt > observedAt || installed.ValidUntil <= observedAt
            || installed.WriterGeneration > qualificationRevision
            || !await HasReceiptAsync(operationId => new InstallModerationConsumerSource
            {
                OperationId = operationId,
                Target = authority.Tenant,
                ExpectedQualificationRevision = installed.WriterGeneration - 1,
                InstallationId = installed.Id,
                ConsumerId = consumerId,
                Mode = binding.Mode,
                ExpectedRuntimeGeneration = installed.RuntimeGeneration,
                ExpectedInventoryFingerprint = installed.InventoryFingerprint,
                ValidUntil = installed.ValidUntil
            }, ModerationQualificationSourceKind.ConsumerInstallation, installed.Id, 1,
                installed.InstalledByHumanId, installed.WriterGeneration, installed.InstalledAt, token))
            return null;
        return installed;
    }

    private async Task<bool> DefinitionCurrentAsync(
        ModerationActivationBinding binding, ModerationConsumerMeasurementChallenge challenge,
        DateTimeOffset observedAt, CancellationToken token)
    {
        if (originalRecipe is not null)
            return challenge.DefinitionId == originalRecipe.DefinitionId
                && binding.PublicationId == originalRecipe.Policy.Publication.PublicationId
                && binding.PolicyRevision == originalRecipe.Policy.Publication.ProposedRevision
                && binding.Jurisdiction == originalRecipe.Policy.Publication.Jurisdiction
                && await originalRecipe.CurrentAsync(token);
        if (challenge.DefinitionId != binding.PublicationId)
        {
            var candidate = await publications.AcquireCandidateAsync(TransactionId, authority.Binding.Id,
                binding.Jurisdiction, binding.PolicyId, binding.PublicationId, observedAt, token);
            return candidate is not null && candidate.DefinitionId == challenge.DefinitionId
                && challenge.ValidUntil <= candidate.ValidUntil
                && await AdministratorAsync(candidate.PreparedByHumanId, token)
                && candidate.ProposedStateDigest == ModerationPolicyPublicationProjection.ProposedDigest(policy.Proposed);
        }
        var publication = await publications.AcquireSelectedAsync(TransactionId, authority.Binding.Id,
            binding.Jurisdiction, binding.PolicyId, policy.Proposed.RequestedState == ModerationPolicyRequestedState.Active, token);
        return publication is not null && publication.Id == binding.PublicationId
            && publication.Revision == binding.PolicyRevision && publication.HostBindingId == authority.Binding.Id
            && publication.Jurisdiction == binding.Jurisdiction && publication.PublishedAt <= observedAt
            && publication.WriterGeneration == policy.Publication.WriterGeneration
            && publication.CatalogueRevision == policy.Publication.CatalogueRevision
            && publication.InventoryFingerprint == inventory.Fingerprint;
    }

    private bool Matches(ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt) =>
        !disposed && binding.PolicyId == policy.Publication.PolicyId
        && (originalRecipe is null || binding == originalRecipe.Binding)
        && binding.PolicyRevision == policy.Publication.ProposedRevision
        && binding.PublicationId == policy.Publication.PublicationId
        && binding.Jurisdiction == policy.Publication.Jurisdiction
        && inventory.Consumers.ContainsKey(consumerId)
        && Enum.IsDefined(binding.Capability) && Enum.IsDefined(binding.Mode) && policy.Proposed.Modes.Contains(binding.Mode)
        && policy.Consumers.Any(row => row.Binding == binding && row.Consumers.Any(item => item.Id == consumerId))
        && observedAt.Offset == TimeSpan.Zero && observedAt != DateTimeOffset.MinValue && observedAt != DateTimeOffset.MaxValue;

    private async Task<bool> CurrentAsync(CancellationToken token)
    {
        if (disposed || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || transaction.GetDbTransaction().Connection != context.Database.GetDbConnection()
            || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || context.IsTenantFilterBypassed || context.TenantFilterTenantId != authority.Tenant.Id
            || context.ModerationTrustDomainId != authority.Binding.TrustDomainId
            || !await context.Tenants.AsNoTracking().AnyAsync(
                row => row.Id == authority.Tenant.Id && row.TenantStatus.IsActiveState, token)
            || !await currentAcquisition(token)
            || originalRecipe is not null && (originalRecipe.Policy.Publication != policy.Publication
                || !await originalRecipe.CurrentAsync(token)))
            return false;
        return !disposed && ReferenceEquals(context.Database.CurrentTransaction, transaction)
        && transaction.GetDbTransaction().Connection == context.Database.GetDbConnection()
        && !context.IsTenantFilterBypassed && context.TenantFilterTenantId == authority.Tenant.Id
        && context.ModerationTrustDomainId == authority.Binding.TrustDomainId;
    }

    private async Task<bool> AdministratorAsync(Guid humanId, CancellationToken token)
    {
        if (!authority.FencedHumans.Contains(humanId) || !authority.Administrators.Contains(humanId)) return false;
        return await context.PlatformUserRoles.AsNoTracking().AnyAsync(row => row.UserId == humanId
                && !row.User.IsDeleted && row.Role.RoleScopeId == (int)RoleScopeEnum.Platform
                && row.Role.MasterCode == "platform.admin", token)
            || await context.TenantUserRoleGrants.AsNoTracking().AnyAsync(row => row.TenantId == authority.Tenant.Id
                && row.TenantUser.UserId == humanId && row.RevokedAt == null
                && row.RoleScopeId == (int)RoleScopeEnum.Tenant && row.RoleId == (int)RoleEnum.TenantAdmin
                && row.Role.RoleScopeId == (int)RoleScopeEnum.Tenant
                && row.TenantUser.StatusId == (int)TenantUserStatusEnum.Active
                && !row.TenantUser.IsDeleted && !row.TenantUser.User.IsDeleted, token);
    }

    private async Task<bool> HasReceiptAsync(
        Func<Guid, ModerationQualificationMutation> request, ModerationQualificationSourceKind kind, Guid id,
        long revision, Guid principalId, long writerGeneration, DateTimeOffset completedAt, CancellationToken token)
    {
        var receiptId = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .Where(row => row.SourceId == id && row.SourceKind == kind && row.ResultRevision == revision)
            .Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (receiptId is null) return false;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationQualificationMutationReceipt>(context, receiptId.Value, token);
        var receipt = await context.ModerationQualificationMutationReceipts.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == receiptId.Value, token);
        if (receipt is null) return false;
        receipt.ValidatePersistedState();
        var accepted = request(receipt.Id);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            accepted.GetType().FullName + "\n" + JsonSerializer.Serialize(accepted, accepted.GetType()))));
        if (receipt.RequestDigest != digest && originalRecipe is not null
            && await originalRecipe.CurrentAsync(token))
        {
            var originalRequest = accepted switch
            {
                PrepareModerationConsumerMeasurementSource preparation =>
                    (ModerationQualificationMutation)(preparation with { OriginalEffect = originalRecipe.Anchor }),
                AcceptModerationConsumerMeasurementSource acceptance =>
                    acceptance with { OriginalEffect = originalRecipe.Anchor },
                _ => null
            };
            if (originalRequest is not null)
                digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                    originalRequest.GetType().FullName + "\n"
                    + JsonSerializer.Serialize(originalRequest, originalRequest.GetType()))));
        }
        return receipt.SourceId == id && receipt.SourceKind == kind && receipt.ResultRevision == revision
            && receipt.RequestDigest == digest && receipt.HostBindingId == authority.Binding.Id
            && receipt.TrustDomainId == authority.Binding.TrustDomainId && receipt.TenantId == authority.Tenant.Id
            && receipt.PrincipalId == principalId && receipt.QualificationRevision == writerGeneration
            && receipt.CompletedAt == completedAt && writerGeneration <= qualificationRevision;
    }
}

/// <summary>Internal original-transaction references, not request coordinates or permission to publish.</summary>
internal sealed record ModerationNativeEvidenceAcquisition(
    ExploreDbContext Context, IDbContextTransaction Transaction, ModerationSourceAuthority Authority,
    ModerationPolicyCompiledDraft Policy, ModerationOriginalRecipeAuthority? OriginalRecipe = null);
