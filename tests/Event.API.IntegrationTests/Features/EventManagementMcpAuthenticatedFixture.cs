using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.API.Configuration;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ApiIntegrationTests.Features;

/// <summary>
/// Issues a genuine Local session using setup authority, then switches both REST and
/// MCP requests to the enrolled Runtime login without granting private table access.
/// </summary>
internal sealed class EventManagementMcpAuthenticatedFixture : IAsyncDisposable
{
    private readonly PostgreSqlContainerFixture postgres;
    private readonly TenantBrandingNativeAuthenticationHost host;
    private readonly string slug;
    internal Guid TenantId { get; } = Guid.CreateVersion7();
    internal Guid UserId { get; private set; }
    internal HttpClient Client { get; }

    private EventManagementMcpAuthenticatedFixture(PostgreSqlContainerFixture postgres)
    {
        this.postgres = postgres;
        slug = $"event-management-{TenantId:N}";
        host = new TenantBrandingNativeAuthenticationHost(postgres, TenantId, [],
            configureServices: services => services.PostConfigure<McpAdapterSettings>(settings =>
            {
                settings.Enabled = true;
                settings.EndpointPath = "/mcp";
                settings.Stateless = true;
                settings.EnableLegacySse = false;
            }));
        Client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    internal static async Task<EventManagementMcpAuthenticatedFixture> CreateAsync(
        PostgreSqlContainerFixture postgres)
    {
        var fixture = new EventManagementMcpAuthenticatedFixture(postgres);
        try
        {
            await fixture.InitializeAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal ExploreDbContext CreateMigratorContext()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>()
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.ManyServiceProvidersCreatedWarning));
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        var context = new ExploreDbContext(options.Options);
        context.EnableTenantFilterBypass("Event management fixture setup uses the declared Migrator login.");
        return context;
    }

    private async Task InitializeAsync()
    {
        await host.EnsurePrivacyAuthorityReadyAsync();
        await using (var setup = CreateMigratorContext())
        {
            setup.Tenants.Add(new Tenant
            {
                Id = TenantId,
                FullName = "Native event management",
                Slug = slug,
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await setup.SaveChangesAsync();
        }
        await using var scope = host.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().BindExecutionTenant(TenantId);
        await using var database = CreateMigratorContext();
        database.TenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        database.ClearTenantFilterBypass();
        database.BindTenantLease();
        scope.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(database, TenantId);
        var selected = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(selected).IsSameReferenceAs(database);
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
        string email = $"event-management-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        var created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            Guid.CreateVersion7(), initiator.Id, email, "MCP", "Event owner"), CancellationToken.None);
        await Assert.That(created.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var receipt = created.Receipt!;
        var user = new User
        {
            Id = receipt.LocalSubjectId,
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
            Pii = new UserPii { Email = email, FirstName = "MCP", LastName = "Event owner" }
        };
        database.AddRange(user, new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = user.Id,
            User = user,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "MCP Event owner" },
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
        var identity = (await manager.FindByIdAsync(user.Id.ToString("D")))!;
        await database.Entry(identity).ReloadAsync();
        var issuedAt = host.Services.GetRequiredService<TimeProvider>().GetUtcNow();
        await Assert.That(await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            new LocalCredentialReplacementAuthority(
                new LocalCredentialReplacementSubject(identity.Id, receipt.OperationId, identity.SecurityStamp!),
                issuedAt, issuedAt.AddMinutes(5)), password), CancellationToken.None))
            .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await database.Entry(identity).ReloadAsync();
        identity.EmailConfirmed = true;
        await Assert.That((await manager.UpdateAsync(identity)).Succeeded).IsTrue();
        var binding = await credentials.ReadLinkedIdentityAsync(user.Id, CancellationToken.None);
        await Assert.That(binding!.CredentialState).IsEqualTo(LocalCredentialState.Ready);
        database.TenantUsers.Add(new TenantUser
        {
            Id = Guid.CreateVersion7(),
            TenantId = TenantId,
            Tenant = null!,
            UserId = user.Id,
            User = user,
            ActorId = receipt.PersonalActorId,
            Actor = null!,
            StatusId = (int)TenantUserStatusEnum.Active,
            JoinedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        await database.SaveChangesAsync();
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/local/login");
        login.Headers.Add("X-Tenant-Slug", slug);
        login.Content = JsonContent.Create(new LocalAuthRequestDto(email, password));
        using var response = await Client.SendAsync(login);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK)
            .Because(host.RequestFailure?.GetType().Name ?? "The genuine Local login must succeed.");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(body.RootElement.GetProperty("success").GetBoolean()).IsTrue();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.RootElement.GetProperty("token").GetString()!);
        Client.DefaultRequestHeaders.Add("X-Tenant-Slug", slug);
        UserId = user.Id;
        host.UseRuntimeRequestContexts();
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await host.DisposeAsync();
    }
}
