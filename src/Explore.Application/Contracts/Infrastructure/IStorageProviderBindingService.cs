using Explore.Domain;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Resource storage uses this port, never current provider-key resolution.</summary>
public interface IStorageProviderBindingService
{
    /// <summary>Stages the captured binding; caller commits before external storage writes.</summary>
    Task<StorageProviderBinding> CaptureAsync(string provider, Guid tenantId, CancellationToken cancellationToken);
    Task<IFileStorageProvider> ResolveAsync(Guid bindingId, CancellationToken cancellationToken);
}
