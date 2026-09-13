using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.Application.Contracts.Infrastructure;
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
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeTenantStorageHttpTests
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
        // Warm the real S3 cache, then verify a subsequent probe uses the patched bucket.
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
        using (var probe = await admin.PostAsync(Root + "/test", null))
            await Assert.That((await probe.Content.ReadFromJsonAsync<InstanceStorageProviderStatusDto>())!.IsAvailable).IsTrue();
        await Assert.That(factory.Boundary.Operations.Select(operation => operation.Bucket))
            .IsEquivalentTo(new[] { "tenant-storage", "tenant-storage", "tenant-storage", "changed-storage", "changed-storage", "changed-storage" });
        await Assert.That(factory.Boundary.Objects).IsEmpty();
        using var scope = factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
        await resolver.SetValueAsync(GovernanceSettingKeys.Deployment.Mode, JsonSerializer.Serialize("MultiTenant"), SettingScope.Instance, Guid.Empty, data.AdminId);
        await resolver.SetValueAsync(GovernanceSettingKeys.TenantDelegation.LockStorage, "true", SettingScope.Instance, Guid.Empty, data.AdminId);
        // Native scope preserves policy authority without depending on multi-tenant HTTP host routing.
        SetPrincipal(scope, data.AdminId, PlatformDefaults.DefaultTenantId);
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
        var after = await SettingsAsync(admin);
        await Assert.That(after.MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That(after.S3BucketName).IsEqualTo(before.S3BucketName);
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>().InvalidateCache(SettingScope.Tenant, PlatformDefaults.DefaultTenantId);
            scope.ServiceProvider.GetRequiredService<IS3ConfigResolver>().InvalidateCache(PlatformDefaults.DefaultTenantId);
        }
        var persisted = await SettingsAsync(admin);
        await Assert.That(persisted.MaxUploadBytes).IsEqualTo(before.MaxUploadBytes);
        await Assert.That(persisted.S3BucketName).IsEqualTo(before.S3BucketName);
        using var finalProbe = await admin.PostAsync(Root + "/test", null);
        await Assert.That((await finalProbe.Content.ReadFromJsonAsync<InstanceStorageProviderStatusDto>())!.IsAvailable).IsTrue();
        await Assert.That(factory.Boundary.Operations.All(operation => operation.Bucket == "tenant-storage")).IsTrue();
        await Assert.That(factory.Boundary.Objects).IsEmpty();
    }

    [Test]
    [Arguments("head", "s3_bucket_forbidden", 0)]
    [Arguments("put", "s3_put_forbidden", 0)]
    [Arguments("delete", "s3_delete_forbidden", 1)]
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
        SetPrincipal(scope, data.AdminId, data.OtherTenantId);
        var probe = scope.ServiceProvider.GetRequiredService<ICommandHandler<TestTenantStorageProviderCommand, InstanceStorageProviderStatusDto>>();
        var query = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetTenantStorageSettingsQuery, TenantStorageSettingsDto>>();
        await Assert.That(async () => { await probe.ExecuteAsync(new(), default); }).Throws<AuthorizationException>();
        await Assert.That(async () => { await query.QueryAsync(new(), default); }).Throws<AuthorizationException>();
        var patch = await scope.ServiceProvider.GetRequiredService<ICommandHandler<PatchTenantStorageSettingsCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { UserId = data.AdminId, Settings = new() { Policy = new() { MaxUploadBytes = OptionalUpdate<long>.Set(4096) } } }, default);
        await Assert.That(patch.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await Assert.That(factory.Boundary.Operations).IsEmpty();
        SetPrincipal(scope, data.InstanceAdminId, data.OtherTenantId);
        var foreign = await query.QueryAsync(new(), default);
        await Assert.That(foreign.S3BucketName).IsEqualTo("foreign-storage");
        await Assert.That((await probe.ExecuteAsync(new(), default)).IsAvailable).IsTrue();
        await Assert.That(factory.Boundary.Operations.All(operation => operation.Bucket == "foreign-storage")).IsTrue();
        SetPrincipal(scope, data.AdminId, PlatformDefaults.DefaultTenantId);
        factory.Boundary.BlockHead = true;
        using var cancellation = new CancellationTokenSource();
        var pending = probe.ExecuteAsync(new(), cancellation.Token);
        await factory.Boundary.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10))).Throws<OperationCanceledException>();
        await Assert.That(factory.Boundary.Objects).IsEmpty();
        await Assert.That(factory.Boundary.Operations.Last().Verb).IsEqualTo("head");
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

    private static void SetPrincipal(IServiceScope scope, Guid userId, Guid tenantId)
    {
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("internal_user_id", userId.ToString())], "Test"))
        };
    }

    private static async Task<SeedData> SeedAsync(StorageFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var instance = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var membership = await db.TenantUsers.Include(item => item.Tenant).SingleAsync(item => item.UserId == admin.UserId);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = admin.TenantId, Tenant = membership.Tenant,
            TenantUserId = membership.Id, TenantUser = membership, RoleId = (int)RoleEnum.TenantAdmin,
            Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = instance.UserId, User = null!, RoleId = role.Id, Role = role });
        db.StorageUsageCounters.Add(new StorageUsageCounter
        {
            Id = Guid.CreateVersion7(), TenantId = admin.TenantId, Provider = StorageProviders.S3Compatible,
            UsedBytes = 123, ReservedBytes = 17, ObjectCount = 2, ConcurrencyStamp = Guid.CreateVersion7()
        });
        await db.SaveChangesAsync();
        var resolver = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
        foreach (var tenant in new[] { admin.TenantId, other.TenantId })
        {
            foreach (var (key, value) in new[]
            {
                (GovernanceSettingKeys.Storage.Provider, StorageProviders.S3Compatible),
                (GovernanceSettingKeys.Storage.Endpoint, "https://storage.example.test"),
                (GovernanceSettingKeys.Storage.BucketName, tenant == admin.TenantId ? "tenant-storage" : "foreign-storage")
            })
                await resolver.SetValueAsync(key, JsonSerializer.Serialize(value), SettingScope.Tenant, tenant, admin.UserId);
        }
        return new(admin.UserId, member.UserId, instance.UserId, other.TenantId);
    }

    private static HttpClient Client(StorageFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
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

    private sealed class StorageFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-tenant-storage-{Guid.CreateVersion7():N}.db");
        public string AccessKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string SecretKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public StorageBoundary Boundary { get; } = new();
        public CommitFailure CommitFault { get; } = new();

        public static async Task<StorageFactory> CreateAsync()
        {
            var factory = new StorageFactory();
            try
            {
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                factory.ConfigureDatabase(options);
                await using var db = new ExploreDbContext(options.Options);
                await db.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(db, CancellationToken.None);
                return factory;
            }
            catch
            {
                await factory.DisposeAsync();
                throw;
            }
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention().AddInterceptors(CommitFault);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
                services.AddScoped(provider =>
                {
                    var db = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    db.TenantContext = provider.GetRequiredService<ITenantContext>();
                    return db;
                });
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
                var clients = Substitute.For<IS3ClientFactory>();
                clients.CreateDataClient(Arg.Any<S3Configuration>()).Returns(call =>
                {
                    var config = call.Arg<S3Configuration>();
                    ArgumentNullException.ThrowIfNull(config);
                    if (config.AccessKeyId != AccessKey || config.SecretAccessKey != SecretKey)
                        throw new InvalidOperationException("Unexpected storage credentials.");
                    return Boundary.CreateClient(config, SecretKey);
                });
                clients.CreatePresignClient(Arg.Any<S3Configuration>()).Returns(_ => throw new InvalidOperationException("Unexpected presign operation."));
                services.RemoveAll<IS3ClientFactory>();
                services.AddSingleton(clients);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }

    private sealed class CommitFailure : DbTransactionInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Enabled)
                throw new InvalidOperationException("Injected storage commit failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StorageBoundary
    {
        public List<(string Verb, string Bucket)> Operations { get; } = [];
        public HashSet<(string Bucket, string Key)> Objects { get; } = [];
        public string? Failure { get; set; }
        public bool BlockHead { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IAmazonS3 CreateClient(S3Configuration config, string rawError)
        {
            var client = Substitute.For<IAmazonS3>();
            client.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var request = call.Arg<HeadBucketRequest>();
                ArgumentNullException.ThrowIfNull(request);
                Check("head", request.BucketName, config, rawError);
                if (BlockHead)
                {
                    var token = call.Arg<CancellationToken>();
                    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => completion.TrySetCanceled(token));
                    Entered.TrySetResult();
                    await completion.Task;
                }
                return new HeadBucketResponse();
            });
            client.PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<PutObjectRequest>();
                ArgumentNullException.ThrowIfNull(request);
                Check("put", request.BucketName, config, rawError);
                if (request.Headers.ContentLength != 0 || !Objects.Add((request.BucketName, request.Key)))
                    throw new InvalidOperationException("Invalid probe write.");
                return new PutObjectResponse();
            });
            client.DeleteObjectAsync(Arg.Any<DeleteObjectRequest>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var request = call.Arg<DeleteObjectRequest>();
                ArgumentNullException.ThrowIfNull(request);
                Check("delete", request.BucketName, config, rawError);
                if (!Objects.Remove((request.BucketName, request.Key)))
                    throw new InvalidOperationException("Probe cleanup did not match a write.");
                return new DeleteObjectResponse();
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
