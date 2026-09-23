using Explore.Application.Contracts.Persistence;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Settings;

public sealed class EventResourceGovernancePolicyReaderTests
{
    [Test]
    public async Task MissingRowsUseClosedResourceDefaultsWithoutOptingIntoUnscannedFiles()
    {
        var systems = Substitute.For<ISystemSettingRepository>();
        var tenants = Substitute.For<ITenantSettingRepository>();
        tenants.GetByTenantAndKeys(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<TenantSetting>());
        var policy = await new EventResourceGovernancePolicyReader(systems, tenants)
            .ReadAsync(Guid.CreateVersion7(), default);
        await Assert.That(policy).IsNotNull();
        await Assert.That(policy!.EnabledDeliveryTypes).IsEquivalentTo(
            [EventResourceDeliveryTypeEnum.StoredFile, EventResourceDeliveryTypeEnum.ExternalLink]);
        await Assert.That(policy.AllowUnscannedDocuments).IsFalse();
        await Assert.That(policy.ExternalOrigins).IsEmpty();
        await Assert.That(policy.MaxUploadBytes).IsEqualTo(10485760L);
        await Assert.That(policy.AuditRetentionDays).IsEqualTo(30);
        await Assert.That(policy.MaxActiveResources).IsEqualTo(500);
    }

    [Test]
    public async Task TighteningInstanceBytesImmediatelyRestrictsAnOlderTenantOverride()
    {
        var systems = Substitute.For<ISystemSettingRepository>();
        var tenants = Substitute.For<ITenantSettingRepository>();
        var tenantId = Guid.CreateVersion7();
        string key = GovernanceSettingKeys.EventResources.MaxUploadBytes;
        string currentCeiling = "10485760";
        systems.GetByKey(key, Arg.Any<CancellationToken>()).Returns(_ => new SystemSetting
        {
            SettingKey = key, Value = currentCeiling, ValueType = SettingValueType.Long, IsLocked = false
        });
        tenants.GetByTenantAndKeys(tenantId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new TenantSetting { TenantId = tenantId, Tenant = null!, SettingKey = key, Value = "8388608" }]);
        var reader = new EventResourceGovernancePolicyReader(systems, tenants);
        await Assert.That((await reader.ReadAsync(tenantId, default))!.MaxUploadBytes).IsEqualTo(8388608L);
        currentCeiling = "4194304";
        await Assert.That((await reader.ReadAsync(tenantId, default))!.MaxUploadBytes).IsEqualTo(4194304L);
    }

    [Test]
    public async Task NativeInstanceLockCannotBeBypassedBySingleTenantConvenienceInheritance()
    {
        var systems = Substitute.For<ISystemSettingRepository>();
        var tenants = Substitute.For<ITenantSettingRepository>();
        var tenantId = Guid.CreateVersion7();
        string key = GovernanceSettingKeys.EventResources.EnabledAudiences;
        systems.GetByKey(GovernanceSettingKeys.Deployment.Mode, Arg.Any<CancellationToken>())
            .Returns(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Deployment.Mode, Value = "\"SingleTenant\"",
                ValueType = SettingValueType.String
            });
        systems.GetByKey(key, Arg.Any<CancellationToken>()).Returns(new SystemSetting
        {
            SettingKey = key, Value = "[\"Organizer\"]", ValueType = SettingValueType.Json, IsLocked = true
        });
        tenants.GetByTenantAndKeys(tenantId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new TenantSetting { TenantId = tenantId, Tenant = null!, SettingKey = key, Value = "[\"Public\"]" }]);
        var policy = await new EventResourceGovernancePolicyReader(systems, tenants).ReadAsync(tenantId, default);
        await Assert.That(policy!.EnabledAudiences).IsEquivalentTo([EventResourceAudienceKindEnum.Organizer]);
    }

    [Test]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("-1")]
    [Arguments("\"10485760\"")]
    public async Task InvalidPersistedCeilingCannotFallBackToAWiderDefault(string malformed)
    {
        var systems = Substitute.For<ISystemSettingRepository>();
        var tenants = Substitute.For<ITenantSettingRepository>();
        string key = GovernanceSettingKeys.EventResources.MaxUploadBytes;
        systems.GetByKey(key, Arg.Any<CancellationToken>()).Returns(new SystemSetting
        {
            SettingKey = key, Value = malformed, ValueType = SettingValueType.Long
        });
        tenants.GetByTenantAndKeys(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(new List<TenantSetting>());
        await Assert.That(await new EventResourceGovernancePolicyReader(systems, tenants)
            .ReadAsync(Guid.CreateVersion7(), default)).IsNull();
    }

    [Test]
    [Arguments(false, 10485760L)]
    [Arguments(true, 8388608L)]
    public async Task NativeStorageDelegationAndItsFreshHardCeilingAlsoBoundResourceBytes(bool singleTenant, long expected)
    {
        var systems = Substitute.For<ISystemSettingRepository>();
        var tenants = Substitute.For<ITenantSettingRepository>();
        var tenantId = Guid.CreateVersion7();
        string storageCeiling = "104857600";
        systems.GetByKey(GovernanceSettingKeys.Deployment.Mode, Arg.Any<CancellationToken>())
            .Returns(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Deployment.Mode,
                Value = singleTenant ? "\"SingleTenant\"" : "\"MultiTenant\"", ValueType = SettingValueType.String
            });
        systems.GetByKey(GovernanceSettingKeys.EventResources.MaxUploadBytes, Arg.Any<CancellationToken>())
            .Returns(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.EventResources.MaxUploadBytes, Value = "20971520", ValueType = SettingValueType.Long
            });
        systems.GetByKey(GovernanceSettingKeys.Storage.InstanceMaxUploadBytes, Arg.Any<CancellationToken>())
            .Returns(_ => new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Storage.InstanceMaxUploadBytes,
                Value = storageCeiling, ValueType = SettingValueType.Long
            });
        tenants.GetByTenantAndKeys(tenantId, Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([new TenantSetting
            {
                TenantId = tenantId, Tenant = null!, SettingKey = GovernanceSettingKeys.Storage.DefaultMaxUploadBytes,
                Value = "8388608"
            }]);
        var reader = new EventResourceGovernancePolicyReader(systems, tenants);
        await Assert.That((await reader.ReadAsync(tenantId, default))!.MaxUploadBytes).IsEqualTo(expected);
        storageCeiling = "4194304";
        await Assert.That((await reader.ReadAsync(tenantId, default))!.MaxUploadBytes).IsEqualTo(4194304L);
    }
}
