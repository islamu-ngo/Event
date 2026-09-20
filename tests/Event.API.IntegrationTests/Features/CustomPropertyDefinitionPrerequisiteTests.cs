using System.Net;
using System.Net.Http.Json;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.CustomPropertyDefinition;
using Explore.Application.Features.CustomPropertyDefinitions.Requests.Queries;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Secrets.Database;
using Explore.Application.Contracts.Operations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel("ApiTestFixture")]
public sealed partial class CustomPropertyDefinitionPrerequisiteTests
{
    private const string Root = "/api/custompropertydefinition";

    [Test]
    public async Task ListHttp_AfterAnotherTenantWarmsCache_MustNotExposeForeignDefinition()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(data.UserId, PlatformDefaults.DefaultTenantId));
        using (var foreign = factory.Services.CreateScope())
        {
            foreign.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(data.ForeignTenantId);
            var result = await foreign.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionListQuery, PaginatedResult<CustomPropertyDefinitionListDto>>>().QueryAsync(
                new GetCustomPropertyDefinitionListQuery(EntityTypeName.Organization), default);
            await Assert.That(result.Items.Single().Id).IsEqualTo(data.ForeignDefinitionId);
        }
        using (var own = factory.Services.CreateScope())
        {
            own.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var repository = own.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>();
            var rows = await repository.GetDefinitionsWithDetailsPaged(EntityTypeName.Organization, 1, 20);
            await Assert.That(rows.Items.Single().Id).IsEqualTo(data.OwnDefinitionId);
        }
        using var response = await client.GetAsync($"{Root}?entityTypeName=Organization");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        await Assert.That(json).DoesNotContain(data.ForeignDefinitionId.ToString());
        await Assert.That(json).Contains(data.OwnDefinitionId.ToString());
    }

    [Test]
    public async Task CreateHttp_MustInvalidatePreviouslyCachedNonDefaultPageSize()
    {
        await using var factory = new DefinitionFactory();
        using var client = factory.CreateClient();
        var data = await SeedAsync(factory);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName,
            TestAuthHandler.CreateTenantAdminHeaderValue(data.UserId, PlatformDefaults.DefaultTenantId));
        using var warm = await client.GetAsync($"{Root}?entityTypeName=Organization&pageSize=1");
        await Assert.That(warm.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var created = await client.PostAsJsonAsync(Root, new CreateCustomPropertyDefinitionDto
        {
            EntityTypeName = EntityTypeName.Organization,
            Namespace = "tenant.community",
            Key = "new_definition",
            DisplayName = "New definition",
            PropertyType = PropertyType.Text,
            ExposureLevel = ExposureLevel.Internal
        });
        await Assert.That(created.StatusCode).IsEqualTo(HttpStatusCode.Created);
        var command = (await created.Content.ReadFromJsonAsync<BaseCommandResponse<Guid>>())!;
        using var detail = await client.GetAsync($"{Root}/{command.Id}");
        await Assert.That(detail.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var repository = scope.ServiceProvider.GetRequiredService<ICustomPropertyDefinitionRepository>();
        await Assert.That((await repository.GetDefinitionsWithDetailsPaged(EntityTypeName.Organization, 1, 1)).TotalCount).IsEqualTo(2);
        var result = await scope.ServiceProvider.GetRequiredService<IQueryHandler<GetCustomPropertyDefinitionListQuery, PaginatedResult<CustomPropertyDefinitionListDto>>>().QueryAsync(
            new GetCustomPropertyDefinitionListQuery(EntityTypeName.Organization, 1, 1), default);
        await Assert.That(result.TotalCount).IsEqualTo(2);
    }

    private static async Task<SeedData> SeedAsync(DefinitionFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        accessor.SetTenant(PlatformDefaults.DefaultTenantId);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var admin = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
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
        var own = Definition(admin.TenantId, "own_definition");
        db.CustomPropertyDefinitions.Add(own);
        await db.SaveChangesAsync();
        var foreign = await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(db);
        accessor.SetTenant(foreign.TenantId);
        var other = Definition(foreign.TenantId, "foreign_definition");
        db.CustomPropertyDefinitions.Add(other);
        await db.SaveChangesAsync();
        return new(admin.UserId, foreign.TenantId, own.Id, other.Id);
    }

    private static CustomPropertyDefinition Definition(Guid tenantId, string key) => new()
    {
        Id = Guid.CreateVersion7(),
        ConcurrencyStamp = Guid.CreateVersion7(),
        TenantId = tenantId,
        EntityTypeName = EntityTypeName.Organization,
        Namespace = "tenant.community",
        Key = key,
        DisplayName = key,
        PropertyType = PropertyType.Text,
        ExposureLevel = ExposureLevel.Internal,
        IsActive = true
    };

    private sealed record SeedData(Guid UserId, Guid ForeignTenantId, Guid OwnDefinitionId, Guid ForeignDefinitionId);

    private sealed class DefinitionFactory : AuthenticatedWebApplicationFactory
    {
        private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"custom-definition-prerequisite-{Guid.CreateVersion7():N}.db");

        public DefinitionReadMonitor Reads { get; } = new();
        public DefinitionCommitControl Commits { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveExploreDbContextRegistrations();
                var options = new DbContextOptionsBuilder<ExploreDbContext>();
                ConfigureDatabase(options);
                using (var db = new ExploreDbContext(options.Options)) db.Database.EnsureCreated();
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

        private void ConfigureDatabase(DbContextOptionsBuilder options)
        {
            PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _databasePath
            });
            options.UseSnakeCaseNamingConvention().AddInterceptors(Reads, Commits);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            ConfigureDatabase(options);
            await using var db = new ExploreDbContext(options.Options);
            SqliteConnection.ClearPool((SqliteConnection)db.Database.GetDbConnection());
            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
        }
    }
}
