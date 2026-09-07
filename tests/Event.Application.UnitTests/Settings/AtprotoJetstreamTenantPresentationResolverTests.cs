using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Federation;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Settings;

public sealed class AtprotoJetstreamTenantPresentationResolverTests
{
    [Test]
    public async Task ResolveEnabledTenantIds_UsesInstanceLockAndActiveTenantBoundary()
    {
        Guid enabledOverrideTenantId = Guid.CreateVersion7();
        Guid disabledOverrideTenantId = Guid.CreateVersion7();
        ITenantRepository tenants = Substitute.For<ITenantRepository>();
        ISystemSettingRepository systemSettings = Substitute.For<ISystemSettingRepository>();
        ITenantSettingRepository tenantSettings = Substitute.For<ITenantSettingRepository>();
        tenants.GetActiveAsNoTrackingAsync(Arg.Any<CancellationToken>()).Returns([
            Tenant(enabledOverrideTenantId),
            Tenant(disabledOverrideTenantId)
        ]);
        systemSettings.GetByKey(GovernanceSettingKeys.Deployment.Mode, Arg.Any<CancellationToken>())
            .Returns(SystemSetting(GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"", locked: false));
        systemSettings.GetByKey(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Arg.Any<CancellationToken>())
            .Returns(SystemSetting(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "false", locked: false));
        tenantSettings.GetByKeyAcrossTenants(
                GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                Arg.Any<CancellationToken>())
            .Returns([
                TenantSetting(enabledOverrideTenantId, "true"),
                TenantSetting(disabledOverrideTenantId, "false")
            ]);
        var sut = new AtprotoJetstreamTenantPresentationResolver(tenants, systemSettings, tenantSettings);

        IReadOnlyList<Guid> enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledOverrideTenantId]);

        systemSettings.GetByKey(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Arg.Any<CancellationToken>())
            .Returns(SystemSetting(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "true", locked: true));

        enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledOverrideTenantId, disabledOverrideTenantId]);

        systemSettings.GetByKey(GovernanceSettingKeys.Deployment.Mode, Arg.Any<CancellationToken>())
            .Returns(SystemSetting(GovernanceSettingKeys.Deployment.Mode, "\"SingleTenant\"", locked: false));
        systemSettings.GetByKey(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Arg.Any<CancellationToken>())
            .Returns(SystemSetting(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "false", locked: true));

        enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEquivalentTo([enabledOverrideTenantId]);

        systemSettings.GetByKey(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, Arg.Any<CancellationToken>())
            .Returns((SystemSetting?)null);

        enabled = await sut.ResolveEnabledTenantIdsAsync(CancellationToken.None);

        await Assert.That(enabled).IsEmpty();
    }

    private static Tenant Tenant(Guid id) => new()
    {
        Id = id,
        FullName = $"Tenant {id}",
        Slug = id.ToString("N"),
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = new TenantStatus { Id = (int)TenantStatusEnum.Active, MasterCode = "ACTIVE", FullName = "Active", IsActiveState = true },
        CreatedAt = DateTime.UtcNow
    };

    private static SystemSetting SystemSetting(string key, string value, bool locked) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = key,
        Value = value,
        IsLocked = locked,
        CreatedAt = DateTime.UtcNow
    };

    private static TenantSetting TenantSetting(Guid tenantId, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        SettingKey = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
        Value = value,
        CreatedAt = DateTime.UtcNow
    };
}
