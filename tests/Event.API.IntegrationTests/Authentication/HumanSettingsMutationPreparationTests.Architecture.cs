using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    public async Task GuardedTenantLockHandlersMustRejectPublicationPolicyKeys()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = CreateMigratedSettingsHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var lockHandler = selected.Services
            .GetRequiredService<ICommandHandler<LockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>();
        var unlockHandler = selected.Services
            .GetRequiredService<ICommandHandler<UnlockInstanceTenantSettingCommand, BaseCommandResponse<Guid>>>();

        foreach (string key in PublicationPolicySettingKeys.All)
        {
            var locked = await lockHandler.ExecuteAsync(
                new(PlatformDefaults.DefaultTenantId, key), CancellationToken.None);
            var unlocked = await unlockHandler.ExecuteAsync(
                new(PlatformDefaults.DefaultTenantId, key), CancellationToken.None);
            await Assert.That(locked.IsSuccess).IsFalse();
            await Assert.That(locked.FailureCode).IsEqualTo("setting_not_lockable");
            await Assert.That(unlocked.IsSuccess).IsFalse();
            await Assert.That(unlocked.FailureCode).IsEqualTo("setting_not_lockable");
        }
    }

    [Test]
    public async Task GenericMutationEntryPointsMustRejectPublicationPolicyKeys()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = CreateMigratedSettingsHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);

        foreach (string key in PublicationPolicySettingKeys.All)
        {
            // Each attempt owns a fresh authenticated physical invocation. The generic
            // entry point must reject even when the original scalar action was admitted.
            for (int attempt = 0; attempt < 11; attempt++)
            {
                bool instance = attempt is 5 or 6;
                await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
                    .CreateAsync(instance ? PolicyMutationScope.Instance : new(PlatformDefaults.DefaultTenantId),
                        CancellationToken.None);
                var services = selected.Services;
                await Assert.That(services.GetRequiredService<ICoordinatedSettingMutationStore>())
                    .IsTypeOf<CoordinatedSettingMutationRepository>();
                var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
                var scope = instance ? SettingScope.Instance : SettingScope.Tenant;
                var proof = attempt switch
                {
                    1 or 8 => await preparation.PrepareAsync(new ResetSettingCommand { Key = key, Scope = scope }),
                    2 or 9 => await preparation.PrepareAsync(new LockSettingCommand { Key = key, Scope = scope }),
                    3 or 6 or 10 => await preparation.PrepareAsync(new UnlockSettingCommand { Key = key, Scope = scope }),
                    _ => await preparation.PrepareAsync(new UpdateSettingCommand { Key = key, Scope = scope, Value = "true" }, "true")
                };
                var tenants = services.GetRequiredService<ITenantSettingRepository>();
                var systems = services.GetRequiredService<ISystemSettingRepository>();
                var resolver = services.GetRequiredService<IHierarchicalSettingsResolver>();
                Guid tenant = PlatformDefaults.DefaultTenantId;
                Guid actor = fixture.Binding.LocalSubjectId;
                await preparation.ExecutePreparedAsync(proof, async (_, token) =>
                {
                    Task Attack() => attempt switch
                    {
                        0 => tenants.SetValueAsync(tenant, key, "true", token, actor),
                        1 => tenants.RemoveOverrideAsync(tenant, key, token),
                        2 => tenants.LockAsync(tenant, key, actor, token),
                        3 => tenants.UnlockAsync(tenant, key, actor, token),
                        4 => tenants.UpsertManyForTenantAsync(tenant,
                            [new TenantSettingOverrideUpsert(key, "true", false)], actor, token),
                        5 => systems.UpsertAsync(new SystemSetting { SettingKey = key, Value = "true", CreatedBy = actor }, token),
                        6 => systems.UpsertLockAsync(new SystemSetting { SettingKey = key, Value = "true", CreatedBy = actor }, token),
                        7 => resolver.SetValueAsync(key, "true", scope, tenant, actor, token),
                        8 => resolver.RemoveOverrideAsync(key, scope, tenant, actor, token),
                        9 => resolver.LockAsync(key, scope, tenant, actor, token),
                        _ => resolver.UnlockAsync(key, scope, tenant, actor, token)
                    };
                    // Batch upsert replaces value AND lock. A scalar command is never
                    // upgraded to that compound authority merely to reach a later guard.
                    if (attempt == 4)
                        await Assert.ThrowsAsync<UnauthorizedAccessException>(Attack);
                    else
                        await Assert.ThrowsAsync<InvalidOperationException>(Attack);
                    await Assert.That(services.GetRequiredService<ExploreDbContext>().ChangeTracker.Entries()
                        .Any(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
                        .IsFalse();
                    return true;
                });
            }
        }
    }
}
