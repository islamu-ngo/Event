using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Infrastructure.Storage;

public sealed class StorageProviderBindingService(
    IStorageProviderBindingRepository repository,
    IHierarchicalSettingsResolver settings,
    IRetainedSecretResolver secrets,
    IOptions<LocalFileStorageOptions> localOptions,
    IS3ClientFactory clientFactory,
    ILoggerFactory loggerFactory) : IStorageProviderBindingService
{
    public async Task<StorageProviderBinding> CaptureAsync(
        string provider, Guid tenantId, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant is required to capture storage policy.", nameof(tenantId));
        StorageProviderBinding binding;
        switch (provider)
        {
            case StorageProviders.Local:
                binding = StorageProviderBinding.Local(Path.GetFullPath(localOptions.Value.RootPath));
                break;
            case StorageProviders.S3Compatible:
                var context = new SettingContext(TenantId: tenantId);
                var endpoint = await settings.ResolveAsync<string>(GovernanceSettingKeys.Storage.Endpoint, context, cancellationToken);
                var bucket = await settings.ResolveAsync<string>(GovernanceSettingKeys.Storage.BucketName, context, cancellationToken);
                var region = await settings.ResolveAsync<string>(GovernanceSettingKeys.Storage.Region, context, cancellationToken);
                var pathStyle = await settings.ResolveAsync<bool>(GovernanceSettingKeys.Storage.ForcePathStyle, context, cancellationToken);
                var accessKey = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId, tenantId, cancellationToken);
                var secretKey = await secrets.CaptureAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, tenantId, cancellationToken);
                binding = StorageProviderBinding.S3(endpoint!, bucket!, string.IsNullOrWhiteSpace(region) ? "us-east-1" : region,
                    pathStyle, accessKey, secretKey);
                break;
            default:
                throw new InvalidOperationException("storage_provider_binding_unsupported");
        }
        await repository.AddAsync(binding, cancellationToken);
        return binding;
    }

    public async Task<IFileStorageProvider> ResolveAsync(Guid bindingId, CancellationToken cancellationToken)
    {
        var binding = await repository.GetByIdAsync(bindingId, cancellationToken)
            ?? throw new InvalidOperationException("storage_provider_binding_unavailable");
        return binding.Provider switch
        {
            StorageProviders.Local => new LocalFileStorageProvider(
                Options.Create(new LocalFileStorageOptions { RootPath = binding.LocalRootPath!, CreateRootIfMissing = false }),
                loggerFactory.CreateLogger<LocalFileStorageProvider>(), boundTarget: true),
            StorageProviders.S3Compatible => CreateS3(binding),
            _ => throw new InvalidOperationException("storage_provider_binding_unsupported")
        };
    }

    private S3FileStorageProvider CreateS3(StorageProviderBinding binding)
    {
        var config = new BoundS3Configuration(binding, secrets);
        return new S3FileStorageProvider(config, clientFactory,
            new S3PreflightVerifier(config, clientFactory, loggerFactory.CreateLogger<S3PreflightVerifier>()), boundTarget: true);
    }

    private sealed class BoundS3Configuration(StorageProviderBinding binding, IRetainedSecretResolver secrets) : IS3ConfigResolver
    {
        public async Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default)
        {
            var accessKey = await secrets.ResolveAsync(binding.AccessKeyReference!, cancellationToken);
            var secretKey = await secrets.ResolveAsync(binding.SecretKeyReference!, cancellationToken);
            if (!accessKey.IsResolved || string.IsNullOrWhiteSpace(accessKey.Value)
                || !secretKey.IsResolved || string.IsNullOrWhiteSpace(secretKey.Value))
                throw new InvalidOperationException("storage_secret_unavailable");
            return new S3Configuration
            {
                Endpoint = binding.Endpoint!, BucketName = binding.BucketName!, Region = binding.Region!,
                ForcePathStyle = binding.ForcePathStyle,
                AccessKeyId = accessKey.Value, SecretAccessKey = secretKey.Value
            };
        }

        public async Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
            await ResolveAsync(cancellationToken) is not null;

        public void InvalidateCache(Guid? tenantId = null) { }
    }
}
