using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private void RequireConsumerRuntime(ModerationQualificationMutation mutation)
    {
        var expected = mutation switch
        {
            InstallModerationConsumerSource source => (source.ConsumerId, source.Mode,
                source.ExpectedRuntimeGeneration, source.ExpectedInventoryFingerprint),
            PrepareModerationConsumerMeasurementSource source => (source.ConsumerId, source.Binding.Mode,
                source.ExpectedRuntimeGeneration, source.ExpectedInventoryFingerprint),
            _ => ((string ConsumerId, Explore.Application.Services.Moderation.ModerationNativeMode Mode,
                string ExpectedRuntimeGeneration, string ExpectedInventoryFingerprint)?)null
        };
        if (expected is not { } sourceArtifacts) return;
        var owner = consumerSources?.SingleOrDefault(row => row.ConsumerId == sourceArtifacts.ConsumerId && row.Mode == sourceArtifacts.Mode);
        var current = owner?.ReadCurrent();
        if (deploymentInventory is null || !deploymentInventory.Consumers.ContainsKey(sourceArtifacts.ConsumerId)
            || current is null || current.ConsumerId != sourceArtifacts.ConsumerId || current.Mode != sourceArtifacts.Mode
            || current.InventoryFingerprint != deploymentInventory.Fingerprint
            || current.RuntimeGeneration != sourceArtifacts.ExpectedRuntimeGeneration
            || current.InventoryFingerprint != sourceArtifacts.ExpectedInventoryFingerprint)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
    }

    private async Task<QualifiedSource> InstallConsumerAsync(
        InstallModerationConsumerSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (source.Target != authority.Tenant || source.ValidUntil <= now)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        RequireConsumerRuntime(source);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.consumer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{source.ConsumerId}:{(int)source.Mode}", token);
        var previous = await context.ModerationConsumerInstallations.AsNoTracking()
            .Where(row => row.HostBindingId == authority.Binding.Id && row.ConsumerId == source.ConsumerId && row.ModeCode == (int)source.Mode)
            .OrderByDescending(row => row.WriterGeneration).FirstOrDefaultAsync(token);
        if (previous is not null)
        {
            previous.ValidatePersistedState();
            if (!authority.FencedHumans.Contains(previous.InstalledByHumanId)
                || previous.WithdrawnByHumanId is { } revoker && !authority.FencedHumans.Contains(revoker))
                throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
            await RequireConsumerInstallationReceiptAsync(previous, authority, token);
            if (previous.WithdrawnAt is null && previous.ValidUntil > now)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        }
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerInstallation>(context, source.InstallationId, token);
        if (await context.ModerationConsumerInstallations.AnyAsync(row => row.Id == source.InstallationId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var installed = ModerationConsumerInstallation.Install(source.InstallationId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, source.ConsumerId, (int)source.Mode,
            source.ExpectedRuntimeGeneration, source.ExpectedInventoryFingerprint, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, source.ValidUntil);
        context.ModerationConsumerInstallations.Add(installed);
        return new(ModerationQualificationSourceKind.ConsumerInstallation, installed.Id, 1, MutationValidUntil: installed.ValidUntil);
    }

    private async Task<QualifiedSource> WithdrawConsumerAsync(
        WithdrawModerationConsumerSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (source.Target != authority.Tenant) throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var discovered = await context.ModerationConsumerInstallations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.InstallationId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.consumer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{discovered.ConsumerId}:{discovered.ModeCode}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationConsumerInstallation>(context, source.InstallationId, token);
        var installed = await context.ModerationConsumerInstallations.SingleAsync(row => row.Id == source.InstallationId, token);
        installed.ValidatePersistedState();
        if (installed.HostBindingId != authority.Binding.Id || installed.TrustDomainId != authority.Binding.TrustDomainId
            || installed.TenantId != authority.Tenant.Id || !authority.FencedHumans.Contains(installed.InstalledByHumanId)
            || installed.ConsumerId != discovered.ConsumerId || installed.ModeCode != discovered.ModeCode)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (installed.Revision != source.ExpectedInstallationRevision || installed.WithdrawnAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RequireConsumerInstallationReceiptAsync(installed, authority, token);
        installed.Withdraw(source.ExpectedInstallationRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.ConsumerInstallation, installed.Id, installed.Revision);
    }

    private Task RequireConsumerInstallationReceiptAsync(
        ModerationConsumerInstallation installed, ModerationSourceAuthority authority, CancellationToken token) =>
        RequireTargetSourceReceiptAsync(new InstallModerationConsumerSource
        {
            OperationId = Guid.Empty, Target = authority.Tenant, ExpectedQualificationRevision = installed.WriterGeneration - 1,
            InstallationId = installed.Id, ConsumerId = installed.ConsumerId,
            Mode = (Explore.Application.Services.Moderation.ModerationNativeMode)installed.ModeCode,
            ExpectedRuntimeGeneration = installed.RuntimeGeneration, ExpectedInventoryFingerprint = installed.InventoryFingerprint,
            ValidUntil = installed.ValidUntil
        }, ModerationQualificationSourceKind.ConsumerInstallation, installed.Id, 1,
            installed.InstalledByHumanId, installed.WriterGeneration, installed.InstalledAt, authority, token);
}
