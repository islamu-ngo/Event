using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Services.Moderation;

/// <summary>Joins approved completed measurements to independently current native deployment facts.</summary>
public sealed class ModerationConsumerConformanceReader(
    ModerationCapabilityRegistry inventory, IModerationMeasurementCatalogueSource catalogues, TimeProvider clock)
{
    public async Task<ModerationCapabilityRegistration?> ReadAsync(
        IModerationConsumerEvidenceSession session, IModerationConsumerRuntimeSource runtimeSource,
        Guid transactionId, ModerationActivationBinding binding, DateTimeOffset observedAt, CancellationToken token)
    {
        var consumerId = runtimeSource.ConsumerId;
        if (transactionId == Guid.Empty || session.TransactionId != transactionId
            || runtimeSource.Mode != binding.Mode || observedAt.Offset != TimeSpan.Zero
            || observedAt == DateTimeOffset.MinValue || observedAt == DateTimeOffset.MaxValue)
            return null;
        if (!inventory.RequiredConsumers(binding.Capability, binding.Mode).Contains(consumerId, StringComparer.Ordinal)
            || !inventory.Consumers.TryGetValue(consumerId, out var entry))
            return null;
        var contract = entry.Contract;
        var runtime = runtimeSource.ReadCurrent();
        if (runtime is null || runtime.ConsumerId != consumerId || runtime.Mode != binding.Mode
            || runtime.InventoryFingerprint != inventory.Fingerprint
            || !ModerationMeasurementCatalogue.IsDigest(runtime.ProductArtifactDigest))
            return null;
        var installed = await session.AcquireInstallationAsync(binding, consumerId, observedAt, token);
        var primary = await session.AcquirePrimaryDatabaseAsync(observedAt, token);
        var measured = await session.AcquireMeasurementAsync(binding, consumerId, observedAt, token);
        if (installed is null || primary is null || measured is null
            || installed.Binding != binding || installed.ConsumerId != consumerId
            || installed.ObservedAt != observedAt || primary.ObservedAt != observedAt || measured.ObservedAt != observedAt
            || primary.TransactionId != transactionId || measured.TransactionId != transactionId || measured.Runtime != runtime)
            return null;
        var receipt = measured.Receipt;
        if (receipt.ConsumerId != consumerId || receipt.RuntimeGeneration != runtime.RuntimeGeneration
            || receipt.InstallationId != installed.SourceId || receipt.InstallationRevision != installed.Revision
            || installed.WriterGeneration != runtime.RuntimeGeneration
            || receipt.InventoryFingerprint != runtime.InventoryFingerprint
            || receipt.ProductArtifactDigest != runtime.ProductArtifactDigest
            || receipt.PrimaryConfigurationDigest != primary.ConfigurationDigest
            || receipt.ProviderCode != primary.ProviderCode || receipt.ProviderVersion != primary.ProviderVersion
            || !ModerationMeasurementCatalogue.IsDigest(receipt.TestConfigurationDigest))
            return null;
        var approved = await catalogues.ReadAsync(measured.TrustKey.ApprovedCatalogueDigest, token);
        if (approved is null || approved.Digest != receipt.CatalogueDigest
            || approved.Digest != measured.TrustKey.ApprovedCatalogueDigest
            || approved.Catalogue.ConsumerId != consumerId
            || approved.Catalogue.RuntimeVersion != Environment.Version.ToString()
            || approved.Catalogue.ProductArtifactDigest != runtime.ProductArtifactDigest
            || approved.Catalogue.ConfigurationDigest != receipt.ConfigurationDigest
            || approved.Catalogue.ProviderCode != primary.ProviderCode
            || approved.Catalogue.ProviderVersion != primary.ProviderVersion
            || receipt.ExecutedCount != approved.Catalogue.CaseIds.Length
            || !receipt.CaseIds.SequenceEqual(approved.Catalogue.CaseIds, StringComparer.Ordinal))
            return null;
        var until = installed.ValidUntil < measured.ValidUntil ? installed.ValidUntil : measured.ValidUntil;
        var current = await session.AcquireInstallationAsync(binding, consumerId, observedAt, token);
        var currentPrimary = await session.AcquirePrimaryDatabaseAsync(observedAt, token);
        var currentCatalogue = await catalogues.ReadAsync(measured.TrustKey.ApprovedCatalogueDigest, token);
        var currentMeasurement = await session.AcquireMeasurementAsync(binding, consumerId, observedAt, token);
        var completed = clock.GetUtcNow();
        if (current != installed || currentPrimary != primary || runtimeSource.ReadCurrent() != runtime
            || currentCatalogue?.Digest != approved.Digest || currentMeasurement is null
            || currentMeasurement.TokenDigest != measured.TokenDigest
            || currentMeasurement.Receipt.ReceiptId != receipt.ReceiptId
            || currentMeasurement.Revision != measured.Revision
            || currentMeasurement.WriterGeneration != measured.WriterGeneration
            || currentMeasurement.IssuerEnrollmentId != measured.IssuerEnrollmentId
            || currentMeasurement.ValidUntil != measured.ValidUntil
            || completed < observedAt || completed >= until)
            return null;
        return new(binding, inventory.Fingerprint, contract, installed.SourceId, installed.Revision,
            installed.WriterGeneration, ModerationNativeConformance.Passed, observedAt, until);
    }
}
