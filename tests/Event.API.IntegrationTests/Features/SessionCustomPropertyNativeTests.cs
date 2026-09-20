using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.EventSessionCustomProperty;
using Explore.Application.Features.EventSessionCustomProperties.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class SessionCustomPropertyNativeTests
{
    private const string Root = "/api/eventsessioncustomproperty";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    public async Task WarmTenantA_ThenTenantBWithSameSessionId_DoesNotLeakCachedDefinition()
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        var warm = await ListAsync(factory, data.ForeignTenantId, data.ForeignSessionId, 1, 1);
        await Assert.That(warm.Items.Single().Id).IsEqualTo(data.ForeignDefinitionId);
        using (var scope = Scope(factory, PlatformDefaults.DefaultTenantId))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyRepository>();
            await Assert.That((await repository.GetDefinitionsForSessionPaged(data.ForeignSessionId, 1, 1)).TotalCount).IsEqualTo(0);
        }
        var isolated = await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.ForeignSessionId, 1, 1);
        await Assert.That(isolated.TotalCount).IsEqualTo(0);
        using var response = await client.GetAsync($"{Root}?eventSessionId={data.ForeignSessionId}&pageSize=1");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(json.RootElement.GetProperty("totalCount").GetInt32()).IsEqualTo(0);
        await Assert.That(await response.Content.ReadAsStringAsync()).DoesNotContain(data.ForeignDefinitionId.ToString());
    }

    [Test]
    public async Task Create_RefreshesWarmNonDefaultPages()
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.SessionId, 1, 1)).TotalCount).IsEqualTo(3);
        await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.SessionId, 2, 1);
        using var created = await client.PostAsJsonAsync(Root, CreateDto(data.SessionId));
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        await Assert.That((await scope.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyRepository>()
            .GetDefinitionsForSessionPaged(data.SessionId, 1, 1)).TotalCount).IsEqualTo(4);
        await Assert.That((await ListAsync(factory, PlatformDefaults.DefaultTenantId, data.SessionId, 2, 1)).TotalCount).IsEqualTo(4);
    }

    [Test]
    public async Task CreateOptions_AssignsParentIdentityBeforePersistingChildren()
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var created = await client.PostAsJsonAsync(Root, CreateDto(data.SessionId) with
        {
            PropertyType = PropertyType.Option,
            Options = [new() { Namespace = "tenant.community", Key = "kept", DisplayName = "Kept", Value = "kept", IsActive = true, IsDefault = true }]
        });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!.Id;
        using var detail = await client.GetAsync($"{Root}/{id}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var definition = (await detail.Content.ReadFromJsonAsync<EventSessionCustomPropertyDefinitionDto>(JsonOptions))!;
        await Assert.That(definition.DefaultOptionId).IsEqualTo(definition.Options.Single().Id);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        var graph = await scope.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyRepository>().GetDefinitionWithDetails(id);
        await Assert.That(graph!.Options.Single().EventSessionCustomPropertyDefinitionId).IsEqualTo(id);
    }

    [Test]
    public async Task CreateForForeignSession_RefusesMutation()
    {
        await using var factory = await SessionFactory.CreateAsync();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory, client);
        using var response = await client.PostAsJsonAsync(Root, CreateDto(data.ForeignSessionId));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        await Assert.That((await scope.ServiceProvider.GetRequiredService<IEventSessionCustomPropertyRepository>()
            .GetDefinitionsForSessionPaged(data.ForeignSessionId, 1, 20)).TotalCount).IsEqualTo(0);
    }

    private static async Task<SeedData> SeedAsync(SessionFactory factory, HttpClient client)
    {
        using var scope = Scope(factory, PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var member = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
        var membership = await db.TenantUsers.Include(row => row.Tenant).SingleAsync(row => row.UserId == admin.UserId);
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
        var role = await db.Set<Role>().SingleAsync(item => item.MasterCode == "platform.admin");
        db.PlatformUserRoles.Add(new PlatformUserRole { Id = Guid.CreateVersion7(), UserId = admin.UserId, User = null!, RoleId = role.Id, Role = role });
        var ownEvent = await EventScenarioSeed.SeedPublishedEventAsync(db, admin.ActorId, admin.TenantId);
        var session = await db.EventSessions.SingleAsync(row => row.EventId == ownEvent.EventId);
        var own = Definition(admin.TenantId, session.Id, "own", 0);
        var second = Definition(admin.TenantId, session.Id, "second", 10);
        var third = Definition(admin.TenantId, session.Id, "third", 20);
        db.EventSessionCustomPropertyDefinitions.AddRange(own, second, third);
        await db.SaveChangesAsync();
        var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(foreign.TenantId);
        var foreignEvent = await EventScenarioSeed.SeedPublishedEventAsync(db, foreign.ActorId, foreign.TenantId);
        var foreignSession = await db.EventSessions.SingleAsync(row => row.EventId == foreignEvent.EventId);
        var other = Definition(foreign.TenantId, foreignSession.Id, "foreign-internal", 0);
        db.EventSessionCustomPropertyDefinitions.Add(other);
        await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateAuthHeaderValue(admin.UserId, "Session administrator", (ClaimTypes.Role, "Admin")));
        return new(admin.UserId, member.UserId, session.Id, own.Id, second.Id, third.Id, own.ConcurrencyStamp,
            foreign.TenantId, foreignSession.Id, other.Id);
    }

    private static EventSessionCustomPropertyDefinition Definition(Guid tenantId, Guid sessionId, string key, int order) => new()
    {
        Id = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        TenantId = tenantId,
        EventSessionId = sessionId,
        Namespace = "tenant.community",
        Key = key,
        DisplayName = key,
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Internal,
        IsActive = true,
        SortOrder = order
    };

    private static CreateEventSessionCustomPropertyDefinitionDto CreateDto(Guid sessionId) => new()
    {
        EventSessionId = sessionId,
        Namespace = "tenant.community",
        Key = "created",
        DisplayName = "Created",
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Internal,
        SortOrder = 5,
        IsActive = true
    };

    private static IServiceScope Scope(SessionFactory factory, Guid tenantId, Guid? userId = null)
    {
        var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(tenantId);
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = new DefaultHttpContext
        {
            User = userId is { } id ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], "Test")) : new ClaimsPrincipal()
        };
        return scope;
    }

    private static async Task<PaginatedResult<EventSessionCustomPropertyDefinitionListDto>> ListAsync(
        SessionFactory factory, Guid tenantId, Guid sessionId, int page, int size)
    {
        using var scope = Scope(factory, tenantId);
        return await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetEventSessionCustomPropertyDefinitionListRequest, PaginatedResult<EventSessionCustomPropertyDefinitionListDto>>>().QueryAsync(new GetEventSessionCustomPropertyDefinitionListRequest
        {
            EventSessionId = sessionId,
            PageNumber = page,
            PageSize = size
        }, default);
    }

    private sealed record SeedData(Guid AdminId, Guid MemberId, Guid SessionId, Guid DefinitionId, Guid SecondId, Guid ThirdId,
        Guid Stamp, Guid ForeignTenantId, Guid ForeignSessionId, Guid ForeignDefinitionId);

    private sealed class SessionFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"session-custom-properties-{Guid.CreateVersion7():N}.db");
        public ReadMonitor Reads { get; } = new();
        public CommitControl Commits { get; } = new();

        public static async Task<SessionFactory> CreateAsync()
        {
            var factory = new SessionFactory();
            factory.AdditionalConfiguration["Authorization:Provider"] = "local";
            factory.AdditionalConfiguration["Testing:DisableDeploymentModeCache"] = "true";
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            factory.ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            await SqliteDatabaseInitializer.InitializeAsync(db, CancellationToken.None);
            return factory;
        }

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _path
            });
            options.UseSnakeCaseNamingConvention().AddInterceptors(Reads, Commits);
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
                    db.CurrentUserService = provider.GetRequiredService<ICurrentUserService>();
                    return db;
                });
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path }.ToString());
            SqliteConnection.ClearPool(connection);
            File.Delete(_path);
            File.Delete(_path + "-wal");
            File.Delete(_path + "-shm");
        }
    }

    private sealed class ReadMonitor : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<Guid, int> _reads = new();
        public int Count(Guid tenantId) => _reads.GetValueOrDefault(tenantId);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is ExploreDbContext { TenantContext: { } tenant }
                && command.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                && command.CommandText.Contains("ie_event_session_custom_property_definitions", StringComparison.Ordinal))
                _reads.AddOrUpdate(tenant.TenantId, 1, (_, count) => count + 1);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class CommitControl : DbTransactionInterceptor
    {
        public TaskCompletionSource? Entered { get; set; }
        public TaskCompletionSource? Release { get; set; }
        public bool Fail { get; set; }
        public Action? AfterCommit { get; set; }
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Entered is { } entered && Release is { } release)
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            if (Fail) throw new InvalidOperationException("session-property-commit-fault");
            return result;
        }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            AfterCommit?.Invoke();
            return Task.CompletedTask;
        }
    }
}
