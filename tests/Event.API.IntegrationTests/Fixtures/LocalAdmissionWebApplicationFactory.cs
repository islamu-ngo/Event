// ABOUTME: Hosts production Local, OIDC, and ATProto authentication with real account persistence over isolated SQLite.
// ABOUTME: Separates ephemeral Local environment authority from external issuer public metadata and signing material.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.API.Authentication;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    private readonly Dictionary<string, string?> _previousEnvironment = [];
    private string? _connectionString;

    private LocalAdmissionWebApplicationFactory(AuthenticationProviderKind primaryProvider)
    {
        if (primaryProvider is not (AuthenticationProviderKind.Local or AuthenticationProviderKind.Keycloak
            or AuthenticationProviderKind.Atproto))
        {
            throw new ArgumentOutOfRangeException(nameof(primaryProvider));
        }

        _primaryProvider = primaryProvider;
        _externalSigningKey = primaryProvider == AuthenticationProviderKind.Keycloak ? RSA.Create(2048) : null;
        if (primaryProvider == AuthenticationProviderKind.Atproto)
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
        AuthenticationProviderKind primaryProvider = AuthenticationProviderKind.Local)
    {
        var factory = new LocalAdmissionWebApplicationFactory(primaryProvider);
        try
        {
            await factory.SeedDatabaseAsync();
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
        string primaryProvider = _primaryProvider.ToString().ToLowerInvariant();
        builder.UseSetting("Authentication:Provider", primaryProvider);
        builder.UseSetting("SecretProvider:Provider", "Environment");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Authentication:Provider"] = primaryProvider,
                ["IdentityDatabase:Topology"] = "colocated",
                ["SecretProvider:Provider"] = "Environment",
                ["Database:Provider"] = "Sqlite",
                ["Database:Database"] = _databasePath,
                ["Database:Runtime:Database"] = _databasePath,
                ["SETUP_SECRET_FILE"] = _databasePath + ".setup",
                ["OutboxProcessor:Enabled"] = "false",
                ["EmailDispatchProcessor:Enabled"] = "false",
                ["Testing:SkipJwtAuthorityWarmup"] = "true"
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
            if (_primaryProvider == AuthenticationProviderKind.Atproto)
            {
                ConfigureAtprotoAuthorities(services);
            }
        });
    }

    public string ExternalIssuer => _externalIssuer;
    public string AtprotoOAuthKeyId => _atprotoOAuthKeyId;

    public string CreateAtprotoBootstrapAssertion(AtprotoDid did, AtprotoSubjectClassification classification)
    {
        if (_atprotoOAuthKey is null)
        {
            throw new InvalidOperationException("The ATProto authority was not selected for this host.");
        }

        DateTime now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = AtprotoJwtOptions.BootstrapIssuer,
            Audience = AtprotoJwtOptions.BootstrapAudience,
            Subject = new ClaimsIdentity([
                new Claim(JwtRegisteredClaimNames.Sub, "event-blazor-bff"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString("D")),
                new Claim(AtprotoJwtOptions.TenantClaim, PlatformDefaults.DefaultTenantId.ToString("D")),
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

    public async Task<LocalAuthRequestDto> SeedLocalUserAsync(bool emailConfirmed)
    {
        await using AsyncServiceScope scope = Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
        string email = $"local-{Guid.CreateVersion7():N}@example.test";
        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var user = new LocalIdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed,
            FirstName = "Local",
            LastName = "Member"
        };
        IdentityResult result = await manager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("The native Identity fixture could not create its account.");
        }

        return new LocalAuthRequestDto(Email: email, Password: password);
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

            File.Delete(_databasePath);
            File.Delete(_databasePath + "-wal");
            File.Delete(_databasePath + "-shm");
            File.Delete(_databasePath + ".setup");
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
        bootstrap.CompleteInteractive(completedByUserId: bootstrapUser.Id, completedAt: now);
        database.InstanceBootstrapStates.Add(bootstrap);
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
        await database.SaveChangesAsync();
    }

    private void SetEnvironment(string name, string? value)
    {
        _previousEnvironment.Add(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }
}
