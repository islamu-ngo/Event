using System.Security.Cryptography;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Cerbos.Sdk.Builder;
using Cerbos.Sdk.Response;
using Grpc.Core;
using Explore.Application.Authorization;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
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

internal sealed class NotificationHttpFixture : AuthenticatedWebApplicationFactory
{
    private readonly string _database = Path.Combine(Path.GetTempPath(), $"native-notifications-{Guid.CreateVersion7():N}.db");
    public NotificationTransactionGate? TransactionGate { get; private init; }
    public Guid UserId { get; private set; }
    public Guid StrangerId { get; private set; }
    public Guid GroupAdminOnlyId { get; private set; }
    public Guid OtherTenantId { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid GroupId { get; private set; }
    public Guid ForeignGroupId { get; private set; }
    public Guid ForeignOrganizationId { get; private set; }
    public string PublicKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(65));

    public static async Task<NotificationHttpFixture> CreateAsync(NotificationTransactionGate? transactionGate = null, bool useCerbos = false)
    {
        var factory = new NotificationHttpFixture { TransactionGate = transactionGate };
        factory.AdditionalConfiguration["Authorization:Provider"] = useCerbos ? "cerbos" : "local";
        try
        {
            var options = new DbContextOptionsBuilder<ExploreDbContext>();
            factory.ConfigureDatabase(options);
            await using (var db = new ExploreDbContext(options.Options))
            {
                await db.Database.EnsureCreatedAsync();
                await SqliteDatabaseInitializer.InitializeAsync(db, default);
            }
            using var scope = factory.Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var organization = await TenantScenarioSeed.SeedActiveTenantWithOrganizationPublisherAsync(context);
            factory.UserId = organization.UserId;
            factory.OrganizationId = organization.OrganizationId;
            factory.StrangerId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context)).UserId;
            factory.GroupAdminOnlyId = (await TenantScenarioSeed.SeedActiveTenantWithUserAsync(context)).UserId;
            factory.OtherTenantId = (await TenantScenarioSeed.SeedSecondaryTenantWithUserAsync(context)).TenantId;
            var parent = await context.OrganizationTenants.SingleAsync(row => row.OrganizationId == factory.OrganizationId);
            factory.GroupId = AddGroup(context, factory.GroupAdminOnlyId, PlatformDefaults.DefaultTenantId, parent.Id);
            factory.ForeignGroupId = AddGroup(context, factory.GroupAdminOnlyId, factory.OtherTenantId, null);
            var foreignOrganization = new Organization
            {
                Id = Guid.CreateVersion7(), ConcurrencyStamp = Guid.CreateVersion7(),
                Pii = new OrganizationPii { FullName = "Foreign notification owner" }
            };
            foreignOrganization.Pii.OrganizationId = foreignOrganization.Id;
            factory.ForeignOrganizationId = foreignOrganization.Id;
            var foreignParticipation = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(), TenantId = factory.OtherTenantId, Tenant = null!,
                OrganizationId = foreignOrganization.Id, Organization = foreignOrganization,
                ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.OrganizationMembers.Add(new OrganizationMember
            {
                Id = Guid.CreateVersion7(), TenantId = factory.OtherTenantId, Tenant = null!,
                OrganizationTenantId = foreignParticipation.Id, OrganizationTenant = foreignParticipation,
                UserId = factory.UserId, User = null!, RoleId = (int)RoleEnum.OrgAdmin, Role = null!
            });
            await context.SaveChangesAsync();
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    private static Guid AddGroup(ExploreDbContext db, Guid userId, Guid tenantId, Guid? parentId)
    {
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Notification group", ConcurrencyStamp = Guid.CreateVersion7() };
        var participation = new GroupTenant
        {
            Id = Guid.CreateVersion7(), GroupId = group.Id, Group = group,
            TenantId = tenantId, Tenant = null!, ParentOrganizationTenantId = parentId,
            ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        db.GroupMembers.Add(new GroupMember
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Tenant = null!,
            GroupTenantId = participation.Id, GroupTenant = participation,
            UserId = userId, User = null!, RoleId = (int)RoleEnum.GroupAdmin, Role = null!
        });
        return group.Id;
    }

