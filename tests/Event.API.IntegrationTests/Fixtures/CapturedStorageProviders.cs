using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>Keeps real target capture/resolution while substituting only the external byte provider.</summary>
internal static class CapturedStorageProviders
{
    public static void AddCapturedStorageProviders(this IServiceCollection services)
    {
        services.RemoveAll<IStorageProviderBindingService>();
        services.AddScoped<IStorageProviderBindingService>(sp => new ByteProviders(
            ActivatorUtilities.CreateInstance<StorageProviderBindingService>(sp),
            sp.GetRequiredService<IStorageProviderBindingRepository>(), sp.GetRequiredService<IFileStorageProviderResolver>()));
    }

    public static StorageProviderBinding LocalBinding() => StorageProviderBinding.Local(Path.GetTempPath());

    public static StorageProviderBinding S3Binding() => StorageProviderBinding.S3(
        "https://storage.example.test", "fixture-bucket", "fixture-region", true,
        Reference(SecretDefinitionRegistry.Keys.Storage.AccessKeyId, "FIXTURE_STORAGE_ACCESS"),
        Reference(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, "FIXTURE_STORAGE_SECRET"));

    private static RetainedSecretReference Reference(string key, string environmentName) =>
        RetainedSecretReference.Capture(
            SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, environmentName), "Environment");

    private sealed class ByteProviders(IStorageProviderBindingService bindings,
        IStorageProviderBindingRepository repository, IFileStorageProviderResolver providers)
        : IStorageProviderBindingService
    {
        public async Task<StorageProviderBinding> CaptureAsync(string provider, Guid tenantId, CancellationToken cancellationToken)
        {
            if (provider != StorageProviders.S3Compatible)
                return await bindings.CaptureAsync(provider, tenantId, cancellationToken);
            // The SDK byte boundary is substituted, with this explicitly declared fixture target.
            var binding = S3Binding();
            await repository.AddAsync(binding, cancellationToken);
            return binding;
        }

        public async Task<IFileStorageProvider> ResolveAsync(Guid bindingId, CancellationToken cancellationToken)
        {
            var captured = await bindings.ResolveAsync(bindingId, cancellationToken);
            var provider = providers.GetRequired(captured.Provider);
            if (provider.Provider != captured.Provider)
                throw new InvalidOperationException("Test byte provider differs from captured target.");
            return provider;
        }
    }
}
