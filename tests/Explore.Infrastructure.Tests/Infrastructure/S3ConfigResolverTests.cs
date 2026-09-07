using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Explore.Infrastructure.Tests.Infrastructure;

public sealed class S3ConfigResolverTests : IDisposable
{
    private readonly IHierarchicalSettingsResolver _settings = Substitute.For<IHierarchicalSettingsResolver>();
    private readonly ITenantContext _tenant = Substitute.For<ITenantContext>();
    private readonly ISecretResolver _secrets = Substitute.For<ISecretResolver>();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly Guid _tenantId = Guid.NewGuid();

    public S3ConfigResolverTests() => _tenant.TenantId.Returns(_tenantId);

    public void Dispose() => _cache.Dispose();

    [Test]
    public async Task ResolveAsync_UnconfiguredCredential_DisablesOptionalCapability()
    {
        ConfigureGovernance();
        _secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unconfigured);

        var result = await CreateResolver().ResolveAsync();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task ResolveAsync_ResolvedCredentials_ComposesRuntimeConfiguration()
    {
        ConfigureGovernance();
        var accessKey = Guid.NewGuid().ToString("N");
        var secretKey = Guid.NewGuid().ToString("N");
        ConfigureResolvedCredentials(accessKey, secretKey);

        var result = await CreateResolver().ResolveAsync();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Endpoint).IsEqualTo("https://storage.example.test");
        await Assert.That(result.BucketName).IsEqualTo("bucket");
        await Assert.That(result.AccessKeyId).IsEqualTo(accessKey);
        await Assert.That(result.SecretAccessKey).IsEqualTo(secretKey);
    }

    [Test]
    public async Task ResolveAsync_UnavailableCredential_FailsClosedWithoutProviderDetails()
    {
        ConfigureGovernance();
        _secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unavailable);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateResolver().ResolveAsync());

        await Assert.That(exception.Message).IsEqualTo("storage_secret_unavailable");
    }

    private S3ConfigResolver CreateResolver() => new(
        _settings,
        _tenant,
        _cache,
        _secrets,
        Substitute.For<ILogger<S3ConfigResolver>>());

    private void ConfigureGovernance()
    {
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Storage.Endpoint, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("https://storage.example.test");
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Storage.BucketName, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("bucket");
        _settings.ResolveAsync<string>(GovernanceSettingKeys.Storage.Region, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns("eu-central-1");
        _settings.ResolveAsync<int>(GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(60);
        _settings.ResolveAsync<bool>(GovernanceSettingKeys.Storage.ForcePathStyle, Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private void ConfigureResolvedCredentials(string accessKey, string secretKey)
    {
        _secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Storage.AccessKeyId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(SecretDefinitionRegistry.Keys.Storage.AccessKeyId, accessKey));
        _secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Resolved(SecretDefinitionRegistry.Keys.Storage.SecretAccessKey, secretKey));
    }

    private SecretResolutionResult Resolved(string key, string value) => SecretResolutionResult.Resolved(new ResolvedSecret(
        key,
        value,
        SecretSourceType.EnvironmentVariable,
        SecretScope.Tenant,
        _tenantId,
        DateTimeOffset.UtcNow));
}
