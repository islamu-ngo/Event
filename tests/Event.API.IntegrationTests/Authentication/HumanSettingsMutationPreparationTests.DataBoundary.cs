using Explore.Persistence.Services;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceAdmin.Requests.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    public async Task ExplicitObservationRepositoriesCannotMutateEvenInsideSerializableOwner()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var context = fixture.Native.CreateDatabase();
        var unit = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unit);
        var systems = SystemSettingRepository.CreateReadOnly(context, mutationLock);
        var tenants = TenantSettingRepository.CreateReadOnly(context, mutationLock);
        var preferences = UserPreferenceRepository.CreateReadOnly(context);
        const string key = "observation-cannot-write";
        await unit.ExecuteSerializableAsync(async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                systems.UpsertAsync(new SystemSetting { SettingKey = key, Value = "12" }, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                tenants.SetValueAsync(PlatformDefaults.DefaultTenantId, key, "12", token));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                preferences.Create(new UserPreference
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    SettingKey = key,
                    Value = "12"
                }));
            await Assert.That(await systems.GetByKey(key, token)).IsNull();
            await Assert.That(await tenants.GetByTenantAndKey(PlatformDefaults.DefaultTenantId, key, token)).IsNull();
            await Assert.That(await preferences.GetByUserAndKey(
                PlatformDefaults.DefaultTenantId, fixture.Binding.LocalSubjectId, key)).IsNull();
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    [Arguments(SettingScope.User)]
    [Arguments(SettingScope.Tenant)]
    [Arguments(SettingScope.Instance)]
    public async Task PublicSettingWritesRequireActiveExactOriginalDataOwner(SettingScope scope)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(scope == SettingScope.Instance ? PolicyMutationScope.Instance
                : new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        const string key = "event_list.page_size";
        var proof = scope == SettingScope.Tenant
            ? await preparation.PrepareAsync(new SetInstanceTenantSettingCommand(PlatformDefaults.DefaultTenantId, key, "12"), "12")
            : await preparation.PrepareAsync(new UpdateSettingCommand { Key = key, Value = "12", Scope = scope }, "12");
        var context = services.GetRequiredService<ExploreDbContext>();
        var boundary = services.GetRequiredService<HumanSettingsDataBoundary>();
        var mutationLock = services.GetRequiredService<ISettingMutationLock>();
        Guid userId = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId;

        Task WriteAsync(ExploreDbContext owner, string targetKey, string value, CancellationToken token) => scope switch
        {
            SettingScope.Instance => new SystemSettingRepository(owner, mutationLock, boundary)
                .UpsertAsync(new SystemSetting { SettingKey = targetKey, Value = value }, token),
            SettingScope.Tenant => new TenantSettingRepository(owner, mutationLock, boundary)
                .SetValueAsync(PlatformDefaults.DefaultTenantId, targetKey, value, token, userId),
            _ => new UserPreferenceRepository(owner, boundary).Create(new UserPreference
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = userId,
                SettingKey = targetKey,
                Value = value,
                CreatedBy = userId
            })
        };

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WriteAsync(context, key, "12", CancellationToken.None));
        await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WriteAsync(context, key, "12", token));
            if (scope == SettingScope.Instance)
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    new SystemSettingRepository(context, mutationLock, boundary).UpsertInCurrentTransactionAsync(
                        new SystemSetting { SettingKey = key, Value = "12" }, token));
            return true;
        });
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WriteAsync(context, key, "13", token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WriteAsync(context, "unrelated.key", "12", token));
            await using var substituted = fixture.Native.CreateDatabase();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WriteAsync(substituted, key, "12", token));
            await WriteAsync(context, key, "12", token);
            if (scope == SettingScope.Instance)
                await new SystemSettingRepository(context, mutationLock, boundary).UpsertInCurrentTransactionAsync(
                    new SystemSetting { SettingKey = key, Value = "12" }, token);
            return true;
        });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => WriteAsync(context, key, "12", CancellationToken.None));
        await using var observer = fixture.Native.CreateDatabase();
        string? stored = scope switch
        {
            SettingScope.Instance => await observer.SystemSettings.Where(row => row.SettingKey == key)
                .Select(row => row.Value).SingleAsync(),
            SettingScope.Tenant => await observer.TenantSettingOverrides
                .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.SettingKey == key)
                .Select(row => row.Value).SingleAsync(),
            _ => await observer.UserPreferences
                .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == userId && row.SettingKey == key)
                .Select(row => row.Value).SingleAsync()
        };
        await Assert.That(stored).IsEqualTo("12");
    }

    [Test]
    public async Task ValueAdmissionCannotSmuggleSystemLocks()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var context = services.GetRequiredService<ExploreDbContext>();
        var repository = new SystemSettingRepository(context, services.GetRequiredService<ISettingMutationLock>(),
            services.GetRequiredService<HumanSettingsDataBoundary>());
        const string key = "settings-data-compound";
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.Instance }, "12");
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.UpsertAsync(
                new SystemSetting { SettingKey = key, Value = "12", IsLocked = true }, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.UpsertLockAsync(
                new SystemSetting { SettingKey = key, Value = "12", IsLocked = true }, token));
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key)).IsFalse();
    }

    [Test]
    public async Task TenantValueProofCannotAdmitLockRemoveOrCompoundBatch()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var context = services.GetRequiredService<ExploreDbContext>();
        var repository = new TenantSettingRepository(context, services.GetRequiredService<ISettingMutationLock>(),
            services.GetRequiredService<HumanSettingsDataBoundary>());
        Guid userId = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId;
        const string key = "settings-data-tenant";
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        var proof = await preparation.PrepareAsync(new SetInstanceTenantSettingCommand(tenantId, key, "12"), "12");
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.LockAsync(tenantId, key, userId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.UnlockAsync(tenantId, key, userId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.RemoveOverrideAsync(tenantId, key, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.UpsertManyForTenantAsync(tenantId, [new(key, "12", false)], userId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.CreateManyForTenantAsync(tenantId, [new(key, "12", false)], userId, DateTime.UtcNow, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.UpsertManyForTenantAsync(tenantId, [], userId, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.SetValueAsync(tenantId, key, "12", token, Guid.CreateVersion7()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.SetValueAsync(Guid.CreateVersion7(), key, "12", token, userId));
            await repository.SetValueAsync(tenantId, key, "12", token, userId);
            return true;
        });
        foreach (bool locked in new[] { true, false })
        {
            var lockProof = locked
                ? await preparation.PrepareAsync(new LockInstanceTenantSettingCommand(tenantId, key))
                : await preparation.PrepareAsync(new UnlockInstanceTenantSettingCommand(tenantId, key));
            await preparation.ExecutePreparedAsync(lockProof, async (_, token) =>
            {
                bool changed = locked
                    ? await repository.LockAsync(tenantId, key, userId, token)
                    : await repository.UnlockAsync(tenantId, key, userId, token);
                await Assert.That(changed).IsTrue();
                bool noChange = locked
                    ? await repository.LockAsync(tenantId, key, userId, token)
                    : await repository.UnlockAsync(tenantId, key, userId, token);
                await Assert.That(noChange).IsFalse();
                return true;
            });
        }
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.TenantSettingOverrides.SingleAsync(row => row.TenantId == tenantId && row.SettingKey == key);
        await Assert.That(stored.Value).IsEqualTo("12");
        await Assert.That(stored.IsLocked).IsFalse();
    }

    [Test]
    public async Task PreferenceGenericMethodsCannotRebindStoredIdentityOrReuseValueProofForRemoval()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        const string key = "settings-data-user";
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.User }, "12");
        var context = services.GetRequiredService<ExploreDbContext>();
        var repository = new UserPreferenceRepository(context, services.GetRequiredService<HumanSettingsDataBoundary>());
        Guid userId = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId;
        Guid existingId = Guid.CreateVersion7();
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.UserPreferences.Add(new UserPreference
            {
                Id = existingId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = userId,
                SettingKey = key + "-other",
                Value = "untouched"
            });
            await seed.SaveChangesAsync();
        }
        var setting = new UserPreference
        {
            Id = existingId,
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            UserId = userId,
            SettingKey = key,
            Value = "12"
        };
        await preparation.ExecutePreparedAsync(proof, async (_, _) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.Update(setting));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.Delete(setting));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.RemoveOverride(setting.TenantId, setting.UserId, setting.SettingKey));
            setting.Id = Guid.CreateVersion7();
            await repository.Create(setting);
            await repository.Update(setting);
            return true;
        });
        var remove = await preparation.PrepareAsync(new ResetSettingCommand { Key = key, Scope = SettingScope.User });
        await preparation.ExecutePreparedAsync(remove, async (_, _) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.RemoveOverride(setting.TenantId, Guid.CreateVersion7(), key));
            await repository.Delete(setting);
            await Assert.That(await repository.RemoveOverride(setting.TenantId, userId, key)).IsFalse();
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(await observer.UserPreferences.Where(row => row.Id == existingId)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("untouched");
    }

    [Test]
    public async Task OrdinaryTenantAdministratorCanRemoveOnlyTheAdmittedOverride()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        const string key = "settings-data-remove";
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var member = await seed.TenantUsers.SingleOrDefaultAsync(
                row => row.TenantId == tenantId && row.UserId == fixture.Binding.LocalSubjectId);
            if (member is null)
            {
                member = new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                };
                seed.TenantUsers.Add(member);
            }
            seed.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = null!,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            });
            seed.TenantSettingOverrides.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                SettingKey = key,
                Value = "12"
            });
            await seed.SaveChangesAsync();
        }
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(tenantId), CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(new ResetSettingCommand { Key = key, Scope = SettingScope.Tenant });
        var repository = new TenantSettingRepository(services.GetRequiredService<ExploreDbContext>(),
            services.GetRequiredService<ISettingMutationLock>(), services.GetRequiredService<HumanSettingsDataBoundary>());
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                repository.RemoveOverrideAsync(tenantId, key + "-other", token));
            await Assert.That(await repository.RemoveOverrideAsync(tenantId, key, token)).IsTrue();
            await Assert.That(await repository.RemoveOverrideAsync(tenantId, key, token)).IsFalse();
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.TenantSettingOverrides.AnyAsync(
            row => row.TenantId == tenantId && row.SettingKey == key)).IsFalse();
    }

    [Test]
    public async Task LockAdmissionDoesNotAdmitFallbackValueCreation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        await using var selection = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selection.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var context = services.GetRequiredService<ExploreDbContext>();
        var repository = new SystemSettingRepository(context, services.GetRequiredService<ISettingMutationLock>(),
            services.GetRequiredService<HumanSettingsDataBoundary>());
        const string key = "settings-data-lock";
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = "original" });
            await seed.SaveChangesAsync();
        }
        var proof = await preparation.PrepareAsync(new LockSettingCommand { Key = key, Scope = SettingScope.Instance });
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await repository.UpsertLockAsync(new SystemSetting { SettingKey = key, Value = "not-admitted", IsLocked = true }, token);
            return true;
        });
        var absentProof = await preparation.PrepareAsync(
            new LockSettingCommand { Key = key + "-absent", Scope = SettingScope.Instance });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.ExecutePreparedAsync(absentProof, async (_, token) =>
            {
                await repository.UpsertLockAsync(
                    new SystemSetting { SettingKey = key + "-absent", Value = "not-admitted", IsLocked = true }, token);
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        var stored = await observer.SystemSettings.SingleAsync(row => row.SettingKey == key);
        await Assert.That(stored.Value).IsEqualTo("original");
        await Assert.That(stored.IsLocked).IsTrue();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key + "-absent")).IsFalse();
    }

    [Test]
    public async Task RevokedOriginalSourceRollsBackActualPreferenceMutation()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await using var host = CreateDataBoundaryHost(fixture);
        await using var request = await CreateDataBoundaryRequestAsync(fixture, host);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        const string key = "settings-data-revocation";
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.User }, "12");
        var context = services.GetRequiredService<ExploreDbContext>();
        var repository = new UserPreferenceRepository(context, services.GetRequiredService<HumanSettingsDataBoundary>());
        Guid userId = services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.ExecutePreparedAsync(proof, async (_, token) =>
            {
                await repository.Create(new UserPreference
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = userId,
                    SettingKey = key,
                    Value = "12"
                });
                await context.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId).ExecuteDeleteAsync(token);
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.SettingKey == key)).IsFalse();
        await Assert.That(await observer.UserExternalLogins.AnyAsync(row => row.Id == fixture.Binding.ExternalLoginId)).IsTrue();
    }

    private static WebApplicationFactory<Program> CreateDataBoundaryHost(LocalIdentityLifecycleHttpFixture fixture) =>
        fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddHumanSettingsMutationPreparation();
            services.AddScoped<HumanSettingsDataBoundary>();
        }));

    private static async Task<Request> CreateDataBoundaryRequestAsync(
        LocalIdentityLifecycleHttpFixture fixture, WebApplicationFactory<Program> host)
    {
        await using (var seed = fixture.Native.CreateDatabase())
        {
            if (!await seed.TenantUsers.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
                    && row.UserId == fixture.Binding.LocalSubjectId))
                seed.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    User = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
                });
            await seed.SaveChangesAsync();
        }
        string token = await fixture.SignInAsync();
        var scope = host.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = context;
        context.Request.Headers.Authorization = "Bearer " + token;
        var authenticated = await context.AuthenticateAsync();
        if (!authenticated.Succeeded)
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
            throw new InvalidOperationException("Real Local authentication did not succeed.");
        }
        context.User = authenticated.Principal!;
        return new Request(scope, accessor, context);
    }
}
