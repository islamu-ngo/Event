using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.StorageObject.Validators;
using Explore.Application.Models.Storage;
using Explore.Domain;

namespace Explore.Infrastructure.Storage;

/// <summary>Commits generated/import producer authority before any persistent provider write.</summary>
public sealed class ManagedStorageProducer(
    IStorageProviderBindingService bindings, IStorageProducerOperationRepository objects, IUnitOfWork unitOfWork)
{
    public async Task<StagedStorageWrite> WriteAsync(
        string provider, FileStorageWriteInput input, CancellationToken cancellationToken)
    {
        Guid id = Guid.CreateVersion7();
        string key = $"tenants/{input.TenantId:N}/generated/{id:N}";
        DateTime now = DateTime.UtcNow;
        var operation = await unitOfWork.ExecuteSerializableAsync(async ct =>
        {
            var binding = await bindings.CaptureAsync(provider, input.TenantId, ct);
            var producer = StorageProducerOperation.Create(id, input.TenantId, binding, key, now);
            await objects.AddProducerAsync(producer, ct);
            return producer;
        }, cancellationToken);
        try
        {
            var target = await bindings.ResolveTargetAsync(operation.ProviderBindingId, provider, cancellationToken);
            var written = await target.WriteAsync(input with { ObjectKey = key }, cancellationToken);
            if (written.Provider == provider && written.ObjectKey == key)
                await unitOfWork.ExecuteSerializableAsync(async ct =>
                {
                    await objects.SettleProducerAsync(operation, written.ProviderVersionId, ct);
                    return true;
                }, CancellationToken.None);
            if (written.Provider != provider || written.ObjectKey != key
                || input.ExpectedSizeBytes is { } size && written.SizeBytes != size
                || input.MaxSizeBytes is { } maximum && written.SizeBytes > maximum
                || !string.Equals(written.ContentType, input.ContentType, StringComparison.OrdinalIgnoreCase)
                || !StorageObjectMetadataValidation.BeValidSha256HexDigest(written.Sha256Checksum))
                throw new InvalidOperationException("storage_producer_result_mismatch");
            return new(id, input.TenantId, operation.ProviderBindingId, written);
        }
        catch
        {
            // Unknown writes remain AwaitingProducer; failure never proves absence.
            await RetireAsync(id, input.TenantId, CancellationToken.None);
            throw;
        }
    }

    public Task RetireAsync(Guid id, Guid tenantId, CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            ct => objects.RetireProducerAsync(id, tenantId, DateTime.UtcNow, ct), cancellationToken);
}
