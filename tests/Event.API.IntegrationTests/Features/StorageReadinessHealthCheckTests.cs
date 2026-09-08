using Event.Api.IntegrationTests.Fixtures;
using Explore.API.HealthChecks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Domain;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[Category(TestCategories.Fast)]
[Category("StorageHealth")]
public sealed class StorageReadinessHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_WhenLocalProviderAvailable_ReturnsHealthyWithoutS3()
    {
        var provider = CreateProvider(new FileStorageProviderStatus(
            StorageProviders.Local,
            IsAvailable: true,
            SupportsServerSideStreaming: true,
            SupportsBrowserDirectUpload: false,
            Message: "Local storage root is writable."));
        var healthCheck = CreateHealthCheck(StorageProviders.Local, provider);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Data["selectedProvider"]).IsEqualTo(StorageProviders.Local);
        await Assert.That(result.Data["provider"]).IsEqualTo(StorageProviders.Local);
        await Assert.That(result.Data["supportsBrowserDirectUpload"]).IsEqualTo(false);
        await Assert.That(result.Data.ContainsKey("endpoint")).IsFalse();
        await Assert.That(result.Data.ContainsKey("bucket")).IsFalse();
        await Assert.That(result.Data.ContainsKey("path")).IsFalse();
        await provider.Received(1).TestAsync(Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CheckHealthAsync_WhenSelectedS3Unavailable_ReturnsUnhealthyWithFailureCode()
    {
        var provider = CreateProvider(new FileStorageProviderStatus(
            StorageProviders.S3Compatible,
            IsAvailable: false,
            SupportsServerSideStreaming: true,
            SupportsBrowserDirectUpload: true,
            FailureCode: "s3_not_configured",
            Message: "S3-compatible storage is not configured."));
        var healthCheck = CreateHealthCheck(StorageProviders.S3Compatible, provider);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("S3-compatible storage is not configured");
        await Assert.That(result.Data["selectedProvider"]).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(result.Data["failureCode"]).IsEqualTo("s3_not_configured");
        await Assert.That(result.Data.ContainsKey("secret")).IsFalse();
        await Assert.That(result.Data.ContainsKey("accessKey")).IsFalse();
    }

    [Test]
    public async Task CheckHealthAsync_WhenLocalRootUnavailable_ReturnsUnhealthyWithOperatorSafeData()
    {
        var provider = CreateProvider(new FileStorageProviderStatus(
            StorageProviders.Local,
            IsAvailable: false,
            SupportsServerSideStreaming: true,
            SupportsBrowserDirectUpload: false,
            FailureCode: "local_storage_unavailable",
            Message: "Local storage root is not writable."));
        var healthCheck = CreateHealthCheck(StorageProviders.Local, provider);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("not writable");
        await Assert.That(result.Data["failureCode"]).IsEqualTo("local_storage_unavailable");
        await Assert.That(result.Data.ContainsKey("path")).IsFalse();
        await Assert.That(result.Data.ContainsKey("root")).IsFalse();
    }

    [Test]
    public async Task CheckHealthAsync_WhenSelectedProviderCannotResolve_ReturnsUnhealthy()
    {
        var policyResolver = Substitute.For<IStoragePolicyResolver>();
        policyResolver.ResolveAsync(null, Arg.Any<CancellationToken>())
            .Returns(CreatePolicy(StorageProviders.Local));
        var providerResolver = Substitute.For<IFileStorageProviderResolver>();
        providerResolver.GetRequired(StorageProviders.Local)
            .Returns(_ => throw new InvalidOperationException("Storage provider 'local' is not registered."));
        var healthCheck = new StorageReadinessHealthCheck(policyResolver, providerResolver);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Unhealthy);
        await Assert.That(result.Description).Contains("could not be resolved");
        await Assert.That(result.Data["failureCode"]).IsEqualTo("storage_provider_resolution_failed");
        await Assert.That(result.Data["reason"]).IsEqualTo(nameof(InvalidOperationException));
    }

    private static StorageReadinessHealthCheck CreateHealthCheck(
        string selectedProvider,
        IFileStorageProvider provider)
    {
        var policyResolver = Substitute.For<IStoragePolicyResolver>();
        policyResolver.ResolveAsync(null, Arg.Any<CancellationToken>())
            .Returns(CreatePolicy(selectedProvider));
        var providerResolver = Substitute.For<IFileStorageProviderResolver>();
        providerResolver.GetRequired(selectedProvider).Returns(provider);

        return new StorageReadinessHealthCheck(policyResolver, providerResolver);
    }

    private static IFileStorageProvider CreateProvider(FileStorageProviderStatus status)
    {
        var provider = Substitute.For<IFileStorageProvider>();
        provider.Provider.Returns(status.Provider);
        provider.TestAsync(Arg.Any<CancellationToken>()).Returns(status);
        return provider;
    }

    private static ResolvedStoragePolicy CreatePolicy(string provider)
        => new(
            TenantId: null,
            Provider: provider,
            MaxUploadBytes: 1024,
            TenantQuotaBytes: 4096,
            InstanceMaxUploadBytes: 8192,
            TenantOverridesAllowed: false,
            TenantStorageLocked: true,
            ProviderSource: SettingSource.SystemDefault,
            MaxUploadSource: SettingSource.SystemDefault,
            QuotaSource: SettingSource.SystemDefault);
}
