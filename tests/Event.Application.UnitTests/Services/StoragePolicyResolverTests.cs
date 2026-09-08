using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models.Storage;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class StoragePolicyResolverTests
{
    private static readonly Guid TenantId = Guid.CreateVersion7();

    private readonly IHierarchicalSettingsResolver _settingsResolver = Substitute.For<IHierarchicalSettingsResolver>();
    private readonly IFileStorageProviderResolver _providerResolver = Substitute.For<IFileStorageProviderResolver>();
    private readonly IS3ConfigResolver _s3ConfigResolver = Substitute.For<IS3ConfigResolver>();

    [Test]
    public async Task ResolveAsync_WithoutTenant_UsesInstancePolicy()
    {
        SetupInstanceSettings(CreateSettings(provider: StorageProviders.Local, maxUploadBytes: 20, quotaBytes: 200, instanceMaxUploadBytes: 100));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(null);

        await Assert.That(policy.TenantId).IsNull();
        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.Local);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(20);
        await Assert.That(policy.TenantQuotaBytes).IsEqualTo(200);
        await Assert.That(policy.InstanceMaxUploadBytes).IsEqualTo(100);
        await Assert.That(policy.TenantOverridesAllowed).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_MultiTenantWithStorageLocked_IgnoresTenantOverrides()
    {
        SetupInstanceSettings(CreateSettings(
            deploymentMode: "MultiTenant",
            lockStorage: true,
            provider: StorageProviders.Local,
            maxUploadBytes: 20,
            quotaBytes: 200,
            instanceMaxUploadBytes: 100));
        SetupTenantSettings(CreateSettings(provider: StorageProviders.S3Compatible, maxUploadBytes: 90, quotaBytes: 900));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(TenantId);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.Local);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(20);
        await Assert.That(policy.TenantQuotaBytes).IsEqualTo(200);
        await Assert.That(policy.TenantOverridesAllowed).IsFalse();
        await Assert.That(policy.TenantStorageLocked).IsTrue();
    }

    [Test]
    public async Task ResolveAsync_MultiTenantUnlocked_AppliesTenantProviderAndCapsMaxUpload()
    {
        SetupInstanceSettings(CreateSettings(
            deploymentMode: "MultiTenant",
            lockStorage: false,
            provider: StorageProviders.Local,
            maxUploadBytes: 20,
            quotaBytes: 200,
            instanceMaxUploadBytes: 50));
        SetupTenantSettings(CreateSettings(provider: StorageProviders.S3Compatible, maxUploadBytes: 90, quotaBytes: 900));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(TenantId);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(50);
        await Assert.That(policy.TenantQuotaBytes).IsEqualTo(900);
        await Assert.That(policy.InstanceMaxUploadBytes).IsEqualTo(50);
        await Assert.That(policy.TenantOverridesAllowed).IsTrue();
        await Assert.That(policy.TenantStorageLocked).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_SingleTenant_AllowsTenantOverrideEvenWhenDelegationLocked()
    {
        SetupInstanceSettings(CreateSettings(
            deploymentMode: "SingleTenant",
            lockStorage: true,
            provider: StorageProviders.Local,
            maxUploadBytes: 20,
            quotaBytes: 200,
            instanceMaxUploadBytes: 100));
        SetupTenantSettings(CreateSettings(provider: StorageProviders.S3Compatible, maxUploadBytes: 75, quotaBytes: 750));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(TenantId);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(75);
        await Assert.That(policy.TenantQuotaBytes).IsEqualTo(750);
        await Assert.That(policy.TenantOverridesAllowed).IsTrue();
    }

    [Test]
    public async Task ResolveAsync_WithUnsupportedProvider_FallsBackToLocal()
    {
        SetupInstanceSettings(CreateSettings(provider: StorageProviders.LegacyExternal));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(null);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.Local);
    }


    [Test]
    public async Task ResolveAsync_WhenProviderIsDefaultLocalAndS3Configured_UsesS3CompatibleProvider()
    {
        SetupInstanceSettings(CreateSettings(provider: StorageProviders.Local));
        _s3ConfigResolver.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(null);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(policy.RouteKey).IsEqualTo(StorageRouteKeys.General);
    }

    [Test]
    public async Task ResolveAsync_WhenProviderIsSystemLevelLocalAndS3Configured_UsesS3CompatibleProvider()
    {
        var settings = CreateSettings(provider: StorageProviders.Local).ToList();
        var providerSettingIndex = settings.FindIndex(s => s.Key == GovernanceSettingKeys.Storage.Provider);
        settings[providerSettingIndex] = new ResolvedSetting
        {
            Key = GovernanceSettingKeys.Storage.Provider,
            Value = SettingValueSerializer.Serialize(StorageProviders.Local),
            ValueType = SettingValueType.String,
            Source = SettingSource.SystemLocked,
            IsLocked = true
        };

        SetupInstanceSettings(settings);
        _s3ConfigResolver.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(true);
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(null);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.S3Compatible);
    }

    [Test]
    public async Task ResolveAsync_WhenProviderIsDefaultLocalAndS3Missing_KeepsLocalProvider()
    {
        SetupInstanceSettings(CreateSettings(provider: StorageProviders.Local));
        _s3ConfigResolver.IsConfiguredAsync(Arg.Any<CancellationToken>()).Returns(false);
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(null);

        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.Local);
    }

    [Test]
    public async Task ResolveProviderAsync_UsesEffectiveProvider()
    {
        SetupInstanceSettings(CreateSettings(deploymentMode: "MultiTenant", lockStorage: false));
        SetupTenantSettings(CreateSettings(provider: StorageProviders.S3Compatible));
        var provider = Substitute.For<IFileStorageProvider>();
        _providerResolver.GetRequired(StorageProviders.S3Compatible).Returns(provider);
        var resolver = CreateResolver();

        var result = await resolver.ResolveProviderAsync(TenantId);

        await Assert.That(result).IsSameReferenceAs(provider);
        _providerResolver.Received(1).GetRequired(StorageProviders.S3Compatible);
    }

    [Test]
    public async Task ResolveAsync_WithRouteMatrix_SelectsProviderFromUploadIntent()
    {
        SetupInstanceSettings(CreateSettings(
            provider: StorageProviders.S3Compatible,
            maxUploadBytes: 25,
            instanceMaxUploadBytes: 100,
            routes: new[]
            {
                Route(StorageRouteKeys.Images, StorageProviders.Local, 30),
                Route(StorageRouteKeys.Documents, StorageProviders.S3Compatible, 40),
                Route(StorageRouteKeys.General, StorageProviders.S3Compatible, 25)
            }));
        var resolver = CreateResolver();

        var imagePolicy = await resolver.ResolveAsync(
            null,
            new StoragePolicyIntent(StorageObjectPurposes.EventImage, StorageObjectVisibilities.PublicImage, "image/png", ExpectedSizeBytes: 10));
        var documentPolicy = await resolver.ResolveAsync(
            null,
            new StoragePolicyIntent(StorageObjectPurposes.Document, StorageObjectVisibilities.PrivateOwner, "application/pdf", ExpectedSizeBytes: 10));

        await Assert.That(imagePolicy.RouteKey).IsEqualTo(StorageRouteKeys.Images);
        await Assert.That(imagePolicy.Provider).IsEqualTo(StorageProviders.Local);
        await Assert.That(imagePolicy.MaxUploadBytes).IsEqualTo(30);
        await Assert.That(documentPolicy.RouteKey).IsEqualTo(StorageRouteKeys.Documents);
        await Assert.That(documentPolicy.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(documentPolicy.MaxUploadBytes).IsEqualTo(40);
    }

    [Test]
    public async Task ResolveAsync_WhenRouteMaxExceedsInstanceCeiling_CapsSelectedRouteMaxUpload()
    {
        SetupInstanceSettings(CreateSettings(
            maxUploadBytes: 25,
            instanceMaxUploadBytes: 50,
            routes: new[]
            {
                Route(StorageRouteKeys.Images, StorageProviders.Local, 90)
            }));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(
            null,
            new StoragePolicyIntent(StorageObjectPurposes.ProfileImage, StorageObjectVisibilities.PublicImage, "image/webp", ExpectedSizeBytes: 10));

        await Assert.That(policy.RouteKey).IsEqualTo(StorageRouteKeys.Images);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(50);
        await Assert.That(policy.SelectedRoute.MaxUploadBytes).IsEqualTo(50);
    }

    [Test]
    public async Task ResolveAsync_WhenTenantStorageLocked_IgnoresTenantRouteMatrixOverrides()
    {
        SetupInstanceSettings(CreateSettings(
            deploymentMode: "MultiTenant",
            lockStorage: true,
            provider: StorageProviders.Local,
            maxUploadBytes: 20,
            instanceMaxUploadBytes: 100,
            routes: new[]
            {
                Route(StorageRouteKeys.Images, StorageProviders.Local, 20)
            }));
        SetupTenantSettings(CreateSettings(
            provider: StorageProviders.S3Compatible,
            maxUploadBytes: 90,
            routes: new[]
            {
                Route(StorageRouteKeys.Images, StorageProviders.S3Compatible, 90)
            }));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(
            TenantId,
            new StoragePolicyIntent(StorageObjectPurposes.EventImage, StorageObjectVisibilities.PublicImage, "image/jpeg", ExpectedSizeBytes: 10));

        await Assert.That(policy.TenantOverridesAllowed).IsFalse();
        await Assert.That(policy.RouteKey).IsEqualTo(StorageRouteKeys.Images);
        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.Local);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(20);
    }

    [Test]
    public async Task ResolveAsync_WhenRouteMissing_FallsBackToDefaultProviderAndDefaultMaxUpload()
    {
        SetupInstanceSettings(CreateSettings(
            provider: StorageProviders.S3Compatible,
            maxUploadBytes: 25,
            instanceMaxUploadBytes: 100,
            routes: new[]
            {
                Route(StorageRouteKeys.Images, StorageProviders.Local, 30)
            }));
        var resolver = CreateResolver();

        var policy = await resolver.ResolveAsync(
            null,
            new StoragePolicyIntent(StorageObjectPurposes.Attachment, StorageObjectVisibilities.PrivateOwner, "text/plain", ExpectedSizeBytes: 10));

        await Assert.That(policy.RouteKey).IsEqualTo(StorageRouteKeys.General);
        await Assert.That(policy.Provider).IsEqualTo(StorageProviders.S3Compatible);
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(25);
    }

    private StoragePolicyResolver CreateResolver()
        => new(_settingsResolver, _providerResolver, _s3ConfigResolver);

    private void SetupInstanceSettings(IReadOnlyList<ResolvedSetting> settings)
    {
        _settingsResolver
            .ResolveBatchAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Is<SettingContext>(context => context.TenantId == null),
                Arg.Any<CancellationToken>())
            .Returns(settings);
    }

    private void SetupTenantSettings(IReadOnlyList<ResolvedSetting> settings)
    {
        _settingsResolver
            .ResolveBatchAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Is<SettingContext>(context => context.TenantId == TenantId),
                Arg.Any<CancellationToken>())
            .Returns(settings);
    }

    private static IReadOnlyList<ResolvedSetting> CreateSettings(
        string deploymentMode = "MultiTenant",
        bool lockStorage = true,
        string provider = StorageProviders.Local,
        long maxUploadBytes = 10 * 1024 * 1024,
        long quotaBytes = 1024L * 1024 * 1024,
        long instanceMaxUploadBytes = 100L * 1024 * 1024,
        IReadOnlyList<StorageRouteSetting>? routes = null)
        =>
        [
            Setting(GovernanceSettingKeys.Deployment.Mode, deploymentMode, SettingValueType.String),
            Setting(GovernanceSettingKeys.TenantDelegation.LockStorage, lockStorage, SettingValueType.Boolean),
            Setting(GovernanceSettingKeys.Storage.Provider, provider, SettingValueType.String),
            Setting(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, maxUploadBytes, SettingValueType.Long),
            Setting(GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, quotaBytes, SettingValueType.Long),
            Setting(GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, instanceMaxUploadBytes, SettingValueType.Long),
            Setting(GovernanceSettingKeys.Storage.RouteMatrix, new StorageRouteMatrixDocument(1, routes ?? Array.Empty<StorageRouteSetting>()), SettingValueType.Json)
        ];

    private static StorageRouteSetting Route(string routeKey, string provider, long maxUploadBytes)
        => new(routeKey, provider, maxUploadBytes);

    private static ResolvedSetting Setting<T>(string key, T value, SettingValueType valueType)
        => new()
        {
            Key = key,
            Value = SettingValueSerializer.Serialize(value),
            ValueType = valueType,
            Source = SettingSource.SystemDefault,
            IsLocked = false
        };
}
