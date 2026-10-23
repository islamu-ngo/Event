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
/// Fixed deployment owner. It borrows the original native acquisition and independently rereads
/// composed primary/runtime facts and receipted adoption; it owns no other readiness prerequisite.
/// </summary>
public sealed class ModerationDeploymentSourceOwner(
    ExploreDbContext context, ModerationPolicyDefinitionSourceOwner definitions,
    ModerationCapabilityRegistry inventory, IEnumerable<IModerationConsumerRuntimeSource> runtimeSources, TimeProvider clock)
{
    public async Task<ModerationDeploymentSourceObservation?> AcquireSourceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context)) return null;
        var requirement = native.Policy.Publication;
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            Slot(native.Authority, requirement.PublicationId, profileId, mode), token);
        var discovered = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .Where(row => row.HostBindingId == native.Authority.Binding.Id && row.TenantId == native.Authority.Tenant.Id
                && row.PublicationId == requirement.PublicationId && row.ProfileId == profileId && row.ModeCode == (int)mode)
            .OrderByDescending(row => row.WriterGeneration).Select(row => (Guid?)row.Id).FirstOrDefaultAsync(token);
        if (discovered is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDeploymentSource>(context, discovered.Value, token);
        var source = await context.Set<ModerationDeploymentSource>().AsNoTracking()
            .Include(row => row.Consumers.OrderBy(item => item.Ordinal)).SingleOrDefaultAsync(row => row.Id == discovered, token);
        if (source is null) return null;
        source.ValidatePersistedState();
        if (source.HostBindingId != native.Authority.Binding.Id || source.TrustDomainId != native.Authority.Binding.TrustDomainId
            || source.TenantId != native.Authority.Tenant.Id || source.PolicyId != requirement.PolicyId
            || source.PublicationId != requirement.PublicationId || source.ProfileId != profileId || source.ModeCode != (int)mode
            || source.PolicyRevision != requirement.ProposedRevision || source.RecordedAt > observedAt || source.ValidUntil <= observedAt
            || source.WriterGeneration > native.Authority.Epoch.Revision
            || !Administrator(native.Authority, source.RecordedByHumanId)
            || !await HasReceiptAsync(native, RecordRequest(source, requirement.Jurisdiction),
                ModerationQualificationSourceKind.DeploymentSource, source.Id, 1, source.RecordedByHumanId,
                source.WriterGeneration, source.RecordedAt, token)) return null;
        var live = await ObserveAsync(session, transactionId, source.Id, profileId, mode, observedAt, token);
        if (live is null || !Matches(source, live)
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null
            || clock.GetUtcNow() >= source.ValidUntil) return null;
        return new(transactionId, source, observedAt, source.ValidUntil);
    }

    public async Task<ModerationDeploymentAdoptionObservation?> ReadAdoptionAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        var current = await AcquireSourceAsync(evidenceSession, transactionId, profileId, mode, observedAt, token);
        if (current is null || evidenceSession is not ModerationConsumerEvidenceSession session
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native) return null;
        var discovered = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
            .Where(row => row.SourceId == current.Source.Id).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(token);
        if (discovered is null) return null;
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationDeploymentAdoption>(context, discovered.Value, token);
        var adoption = await context.Set<ModerationDeploymentAdoption>().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == discovered, token);
        if (adoption is null) return null;
        adoption.ValidatePersistedState();
        if (!adoption.MatchesCurrent(current.Source, observedAt)
            || adoption.WriterGeneration > native.Authority.Epoch.Revision
            || !Administrator(native.Authority, adoption.AdoptedByHumanId)
            || !await HasReceiptAsync(native, AdoptionRequest(adoption, native.Policy.Publication.Jurisdiction),
                ModerationQualificationSourceKind.DeploymentAdoption, adoption.Id, 1, adoption.AdoptedByHumanId,
                adoption.WriterGeneration, adoption.AdoptedAt, token)) return null;
        var completed = await AcquireSourceAsync(evidenceSession, transactionId, profileId, mode, observedAt, token);
        if (completed is null || !adoption.MatchesCurrent(completed.Source, clock.GetUtcNow())
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        return new(transactionId, adoption, observedAt, adoption.ValidUntil);
    }

    internal async Task<DeploymentLiveState?> ObserveAsync(
        ModerationConsumerEvidenceSession session, Guid transactionId, Guid sourceId, Guid profileId,
        ModerationNativeMode mode, DateTimeOffset observedAt, CancellationToken token)
    {
        if (await session.AcquireNativeAsync(transactionId, observedAt, token) is not { } native
            || !ReferenceEquals(native.Context, context) || !Enum.IsDefined(mode)) return null;
        var requirement = native.Policy.Publication;
        var definition = await definitions.ReadDefinitionAsync(native.Authority, transactionId, requirement.Jurisdiction,
            requirement.PolicyId, requirement.PublicationId, observedAt, token);
        if (definition is null || definition.Policy.Publication.ProposedRevision != requirement.ProposedRevision
            || definition.Policy.Proposed.InventoryFingerprint != inventory.Fingerprint
            || ModerationPolicyPublicationProjection.ProposedDigest(definition.Policy.Proposed)
                != ModerationPolicyPublicationProjection.ProposedDigest(native.Policy.Proposed)
            || !definition.Policy.Proposed.Modes.Contains(mode)) return null;
        var profile = definition.Policy.Profiles.SingleOrDefault(row => row.Definition.Id == profileId);
        if (profile is null) return null;
        var obligations = definition.Policy.Consumers.Where(row => row.Binding.Rule == profile.Definition.Rule
            && row.Binding.Mode == mode).OrderBy(row => row.Binding.Capability).ToArray();
        if (obligations.Length == 0 || obligations.Any(row => row.Consumers.IsDefaultOrEmpty)) return null;
        var database = await session.AcquirePrimaryDatabaseAsync(observedAt, token);
        if (database is null || database.TransactionId != transactionId) return null;
        var rows = new List<ModerationDeploymentConsumer>();
        var retained = new List<(IModerationConsumerRuntimeSource Owner, ModerationConsumerRuntimeObservation Observation)>();
        foreach (var obligation in obligations)
            foreach (var contract in obligation.Consumers.OrderBy(row => row.Id, StringComparer.Ordinal))
            {
                if (rows.Count >= 256) return null;
                var owners = runtimeSources.Where(row => row.ConsumerId == contract.Id && row.Mode == mode).ToArray();
                if (owners.Length != 1) return null;
                var runtime = owners[0].ReadCurrent();
                if (runtime is null || runtime.ConsumerId != contract.Id || runtime.Mode != mode
                    || runtime.InventoryFingerprint != inventory.Fingerprint
                    || runtime.ProductArtifactDigest is not { Length: 64 } artifact || !artifact.All(char.IsAsciiHexDigit)) return null;
                var installation = await session.AcquireInstallationAsync(obligation.Binding, contract.Id, observedAt, token);
                if (installation is null || installation.Binding != obligation.Binding || installation.ConsumerId != contract.Id
                    || installation.WriterGeneration != runtime.RuntimeGeneration || installation.ObservedAt != observedAt) return null;
                var installed = await context.ModerationConsumerInstallations.AsNoTracking()
                    .SingleOrDefaultAsync(row => row.Id == installation.SourceId, token);
                if (installed is null || installed.Revision != installation.Revision || installed.WithdrawnAt is not null
                    || installed.RuntimeGeneration != runtime.RuntimeGeneration || installed.ValidUntil != installation.ValidUntil
                    || installed.HostBindingId != native.Authority.Binding.Id || installed.TenantId != native.Authority.Tenant.Id
                    || installed.TrustDomainId != native.Authority.Binding.TrustDomainId
                    || owners[0].ReadCurrent() != runtime) return null;
                rows.Add(ModerationDeploymentConsumer.Capture(sourceId, rows.Count, contract.Id,
                    ModerationPolicyPublicationProjection.Digest(obligation.Binding), ModerationPolicyPublicationProjection.Digest(contract),
                    installed.Id, installed.Revision, installed.WriterGeneration, runtime.RuntimeGeneration, artifact, installed.ValidUntil));
                retained.Add((owners[0], runtime));
            }
        // All participating owners are reread after the complete read, not just after their own slot.
        if (await session.AcquirePrimaryDatabaseAsync(observedAt, token) != database
            || retained.Any(row => row.Owner.ReadCurrent() != row.Observation)
            || await session.AcquireNativeAsync(transactionId, observedAt, token) is null) return null;
        var until = rows.Select(row => row.ValidUntil).Append(definition.ValidUntil).Min();
        return clock.GetUtcNow() < until ? new(definition, database, rows, until) : null;
    }

    internal static bool Matches(ModerationDeploymentSource source, DeploymentLiveState live)
    {
        if (source.DefinitionId != live.Definition.DefinitionId
            || source.ProposedStateDigest != ModerationPolicyPublicationProjection.ProposedDigest(live.Definition.Policy.Proposed)
            || source.InventoryFingerprint != live.Definition.Policy.Proposed.InventoryFingerprint
            || source.DatabaseConfigurationDigest != live.Database.ConfigurationDigest
            || source.DatabaseProviderCode != live.Database.ProviderCode || source.DatabaseProviderVersion != live.Database.ProviderVersion
            || source.ValidUntil > live.ValidUntil || source.Consumers.Count != live.Consumers.Count) return false;
        return source.Consumers.Zip(live.Consumers).All(pair =>
            pair.First.Ordinal == pair.Second.Ordinal && pair.First.ConsumerId == pair.Second.ConsumerId
            && pair.First.BindingDigest == pair.Second.BindingDigest && pair.First.ContractDigest == pair.Second.ContractDigest
            && pair.First.InstallationId == pair.Second.InstallationId
            && pair.First.InstallationRevision == pair.Second.InstallationRevision
            && pair.First.InstallationWriterGeneration == pair.Second.InstallationWriterGeneration
            && pair.First.RuntimeGeneration == pair.Second.RuntimeGeneration
            && pair.First.ProductArtifactDigest == pair.Second.ProductArtifactDigest && pair.First.ValidUntil == pair.Second.ValidUntil);
    }

    internal static string Slot(ModerationSourceAuthority authority, Guid publicationId, Guid profileId, ModerationNativeMode mode) =>
        $"moderation.deployment:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{publicationId:N}:{profileId:N}:{(int)mode}";

    internal static bool Administrator(ModerationSourceAuthority authority, Guid humanId) =>
        authority.FencedHumans.Contains(humanId) && authority.Administrators.Contains(humanId);

    internal static RecordModerationDeploymentSource RecordRequest(ModerationDeploymentSource source, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty,
        Target = jurisdiction,
        ExpectedQualificationRevision = source.WriterGeneration - 1,
        SourceId = source.Id,
        PolicyId = source.PolicyId,
        ExpectedPolicyRevision = source.PolicyRevision,
        PublicationId = source.PublicationId,
        ProfileId = source.ProfileId,
        Mode = (ModerationNativeMode)source.ModeCode,
        ValidUntil = source.ValidUntil
    };

    internal static AdoptModerationDeploymentSource AdoptionRequest(ModerationDeploymentAdoption adoption, AuthorityScope jurisdiction) => new()
    {
        OperationId = Guid.Empty,
        Target = jurisdiction,
        ExpectedQualificationRevision = adoption.WriterGeneration - 1,
        SourceId = adoption.SourceId,
        ExpectedSourceRevision = adoption.SourceRevision,
        ExpectedSourceWriterGeneration = adoption.SourceWriterGeneration,
        AdoptionId = adoption.Id,
        ValidUntil = adoption.ValidUntil
    };

    private async Task<bool> HasReceiptAsync(ModerationNativeEvidenceAcquisition native, ModerationQualificationMutation template,
        ModerationQualificationSourceKind kind, Guid id, long revision, Guid humanId, long generation,
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
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().FullName + "\n" + JsonSerializer.Serialize(request, request.GetType()))));
        return receipt.SourceId == id && receipt.SourceKind == kind && receipt.ResultRevision == revision
            && receipt.RequestDigest == digest && receipt.PrincipalId == humanId && receipt.QualificationRevision == generation
            && receipt.CompletedAt == completedAt && generation <= native.Authority.Epoch.Revision
            && receipt.HostBindingId == native.Authority.Binding.Id && receipt.TrustDomainId == native.Authority.Binding.TrustDomainId
            && receipt.TenantId == native.Authority.Tenant.Id;
    }

    internal sealed record DeploymentLiveState(ModerationPolicyDefinitionSourceOwner.Definition Definition, ModerationMeasurementDatabaseObservation Database,
        List<ModerationDeploymentConsumer> Consumers, DateTimeOffset ValidUntil);
}
