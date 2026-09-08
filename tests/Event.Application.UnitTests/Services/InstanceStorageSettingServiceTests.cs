using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Instance;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models.Common;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Constants;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class InstanceStorageSettingServiceTests
{
    [Test]
    public async Task ApplySettingsAsync_WhenOnlyPolicyGroupIsSupplied_WritesOnlyPolicyKeys()
    {
        var writes = CaptureWrites(out var repository);
        var service = CreateService(repository);

        await service.ApplySettingsAsync(
            CreateSettings(),
            new PatchInstanceStorageSettingsDto
            {
                Policy = OptionalUpdate<InstanceStoragePolicyWriteDto>.Set(new())
            });

        await Assert.That(writes).IsEquivalentTo([
            GovernanceSettingKeys.Storage.Provider,
            GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
            GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
            GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
            GovernanceSettingKeys.TenantDelegation.LockStorage,
            GovernanceSettingKeys.Storage.RouteMatrix
        ]);
    }

    [Test]
    public async Task ApplySettingsAsync_WhenOnlyS3GroupIsSupplied_WritesOnlyS3KeysAndSkipsRedactedCredentials()
    {
        var writes = CaptureWrites(out var repository);
        var service = CreateService(repository);

        await service.ApplySettingsAsync(
            CreateSettings(),
            new PatchInstanceStorageSettingsDto
            {
                S3Configuration = OptionalUpdate<InstanceS3ConfigurationWriteDto>.Set(new())
            });

        await Assert.That(writes).IsEquivalentTo([
            GovernanceSettingKeys.Storage.Endpoint,
            GovernanceSettingKeys.Storage.PublicEndpoint,
            GovernanceSettingKeys.Storage.BucketName,
            GovernanceSettingKeys.Storage.Region,
            GovernanceSettingKeys.Storage.ForcePathStyle,
            GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes
        ]);
    }

    private static List<string> CaptureWrites(out ISystemSettingRepository repository)
    {
        repository = Substitute.For<ISystemSettingRepository>();
        var writes = new List<string>();
        repository.UpsertAsync(
                Arg.Do<SystemSetting>(setting => writes.Add(setting.SettingKey)),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>(null));
        return writes;
    }

    private static InstanceStorageSettingService CreateService(ISystemSettingRepository repository)
        => new(
            repository,
            Substitute.For<IStoragePolicyResolver>(),
            Substitute.For<IStorageUsageCounterRepository>(),
            Substitute.For<IStorageObjectRepository>(),
            Substitute.For<IUnitOfWork>(),
            CreateMetrics());

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private static InstanceStorageSettingsDto CreateSettings() => new()
    {
        Provider = StorageProviders.S3Compatible,
        DefaultMaxUploadBytes = 10,
        DefaultTenantQuotaBytes = 100,
        InstanceMaxUploadBytes = 100,
        LockTenantStorage = true,
        S3Endpoint = "https://s3.example.test",
        S3PublicEndpoint = "https://public.example.test",
        S3BucketName = "events",
        S3Region = "fsn1",
        S3ForcePathStyle = true,
        S3UploadUrlExpirationMinutes = 60
    };
}
