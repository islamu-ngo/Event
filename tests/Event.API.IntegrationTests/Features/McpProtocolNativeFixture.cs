using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Cerbos.Api.V1.Effect;
using Cerbos.Sdk;
using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Explore.Application.Authorization;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Ai;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Infrastructure.Ai;
using Explore.Infrastructure.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Grpc.Core;
using NSubstitute;

namespace Event.Api.IntegrationTests.Features;

/// <summary>
/// Provisions original Local account sources with setup credentials, then sends signed
/// HTTP and MCP requests through enrolled Runtime contexts and production graph owners.
/// The external PDP records the actual production questions and refuses moderation
/// without the server's persisted instance-administrator principal attribute.
/// </summary>
internal sealed class McpProtocolNativeFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainerFixture postgres;
    private readonly TenantBrandingNativeAuthenticationHost host;
    private readonly PolicyTransport policy = new();
    private readonly string slug;
    private string token = null!;
    internal Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid OtherTenantId { get; } = Guid.CreateVersion7();
    internal Guid UserId { get; private set; }
    internal HttpClient Client { get; }
    internal Exception? RequestFailure => host.RequestFailure;

    // Successful proposals authorize both before and inside the Serializable graph
    // transaction. Retain distinct original questions, not a synthetic single decision.
    internal IReadOnlyList<AuthorizationRequest> ModerationChecks => policy.Checks.Distinct().ToArray();

    private McpProtocolNativeFixture(PostgreSqlContainerFixture postgres, bool mcpEnabled)
    {
        this.postgres = postgres;
        slug = $"mcp-{TenantId:N}";
        host = new TenantBrandingNativeAuthenticationHost(postgres, TenantId, [], configureServices: services =>
        {
            services.PostConfigure<McpAdapterSettings>(settings =>
            {
                settings.Enabled = mcpEnabled;
                settings.EndpointPath = "/mcp";
                settings.Stateless = true;
                settings.EnableLegacySse = false;
            });
            // Only external model/PDP transports are substituted. Settings, actor-source
            // admission, graph persistence and Runtime authorization remain production.
            services.RemoveAll<IAiChatProvider>();
            services.RemoveAll<IAiModelCatalog>();
            services.AddSingleton<FakeAiChatProvider>();
            services.AddSingleton<IAiChatProvider>(sp => sp.GetRequiredService<FakeAiChatProvider>());
            services.AddSingleton<IAiModelCatalog>(sp => sp.GetRequiredService<FakeAiChatProvider>());
            services.PostConfigure<AuthorizationProviderDeploymentOptions>(options => options.Provider = "cerbos");
            services.PostConfigure<CerbosSettings>(options =>
            {
                options.GrpcEndpoint = "http://mcp.policy.test:3593";
                options.UsePolicyScope = true;
            });
            services.RemoveAll<ICerbosClient>();
            services.RemoveAll<ICerbosClientFactory>();
            services.AddSingleton(policy.Client);
            services.AddSingleton(policy.Factory);
        });
        Client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    internal static async Task<McpProtocolNativeFixture> CreateAsync(
        PostgreSqlContainerFixture postgres, bool mcpEnabled = true, bool instanceAdmin = false)
    {
        var fixture = new McpProtocolNativeFixture(postgres, mcpEnabled);
        try
        {
            await fixture.InitializeAsync(instanceAdmin);
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>Setup and observational reads never lend their authority to a request.</summary>
    internal ExploreDbContext CreateSetupContext()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.ManyServiceProvidersCreatedWarning));
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.EnableTenantFilterBypass("MCP fixture setup and observation use only the Migrator credential.");
        return context;
    }

    private async Task InitializeAsync(bool instanceAdmin)
    {
        await host.EnsurePrivacyAuthorityReadyAsync();
        await using (var setup = CreateSetupContext())
        {
            setup.Tenants.AddRange(
                NewTenant(TenantId, slug), NewTenant(OtherTenantId, $"mcp-{OtherTenantId:N}"));
            foreach (var (key, value, type) in new[]
            {
                (GovernanceSettingKeys.Security.AuthorizationProvider, "\"cerbos\"", SettingValueType.String),
                (GovernanceSettingKeys.Cerbos.GrpcEndpoint, "\"http://mcp.policy.test:3593\"", SettingValueType.String),
                (GovernanceSettingKeys.Deployment.Mode, "\"MultiTenant\"", SettingValueType.String),
                (GovernanceSettingKeys.AiAssistant.Enabled, "true", SettingValueType.Boolean),
                (GovernanceSettingKeys.AiAssistant.Provider, JsonSerializer.Serialize(AiProviderDefaults.ProviderFake), SettingValueType.String),
                (GovernanceSettingKeys.AiAssistant.ModelId, JsonSerializer.Serialize(AiProviderDefaults.FakeModelId), SettingValueType.String),
                (GovernanceSettingKeys.AiAssistant.DailyMessageLimit, "50", SettingValueType.Integer),
                (GovernanceSettingKeys.AiAssistant.ToolProposalsEnabled, "true", SettingValueType.Boolean),
                (GovernanceSettingKeys.Mcp.Enabled, "true", SettingValueType.Boolean),
                (GovernanceSettingKeys.Mcp.EnableLegacySse, "false", SettingValueType.Boolean)
            })
            {
                var setting = await setup.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
                if (setting is null)
                    setup.SystemSettings.Add(new SystemSetting { SettingKey = key, Value = value, ValueType = type });
                else
                {
                    setting.Value = value;
                    setting.ValueType = type;
                }
            }
            await setup.SaveChangesAsync();
            var sql = new HumanPrincipalSourceSql(setup);
            string runtime = sql.Quote(postgres.NativeQueueEnrollment.RuntimeLogin);
            // Match the existing AI fixture's ordinary data grants. Private human,
            // actor and account sources retain their execute-only native contracts.
            await setup.Database.ExecuteSqlRawAsync(
                $"GRANT SELECT ON {sql.Table(typeof(SystemSetting))}, {sql.Table(typeof(TenantSetting))} TO {runtime}");
            foreach (Type type in new[]
            {
                typeof(AiConversation), typeof(AiMessage), typeof(AiRun), typeof(AiConversationReference),
                typeof(AiProposedAction), typeof(AiToolExecution)
            })
                await setup.Database.ExecuteSqlRawAsync(
                    $"GRANT SELECT, INSERT, UPDATE, DELETE ON {sql.Table(type)} TO {runtime}");
            foreach (Type type in new[]
            {
                typeof(AiConversationStatusLookup), typeof(AiMessageRoleLookup), typeof(AiRunStatusLookup),
                typeof(AiReferenceKindLookup), typeof(AiProposedActionKindLookup),
                typeof(AiProposedActionStatusLookup), typeof(AiProviderKindLookup),
                typeof(Explore.Domain.Event), typeof(EventParticipationConfiguration)
            })
                await setup.Database.ExecuteSqlRawAsync($"GRANT SELECT ON {sql.Table(type)} TO {runtime}");
        }
        UserId = await ProvisionAndLoginAsync(instanceAdmin);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var deployment = scope.ServiceProvider.GetRequiredService<IDeploymentModeProvider>();
            await deployment.InvalidateCacheAsync();
            await Assert.That(await deployment.GetCurrentModeAsync()).IsEqualTo(DeploymentMode.MultiTenant);
        }
        host.UseRuntimeRequestContexts();
    }

    private static Tenant NewTenant(Guid id, string tenantSlug) => new()
    {
        Id = id,
        FullName = "Native MCP protocol",
        Slug = tenantSlug,
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private async Task<Guid> ProvisionAndLoginAsync(bool instanceAdmin)
    {
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(TenantId);
        await using var setup = CreateSetupContext();
        setup.TenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        setup.ClearTenantFilterBypass();
        setup.BindTenantLease();
        scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(setup, TenantId);
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var initiator = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = string.Empty, FirstName = "Credential", LastName = "Provisioner" }
        };
        database.Users.Add(initiator);
        if (!await database.InstanceBootstrapStates.AnyAsync())
        {
            var bootstrap = InstanceBootstrapState.CreateInteractivePending(
                Guid.CreateVersion7(), DeploymentMode.MultiTenant, DateTime.UtcNow);
            bootstrap.CompleteInteractive(initiator.Id, DateTime.UtcNow);
            database.InstanceBootstrapStates.Add(bootstrap);
        }
        await database.SaveChangesAsync();
        string email = $"mcp-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "MCP", "Participant"), CancellationToken.None);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var receipt = created.Receipt!;
        var user = new User
        {
            Id = receipt.LocalSubjectId,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = email, FirstName = "MCP", LastName = "Participant" }
        };
        database.AddRange(user, new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "MCP Participant" },
            CreatedAt = DateTime.UtcNow
        }, new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = user.Id.ToString("D"),
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
        var pending = (await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken.None))!;
        await Assert.That(await credentials.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(receipt.OperationId, pending.OperationConcurrencyStamp), CancellationToken.None))
            .IsEqualTo(LocalCredentialActivationOutcome.Activated);
        var identityUser = (await manager.FindByIdAsync(receipt.LocalSubjectId.ToString("D")))!;
        await database.Entry(identityUser).ReloadAsync();
        DateTimeOffset issuedAt = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            new LocalCredentialReplacementAuthority(
                new LocalCredentialReplacementSubject(identityUser.Id, receipt.OperationId, identityUser.SecurityStamp!),
                issuedAt, issuedAt.AddMinutes(5)), password), CancellationToken.None))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await database.Entry(identityUser).ReloadAsync();
        identityUser.EmailConfirmed = true;
        await Assert.That((await manager.UpdateAsync(identityUser)).Succeeded).IsTrue();
        var binding = await credentials.ReadLinkedIdentityAsync(receipt.LocalSubjectId, CancellationToken.None);
        await Assert.That(binding!.CredentialState).IsEqualTo(LocalCredentialState.Ready);
        await using (var seed = CreateSetupContext())
        {
            foreach (Guid tenant in new[] { TenantId, OtherTenantId })
                seed.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant,
                    Tenant = null!,
                    UserId = user.Id,
                    User = null!,
                    ActorId = receipt.PersonalActorId,
                    Actor = null!,
                    StatusId = (int)TenantUserStatusEnum.Active,
                    JoinedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                });
            if (instanceAdmin)
                seed.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = user.Id,
                    User = null!,
                    RoleId = (int)RoleEnum.Admin,
                    Role = null!,
                    GrantedAt = DateTime.UtcNow
                });
            await seed.SaveChangesAsync();
        }
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/local/login");
        login.Headers.Add("X-Tenant-Slug", slug);
        login.Content = JsonContent.Create(new LocalAuthRequestDto(email, password));
        using var response = await Client.SendAsync(login);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(host.RequestFailure?.GetType().Name ?? "The genuine Local login must succeed.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        token = body.RootElement.GetProperty("token").GetString()!;
        return user.Id;
    }

    internal HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Tenant-Slug", slug);
        return request;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.DisposeAsync();
        host.PrivacyKeys.Dispose();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = host.PrivacyKeys.AuthorityPath
        }.ToString());
        SqliteConnection.ClearPool(connection);
        string directory = Path.GetDirectoryName(host.PrivacyKeys.AuthorityPath)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    /// <summary>External policy transport; production builds every principal, resource and action.</summary>
    private sealed class PolicyTransport
    {
        internal ICerbosClient Client { get; } = Substitute.For<ICerbosClient>();
        internal ICerbosClientFactory Factory { get; } = Substitute.For<ICerbosClientFactory>();
        internal ConcurrentQueue<AuthorizationRequest> Checks { get; } = new();

        internal PolicyTransport()
        {
            Factory.GetOrCreate(Arg.Any<string>()).Returns(Client);
            Client.CheckResourcesAsync(Arg.Any<Cerbos.Sdk.Builder.CheckResourcesRequest>(), Arg.Any<Metadata>())
                .Returns(call =>
                {
                    var sent = call.ArgAt<Cerbos.Sdk.Builder.CheckResourcesRequest>(0).ToCheckResourcesRequest().Clone();
                    var response = new Cerbos.Api.V1.Response.CheckResourcesResponse { RequestId = sent.RequestId };
                    foreach (var resource in sent.Resources)
                    {
                        var result = new Cerbos.Api.V1.Response.CheckResourcesResponse.Types.ResultEntry
                        {
                            Resource = new() { Kind = resource.Resource.Kind, Id = resource.Resource.Id }
                        };
                        foreach (string action in resource.Actions)
                        {
                            bool moderation = resource.Resource.Kind == ResourceKinds.Event
                                && action == AuthorizationActions.Events.ModerateHeavy;
                            if (moderation)
                                Checks.Enqueue(new AuthorizationRequest(ResourceKinds.Event, resource.Resource.Id, action,
                                    Facts: new EventScopedAuthorizationFacts(
                                        Guid.Parse(resource.Resource.Attr["tenantId"].StringValue),
                                        Guid.Parse(resource.Resource.Attr["eventId"].StringValue))));
                            bool allowed = !moderation || sent.Principal.Attr.TryGetValue("isInstanceAdmin", out var admin)
                                && admin.BoolValue;
                            result.Actions.Add(action, allowed ? Effect.Allow : Effect.Deny);
                        }
                        response.Results.Add(result);
                    }
                    return new Cerbos.Sdk.Response.CheckResourcesResponse(response);
                });
        }
    }
}
