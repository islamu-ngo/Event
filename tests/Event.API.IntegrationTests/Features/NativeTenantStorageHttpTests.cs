using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Exceptions;
using Explore.Application.Features.TenantStorageSettings.Requests.Commands;
using Explore.Application.Features.TenantStorageSettings.Requests.Queries;
using Explore.Application.Features.Users.Requests.Queries;
using Explore.Application.Hateoas;
using Explore.Application.Models;
using Explore.Application.Models.Common;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Infrastructure.Storage;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed partial class NativeTenantStorageHttpTests
{
    private const string Root = "/api/tenant/settings/storage";

    [Test]
    public async Task Settings_FencePersistedAdministrativeAuthorityAndDiscloseOnlyAmbientPolicyAndUsage()
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var forged = Client(factory, data.MemberId);
        using var admin = Client(factory, data.AdminId);
        using var instance = Client(factory, data.InstanceAdminId);
        var before = await SettingsAsync(admin);
        var persistedBefore = await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId);
        string cachedBucketBefore = (await S3Async(factory)).BucketName;
        foreach (var client in new[] { anonymous, forged })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            using var read = await client.GetAsync(Root);
            using var patch = await client.PatchAsJsonAsync(Root, new PatchTenantStorageSettingsDto
            {
                Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(4096) }
            });
            using var probe = await client.PostAsync(Root + "/test", null);
            await ProblemAsync(read, expected);
            await ProblemAsync(patch, expected, client == anonymous ? "application/problem+json" : "application/json");
            await ProblemAsync(probe, expected);
        }
        await Assert.That(factory.Boundary.Operations).IsEmpty();
        await Assert.That(await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId)).IsEquivalentTo(persistedBefore);
        await Assert.That((await SettingsAsync(admin)).MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo(cachedBucketBefore);
        foreach (var client in new[] { admin, instance })
        {
            using var response = await client.GetAsync(Root);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            string json = await response.Content.ReadAsStringAsync();
            await Assert.That(json).DoesNotContain(factory.AccessKey);
            await Assert.That(json).DoesNotContain(factory.SecretKey);
            var settings = (await response.Content.ReadFromJsonAsync<TenantStorageSettingsDto>())!;
            await Assert.That(settings.TenantId).IsEqualTo(PlatformDefaults.DefaultTenantId);
            await Assert.That(settings.Usage.UsedBytes).IsEqualTo(123);
            await Assert.That(settings.Usage.ReservedBytes).IsEqualTo(17);
            await Assert.That(settings.Usage.AvailableBytes).IsEqualTo(settings.TenantQuotaBytes - 140);
            await Assert.That(settings.S3BucketName).IsEqualTo("tenant-storage");
        }
        await Assert.That(factory.Boundary.Operations).IsEmpty();
    }

    [Test]
    public async Task Patch_PreservesPartialStateValidationAndLocksAndEvictsBothCachesAfterCommit()
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        using var instance = Client(factory, data.InstanceAdminId);
        var before = await SettingsAsync(admin);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo("tenant-storage");
        using (var probe = await admin.PostAsync(Root + "/test", null))
            await Assert.That((await probe.Content.ReadFromJsonAsync<InstanceStorageProviderStatusDto>())!.IsAvailable).IsTrue();
        using (var empty = await admin.PatchAsJsonAsync(Root, new PatchTenantStorageSettingsDto()))
            await ProblemAsync(empty, HttpStatusCode.BadRequest, "application/json");
        using (var invalid = await admin.PatchAsJsonAsync(Root, new PatchTenantStorageSettingsDto
        {
            Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(before.EffectivePolicy.InstanceMaxUploadBytes + 1) }
        }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest, "application/json");
        using (var patched = await instance.PatchAsJsonAsync(Root, new PatchTenantStorageSettingsDto
        {
            S3 = new() { BucketName = OptionalUpdate<string>.Set("  changed-storage  ") }
        }))
        {
            await Assert.That(patched.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await patched.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.Id)
                .IsEqualTo(PlatformDefaults.DefaultTenantId);
        }
        var after = await SettingsAsync(admin);
        await Assert.That(after.S3BucketName).IsEqualTo("changed-storage");
        await Assert.That(after.MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That(after.S3Endpoint).IsEqualTo(before.S3Endpoint);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo("changed-storage");
        using (var probe = await admin.PostAsync(Root + "/test", null))
            await Assert.That((await probe.Content.ReadFromJsonAsync<InstanceStorageProviderStatusDto>())!.IsAvailable).IsTrue();
        await Assert.That(factory.Boundary.Operations.Select(operation => operation.Bucket))
            .IsEquivalentTo(new[] { "tenant-storage", "tenant-storage", "tenant-storage", "changed-storage", "changed-storage", "changed-storage" });
        await Assert.That(factory.Boundary.Objects).IsEmpty();
        using var scope = factory.Services.CreateScope();
        await SetPrincipalAsync(scope, factory, data.AdminId, PlatformDefaults.DefaultTenantId, HttpMethods.Patch);
        var resolver = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
        await using (var bootstrap = await scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>().CreateDbContextAsync())
        {
            foreach (var (key, value) in new[]
            {
                (GovernanceSettingKeys.Deployment.Mode, JsonSerializer.Serialize("MultiTenant")),
                (GovernanceSettingKeys.TenantDelegation.LockStorage, "true")
            })
            {
                var setting = await bootstrap.SystemSettings.SingleOrDefaultAsync(item => item.SettingKey == key);
                if (setting is null)
                    bootstrap.SystemSettings.Add(new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = key, Value = value });
                else
                    setting.Value = value;
            }
            await bootstrap.SaveChangesAsync();
        }
        resolver.InvalidateCache(SettingScope.Instance);
        // Native scope preserves policy authority without depending on multi-tenant HTTP host routing.
        var patch = scope.ServiceProvider.GetRequiredService<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>();
        var locked = await patch.ExecuteAsync(new() { UserId = data.AdminId, Settings = new() { Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(4096) } } }, default);
        await Assert.That(locked.FailureCode).IsEqualTo("StorageTenantOverridesLocked");
    }

    [Test]
    public async Task FailedCommit_RollsBackEveryLeafAndRetainsWarmSettingsAndS3Configuration()
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        var before = await SettingsAsync(admin);
        await Assert.That(before.MaxUploadBytes).IsNotEqualTo(4096L);
        await Assert.That(before.S3BucketName).IsNotEqualTo("rolled-back");
        var persistedBefore = await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId);
        await Assert.That(persistedBefore.GetValueOrDefault(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes)).IsNotEqualTo("4096");
        await Assert.That(persistedBefore.GetValueOrDefault(GovernanceSettingKeys.Storage.BucketName)).IsNotEqualTo("\"rolled-back\"");
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo("tenant-storage");
        using (var probe = await admin.PostAsync(Root + "/test", null))
            await Assert.That(probe.StatusCode).IsEqualTo(HttpStatusCode.OK);
        factory.CommitFault.Enabled = true;
        using (var failed = await admin.PatchAsJsonAsync(Root, new PatchTenantStorageSettingsDto
        {
            Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(4096) },
            S3 = new() { BucketName = OptionalUpdate<string>.Set("rolled-back") }
        }))
            await ProblemAsync(failed, HttpStatusCode.InternalServerError);
        factory.CommitFault.Enabled = false;
        await Assert.That(factory.CommitFault.ObservedBothLeavesBeforeRollback).IsTrue();
        var after = await SettingsAsync(admin);
        await Assert.That(after.MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That(after.S3BucketName).IsEqualTo(before.S3BucketName);
        await Assert.That((await S3Async(factory)).BucketName).IsEqualTo(before.S3BucketName);
        await Assert.That(await TenantSettingsAsync(factory, PlatformDefaults.DefaultTenantId)).IsEquivalentTo(persistedBefore);
        using var finalProbe = await admin.PostAsync(Root + "/test", null);
        await Assert.That((await finalProbe.Content.ReadFromJsonAsync<InstanceStorageProviderStatusDto>())!.IsAvailable).IsTrue();
        await Assert.That(factory.Boundary.Operations.All(operation => operation.Bucket == "tenant-storage")).IsTrue();
        await Assert.That(factory.Boundary.Objects).IsEmpty();
    }

    [Test]
    [Arguments("versioning", "probe_connection_failed", 0)]
    [Arguments("put", "probe_write_unconfirmed", 0)]
    [Arguments("delete", "probe_cleanup_failed", 1)]
    public async Task Probe_MapsProviderFailuresWithoutDisclosingCredentialsOrRawErrors(string failure, string code, int remainingObjects)
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        factory.Boundary.Failure = failure;
        using var response = await admin.PostAsync(Root + "/test", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<InstanceStorageProviderStatusDto>())!;
        await Assert.That(result.IsAvailable).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(code);
        string json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain(factory.AccessKey);
        await Assert.That(json).DoesNotContain(factory.SecretKey);
        await Assert.That(factory.Boundary.Objects.Count).IsEqualTo(remainingObjects);
    }

    [Test]
    public async Task NativeProbe_RejectsForeignTenantAndPropagatesCancellationBeforeAnyObjectIsWritten()
    {
        await using var factory = await StorageFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        await SetPrincipalAsync(scope, factory, data.AdminId, data.OtherTenantId);
        var probe = scope.ServiceProvider.GetRequiredService<ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>>();
        await Assert.That(async () => { await probe.ExecuteAsync(new() { UserId = data.AdminId }, default); }).Throws<AuthorizationException>();
        await Assert.That(async () => { await query.QueryAsync(new(), default); }).Throws<AuthorizationException>();
        var patch = await scope.ServiceProvider.GetRequiredService<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { UserId = data.AdminId, Settings = new() { Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(4096) } } }, default);
        await Assert.That(patch.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(factory.Boundary.Operations).IsEmpty();
        using var instanceScope = factory.Services.CreateScope();
        await SetPrincipalAsync(instanceScope, factory, data.InstanceAdminId, data.OtherTenantId);
        var foreign = await instanceScope.ServiceProvider
            .GetRequiredService<IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>>()
            .QueryAsync(new(), default);
        await Assert.That(foreign.S3BucketName).IsEqualTo("foreign-storage");
        await Assert.That((await instanceScope.ServiceProvider
            .GetRequiredService<ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>>()
            .ExecuteAsync(new() { UserId = data.InstanceAdminId }, default)).IsAvailable).IsTrue();
        await Assert.That(factory.Boundary.Operations.All(operation => operation.Bucket == "foreign-storage")).IsTrue();
        using var cancellationScope = factory.Services.CreateScope();
        await SetPrincipalAsync(cancellationScope, factory, data.AdminId, PlatformDefaults.DefaultTenantId);
        var cancellationProbe = cancellationScope.ServiceProvider
            .GetRequiredService<ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>>();
        factory.Boundary.BlockVersioning = true;
        using var cancellation = new CancellationTokenSource();
        var pending = cancellationProbe.ExecuteAsync(new() { UserId = data.AdminId }, cancellation.Token);
        await factory.Boundary.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10))).Throws<OperationCanceledException>();
        await Assert.That(factory.Boundary.Objects).IsEmpty();
        await Assert.That(factory.Boundary.Operations.Last().Verb).IsEqualTo("versioning");
    }

    [Test]
    public async Task Controller_RequiresOnlyClosedDecoratedOperationPortsAndTheHalAssembler()
    {
        Type[] parameters = typeof(TenantStorageSettingsController).GetConstructors().Single().GetParameters()
            .Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(parameters).IsEquivalentTo(new[]
        {
            typeof(IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>),
            typeof(ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>),
            typeof(IQueryHandler<ResolveCurrentUserIdByIdentityRequest, Guid?>),
            typeof(IResourceAssembler<TenantStorageSettingsDto, TenantStorageSettingsDto>)
        });
    }

    private static Task SetPrincipalAsync(IServiceScope scope, StorageFactory factory, Guid userId,
        Guid tenantId, string method = "POST")
    {
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = method;
        context.Request.Path = method == HttpMethods.Patch ? Root : Root + "/test";
        context.Request.Headers.Authorization = "Bearer " + factory.CreateToken(userId);
        // Publish before returning a task so the caller retains this ambient request.
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = context;
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        return AuthenticateAsync();

        async Task AuthenticateAsync()
        {
            var authentication = await context.AuthenticateAsync();
            if (!authentication.Succeeded)
                throw new InvalidOperationException("The native storage request did not authenticate.");
            context.User = authentication.Principal!;
        }
    }

    private static async Task<SeedData> SeedAsync(StorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await using var bootstrap = await scope.ServiceProvider.GetRequiredService<IGlobalExploreDbContextFactory>()
            .CreateDbContextAsync();
        if (await bootstrap.Tenants.FindAsync(PlatformDefaults.DefaultTenantId) is null)
        {
            bootstrap.Tenants.Add(new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId)
                .WithFullName("Default Test Tenant").WithSlug("default-test").Build());
            await bootstrap.SaveChangesAsync();
        }
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await SeedTenantUserAsync(factory, bootstrap, db);
        var member = await SeedTenantUserAsync(factory, bootstrap, db);
        var instance = await SeedTenantUserAsync(factory, bootstrap, db);
        var otherTenantId = Guid.CreateVersion7();
        bootstrap.Tenants.Add(new TenantBuilder().WithId(otherTenantId).WithFullName("Secondary Tenant").Build());
        var role = await bootstrap.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        bootstrap.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = instance.UserId, User = null!, RoleId = role.Id, Role = role });
        await bootstrap.SaveChangesAsync();
        var membership = await db.TenantUsers.Include(item => item.Tenant).SingleAsync(item => item.UserId == admin.UserId);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(),
            TenantId = admin.TenantId,
            Tenant = membership.Tenant,
            TenantUserId = membership.Id,
            TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!,
            RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        db.StorageUsageCounters.Add(new StorageUsageCounter
        {
            Id = Guid.CreateVersion7(),
            TenantId = admin.TenantId,
            Provider = StorageProviders.S3Compatible,
            UsedBytes = 123,
            ReservedBytes = 17,
            ObjectCount = 2,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        foreach (var tenant in new[] { admin.TenantId, otherTenantId })
        {
            using var settingsScope = factory.Services.CreateScope();
            settingsScope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenant);
            var settings = settingsScope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            foreach (var (key, value) in new[]
            {
                (GovernanceSettingKeys.Storage.Provider, StorageProviders.S3Compatible),
                (GovernanceSettingKeys.Storage.Endpoint, "https://storage.example.test"),
                (GovernanceSettingKeys.Storage.BucketName, tenant == admin.TenantId ? "tenant-storage" : "foreign-storage")
            })
                settings.TenantSettingOverrides.Add(new TenantSetting
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    SettingKey = key,
                    Value = JsonSerializer.Serialize(value),
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = admin.UserId
                });
            await settings.SaveChangesAsync();
        }
        return new(admin.UserId, member.UserId, instance.UserId, otherTenantId);
    }

    private static async Task<TenantScenarioSeed.TenantScenarioResult> SeedTenantUserAsync(
        StorageFactory factory, ExploreDbContext bootstrap, ExploreDbContext tenant)
    {
        var user = new UserBuilder().WithId(Guid.CreateVersion7()).Build();
        var actor = new ActorBuilder().WithId(Guid.CreateVersion7()).WithUserId(user.Id).Build();
        bootstrap.Users.Add(user);
        bootstrap.Actors.Add(actor);
        bootstrap.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(factory.Issuer, user.Id.ToString("D")).Value,
            CreatedAt = DateTime.UtcNow
        });
        await bootstrap.SaveChangesAsync();
        tenant.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = PlatformDefaults.DefaultTenantId,
            Tenant = null!,
            UserId = user.Id,
            User = null!,
            ActorId = actor.Id,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await tenant.SaveChangesAsync();
        return new(PlatformDefaults.DefaultTenantId, user.Id, actor.Id);
    }

    private static HttpClient Client(StorageFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", factory.CreateToken(userId));
        return client;
    }

    private static async Task<S3Configuration> S3Async(StorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        return (await scope.ServiceProvider.GetRequiredService<IS3ConfigResolver>().ResolveAsync())!;
    }

    private static async Task<Dictionary<string, string>> TenantSettingsAsync(StorageFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        return await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().TenantSettingOverrides.AsNoTracking()
            .Where(row => row.TenantId == tenantId).ToDictionaryAsync(row => row.SettingKey, row => row.Value);
    }

    private static async Task<TenantStorageSettingsDto> SettingsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<TenantStorageSettingsDto>(Root))!;

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected,
        string mediaType = "application/problem+json")
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo(mediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)expected);
    }

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid InstanceAdminId, Guid OtherTenantId);

    private sealed class StorageFactory : IAsyncDisposable
    {
        private LocalAdmissionWebApplicationFactory? _native;
        private WebApplicationFactory<Program>? _host;
        public IServiceProvider Services => _host!.Services;
        public string Issuer => _native!.ExternalIssuer;
        public string AccessKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string SecretKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public StorageBoundary Boundary { get; } = new();
        public CommitFailure CommitFault { get; } = new();
        public TransactionAuthorityControls AuthorityControls { get; } = new();

        public static async Task<StorageFactory> CreateAsync()
        {
            var factory = new StorageFactory();
            factory.CommitFault.AuthorityControls = factory.AuthorityControls;
            try
            {
                factory._native = await LocalAdmissionWebApplicationFactory.CreateAsync(
                    AuthenticationProviderKind.Keycloak, persistenceInterceptor: factory.CommitFault);
                factory._host = factory._native.WithWebHostBuilder(builder =>
                    builder.ConfigureTestServices(factory.ConfigureServices));
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        public HttpClient CreateClient() => _host!.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        public string CreateToken(Guid userId) => _native!.CreateExternalProviderToken(
        [
            new Claim("sub", userId.ToString("D")),
            new Claim("typ", "Bearer"),
            new Claim("azp", "islamu-event-blazor"),
            new Claim("auth_provider", "keycloak"),
            new Claim("explore:admin:tenant", PlatformDefaults.DefaultTenantId.ToString("D"))
        ], Issuer);

        private void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IHostedService>();
            services.AddTestContextOwners();
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                string? key = call.Arg<string>();
                ArgumentNullException.ThrowIfNull(key);
                string? value = key switch
                {
                    SecretDefinitionRegistry.Keys.Storage.AccessKeyId => AccessKey,
                    SecretDefinitionRegistry.Keys.Storage.SecretAccessKey => SecretKey,
                    _ => null
                };
                return value is null ? SecretResolutionResult.Unconfigured : SecretResolutionResult.Resolved(
                    new ResolvedSecret(key, value, SecretSourceType.EnvironmentVariable, SecretScope.Tenant,
                        call.Arg<Guid?>(), DateTimeOffset.UtcNow));
            });
            services.RemoveAll<ISecretResolver>();
            services.AddSingleton(secrets);
            services.RemoveAll<ISecretSource>();
            services.AddSingleton<ISecretSource>(new StorageSecrets(AccessKey, SecretKey, AuthorityControls));
            services.RemoveAll<IRetainedSecretResolver>();
            services.AddScoped<IRetainedSecretResolver, Explore.Infrastructure.Secrets.SecretResolver>();
            var clients = Substitute.For<IStorageProbeClientFactory>();
            clients.Create(Arg.Any<S3Configuration>()).Returns(call =>
            {
                var config = call.Arg<S3Configuration>();
                ArgumentNullException.ThrowIfNull(config);
                if (config.AccessKeyId != AccessKey || config.SecretAccessKey != SecretKey)
                    throw new InvalidOperationException("Unexpected storage credentials.");
                return Boundary.CreateClient(config, SecretKey);
            });
            services.RemoveAll<IStorageProbeClientFactory>();
            services.AddSingleton(clients);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_host is not null) await _host.DisposeAsync();
            }
            finally
            {
                if (_native is not null) await _native.DisposeAsync();
            }
        }
    }

    private sealed class StorageSecrets(string accessKey, string secretKey, TransactionAuthorityControls controls) : ISecretSource
    {
        private readonly string _revision = Guid.CreateVersion7().ToString("N");
        public SecretSourceType SourceType => SecretSourceType.EnvironmentVariable;

        public async Task<SecretResolutionResult> GetSecretAsync(SecretBinding binding, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await controls.BeforeSecretReadAsync(binding, cancellationToken);
            string? value = binding.SettingKey switch
            {
                SecretDefinitionRegistry.Keys.Storage.AccessKeyId => accessKey,
                SecretDefinitionRegistry.Keys.Storage.SecretAccessKey => secretKey,
                _ => null
            };
            return value is null ? SecretResolutionResult.Unconfigured : SecretResolutionResult.Resolved(
                new ResolvedSecret(binding.SettingKey, value, SourceType, binding.Scope, binding.ScopeId,
                    DateTimeOffset.UtcNow, _revision));
        }

        public async Task<bool> ValidateAsync(SecretBinding binding, CancellationToken cancellationToken = default) =>
            (await GetSecretAsync(binding, cancellationToken)).IsResolved;
    }

    private sealed partial class CommitFailure : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public bool ObservedBothLeavesBeforeRollback { get; private set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context is ExploreDbContext database
                && database.TenantFilterTenantId == PlatformDefaults.DefaultTenantId)
            {
                var leaves = await database.TenantSettingOverrides.AsNoTracking()
                    .Where(row => row.TenantId == PlatformDefaults.DefaultTenantId
                        && (row.SettingKey == GovernanceSettingKeys.Storage.BucketName
                            || row.SettingKey == GovernanceSettingKeys.Storage.DefaultMaxUploadBytes))
                    .ToDictionaryAsync(row => row.SettingKey, row => row.Value, cancellationToken);
                if (leaves.GetValueOrDefault(GovernanceSettingKeys.Storage.BucketName) == "\"rolled-back\"")
                {
                    ObservedBothLeavesBeforeRollback =
                        leaves.GetValueOrDefault(GovernanceSettingKeys.Storage.DefaultMaxUploadBytes) == "4096";
                    throw new InvalidOperationException("Injected storage commit failure.");
                }
            }
            return result;
        }
    }

    private sealed class StorageBoundary
    {
        public List<(string Verb, string Bucket)> Operations { get; } = [];
        public HashSet<(string Bucket, string Key, string Version)> Objects { get; } = [];
        public string? Failure { get; set; }
        public bool BlockVersioning { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IAmazonS3 CreateClient(S3Configuration config, string rawError)
        {
            var client = Substitute.For<IAmazonS3>();
            client.GetBucketVersioningAsync(Arg.Any<GetBucketVersioningRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var request = call.Arg<GetBucketVersioningRequest>();
                ArgumentNullException.ThrowIfNull(request);
                Check("versioning", request.BucketName, config, rawError);
                if (BlockVersioning)
                {
                    var token = call.Arg<CancellationToken>();
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => completion.TrySetCanceled(token));
                    Entered.TrySetResult();
                    await completion.Task;
                }
                return new GetBucketVersioningResponse
                { VersioningConfig = new S3BucketVersioningConfig { Status = VersionStatus.Enabled } };
            });
            client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<PutObjectRequest>();
                ArgumentNullException.ThrowIfNull(request);
                Check("put", request.BucketName, config, rawError);
                if (request.IfNoneMatch != "*" || request.Headers.ContentLength != 0
                    || !Objects.Add((request.BucketName, request.Key, "fixture-probe-version")))
                    throw new InvalidOperationException("Invalid probe write.");
                return new PutObjectResponse { VersionId = "fixture-probe-version" };
            });
            client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<DeleteObjectRequest>();
                ArgumentNullException.ThrowIfNull(request);
                Check("delete", request.BucketName, config, rawError);
                if (request.VersionId != "fixture-probe-version"
                    || !Objects.Remove((request.BucketName, request.Key, request.VersionId)))
                    throw new InvalidOperationException("Probe cleanup did not match a write.");
                return new DeleteObjectResponse { VersionId = request.VersionId };
            });
            return client;
        }

        private void Check(string verb, string bucket, S3Configuration config, string rawError)
        {
            if (bucket != config.BucketName || config.Endpoint != "https://storage.example.test")
                throw new InvalidOperationException("Unexpected storage destination.");
            Operations.Add((verb, bucket));
            if (Failure == verb)
                throw new AmazonS3Exception(rawError) { StatusCode = HttpStatusCode.Forbidden };
        }
    }
}
