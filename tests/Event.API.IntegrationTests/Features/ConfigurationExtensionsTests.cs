using Explore.API.Extensions;
using Explore.Infrastructure.Services;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using System.Reflection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class ConfigurationExtensionsTests
{
    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsCerbosUsePolicyScopeFromAuthoritativeKey()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CERBOS_USE_POLICY_SCOPE"] = "false"
        });

        await Assert.That(configuration["Cerbos:UsePolicyScope"]).IsEqualTo("false");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsKeycloakClientIdsForApiProviderManagement()
    {
        var canonical = BuildConfiguration(new Dictionary<string, string?>
        {
            ["KEYCLOAK_CLIENT_ID"] = "canonical-blazor"
        });
        var composeAlias = BuildConfiguration(new Dictionary<string, string?>
        {
            ["KEYCLOAK_BLAZOR_CLIENT_ID"] = "event-blazor",
            ["KEYCLOAK_BLAZOR_CLIENT_SECRET"] = "server-only-secret"
        });

        await Assert.That(canonical["Keycloak:ClientId"]).IsEqualTo("canonical-blazor");
        await Assert.That(composeAlias["Keycloak:ClientId"]).IsEqualTo("event-blazor");
        await Assert.That(composeAlias["Keycloak:ClientSecret"]).IsEqualTo("server-only-secret");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_DoesNotOverrideCanonicalCerbosUsePolicyScope()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CERBOS_USE_POLICY_SCOPE"] = "false",
            ["Cerbos:UsePolicyScope"] = "true"
        });

        await Assert.That(configuration["Cerbos:UsePolicyScope"]).IsEqualTo("true");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsCerbosHttpEndpointToAdminApiEndpoint()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CERBOS_HTTP_ENDPOINT"] = "cerbosapi.openislamu.org:443"
        });

        await Assert.That(configuration["Cerbos:HttpEndpoint"]).IsEqualTo("cerbosapi.openislamu.org:443");
        await Assert.That(configuration["Cerbos:AdminApi:Endpoints:0"]).IsEqualTo("cerbosapi.openislamu.org:443");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsCerbosTlsWithoutCredentialAliases()
    {
        var username = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["CERBOS_USE_TLS"] = "true",
            ["CERBOS_PLAINTEXT_MODE"] = "false",
            ["CERBOS_ADMIN_USERNAME"] = username,
            ["CERBOS_ADMIN_PASSWORD"] = password
        });

        await Assert.That(configuration["Cerbos:UseTls"]).IsEqualTo("true");
        await Assert.That(configuration["Cerbos:PlaintextMode"]).IsEqualTo("false");
        await Assert.That(configuration["Cerbos:AdminApi:AdminUsername"]).IsNull();
        await Assert.That(configuration["Cerbos:AdminApi:AdminPassword"]).IsNull();
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsAuthorizationProviderIntentWithoutOverridingCanonicalValue()
    {
        var mapped = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AUTHORIZATION_PROVIDER"] = "cerbos"
        });
        var canonical = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AUTHORIZATION_PROVIDER"] = "cerbos",
            ["Authorization:Provider"] = "local"
        });

        await Assert.That(mapped["Authorization:Provider"]).IsEqualTo("cerbos");
        await Assert.That(canonical["Authorization:Provider"]).IsEqualTo("local");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsTwoAxisLocalIdentityConfigurationWithoutOverridingCanonicalValues()
    {
        var mapped = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AUTHENTICATION_PROVIDER"] = "local",
            ["ATPROTO_LOGIN_ENABLED"] = "true",
            ["AUTHENTICATION_LOCAL_JWT_KEY"] = "deployment-secret",
            ["AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD"] = "7",
            ["AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES"] = "20",
            ["IDENTITY_DATABASE_TOPOLOGY"] = "external",
            ["IDENTITY_DATABASE_PROVIDER"] = "PostgreSql",
            ["IDENTITY_DATABASE_HOST"] = "identity-db",
            ["IDENTITY_DATABASE_RUNTIME_PASSWORD"] = "runtime-secret",
        });
        var canonical = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AUTHENTICATION_PROVIDER"] = "local",
            ["ATPROTO_LOGIN_ENABLED"] = "true",
            ["Authentication:Provider"] = "keycloak",
            ["Authentication:AtprotoLoginEnabled"] = "false",
        });

        await Assert.That(mapped["Authentication:Provider"]).IsEqualTo("local");
        await Assert.That(mapped["Authentication:AtprotoLoginEnabled"]).IsEqualTo("true");
        await Assert.That(mapped["Authentication:Local:JwtKey"]).IsEqualTo("deployment-secret");
        await Assert.That(mapped["Authentication:Local:LockoutThreshold"]).IsEqualTo("7");
        await Assert.That(mapped["Authentication:Local:LockoutDurationMinutes"]).IsEqualTo("20");
        await Assert.That(mapped["IdentityDatabase:Topology"]).IsEqualTo("external");
        await Assert.That(mapped["IdentityDatabase:Provider"]).IsEqualTo("PostgreSql");
        await Assert.That(mapped["IdentityDatabase:Host"]).IsEqualTo("identity-db");
        await Assert.That(mapped["IdentityDatabase:Runtime:Password"]).IsEqualTo("runtime-secret");
        await Assert.That(canonical["Authentication:Provider"]).IsEqualTo("keycloak");
        await Assert.That(canonical["Authentication:AtprotoLoginEnabled"]).IsEqualTo("false");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsDatabaseFolderAgnosticKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_PROVIDER"] = "SqlServer",
            ["DATABASE_HOST"] = "sql.example.test",
            ["DATABASE_PORT"] = "1433",
            ["DATABASE_NAME"] = "event_sql_db",
            ["DATABASE_SCHEMA"] = "event_schema",
            ["DATABASE_RUNTIME_USERNAME"] = "sql_runtime_user",
            ["DATABASE_RUNTIME_PASSWORD"] = "sql-secret",
            ["DATABASE_TLS_MODE"] = "Required",
            ["DATABASE_TRUST_SERVER_CERTIFICATE"] = "true",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(options.Provider).IsEqualTo(PrimaryDatabaseProvider.SqlServer);
        await Assert.That(options.Host).IsEqualTo("sql.example.test");
        await Assert.That(options.Port).IsEqualTo(1433);
        await Assert.That(options.Database).IsEqualTo("event_sql_db");
        await Assert.That(options.Schema).IsEqualTo("event_schema");
        await Assert.That(options.Username).IsEqualTo("sql_runtime_user");
        await Assert.That(options.Password).IsEqualTo("sql-secret");
        await Assert.That(options.TlsMode).IsEqualTo(PrimaryDatabaseTlsMode.Required);
        await Assert.That(options.TrustServerCertificate).IsTrue();
    }

    [Test]
    public async Task ApplyMapping_WhenSelectedAuthorityOmitsDatabaseCredentials_MasksLowerSource()
    {
        string canary = Guid.CreateVersion7().ToString("N");
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "lower-authority.example.test",
            ["Database:Database"] = "event_db",
            ["Database:Runtime:Username"] = "lower_user",
            ["Database:Runtime:Password"] = canary,
        });
        var method = typeof(Explore.API.Extensions.ConfigurationExtensions).GetMethod(
            "ApplyMapping",
            BindingFlags.NonPublic | BindingFlags.Static);

        method!.Invoke(null, [builder, new ConfigurationBuilder().Build()]);
        IConfiguration configuration = builder.Build();
        Action bind = () => PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(configuration["Database:Runtime:Password"]).IsNull();
        var exception = await Assert.That(bind).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message).DoesNotContain(canary);
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_DoesNotOverrideExplicitStructuredDatabaseContract()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_HOST"] = "mapped-host",
            ["DATABASE_NAME"] = "mapped_db",
            ["DATABASE_RUNTIME_USERNAME"] = "mapped_user",
            ["DATABASE_RUNTIME_PASSWORD"] = "mapped-secret",
            ["Database:Provider"] = "PostgreSql",
            ["Database:Host"] = "explicit-host",
            ["Database:Name"] = "explicit_db",
            ["Database:Runtime:Username"] = "explicit_user",
            ["Database:Runtime:Password"] = "explicit-secret",
            ["Database:Runtime:TlsMode"] = "Required",
        });

        var options = PrimaryDatabaseConfiguration.BindRuntime(configuration);

        await Assert.That(options.Host).IsEqualTo("explicit-host");
        await Assert.That(options.Database).IsEqualTo("explicit_db");
        await Assert.That(options.Username).IsEqualTo("explicit_user");
        await Assert.That(options.Password).IsEqualTo("explicit-secret");
        await Assert.That(options.TlsMode).IsEqualTo(PrimaryDatabaseTlsMode.Required);
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsPrivacyErasureAuthorityKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ERASURE_DATABASE_TOPOLOGY"] = "ExternalDatabase",
            ["PRIVACY_ERASURE_AUTHORITY_HOST"] = "authority",
            ["PRIVACY_ERASURE_AUTHORITY_PORT"] = "6543",
            ["PRIVACY_ERASURE_AUTHORITY_DATABASE"] = "privacy",
            ["PRIVACY_ERASURE_AUTHORITY_RUNTIME_USERNAME"] = "runtime",
            ["PRIVACY_ERASURE_AUTHORITY_RUNTIME_PASSWORD"] = "runtime-secret",
            ["PRIVACY_ERASURE_AUTHORITY_MIGRATOR_USERNAME"] = "migrator",
            ["PRIVACY_ERASURE_AUTHORITY_MIGRATOR_PASSWORD"] = "migrator-secret",
            ["PRIVACY_ERASURE_AUTHORITY_TLS_MODE"] = "Required",
            ["PRIVACY_ERASURE_AUTHORITY_TRUST_SERVER_CERTIFICATE"] = "false"
        });

        await Assert.That(configuration["PrivacyErasure:Authority:Topology"])
            .IsEqualTo("ExternalDatabase");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Provider"])
            .IsEqualTo("PostgreSql");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Host"])
            .IsEqualTo("authority");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Runtime:Username"])
            .IsEqualTo("runtime");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Migrator:Username"])
            .IsEqualTo("migrator");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsErasureDatabaseKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ERASURE_DATABASE_TOPOLOGY"] = "ExternalDatabase",
            ["ERASURE_DATABASE_HOST"] = "erasure-authority",
            ["ERASURE_DATABASE_PORT"] = "5432",
            ["ERASURE_DATABASE_NAME"] = "erasure_db",
            ["ERASURE_DATABASE_RUNTIME_USERNAME"] = "erasure_runtime",
            ["ERASURE_DATABASE_RUNTIME_PASSWORD"] = "erasure-secret",
            ["ERASURE_DATABASE_MIGRATOR_USERNAME"] = "erasure_migrator",
            ["ERASURE_DATABASE_MIGRATOR_PASSWORD"] = "erasure-migrator-secret",
        });

        await Assert.That(configuration["PrivacyErasure:Authority:Topology"])
            .IsEqualTo("ExternalDatabase");
        await Assert.That(configuration["Database:Erasure:Provider"])
            .IsEqualTo("PostgreSql");
        await Assert.That(configuration["Database:Erasure:Host"])
            .IsEqualTo("erasure-authority");
        await Assert.That(configuration["Database:Erasure:Database"])
            .IsEqualTo("erasure_db");
        await Assert.That(configuration["Database:Erasure:Runtime:Username"])
            .IsEqualTo("erasure_runtime");
        await Assert.That(configuration["Database:Erasure:Migrator:Username"])
            .IsEqualTo("erasure_migrator");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Provider"])
            .IsEqualTo("PostgreSql");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Host"])
            .IsEqualTo("erasure-authority");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Database"])
            .IsEqualTo("erasure_db");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Runtime:Username"])
            .IsEqualTo("erasure_runtime");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Migrator:Username"])
            .IsEqualTo("erasure_migrator");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_DoesNotOverrideCanonicalPrivacyErasureAuthorityKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["ERASURE_DATABASE_TOPOLOGY"] = "ExternalDatabase",
            ["PRIVACY_ERASURE_AUTHORITY_HOST"] = "mapped-host",
            ["PRIVACY_ERASURE_AUTHORITY_RUNTIME_USERNAME"] = "mapped-user",
            ["PrivacyErasure:Authority:Topology"] = "EmbeddedSqlite",
            ["PrivacyErasureAuthorityDatabase:Host"] = "explicit-host",
            ["PrivacyErasureAuthorityDatabase:Runtime:Username"] = "explicit-user"
        });

        await Assert.That(configuration["PrivacyErasure:Authority:Topology"])
            .IsEqualTo("EmbeddedSqlite");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Host"])
            .IsEqualTo("explicit-host");
        await Assert.That(configuration["PrivacyErasureAuthorityDatabase:Runtime:Username"])
            .IsEqualTo("explicit-user");
    }

    [Test]
    [Arguments(null, true)]
    [Arguments("", true)]
    [Arguments("local", true)]
    [Arguments("CERBOS", true)]
    [Arguments("fallback", false)]
    public async Task AuthorizationProviderDeploymentOptions_ValidatesOnlySupportedIntent(
        string? provider,
        bool expected)
    {
        var result = AuthorizationProviderDeploymentOptions.IsValid(new()
        {
            Provider = provider
        });

        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsNonSecretMailSmtpKeysOnly()
    {
        var username = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["MAIL_SMTP_HOST"] = "mailpit",
            ["MAIL_SMTP_PORT"] = "1025",
            ["MAIL_SMTP_USERNAME"] = username,
            ["MAIL_SMTP_PASSWORD"] = password,
            ["MAIL_SMTP_ENCRYPTION"] = "None",
            ["MAIL_SMTP_FROM_ADDRESS"] = "noreply@localhost",
            ["MAIL_SMTP_FROM_NAME"] = "ISLAMU Event Dev"
        });

        await Assert.That(configuration["Smtp:Host"]).IsEqualTo("mailpit");
        await Assert.That(configuration["Smtp:Port"]).IsEqualTo("1025");
        await Assert.That(configuration["Smtp:Username"]).IsNull();
        await Assert.That(configuration["Smtp:Password"]).IsNull();
        await Assert.That(configuration["Smtp:Encryption"]).IsEqualTo("None");
        await Assert.That(configuration["Smtp:FromAddress"]).IsEqualTo("noreply@localhost");
        await Assert.That(configuration["Smtp:FromName"]).IsEqualTo("ISLAMU Event Dev");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsApiFolderVapidKeys()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["VAPID_PUBLIC_KEY"] = "public-key",
            ["VAPID_PRIVATE_KEY"] = "private-key",
            ["VAPID_SUBJECT"] = "mailto:admin@example.com"
        });

        await Assert.That(configuration["WebPush:Enabled"]).IsEqualTo("true");
        await Assert.That(configuration["WebPush:VapidPublicKey"]).IsEqualTo("public-key");
        await Assert.That(configuration["WebPush:VapidPrivateKey"]).IsEqualTo("private-key");
        await Assert.That(configuration["WebPush:VapidSubject"]).IsEqualTo("mailto:admin@example.com");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_MapsDocumentedGeocodingEnvironmentContract()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["GEOCODING_PROVIDER"] = "Photon",
            ["GEOCODING_ENDPOINT"] = "https://photon.operator.example/",
            ["GEOCODING_LANGUAGE"] = "nl",
            ["GEOCODING_COUNTRY_CODES"] = "be,nl",
            ["GEOCODING_DATASET_VERSION"] = "benelux-2026-08-26",
            ["GEOCODING_MAXIMUM_RESULTS"] = "12",
            ["GEOCODING_MAXIMUM_RESPONSE_BYTES"] = "131072",
            ["GEOCODING_TOTAL_TIMEOUT_MILLISECONDS"] = "4500",
            ["GEOCODING_MAXIMUM_RETRY_COUNT"] = "2",
            ["GEOCODING_RETRY_DELAYS_MILLISECONDS"] = "150,400",
            ["GEOCODING_READINESS_TIMEOUT_MILLISECONDS"] = "1250",
            ["GEOCODING_SELECTION_LIFETIME_SECONDS"] = "240"
        });

        await Assert.That(configuration["Geocoding:Provider"]).IsEqualTo("Photon");
        await Assert.That(configuration["Geocoding:Endpoint"])
            .IsEqualTo("https://photon.operator.example/");
        await Assert.That(configuration["Geocoding:Language"]).IsEqualTo("nl");
        await Assert.That(configuration["Geocoding:CountryCodes:0"]).IsEqualTo("be");
        await Assert.That(configuration["Geocoding:CountryCodes:1"]).IsEqualTo("nl");
        await Assert.That(configuration["Geocoding:DatasetVersion"])
            .IsEqualTo("benelux-2026-08-26");
        await Assert.That(configuration["Geocoding:MaximumResults"]).IsEqualTo("12");
        await Assert.That(configuration["Geocoding:MaximumResponseBytes"])
            .IsEqualTo("131072");
        await Assert.That(configuration["Geocoding:TotalTimeoutMilliseconds"])
            .IsEqualTo("4500");
        await Assert.That(configuration["Geocoding:MaximumRetryCount"]).IsEqualTo("2");
        await Assert.That(configuration["Geocoding:RetryDelaysMilliseconds:0"])
            .IsEqualTo("150");
        await Assert.That(configuration["Geocoding:RetryDelaysMilliseconds:1"])
            .IsEqualTo("400");
        await Assert.That(configuration["Geocoding:ReadinessTimeoutMilliseconds"])
            .IsEqualTo("1250");
        await Assert.That(configuration["Geocoding:SelectionLifetimeSeconds"])
            .IsEqualTo("240");
    }

    [Test]
    public async Task AddSecretAuthorityConfiguration_WhenUserSecretsIsSelectedInProduction_FailsClosed()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = "UserSecrets",
        });

        Action act = () => builder.AddSecretAuthorityConfiguration("Production");

        var exception = await Assert.That(act).Throws<InvalidOperationException>();
        await Assert.That(exception!.Message)
            .IsEqualTo("secret_authority_user_secrets_environment_invalid");
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
    {
        values.TryAdd("SecretProvider:Provider", "Environment");
        var previous = values.Keys.ToDictionary(
            key => key,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);

        try
        {
            foreach (var pair in values)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }

            var builder = new ConfigurationBuilder()
                .AddInMemoryCollection(values);
            builder.AddSecretAuthorityConfiguration("Testing");
            return builder.Build();
        }
        finally
        {
            foreach (var pair in previous)
            {
                Environment.SetEnvironmentVariable(pair.Key, pair.Value);
            }
        }
    }
}
