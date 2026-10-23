using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Constants;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.InstanceOnboarding.Handlers.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using Explore.Application.Features.TenantStorageSettings.Handlers.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Models.Common;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Services;
using Explore.Infrastructure.Storage;
using Explore.Application.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task StorageHandlerRetainsFirstValidationAbsenceAndIdentity(bool tenant, bool replacement)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedConfigurationAsync(fixture, tenant);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.PublicEndpoint).ExecuteDeleteAsync();
            if (replacement)
            {
                seed.SystemSettings.Add(new() { SettingKey = GovernanceSettingKeys.Storage.PublicEndpoint, Value = "\"\"" });
                await seed.SaveChangesAsync();
            }
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), tenant);
        var services = request.Services;
        bool enteredAfterValidation = false;
        async Task ChangeOriginal(CancellationToken token)
        {
            enteredAfterValidation = true;
            await using var independent = fixture.Native.CreateDatabase();
            await independent.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.PublicEndpoint).ExecuteDeleteAsync(token);
            independent.SystemSettings.Add(new() { SettingKey = GovernanceSettingKeys.Storage.PublicEndpoint, Value = "\"\"" });
            await independent.SaveChangesAsync(token);
        }
        if (tenant)
        {
            var wrapper = new ConfigurationTenantService(services.GetRequiredService<ITenantStorageSettingService>(), ChangeOriginal);
            var handler = new PatchTenantStorageSettingsCommandHandler(services.GetRequiredService<ITenantContext>(),
                services.GetRequiredService<IAdminContext>(), wrapper);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(
                ConfigurationTenantCommand(fixture), default));
        }
        else
        {
            var wrapper = new ConfigurationInstanceService(services.GetRequiredService<IInstanceStorageSettingService>(), ChangeOriginal);
            var handler = new UpdateInstanceStorageSettingsCommandHandler(services.GetRequiredService<IAdminContext>(), wrapper);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(
                ConfigurationInstanceCommand(fixture), default));
        }
        await Assert.That(enteredAfterValidation).IsTrue();
        await AssertOriginalConfigurationAsync(fixture, tenant);
    }

    [Test]
    [Arguments(false, "invalid-endpoint")]
    [Arguments(false, "invalid-expiration")]
    [Arguments(true, "invalid-endpoint")]
    [Arguments(true, "invalid-expiration")]
    [Arguments(false, "provider")]
    [Arguments(true, "provider")]
    [Arguments(false, "ceiling")]
    [Arguments(true, "ceiling")]
    [Arguments(false, "empty")]
    [Arguments(true, "empty")]
    public async Task DirectConfigurationMutationCannotBypassMergedValidation(bool tenant, string defect)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedConfigurationAsync(fixture, tenant);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), tenant);
        if (tenant)
        {
            var service = request.Services.GetRequiredService<ITenantStorageSettingService>();
            var command = ConfigurationTenantCommand(fixture) with
            {
                Settings = new()
                {
                    S3 = new()
                    {
                        Endpoint = OptionalUpdate<string>.Set(defect == "invalid-endpoint" ? "file:///invalid" : ""),
                        UploadUrlExpirationMinutes = OptionalUpdate<int>.Set(defect == "invalid-expiration" ? 1441 : 60)
                    }
                }
            };
            if (defect is "provider" or "ceiling")
                command = command with
                {
                    Settings = new()
                    {
                        Policy = new()
                        {
                            Provider = OptionalUpdate<string>.Set(defect == "provider" ? "foreign" : "local"),
                            MaxUploadBytes = OptionalUpdate<long>.Set(defect == "ceiling" ? long.MaxValue : 10)
                        }
                    }
                };
            if (defect == "empty") command = command with { Settings = new() };
            await using var observation = await service.ObserveConfigurationAsync(command);
            await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => service.ApplyPatchAsync(observation));
        }
        else
        {
            var service = request.Services.GetRequiredService<IInstanceStorageSettingService>();
            var command = ConfigurationInstanceCommand(fixture);
            command = command with
            {
                Patch = command.Patch with
                {
                    S3Configuration =
                OptionalUpdate<Explore.Application.DTOs.Instance.InstanceS3ConfigurationWriteDto>.Set(new()
                {
                    Endpoint = defect == "invalid-endpoint" ? "file:///invalid" : "",
                    BucketName = "after",
                    UploadUrlExpirationMinutes = defect == "invalid-expiration" ? 1441 : 60
                })
                }
            };
            if (defect is "provider" or "ceiling")
                command = command with
                {
                    Patch = new()
                    {
                        Policy =
                    OptionalUpdate<Explore.Application.DTOs.Instance.InstanceStoragePolicyWriteDto>.Set(new()
                    {
                        Provider = defect == "provider" ? "foreign" : "local",
                        DefaultMaxUploadBytes = defect == "ceiling" ? 101 : 10,
                        DefaultTenantQuotaBytes = 100,
                        InstanceMaxUploadBytes = 100
                    })
                    }
                };
            if (defect == "empty") command = command with { Patch = new() };
            await using var observation = await service.ObserveConfigurationAsync(command);
            await Assert.ThrowsAsync<FluentValidation.ValidationException>(() => service.ApplySettingsAsync(observation));
        }
        await AssertOriginalConfigurationAsync(fixture, tenant);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SkippingThePrivateConfigurationWriterCannotSettle(bool tenant, bool equalScalarWrites)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedConfigurationAsync(fixture, tenant);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), tenant);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenant ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance, default);
        var services = selected.Services;
        var instanceCommand = ConfigurationInstanceCommand(fixture);
        var tenantCommand = ConfigurationTenantCommand(fixture);
        await using var observation = tenant
            ? await services.GetRequiredService<ITenantStorageSettingService>().ObserveConfigurationAsync(tenantCommand)
            : await services.GetRequiredService<IInstanceStorageSettingService>().ObserveConfigurationAsync(instanceCommand);
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = tenant ? await preparation.PrepareAsync(tenantCommand, observation)
            : await preparation.PrepareAsync(instanceCommand, observation);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.ExecutePreparedAsync(proof, (owner, token) =>
                preparation.ExecuteStorageJoinedAsync(owner, observation, async (_, joined) =>
                {
                    if (equalScalarWrites)
                        foreach (var effect in observation.Effects)
                            if (effect.TenantId is { } target)
                                await services.GetRequiredService<ITenantSettingRepository>()
                                    .SetValueAsync(target, effect.Key, effect.Value, joined, observation.Actor);
                            else
                                await services.GetRequiredService<ISystemSettingRepository>()
                                    .UpsertInCurrentTransactionAsync(new()
                                    {
                                        Id = effect.Id,
                                        SettingKey = effect.Key,
                                        Value = effect.Value,
                                        ValueType = effect.ValueType!.Value,
                                        IsLocked = false,
                                        AllowedValues = effect.AllowedValues,
                                        Category = effect.Category,
                                        Description = effect.Description,
                                        DisplayOrder = effect.DisplayOrder!.Value,
                                        CreatedAt = effect.CreatedAt,
                                        CreatedBy = effect.CreatedBy,
                                        UpdatedAt = effect.UpdatedAt,
                                        UpdatedBy = effect.UpdatedBy
                                    }, joined);
                    return true;
                }, token)));
        await AssertOriginalConfigurationAsync(fixture, tenant);
        await AssertConfigurationEffectsNotCommittedAsync(fixture, observation);
    }

    [Test]
    [Arguments(false, "unchanged")]
    [Arguments(true, "unchanged")]
    [Arguments(false, "original-credential")]
    [Arguments(true, "original-credential")]
    [Arguments(false, "administrator")]
    [Arguments(true, "administrator")]
    [Arguments(false, "request")]
    [Arguments(true, "request")]
    [Arguments(false, "connection")]
    [Arguments(true, "connection")]
    [Arguments(false, "terminal-tamper")]
    [Arguments(true, "terminal-tamper")]
    [Arguments(false, "terminal-expiry")]
    [Arguments(true, "terminal-expiry")]
    public async Task ConfigurationRetainsSignedCustodyAndRollsBackWholePatch(bool tenant, string change)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        Guid grant = await SeedPlatformAdministratorAsync(fixture);
        await SeedConfigurationAsync(fixture, tenant);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), tenant);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenant ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance, default);
        var services = selected.Services;
        var writer = (ConfigurationWriterObservation)services.GetRequiredService<IStorageConfigurationWriter>();
        var instanceService = services.GetRequiredService<IInstanceStorageSettingService>();
        var tenantService = services.GetRequiredService<ITenantStorageSettingService>();
        await using var observation = tenant
            ? await tenantService.ObserveConfigurationAsync(ConfigurationTenantCommand(fixture))
            : await instanceService.ObserveConfigurationAsync(ConfigurationInstanceCommand(fixture));
        if (change == "original-credential")
            request.Context.Request.Headers.Authorization = "Bearer " + await fixture.SignInAsync();
        if (change == "administrator")
        {
            await using var independent = fixture.Native.CreateDatabase();
            await independent.PlatformUserRoles.Where(row => row.Id == grant).ExecuteDeleteAsync();
        }
        if (change == "request") request.Context.Request.Path = "/api/instance/settings/other";
        if (change == "connection")
        {
            await services.GetRequiredService<ExploreDbContext>().Database.GetDbConnection().CloseAsync();
            await services.GetRequiredService<ExploreDbContext>().Database.OpenConnectionAsync();
        }
        writer.AfterWrite = async token =>
        {
            if (change == "terminal-tamper")
                await services.GetRequiredService<ExploreDbContext>().SystemSettings.Where(row =>
                    row.SettingKey == GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.Description, "changed after fixed writer"), token);
            if (change == "terminal-expiry")
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    fixture.Clock.Advance(observation.Source.Lifetime.ExpiresAt.Add(observation.Source.Lifetime.ClockSkew)
                        .AddTicks(1) - fixture.Clock.GetUtcNow());
                    return Task.CompletedTask;
                }, _ => { });
        };
        Task Apply() => tenant ? tenantService.ApplyPatchAsync(observation) : instanceService.ApplySettingsAsync(observation);
        if (change == "unchanged")
        {
            await Apply();
            await using var observer = fixture.Native.CreateDatabase();
            if (tenant)
            {
                var row = await observer.Set<TenantSetting>().SingleAsync(item => item.SettingKey == GovernanceSettingKeys.Storage.BucketName);
                await Assert.That(row.Value).IsEqualTo("\"after\"");
                await Assert.That(row.IsLocked).IsTrue();
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            }
            else
            {
                var row = await observer.SystemSettings.SingleAsync(item => item.SettingKey == GovernanceSettingKeys.Storage.BucketName);
                await Assert.That(row.Value).IsEqualTo("\"after\"");
                await Assert.That(row.IsLocked).IsFalse();
                await Assert.That(row.Id).IsEqualTo(observation.Before.System[row.SettingKey]!.Id);
                await Assert.That(row.CreatedAt).IsEqualTo(observation.Before.System[row.SettingKey]!.CreatedAt);
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            }
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Apply);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(Apply);
            await AssertOriginalConfigurationAsync(fixture, tenant);
            await AssertConfigurationEffectsNotCommittedAsync(fixture, observation);
        }
    }

    [Test]
    public async Task OriginalTenantAdministratorNeedsNoPlatformAdministratorGrant()
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedConfigurationAsync(fixture, true);
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
            seed.TenantUserRoleGrants.Add(new()
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
            });
            await seed.SaveChangesAsync();
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), true);
        var handler = ActivatorUtilities.CreateInstance<PatchTenantStorageSettingsCommandHandler>(request.Services);
        var result = await handler.ExecuteAsync(ConfigurationTenantCommand(fixture), default);
        await Assert.That(result.IsSuccess).IsTrue();
        await using var observer = fixture.Native.CreateDatabase();
        var row = await observer.Set<TenantSetting>().SingleAsync(item => item.SettingKey == GovernanceSettingKeys.Storage.BucketName);
        await Assert.That(row.Value).IsEqualTo("\"after\"");
        await Assert.That(row.IsLocked).IsTrue();
        await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
    }

    [Test]
    public async Task AcceptedSetupCredentialCannotIssueConfigurationObservation()
    {
        await using var native = await LocalAdmissionWebApplicationFactory.CreateAsync(incompleteSetup: true);
        await using var host = native.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, null, false, native.SetupSecret);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => request.Services
            .GetRequiredService<IInstanceStorageSettingService>().ObserveConfigurationAsync(new()
            {
                UserId = Guid.CreateVersion7(),
                Patch = new()
                {
                    S3Configuration = OptionalUpdate<Explore.Application.DTOs.Instance.InstanceS3ConfigurationWriteDto>.Set(new()
                    { UploadUrlExpirationMinutes = 60 })
                }
            }));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalSupportDeadlineKeepsFullPrecisionAfterAllSourceAwaits(bool expire)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedConfigurationAsync(fixture, true);
        var deadline = fixture.Clock.GetUtcNow().AddMinutes(5).AddTicks(7);
        Guid sessionId;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string key in new[] { GovernanceSettingKeys.SupportAccess.Enabled, GovernanceSettingKeys.SupportAccess.AllowWriteMode })
            {
                var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = "true" });
                else row.Value = "true";
            }
            var session = SupportAccessSession.Start(fixture.Binding.LocalSubjectId, PlatformDefaults.DefaultTenantId,
                SupportAccessModeEnum.Write, "maintenance", "Configuration custody", "configuration-test",
                fixture.Clock.GetUtcNow().AddMinutes(-1), deadline);
            seed.SupportAccessSessions.Add(session);
            await seed.SaveChangesAsync();
            sessionId = session.Id;
        }
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(RegisterConfigurationCustody));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), true);
        request.Context.Request.Headers[SupportAccessHeaderNames.SessionId] = sessionId.ToString("D");
        await Assert.That((await request.Services.GetRequiredService<ISupportAccessSessionService>().GetCurrentAsync()).IsActive).IsTrue();
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(new(PlatformDefaults.DefaultTenantId), default);
        var services = selected.Services;
        var writer = (ConfigurationWriterObservation)services.GetRequiredService<IStorageConfigurationWriter>();
        writer.AfterWrite = _ =>
        {
            services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
            {
                fixture.Clock.Advance((expire ? deadline : deadline.AddTicks(-1)) - fixture.Clock.GetUtcNow());
                return Task.CompletedTask;
            }, _ => { });
            return Task.CompletedTask;
        };
        var service = services.GetRequiredService<ITenantStorageSettingService>();
        await using var observation = await service.ObserveConfigurationAsync(ConfigurationTenantCommand(fixture));
        if (expire)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyPatchAsync(observation));
            await AssertOriginalConfigurationAsync(fixture, true);
        }
        else await service.ApplyPatchAsync(observation);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ConfigurationActivatesActualCachesOnlyAfterAcknowledgedCommit(bool tenant, bool reject)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await SeedConfigurationAsync(fixture, tenant);
        int activations = 0;
        string cacheKey = tenant ? $"HierSettings:Tenant:{PlatformDefaults.DefaultTenantId}" : "HierSettings:System";
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            RegisterConfigurationCustody(services);
            services.AddScoped<S3ConfigResolver>();
            services.AddScoped<IS3ConfigResolver>(provider => new ConfigurationCacheObservation(
                provider.GetRequiredService<S3ConfigResolver>(), target =>
                {
                    if (provider.GetRequiredService<ExploreDbContext>().Database.CurrentTransaction is not null
                        || target != (tenant ? PlatformDefaults.DefaultTenantId : (Guid?)null)
                        || provider.GetRequiredService<IMemoryCache>().TryGetValue(cacheKey, out _))
                        throw new InvalidOperationException("Cache activation did not follow original settlement and hierarchical invalidation.");
                    using var independent = fixture.Native.CreateDatabase();
                    string value = tenant ? independent.Set<TenantSetting>().Single(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName).Value
                        : independent.SystemSettings.Single(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName).Value;
                    if (value != "\"after\"") throw new InvalidOperationException("Activation did not observe durable configuration.");
                    activations++;
                }));
        }));
        await using var request = await ConfigurationRequestAsync(host.Services, await fixture.SignInAsync(), tenant);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(tenant ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance, default);
        var services = selected.Services;
        services.GetRequiredService<IMemoryCache>().Set(cacheKey, new List<SystemSetting>());
        if (reject)
            ((ConfigurationWriterObservation)services.GetRequiredService<IStorageConfigurationWriter>()).AfterWrite = _ =>
            {
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(
                    _ => throw new UnauthorizedAccessException("Reject original configuration settlement."), _ => { });
                return Task.CompletedTask;
            };
        var instance = services.GetRequiredService<IInstanceStorageSettingService>();
        var tenants = services.GetRequiredService<ITenantStorageSettingService>();
        await using var observation = tenant
            ? await tenants.ObserveConfigurationAsync(ConfigurationTenantCommand(fixture))
            : await instance.ObserveConfigurationAsync(ConfigurationInstanceCommand(fixture));
        Task Apply() => tenant ? tenants.ApplyPatchAsync(observation) : instance.ApplySettingsAsync(observation);
        if (reject) await Assert.ThrowsAsync<UnauthorizedAccessException>(Apply);
        else await Apply();
        await Assert.That(activations).IsEqualTo(reject ? 0 : 1);
        if (reject) await AssertOriginalConfigurationAsync(fixture, tenant);
    }

    private static void RegisterConfigurationCustody(IServiceCollection services)
    {
        services.AddStorageConfigurationAuthority();
        services.AddScoped<StorageConfigurationWriter>();
        services.AddScoped<IStorageConfigurationWriter, ConfigurationWriterObservation>();
    }

    private static async Task<Request> ConfigurationRequestAsync(IServiceProvider services, string? bearer, bool tenant, string? setupSecret = null)
    {
        var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Patch;
        context.Request.Path = tenant ? "/api/tenant/settings/storage" : "/api/instance/settings/storage";
        if (bearer is not null) context.Request.Headers.Authorization = "Bearer " + bearer;
        if (setupSecret is not null) context.Request.Headers["X-Setup-Secret"] = setupSecret;
        accessor.HttpContext = context;
        var authentication = await context.AuthenticateAsync();
        if (!authentication.Succeeded)
        {
            accessor.HttpContext = null;
            await scope.DisposeAsync();
            throw new InvalidOperationException("The actual storage configuration request did not authenticate.");
        }
        context.User = authentication.Principal!;
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        return new(scope, accessor, context);
    }

    private static UpdateInstanceStorageSettingsCommand ConfigurationInstanceCommand(LocalIdentityLifecycleHttpFixture fixture) => new()
    {
        UserId = fixture.Binding.LocalSubjectId,
        Patch = new()
        {
            S3Configuration = OptionalUpdate<Explore.Application.DTOs.Instance.InstanceS3ConfigurationWriteDto>.Set(new()
            { Endpoint = "", PublicEndpoint = "", BucketName = "after", Region = "fsn1", ForcePathStyle = true, UploadUrlExpirationMinutes = 60 })
        }
    };

    private static PatchTenantStorageSettingsCommand ConfigurationTenantCommand(LocalIdentityLifecycleHttpFixture fixture) => new()
    {
        UserId = fixture.Binding.LocalSubjectId,
        Settings = new()
        {
            S3 = new()
            { BucketName = OptionalUpdate<string>.Set("after"), Region = OptionalUpdate<string>.Set("fsn1") }
        }
    };

    private static async Task SeedConfigurationAsync(LocalIdentityLifecycleHttpFixture fixture, bool tenant)
    {
        await using var seed = fixture.Native.CreateDatabase();
        foreach (var (key, value) in new[]
        {
            (GovernanceSettingKeys.Deployment.Mode, "\"SingleTenant\""),
            (GovernanceSettingKeys.Storage.Provider, "\"local\""),
            (GovernanceSettingKeys.Storage.Endpoint, "\"\""),
            (GovernanceSettingKeys.Storage.BucketName, "\"before\""),
            (GovernanceSettingKeys.Storage.DefaultTenantQuotaBytes, "1024")
        })
        {
            var row = await seed.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
            bool locked = !tenant && key == GovernanceSettingKeys.Storage.BucketName;
            if (row is null) seed.SystemSettings.Add(new() { SettingKey = key, Value = value, IsLocked = locked });
            else { row.Value = value; row.IsLocked = locked; }
        }
        if (tenant)
            seed.Set<TenantSetting>().Add(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                SettingKey = GovernanceSettingKeys.Storage.BucketName,
                Value = "\"before\"",
                IsLocked = true
            });
        await seed.SaveChangesAsync();
    }

    private static async Task AssertOriginalConfigurationAsync(LocalIdentityLifecycleHttpFixture fixture, bool tenant)
    {
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == GovernanceSettingKeys.Storage.BucketName)
            .Select(row => row.Value).SingleAsync()).IsEqualTo("\"before\"");
        if (tenant)
        {
            var row = await observer.Set<TenantSetting>().SingleAsync(item => item.SettingKey == GovernanceSettingKeys.Storage.BucketName);
            await Assert.That(row.Value).IsEqualTo("\"before\"");
            await Assert.That(row.IsLocked).IsTrue();
            await Assert.That(row.UpdatedBy).IsNull();
        }
    }

    private static async Task AssertConfigurationEffectsNotCommittedAsync(
        LocalIdentityLifecycleHttpFixture fixture, StorageSettingsMutation observation)
    {
        await using var independent = fixture.Native.CreateDatabase();
        foreach (var effect in observation.Effects)
        {
            StorageConfigurationRow? actual;
            StorageConfigurationRow? original;
            if (effect.TenantId is { } tenant)
            {
                var row = await independent.Set<TenantSetting>().AsNoTracking().SingleOrDefaultAsync(item =>
                    item.TenantId == tenant && item.SettingKey == effect.Key);
                actual = row is null ? null : StorageConfigurationRow.From(row);
                original = observation.Before.Tenant[effect.Key];
            }
            else
            {
                var row = await independent.SystemSettings.AsNoTracking().SingleOrDefaultAsync(item => item.SettingKey == effect.Key);
                actual = row is null ? null : StorageConfigurationRow.From(row);
                original = observation.Before.System[effect.Key];
            }
            await Assert.That(actual).IsEqualTo(original);
        }
    }

    private sealed class ConfigurationWriterObservation(StorageConfigurationWriter actual) : IStorageConfigurationWriter
    {
        internal Func<CancellationToken, Task>? AfterWrite { get; set; }
        public void RequireIdle() => actual.RequireIdle();
        public DateTime RepresentTimestamp(DateTime timestamp) => actual.RepresentTimestamp(timestamp);
        public Task<StorageConfigurationState> ObserveAsync(IStorageConfigurationSource source, IUnitOfWork owner, CancellationToken token) =>
            actual.ObserveAsync(source, owner, token);
        public Task RequireUnchangedAsync(StorageConfigurationState original, CancellationToken token) => actual.RequireUnchangedAsync(original, token);
        public Task RequireAppliedAsync(StorageSettingsMutation mutation, CancellationToken token) => actual.RequireAppliedAsync(mutation, token);
        public async Task ExecuteAsync(StorageSettingsMutation mutation, CancellationToken token)
        {
            await actual.ExecuteAsync(mutation, token);
            if (AfterWrite is { } callback) { AfterWrite = null; await callback(token); }
        }
    }

    private sealed class ConfigurationCacheObservation(IS3ConfigResolver actual, Action<Guid?> observe) : IS3ConfigResolver
    {
        public Task<S3Configuration?> ResolveAsync(CancellationToken token = default) => actual.ResolveAsync(token);
        public Task<bool> IsConfiguredAsync(CancellationToken token = default) => actual.IsConfiguredAsync(token);
        public void InvalidateCache(Guid? tenantId = null) { observe(tenantId); actual.InvalidateCache(tenantId); }
    }

    private sealed class ConfigurationInstanceService(IInstanceStorageSettingService actual, Func<CancellationToken, Task> before)
        : IInstanceStorageSettingService
    {
        public Task<StorageSettingsMutation> ObserveConfigurationAsync(UpdateInstanceStorageSettingsCommand command, CancellationToken token = default) =>
            actual.ObserveConfigurationAsync(command, token);
        public async Task ApplySettingsAsync(StorageSettingsMutation observation, CancellationToken token = default)
        { await before(token); await actual.ApplySettingsAsync(observation, token); }
        public Task<InstanceStorageSettingsDto> ReadSettingsAsync(CancellationToken token = default) => actual.ReadSettingsAsync(token);
        public Task<InstanceStorageProviderStatusDto> TestProviderAsync(TestInstanceStorageProviderQuery query, CancellationToken token = default) =>
            actual.TestProviderAsync(query, token);
        public Task<InstanceStorageProviderStatusDto> TestProviderAsync(TestInstanceStorageProviderDuringSetupQuery query, CancellationToken token = default) =>
            actual.TestProviderAsync(query, token);
        public Task<InstanceStorageUsageDto> RecalculateUsageAsync(RecalculateInstanceStorageUsageCommand command, CancellationToken token = default) =>
            actual.RecalculateUsageAsync(command, token);
        public Task<InstanceStorageUsageDto> RecalculateUsageAsync(RecalculateInstanceStorageUsageDuringSetupCommand command, CancellationToken token = default) =>
            actual.RecalculateUsageAsync(command, token);
    }

    private sealed class ConfigurationTenantService(ITenantStorageSettingService actual, Func<CancellationToken, Task> before)
        : ITenantStorageSettingService
    {
        public Task<StorageSettingsMutation> ObserveConfigurationAsync(PatchTenantStorageSettingsCommand command, CancellationToken token = default) =>
            actual.ObserveConfigurationAsync(command, token);
        public async Task ApplyPatchAsync(StorageSettingsMutation observation, CancellationToken token = default)
        { await before(token); await actual.ApplyPatchAsync(observation, token); }
        public Task<TenantStorageSettingsDto> ReadSettingsAsync(Guid tenantId, CancellationToken token = default) =>
            actual.ReadSettingsAsync(tenantId, token);
        public Task<InstanceStorageProviderStatusDto> TestProviderAsync(TestTenantStorageProviderCommand command, CancellationToken token = default) =>
            actual.TestProviderAsync(command, token);
    }
}
