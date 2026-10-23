using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Models;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Identity;
using Explore.Persistence.Services;
using Explore.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    private static async Task ApplyStorageConfigurationAsync(
        IInstanceStorageSettingService service, UpdateInstanceStorageSettingsCommand command)
    {
        await using var observation = await service.ObserveConfigurationAsync(command);
        await service.ApplySettingsAsync(observation);
    }

    private static async Task ApplyStorageConfigurationAsync(
        ITenantStorageSettingService service, PatchTenantStorageSettingsCommand command)
    {
        await using var observation = await service.ObserveConfigurationAsync(command);
        await service.ApplyPatchAsync(observation);
    }

    private static void ObserveStorageWriterOwner(IServiceCollection services, Action<ExploreDbContext> observe,
        Action<IServiceProvider>? observeServices = null)
    {
        services.AddScoped<IStorageConfigurationWriter>(selected =>
        {
            observe(selected.GetRequiredService<ExploreDbContext>());
            observeServices?.Invoke(selected);
            return ActivatorUtilities.CreateInstance<StorageConfigurationWriter>(selected);
        });
    }

    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(false, "binding-revoked")]
    [Arguments(false, "administrator-revoked")]
    [Arguments(false, "terminal-expiry")]
    [Arguments(false, "principal-replaced")]
    [Arguments(true, "unchanged")]
    [Arguments(true, "binding-revoked")]
    [Arguments(true, "administrator-revoked")]
    [Arguments(true, "terminal-expiry")]
    [Arguments(true, "principal-replaced")]
    public async Task StorageConsumersRetainOriginalHumanThroughSettlement(bool tenant, string change)
    {
        const string key = GovernanceSettingKeys.Storage.BucketName;
        var before = new ConsumerBeforeOwnerTransaction();
        var terminal = new SpecializedBeforeSettlement(key);
        StorageActivationProbe? cache = null;
        IServiceProvider? originalServices = null;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            interceptor: change == "terminal-expiry" ? terminal : before,
            configureServices: services =>
            {
                ObserveStorageWriterOwner(services, owner => before.Owner = terminal.Owner = owner,
                    selected => originalServices = selected);
                services.AddScoped<IS3ConfigResolver>(selected => cache = new StorageActivationProbe(
                    ActivatorUtilities.CreateInstance<S3ConfigResolver>(selected),
                    selected.GetRequiredService<ExploreDbContext>()));
            });
        Guid grant = await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            seed.SystemSettings.Add(new SystemSetting
            {
                SettingKey = key,
                Value = "\"previous-bucket\"",
                IsLocked = true
            });
            if (tenant)
            {
                seed.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = GovernanceSettingKeys.Deployment.Mode,
                    Value = "\"SingleTenant\""
                });
                seed.Set<TenantSetting>().Add(new TenantSetting
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    SettingKey = key,
                    Value = "\"tenant-before\"",
                    IsLocked = true
                });
            }
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenant ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance,
                CancellationToken.None);
        var services = selected.Services;
        var context = services.GetRequiredService<ExploreDbContext>();
        before.Owner = context;
        before.BeforeStart = async token =>
        {
            await using var independent = fixture.Native.CreateDatabase();
            if (change == "binding-revoked")
                await independent.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteDeleteAsync(token);
            if (change == "administrator-revoked")
                await independent.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync(token);
            if (change == "principal-replaced")
                request.Context.User = new System.Security.Claims.ClaimsPrincipal(request.Context.User);
        };
        terminal.Owner = context;
        terminal.BeforeSettlement = () =>
        {
            var ownerServices = originalServices ?? throw new InvalidOperationException("The original storage writer was not selected.");
            var lifetime = ownerServices.GetRequiredService<Explore.Persistence.Database.ExploreDbContextExecutionLease>()
                .HumanLifetime!;
            ownerServices.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                fixture.Clock.Advance(lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1)
                    - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
        };
        var execution = tenant
            ? ActivatorUtilities.CreateInstance<PatchTenantStorageSettingsCommandHandler>(services)
                .ExecuteAsync(new PatchTenantStorageSettingsCommand
                {
                    UserId = fixture.Binding.LocalSubjectId,
                    Settings = new()
                    {
                        S3 = new()
                        {
                            BucketName = OptionalUpdate<string>.Set("storage-custody"),
                            Region = OptionalUpdate<string>.Set(" fsn1 ")
                        }
                    }
                }, CancellationToken.None)
            : ActivatorUtilities.CreateInstance<UpdateInstanceStorageSettingsCommandHandler>(services)
                .ExecuteAsync(StorageInstanceCommand(fixture.Binding.LocalSubjectId), CancellationToken.None);
        bool committed = change == "unchanged";
        if (committed)
            await Assert.That((await execution).IsSuccess).IsTrue();
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(change == "terminal-expiry" ? terminal.Observed : before.Observed).IsTrue();
        await Assert.That(cache?.Activated ?? false).IsEqualTo(committed);
        await using var observer = fixture.Native.CreateDatabase();
        if (tenant)
        {
            var row = await observer.Set<TenantSetting>().AsNoTracking().SingleAsync(item =>
                item.TenantId == PlatformDefaults.DefaultTenantId && item.SettingKey == key);
            await Assert.That(row.Value).IsEqualTo(committed ? "\"storage-custody\"" : "\"tenant-before\"");
            await Assert.That(row.IsLocked).IsTrue();
            if (committed)
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(await observer.Set<TenantSetting>().Where(item =>
                    item.TenantId == PlatformDefaults.DefaultTenantId
                    && item.SettingKey == GovernanceSettingKeys.Storage.Region)
                .Select(item => item.Value).SingleOrDefaultAsync()).IsEqualTo(committed ? "\"fsn1\"" : null);
        }
        else
        {
            var row = await observer.SystemSettings.AsNoTracking().SingleAsync(item => item.SettingKey == key);
            await Assert.That(row.Value).IsEqualTo(committed ? "\"storage-custody\"" : "\"previous-bucket\"");
            await Assert.That(row.IsLocked).IsEqualTo(!committed);
            if (committed)
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            await Assert.That(await observer.SystemSettings.Where(item =>
                    item.SettingKey == GovernanceSettingKeys.Storage.Region)
                .Select(item => item.Value).SingleOrDefaultAsync()).IsEqualTo(committed ? "\"fsn1\"" : null);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StorageServicesRejectSubstitutedOriginalActor(bool tenant)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        Task execution = tenant
            ? ApplyStorageConfigurationAsync(request.Services.GetRequiredService<ITenantStorageSettingService>(), new()
            {
                UserId = Guid.CreateVersion7(),
                Settings = new() { S3 = new() { BucketName = OptionalUpdate<string>.Set("actor-substitution") } }
            })
            : ApplyStorageConfigurationAsync(request.Services.GetRequiredService<IInstanceStorageSettingService>(),
                StorageInstanceCommand(Guid.CreateVersion7()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.Value == "\"storage-custody\"")).IsFalse();
        await Assert.That(await observer.Set<TenantSetting>().AnyAsync(row => row.Value == "\"actor-substitution\"")).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SetupAuthenticationCannotBeRelabeledAsStorageAdministration(bool tenant)
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var request = await ProviderRequestAsync(native.Services, null, native.SetupSecret);
        Task execution = tenant
            ? ApplyStorageConfigurationAsync(request.Services.GetRequiredService<ITenantStorageSettingService>(), new()
            {
                UserId = Guid.CreateVersion7(),
                Settings = new() { S3 = new() { BucketName = OptionalUpdate<string>.Set("setup-substitution") } }
            })
            : ApplyStorageConfigurationAsync(request.Services.GetRequiredService<IInstanceStorageSettingService>(),
                StorageInstanceCommand(Guid.CreateVersion7()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await using var observer = native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.Value == "\"storage-custody\"")).IsFalse();
        await Assert.That(await observer.Set<TenantSetting>().AnyAsync(row => row.Value == "\"setup-substitution\"")).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StorageMutationRejectsUnsupportedIndependentLocalAuthority(bool tenant)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(topology: IdentityDatabaseTopology.External);
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        Task execution = tenant
            ? ApplyStorageConfigurationAsync(request.Services.GetRequiredService<ITenantStorageSettingService>(), new()
            {
                UserId = fixture.Binding.LocalSubjectId,
                Settings = new() { S3 = new() { BucketName = OptionalUpdate<string>.Set("unsupported-local") } }
            })
            : ApplyStorageConfigurationAsync(request.Services.GetRequiredService<IInstanceStorageSettingService>(),
                StorageInstanceCommand(fixture.Binding.LocalSubjectId));
        await Assert.ThrowsAsync<NotSupportedException>(() => execution);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.Value == "\"storage-custody\"")).IsFalse();
        await Assert.That(await observer.Set<TenantSetting>().AnyAsync(row => row.Value == "\"unsupported-local\"")).IsFalse();
    }

    [Test]
    [Arguments("unchanged")]
    [Arguments("tenant-grant-revoked")]
    [Arguments("delegation-locked")]
    [Arguments("ceiling-lowered")]
    public async Task TenantStorageRetainsTenantAdministratorAndAcquiredDelegation(string change)
    {
        var before = new ConsumerBeforeOwnerTransaction();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: before,
            configureServices: services => ObserveStorageWriterOwner(services, owner => before.Owner = owner));
        Guid grantId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            var member = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            seed.TenantUsers.Add(member);
            var grant = new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = member.TenantId,
                Tenant = null!,
                TenantUserId = member.Id,
                TenantUser = null!,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = fixture.Clock.GetUtcNow().UtcDateTime
            };
            grantId = grant.Id;
            seed.TenantUserRoleGrants.Add(grant);
            foreach (var (key, value) in new Dictionary<string, string>
            {
                [GovernanceSettingKeys.Deployment.Mode] = "\"MultiTenant\"",
                [GovernanceSettingKeys.TenantDelegation.LockStorage] = "false",
                [GovernanceSettingKeys.Storage.InstanceMaxUploadBytes] = "100"
            })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = value });
                else row.Value = value;
            }
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        before.Owner = selected.Services.GetRequiredService<ExploreDbContext>();
        before.BeforeStart = async token =>
        {
            await using var independent = fixture.Native.CreateDatabase();
            if (change == "tenant-grant-revoked")
                await independent.TenantUserRoleGrants.Where(row => row.Id == grantId).ExecuteDeleteAsync(token);
            if (change == "delegation-locked")
                await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.TenantDelegation.LockStorage)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "true"), token);
            if (change == "ceiling-lowered")
                await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.InstanceMaxUploadBytes)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "10"), token);
        };
        var execution = ApplyStorageConfigurationAsync(selected.Services.GetRequiredService<ITenantStorageSettingService>(), new()
        {
            UserId = fixture.Binding.LocalSubjectId,
            Settings = new()
            {
                Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(50) },
                S3 = new() { BucketName = OptionalUpdate<string>.Set("tenant-authority") }
            }
        });
        if (change == "unchanged") await execution;
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        await Assert.That(before.Observed).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var rows = await observer.Set<TenantSetting>().AsNoTracking()
            .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                && (row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes
                    || row.SettingKey == GovernanceSettingKeys.Storage.BucketName)).ToListAsync();
        await Assert.That(rows.Count).IsEqualTo(change == "unchanged" ? 2 : 0);
        if (change == "unchanged")
            await Assert.That(rows.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes).Value)
                .IsEqualTo("50");
    }

    [Test]
    public async Task StorageGroupCompilerKeepsOmittedGroupAndRejectsLowerRepositoryExpansion()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        await ApplyStorageConfigurationAsync(services.GetRequiredService<IInstanceStorageSettingService>(), new()
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new()
            {
                Policy = OptionalUpdate<InstanceStoragePolicyWriteDto>.Set(new()
                {
                    Provider = StorageProviders.Local,
                    DefaultMaxUploadBytes = 10,
                    DefaultTenantQuotaBytes = 100,
                    InstanceMaxUploadBytes = 100,
                    LockTenantStorage = false
                })
            }
        });
        await using (var observer = fixture.Native.CreateDatabase())
        {
            await Assert.That(await observer.SystemSettings.Where(row =>
                    row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes)
                .Select(row => row.Value).SingleAsync()).IsEqualTo("10");
            await Assert.That(await observer.SystemSettings.AnyAsync(row =>
                row.SettingKey == GovernanceSettingKeys.Storage.BucketName)).IsFalse();
        }
        await services.GetRequiredService<ISerializableAttemptOwner>().ExecuteAsync(async token =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                services.GetRequiredService<ISystemSettingRepository>().UpsertInCurrentTransactionAsync(new()
                {
                    SettingKey = GovernanceSettingKeys.Storage.BucketName,
                    Value = "\"escaped\"",
                    UpdatedBy = fixture.Binding.LocalSubjectId
                }, token));
            return true;
        });
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.RequireCurrentOwnedAsync(SettingScope.Instance, null, null,
                GovernanceSettingKeys.Storage.DefaultMaxUploadBytes, HumanSettingsMutationKind.SetValue, "10"));
        await Assert.That(typeof(StorageSettingsMutation).GetConstructors().Length).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task StorageRetainsOriginalSupportHorizonAtPhysicalSettlement(bool expire)
    {
        var terminal = new SpecializedBeforeSettlement(GovernanceSettingKeys.Storage.BucketName);
        IServiceProvider? originalServices = null;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(interceptor: terminal,
            configureServices: services => ObserveStorageWriterOwner(services, owner => terminal.Owner = owner,
                selected => originalServices = selected));
        await SeedPlatformAdministratorAsync(fixture);
        var expiry = fixture.Clock.GetUtcNow().AddMinutes(5);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (var (key, value) in new Dictionary<string, string>
            {
                [GovernanceSettingKeys.Deployment.Mode] = "\"SingleTenant\"",
                [GovernanceSettingKeys.SupportAccess.Enabled] = "true",
                [GovernanceSettingKeys.SupportAccess.AllowWriteMode] = "true"
            })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = value });
                else row.Value = value;
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Storage administration", "storage-fixture",
                fixture.Clock.GetUtcNow().AddMinutes(-1), expiry);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        await using var request = await Request.CreateAsync(fixture);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), CancellationToken.None);
        var services = selected.Services;
        terminal.Owner = services.GetRequiredService<ExploreDbContext>();
        terminal.BeforeSettlement = () =>
            (originalServices ?? throw new InvalidOperationException("The original storage writer was not selected."))
                .GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                if (expire) fixture.Clock.Advance(expiry - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
        var execution = ApplyStorageConfigurationAsync(services.GetRequiredService<ITenantStorageSettingService>(), new()
        {
            UserId = fixture.Binding.LocalSubjectId,
            Settings = new() { S3 = new() { BucketName = OptionalUpdate<string>.Set("support-storage") } }
        });
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => execution);
        else await execution;
        await Assert.That(terminal.Observed).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Set<TenantSetting>().AnyAsync(row => row.Value == "\"support-storage\""))
            .IsEqualTo(!expire);
    }

    private static UpdateInstanceStorageSettingsCommand StorageInstanceCommand(Guid user) => new()
    {
        UserId = user,
        Patch = new()
        {
            S3Configuration = OptionalUpdate<InstanceS3ConfigurationWriteDto>.Set(new()
            {
                BucketName = "storage-custody",
                Region = " fsn1 ",
                ForcePathStyle = true,
                UploadUrlExpirationMinutes = 60
            })
        }
    };

    private sealed class StorageActivationProbe(IS3ConfigResolver inner, ExploreDbContext context) : IS3ConfigResolver
    {
        internal bool Activated { get; private set; }
        public Task<S3Configuration?> ResolveAsync(CancellationToken cancellationToken = default) =>
            inner.ResolveAsync(cancellationToken);
        public Task<bool> IsConfiguredAsync(CancellationToken cancellationToken = default) =>
            inner.IsConfiguredAsync(cancellationToken);
        public void InvalidateCache(Guid? tenantId = null)
        {
            if (context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("Storage activation cannot observe provisional settings.");
            Activated = true;
            inner.InvalidateCache(tenantId);
        }
    }
}
