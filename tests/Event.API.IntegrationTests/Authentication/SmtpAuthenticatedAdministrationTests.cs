using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Exceptions;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

[NotInParallel]
public sealed class SmtpAuthenticatedAdministrationTests
{
    public enum Mutation { Set, Reset, Lock, Unlock }

    [Test]
    public async Task ResolveAsync_InstanceHostLock_OverridesTenantDelegation()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
            await settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost, SettingScope.Instance,
                Guid.Empty, fixture.Subject.PrincipalId);

            var capability = await services.GetRequiredService<IEmailDeliveryCapabilityResolver>()
                .ResolveAsync(fixture.TenantId);
            var transport = await services.GetRequiredService<ISmtpConfigResolver>().ResolveAsync();
            await Assert.That(capability.Scope).IsEqualTo(SecretScope.Instance);
            await Assert.That(transport!.Host).IsEqualTo("smtp.instance.test");
            await Assert.That(transport.FromAddress).IsEqualTo("events@instance.test");
        });
    }

    [Test]
    public async Task ApplySettingsAsync_LockedHost_PreservesLockAndInstanceOwnership()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
            await settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost, SettingScope.Instance,
                Guid.Empty, fixture.Subject.PrincipalId);
            await services.GetRequiredService<IInstanceSmtpSettingService>().ApplySettingsAsync(
                new InstanceSmtpSettingsDto
                {
                    Host = "smtp.updated-instance.test",
                    FromAddress = "events@updated-instance.test"
                }, fixture.Subject.PrincipalId);
            settings.InvalidateCache(SettingScope.Instance);

            var setting = await settings.ResolveWithMetadataAsync(GovernanceSettingKeys.Email.SmtpHost,
                new SettingContext(fixture.TenantId));
            var transport = await services.GetRequiredService<ISmtpConfigResolver>().ResolveAsync();
            await Assert.That(setting!.Source).IsEqualTo(SettingSource.SystemLocked);
            await Assert.That(transport!.Host).IsEqualTo("smtp.updated-instance.test");
            await Assert.That(transport.FromAddress).IsEqualTo("events@updated-instance.test");
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ApplySettingsAsync_LaterWriteFails_RollsBackEarlierHostWrite(bool reject)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        var interceptor = new AfterSmtpBatchSaved(() =>
        {
            if (reject)
                throw new DbUpdateException("SMTP port write rejected by test database boundary.");
        });
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var smtp = services.GetRequiredService<IInstanceSmtpSettingService>();
            Task ApplyAsync() => smtp.ApplySettingsAsync(new InstanceSmtpSettingsDto
            {
                Host = "smtp.rollback.test",
                Port = 2525,
                FromAddress = "events@rollback.test"
            }, fixture.Subject.PrincipalId);
            if (reject)
                await Assert.ThrowsAsync<DbUpdateException>(ApplyAsync);
            else
                await ApplyAsync();
            await Assert.That(interceptor.ObservedSavedBatch).IsTrue();
        }, interceptor);
        // A fresh selected owner prevents a cleared/tracked cache from pretending rollback occurred.
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var settings = await services.GetRequiredService<IInstanceSmtpSettingService>().ReadSettingsAsync();
            await Assert.That(settings.Host).IsEqualTo(reject ? "smtp.instance.test" : "smtp.rollback.test");
            await Assert.That(settings.Port).IsEqualTo(reject ? 587 : 2525);
            await Assert.That(settings.FromAddress).IsEqualTo(reject ? "events@instance.test" : "events@rollback.test");
        });
    }

    [Test]
    [Arguments(Mutation.Set)]
    [Arguments(Mutation.Reset)]
    [Arguments(Mutation.Lock)]
    [Arguments(Mutation.Unlock)]
    public async Task DelegationLockRejectsMutationAndPreservesDormantOverride(Mutation mutation)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await SetDelegationLockAsync(fixture, true);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var result = await services.GetRequiredService<EmailDeliverySettingsWriter>().ApplyAsync(
                [new(TenantId: fixture.TenantId, Key: GovernanceSettingKeys.Email.SmtpHost,
                    Kind: mutation switch
                    {
                        Mutation.Set => EmailDeliverySettingMutationKind.SetValue,
                        Mutation.Reset => EmailDeliverySettingMutationKind.Remove,
                        _ => EmailDeliverySettingMutationKind.SetLock
                    },
                    Value: mutation == Mutation.Set ? "\"smtp.replacement.test\"" : null,
                    IsLocked: mutation is Mutation.Lock or Mutation.Unlock ? mutation == Mutation.Lock : null)],
                fixture.Subject.PrincipalId);
            await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Locked);
            await Assert.That(result.Changes).IsEmpty();
            await Assert.ThrowsAsync<ValidationException>(() => ApplyAsync(fixture, services, mutation));
        });
        await SetDelegationLockAsync(fixture, false);
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var restored = await services.GetRequiredService<IHierarchicalSettingsReader>()
                .ResolveWithMetadataAsync(GovernanceSettingKeys.Email.SmtpHost, new SettingContext(fixture.TenantId));
            await Assert.That(SettingValueSerializer.DeserializeString(restored!.Value)).IsEqualTo("smtp.tenant.test");
            await Assert.That(restored.IsLocked).IsFalse();
        });
    }

    [Test]
    [Arguments(Mutation.Set, "smtp.replacement.test", false)]
    [Arguments(Mutation.Reset, "smtp.instance.test", false)]
    [Arguments(Mutation.Lock, "smtp.tenant.test", true)]
    [Arguments(Mutation.Unlock, "smtp.tenant.test", false)]
    public async Task UnlockedDelegationAppliesMutationToRealTenantSettings(
        Mutation mutation, string expectedHost, bool expectedLock)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            if (mutation == Mutation.Unlock)
                await ApplyAsync(fixture, services, Mutation.Lock);
            await ApplyAsync(fixture, services, mutation);

            var resolved = await services.GetRequiredService<IHierarchicalSettingsReader>()
                .ResolveWithMetadataAsync(GovernanceSettingKeys.Email.SmtpHost, new SettingContext(fixture.TenantId));
            await Assert.That(SettingValueSerializer.DeserializeString(resolved!.Value)).IsEqualTo(expectedHost);
            await Assert.That(resolved.IsLocked).IsEqualTo(expectedLock);
        });
    }

    [Test]
    public async Task UnlockedTenantPortOverrideIsResolvedWithItsDeclaredValue()
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        await fixture.ExecuteAsync(tenantOwner: true, async services =>
        {
            var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
            await settings.SetValueAsync(GovernanceSettingKeys.Email.SmtpPort, "465", SettingScope.Tenant,
                fixture.TenantId, fixture.Subject.PrincipalId);

            await Assert.That(await settings.ResolveAsync<int>(
                GovernanceSettingKeys.Email.SmtpPort, new SettingContext(fixture.TenantId))).IsEqualTo(465);
            await Assert.That((await services.GetRequiredService<ISmtpConfigResolver>().ResolveAsync())!.Port)
                .IsEqualTo(465);
        });
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task OriginalAuthenticatedAccountRequiresPersistedAdministratorGrant(bool tenantOwner, bool granted)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync(
            instanceAdministrator: !tenantOwner && granted, tenantAdministrator: tenantOwner && granted);
        await fixture.ExecuteAsync(tenantOwner, async services =>
        {
            Task ApplyAsync() => services.GetRequiredService<IHierarchicalSettingsResolver>().SetValueAsync(
                GovernanceSettingKeys.Email.SmtpPort, "465", tenantOwner ? SettingScope.Tenant : SettingScope.Instance,
                tenantOwner ? fixture.TenantId : Guid.Empty, fixture.Subject.PrincipalId);
            if (granted)
                await ApplyAsync();
            else
                await Assert.ThrowsAsync<UnauthorizedAccessException>(ApplyAsync);
        });
        await fixture.ExecuteAsync(tenantOwner, async services =>
        {
            var resolved = await services.GetRequiredService<IHierarchicalSettingsReader>().ResolveAsync<int>(
                GovernanceSettingKeys.Email.SmtpPort, new SettingContext(tenantOwner ? fixture.TenantId : null));
            await Assert.That(resolved).IsEqualTo(granted ? 465 : 587);
        });
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalValidatedLifetimeMustSurviveTheActualEmailCommit(bool expireAfterWrite)
    {
        await using var fixture = await SmtpAuthenticatedFixture.CreateAsync();
        var interceptor = new AfterSmtpBatchSaved(() =>
        {
            if (expireAfterWrite)
                fixture.Clock.Advance(fixture.Lifetime.ExpiresAt.Add(fixture.Lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
        });
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            Task ApplyAsync() => services.GetRequiredService<IInstanceSmtpSettingService>().ApplySettingsAsync(
                new InstanceSmtpSettingsDto
                {
                    Host = "smtp.lifetime.test",
                    Port = 2525,
                    FromAddress = "events@lifetime.test"
                }, fixture.Subject.PrincipalId);
            if (expireAfterWrite)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(ApplyAsync);
            else
                await ApplyAsync();
            await Assert.That(interceptor.ObservedSavedBatch).IsTrue();
        }, interceptor);
        await fixture.ExecuteAsync(tenantOwner: false, async services =>
        {
            var settings = await services.GetRequiredService<IInstanceSmtpSettingService>().ReadSettingsAsync();
            await Assert.That(settings.Host).IsEqualTo(expireAfterWrite ? "smtp.instance.test" : "smtp.lifetime.test");
            await Assert.That(settings.Port).IsEqualTo(expireAfterWrite ? 587 : 2525);
            await Assert.That(settings.FromAddress).IsEqualTo(
                expireAfterWrite ? "events@instance.test" : "events@lifetime.test");
        });
    }

    private static Task SetDelegationLockAsync(SmtpAuthenticatedFixture fixture, bool locked) =>
        fixture.ExecuteAsync(tenantOwner: false, services =>
            services.GetRequiredService<IHierarchicalSettingsResolver>().SetValueAsync(
                GovernanceSettingKeys.TenantDelegation.LockSmtp, locked ? "true" : "false",
                SettingScope.Instance, Guid.Empty, fixture.Subject.PrincipalId));

    private static Task ApplyAsync(SmtpAuthenticatedFixture fixture, IServiceProvider services, Mutation mutation)
    {
        var settings = services.GetRequiredService<IHierarchicalSettingsResolver>();
        return mutation switch
        {
            Mutation.Set => settings.SetValueAsync(GovernanceSettingKeys.Email.SmtpHost,
                "\"smtp.replacement.test\"", SettingScope.Tenant, fixture.TenantId, fixture.Subject.PrincipalId),
            Mutation.Reset => settings.RemoveOverrideAsync(GovernanceSettingKeys.Email.SmtpHost,
                SettingScope.Tenant, fixture.TenantId, fixture.Subject.PrincipalId),
            Mutation.Lock => settings.LockAsync(GovernanceSettingKeys.Email.SmtpHost,
                SettingScope.Tenant, fixture.TenantId, fixture.Subject.PrincipalId),
            Mutation.Unlock => settings.UnlockAsync(GovernanceSettingKeys.Email.SmtpHost,
                SettingScope.Tenant, fixture.TenantId, fixture.Subject.PrincipalId),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
    }

    /// <summary>Fails or advances the clock after real SMTP writes but before their owning commit.</summary>
    private sealed class AfterSmtpBatchSaved(Action afterWrite) : SaveChangesInterceptor
    {
        private bool savingSmtpBatch;
        internal bool ObservedSavedBatch { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            savingSmtpBatch = eventData.Context!.ChangeTracker.Entries<SystemSetting>().Any(entry =>
                entry.Entity.SettingKey == GovernanceSettingKeys.Email.SmtpPort && entry.Entity.Value == "2525"
                && entry.State is EntityState.Added or EntityState.Modified);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (savingSmtpBatch)
            {
                if (eventData.Context!.Database.CurrentTransaction is null)
                    throw new InvalidOperationException("The SMTP batch must remain inside its selected transaction.");
                savingSmtpBatch = false;
                ObservedSavedBatch = true;
                afterWrite();
            }
            return ValueTask.FromResult(result);
        }
    }
}
