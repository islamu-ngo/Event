using Explore.Domain;

namespace Explore.Application.Contracts.Infrastructure;

/// <summary>Managed storage resolves captured targets, never the current provider configuration.</summary>
public interface IStorageProviderBindingService
{
    /// <summary>Stages the captured binding; caller commits before external storage writes.</summary>
    Task<StorageProviderBinding> CaptureAsync(string provider, Guid tenantId, CancellationToken cancellationToken);
    Task<IFileStorageProvider> ResolveAsync(Guid bindingId, CancellationToken cancellationToken);
}

public static class StorageProviderBindingServiceExtensions
{
    public static async Task<IFileStorageProvider> ResolveTargetAsync(this IStorageProviderBindingService bindings,
        Guid? bindingId, string provider, CancellationToken cancellationToken)
    {
        if (bindingId is null || bindingId == Guid.Empty)
            throw new InvalidOperationException("storage_provider_binding_unavailable");
        var target = await bindings.ResolveAsync(bindingId.Value, cancellationToken);
        if (!string.Equals(target.Provider, provider, StringComparison.Ordinal))
            throw new InvalidOperationException("storage_provider_binding_mismatch");
        return target;
    }
}
