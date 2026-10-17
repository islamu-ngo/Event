using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(true, PublicationPolicyLockedSystemBehavior.RemoveOverride, false)]
    [Arguments(false, PublicationPolicyLockedSystemBehavior.RemoveOverride, false)]
    [Arguments(true, PublicationPolicyLockedSystemBehavior.Reject, false)]
    [Arguments(false, PublicationPolicyLockedSystemBehavior.Reject, false)]
    [Arguments(true, PublicationPolicyLockedSystemBehavior.RemoveOverride, true)]
    public async Task PublicationCleanupDerivesOnlyTheOriginalLockedTenantEffect(
        bool locked, PublicationPolicyLockedSystemBehavior behavior, bool expire)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        string key = EventReportingIntakeSettingDefinitions.IntakeEnabled.Key;
        string originalValue = locked ? "true" : "false";
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string policyKey in PublicationPolicySettingKeys.All)
            {
                var system = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == policyKey);
                string value = policyKey == EventSettingDefinitions.RequireApproval.Key ? "true" : "false";
                if (system is null)
                    seed.SystemSettings.Add(new SystemSetting
                    {
                        SettingKey = policyKey,
                        Value = value,
                        IsLocked = policyKey == key && locked
                    });
                else
                {
                    system.Value = value;
                    system.IsLocked = policyKey == key && locked;
                }
            }
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                SettingKey = key,
                Value = originalValue,
                CreatedBy = fixture.Binding.LocalSubjectId,
                CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            await seed.SaveChangesAsync();
        }
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var command = new UpdateSettingCommand { Key = key, Value = "true", Scope = SettingScope.Tenant };
        var proof = await preparation.PrepareAsync(command, "true");
        bool removed = locked && behavior == PublicationPolicyLockedSystemBehavior.RemoveOverride;
        async Task<PublicationPolicyMutationResult> Apply()
        {
            return await preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                var result = await services.GetRequiredService<IPublicationPolicyMutationBoundary>()
                    .ApplyTenantAsync(new(
                        PlatformDefaults.DefaultTenantId, fixture.Binding.LocalSubjectId,
                        fixture.Clock.GetUtcNow().UtcDateTime,
                        [new(key, PublicationPolicyMutationKind.Set, "true",
                            PlatformDefaults.DefaultTenantId, null)], behavior), token);
                // The cleanup child has ended. An original Set never becomes a general Remove grant.
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    preparation.RequireCurrentOwnedAsync(SettingScope.Tenant,
                        PlatformDefaults.DefaultTenantId, null, key, HumanSettingsMutationKind.Remove,
                        cancellationToken: token));
                if (expire)
                    services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                    {
                        var lifetime = services.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>().HumanLifetime!;
                        fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                            - fixture.Clock.GetUtcNow());
                        return Task.CompletedTask;
                    }, _ => { });
                return result;
            });
        }
        if (expire)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Apply);
        else
        {
            var result = await Apply();
            await Assert.That(result.Success).IsEqualTo(!locked || removed);
            if (locked && !removed)
            {
                await Assert.That(result.FailureCode).IsEqualTo(PublicationPolicyMutationFailureCodes.LockedPolicy);
                await Assert.That(result.DeferredNotifications.IsEmpty).IsTrue();
            }
            else
            {
                await Assert.That(result.DeferredNotifications.Length).IsEqualTo(1);
                var notification = result.DeferredNotifications[0];
                await Assert.That(notification.Key).IsEqualTo(key);
                await Assert.That(notification.OldValue).IsEqualTo(originalValue);
                await Assert.That(notification.NewValue).IsEqualTo(removed ? null : "true");
            }
        }
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides
            .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == key)
            .Select(row => row.Value).SingleOrDefaultAsync())
            .IsEqualTo(expire || locked && !removed ? originalValue : removed ? null : "true");
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key)
            .Select(row => row.IsLocked).SingleAsync()).IsEqualTo(locked);
    }
}
