// ABOUTME: Hosts production Local, OIDC, and ATProto authentication with real account persistence over isolated SQLite.
// ABOUTME: Separates ephemeral Local environment authority from external issuer public metadata and signing material.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.API.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Atproto.Models;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Seed;
using Explore.Secrets.Database;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.Api.IntegrationTests.Fixtures;

internal sealed class LocalAdmissionWebApplicationFactory : CustomWebApplicationFactory
{
    private const string SigningKeyVariable = "AUTHENTICATION_LOCAL_JWT_KEY";
    private const string ExternalAudience = "islamu-event-api";
    private readonly AuthenticationProviderKind _primaryProvider;
    private readonly RSA? _externalSigningKey;
    private readonly ECDsa? _atprotoOAuthKey;
    private readonly ECDsa? _atprotoSessionKey;
    private readonly string _atprotoOAuthKeyId = Guid.CreateVersion7().ToString("N");
    private readonly string _atprotoSessionKeyId = Guid.CreateVersion7().ToString("N");
    private readonly string _externalIssuer = $"https://issuer-{Guid.CreateVersion7():N}.example.test/realms/native";
    private readonly string _externalKeyId = Guid.CreateVersion7().ToString("N");
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"local-admission-{Guid.CreateVersion7():N}.db");
    private readonly string _identityDatabasePath = Path.Combine(
        Path.GetTempPath(), $"local-admission-identity-{Guid.CreateVersion7():N}.db");
    private readonly Dictionary<string, string?> _previousEnvironment = [];
    private string? _connectionString;
    private IInterceptor? _persistenceInterceptor;
    private ILoggerProvider? _logCapture;
    private IdentityDatabaseTopology _identityTopology = IdentityDatabaseTopology.Colocated;
    private string? _identityConnectionString;
    private bool _incompleteSetup;
    private bool _enableRateLimiting;

    public string SetupSecret { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private LocalAdmissionWebApplicationFactory(AuthenticationProviderKind primaryProvider, bool enableAtproto)
    {
        if (primaryProvider is not (AuthenticationProviderKind.Local or AuthenticationProviderKind.Keycloak
            or AuthenticationProviderKind.Atproto))
        {
            throw new ArgumentOutOfRangeException(nameof(primaryProvider));
        }

        _primaryProvider = primaryProvider;
        _externalSigningKey = primaryProvider == AuthenticationProviderKind.Keycloak ? RSA.Create(2048) : null;
        if (primaryProvider == AuthenticationProviderKind.Atproto || enableAtproto)
        {
            _atprotoOAuthKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            _atprotoSessionKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        }
        SetEnvironment("SECRET_PROVIDER", "Environment");
        SetEnvironment("SecretProvider__Provider", "Environment");
        SetEnvironment("Authentication__Local__JwtKey", null);
        SetEnvironment(SigningKeyVariable, Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)));
    }

    public static async Task<LocalAdmissionWebApplicationFactory> CreateAsync(
        AuthenticationProviderKind primaryProvider = AuthenticationProviderKind.Local,
        IInterceptor? persistenceInterceptor = null,
        ILoggerProvider? logCapture = null,
        IdentityDatabaseTopology identityTopology = IdentityDatabaseTopology.Colocated,
        bool incompleteSetup = false,
        bool enableRateLimiting = false,
        bool enableAtproto = false)
    {
        var factory = new LocalAdmissionWebApplicationFactory(primaryProvider, enableAtproto)
        {
            _persistenceInterceptor = persistenceInterceptor,
            _logCapture = logCapture,
            _identityTopology = identityTopology,
            _incompleteSetup = incompleteSetup,
            _enableRateLimiting = enableRateLimiting
        };
        try
        {
            if (incompleteSetup)
                factory.SetEnvironment("SETUP_SECRET", factory.SetupSecret);
            await factory.SeedDatabaseAsync();
            if (identityTopology == IdentityDatabaseTopology.External)
            {
                await using DbContext identity = factory.CreateIdentityDatabase();
                await identity.Database.EnsureCreatedAsync();
            }
            return factory;
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        if (_logCapture is not null)
            builder.ConfigureLogging(logging => logging.AddProvider(_logCapture));
        string primaryProvider = _primaryProvider.ToString().ToLowerInvariant();
        builder.UseSetting("Authentication:Provider", primaryProvider);
        builder.UseSetting("SecretProvider:Provider", "Environment");
        builder.UseSetting("IdentityDatabase:Topology", _identityTopology.ToString().ToLowerInvariant());
        builder.UseSetting("IdentityDatabase:Provider", "Sqlite");
        builder.UseSetting("IdentityDatabase:Name", _identityDatabasePath);
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = primaryProvider,
                ["IdentityDatabase:Topology"] = _identityTopology.ToString().ToLowerInvariant(),
                ["IdentityDatabase:Provider"] = "Sqlite",
                ["IdentityDatabase:Name"] = _identityDatabasePath,
                ["SecretProvider:Provider"] = "Environment",
                ["Database:Provider"] = "Sqlite",
                ["Database:Database"] = _databasePath,
                ["Database:Runtime:Database"] = _databasePath,
                ["SETUP_SECRET_FILE"] = _databasePath + ".setup",
                ["OutboxProcessor:Enabled"] = "false",
                ["EmailDispatchProcessor:Enabled"] = "false",
                ["Testing:SkipJwtAuthorityWarmup"] = "true",
                ["RateLimiting:DisableInTesting"] = _enableRateLimiting ? "false" : "true"
            };
            if (_primaryProvider == AuthenticationProviderKind.Keycloak)
            {
                settings["Keycloak:Authority"] = _externalIssuer;
                settings["Keycloak:MetadataAddress"] = _externalIssuer + "/.well-known/openid-configuration";
                settings["Keycloak:Audience"] = ExternalAudience;
            }
            else if (_primaryProvider == AuthenticationProviderKind.Atproto)
            {
                settings["Authentication:AtprotoLoginEnabled"] = "true";
            }
            else if (_incompleteSetup)
            {
                settings["Keycloak:Authority"] = null;
                settings["Keycloak:MetadataAddress"] = null;
                settings["PublicBaseUrl"] = "https://example.test";
            }

            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureTestServices(services =>
        {
            services.RemoveExploreDbContextRegistrations();
            services.AddDbContextFactory<ExploreDbContext>(ConfigureDatabase);
            services.AddScoped(provider =>
            {
                var context = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>()
                    .CreateDbContext();
                context.TenantContext = provider.GetService<ITenantContext>();
                context.CurrentUserService = provider.GetService<ICurrentUserService>();
                context.ClearTenantFilterBypass();
                return context;
            });
            if (_externalSigningKey is not null)
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    var metadata = new OpenIdConnectConfiguration { Issuer = _externalIssuer };
                    metadata.SigningKeys.Add(new RsaSecurityKey(_externalSigningKey.ExportParameters(false))
                    {
                        KeyId = _externalKeyId
                    });
                    options.Configuration = metadata;
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
                });
            }
            if (_atprotoOAuthKey is not null)
            {
                ConfigureAtprotoAuthorities(services);
            }
        });
    }

    public string ExternalIssuer => _externalIssuer;
    public string AtprotoOAuthKeyId => _atprotoOAuthKeyId;

    public string CreateAtprotoBootstrapAssertion(AtprotoDid did, AtprotoSubjectClassification classification,
        Guid? tenantId = null, string? issuer = null)
    {
        if (_atprotoOAuthKey is null)
        {
            throw new InvalidOperationException("The ATProto authority was not selected for this host.");
        }

        DateTime now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? AtprotoJwtOptions.BootstrapIssuer,
            Audience = AtprotoJwtOptions.BootstrapAudience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, "event-blazor-bff"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("D")),
                new Claim(AtprotoJwtOptions.TenantClaim, (tenantId ?? PlatformDefaults.DefaultTenantId).ToString("D")),
                new Claim(AtprotoJwtOptions.DidClaim, did.Value),
                new Claim(AtprotoJwtOptions.ClassificationClaim, classification.ToString().ToLowerInvariant()),
                new Claim(AtprotoJwtOptions.MethodClaim, "POST"),
                new Claim(AtprotoJwtOptions.PathClaim, AtprotoJwtOptions.BridgePath)
            ]),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(1),
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(_atprotoOAuthKey) { KeyId = _atprotoOAuthKeyId },
                SecurityAlgorithms.EcdsaSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    private void ConfigureAtprotoAuthorities(IServiceCollection services)
    {
        var rings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SecretDefinitionRegistry.Keys.Atproto.OAuthClientPrivateJwks] =
                CreatePrivateKeyRing(_atprotoOAuthKey!, _atprotoOAuthKeyId),
            [SecretDefinitionRegistry.Keys.Atproto.SessionJwtPrivateJwks] =
                CreatePrivateKeyRing(_atprotoSessionKey!, _atprotoSessionKeyId)
        };
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveAsync(Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<Guid?>(1) is null
                && rings.TryGetValue(call.ArgAt<string>(0), out string? value)
                    ? SecretResolutionResult.Resolved(new ResolvedSecret(
                        SettingKey: call.ArgAt<string>(0),
                        Value: value,
                        Source: SecretSourceType.EnvironmentVariable,
                        Scope: SecretScope.Instance,
                        ScopeId: null,
                        ResolvedAt: DateTimeOffset.UtcNow))
                    : SecretResolutionResult.Unconfigured);
        services.RemoveAll<ISecretResolver>();
        services.AddSingleton(secrets);

        var gateway = Substitute.For<IAtprotoOAuthSecurityGateway>();
        gateway.VerifyAsync(Arg.Any<AtprotoOAuthVerificationInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var input = call.ArgAt<AtprotoOAuthVerificationInput>(0);
                return AtprotoOAuthVerificationResult.Verified(new AtprotoVerifiedOAuthSession(
                    Did: input.ExpectedDid,
                    Handle: "verified.example.test",
                    PdsUri: input.ExpectedPdsUri,
                    OAuthClientKeyId: input.OAuthClientKeyId,
                    OAuthSessionPayload: input.OAuthSessionPayload));
            });
        gateway.PreparePersistenceAsync(Arg.Any<AtprotoVerifiedOAuthSession>(), Arg.Any<Guid>(),
                Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var verified = call.ArgAt<AtprotoVerifiedOAuthSession>(0);
                return new AtprotoPreparedOAuthSession(
                    SessionCiphertext: RandomNumberGenerator.GetBytes(64),
                    EncryptionKeyId: Guid.CreateVersion7().ToString("N"),
                    EnvelopeVersion: 1,
                    TenantId: call.ArgAt<Guid>(1),
                    UserId: call.ArgAt<Guid>(2),
                    SubjectDid: verified.Did,
                    PdsHost: verified.PdsUri.AbsoluteUri,
                    OAuthClientKeyId: verified.OAuthClientKeyId,
                    ExpiresAt: DateTime.UtcNow.AddMinutes(15));
            });
        gateway.PersistPreparedAsync(Arg.Any<AtprotoPreparedOAuthSession>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        services.RemoveAll<IAtprotoOAuthSecurityGateway>();
        services.AddSingleton(gateway);
    }

    private static string CreatePrivateKeyRing(ECDsa key, string keyId)
    {
        ECParameters parameters = key.ExportParameters(includePrivateParameters: true);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "EC", crv = "P-256", kid = keyId, use = "sig", alg = "ES256", status = "active",
                    x = Base64UrlEncoder.Encode(parameters.Q.X!),
                    y = Base64UrlEncoder.Encode(parameters.Q.Y!),
                    d = Base64UrlEncoder.Encode(parameters.D!)
                }
            }
        });
    }

    public string CreateExternalProviderToken(Guid subject, string email, bool? emailVerified)
    {
        if (_externalSigningKey is null)
        {
            throw new InvalidOperationException("The external OIDC authority was not selected for this host.");
        }

        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, subject.ToString("D")),
            new("email", email),
            new("given_name", "External"),
            new("family_name", "Member"),
            new("preferred_username", "external-member"),
            new("auth_provider", "keycloak")
        ];
        if (emailVerified.HasValue)
        {
            claims.Add(new Claim("email_verified", emailVerified.Value ? "true" : "false", ClaimValueTypes.Boolean));
        }

        DateTime now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _externalIssuer,
            Audience = ExternalAudience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_externalSigningKey) { KeyId = _externalKeyId },
                SecurityAlgorithms.RsaSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    public ExploreDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        ConfigureDatabase(options);
        return new ExploreDbContext(options.Options);
    }

    public DbContext CreateIdentityDatabase()
    {
        if (_identityTopology == IdentityDatabaseTopology.Colocated)
            return CreateDatabase();
        using IServiceScope scope = Services.CreateScope();
        DbContextOptions<ExternalIdentityDbContext> options = scope.ServiceProvider
            .GetRequiredService<DbContextOptions<ExternalIdentityDbContext>>();
        var context = new ExternalIdentityDbContext(options);
        _identityConnectionString = context.Database.GetConnectionString();
        return context;
    }

    public async Task<LocalAuthRequestDto> SeedLocalUserAsync(bool emailConfirmed)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        var credentials = scope.ServiceProvider.GetRequiredService<ILocalCredentialAdministration>();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid initiatorId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        string email = $"local-{Guid.CreateVersion7():N}@example.test";
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        LocalCredentialCreateResult created = await credentials.CreatePendingAsync(new LocalCredentialCreateRequest(
            operationId: Guid.CreateVersion7(), initiatingApplicationUserId: initiatorId,
            email: email, firstName: "Local", lastName: "Member"), CancellationToken.None);
        if (created.Outcome != LocalCredentialCreateOutcome.Created)
        {
            throw new InvalidOperationException("The native Identity fixture could not create its account.");
        }
        LocalCredentialOperationReceipt receipt = created.Receipt!;
        DateTime createdAt = DateTime.UtcNow;
        var applicationUser = new User
        {
            Id = receipt.LocalSubjectId,
            Pii = new UserPii { Email = email, FirstName = "Local", LastName = "Member" },
            EmailVerified = true,
            CreatedAt = createdAt
        };
        database.AddRange(applicationUser, new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = applicationUser.Id,
            User = applicationUser,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Local Member" },
            CreatedAt = createdAt
        }, new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = applicationUser.Id,
            User = applicationUser,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = applicationUser.Id.ToString("D"),
            CreatedAt = createdAt
        });
        await database.SaveChangesAsync();

        LocalCredentialProvisioningSnapshot pending = (await credentials.ReadProvisioningAsync(
            receipt.OperationId, CancellationToken.None))!;
        LocalCredentialActivationOutcome activated = await credentials.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: receipt.OperationId,
                expectedOperationConcurrencyStamp: pending.OperationConcurrencyStamp), CancellationToken.None);
        if (activated != LocalCredentialActivationOutcome.Activated)
        {
            throw new InvalidOperationException("The native Identity fixture could not activate its exact account binding.");
        }
        LocalIdentityUser user = (await manager.FindByIdAsync(receipt.LocalSubjectId.ToString("D")))!;
        DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
        LocalCredentialReplacementOutcome replaced = await credentials.ReplaceAsync(new LocalCredentialReplacementRequest(
            authority: new LocalCredentialReplacementAuthority(
                subject: new LocalCredentialReplacementSubject(localSubjectId: user.Id,
                    operationId: receipt.OperationId, securityStamp: user.SecurityStamp!),
                issuedAtUtc: issuedAt, expiresAtUtc: issuedAt.AddMinutes(5)),
            newPassword: password), CancellationToken.None);
        if (replaced != LocalCredentialReplacementOutcome.Replaced)
        {
            throw new InvalidOperationException("The native Identity fixture could not establish ready credentials.");
        }
        DbContext identityDatabase = _identityTopology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : database;
        await identityDatabase.Entry(user).ReloadAsync();
        user.EmailConfirmed = emailConfirmed;
        if (!(await manager.UpdateAsync(user)).Succeeded)
        {
            throw new InvalidOperationException("The native Identity fixture could not restore its verification state.");
        }
        applicationUser = await database.Users.SingleAsync(row => row.Id == receipt.LocalSubjectId);
        applicationUser.EmailVerified = emailConfirmed;
        await database.SaveChangesAsync();

        return new LocalAuthRequestDto(Identifier: email, Password: password);
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync();
        }
        finally
        {
            _externalSigningKey?.Dispose();
            _atprotoOAuthKey?.Dispose();
            _atprotoSessionKey?.Dispose();
            foreach ((string name, string? value) in _previousEnvironment)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            if (_connectionString is not null)
            {
                using var connection = new SqliteConnection(_connectionString);
                SqliteConnection.ClearPool(connection);
            }
            if (_identityConnectionString is not null)
            {
                using var connection = new SqliteConnection(_identityConnectionString);
                SqliteConnection.ClearPool(connection);
            }

            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
            File.Delete(_databasePath + ".setup");
            if (_identityTopology == IdentityDatabaseTopology.External)
            {
                File.Delete(_identityDatabasePath);
                File.Delete(_identityDatabasePath + "-wal");
                File.Delete(_identityDatabasePath + "-shm");
            }
        }
    }

    private void ConfigureDatabase(DbContextOptionsBuilder options)
    {
        PrimaryDatabaseConnectionResult database = PrimaryDatabaseProviderComposition.ConfigureApplication(
            optionsBuilder: options,
            options: new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Runtime,
                Provider = PrimaryDatabaseProvider.Sqlite,
                Database = _databasePath
            });
        _connectionString = database.ConnectionString;
        options.UseSnakeCaseNamingConvention();
        if (_persistenceInterceptor is not null)
            options.AddInterceptors(_persistenceInterceptor);
    }

    private async Task SeedDatabaseAsync()
    {
        await using ExploreDbContext database = CreateDatabase();
        await database.Database.EnsureCreatedAsync();
        await SqliteDatabaseInitializer.InitializeAsync(database, CancellationToken.None);
        await LookupTableSeeder.SeedAsync(database);
        DateTime now = DateTime.UtcNow;
        var bootstrapUser = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"bootstrap-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Bootstrap",
                LastName = "Operator"
            },
            EmailVerified = true,
            CreatedAt = now
        };
        database.Users.Add(bootstrapUser);
        database.Tenants.Add(new Tenant
        {
            Id = PlatformDefaults.DefaultTenantId,
            FullName = "Local admission instance",
            Slug = "local-admission",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = await database.Set<TenantStatus>().SingleAsync(
                status => status.Id == (int)TenantStatusEnum.Active),
            CreatedAt = now
        });
        var bootstrap = InstanceBootstrapState.CreateInteractivePending(
            id: Guid.CreateVersion7(),
            deploymentMode: DeploymentMode.SingleTenant,
            createdAt: now);
        if (!_incompleteSetup)
        {
            bootstrap.CompleteInteractive(completedByUserId: bootstrapUser.Id, completedAt: now);
            database.InstanceBootstrapStates.Add(bootstrap);
        }
        database.SystemSettings.Add(new SystemSetting
        {
            Id = Guid.CreateVersion7(),
            SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
            Value = "true",
            ValueType = SettingValueType.Boolean,
            Category = "Email",
            CreatedAt = now
        });
        database.Set<SecretBinding>().Add(SecretBinding.CreateEnvironmentVariable(
            settingKey: SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey,
            scope: SecretScope.Instance,
            scopeId: null,
            variableName: SigningKeyVariable));
        if (_atprotoOAuthKey is not null)
        {
            database.SystemSettings.AddRange(new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Authentication.AtprotoLoginEnabled,
                Value = "true",
                ValueType = SettingValueType.Boolean,
                Category = "Authentication",
                CreatedAt = now
            }, new SystemSetting
            {
                SettingKey = GovernanceSettingKeys.Authentication.AtprotoPublicUrl,
                Value = JsonSerializer.Serialize("https://localhost"),
                ValueType = SettingValueType.String,
                Category = "Authentication",
                CreatedAt = now
            });
        }
        await database.SaveChangesAsync();
    }

    private void SetEnvironment(string name, string? value)
    {
        _previousEnvironment.Add(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }
}
