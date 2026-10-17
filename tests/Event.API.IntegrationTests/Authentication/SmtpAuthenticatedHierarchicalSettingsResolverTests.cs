using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class SmtpAuthenticatedHierarchicalSettingsResolverTests
{
    [Test]
    public async Task SetValueAsync_SucceedsAtInstanceScope()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().SetValueAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, value: "465",
                scope: SettingScope.Instance, scopeId: Guid.Empty, actorId: fixture.Subject.PrincipalId));

        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            await Assert.That(await services.GetRequiredService<IHierarchicalSettingsResolver>().ResolveAsync<int>(
                key: GovernanceSettingKeys.Email.SmtpPort, context: new SettingContext())).IsEqualTo(465);
        });
    }

    [Test]
    public async Task RemoveOverrideAsync_WhenSystemSettingIsLocked_DoesNotRemoveTenantOverride()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        // The fixture persists an unlocked instance port of 587 with SMTP delegation allowed.
        await fixture.ExecuteAsync(tenantOwner: true, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().SetValueAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, value: "465",
                scope: SettingScope.Tenant, scopeId: fixture.TenantId, actorId: fixture.Subject.PrincipalId));
        await fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().LockAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
                scopeId: Guid.Empty, actorId: fixture.Subject.PrincipalId));

        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
            var locked = await settings.ResolveWithMetadataAsync(
                key: GovernanceSettingKeys.Email.SmtpPort,
                context: new SettingContext(TenantId: fixture.TenantId));
            await Assert.That(locked).IsNotNull();
            await Assert.That(locked!.IsLocked).IsTrue();
            await Assert.That(locked.Value).IsEqualTo("587");

            ValidationException? exception = await Assert.ThrowsAsync<ValidationException>(() =>
                settings.RemoveOverrideAsync(
                    key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Tenant,
                    scopeId: fixture.TenantId, actorId: fixture.Subject.PrincipalId));

            await Assert.That(exception!.Errors).Contains("SMTP settings are locked by current governance.");
        });

        await fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().UnlockAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
                scopeId: Guid.Empty, actorId: fixture.Subject.PrincipalId));
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            await Assert.That(await services.GetRequiredService<IHierarchicalSettingsResolver>().ResolveAsync<int>(
                key: GovernanceSettingKeys.Email.SmtpPort,
                context: new SettingContext(TenantId: fixture.TenantId))).IsEqualTo(465);
        });
    }

    [Test]
    public async Task LockAsync_SetsIsLockedToTrue()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().LockAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
                scopeId: Guid.Empty, actorId: fixture.Subject.PrincipalId));

        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var setting = await services.GetRequiredService<IHierarchicalSettingsResolver>().ResolveWithMetadataAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, context: new SettingContext());
            await Assert.That(setting).IsNotNull();
            await Assert.That(setting!.IsLocked).IsTrue();
            await Assert.That(setting.Value).IsEqualTo("587");
        });
    }

    [Test]
    public async Task UnlockAsync_SucceedsAtInstanceScope()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().LockAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
                scopeId: Guid.Empty, actorId: fixture.Subject.PrincipalId));

        await fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().UnlockAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, scope: SettingScope.Instance,
                scopeId: Guid.Empty, actorId: fixture.Subject.PrincipalId));

        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var setting = await services.GetRequiredService<IHierarchicalSettingsResolver>().ResolveWithMetadataAsync(
                key: GovernanceSettingKeys.Email.SmtpPort, context: new SettingContext());
            await Assert.That(setting).IsNotNull();
            await Assert.That(setting!.IsLocked).IsFalse();
            await Assert.That(setting.Value).IsEqualTo("587");
        });
    }
}
