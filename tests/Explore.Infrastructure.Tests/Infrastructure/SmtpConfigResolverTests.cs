using System.Text.Json;
using SettingsDatabase = Explore.Tests.Shared.Settings.SmtpSettingsDatabase;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Models;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Infrastructure.Mail;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Tests.Fixtures;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.Extensions;

namespace Explore.Infrastructure.Tests.Infrastructure;

[Category(InfrastructureTestCategories.Email)]
public sealed class SmtpConfigResolverTests
{
    [Test]
    public async Task ResolveAsync_DeliveryDisabled_DoesNotReadSecrets()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.RejectSecretReads();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(capability.Enabled).IsFalse();
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_NoDeliverySetting_DefaultsToDisabled()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(capability.Enabled).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_EnabledAnonymousInstance_IsAvailable()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(capability.Enabled).IsTrue();
        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(transport).IsNotNull();
        await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
        await Assert.That(transport.Security).IsEqualTo(SmtpSecurityMode.StartTls);
        await Assert.That(transport.Username).IsNull();
        await Assert.That(transport.Password).IsNull();
    }

    [Test]
    public async Task ResolveAsync_InstanceTransport_ResolvesOnlyInstanceCredentials()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        await fixture.AddCredentialsAsync(SecretScope.Tenant);
        var credentials = fixture.ResolveCredentials(SecretScope.Instance, null);

        var transport = await fixture.Smtp.ResolveAsync();
        var json = JsonSerializer.Serialize(await fixture.Capabilities.ResolveAsync(fixture.TenantId));

        await Assert.That(transport!.Username).IsEqualTo(credentials.Username);
        await Assert.That(transport.Password).IsEqualTo(credentials.Password);
        await Assert.That(json).DoesNotContain(credentials.Username);
        await Assert.That(json).DoesNotContain(credentials.Password);
        await Assert.That(json).DoesNotContain(transport.Host);
        await Assert.That(json).DoesNotContain(transport.FromAddress);
    }

    [Test]
    public async Task ResolveAsync_TenantHostWithoutOwnBindings_DoesNotInheritInstanceCredentials()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        await fixture.ConfigureTenantAsync();
        fixture.RejectSecretReads();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Tenant);
        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(transport!.Host).IsEqualTo("smtp.tenant.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@tenant.test");
        await Assert.That(transport.Username).IsNull();
        await Assert.That(transport.Password).IsNull();
    }

    [Test]
    public async Task ResolveAsync_TenantEnablesOwnTransport_DoesNotEnableInstanceOrOtherTenants()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        await fixture.ConfigureTenantAsync();
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        fixture.RejectSecretReads();

        var tenant = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var instance = await fixture.Capabilities.ResolveAsync(null);
        var otherTenant = await fixture.Capabilities.ResolveAsync(Guid.CreateVersion7());
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(tenant.State).IsEqualTo(EmailDeliveryState.Available);
        await Assert.That(tenant.Enabled).IsTrue();
        await Assert.That(tenant.Scope).IsEqualTo(SecretScope.Tenant);
        await Assert.That(tenant.TenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(transport!.Host).IsEqualTo("smtp.tenant.test");
        await Assert.That(instance.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(instance.Enabled).IsFalse();
        await Assert.That(otherTenant.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(otherTenant.Enabled).IsFalse();
    }

    [Test]
    public async Task ResolveAsync_TenantEnableWithoutOwnHost_CannotActivateDisabledInstanceTransport()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.RejectSecretReads();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Disabled);
        await Assert.That(capability.Enabled).IsFalse();
        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_TenantCredentials_ComposesTenantTransport()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.AddCredentialsAsync(SecretScope.Tenant);
        var credentials = fixture.ResolveCredentials(SecretScope.Tenant, fixture.TenantId);

        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(transport!.Host).IsEqualTo("smtp.tenant.test");
        await Assert.That(transport.Username).IsEqualTo(credentials.Username);
        await Assert.That(transport.Password).IsEqualTo(credentials.Password);
    }

    [Test]
    [Arguments(SecretScope.Instance)]
    [Arguments(SecretScope.Tenant)]
    public async Task ResolveAsync_TenantCredentialsReturnedFromWrongOwner_FailsClosed(SecretScope returnedScope)
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.AddCredentialsAsync(SecretScope.Tenant);
        fixture.ResolveCredentials(returnedScope, returnedScope == SecretScope.Instance ? null : Guid.CreateVersion7(),
            requestedTenantId: fixture.TenantId);

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(capability.Enabled).IsTrue();
        await Assert.That(capability.ReasonCode).IsNotNullOrEmpty();
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_TenantHostWithoutOwnSender_FailsClosed()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, false);
        await fixture.SetTenantAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.tenant.test");

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResolveAsync_GovernanceLock_UsesInstanceTransport(bool explicitLock)
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        if (explicitLock)
            await fixture.SetInstanceAsync(GovernanceSettingKeys.TenantDelegation.LockSmtp, true);
        else
            (await fixture.Writer.ApplyAsync([new(TenantId: null,
                Key: GovernanceSettingKeys.TenantDelegation.LockSmtp,
                Kind: EmailDeliverySettingMutationKind.Remove)], fixture.ActorId)).EnsureAccepted();

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
    }

    [Test]
    public async Task ResolveAsync_InstanceHostLock_OverridesTenantDelegation()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.Settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost, SettingScope.Instance,
            Guid.Empty, fixture.ActorId);

        var capability = await fixture.Capabilities.ResolveAsync(fixture.TenantId);
        var transport = await fixture.Smtp.ResolveAsync();

        await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
        await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
    }

    [Test]
    public async Task ResolveAsync_DisabledAfterResolution_DoesNotReuseTransportOrReadSecrets()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.ResolveCredentials(SecretScope.Instance, null);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNotNull();

        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, false);
        fixture.RejectSecretReads();

        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
        await Assert.That((await fixture.Capabilities.ResolveAsync(null)).State)
            .IsEqualTo(EmailDeliveryState.Disabled);
    }

    [Test]
    public async Task ResolveAsync_EnabledWithoutHost_IsUnconfigured()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Unconfigured);
        await Assert.That(capability.Enabled).IsTrue();
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_EnabledWithoutSender_IsMisconfigured()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.DeliveryEnabled, true);
        await fixture.SetInstanceAsync(GovernanceSettingKeys.Email.SmtpHost, "smtp.instance.test");

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_PartialCredentials_FailsClosed()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        fixture.Secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Smtp.Username, null, Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                SecretDefinitionRegistry.Keys.Smtp.Username, Guid.CreateVersion7().ToString("N"),
                SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow)));

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_UnauthorizedCredentials_FailsClosed()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.AddCredentialsAsync(SecretScope.Instance);
        fixture.Secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(SecretResolutionResult.Unauthorized);

        var capability = await fixture.Capabilities.ResolveAsync(null);

        await Assert.That(capability.State).IsEqualTo(EmailDeliveryState.Degraded);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ResolveAsync_ResolvedEmptyCredentials_DoesNotDowngradeToAnonymous()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        fixture.Secrets.ResolveAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(call => SecretResolutionResult.Resolved(new ResolvedSecret(call.ArgAt<string>(0),
                string.Empty, SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, DateTimeOffset.UtcNow)));

        await Assert.That((await fixture.Capabilities.ResolveAsync(null)).State)
            .IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    [Arguments(SecretScope.Instance, true)]
    [Arguments(SecretScope.Instance, false)]
    [Arguments(SecretScope.Tenant, true)]
    [Arguments(SecretScope.Tenant, false)]
    public async Task ResolveAsync_DeclaredCredentialsMissingFromAuthority_DoesNotDowngradeToAnonymous(
        SecretScope scope, bool completeBindings)
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        if (scope == SecretScope.Tenant)
            await fixture.ConfigureTenantAsync();
        await fixture.AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Username, scope);
        if (completeBindings)
            await fixture.AddBindingAsync(SecretDefinitionRegistry.Keys.Smtp.Password, scope);

        await Assert.That((await fixture.Capabilities.ResolveAsync(fixture.TenantId)).State)
            .IsEqualTo(EmailDeliveryState.Misconfigured);
        await Assert.That(await fixture.Smtp.ResolveAsync()).IsNull();
    }

    [Test]
    public async Task ApplySettingsAsync_LockedHost_PreservesLockAndInstanceOwnership()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.ConfigureTenantAsync();
        await fixture.Settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost, SettingScope.Instance,
            Guid.Empty, fixture.ActorId);

        await fixture.InstanceSmtp.ApplySettingsAsync(new InstanceSmtpSettingsDto
        {
            Host = "smtp.updated-instance.test",
            FromAddress = "events@updated-instance.test"
        });
        fixture.Settings.InvalidateCache(SettingScope.Instance);

        var setting = await fixture.Settings.ResolveWithMetadataAsync(GovernanceSettingKeys.Email.SmtpHost,
            new SettingContext(fixture.TenantId));
        var transport = await fixture.Smtp.ResolveAsync();
        await Assert.That(setting!.Source).IsEqualTo(SettingSource.SystemLocked);
        await Assert.That(transport!.Host).IsEqualTo("smtp.updated-instance.test");
        await Assert.That(transport.FromAddress).IsEqualTo("events@updated-instance.test");
    }

    [Test]
    public async Task ApplySettingsAsync_LaterWriteFails_RollsBackEarlierHostWrite()
    {
        await using var fixture = await SettingsDatabase.CreateAsync();
        await fixture.ConfigureInstanceAsync();
        await fixture.RejectPortUpdatesAsync();

        await Assert.ThrowsAsync<DbUpdateException>(() => fixture.InstanceSmtp.ApplySettingsAsync(
            new InstanceSmtpSettingsDto { Host = "smtp.rollback.test", Port = 2525, FromAddress = "events@rollback.test" }));

        var settings = await fixture.InstanceSmtp.ReadSettingsAsync();
        await Assert.That(settings.Host).IsEqualTo("smtp.instance.test");
        await Assert.That(settings.Port).IsEqualTo(587);
        await Assert.That(settings.FromAddress).IsEqualTo("events@instance.test");
    }

}
