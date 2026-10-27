using System.Security.Cryptography;
using Explore.API.Extensions;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Explore.ServiceDefaults.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>Authenticates the graph fixture's organizer through the production Local issuer and validator.</summary>
internal static class NativeDraftGraphAuthentication
{
    internal static IConfigurationRoot CreateConfiguration()
    {
        // Fresh observer contexts have a synchronous construction contract. Keep the
        // async acquisition scoped to this boundary, never to a configuration provider.
        return CreateAsync().GetAwaiter().GetResult();

        static async Task<IConfigurationRoot> CreateAsync()
        {
            await using var owner = await OpenSecretAuthorityAsync();
            return await CreateConfigurationAsync(owner);
        }
    }

    internal static ValueTask<SecretAuthorityAcquisition> OpenSecretAuthorityAsync()
    {
        var bootstrap = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        return SecretAuthorityBootstrap.OpenAsync(
            bootstrap, Environments.Production, SecretConsumerRole.Api,
            [new("/runtime/api/authentication/keycloak", "KEYCLOAK_ENDPOINT", "KEYCLOAK_ENDPOINT")],
            new SecretAuthorityLoginAttempt());
    }

    internal static async Task<IConfigurationRoot> CreateConfigurationAsync(
        SecretAuthorityAcquisition owner)
    {
        var builder = new ConfigurationBuilder().AddEnvironmentVariables();
        await builder.AddPreparedApiConfigurationAsync(owner);
        builder.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Provider"] = "local",
            ["Authorization:Provider"] = "local",
            ["IdentityDatabase:Topology"] = "colocated"
        });
        var configuration = builder.Build();
        if (string.IsNullOrWhiteSpace(configuration["Authentication:Local:JwtKey"])
            || string.IsNullOrWhiteSpace(configuration["AUTHENTICATION_LOCAL_JWT_KEY"]))
            throw new InvalidOperationException("The native graph lane requires its explicit Local JWT environment authority.");
        return configuration;
    }

    internal static Task PrepareDatabaseAsync()
    {
        var configuration = CreateConfiguration();
        return PrimaryDatabaseProviderBehaviorFixture.Create(
            PrimaryDatabaseConfiguration.BindMigrator(configuration)).PrepareAsync();
    }

    internal static ExploreDbContext CreateRuntimeContext(ITenantContext tenant) =>
        CreateContext(CreateConfiguration(), tenant, PrimaryDatabaseRole.Runtime);

    internal static ExploreDbContext CreateContext(
        IConfiguration configuration, ITenantContext tenant, PrimaryDatabaseRole role, IInterceptor? interceptor = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        var database = PrimaryDatabaseConfiguration.Bind(configuration, role);
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, database);
        if (database.Provider == PrimaryDatabaseProvider.PostgreSql && role == PrimaryDatabaseRole.Runtime)
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        if (interceptor is not null)
            options.AddInterceptors(interceptor);
        var context = new ExploreDbContext(options.Options) { TenantContext = tenant };
        context.BindTenantLease();
        return context;
    }

    internal static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var environment = new GraphWebEnvironment();
        services.AddSingleton<IWebHostEnvironment>(environment);
        services.AddApiAuthentication(configuration, environment, skipAuthorityWarmup: true);
        services.AddSingleton<IHttpContextAccessor>(new GraphHttpContextAccessor());
    }

    internal static string Email(Guid userId) => $"native-graph-{userId:N}@example.test";

    internal static async Task<LocalCredentialOperationReceipt> CreatePendingAsync(IServiceProvider services, Guid userId)
    {
        if (!services.GetRequiredService<LocalPasswordBlocklistSnapshot>().IsUsable)
            throw new InvalidOperationException("The native graph lane requires its pinned Local password blocklist.");
        var created = await services.GetRequiredService<ILocalCredentialAdministration>().CreateBootstrapPendingAsync(
            new LocalCredentialCreateRequest(Guid.CreateVersion7(), userId, Email(userId), "Native", "Organizer"),
            userId, NewPassword(), CancellationToken.None);
        if (created.Outcome != LocalCredentialCreateOutcome.Created)
            throw new InvalidOperationException($"Graph organizer credential provisioning failed: {created.Outcome}.");
        return created.Receipt!;
    }

    internal static async Task<string> ActivateAndLoginAsync(IServiceProvider services, LocalCredentialOperationReceipt receipt)
    {
        var credentials = services.GetRequiredService<ILocalCredentialAdministration>();
        var pending = await credentials.ReadProvisioningAsync(receipt.OperationId, CancellationToken.None)
            ?? throw new InvalidOperationException("Graph organizer credential provisioning is absent.");
        var activation = await credentials.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(receipt.OperationId, pending.OperationConcurrencyStamp), CancellationToken.None);
        if (activation != LocalCredentialActivationOutcome.Activated)
            throw new InvalidOperationException($"Graph organizer credential activation failed: {activation}.");
        var context = services.GetRequiredService<ExploreDbContext>();
        context.ChangeTracker.Clear();
        var manager = services.GetRequiredService<UserManager<LocalIdentityUser>>();
        var identity = await manager.FindByIdAsync(receipt.LocalSubjectId.ToString("D"))
            ?? throw new InvalidOperationException("Graph organizer Local account is absent.");
        string password = NewPassword();
        DateTimeOffset now = services.GetRequiredService<TimeProvider>().GetUtcNow();
        var replacement = await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            new LocalCredentialReplacementAuthority(
                new LocalCredentialReplacementSubject(receipt.LocalSubjectId, receipt.OperationId, identity.SecurityStamp!),
                now, now.AddMinutes(5)), password), CancellationToken.None);
        if (replacement != LocalCredentialReplacementOutcome.Replaced)
            throw new InvalidOperationException($"Graph organizer credential replacement failed: {replacement}.");

        context.ChangeTracker.Clear();
        var binding = await services.GetRequiredService<ISecretBindingRepository>().GetByKeyAndScopeAsync(
            SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, SecretScope.Instance, null);
        if (binding is null)
        {
            context.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, SecretScope.Instance, null,
                "AUTHENTICATION_LOCAL_JWT_KEY"));
            await context.SaveChangesAsync();
        }
        else if (binding.SourceType != SecretSourceType.EnvironmentVariable
            || binding.EnvironmentVariableName != "AUTHENTICATION_LOCAL_JWT_KEY")
            throw new InvalidOperationException("The graph lane cannot replace an existing Local JWT secret authority.");
        context.ChangeTracker.Clear();

        var login = await services.GetRequiredService<ILocalIdentityAuthService>()
            .AuthenticateAsync(new LocalAuthRequestDto(Email(receipt.LocalSubjectId), password), CancellationToken.None);
        if (login.Outcome != LocalAuthOutcome.Authenticated || string.IsNullOrWhiteSpace(login.Token))
            throw new InvalidOperationException("The production Local issuer rejected the graph organizer's credential.");
        return login.Token;
    }

    internal static async Task AuthenticateRequestAsync(IServiceProvider services, string token, Guid userId)
    {
        var request = services.GetRequiredService<IHttpContextAccessor>().HttpContext
            ?? throw new InvalidOperationException("The graph request owner is absent.");
        request.RequestServices = services;
        request.Request.Headers.Authorization = "Bearer " + token;
        var result = await request.AuthenticateAsync();
        if (!result.Succeeded)
            throw new InvalidOperationException("The production Local validator rejected the graph organizer's session.");
        request.User = result.Principal!;
        var lifetime = await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.User, CancellationToken.None);
        if (lifetime?.LocalSession?.LocalSubjectId != userId)
            throw new InvalidOperationException("The validated graph request lost its original organizer session.");
        var human = services.GetRequiredService<IHumanCommitScopePreparation>();
        await human.PrepareAsync(CancellationToken.None);
        if (human.Subject?.PrincipalId != userId)
            throw new InvalidOperationException("The graph request resolved another organizer.");
    }

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    private sealed class GraphHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }

    private sealed class GraphWebEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = typeof(Program).Assembly.GetName().Name!;
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