    public HttpClient Client(Guid? userId = null)
    {
        var client = CreateClient();
        if (userId.HasValue)
            client.DefaultRequestHeaders.Add(TestAuthHandler.AuthHeaderName, TestAuthHandler.CreateAuthHeaderValue(userId.Value));
        return client;
    }

    public async Task<Guid> SeedNotificationAsync(Guid userId, Guid? tenantId = null, int scopeId = (int)ActorTypeEnum.User)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var notification = new Notification
        {
            Id = Guid.CreateVersion7(), UserId = userId, User = null!,
            TenantId = tenantId ?? PlatformDefaults.DefaultTenantId, Tenant = null!,
            NotificationTypeId = (int)NotificationTypeEnum.EventCreated, NotificationType = null!,
            NotificationScopeId = scopeId, NotificationScope = null!,
            Title = "Notification test", DeduplicationKey = Guid.CreateVersion7().ToString(),
            CreatedAt = DateTime.UtcNow
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification.Id;
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, new PrimaryDatabaseConnectionOptions
        {
            Role = PrimaryDatabaseRole.Runtime, Provider = PrimaryDatabaseProvider.Sqlite, Database = _database
        });
        options.UseSnakeCaseNamingConvention();
        if (TransactionGate is not null)
            options.AddInterceptors(TransactionGate);
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
            var push = Substitute.For<IWebPushConfigurationProvider>();
            push.GetPublicConfiguration().Returns(new WebPushPublicConfiguration(true, PublicKey));
            services.RemoveAll<IWebPushConfigurationProvider>();
            services.AddSingleton(push);
            // Only the remote PDP is substituted. Runtime routing, principal construction,
            // persisted administrative memberships and the native decorators remain real.
            var pdp = Substitute.For<ICerbosClient>();
            pdp.CheckResourcesAsync(Arg.Any<CheckResourcesRequest>(), Arg.Any<Metadata>()).Returns(call =>
            {
                var request = call.Arg<CheckResourcesRequest>().ToCheckResourcesRequest();
                var response = new Cerbos.Api.V1.Response.CheckResourcesResponse();
                foreach (var entry in request.Resources)
                {
                    string? organizationId = entry.Resource.Kind == ResourceKinds.Organization
                        ? entry.Resource.Id
                        : entry.Resource.Attr.TryGetValue("organizationId", out var parent) ? parent.StringValue : null;
                    bool instanceAdmin = request.Principal.Attr["isInstanceAdmin"].BoolValue;
                    bool tenantAdmin = request.Principal.Attr["tenantMemberships"].StructValue.Fields
                        .ContainsKey(entry.Resource.Attr["tenantId"].StringValue);
                    bool parentAdmin = organizationId is not null
                        && request.Principal.Attr["orgMemberships"].StructValue.Fields.ContainsKey(organizationId);
                    var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                    {
                        Resource = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry.Types.Resource
                        {
                            Id = entry.Resource.Id, Kind = entry.Resource.Kind
                        }
                    };
                    // Bounded provider-boundary model of the bundled organization/group policies:
                    // authenticated view; administrative CRUD; never GroupAdmin-only management.
                    foreach (string action in entry.Actions)
                    {
                        bool allowed = entry.Resource.Kind is ResourceKinds.Organization or ResourceKinds.Group
                            && (instanceAdmin || action == AuthorizationActions.View
                                || (action is AuthorizationActions.Create or AuthorizationActions.Update or AuthorizationActions.Delete
                                    && (tenantAdmin || parentAdmin)));
                        result.Actions.Add(action, allowed ? Effect.Allow : Effect.Deny);
                    }
                    response.Results.Add(result);
                }
                return new CheckResourcesResponse(response);
            });
            services.RemoveAll<ICerbosClient>();
            services.AddSingleton(pdp);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(_database);
        File.Delete(_database + "-wal");
        File.Delete(_database + "-shm");
    }
}
