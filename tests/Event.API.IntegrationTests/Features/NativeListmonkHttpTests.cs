using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.API.Controllers;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Integrations;
using Explore.Application.Features.Integrations.Listmonk.Requests.Commands;
using Explore.Application.Features.Integrations.Listmonk.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Settings;
using Explore.Infrastructure.Integrations.Listmonk;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed class NativeListmonkHttpTests
{
    private const string Root = "/api/integrations/listmonk";

    [Test]
    public async Task PublicSettings_DiscloseOnlyConfigurationFlagsAndUseTheAmbientTenant()
    {
        await using var factory = await ListmonkFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using var response = await anonymous.GetAsync(Root + "/settings");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<ListmonkIntegrationSettingsDto>())!;
        await Assert.That(body.ApiUsernameConfigured).IsTrue();
        await Assert.That(body.ApiKeyConfigured).IsTrue();
        await Assert.That(body.CanEdit).IsFalse();
        string json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain(factory.Username);
        await Assert.That(json).DoesNotContain(factory.ApiKey);
        using var admin = Client(factory, data.AdminId);
        await Assert.That((await SettingsAsync(admin)).CanEdit).IsTrue();
        using var forged = Client(factory, data.MemberId);
        await Assert.That((await SettingsAsync(forged)).CanEdit).IsFalse();

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(data.OtherTenantId);
        var foreign = await scope.ServiceProvider.GetRequiredService<
            IQueryHandler<GetListmonkIntegrationSettingsQuery, ListmonkIntegrationSettingsDto>>()
            .QueryAsync(new(), default);
        await Assert.That(foreign.ApiUsernameConfigured).IsFalse();
        await Assert.That(foreign.ApiKeyConfigured).IsFalse();
        await Assert.That(foreign.InstanceUrl).IsNotEqualTo(body.InstanceUrl);
        await Assert.That(factory.Transport.Requests).IsEmpty();
    }

    [Test]
    public async Task SettingsPatch_PreservesAuthorityValidationPartialGroupsLocksAndCachedReads()
    {
        await using var factory = await ListmonkFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var admin = Client(factory, data.AdminId);
        using var forged = Client(factory, data.MemberId);
        using var anonymous = factory.CreateClient();
        var before = await SettingsAsync(admin);
        var connection = new UpdateListmonkIntegrationSettingsDto
        {
            Connection = new() { InstanceUrl = "  https://changed.example.test  ", DefaultListId = 42 }
        };
        using (var denied = await anonymous.PatchAsJsonAsync(Root + "/settings", connection))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var denied = await forged.PatchAsJsonAsync(Root + "/settings", connection))
            await ProblemAsync(denied, HttpStatusCode.BadRequest);
        using (var invalid = await admin.PatchAsJsonAsync(Root + "/settings",
            connection with { Connection = new() { InstanceUrl = "http://localhost", DefaultListId = -1 } }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        await Assert.That(await SettingsAsync(admin)).IsEqualTo(before);
        using (var updated = await admin.PatchAsJsonAsync(Root + "/settings", connection))
        {
            await Assert.That(updated.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That((await updated.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.Id)
                .IsEqualTo(PlatformDefaults.DefaultTenantId);
        }
        var changed = await SettingsAsync(admin);
        await Assert.That(changed.InstanceUrl).IsEqualTo("https://changed.example.test");
        await Assert.That(changed.DefaultListId).IsEqualTo(42);
        await Assert.That(changed.Enabled).IsEqualTo(before.Enabled);
        await Assert.That(changed.SyncOnRegistration).IsEqualTo(before.SyncOnRegistration);
        using (var enabled = await admin.PatchAsJsonAsync(Root + "/settings",
            new UpdateListmonkIntegrationSettingsDto { Behavior = new() { Enabled = true, SyncOnRegistration = true } }))
            await Assert.That(enabled.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That((await SettingsAsync(admin)).Enabled).IsTrue();
        using (var invalid = await admin.PatchAsJsonAsync(Root + "/settings",
            new UpdateListmonkIntegrationSettingsDto { Connection = new() { InstanceUrl = "", DefaultListId = 0 } }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        using (var scope = factory.Services.CreateScope())
        {
            var resolver = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>();
            await resolver.SetValueAsync(GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
                JsonSerializer.Serialize("https://changed.example.test"), SettingScope.Instance, Guid.Empty, data.AdminId);
            await resolver.LockAsync(GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
                SettingScope.Instance, Guid.Empty, data.AdminId);
        }
        using (var locked = await admin.PatchAsJsonAsync(Root + "/settings", connection))
            await ProblemAsync(locked, HttpStatusCode.BadRequest);
        await Assert.That(factory.Transport.Requests).IsEmpty();
    }

    [Test]
    public async Task ConnectionPost_IsAuthenticatedReadOnlyHealthWithCurrentCredentialsAndSafeFailure()
    {
        await using var factory = await ListmonkFactory.CreateAsync();
        var data = await SeedAsync(factory);
        using var anonymous = factory.CreateClient();
        using (var denied = await anonymous.PostAsync(Root + "/test-connection", null))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(factory.Transport.Requests).IsEmpty();
        // Existing authority is any authenticated caller, not a newly invented admin requirement.
        using var member = Client(factory, data.MemberId);
        var before = await SettingsAsync(member);
        using (var connected = await member.PostAsync(Root + "/test-connection", null))
        {
            await Assert.That(connected.StatusCode).IsEqualTo(HttpStatusCode.OK);
            var result = (await connected.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.Id).IsEqualTo(Guid.Empty);
        }
        factory.Transport.Status = HttpStatusCode.ServiceUnavailable;
        using (var failed = await member.PostAsync(Root + "/test-connection", null))
        {
            await ProblemAsync(failed, HttpStatusCode.BadRequest);
            string json = await failed.Content.ReadAsStringAsync();
            await Assert.That(json).DoesNotContain(factory.Username);
            await Assert.That(json).DoesNotContain(factory.ApiKey);
        }
        await Assert.That(factory.Transport.Requests).IsEquivalentTo(new[]
        {
            "GET https://listmonk.example.test/api/health",
            "GET https://listmonk.example.test/api/health"
        });
        await Assert.That(await SettingsAsync(member)).IsEqualTo(before);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(data.OtherTenantId);
        var missing = await scope.ServiceProvider.GetRequiredService<
            IQueryHandler<TestListmonkConnectionQuery, BaseCommandResponse<Guid>>>().QueryAsync(new(), default);
        await Assert.That(missing.IsSuccess).IsFalse();
        await Assert.That(missing.Errors).IsNotEmpty();
        await Assert.That(factory.Transport.Requests.Count).IsEqualTo(2);
    }

    [Test]
    public async Task ScopedConnectionQuery_PropagatesCancellationDuringHealthWithoutReturningValidation()
    {
        await using var factory = await ListmonkFactory.CreateAsync();
        await SeedAsync(factory);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var query = scope.ServiceProvider.GetRequiredService<
            IQueryHandler<TestListmonkConnectionQuery, BaseCommandResponse<Guid>>>();
        factory.Transport.Block = true;
        using var cancellation = new CancellationTokenSource();
        Task<BaseCommandResponse<Guid>> pending = query.QueryAsync(new(), cancellation.Token);
        await factory.Transport.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await cancellation.CancelAsync();
        await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)))
            .Throws<OperationCanceledException>();
    }

    [Test]
    [Arguments(IntegrationSyncRecoveryDecision.ConfirmAccepted, IntegrationSyncStatus.Completed,
        IntegrationSyncFailureCodes.OperatorConfirmedAccepted)]
    [Arguments(IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted, IntegrationSyncStatus.RetryScheduled,
        IntegrationSyncFailureCodes.OperatorRetryDefinitelyNotAccepted)]
    [Arguments(IntegrationSyncRecoveryDecision.DeadLetter, IntegrationSyncStatus.DeadLettered,
        IntegrationSyncFailureCodes.OperatorDeadLettered)]
    public async Task AmbiguityResolution_FencesTenantAuthorityAndOneDecisionWithoutProviderReplay(
        IntegrationSyncRecoveryDecision decision, IntegrationSyncStatus expectedStatus, string expectedCode)
    {
        await using var factory = await ListmonkFactory.CreateAsync();
        var data = await SeedAsync(factory);
        Guid outboxId = await SeedAmbiguityAsync(factory, PlatformDefaults.DefaultTenantId);
        Guid foreignId = await SeedAmbiguityAsync(factory, data.OtherTenantId);
        using var admin = Client(factory, data.AdminId);
        using var forged = Client(factory, data.MemberId);
        using var anonymous = factory.CreateClient();
        var resolution = new ResolveIntegrationSyncAmbiguityDto
        {
            Decision = decision, EvidenceReference = "  incident-reviewed  "
        };
        string route = $"{Root}/queue/{outboxId}/resolve";
        using (var denied = await anonymous.PostAsJsonAsync(route, resolution))
            await Assert.That(denied.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        using (var denied = await forged.PostAsJsonAsync(route, resolution))
            await ProblemAsync(denied, HttpStatusCode.BadRequest);
        using (var invalid = await admin.PostAsJsonAsync(route, resolution with { EvidenceReference = " " }))
            await ProblemAsync(invalid, HttpStatusCode.BadRequest);
        using (var foreign = await admin.PostAsJsonAsync($"{Root}/queue/{foreignId}/resolve", resolution))
            await ProblemAsync(foreign, HttpStatusCode.BadRequest);
        using (var accepted = await admin.PostAsJsonAsync(route, resolution))
            await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using (var repeated = await admin.PostAsJsonAsync(route, resolution))
            await ProblemAsync(repeated, HttpStatusCode.BadRequest);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        // There is no application detail query for parked queue rows.
        var row = await db.IntegrationSyncOutbox.SingleAsync(item => item.Id == outboxId);
        await Assert.That(row.Status).IsEqualTo(expectedStatus);
        await Assert.That(row.LastError).IsEqualTo(expectedCode);
        await Assert.That(row.UpdatedBy).IsEqualTo(data.AdminId);
        await Assert.That(row.CorrelationId).IsEqualTo("incident-reviewed");
        await Assert.That(row.CompletedAt.HasValue).IsEqualTo(decision == IntegrationSyncRecoveryDecision.ConfirmAccepted);
        await Assert.That(row.NextAttemptAt.HasValue).IsEqualTo(decision == IntegrationSyncRecoveryDecision.RetryDefinitelyNotAccepted);
        await Assert.That(row.DeadLetteredAt.HasValue).IsEqualTo(decision == IntegrationSyncRecoveryDecision.DeadLetter);
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(data.OtherTenantId);
        await Assert.That((await db.IntegrationSyncOutbox.SingleAsync(item => item.Id == foreignId)).LastError)
            .IsEqualTo(IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous);
        await Assert.That(factory.Transport.Requests).IsEmpty();
    }

    [Test]
    public async Task Controller_RequiresExactlyTheFourClosedNativePorts()
    {
        Type[] parameters = typeof(ListmonkIntegrationSettingsController).GetConstructors().Single()
            .GetParameters().Select(parameter => parameter.ParameterType).ToArray();
        await Assert.That(parameters).IsEquivalentTo(new[]
        {
            typeof(IQueryHandler<GetListmonkIntegrationSettingsQuery, ListmonkIntegrationSettingsDto>),
            typeof(IQueryHandler<TestListmonkConnectionQuery, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<UpdateListmonkIntegrationSettingsCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<ResolveIntegrationSyncAmbiguityCommand, BaseCommandResponse<Guid>>)
        });
    }

    private static async Task<SeedData> SeedAsync(ListmonkFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var other = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        var membership = await db.TenantUsers.Include(item => item.Tenant)
            .SingleAsync(item => item.UserId == admin.UserId);
        db.TenantUserRoleGrants.Add(new TenantUserRoleGrant
        {
            Id = Guid.CreateVersion7(), TenantId = admin.TenantId, Tenant = membership.Tenant,
            TenantUserId = membership.Id, TenantUser = membership,
            RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant
        });
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsResolver>().SetValueAsync(
            GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl,
            JsonSerializer.Serialize("https://listmonk.example.test"), SettingScope.Tenant, admin.TenantId, admin.UserId);
        return new(admin.UserId, member.UserId, other.TenantId);
    }

    private static async Task<Guid> SeedAmbiguityAsync(ListmonkFactory factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var row = new IntegrationSyncOutbox
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!, Kind = IntegrationKind.Listmonk,
            SourceType = "registration", SourceId = Guid.CreateVersion7(), SubscriberEmail = "subscriber@example.test",
            SubscriberPayloadJson = "{}", ListmonkListId = 1, Status = IntegrationSyncStatus.DeadLettered,
            LastError = IntegrationSyncFailureCodes.ProviderOutcomeAmbiguous, AttemptCount = 1, MaxAttempts = 5,
            CreatedAt = DateTime.UtcNow, DeadLetteredAt = DateTime.UtcNow
        };
        db.IntegrationSyncOutbox.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static HttpClient Client(ListmonkFactory factory, Guid userId)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(userId, PlatformDefaults.DefaultTenantId));
        return client;
    }

    private static async Task<ListmonkIntegrationSettingsDto> SettingsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ListmonkIntegrationSettingsDto>(Root + "/settings"))!;

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await Assert.That(response.StatusCode).IsEqualTo(expected);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("status").GetInt32()).IsEqualTo((int)expected);
    }

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid OtherTenantId);

    private sealed class ListmonkFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"native-listmonk-{Guid.CreateVersion7():N}.db");
        public string Username { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        public string ApiKey { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        public HealthTransport Transport { get; }

        private ListmonkFactory()
        {
            Transport = new HealthTransport(Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{ApiKey}")));
        }

        public static async Task<ListmonkFactory> CreateAsync()
        {
            var factory = new ListmonkFactory();
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
            options.UseSnakeCaseNamingConvention();
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
                secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                    .Returns(call =>
                    {
                        call.Arg<CancellationToken>().ThrowIfCancellationRequested();
                        string? key = call.Arg<string>();
                        ArgumentNullException.ThrowIfNull(key);
                        Guid? tenant = call.Arg<Guid?>();
                        string? value = tenant != PlatformDefaults.DefaultTenantId ? null : key switch
                        {
                            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername => Username,
                            SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey => ApiKey,
                            _ => null
                        };
                        return value is null ? SecretResolutionResult.Unconfigured : SecretResolutionResult.Resolved(
                            new ResolvedSecret(key, value, SecretSourceType.EnvironmentVariable,
                                SecretScope.Tenant, tenant, DateTimeOffset.UtcNow));
                    });
                services.RemoveAll<ISecretResolver>();
                services.AddSingleton(secrets);
                services.RemoveAll<IHttpClientFactory>();
                services.AddHttpClient(ListmonkSyncService.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => Transport);
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

    private sealed class HealthTransport(string expectedAuthorization) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public bool Block { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Get || request.RequestUri?.AbsolutePath != "/api/health" ||
                request.Headers.Authorization?.Scheme != "Basic" ||
                request.Headers.Authorization.Parameter != expectedAuthorization)
                throw new InvalidOperationException("Unexpected Listmonk boundary request.");
            Requests.Add($"{request.Method} {request.RequestUri}");
            if (Block)
            {
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
                Entered.TrySetResult();
                await completion.Task;
            }
            return new HttpResponseMessage(Status) { Content = new StringContent("{\"data\":true}", Encoding.UTF8, "application/json") };
        }
    }
}
