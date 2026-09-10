
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Tests.Shared.Settings;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class EmailDeliveryPolicyFreshnessTests
{
    [Test]
    [Arguments(SettingScope.Instance)]
    [Arguments(SettingScope.Tenant)]
    public async Task CommittedDisableCannotBeHiddenByWarmSettingsCache(SettingScope scope)
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await Assert.That((await fixture.Capabilities.ResolveAsync(fixture.TenantId)).State)
            .IsEqualTo(EmailDeliveryState.Available);

        await fixture.SetEmailValueAsync(scope == SettingScope.Instance ? null : fixture.TenantId,
            GovernanceSettingKeys.Email.DeliveryEnabled, "false");

        fixture.RejectSecretReads();
        await Assert.That((await fixture.Capabilities.ResolveAsync(fixture.TenantId)).State)
            .IsEqualTo(EmailDeliveryState.Disabled);
    }

    [Test]
    public async Task CommittedDelegationLockRevokesCachedTenantTransport()
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await Assert.That((await fixture.Smtp.ResolveAsync())!.Host).IsEqualTo("smtp.tenant.test");

        await fixture.SetEmailValueAsync(null, GovernanceSettingKeys.TenantDelegation.LockSmtp, "true");

        await Assert.That((await fixture.Smtp.ResolveAsync())!.Host).IsEqualTo("smtp.instance.test");
    }

}
