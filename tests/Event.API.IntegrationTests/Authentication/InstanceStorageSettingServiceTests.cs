using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    public async Task ApplySettingsAsync_WhenOnlyPolicyGroupIsSupplied_WritesOnlyPolicyKeys()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Storage.BucketName,
                Value = "\"omitted-bucket\"",
                IsLocked = true
            });
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        await using var baseline = fixture.Native.CreateDatabase();
        var before = await baseline.SystemSettings.AsNoTracking().ToDictionaryAsync(
            row => row.SettingKey, row => new { row.Value, row.IsLocked, row.CreatedBy, row.UpdatedBy });

        await ApplyStorageConfigurationAsync(selected.Services.GetRequiredService<IInstanceStorageSettingService>(),
            new UpdateInstanceStorageSettingsCommand
            {
                UserId = fixture.Binding.LocalSubjectId,
                Patch = new()
                {
                    Policy = OptionalUpdate<InstanceStoragePolicyWriteDto>.Set(new()
                    {
                        Provider = StorageProviders.S3Compatible,
                        DefaultMaxUploadBytes = 10,
                        DefaultTenantQuotaBytes = 100,
                        InstanceMaxUploadBytes = 100,
                        LockTenantStorage = true
                    })
                }
            });

        await using var observer = fixture.Native.CreateDatabase();
        var after = await observer.SystemSettings.AsNoTracking().ToListAsync();
        var written = after.Where(row => !before.TryGetValue(row.SettingKey, out var prior)
            || !prior.Equals(new { row.Value, row.IsLocked, row.CreatedBy, row.UpdatedBy })).ToList();
        await Assert.That(before.Keys.All(key => after.Any(row => row.SettingKey == key))).IsTrue();
        await Assert.That(written.Select(row => row.SettingKey).ToList()).IsEquivalentTo([
            GovernanceSettingKeys.Storage.Provider,
            GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
            GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes,
            GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
            GovernanceSettingKeys.TenantDelegation.LockStorage,
            GovernanceSettingKeys.Storage.RouteMatrix
        ]);
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.Provider).Value)
            .IsEqualTo("\"s3_compatible\"");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes).Value)
            .IsEqualTo("10");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes).Value)
            .IsEqualTo("100");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.InstanceMaxUploadBytes).Value)
            .IsEqualTo("100");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.TenantDelegation.LockStorage).Value)
            .IsEqualTo("true");
        await Assert.That(written.All(row => !row.IsLocked && row.CreatedBy == fixture.Binding.LocalSubjectId
            && row.UpdatedBy == fixture.Binding.LocalSubjectId)).IsTrue();
        var omitted = await observer.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName);
        await Assert.That(omitted.Value).IsEqualTo("\"omitted-bucket\"");
        await Assert.That(omitted.IsLocked).IsTrue();
        await Assert.That(omitted.UpdatedBy).IsNull();
    }

    [Test]
    public async Task ApplySettingsAsync_WhenOnlyS3GroupIsSupplied_WritesOnlyS3KeysAndSkipsRedactedCredentials()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.SystemSettings.Add(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
                Value = "10",
                IsLocked = true
            });
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        await using var baseline = fixture.Native.CreateDatabase();
        var before = await baseline.SystemSettings.AsNoTracking().ToDictionaryAsync(
            row => row.SettingKey, row => new { row.Value, row.IsLocked, row.CreatedBy, row.UpdatedBy });

        await ApplyStorageConfigurationAsync(selected.Services.GetRequiredService<IInstanceStorageSettingService>(),
            new UpdateInstanceStorageSettingsCommand
            {
                UserId = fixture.Binding.LocalSubjectId,
                Patch = new()
                {
                    S3Configuration = OptionalUpdate<InstanceS3ConfigurationWriteDto>.Set(new()
                    {
                        Endpoint = "https://s3.example.test",
                        PublicEndpoint = "https://public.example.test",
                        BucketName = "events",
                        Region = "fsn1",
                        ForcePathStyle = true,
                        UploadUrlExpirationMinutes = 60
                    })
                }
            });

        await using var observer = fixture.Native.CreateDatabase();
        var after = await observer.SystemSettings.AsNoTracking().ToListAsync();
        var written = after.Where(row => !before.TryGetValue(row.SettingKey, out var prior)
            || !prior.Equals(new { row.Value, row.IsLocked, row.CreatedBy, row.UpdatedBy })).ToList();
        await Assert.That(before.Keys.All(key => after.Any(row => row.SettingKey == key))).IsTrue();
        await Assert.That(written.Select(row => row.SettingKey).ToList()).IsEquivalentTo([
            GovernanceSettingKeys.Storage.Endpoint,
            GovernanceSettingKeys.Storage.PublicEndpoint,
            GovernanceSettingKeys.Storage.BucketName,
            GovernanceSettingKeys.Storage.Region,
            GovernanceSettingKeys.Storage.ForcePathStyle,
            GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes
        ]);
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.Endpoint).Value)
            .IsEqualTo("\"https://s3.example.test\"");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.PublicEndpoint).Value)
            .IsEqualTo("\"https://public.example.test\"");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName).Value)
            .IsEqualTo("\"events\"");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.Region).Value)
            .IsEqualTo("\"fsn1\"");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.ForcePathStyle).Value)
            .IsEqualTo("true");
        await Assert.That(written.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.UploadUrlExpirationMinutes).Value)
            .IsEqualTo("60");
        await Assert.That(written.All(row => !row.IsLocked && row.CreatedBy == fixture.Binding.LocalSubjectId
            && row.UpdatedBy == fixture.Binding.LocalSubjectId)).IsTrue();
        var omitted = await observer.SystemSettings.AsNoTracking()
            .SingleAsync(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes);
        await Assert.That(omitted.Value).IsEqualTo("10");
        await Assert.That(omitted.IsLocked).IsTrue();
        await Assert.That(omitted.UpdatedBy).IsNull();
    }
}
