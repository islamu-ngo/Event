
using SmtpSettingsDatabase = Explore.Tests.Shared.Settings.SmtpSettingsDatabase;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Infrastructure.Tests.Fixtures;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class TenantSmtpGovernanceTests
{
    [Test]
    public async Task DelegationLockIsVisibleInEffectiveSettingsMetadata()
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, true);

        var setting = await fixture.Settings.ResolveWithMetadataAsync(
            GovernanceSettingKeys.Email.SmtpHost, new SettingContext(fixture.TenantId));

        await Assert.That(setting!.IsLocked).IsTrue();
        await Assert.That(SettingValueSerializer.DeserializeString(setting.Value)).IsEqualTo("smtp.instance.test");
    }

}
