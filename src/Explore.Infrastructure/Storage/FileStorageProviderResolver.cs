using Explore.Application.Contracts.Infrastructure;

namespace Explore.Infrastructure.Storage;

public sealed class FileStorageProviderResolver : IFileStorageProviderResolver
{
    private readonly IReadOnlyDictionary<string, IFileStorageProvider> _providers;

    public FileStorageProviderResolver(IEnumerable<IFileStorageProvider> providers)
    {
        _providers = providers.ToDictionary(
            provider => provider.Provider,
            StringComparer.OrdinalIgnoreCase);
    }

    public IFileStorageProvider GetRequired(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new ArgumentException("A storage provider key is required.", nameof(provider));
        }

        if (_providers.TryGetValue(provider, out var storageProvider))
        {
            return storageProvider;
        }

        throw new InvalidOperationException($"Storage provider '{provider}' is not registered.");
    }
}
