using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Explore.API.Controllers;
using Explore.API.Extensions;
using Explore.API.Middleware;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Configuration;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Features.ExternalApiKeys.Handlers.Commands;
using Explore.Application.Features.ExternalApiKeys.Handlers.Queries;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Features.ExternalApiKeys.Requests.Queries;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IO;

namespace Event.Api.IntegrationTests.Features;

public sealed class ExternalApiKeyIssuanceHttpTests
{
    [Test]
    public async Task Create_WithOperationKey_DoesNotPersistRecoverableCredential()
    {
        await using var host = await IssuanceHost.StartAsync();
        string operationKey = Guid.CreateVersion7().ToString("N");
        using var response = await host.CreateAsync(operationKey);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var issued = await response.Content.ReadFromJsonAsync<CreateExternalApiKeyCommandResponse>();
        await Assert.That(issued?.ApiKey is not null).IsTrue();

        await using var scope = host.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var records = await context.IdempotencyRecords.AsNoTracking().ToListAsync();
        // Assert a boolean so a failing Red run cannot print credential-bearing replay bodies.
        await Assert.That(records.Any(record =>
            record.ResponseBody?.Contains(issued!.ApiKey!, StringComparison.Ordinal) == true)).IsFalse();
        var key = await context.ExternalApiKeys.IgnoreQueryFilters().AsNoTracking().SingleAsync();
        await Assert.That(key.SecretHash.Contains(issued!.ApiKey!, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task Create_WhenResponseIsRetried_ReturnsMetadataWithoutCredential()
    {
        await using var host = await IssuanceHost.StartAsync();
        string operationKey = Guid.CreateVersion7().ToString("N");
        using var first = await host.CreateAsync(operationKey);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var issued = await first.Content.ReadFromJsonAsync<CreateExternalApiKeyCommandResponse>();
        using var replay = await host.CreateAsync(operationKey);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var recovered = await replay.Content.ReadFromJsonAsync<CreateExternalApiKeyCommandResponse>();
        await Assert.That(recovered?.Id).IsEqualTo(issued!.Id);
        await Assert.That(recovered?.ApiKey is null).IsTrue();
    }

    [Test]
    public async Task Create_AfterCommittedOwnerRevocation_DeniesReplayWithoutMetadata()
    {
        await using var host = await IssuanceHost.StartAsync();
        string operationKey = Guid.CreateVersion7().ToString("N");
        using var first = await host.CreateAsync(operationKey);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var issued = await first.Content.ReadFromJsonAsync<CreateExternalApiKeyCommandResponse>();
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            await context.PlatformUserRoles.Where(role => role.UserId == host.UserId).ExecuteDeleteAsync();
            var authority = scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>();
            await Assert.That(await authority.IsUserPlatformAdmin(host.UserId)).IsFalse();
        }

        using var replay = await host.CreateAsync(operationKey);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        string body = await replay.Content.ReadAsStringAsync();
        await Assert.That(body.Contains(issued!.ApiKey!, StringComparison.Ordinal)).IsFalse();
        await Assert.That(body.Contains(issued.Id.ToString(), StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("invalid key")]
    [Arguments("invalid,key")]
    [Arguments("invalid\tkey")]
    public async Task Create_WithInvalidOperationKey_RejectsBeforeIssuance(string? operationKey)
    {
        await using var host = await IssuanceHost.StartAsync();
        using var response = await host.CreateAsync(operationKey);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await using var scope = host.App.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(await context.ExternalApiKeys.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task Create_WithMultipleOperationKeys_RejectsBeforeIssuance()
    {
        await using var host = await IssuanceHost.StartAsync();
        using var response = await host.CreateAsync("first", secondOperationKey: "second");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_With129CharacterOperationKey_RejectsBeforeIssuance()
    {
        await using var host = await IssuanceHost.StartAsync();
        using var response = await host.CreateAsync(new string('a', 129));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Create_With128CharacterOperationKey_IssuesSuccessfully()
    {
        await using var host = await IssuanceHost.StartAsync();
        using var response = await host.CreateAsync(new string('a', 128));
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Create_WithSameOperationAndChangedPolicy_ReturnsConflict()
    {
        await using var host = await IssuanceHost.StartAsync();
        string operationKey = Guid.CreateVersion7().ToString("N");
        using var first = await host.CreateAsync(operationKey);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using var changed = await host.CreateAsync(operationKey, new CreateExternalApiKeyDto
        {
            Name = "Issuance boundary",
            ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.InstanceAdmin,
            Scopes = ["events:write"]
        });
        await Assert.That(changed.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Create_WithRevokedOrExpiredKey_DoesNotRecoverUsableMetadata(bool expire)
    {
        await using var host = await IssuanceHost.StartAsync();
        string operationKey = Guid.CreateVersion7().ToString("N");
        using var first = await host.CreateAsync(operationKey);
        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
        var issued = await first.Content.ReadFromJsonAsync<CreateExternalApiKeyCommandResponse>();
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            // This models a committed policy change by an independent process.
            var key = await context.ExternalApiKeys.IgnoreQueryFilters().SingleAsync(row => row.Id == issued!.Id);
            if (expire)
                key.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            else
                key.ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Revoked;
            await context.SaveChangesAsync();
        }

        using var replay = await host.CreateAsync(operationKey);
        await Assert.That(replay.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        string body = await replay.Content.ReadAsStringAsync();
        await Assert.That(body.Contains(issued!.ApiKey!, StringComparison.Ordinal)).IsFalse();
    }

    private sealed class IssuanceHost(
        WebApplication app,
        HttpClient client,
        SqliteConnection connection,
        string authorityPath,
        Guid userId) : IAsyncDisposable
    {
        public WebApplication App { get; } = app;
        public Guid UserId { get; } = userId;

        public async Task<HttpResponseMessage> CreateAsync(
            string? operationKey,
            CreateExternalApiKeyDto? payload = null,
            string? secondOperationKey = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/ExternalApiKey")
            {
                Content = JsonContent.Create(payload ?? new CreateExternalApiKeyDto
                {
                    Name = "Issuance boundary",
                    ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.InstanceAdmin,
                    Scopes = ["events:read"]
                })
            };
            if (operationKey is not null)
                request.Headers.TryAddWithoutValidation("Idempotency-Key", operationKey);
            if (secondOperationKey is not null)
                request.Headers.TryAddWithoutValidation("Idempotency-Key", secondOperationKey);
            return await client.SendAsync(request);
        }

        public static async Task<IssuanceHost> StartAsync()
        {
            Guid userId = Guid.CreateVersion7();
            Guid tenantId = Guid.CreateVersion7();
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = ":memory:"
            }.ToString());
            await connection.OpenAsync();
            string authorityPath = Path.Combine(
                Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(),
                $"issuance-http-authority-{Guid.CreateVersion7():N}.db");
            string authorityConnection = new SqliteConnectionStringBuilder { DataSource = authorityPath }.ToString();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddAuthorization();
            builder.Services.AddApiExceptionHandling();
            builder.Services.AddApiRateLimiting(builder.Configuration, builder.Environment);
            builder.Services.AddDataProtection();
            builder.Services.AddMetrics();
            builder.Services.AddSingleton<BusinessMetrics>();
            builder.Services.AddSingleton<RecyclableMemoryStreamManager>();
            builder.Services.AddSingleton<ITenantContext>(new FixedTenantContext(tenantId));
            builder.Services.AddDbContext<ExploreDbContext>(options =>
                options.UseSqlite(connection).UseSnakeCaseNamingConvention());
            builder.Services.AddDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>(options =>
                options.UseSqlite(authorityConnection).UseSnakeCaseNamingConvention());
            builder.Services.AddScoped<IPrivacyIdentityFenceAuthority>(services =>
                new EmbeddedPrivacyErasureAuthorityRepository(
                    services.GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>(),
                    TimeProvider.System, Options.Create(new PrivacyErasureOptions())));
            builder.Services.AddScoped<IUnitOfWork, EfCoreUnitOfWork>();
            builder.Services.AddScoped<IExternalApiKeyIssuanceReceiptRepository, ExternalApiKeyIssuanceReceiptRepository>();
            builder.Services.AddScoped<IExternalApiKeyIssuanceAuthority, ExternalApiKeyIssuanceAuthority>();
            builder.Services.AddScoped<IUserContext, UserContext>();
            builder.Services.AddScoped<IAdminContext, AdminContext>();
            builder.Services.AddScoped<IPlatformUserRoleRepository, PlatformUserRoleRepository>();
            builder.Services.AddScoped<ITenantUserRoleGrantRepository, TenantUserRoleGrantRepository>();
            builder.Services.AddScoped<IUserExternalLoginRepository, UserExternalLoginRepository>();
            builder.Services.AddScoped<IExternalApiKeyRepository, ExternalApiKeyRepository>();
            builder.Services.AddScoped<IExternalApiKeyQuotaRepository, ExternalApiKeyQuotaRepository>();
            builder.Services.AddScoped<IOrganizationRepository, OrganizationRepository>();
            builder.Services.AddScoped<IOrganizationMemberRepository, OrganizationMemberRepository>();
            builder.Services.AddScoped<IGroupRepository, GroupRepository>();
            builder.Services.AddScoped<IGroupMemberRepository, GroupMemberRepository>();
            builder.Services.AddScoped<IIdempotencyRepository, IdempotencyRepository>();
            builder.Services.AddScoped<ICommandHandler<CreateExternalApiKeyCommand, CreateExternalApiKeyCommandResponse>, CreateExternalApiKeyCommandHandler>();
            builder.Services.AddScoped<ICommandHandler<UpdateExternalApiKeyPolicyCommand, BaseCommandResponse<Guid>>, UpdateExternalApiKeyPolicyCommandHandler>();
            builder.Services.AddScoped<ICommandHandler<RevokeExternalApiKeyCommand, bool>, RevokeExternalApiKeyCommandHandler>();
            builder.Services.AddScoped<IQueryHandler<GetExternalApiKeyListRequest, List<ExternalApiKeyListDto>>, GetExternalApiKeyListRequestHandler>();
            builder.Services.AddScoped<IQueryHandler<GetExternalApiKeyDetailsRequest, ExternalApiKeyListDto?>, GetExternalApiKeyDetailsRequestHandler>();
            builder.Services.AddScoped<IQueryHandler<GetExternalApiKeyUsageReportRequest, List<ExternalApiKeyUsageReportDto>>, GetExternalApiKeyUsageReportRequestHandler>();
            builder.Services.AddControllers().AddApplicationPart(typeof(ExternalApiKeyController).Assembly)
                .ConfigureApplicationPartManager(manager =>
                    manager.FeatureProviders.Add(new IssuanceControllerFeatureProvider()));

            var app = builder.Build();
            await using (var authority = await app.Services
                .GetRequiredService<IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>>().CreateDbContextAsync())
                await authority.Database.EnsureCreatedAsync();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
                context.TenantContext = new FixedTenantContext(tenantId);
                await context.Database.EnsureCreatedAsync();
                var platform = new RoleScope { Id = 0, MasterCode = "PLATFORM", FullName = "Platform" };
                var role = new Role { Id = 1, MasterCode = "platform.admin", FullName = "Administrator", RoleScope = platform };
                var user = new User
                {
                    Id = userId,
                    Pii = new UserPii { Email = "", FirstName = "", LastName = "" }
                };
                context.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    User = user,
                    UserId = userId,
                    Role = role,
                    RoleId = role.Id,
                    GrantedAt = DateTime.UtcNow
                });
                context.Tenants.Add(new Tenant
                {
                    Id = tenantId,
                    FullName = "Issuance tests",
                    Slug = "issuance-tests",
                    TenantStatus = new TenantStatus { Id = 1, MasterCode = "ACTIVE", FullName = "Active" }
                });
                context.ExternalApiKeyOwnerTypes.Add(new ExternalApiKeyOwnerTypeLookup
                {
                    Id = (int)ExternalApiKeyOwnerType.InstanceAdmin,
                    MasterCode = "INSTANCE_ADMIN",
                    FullName = "Instance administrator"
                });
                context.ExternalApiKeyStatuses.Add(new ExternalApiKeyStatus
                {
                    Id = (int)ExternalApiKeyStatusEnum.Active,
                    MasterCode = "ACTIVE",
                    FullName = "Active",
                    IsUsable = true
                });
                context.ExternalApiKeyStatuses.Add(new ExternalApiKeyStatus
                {
                    Id = (int)ExternalApiKeyStatusEnum.Revoked,
                    MasterCode = "REVOKED",
                    FullName = "Revoked",
                    IsUsable = false
                });
                context.ExternalApiKeyCreditPeriods.Add(new ExternalApiKeyCreditPeriod
                {
                    Id = (int)ExternalApiKeyCreditPeriodEnum.None,
                    MasterCode = "NONE",
                    FullName = "None"
                });
                await context.SaveChangesAsync();
            }

            app.UseApiExceptionHandling();
            app.UseRouting();
            app.Use(async (context, next) =>
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim("sub", userId.ToString())], "BoundaryPrincipal"));
                context.RequestServices.GetRequiredService<ExploreDbContext>().TenantContext =
                    context.RequestServices.GetRequiredService<ITenantContext>();
                await next(context);
            });
            app.UseAuthorization();
            app.UseRateLimiter();
            app.UseMiddleware<IdempotencyMiddleware>();
            app.MapControllers();
            await app.StartAsync();
            return new IssuanceHost(app, app.GetTestClient(), connection, authorityPath, userId);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await App.DisposeAsync();
            await connection.DisposeAsync();
            File.Delete(authorityPath);
            File.Delete(authorityPath + "-wal");
            File.Delete(authorityPath + "-shm");
        }
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId { get; } = tenantId;
    }

    private sealed class IssuanceControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            foreach (var controller in feature.Controllers.ToArray())
                if (controller.AsType() != typeof(ExternalApiKeyController))
                    feature.Controllers.Remove(controller);
        }
    }
}
