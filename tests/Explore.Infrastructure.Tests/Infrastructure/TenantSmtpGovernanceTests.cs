
using SmtpSettingsDatabase = Explore.Tests.Shared.Settings.SmtpSettingsDatabase;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Infrastructure.Tests.Fixtures;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class TenantSmtpGovernanceTests
{
    public enum Mutation { Set, Reset, Lock, Unlock }

    [Test]
    [Arguments(Mutation.Set)]
    [Arguments(Mutation.Reset)]
    [Arguments(Mutation.Lock)]
    [Arguments(Mutation.Unlock)]
    public async Task DelegationLockRejectsMutationAndPreservesDormantOverride(Mutation mutation)
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, true);

        var result = await fixture.Writer.ApplyAsync([new(TenantId: fixture.TenantId,
            Key: GovernanceSettingKeys.Email.SmtpHost,
            Kind: mutation switch
            {
                Mutation.Set => EmailDeliverySettingMutationKind.SetValue,
                Mutation.Reset => EmailDeliverySettingMutationKind.Remove,
                _ => EmailDeliverySettingMutationKind.SetLock
            },
            Value: mutation == Mutation.Set ? "\"smtp.replacement.test\"" : null,
            IsLocked: mutation is Mutation.Lock or Mutation.Unlock ? mutation == Mutation.Lock : null)], fixture.ActorId);
        await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Locked);
        await Assert.That(result.Changes).IsEmpty();
        await Assert.ThrowsAsync<ValidationException>(() => ApplyAsync(fixture, mutation));

        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        var restored = await fixture.Settings.ResolveWithMetadataAsync(GovernanceSettingKeys.Email.SmtpHost, new SettingContext(fixture.TenantId));
        await Assert.That(SettingValueSerializer.DeserializeString(restored!.Value)).IsEqualTo("smtp.tenant.test");
        await Assert.That(restored.IsLocked).IsFalse();
    }

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

    [Test]
    [Arguments(Mutation.Set, "smtp.replacement.test", false)]
    [Arguments(Mutation.Reset, "smtp.instance.test", false)]
    [Arguments(Mutation.Lock, "smtp.tenant.test", true)]
    [Arguments(Mutation.Unlock, "smtp.tenant.test", false)]
    public async Task UnlockedDelegationAppliesMutationToRealTenantSettings(Mutation mutation, string expectedHost, bool expectedLock)
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        if (mutation == Mutation.Unlock)
            await ApplyAsync(fixture, Mutation.Lock);

        await ApplyAsync(fixture, mutation);

        var resolved = await fixture.Settings.ResolveWithMetadataAsync(
            GovernanceSettingKeys.Email.SmtpHost, new SettingContext(fixture.TenantId));
        await Assert.That(SettingValueSerializer.DeserializeString(resolved!.Value)).IsEqualTo(expectedHost);
        await Assert.That(resolved.IsLocked).IsEqualTo(expectedLock);
    }

    [Test]
    public async Task UnlockedTenantPortOverrideIsResolvedWithItsDeclaredValue()
    {
        await using var fixture = await SmtpSettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();

        await fixture.Settings.SetValueAsync(GovernanceSettingKeys.Email.SmtpPort, "465", SettingScope.Tenant,
            fixture.TenantId, fixture.ActorId);

        await Assert.That(await fixture.Settings.ResolveAsync<int>(
            GovernanceSettingKeys.Email.SmtpPort, new SettingContext(fixture.TenantId))).IsEqualTo(465);
        await Assert.That((await fixture.Smtp.ResolveAsync())!.Port).IsEqualTo(465);
    }

    private static Task ApplyAsync(SmtpSettingsDatabase fixture, Mutation mutation) => mutation switch
    {
        Mutation.Set => fixture.Settings.SetValueAsync(GovernanceSettingKeys.Email.SmtpHost,
            "\"smtp.replacement.test\"", SettingScope.Tenant, fixture.TenantId, fixture.ActorId),
        Mutation.Reset => fixture.Settings.RemoveOverrideAsync(GovernanceSettingKeys.Email.SmtpHost,
            SettingScope.Tenant, fixture.TenantId, fixture.ActorId),
        Mutation.Lock => fixture.Settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost,
            SettingScope.Tenant, fixture.TenantId, fixture.ActorId),
        Mutation.Unlock => fixture.Settings.UnlockAsync(GovernanceSettingKeys.Email.SmtpHost,
            SettingScope.Tenant, fixture.TenantId, fixture.ActorId),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
    };
}
