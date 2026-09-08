using System.Net.Sockets;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DotNetEnv;
using Explore.Application.Configuration;
using Explore.Infrastructure.Configuration;
using Explore.Infrastructure.Webhooks;
using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

const string DefaultControlPlaneHost = "admin.localhost";
const int DefaultControlPlanePort = 7002;
var repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
var dotenvPath = Path.Combine(repositoryRoot, ".env");
if (File.Exists(dotenvPath))
    Env.NoClobber().Load(dotenvPath);
var builder = DistributedApplication.CreateBuilder(args);
var runMode = AspireRunModeExtensions.Parse(builder.Configuration["ISLAMU_ASPIRE_MODE"]);
SecretProviderType configuredSecretProvider =
    SecretAuthorityConfiguration.GetRequiredProvider(builder.Configuration);
IConfiguration authorityBootstrap = runMode == AspireRunMode.FullLocal
    && configuredSecretProvider == SecretProviderType.Infisical
    ? new ConfigurationBuilder()
        .AddConfiguration(builder.Configuration)
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecretProvider:Provider"] = nameof(SecretProviderType.Environment),
        })
        .Build()
    : builder.Configuration;
IConfiguration secretAuthority = SecretAuthorityConfiguration.Build(
    authorityBootstrap,
    builder.Environment.EnvironmentName,
    "/keycloak", "/database", "/database/erasure", "/api", "/blazor",
    "/cerbos", "/mcp", "/ai", "/storage", "/smtp", "/stripe",
    "/integrations/listmonk");
builder.Configuration.AddConfiguration(secretAuthority);
var hostingTopology = ParseHostingTopology(builder.Configuration["Hosting:Topology"]);
var configurationManifestMode = ConfiguredValue(
    builder.Configuration,
    "CONFIGURATION_MANIFEST_MODE",
    "Off");
var configurationManifestHostDirectory = ConfiguredValue(
    builder.Configuration,
    "CONFIGURATION_MANIFEST_HOST_DIRECTORY",
    Path.Combine("deploy", "bootstrap"));
var configurationManifestHostPath = Path.GetFullPath(
    Path.Combine(
        configurationManifestHostDirectory,
        "configuration-manifest.json"),
    repositoryRoot);
var eventLocationPrivacyMigrationStage =
    builder.Configuration["Database:Migrations:EventLocationPrivacyStage"];
var privacyErasureTopology = ParsePrivacyErasureTopology(
    ConfiguredValue(
        builder.Configuration,
        "PrivacyErasure:Authority:Topology",
        ConfiguredValue(
            builder.Configuration,
            "ERASURE_TOPOLOGY",
            ConfiguredValue(
                builder.Configuration,
                "PRIVACY_ERASURE_AUTHORITY_TOPOLOGY",
                nameof(PrivacyErasureAuthorityTopology.EmbeddedSqlite)))));
var usesEmbeddedPrivacyErasureAuthority =
    privacyErasureTopology == PrivacyErasureAuthorityTopology.EmbeddedSqlite;
var usesExternalPrivacyErasureAuthority =
    privacyErasureTopology == PrivacyErasureAuthorityTopology.ExternalDatabase;
var webhookProvider = ConfiguredValue(
    builder.Configuration,
    "WEBHOOKS_PROVIDER",
    WebhookOptions.ProviderLocal);
var includeSvix = UsesSvixProvider(webhookProvider);
var appHostConfigRoot = Path.Combine(repositoryRoot, "src", "Explore.AppHost", "Config");
var cerbosPolicyPackagePath = Path.Combine(repositoryRoot, "cerbos", "policies");
var cerbosConfigPath = Path.Combine(repositoryRoot, "cerbos", "config", ".cerbos.yaml");
var cerbosSchemaPath = Path.Combine(repositoryRoot, "cerbos", "init", "cerbos-schema.sql");
var keycloakRealmExportPath = Path.Combine(repositoryRoot, "docker", "keycloak", "realm-export.json");
var keycloakInitScriptPath = Path.Combine(repositoryRoot, "docker", "keycloak", "keycloak-init.sh");
var coopNginxConfigPath = Path.Combine(appHostConfigRoot, "coop", "nginx.conf");
var localStorageRootPath = Path.Combine(repositoryRoot, "storage-data", "aspire-local");
var embeddedPrivacyErasureAuthorityPath = Path.Combine(
    repositoryRoot,
    "privacy-erasure-authority-data",
    "aspire-local",
    "privacy_erasure_authority.db");
var embeddedPrivacyErasureAuthorityDirectory = Path.GetDirectoryName(embeddedPrivacyErasureAuthorityPath)!;
var embeddedPrivacyErasureAuthorityBusyTimeout = ConfiguredValue(
    builder.Configuration,
    "PRIVACY_ERASURE_AUTHORITY_BUSY_TIMEOUT_SECONDS",
    EmbeddedPrivacyErasureAuthorityOptions.DefaultBusyTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
var prometheusConfigPath = Path.Combine(appHostConfigRoot, "prometheus.yaml");
var admissionCheckInAlertRulesPath = Path.Combine(appHostConfigRoot, "admission-check-in-alerts.yaml");
var grafanaDashboardPath = Path.Combine(appHostConfigRoot, "grafana-dashboard");
var pgAdminServersPath = Path.Combine(appHostConfigRoot, "pgadmin", "servers.json");
var pgAdminPassFilePath = Path.Combine(appHostConfigRoot, "pgadmin", "pgpass");
Directory.CreateDirectory(localStorageRootPath);
if (usesEmbeddedPrivacyErasureAuthority)
    Directory.CreateDirectory(embeddedPrivacyErasureAuthorityDirectory);
Directory.CreateDirectory(grafanaDashboardPath);

Console.WriteLine("===========================================");
Console.WriteLine("Explore AppHost - Local Development Orchestrator");
Console.WriteLine($"Mode: {runMode}");
Console.WriteLine($"Hosting topology: {hostingTopology}");
Console.WriteLine("local-full: full local platform; local-default: lightweight local platform; local-core: local data/cache; local-lite: external infrastructure");
Console.WriteLine("===========================================");

// Delayed health check for startup sequencing
var startAfter = DateTime.Now.AddSeconds(30);
builder.Services.AddHealthChecks().AddCheck("startup-delay", () =>
    DateTime.Now > startAfter ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());

IResourceBuilder<PostgresDatabaseResource>? database = null;
IResourceBuilder<PostgresDatabaseResource>? privacyErasureDatabase = null;
IResourceBuilder<RedisResource>? cache = null;
IResourceBuilder<RabbitMQServerResource>? messaging = null;
LocalPlatformResources? localPlatformResources = null;
var mailpit = AddMailpit(builder);

if (runMode.UsesLocalData())
{
    database = builder.AddPostgres("postgres")
        .WithImageTag("18-alpine")
        .WithDataVolume("islamu-event-postgres-data")
        .AddDatabase("islamu-event-db", "islamu_event_db");

    if (usesExternalPrivacyErasureAuthority)
    {
        privacyErasureDatabase = builder.AddPostgres("privacy-erasure-postgres")
            .WithImageTag("18-alpine")
            .WithDataVolume("islamu-event-privacy-erasure-postgres-data")
            .AddDatabase("privacy-erasure-authority", "islamu_event_privacy_erasure");
    }

    cache = builder.AddRedis("cache")
        .WithDataVolume("islamu-event-redis-data");
}

if (runMode is AspireRunMode.FullLocal or AspireRunMode.DefaultLocal)
{
    messaging = builder.AddRabbitMQ("messaging")
        .WithManagementPlugin()
        .WithDataVolume("islamu-event-rabbitmq-data");

    localPlatformResources = AddLocalPlatform(
        builder,
        includeHeavyExtras: runMode == AspireRunMode.FullLocal,
        includeSvix,
        cerbosConfigPath,
        cerbosPolicyPackagePath,
        cerbosSchemaPath,
        keycloakRealmExportPath,
        keycloakInitScriptPath,
        mailpit,
        coopNginxConfigPath,
        prometheusConfigPath,
        admissionCheckInAlertRulesPath,
        grafanaDashboardPath,
        pgAdminServersPath,
        pgAdminPassFilePath,
        database,
        cache);
}

if (runMode == AspireRunMode.FullLocal)
{
    AddLocalFormbricks(builder);
}

var migrations = WithProfileSecretMode(
        builder.AddProject<Projects.Event_MigrationService>(
            "event-migrationservice",
            ExcludeProjectLaunchProfile),
        runMode,
        builder.Configuration)
    .WithEnvironment("PrivacyErasure__Authority__Topology", privacyErasureTopology.ToString());
migrations = ConfigureConfigurationManifestOwner(
    migrations,
    ownsConfigurationManifest: hostingTopology == HostingTopology.Split,
    configurationManifestMode,
    configurationManifestHostPath);

if (usesEmbeddedPrivacyErasureAuthority)
{
    migrations = WithEmbeddedPrivacyErasureAuthority(
        migrations,
        embeddedPrivacyErasureAuthorityPath,
        embeddedPrivacyErasureAuthorityBusyTimeout);
}

if (!string.IsNullOrWhiteSpace(eventLocationPrivacyMigrationStage))
{
    migrations = migrations.WithEnvironment("Database__Migrations__EventLocationPrivacyStage", eventLocationPrivacyMigrationStage);
}

if (database is not null)
{
    migrations = WithLocalPrimaryDatabase(migrations, database, PrimaryDatabaseRole.Migrator)
        .WaitFor(database);
}
else
{
    migrations = WithExternalPrimaryDatabase(builder, migrations, PrimaryDatabaseRole.Migrator);
}

if (privacyErasureDatabase is not null)
{
    migrations = WithLocalPrivacyErasureAuthorityDatabase(
            migrations,
            privacyErasureDatabase,
            PrimaryDatabaseRole.Migrator)
        .WaitFor(privacyErasureDatabase);
}
else if (usesExternalPrivacyErasureAuthority)
{
    migrations = WithExternalPrivacyErasureAuthorityDatabase(
        builder,
        migrations,
        PrimaryDatabaseRole.Migrator);
}

migrations = ConfigureLocalMailpitSmtp(migrations, mailpit, builder.Configuration);

var vapidPublicKey = builder.Configuration["VAPID_PUBLIC_KEY"];
var vapidPrivateKey = builder.Configuration["VAPID_PRIVATE_KEY"];
var vapidSubject = builder.Configuration["VAPID_SUBJECT"];

if (hostingTopology == HostingTopology.Split)
{
    var exploreAPI = WithProfileSecretMode(
            builder.AddProject<Projects.Explore_API>(
                    "explore-api",
                    ExcludeProjectLaunchProfile)
                .WithHttpEndpoint(name: "http")
                .WithHttpsEndpoint(port: 7039, name: "https"),
            runMode,
            builder.Configuration)
        .WithEnvironment("HttpsRedirection__Enabled", "false")
        .WithEnvironment("CONTROL_PLANE_PUBLIC_ORIGIN", ConfiguredValue(
            builder.Configuration,
            "CONTROL_PLANE_PUBLIC_ORIGIN",
            BuildDefaultHttpUri(DefaultControlPlaneHost, DefaultControlPlanePort)))
        .WithEnvironment("Cerbos__PolicyPackagePath", cerbosPolicyPackagePath)
        .WithEnvironment("Storage__Local__RootPath", localStorageRootPath)
        .WithEnvironment("Storage__Local__CreateRootIfMissing", "true")
        .WithEnvironment("StorageReconciliation__Enabled", "true")
        .WithEnvironment("StorageReconciliation__DryRun", "true")
        .WithEnvironment("PrivacyErasure__Authority__Topology", privacyErasureTopology.ToString())
        .WithEnvironment("PublicBaseUrl", ConfiguredValue(builder.Configuration, "PUBLIC_BASE_URL", string.Empty))
        .WithEnvironment("Payments__Stripe__Mode", ConfiguredValue(builder.Configuration, "PAYMENTS_STRIPE_MODE", "Test"))
        .WithEnvironment("Payments__Stripe__AllowedCheckoutHosts__0", ConfiguredValue(builder.Configuration, "PAYMENTS_STRIPE_ALLOWED_CHECKOUT_HOST", "checkout.stripe.com"))
        .WithEnvironment("Payments__OrganizerDirect__ProviderCode", ConfiguredValue(builder.Configuration, "PAYMENTS_ORGANIZER_DIRECT_PROVIDER_CODE", string.Empty))
        .WithEnvironment("Payments__OrganizerDirect__ConnectPlatformId", ConfiguredValue(builder.Configuration, "PAYMENTS_ORGANIZER_DIRECT_CONNECT_PLATFORM_ID", string.Empty))
        .WithEnvironment("STRIPE_PLATFORM_SECRET_KEY", ConfiguredValue(builder.Configuration, "STRIPE_PLATFORM_SECRET_KEY", string.Empty))
        .WithEnvironment("STRIPE_WEBHOOK_SECRET", ConfiguredValue(builder.Configuration, "STRIPE_WEBHOOK_SECRET", string.Empty))
        .WithEnvironment("ForwardedHeadersTrust__ForwardLimit", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_FORWARD_LIMIT", "1"))
        .WithEnvironment("ForwardedHeadersTrust__TrustLoopbackProxy", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_TRUST_LOOPBACK", "true"))
        .WithEnvironment("ForwardedHeadersTrust__KnownProxies__0", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_KNOWN_PROXY", "127.0.0.1"))
        .WithEnvironment("ForwardedHeadersTrust__KnownNetworks__0", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_KNOWN_NETWORK", "::1/128"))
        .WaitFor(mailpit);

    if (usesEmbeddedPrivacyErasureAuthority)
    {
        exploreAPI = WithEmbeddedPrivacyErasureAuthority(
            exploreAPI,
            embeddedPrivacyErasureAuthorityPath,
            embeddedPrivacyErasureAuthorityBusyTimeout);
    }

    exploreAPI = ConfigureLocalMailpitSmtp(exploreAPI, mailpit, builder.Configuration);
    exploreAPI = ConfigureGeocoding(exploreAPI, builder.Configuration);

    if (!string.IsNullOrWhiteSpace(eventLocationPrivacyMigrationStage))
    {
        exploreAPI = exploreAPI.WithEnvironment("Database__Migrations__EventLocationPrivacyStage", eventLocationPrivacyMigrationStage);
    }

    if (!string.IsNullOrWhiteSpace(vapidPublicKey))
        exploreAPI = exploreAPI.WithEnvironment("VAPID_PUBLIC_KEY", vapidPublicKey);
    if (!string.IsNullOrWhiteSpace(vapidSubject))
        exploreAPI = exploreAPI.WithEnvironment("VAPID_SUBJECT", vapidSubject);
    if (!string.IsNullOrWhiteSpace(vapidPrivateKey))
    {
        var vapidPrivateKeyParameter = builder.AddParameterFromConfiguration(
            "vapid-private-key",
            "VAPID_PRIVATE_KEY",
            secret: true);
        exploreAPI = exploreAPI.WithEnvironment("VAPID_PRIVATE_KEY", vapidPrivateKeyParameter);
    }

    exploreAPI = exploreAPI
        .WithReference(migrations)
        .WaitForCompletion(migrations);

    if (database is not null)
    {
        exploreAPI = WithLocalPrimaryDatabase(exploreAPI, database, PrimaryDatabaseRole.Runtime)
            .WaitFor(database);
    }
    else
    {
        exploreAPI = WithExternalPrimaryDatabase(builder, exploreAPI, PrimaryDatabaseRole.Runtime);
    }

    if (privacyErasureDatabase is not null)
    {
        exploreAPI = WithLocalPrivacyErasureAuthorityDatabase(
                exploreAPI,
                privacyErasureDatabase,
                PrimaryDatabaseRole.Runtime)
            .WaitFor(privacyErasureDatabase);
    }
    else if (usesExternalPrivacyErasureAuthority)
    {
        exploreAPI = WithExternalPrivacyErasureAuthorityDatabase(
            builder,
            exploreAPI,
            PrimaryDatabaseRole.Runtime);
    }

    if (cache is not null)
    {
        exploreAPI = exploreAPI
            .WithReference(cache)
            .WaitFor(cache);
    }

    if (messaging is not null)
    {
        exploreAPI = exploreAPI
            .WithReference(messaging)
            .WaitFor(messaging)
            .WithEnvironment("EmailDispatchRabbitMq__Enabled", "true");
    }
    else
    {
        exploreAPI = exploreAPI.WithEnvironment("EmailDispatchRabbitMq__Enabled", "false");
    }

    if (localPlatformResources is not null)
    {
        exploreAPI = ConfigureLocalPlatformApi(
                exploreAPI,
                localPlatformResources,
                builder.Configuration)
            .WaitFor(localPlatformResources.Keycloak)
            .WaitFor(localPlatformResources.Cerbos)
            .WaitFor(localPlatformResources.Minio);
    }

    // Service discovery (via WithReference) automatically resolves the API URL at runtime.
    // Do NOT hardcode ExploreAPI__BaseUrl here — Aspire assigns dynamic ports.
    var exploreBlazor = WithProfileSecretMode(
            builder.AddProject<Projects.Explore_Blazor>(
                    "explore-blazor",
                    ExcludeProjectLaunchProfile)
                .WithHttpEndpoint(name: "http")
                .WithHttpsEndpoint(port: 7177, name: "https"),
            runMode,
            builder.Configuration)
        .WithReference(exploreAPI)
        .WaitFor(exploreAPI)
        .WithEnvironment("Bff__AdminHosts__0", ConfiguredValue(
            builder.Configuration,
            "CONTROL_PLANE_PUBLIC_ORIGIN",
            BuildDefaultHttpUri(DefaultControlPlaneHost, DefaultControlPlanePort)))
        .WithEnvironment("ForwardedHeadersTrust__ForwardLimit", ConfiguredValue(
            builder.Configuration,
            "BFF_FORWARDED_HEADERS_FORWARD_LIMIT",
            "1"))
        .WithEnvironment("ForwardedHeadersTrust__TrustLoopbackProxy", ConfiguredValue(
            builder.Configuration,
            "BFF_FORWARDED_HEADERS_TRUST_LOOPBACK",
            "true"))
        .WithEnvironment("ForwardedHeadersTrust__KnownProxies__0", ConfiguredValue(
            builder.Configuration,
            "BFF_FORWARDED_HEADERS_KNOWN_PROXY",
            "127.0.0.1"))
        .WithEnvironment("ForwardedHeadersTrust__KnownNetworks__0", ConfiguredValue(
            builder.Configuration,
            "BFF_FORWARDED_HEADERS_KNOWN_NETWORK",
            "::1/128"))
        .WithEnvironment("Payments__Stripe__AllowedCheckoutHosts__0", ConfiguredValue(
            builder.Configuration,
            "PAYMENTS_STRIPE_ALLOWED_CHECKOUT_HOST",
            "checkout.stripe.com"))
        .WithEnvironment("RateLimiting__RegistrationPaymentCheckoutIssue__PermitLimit", ConfiguredValue(
            builder.Configuration,
            "BFF_PAYMENT_CHECKOUT_PERMIT_LIMIT",
            "10"))
        .WithEnvironment("RateLimiting__RegistrationPaymentCheckoutIssue__WindowSeconds", ConfiguredValue(
            builder.Configuration,
            "BFF_PAYMENT_CHECKOUT_WINDOW_SECONDS",
            "60"))
        .WithEnvironment("Storage__Local__RootPath", localStorageRootPath);

    if (localPlatformResources is not null)
    {
        localPlatformResources = localPlatformResources with
        {
            KeycloakInit = ConfigureLocalKeycloakCallbacks(
                localPlatformResources.KeycloakInit,
                exploreBlazor,
                builder.Configuration)
        };
    }

    exploreBlazor = exploreBlazor
        .WithReference(migrations)
        .WaitForCompletion(migrations);

    if (database is not null)
    {
        exploreBlazor = WithLocalPrimaryDatabase(exploreBlazor, database, PrimaryDatabaseRole.Runtime)
            .WaitFor(database);
    }
    else
    {
        exploreBlazor = WithExternalPrimaryDatabase(builder, exploreBlazor, PrimaryDatabaseRole.Runtime);
    }

    if (cache is not null)
    {
        exploreBlazor = exploreBlazor
            .WithReference(cache)
            .WaitFor(cache);
    }

    if (localPlatformResources is not null)
    {
        exploreBlazor = ConfigureLocalPlatformBlazor(exploreBlazor, localPlatformResources, builder.Configuration)
            .WaitFor(localPlatformResources.Keycloak);
    }
}
else
{
    var eventStandalone = WithProfileSecretMode(
            builder.AddProject<Projects.Event_Standalone>(
                    "event-standalone",
                    ExcludeProjectLaunchProfile)
                .WithHttpEndpoint(name: "http")
                .WithHttpsEndpoint(port: 7180, name: "https"),
            runMode,
            builder.Configuration)
        .WithEnvironment("HttpsRedirection__Enabled", "false")
        .WithEnvironment("CONTROL_PLANE_PUBLIC_ORIGIN", ConfiguredValue(
            builder.Configuration,
            "CONTROL_PLANE_PUBLIC_ORIGIN",
            BuildDefaultHttpUri(DefaultControlPlaneHost, DefaultControlPlanePort)))
        .WithEnvironment("Cerbos__PolicyPackagePath", cerbosPolicyPackagePath)
        .WithEnvironment("Storage__Local__RootPath", localStorageRootPath)
        .WithEnvironment("Storage__Local__CreateRootIfMissing", "true")
        .WithEnvironment("StorageReconciliation__Enabled", "true")
        .WithEnvironment("StorageReconciliation__DryRun", "true")
        .WithEnvironment("PrivacyErasure__Authority__Topology", privacyErasureTopology.ToString())
        .WithEnvironment("PublicBaseUrl", ConfiguredValue(builder.Configuration, "PUBLIC_BASE_URL", string.Empty))
        .WithEnvironment("Payments__Stripe__Mode", ConfiguredValue(builder.Configuration, "PAYMENTS_STRIPE_MODE", "Test"))
        .WithEnvironment("Payments__Stripe__AllowedCheckoutHosts__0", ConfiguredValue(builder.Configuration, "PAYMENTS_STRIPE_ALLOWED_CHECKOUT_HOST", "checkout.stripe.com"))
        .WithEnvironment("Payments__OrganizerDirect__ProviderCode", ConfiguredValue(builder.Configuration, "PAYMENTS_ORGANIZER_DIRECT_PROVIDER_CODE", string.Empty))
        .WithEnvironment("Payments__OrganizerDirect__ConnectPlatformId", ConfiguredValue(builder.Configuration, "PAYMENTS_ORGANIZER_DIRECT_CONNECT_PLATFORM_ID", string.Empty))
        .WithEnvironment("STRIPE_PLATFORM_SECRET_KEY", ConfiguredValue(builder.Configuration, "STRIPE_PLATFORM_SECRET_KEY", string.Empty))
        .WithEnvironment("STRIPE_WEBHOOK_SECRET", ConfiguredValue(builder.Configuration, "STRIPE_WEBHOOK_SECRET", string.Empty))
        .WithEnvironment("RateLimiting__RegistrationPaymentCheckoutIssue__PermitLimit", ConfiguredValue(builder.Configuration, "BFF_PAYMENT_CHECKOUT_PERMIT_LIMIT", "10"))
        .WithEnvironment("RateLimiting__RegistrationPaymentCheckoutIssue__WindowSeconds", ConfiguredValue(builder.Configuration, "BFF_PAYMENT_CHECKOUT_WINDOW_SECONDS", "60"))
        .WithEnvironment("ForwardedHeadersTrust__ForwardLimit", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_FORWARD_LIMIT", "1"))
        .WithEnvironment("ForwardedHeadersTrust__TrustLoopbackProxy", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_TRUST_LOOPBACK", "true"))
        .WithEnvironment("ForwardedHeadersTrust__KnownProxies__0", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_KNOWN_PROXY", "127.0.0.1"))
        .WithEnvironment("ForwardedHeadersTrust__KnownNetworks__0", ConfiguredValue(builder.Configuration, "API_FORWARDED_HEADERS_KNOWN_NETWORK", "::1/128"))
        .WithEnvironment("Bff__AdminHosts__0", ConfiguredValue(
            builder.Configuration,
            "CONTROL_PLANE_PUBLIC_ORIGIN",
            BuildDefaultHttpUri(DefaultControlPlaneHost, DefaultControlPlanePort)))
        .WaitFor(mailpit);
    eventStandalone = ConfigureConfigurationManifestOwner(
        eventStandalone,
        ownsConfigurationManifest: hostingTopology == HostingTopology.Standalone,
        configurationManifestMode,
        configurationManifestHostPath);

    if (usesEmbeddedPrivacyErasureAuthority)
    {
        eventStandalone = WithEmbeddedPrivacyErasureAuthority(
            eventStandalone,
            embeddedPrivacyErasureAuthorityPath,
            embeddedPrivacyErasureAuthorityBusyTimeout);
    }

    eventStandalone = ConfigureLocalMailpitSmtp(eventStandalone, mailpit, builder.Configuration);
    eventStandalone = ConfigureGeocoding(eventStandalone, builder.Configuration);

    if (!string.IsNullOrWhiteSpace(eventLocationPrivacyMigrationStage))
    {
        eventStandalone = eventStandalone.WithEnvironment("Database__Migrations__EventLocationPrivacyStage", eventLocationPrivacyMigrationStage);
    }

    if (!string.IsNullOrWhiteSpace(vapidPublicKey))
        eventStandalone = eventStandalone.WithEnvironment("VAPID_PUBLIC_KEY", vapidPublicKey);
    if (!string.IsNullOrWhiteSpace(vapidSubject))
        eventStandalone = eventStandalone.WithEnvironment("VAPID_SUBJECT", vapidSubject);
    if (!string.IsNullOrWhiteSpace(vapidPrivateKey))
    {
        var vapidPrivateKeyParameter = builder.AddParameterFromConfiguration(
            "vapid-private-key",
            "VAPID_PRIVATE_KEY",
            secret: true);
        eventStandalone = eventStandalone.WithEnvironment("VAPID_PRIVATE_KEY", vapidPrivateKeyParameter);
    }

    eventStandalone = eventStandalone
        .WithReference(migrations)
        .WaitForCompletion(migrations);

    if (database is not null)
    {
        eventStandalone = WithLocalPrimaryDatabase(
                eventStandalone,
                database,
                PrimaryDatabaseRole.Runtime)
            .WaitFor(database);
    }
    else
    {
        eventStandalone = WithExternalPrimaryDatabase(builder, eventStandalone, PrimaryDatabaseRole.Runtime);
    }

    if (privacyErasureDatabase is not null)
    {
        eventStandalone = WithLocalPrivacyErasureAuthorityDatabase(
                eventStandalone,
                privacyErasureDatabase,
                PrimaryDatabaseRole.Runtime)
            .WaitFor(privacyErasureDatabase);
    }
    else if (usesExternalPrivacyErasureAuthority)
    {
        eventStandalone = WithExternalPrivacyErasureAuthorityDatabase(
            builder,
            eventStandalone,
            PrimaryDatabaseRole.Runtime);
    }

    if (cache is not null)
    {
        eventStandalone = eventStandalone
            .WithReference(cache)
            .WaitFor(cache);
    }

    if (messaging is not null)
    {
        eventStandalone = eventStandalone
            .WithReference(messaging)
            .WaitFor(messaging)
            .WithEnvironment("EmailDispatchRabbitMq__Enabled", "true");
    }
    else
    {
        eventStandalone = eventStandalone.WithEnvironment("EmailDispatchRabbitMq__Enabled", "false");
    }

    if (localPlatformResources is not null)
    {
        localPlatformResources = localPlatformResources with
        {
            KeycloakInit = ConfigureLocalKeycloakCallbacks(
                localPlatformResources.KeycloakInit,
                eventStandalone,
                builder.Configuration)
        };

        eventStandalone = ConfigureLocalPlatformApi(
                eventStandalone,
                localPlatformResources,
                builder.Configuration)
            .WaitFor(localPlatformResources.Keycloak)
            .WaitFor(localPlatformResources.Cerbos)
            .WaitFor(localPlatformResources.Minio);

        eventStandalone = ConfigureLocalPlatformBlazor(
            eventStandalone,
            localPlatformResources,
            builder.Configuration)
            .WaitFor(localPlatformResources.Keycloak);
    }
}

await builder.Build().RunAsync();

static LocalPlatformResources AddLocalPlatform(
    IDistributedApplicationBuilder builder,
    bool includeHeavyExtras,
    bool includeSvix,
    string cerbosConfigPath,
    string cerbosPolicyPackagePath,
    string cerbosSchemaPath,
    string keycloakRealmExportPath,
    string keycloakInitScriptPath,
    IResourceBuilder<ContainerResource> mailpit,
    string coopNginxConfigPath,
    string prometheusConfigPath,
    string admissionCheckInAlertRulesPath,
    string grafanaDashboardPath,
    string pgAdminServersPath,
    string pgAdminPassFilePath,
    IResourceBuilder<PostgresDatabaseResource>? appDatabase,
    IResourceBuilder<RedisResource>? cache)
{
    var configuration = builder.Configuration;
    var keycloakBlazorClientSecret = builder.AddParameter(
        "keycloak-blazor-client-secret",
        () => configuration["KEYCLOAK_BLAZOR_CLIENT_SECRET"] ?? string.Empty,
        publishValueAsDefault: false,
        secret: true);
    var cerbosAdminUsername = ConfiguredValue(configuration, "CERBOS_ADMIN_USERNAME", string.Empty);
    var cerbosAdminCredentialHash = ConfiguredValue(
        configuration,
        "CERBOS_ADMIN_PASSWORD_HASH",
        string.Empty);
    var cerbosPostgresUser = configuration["CERBOS_POSTGRES_USER"] ?? "cerbos_user";
    var cerbosPostgresPassword = configuration["CERBOS_POSTGRES_PASSWORD"] ?? string.Empty;
    var cerbosPostgresDatabase = configuration["CERBOS_POSTGRES_DB"] ?? "cerbos";
    var crdb = builder.AddContainer("crdb", "cockroachdb/cockroach", "v24.1.1")
        .WithArgs("start-single-node", "--insecure")
        .WithVolume("islamu-event-crdb-data", "/cockroach/cockroach-data")
        .WithEndpoint(
            targetPort: 26257,
            port: 26257,
            name: "sql",
            protocol: ProtocolType.Tcp)
        .WithHttpEndpoint(targetPort: 8080, port: 8081, name: "ui");

    var keycloak = builder.AddContainer("keycloak", "quay.io/phasetwo/phasetwo-keycloak", "26")
        .WithArgs(
            "start",
            "--import-realm",
            "--verbose",
            "--spi-email-template-provider=freemarker-plus-mustache",
            "--spi-email-template-freemarker-plus-mustache-enabled=true",
            "--spi-theme-cache-themes=false")
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", configuration["KEYCLOAK_ADMIN"] ?? string.Empty)
        .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", configuration["KEYCLOAK_ADMIN_PASSWORD"] ?? string.Empty)
        .WithEnvironment("KC_DB", "cockroach")
        .WithEnvironment("KC_DB_URL_HOST", "crdb")
        .WithEnvironment("KC_DB_URL_PORT", "26257")
        .WithEnvironment("KC_DB_URL_DATABASE", "defaultdb")
        .WithEnvironment("KC_DB_SCHEMA", "public")
        .WithEnvironment("KC_DB_USERNAME", "root")
        .WithEnvironment("KC_DB_PASSWORD", "")
        .WithEnvironment("KC_DB_URL_PROPERTIES", "?sslmode=disable&useCockroachMetadata=true")
        .WithEnvironment("KC_TRANSACTION_XA_ENABLED", "false")
        .WithEnvironment("KC_TRANSACTION_JTA_ENABLED", "false")
        .WithEnvironment("KC_CACHE_CONFIG_FILE", "cache-ispn-jdbc-ping.xml")
        .WithEnvironment("KC_ISPN_DB_VENDOR", "cockroachdb")
        .WithEnvironment("KC_HTTP_ENABLED", "true")
        .WithEnvironment("KC_HTTP_RELATIVE_PATH", "/auth")
        .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
        .WithEnvironment("KC_HOSTNAME_STRICT", "false")
        .WithEnvironment("KC_HEALTH_ENABLED", "true")
        .WithEnvironment("KC_METRICS_ENABLED", "true")
        .WithEnvironment("KC_LOG_LEVEL", "INFO,io.phasetwo:DEBUG")
        .WithVolume("islamu-event-keycloak-data", "/opt/keycloak/data")
        .WithBindMount(keycloakRealmExportPath, "/opt/keycloak/data/import/realm-export.json", isReadOnly: true)
        .WithHttpEndpoint(targetPort: 8080, port: 8080, name: "http")
        .WithHttpEndpoint(targetPort: 9000, port: 9000, name: "mgmt")
        .WithHttpHealthCheck("/auth/health/ready", endpointName: "mgmt")
        .WaitFor(crdb);

    var keycloakInit = builder.AddContainer("keycloak-init", "quay.io/phasetwo/phasetwo-keycloak", "26")
        .WithEntrypoint("/bin/bash")
        .WithArgs("/opt/keycloak/bin/keycloak-init.sh")
        .WithEnvironment("KEYCLOAK_INTERNAL_URL", BuildHttpUri("keycloak", 8080, "/auth"))
        .WithEnvironment("KEYCLOAK_REALM", configuration["KEYCLOAK_REALM"] ?? "ISLAMU")
        .WithEnvironment("KEYCLOAK_ADMIN", configuration["KEYCLOAK_ADMIN"] ?? string.Empty)
        .WithEnvironment("KEYCLOAK_ADMIN_PASSWORD", configuration["KEYCLOAK_ADMIN_PASSWORD"] ?? string.Empty)
        .WithEnvironment("KEYCLOAK_BLAZOR_CLIENT_ID", configuration["KEYCLOAK_BLAZOR_CLIENT_ID"] ?? "islamu-event-blazor")
        .WithEnvironment("KEYCLOAK_BLAZOR_CLIENT_SECRET", keycloakBlazorClientSecret)
        .WithEnvironment("KEYCLOAK_API_CLIENT_ID", configuration["KEYCLOAK_API_CLIENT_ID"] ?? "islamu-event-api")
        .WithEnvironment("KEYCLOAK_SMTP_HOST", "mailpit")
        .WithEnvironment("KEYCLOAK_SMTP_PORT", "1025")
        .WithEnvironment("KEYCLOAK_SMTP_FROM", configuration["KEYCLOAK_SMTP_FROM"] ?? "noreply@openislamu.org")
        .WithEnvironment("KEYCLOAK_SMTP_FROM_DISPLAY_NAME", configuration["KEYCLOAK_SMTP_FROM_DISPLAY_NAME"] ?? "ISLAMU Event Dev")
        .WithEnvironment("KEYCLOAK_SMTP_AUTH", configuration["KEYCLOAK_SMTP_AUTH"] ?? "false")
        .WithEnvironment("KEYCLOAK_SMTP_SSL", configuration["KEYCLOAK_SMTP_SSL"] ?? "false")
        .WithEnvironment("KEYCLOAK_SMTP_STARTTLS", configuration["KEYCLOAK_SMTP_STARTTLS"] ?? "false")
        .WithBindMount(keycloakInitScriptPath, "/opt/keycloak/bin/keycloak-init.sh", isReadOnly: true)
        .WaitFor(keycloak)
        .WaitFor(mailpit);

    var cerbosDb = builder.AddContainer("cerbos-db", "postgres", "18-alpine")
        .WithEnvironment("POSTGRES_USER", cerbosPostgresUser)
        .WithEnvironment("POSTGRES_PASSWORD", cerbosPostgresPassword)
        .WithEnvironment("POSTGRES_DB", cerbosPostgresDatabase)
        .WithVolume("islamu-event-cerbos-data", "/var/lib/postgresql")
        .WithBindMount(cerbosSchemaPath, "/docker-entrypoint-initdb.d/cerbos-schema.sql", isReadOnly: true);

    var cerbos = builder.AddContainer("cerbos", "ghcr.io/cerbos/cerbos", "0.53.0")
        .WithArgs("server", "--config=/config/.cerbos.yaml")
        .WithEnvironment("CERBOS_ADMIN_USER", cerbosAdminUsername)
        .WithEnvironment("CERBOS_ADMIN_PASSWORD_HASH", cerbosAdminCredentialHash)
        .WithEnvironment(
            "CERBOS_PG_URL",
            $"postgres://{cerbosPostgresUser}:{cerbosPostgresPassword}@cerbos-db:5432/{cerbosPostgresDatabase}?search_path=cerbos&sslmode=disable")
        .WithBindMount(cerbosConfigPath, "/config/.cerbos.yaml", isReadOnly: true)
        .WithBindMount(cerbosPolicyPackagePath, "/policies", isReadOnly: true)
        .WithHttpEndpoint(targetPort: 3592, port: 3592, name: "http")
        .WithEndpoint(
            targetPort: 3593,
            port: 3593,
            name: "grpc",
            protocol: ProtocolType.Tcp)
        .WithHttpHealthCheck("/_cerbos/health", endpointName: "http")
        .WaitFor(cerbosDb);

    var minio = builder.AddContainer("minio", "minio/minio", "latest")
        .WithArgs("server", "/data", "--console-address", ":9001")
        .WithEnvironment("MINIO_ROOT_USER", configuration["STORAGE_S3_ACCESS_KEY_ID"] ?? string.Empty)
        .WithEnvironment("MINIO_ROOT_PASSWORD", configuration["STORAGE_S3_SECRET_ACCESS_KEY"] ?? string.Empty)
        .WithVolume("islamu-event-minio-data", "/data")
        .WithHttpEndpoint(targetPort: 9000, port: 9005, name: "api")
        .WithHttpEndpoint(targetPort: 9001, port: 9006, name: "console")
        .WithHttpHealthCheck("/minio/health/live", endpointName: "api");
    var minioBootstrap = builder.AddContainer("minio-bootstrap", "minio/mc", "latest")
        .WithEntrypoint("sh")
        .WithArgs(
            "-c",
            $"mc alias set local {BuildHttpUri("minio", 9000)} {configuration["STORAGE_S3_ACCESS_KEY_ID"] ?? string.Empty} {configuration["STORAGE_S3_SECRET_ACCESS_KEY"] ?? string.Empty} && (mc mb -p local/{configuration["STORAGE_S3_BUCKET_NAME"] ?? "explore"} || mc ls local/{configuration["STORAGE_S3_BUCKET_NAME"] ?? "explore"} >/dev/null)")
        .WaitFor(minio);

    IResourceBuilder<ContainerResource>? svixDb = null;
    IResourceBuilder<ContainerResource>? svix = null;
    if (includeSvix)
    {
        svixDb = builder.AddContainer("svix-postgres", "postgres", "13.4")
            .WithEnvironment("POSTGRES_PASSWORD", configuration["SVIX_DB_PASSWORD"] ?? string.Empty)
            .WithEnvironment("POSTGRES_USER", configuration["SVIX_DB_USER"] ?? "postgres")
            .WithEnvironment("POSTGRES_DB", configuration["SVIX_DB_NAME"] ?? "postgres")
            .WithVolume("islamu-event-svix-postgres-data", "/var/lib/postgresql/data");

        svix = builder.AddContainer(
                "svix",
                "svix/svix-server",
                configuration["SVIX_TAG"] ?? $"v{SvixConformanceProfileRegistry.SelfHostedProviderVersion}")
            .WithEnvironment("WAIT_FOR", "true")
            .WithEnvironment(
                "SVIX_DB_DSN",
                BuildPostgresUrl(
                    configuration["SVIX_DB_USER"] ?? "postgres",
                    configuration["SVIX_DB_PASSWORD"] ?? string.Empty,
                    "svix-postgres",
                    5432,
                    configuration["SVIX_DB_NAME"] ?? "postgres",
                    string.Empty))
            .WithEnvironment("SVIX_QUEUE_TYPE", configuration["SVIX_QUEUE_TYPE"] ?? "redis")
            .WithEnvironment("SVIX_CACHE_TYPE", configuration["SVIX_CACHE_TYPE"] ?? "redis")
            .WithEnvironment("SVIX_JWT_SECRET", configuration["SVIX_JWT_SECRET"] ?? string.Empty)
            .WithHttpEndpoint(targetPort: 8071, port: 8071, name: "http")
            .WaitFor(svixDb);

        if (cache is not null)
        {
            svix = svix
                .WithEnvironment(
                    "SVIX_REDIS_DSN",
                    ReferenceExpression.Create($"redis://:{cache.Resource.PasswordParameter!}@cache:6380"))
                .WaitFor(cache);
        }
        else
        {
            svix = svix.WithEnvironment("SVIX_REDIS_DSN", "redis://cache:6379");
        }
    }

    IResourceBuilder<ContainerResource>? weblateDb = null;
    IResourceBuilder<ContainerResource>? weblate = null;
    IResourceBuilder<ContainerResource>? coop = null;
    IResourceBuilder<ContainerResource>? coopMigrations = null;
    IResourceBuilder<ContainerResource>? coopClient = null;
    IResourceBuilder<ContainerResource>? pgAdmin = null;
    IResourceBuilder<ContainerResource>? osprey = null;
    IResourceBuilder<ContainerResource>? prometheus = null;
    IResourceBuilder<ContainerResource>? grafana = null;

    if (includeHeavyExtras)
    {
        weblateDb = builder.AddContainer("weblate-postgres", "postgres", "18-alpine")
            .WithEnvironment("POSTGRES_USER", configuration["WEBLATE_POSTGRES_USER"] ?? "weblate")
            .WithEnvironment("POSTGRES_PASSWORD", configuration["WEBLATE_POSTGRES_PASSWORD"] ?? string.Empty)
            .WithEnvironment("POSTGRES_DB", configuration["WEBLATE_POSTGRES_DB"] ?? "weblate")
            .WithVolume("islamu-event-weblate-postgres-data", "/var/lib/postgresql");

        var (weblateImage, weblateTag) = ResolveImageAndTag(
            configuration["WEBLATE_IMAGE"] ?? "weblate/weblate:latest",
            configuration["WEBLATE_TAG"] ?? "latest");
        weblate = builder.AddContainer("weblate", weblateImage, weblateTag)
            .WithEnvironment("WEBLATE_SITE_DOMAIN", configuration["WEBLATE_SITE_DOMAIN"] ?? "localhost:8083")
            .WithEnvironment("WEBLATE_ADMIN_NAME", configuration["WEBLATE_ADMIN_NAME"] ?? "Admin")
            .WithEnvironment("WEBLATE_ADMIN_EMAIL", configuration["WEBLATE_ADMIN_EMAIL"] ?? "admin@openislamu.org")
            .WithEnvironment("WEBLATE_ADMIN_PASSWORD", configuration["WEBLATE_ADMIN_PASSWORD"] ?? string.Empty)
            .WithEnvironment("POSTGRES_HOST", "weblate-postgres")
            .WithEnvironment("POSTGRES_PORT", "5432")
            .WithEnvironment("POSTGRES_USER", configuration["WEBLATE_POSTGRES_USER"] ?? "weblate")
            .WithEnvironment("POSTGRES_PASSWORD", configuration["WEBLATE_POSTGRES_PASSWORD"] ?? string.Empty)
            .WithEnvironment("POSTGRES_DB", configuration["WEBLATE_POSTGRES_DB"] ?? "weblate")
            .WithHttpEndpoint(targetPort: 8080, port: 8083, name: "http")
            .WithVolume("islamu-event-weblate-data", "/app/data")
            .WaitFor(weblateDb);

        if (cache is not null)
        {
            weblate = weblate
                .WithEnvironment("VALKEY_HOST", "cache")
                .WithEnvironment("VALKEY_PORT", "6380")
                .WithEnvironment("REDIS_HOST", "cache")
                .WithEnvironment("REDIS_PORT", "6380")
                .WithEnvironment("REDIS_PASSWORD", cache.Resource.PasswordParameter!)
                .WaitFor(cache);
        }

        var coopDb = builder.AddContainer("coop-postgres", "postgres", "18-alpine")
            .WithEnvironment("POSTGRES_USER", configuration["COOP_DATABASE_USER"] ?? "coop")
            .WithEnvironment("POSTGRES_PASSWORD", configuration["COOP_DATABASE_PASSWORD"] ?? string.Empty)
            .WithEnvironment("POSTGRES_DB", configuration["COOP_DATABASE_NAME"] ?? "coop")
            .WithVolume("islamu-event-coop-postgres-data", "/var/lib/postgresql");

        var (coopMigrationsImage, coopMigrationsTag) = ResolveImageAndTag(
            builder.Configuration["COOP_MIGRATIONS_IMAGE"] ?? "ghcr.io/roostorg/coop-migrations",
            "latest");
        coopMigrations = builder.AddContainer("coop-migrations", coopMigrationsImage, coopMigrationsTag)
            .WithEntrypoint("sh")
            .WithArgs(
                "-c",
                "npm run db:create -- --db api-server-pg --env staging && npm run db:update -- --db api-server-pg --env staging")
            .WithEnvironment("DATABASE_HOST", "coop-postgres")
            .WithEnvironment("DATABASE_READ_ONLY_HOST", "coop-postgres")
            .WithEnvironment("DATABASE_PORT", "5432")
            .WithEnvironment("DATABASE_USER", builder.Configuration["COOP_DATABASE_USER"] ?? "coop")
            .WithEnvironment("DATABASE_PASSWORD", builder.Configuration["COOP_DATABASE_PASSWORD"] ?? string.Empty)
            .WithEnvironment("DATABASE_NAME", builder.Configuration["COOP_DATABASE_NAME"] ?? "coop")
            .WithEnvironment("API_SERVER_DATABASE_HOST", "coop-postgres")
            .WithEnvironment("API_SERVER_DATABASE_PORT", "5432")
            .WithEnvironment("API_SERVER_DATABASE_USER", builder.Configuration["COOP_DATABASE_USER"] ?? "coop")
            .WithEnvironment("API_SERVER_DATABASE_PASSWORD", builder.Configuration["COOP_DATABASE_PASSWORD"] ?? string.Empty)
            .WithEnvironment("API_SERVER_DATABASE_NAME", builder.Configuration["COOP_DATABASE_NAME"] ?? "coop")
            .WithEnvironment("SCYLLA_HOSTS", builder.Configuration["COOP_SCYLLA_HOSTS"] ?? "coop-scylla")
            .WithEnvironment("SCYLLA_USERNAME", builder.Configuration["COOP_SCYLLA_USERNAME"] ?? "cassandra")
            .WithEnvironment("SCYLLA_PASSWORD", builder.Configuration["COOP_SCYLLA_PASSWORD"] ?? string.Empty)
            .WithEnvironment("SCYLLA_LOCAL_DATACENTER", builder.Configuration["COOP_SCYLLA_LOCAL_DATACENTER"] ?? "datacenter1")
            .WithEnvironment("SCYLLA_SSL", builder.Configuration["COOP_SCYLLA_SSL"] ?? "false")
            .WaitFor(coopDb);

        var (coopImage, coopTag) = ResolveImageAndTag(
            builder.Configuration["COOP_IMAGE"] ?? "ghcr.io/roostorg/coop-server",
            "latest");
        coop = builder.AddContainer("coop", coopImage, coopTag)
            .WithEnvironment("NODE_ENV", builder.Configuration["COOP_NODE_ENV"] ?? "development")
            .WithEnvironment("OTEL_SERVICE_NAME", builder.Configuration["COOP_OTEL_SERVICE_NAME"] ?? "coop")
            .WithEnvironment("PORT", "8080")
            .WithEnvironment("UI_URL", builder.Configuration["COOP_UI_URL"] ?? BuildHttpUri("localhost", 3001))
            .WithEnvironment("SESSION_SECRET", builder.Configuration["COOP_SESSION_SECRET"] ?? string.Empty)
            .WithEnvironment("DATABASE_HOST", "coop-postgres")
            .WithEnvironment("DATABASE_READ_ONLY_HOST", "coop-postgres")
            .WithEnvironment("DATABASE_PORT", "5432")
            .WithEnvironment("DATABASE_USER", builder.Configuration["COOP_DATABASE_USER"] ?? "coop")
            .WithEnvironment("DATABASE_PASSWORD", builder.Configuration["COOP_DATABASE_PASSWORD"] ?? string.Empty)
            .WithEnvironment("DATABASE_NAME", builder.Configuration["COOP_DATABASE_NAME"] ?? "coop")
            .WithEnvironment("WAREHOUSE_ADAPTER", builder.Configuration["COOP_WAREHOUSE_ADAPTER"] ?? "noop")
            .WithEnvironment("ANALYTICS_ADAPTER", builder.Configuration["COOP_ANALYTICS_ADAPTER"] ?? "noop")
            .WithEnvironment("SCYLLA_HOSTS", builder.Configuration["COOP_SCYLLA_HOSTS"] ?? "coop-scylla")
            .WithEnvironment("SCYLLA_USERNAME", builder.Configuration["COOP_SCYLLA_USERNAME"] ?? "cassandra")
            .WithEnvironment("SCYLLA_PASSWORD", builder.Configuration["COOP_SCYLLA_PASSWORD"] ?? string.Empty)
            .WithEnvironment("SCYLLA_LOCAL_DATACENTER", builder.Configuration["COOP_SCYLLA_LOCAL_DATACENTER"] ?? "datacenter1")
            .WithEnvironment("SCYLLA_SSL", builder.Configuration["COOP_SCYLLA_SSL"] ?? "false")
            .WithHttpEndpoint(targetPort: 8080, port: 8082, name: "http")
            .WaitFor(coopDb)
            .WaitForCompletion(coopMigrations);

        if (cache is not null)
        {
            coop = coop
                .WithEnvironment("REDIS_USE_CLUSTER", "false")
                .WithEnvironment("REDIS_HOST", "cache")
                .WithEnvironment("REDIS_PORT", "6380")
                .WithEnvironment("REDIS_PASSWORD", cache.Resource.PasswordParameter!)
                .WaitFor(cache);
        }

        var (coopClientImage, coopClientTag) = ResolveImageAndTag(
            builder.Configuration["COOP_CLIENT_IMAGE"] ?? "ghcr.io/roostorg/coop-client",
            "latest");
        coopClient = builder.AddContainer("coop-client", coopClientImage, coopClientTag)
            .WithBindMount(coopNginxConfigPath, "/etc/nginx/conf.d/default.conf", isReadOnly: true)
            .WithHttpEndpoint(targetPort: 80, port: 3001, name: "http")
            .WaitFor(coop);

        pgAdmin = builder.AddContainer("pgadmin", "dpage/pgadmin4", "latest")
            .WithEnvironment("PGADMIN_DEFAULT_EMAIL", "admin@openislamu.org")
            .WithEnvironment("PGADMIN_DEFAULT_PASSWORD", configuration["PGADMIN_DEFAULT_PASSWORD"] ?? string.Empty)
            .WithEnvironment("PGADMIN_DISABLE_POSTFIX", "true")
            .WithEnvironment("PGADMIN_SERVER_JSON_FILE", "/pgadmin4/servers.json")
            .WithEnvironment("PGADMIN_REPLACE_SERVERS_ON_STARTUP", "True")
            .WithEnvironment("PGPASS_FILE", "/pgadmin4/pgpass")
            .WithBindMount(pgAdminServersPath, "/pgadmin4/servers.json", isReadOnly: true)
            .WithBindMount(pgAdminPassFilePath, "/pgadmin4/pgpass", isReadOnly: true)
            .WithVolume("islamu-event-pgadmin-data", "/var/lib/pgadmin")
            .WithHttpEndpoint(targetPort: 80, port: 5050, name: "http")
            .WaitFor(cerbosDb)
            .WaitFor(weblateDb)
            .WaitFor(coopDb);

        if (svixDb is not null)
        {
            pgAdmin = pgAdmin.WaitFor(svixDb);
        }

        if (appDatabase is not null)
        {
            pgAdmin = pgAdmin.WaitFor(appDatabase);
        }

        var ospreyKafka = builder.AddContainer("osprey-kafka", "confluentinc/cp-kafka", "7.4.0")
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", "1@osprey-kafka:29093")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "INTERNAL")
            .WithEnvironment("KAFKA_LISTENERS", "INTERNAL://osprey-kafka:29092,CONTROLLER://osprey-kafka:29093")
            .WithEnvironment("KAFKA_ADVERTISED_LISTENERS", "INTERNAL://osprey-kafka:29092")
            .WithEnvironment("KAFKA_LISTENER_SECURITY_PROTOCOL_MAP", "INTERNAL:PLAINTEXT,CONTROLLER:PLAINTEXT")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .WithEnvironment("CLUSTER_ID", "P45WxmmWSe2CrdGoeJMcKg");

        var ospreyKafkaBootstrap = builder.AddContainer("osprey-kafka-bootstrap", "confluentinc/cp-kafka", "7.4.0")
            .WithEntrypoint("bash")
            .WithArgs(
                "-c",
                "until kafka-topics --bootstrap-server osprey-kafka:29092 --list >/dev/null 2>&1; do sleep 1; done; kafka-topics --bootstrap-server osprey-kafka:29092 --create --if-not-exists --topic osprey.actions_input --partitions 3 --replication-factor 1")
            .WaitFor(ospreyKafka);

        var (ospreyImage, ospreyTag) = ResolveImageAndTag(
            configuration["OSPREY_IMAGE"] ?? "ghcr.io/roostorg/osprey/osprey-coordinator:latest",
            configuration["OSPREY_TAG"] ?? "latest");
        osprey = builder.AddContainer("osprey", ospreyImage, ospreyTag)
            .WithEnvironment("RUST_LOG", configuration["OSPREY_RUST_LOG"] ?? "info")
            .WithEnvironment("OSPREY_COORDINATOR_CONSUMER_TYPE", "kafka")
            .WithEnvironment("OSPREY_KAFKA_BOOTSTRAP_SERVERS", "osprey-kafka:29092")
            .WithEnvironment("OSPREY_KAFKA_INPUT_STREAM_TOPIC", "osprey.actions_input")
            .WithEnvironment("OSPREY_KAFKA_GROUP_ID", "osprey_coordinator_group")
            .WithEnvironment("OSPREY_COORDINATOR_BIDI_STREAM_PORT", "19950")
            .WithEnvironment("OSPREY_COORDINATOR_SYNC_ACTION_PORT", "19951")
            .WithEnvironment("POD_IP", "osprey")
            .WithEndpoint(targetPort: 19950, port: 19950, name: "bidi-stream", protocol: ProtocolType.Tcp)
            .WithEndpoint(targetPort: 19951, port: 19951, name: "sync-action", protocol: ProtocolType.Tcp)
            .WaitForCompletion(ospreyKafkaBootstrap);

        prometheus = builder.AddContainer("prometheus", "prom/prometheus", "v3.2.1")
            .WithBindMount(prometheusConfigPath, "/etc/prometheus/prometheus.yaml", isReadOnly: true)
            .WithBindMount(admissionCheckInAlertRulesPath, "/etc/prometheus/admission-check-in-alerts.yaml", isReadOnly: true)
            .WithArgs("--web.enable-otlp-receiver", "--config.file=/etc/prometheus/prometheus.yaml")
            .WithHttpEndpoint(targetPort: 9090, port: 9090, name: "http")
            .WithVolume("islamu-event-prometheus-data", "/prometheus");

        grafana = builder.AddContainer("grafana", "grafana/grafana", "latest")
            .WithBindMount(grafanaDashboardPath, "/var/lib/grafana/dashboards", isReadOnly: true)
            .WithEnvironment("PROMETHEUS_ENDPOINT", BuildHttpUri("prometheus", 9090))
            .WithHttpEndpoint(targetPort: 3000, port: 3000, name: "http")
            .WithVolume("islamu-event-grafana-data", "/var/lib/grafana")
            .WaitFor(prometheus);
    }

    return new LocalPlatformResources(
        Keycloak: keycloak,
        KeycloakInit: keycloakInit,
        KeycloakBlazorClientSecret: keycloakBlazorClientSecret,
        Cerbos: cerbos,
        Minio: minio,
        MinioBootstrap: minioBootstrap,
        Svix: svix,
        WeblateDb: weblateDb,
        Weblate: weblate,
        Coop: coop,
        CoopMigrations: coopMigrations,
        CoopClient: coopClient,
        PgAdmin: pgAdmin,
        Osprey: osprey,
        Prometheus: prometheus,
        Grafana: grafana);
}

static IResourceBuilder<ContainerResource> AddMailpit(IDistributedApplicationBuilder builder)
{
    return builder.AddContainer("mailpit", "axllent/mailpit", builder.Configuration["MAILPIT_TAG"] ?? "latest")
        .WithEnvironment("MP_MAX_MESSAGES", builder.Configuration["MAILPIT_MAX_MESSAGES"] ?? "5000")
        .WithEnvironment("MP_DATABASE", "/data/mailpit.db")
        .WithEnvironment("MP_SMTP_AUTH_ACCEPT_ANY", "1")
        .WithEnvironment("MP_SMTP_AUTH_ALLOW_INSECURE", "1")
        .WithEnvironment("MP_DISABLE_VERSION_CHECK", "true")
        .WithVolume("islamu-event-mailpit-data", "/data")
        .WithEndpoint(
            targetPort: 1025,
            port: 1025,
            name: "smtp",
            protocol: ProtocolType.Tcp)
        .WithHttpEndpoint(targetPort: 8025, port: 8025, name: "http");
}

static void AddLocalFormbricks(IDistributedApplicationBuilder builder)
{
    const string formbricksImage = "ghcr.io/formbricks/formbricks";
    const string formbricksTag = "5.2.2@sha256:d6f635714a9c29620203ba29762f4746f5def86c346d6ad95fefa66adc21fd5e";
    const string hubImage = "ghcr.io/formbricks/hub";
    const string hubTag = "latest@sha256:4dc0c4f26cf999b3bf4a26d7b09634fc65ae23cbb30c9ad82042da019d231458";

    IResourceBuilder<ParameterResource> Secret(string name, string key) =>
        builder.AddParameter(name, () => builder.Configuration[key] ?? string.Empty, publishValueAsDefault: false, secret: true);

    var nextAuthSecret = Secret("formbricks-nextauth-secret", "FORMBRICKS_NEXTAUTH_SECRET");
    var encryptionKey = Secret("formbricks-encryption-key", "FORMBRICKS_ENCRYPTION_KEY");
    var cronSecret = Secret("formbricks-cron-secret", "FORMBRICKS_CRON_SECRET");
    var hubApiKey = Secret("formbricks-hub-api-key", "FORMBRICKS_HUB_API_KEY");
    var cubeApiSecret = Secret("formbricks-cube-api-secret", "FORMBRICKS_CUBEJS_API_SECRET");

    var postgres = builder.AddContainer("formbricks-postgres", "pgvector/pgvector", "pg18")
        .WithEnvironment("POSTGRES_USER", "postgres")
        .WithEnvironment("POSTGRES_PASSWORD", builder.Configuration["FORMBRICKS_DATABASE_PASSWORD"] ?? string.Empty)
        .WithEnvironment("POSTGRES_DB", "formbricks")
        .WithVolume("islamu-event-formbricks-postgres-data", "/var/lib/postgresql");

    var redis = builder.AddContainer(
            "formbricks-redis",
            "valkey/valkey@sha256:12ba4f45a7c3e1d0f076acd616cb230834e75a77e8516dde382720af32832d6d")
        .WithArgs("valkey-server", "--appendonly", "yes", "--maxmemory-policy", "noeviction")
        .WithVolume("islamu-event-formbricks-redis-data", "/data");

    var databaseUrl = BuildPostgresUrl(
        "postgres",
        "postgres",
        "formbricks-postgres",
        5432,
        "formbricks",
        "schema=public");

    var hubDatabaseUrl = BuildPostgresUrl(
        "postgres",
        "postgres",
        "formbricks-postgres",
        5432,
        "formbricks",
        "sslmode=disable");

    IResourceBuilder<ContainerResource> Environment(IResourceBuilder<ContainerResource> resource) => resource
        .WithEnvironment("WEBAPP_URL", builder.Configuration["FORMBRICKS_WEBAPP_URL"] ?? BuildHttpUri("localhost", 3005))
        .WithEnvironment("NEXTAUTH_URL", builder.Configuration["FORMBRICKS_WEBAPP_URL"] ?? BuildHttpUri("localhost", 3005))
        .WithEnvironment("DATABASE_URL", databaseUrl)
        .WithEnvironment("NEXTAUTH_SECRET", nextAuthSecret)
        .WithEnvironment("ENCRYPTION_KEY", encryptionKey)
        .WithEnvironment("CRON_SECRET", cronSecret)
        .WithEnvironment("REDIS_URL", "redis://formbricks-redis:6379")
        .WithEnvironment("HUB_API_KEY", hubApiKey)
        .WithEnvironment("HUB_API_URL", BuildHttpUri("formbricks-hub", 8080))
        .WithEnvironment("CUBEJS_API_URL", BuildHttpUri("formbricks-cube", 4000))
        .WithEnvironment("CUBEJS_API_SECRET", cubeApiSecret)
        .WithEnvironment("CUBEJS_JWT_ISSUER", "formbricks-web")
        .WithEnvironment("CUBEJS_JWT_AUDIENCE", "formbricks-cube")
        .WithEnvironment("EMAIL_VERIFICATION_DISABLED", "1")
        .WithEnvironment("PASSWORD_RESET_DISABLED", "1")
        .WithEnvironment("TELEMETRY_DISABLED", "1");

    var migrate = Environment(builder.AddContainer("formbricks-migrate", formbricksImage, formbricksTag))
        .WithEntrypoint("sh")
        .WithArgs("-c", "node /home/nextjs/validate-env.mjs && node packages/database/dist/scripts/apply-migrations.js")
        .WaitFor(postgres);

    var hubMigrate = builder.AddContainer("formbricks-hub-migrate", hubImage, hubTag)
        .WithEntrypoint("sh")
        .WithArgs("-c", "goose -dir /app/migrations postgres \"$DATABASE_URL\" up && river migrate-up --database-url \"$DATABASE_URL\"")
        .WithEnvironment("DATABASE_URL", hubDatabaseUrl)
        .WaitForCompletion(migrate);

    var hub = builder.AddContainer("formbricks-hub", hubImage, hubTag)
        .WithEnvironment("API_KEY", hubApiKey)
        .WithEnvironment("DATABASE_URL", hubDatabaseUrl)
        .WaitForCompletion(hubMigrate);

    var cube = builder.AddContainer("formbricks-cube", "cubejs/cube", "v1.6.6")
        .WithEnvironment("CUBEJS_DB_TYPE", "postgres")
        .WithEnvironment("CUBEJS_DB_HOST", "formbricks-postgres")
        .WithEnvironment("CUBEJS_DB_NAME", "formbricks")
        .WithEnvironment("CUBEJS_DB_USER", "postgres")
        .WithEnvironment("CUBEJS_DB_PASS", "postgres")
        .WithEnvironment("CUBEJS_DB_PORT", "5432")
        .WithEnvironment("CUBEJS_API_SECRET", cubeApiSecret)
        .WithEnvironment("CUBEJS_JWT_ISSUER", "formbricks-web")
        .WithEnvironment("CUBEJS_JWT_AUDIENCE", "formbricks-cube")
        .WithEnvironment("CUBEJS_DEFAULT_API_SCOPES", "meta,data")
        .WithEnvironment("CUBEJS_CACHE_AND_QUEUE_DRIVER", "memory")
        .WithHttpEndpoint(targetPort: 4000, name: "http")
        .WithHttpHealthCheck("/readyz", endpointName: "http")
        .WaitForCompletion(hubMigrate);

    Environment(builder.AddContainer("formbricks", formbricksImage, formbricksTag))
        .WithEnvironment("SKIP_STARTUP_MIGRATION", "true")
        .WithHttpEndpoint(targetPort: 3000, port: 3005, name: "http")
        .WaitForCompletion(migrate)
        .WaitFor(redis)
        .WaitFor(hub)
        .WaitFor(cube);
}

static void ExcludeProjectLaunchProfile(ProjectResourceOptions options)
{
    options.ExcludeLaunchProfile = true;
}

static IResourceBuilder<ProjectResource> ConfigureLocalMailpitSmtp(
    IResourceBuilder<ProjectResource> project,
    IResourceBuilder<ContainerResource> mailpit,
    IConfiguration configuration)
{
    var smtpHost = EndpointHost(mailpit, "smtp");
    var smtpPort = EndpointPort(mailpit, "smtp");

    return project
        .WithEnvironment("MAIL_SMTP_HOST", smtpHost)
        .WithEnvironment("MAIL_SMTP_PORT", smtpPort)
        .WithEnvironment("MAIL_SMTP_USERNAME", configuration["MAIL_SMTP_USERNAME"] ?? string.Empty)
        .WithEnvironment("MAIL_SMTP_PASSWORD", configuration["MAIL_SMTP_PASSWORD"] ?? string.Empty)
        .WithEnvironment("MAIL_SMTP_ENCRYPTION", configuration["MAIL_SMTP_ENCRYPTION"] ?? "None")
        .WithEnvironment("MAIL_SMTP_FROM_ADDRESS", configuration["MAIL_SMTP_FROM_ADDRESS"] ?? "noreply@localhost")
        .WithEnvironment("MAIL_SMTP_FROM_NAME", configuration["MAIL_SMTP_FROM_NAME"] ?? "ISLAMU Event Dev");
}

static IResourceBuilder<ProjectResource> ConfigureGeocoding(
    IResourceBuilder<ProjectResource> project,
    IConfiguration configuration) =>
    project
        .WithEnvironment("GEOCODING_PROVIDER", ConfiguredValue(
            configuration,
            "GEOCODING_PROVIDER",
            "None"))
        .WithEnvironment("GEOCODING_ENDPOINT", ConfiguredValue(
            configuration,
            "GEOCODING_ENDPOINT",
            string.Empty))
        .WithEnvironment("GEOCODING_LANGUAGE", ConfiguredValue(
            configuration,
            "GEOCODING_LANGUAGE",
            "en"))
        .WithEnvironment("GEOCODING_COUNTRY_CODES", ConfiguredValue(
            configuration,
            "GEOCODING_COUNTRY_CODES",
            string.Empty))
        .WithEnvironment("GEOCODING_DATASET_VERSION", ConfiguredValue(
            configuration,
            "GEOCODING_DATASET_VERSION",
            string.Empty))
        .WithEnvironment("GEOCODING_MAXIMUM_RESULTS", ConfiguredValue(
            configuration,
            "GEOCODING_MAXIMUM_RESULTS",
            "10"))
        .WithEnvironment("GEOCODING_MAXIMUM_RESPONSE_BYTES", ConfiguredValue(
            configuration,
            "GEOCODING_MAXIMUM_RESPONSE_BYTES",
            "65536"))
        .WithEnvironment("GEOCODING_TOTAL_TIMEOUT_MILLISECONDS", ConfiguredValue(
            configuration,
            "GEOCODING_TOTAL_TIMEOUT_MILLISECONDS",
            "5000"))
        .WithEnvironment("GEOCODING_MAXIMUM_RETRY_COUNT", ConfiguredValue(
            configuration,
            "GEOCODING_MAXIMUM_RETRY_COUNT",
            "2"))
        .WithEnvironment("GEOCODING_RETRY_DELAYS_MILLISECONDS", ConfiguredValue(
            configuration,
            "GEOCODING_RETRY_DELAYS_MILLISECONDS",
            "200,500"))
        .WithEnvironment("GEOCODING_READINESS_TIMEOUT_MILLISECONDS", ConfiguredValue(
            configuration,
            "GEOCODING_READINESS_TIMEOUT_MILLISECONDS",
            "2000"))
        .WithEnvironment("GEOCODING_SELECTION_LIFETIME_SECONDS", ConfiguredValue(
            configuration,
            "GEOCODING_SELECTION_LIFETIME_SECONDS",
            "300"));

static IResourceBuilder<ProjectResource> ConfigureLocalPlatformApi(
    IResourceBuilder<ProjectResource> api,
    LocalPlatformResources resources,
    IConfiguration configuration)
{
    var keycloakRealm = configuration["KEYCLOAK_REALM"] ?? "ISLAMU";
    var keycloakApiClientId = configuration["KEYCLOAK_API_CLIENT_ID"] ?? "islamu-event-api";
    var keycloakBlazorClientId = configuration["KEYCLOAK_BLAZOR_CLIENT_ID"] ?? "islamu-event-blazor";
    var authorizationProvider = configuration["AUTHORIZATION_PROVIDER"] ?? "cerbos";
    var cerbosAdminUsername = ConfiguredValue(configuration, "CERBOS_ADMIN_USERNAME", string.Empty);
    var cerbosAdminPassword = ConfiguredValue(configuration, "CERBOS_ADMIN_PASSWORD", string.Empty);
    var cerbosAdminCredentialHash = ConfiguredValue(
        configuration,
        "CERBOS_ADMIN_PASSWORD_HASH",
        string.Empty);
    var keycloakBaseUrl = EndpointUrl(resources.Keycloak, "http", "/auth");
    var keycloakAuthority = EndpointUrl(resources.Keycloak, "http", $"/auth/realms/{keycloakRealm}");
    var keycloakMetadataAddress = EndpointUrl(
        resources.Keycloak,
        "http",
        $"/auth/realms/{keycloakRealm}/.well-known/openid-configuration");
    var cerbosGrpcEndpoint = HttpEndpointFromHostAndPort(resources.Cerbos, "grpc");
    var cerbosHttpEndpoint = EndpointUrl(resources.Cerbos, "http");
    var webhookProvider = ConfiguredValue(
        configuration,
        "WEBHOOKS_PROVIDER",
        WebhookOptions.ProviderLocal);

    api = api
        .WithEnvironment("KEYCLOAK_REALM", keycloakRealm)
        .WithEnvironment("KEYCLOAK_ENDPOINT", keycloakBaseUrl)
        .WithEnvironment("KEYCLOAK_BLAZOR_CLIENT_ID", keycloakBlazorClientId)
        .WithEnvironment("KEYCLOAK_BLAZOR_CLIENT_SECRET", resources.KeycloakBlazorClientSecret)
        .WithEnvironment("Keycloak__Realm", keycloakRealm)
        .WithEnvironment("Keycloak__Authority", keycloakAuthority)
        .WithEnvironment("Keycloak__MetadataAddress", keycloakMetadataAddress)
        .WithEnvironment("Keycloak__RequireHttpsMetadata", "false")
        .WithEnvironment("Keycloak__Audience", keycloakApiClientId)
        .WithEnvironment("Keycloak__ValidAudiences__0", keycloakApiClientId)
        .WithEnvironment("Keycloak__ValidAudiences__1", keycloakBlazorClientId)
        .WithEnvironment("KeycloakBootstrap__AllowLocalUrls", "true")
        .WithEnvironment("AUTHORIZATION_PROVIDER", authorizationProvider)
        .WithEnvironment("Cerbos__GrpcEndpoint", cerbosGrpcEndpoint)
        .WithEnvironment("CERBOS_GRPC_ENDPOINT", cerbosGrpcEndpoint)
        .WithEnvironment("Cerbos__HttpEndpoint", cerbosHttpEndpoint)
        .WithEnvironment("Cerbos__UseTls", "false")
        .WithEnvironment("Cerbos__PlaintextMode", "true")
        .WithEnvironment("Cerbos__AdminApi__Endpoints__0", cerbosHttpEndpoint)
        .WithEnvironment("Cerbos__AdminPasswordHash", cerbosAdminCredentialHash)
        .WithEnvironment("CERBOS_ADMIN_USERNAME", cerbosAdminUsername)
        .WithEnvironment("CERBOS_ADMIN_PASSWORD", cerbosAdminPassword)
        .WithEnvironment("STORAGE_S3_ACCESS_KEY_ID", configuration["STORAGE_S3_ACCESS_KEY_ID"] ?? string.Empty)
        .WithEnvironment("STORAGE_S3_SECRET_ACCESS_KEY", configuration["STORAGE_S3_SECRET_ACCESS_KEY"] ?? string.Empty)
        .WithEnvironment("Reporting__Enabled", configuration["REPORTING_ENABLED"] ?? "true")
        .WithEnvironment("Reporting__Mode", configuration["REPORTING_MODE"] ?? "LocalOnly")
        .WithEnvironment("Reporting__SyncReports", configuration["REPORTING_SYNC_REPORTS"] ?? "true")
        .WithEnvironment("Reporting__EvaluateSignals", configuration["REPORTING_EVALUATE_SIGNALS"] ?? "false")
        .WithEnvironment("Reporting__MirrorReviewQueue", configuration["REPORTING_MIRROR_REVIEW_QUEUE"] ?? "true")
        .WithEnvironment("Reporting__ExecuteDecisions", configuration["REPORTING_EXECUTE_DECISIONS"] ?? "true")
        .WithEnvironment("Reporting__Osprey__Enabled", resources.Osprey is not null ? (configuration["REPORTING_OSPREY_ENABLED"] ?? "false") : "false")
        .WithEnvironment("Reporting__Osprey__AllowLocalProviderEndpoints", configuration["REPORTING_OSPREY_ALLOW_LOCAL_PROVIDER_ENDPOINTS"] ?? "true")
        .WithEnvironment("Reporting__Coop__ApiKey", configuration["REPORTING_COOP_API_KEY"] ?? string.Empty)
        .WithEnvironment("Reporting__Coop__AllowLocalProviderEndpoints", configuration["REPORTING_COOP_ALLOW_LOCAL_PROVIDER_ENDPOINTS"] ?? "true")
        .WithEnvironment("Reporting__Coop__WebhookSecret", configuration["REPORTING_COOP_WEBHOOK_SECRET"] ?? string.Empty)
        .WithEnvironment("Webhooks__Enabled", configuration["WEBHOOKS_ENABLED"] ?? "true")
        .WithEnvironment("Webhooks__Provider", webhookProvider);

    if (resources.Svix is not null)
    {
        api = api
            .WithEnvironment("Webhooks__Svix__BaseUrl", EndpointUrl(resources.Svix, "http"))
            .WithEnvironment("Webhooks__Svix__Environment", SvixConformanceProfileRegistry.SelfHostedEnvironment)
            .WithEnvironment("Webhooks__Svix__ProviderVersion", SvixConformanceProfileRegistry.SelfHostedProviderVersion)
            .WithEnvironment("Webhooks__Svix__CapabilityPolicyVersion", SvixConformanceProfileRegistry.SelfHostedCapabilityPolicyVersion)
            .WithEnvironment("Webhooks__Svix__AuthTokenSecretRef", "webhooks.svix.auth_token")
            .WithEnvironment("Webhooks__Svix__OperationalWebhookSecretRef", "webhooks.svix.operational_webhook_secret")
            .WithEnvironment("WEBHOOKS_SVIX_AUTH_TOKEN", configuration["WEBHOOKS_SVIX_AUTH_TOKEN"] ?? string.Empty)
            .WithEnvironment("WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET", configuration["WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET"] ?? string.Empty);
    }

    api = resources.Coop is not null
        ? api
            .WithEnvironment("Reporting__Coop__Enabled", configuration["REPORTING_COOP_ENABLED"] ?? "true")
            .WithEnvironment("Reporting__Coop__EndpointUrl", EndpointUrl(resources.Coop, "http"))
            .WaitFor(resources.Coop)
        // Coop isn't provisioned outside local-full: force-disable regardless of any REPORTING_COOP_ENABLED
        // value in .env, since there is no Coop container/endpoint for this profile to point at.
        : api.WithEnvironment("Reporting__Coop__Enabled", "false");

    if (resources.Weblate is not null)
    {
        api = api.WaitFor(resources.Weblate);
    }

    api = api
        .WaitFor(resources.Cerbos)
        .WaitForCompletion(resources.KeycloakInit)
        .WaitForCompletion(resources.MinioBootstrap);

    if (resources.Svix is not null)
    {
        api = api.WaitFor(resources.Svix);
    }

    if (resources.Prometheus is not null)
    {
        api = api
            .WithEnvironment(
                "OTEL_EXPORTER_OTLP_ENDPOINT",
                $"{BuildHttpUri("prometheus", 9090)}/api/v1/otlp")
            .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
            .WaitFor(resources.Prometheus);
    }

    return api;
}

static IResourceBuilder<ProjectResource> ConfigureLocalPlatformBlazor(
    IResourceBuilder<ProjectResource> blazor,
    LocalPlatformResources resources,
    IConfiguration configuration)
{
    var keycloakRealm = configuration["KEYCLOAK_REALM"] ?? "ISLAMU";
    var keycloakClientId = configuration["KEYCLOAK_BLAZOR_CLIENT_ID"] ?? "islamu-event-blazor";
    var keycloakBaseUrl = EndpointUrl(resources.Keycloak, "http", "/auth");
    var keycloakAuthority = EndpointUrl(resources.Keycloak, "http", $"/auth/realms/{keycloakRealm}");
    var keycloakMetadataAddress = EndpointUrl(
        resources.Keycloak,
        "http",
        $"/auth/realms/{keycloakRealm}/.well-known/openid-configuration");

    return blazor
        .WithEnvironment("KEYCLOAK_REALM", keycloakRealm)
        .WithEnvironment("KEYCLOAK_ENDPOINT", keycloakBaseUrl)
        .WithEnvironment("KEYCLOAK_CLIENT_ID", keycloakClientId)
        .WithEnvironment("KEYCLOAK_BLAZOR_CLIENT_SECRET", resources.KeycloakBlazorClientSecret)
        .WithEnvironment("Keycloak__Realm", keycloakRealm)
        .WithEnvironment("Keycloak__Authority", keycloakAuthority)
        .WithEnvironment("Keycloak__MetadataAddress", keycloakMetadataAddress)
        .WithEnvironment("Keycloak__ClientId", keycloakClientId)
        .WithEnvironment("Keycloak__ClientSecret", resources.KeycloakBlazorClientSecret)
        .WithEnvironment("Keycloak__RequireHttpsMetadata", "false")
        .WaitFor(resources.Keycloak)
        .WaitForCompletion(resources.KeycloakInit);
}

static IResourceBuilder<ContainerResource> ConfigureLocalKeycloakCallbacks(
    IResourceBuilder<ContainerResource> keycloakInit,
    IResourceBuilder<ProjectResource> exploreBlazor,
    IConfiguration configuration)
{
    var httpPort = exploreBlazor
        .GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork)
        .Property(EndpointProperty.Port);
    var httpsPort = exploreBlazor
        .GetEndpoint("https", KnownNetworkIdentifiers.LocalhostNetwork)
        .Property(EndpointProperty.Port);

    var redirectUris = configuration["KEYCLOAK_BLAZOR_REDIRECT_URIS"];
    keycloakInit = string.IsNullOrWhiteSpace(redirectUris)
        ? keycloakInit.WithEnvironment(
            "KEYCLOAK_BLAZOR_REDIRECT_URIS",
            ReferenceExpression.Create($"[\"http://localhost:{httpPort}/signin-oidc\",\"http://admin.localhost:{httpPort}/signin-oidc\",\"https://localhost:{httpsPort}/signin-oidc\",\"https://admin.localhost:{httpsPort}/signin-oidc\"]"))
        : keycloakInit.WithEnvironment("KEYCLOAK_BLAZOR_REDIRECT_URIS", redirectUris);

    var webOrigins = configuration["KEYCLOAK_BLAZOR_WEB_ORIGINS"];
    keycloakInit = string.IsNullOrWhiteSpace(webOrigins)
        ? keycloakInit.WithEnvironment(
            "KEYCLOAK_BLAZOR_WEB_ORIGINS",
            ReferenceExpression.Create($"[\"http://localhost:{httpPort}\",\"http://admin.localhost:{httpPort}\",\"https://localhost:{httpsPort}\",\"https://admin.localhost:{httpsPort}\"]"))
        : keycloakInit.WithEnvironment("KEYCLOAK_BLAZOR_WEB_ORIGINS", webOrigins);

    var logoutRedirectUris = configuration["KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS"];
    return string.IsNullOrWhiteSpace(logoutRedirectUris)
        ? keycloakInit.WithEnvironment(
            "KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS",
            ReferenceExpression.Create($"http://localhost:{httpPort}/signout-callback-oidc##http://admin.localhost:{httpPort}/signout-callback-oidc##https://localhost:{httpsPort}/signout-callback-oidc##https://admin.localhost:{httpsPort}/signout-callback-oidc"))
        : keycloakInit.WithEnvironment("KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS", logoutRedirectUris);
}

static ReferenceExpression EndpointUrl(
    IResourceBuilder<ContainerResource> resource,
    string endpointName,
    string path = "")
{
    var endpoint = resource.GetEndpoint(endpointName);
    return ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.Url)}{path}");
}

static ReferenceExpression EndpointHost(
    IResourceBuilder<ContainerResource> resource,
    string endpointName)
{
    var endpoint = resource.GetEndpoint(endpointName);
    return ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.Host)}");
}

static ReferenceExpression EndpointPort(
    IResourceBuilder<ContainerResource> resource,
    string endpointName)
{
    var endpoint = resource.GetEndpoint(endpointName);
    return ReferenceExpression.Create($"{endpoint.Property(EndpointProperty.Port)}");
}

static ReferenceExpression HttpEndpointFromHostAndPort(
    IResourceBuilder<ContainerResource> resource,
    string endpointName)
{
    var endpoint = resource.GetEndpoint(endpointName);
    return ReferenceExpression.Create($"http://{endpoint.Property(EndpointProperty.HostAndPort)}");
}

static string BuildHttpUri(string host, int port, string path = "")
{
    return BuildUri(Uri.UriSchemeHttp, host, port.ToString(System.Globalization.CultureInfo.InvariantCulture), path);
}

static string BuildDefaultHttpUri(string host, int port, string path = "")
{
    return BuildHttpUri(host, port, path);
}

static string BuildUri(string scheme, string host, string port, string path = "", string query = "")
{
    var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
    var normalizedQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : $"?{query.TrimStart('?')}";
    var normalizedPort = string.IsNullOrWhiteSpace(port) ? string.Empty : $":{port}";
    return $"{scheme}://{host}{normalizedPort}{normalizedPath}{normalizedQuery}";
}

static string BuildPostgresUrl(string username, string password, string host, int port, string database, string query = "")
{
    var normalizedQuery = string.IsNullOrWhiteSpace(query) ? string.Empty : $"?{query.TrimStart('?')}";
    return $"postgresql://{username}:{password}@{host}:{port}/{database}{normalizedQuery}";
}

static IResourceBuilder<ProjectResource> WithLocalPrimaryDatabase(
    IResourceBuilder<ProjectResource> project,
    IResourceBuilder<PostgresDatabaseResource> database,
    PrimaryDatabaseRole role)
{
    var credentialPrefix = $"Database__{role}__";
    var postgres = database.Resource.Parent;

    return project
        .WithEnvironment("Database__Provider", PrimaryDatabaseProvider.PostgreSql.ToString())
        .WithEnvironment("Database__Host", postgres.PrimaryEndpoint.Property(EndpointProperty.Host))
        .WithEnvironment("Database__Port", postgres.PrimaryEndpoint.Property(EndpointProperty.Port))
        .WithEnvironment("Database__Database", database.Resource.DatabaseName)
        .WithEnvironment("Database__Schema", PrimaryDatabaseConnectionOptions.DefaultSchema)
        .WithEnvironment("Database__TlsMode", PrimaryDatabaseTlsMode.Prefer.ToString())
        .WithEnvironment("Database__TrustServerCertificate", "false")
        .WithEnvironment($"{credentialPrefix}Username", postgres.UserNameReference)
        .WithEnvironment($"{credentialPrefix}Password", postgres.PasswordParameter);
}

static IResourceBuilder<ProjectResource> WithExternalPrimaryDatabase(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<ProjectResource> project,
    PrimaryDatabaseRole role)
{
    var database = PrimaryDatabaseConfiguration.Bind(builder.Configuration, role);
    var credentialPrefix = $"Database__{role}__";

    project = project
        .WithEnvironment("Database__Provider", database.Provider.ToString())
        .WithEnvironment("Database__Database", database.Database ?? string.Empty)
        .WithEnvironment("Database__Schema", database.Schema)
        .WithEnvironment("Database__TlsMode", database.TlsMode.ToString())
        .WithEnvironment("Database__TrustServerCertificate", database.TrustServerCertificate.ToString());

    if (database.Host is not null)
        project = project.WithEnvironment("Database__Host", database.Host);
    if (database.Port is not null)
        project = project.WithEnvironment("Database__Port", database.Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    if (database.Username is not null)
        project = project.WithEnvironment($"{credentialPrefix}Username", database.Username);
    if (database.Password is not null)
    {
        var password = builder.AddParameter(
            $"database-{role.ToString().ToLowerInvariant()}-{project.Resource.Name}-password",
            () => database.Password,
            publishValueAsDefault: false,
            secret: true);
        project = project.WithEnvironment($"{credentialPrefix}Password", password);
    }
    if (database.ServerFlavor is not null)
        project = project.WithEnvironment("Database__ServerFlavor", database.ServerFlavor.Value.ToString());
    if (database.ServerVersion is not null)
        project = project.WithEnvironment("Database__ServerVersion", database.ServerVersion.ToString());

    return project;
}

static IResourceBuilder<ProjectResource> WithLocalPrivacyErasureAuthorityDatabase(
    IResourceBuilder<ProjectResource> project,
    IResourceBuilder<PostgresDatabaseResource> database,
    PrimaryDatabaseRole role)
{
    var credentialPrefix = $"PrivacyErasureAuthorityDatabase__{role}__";
    var canonicalCredentialPrefix = $"Database__Erasure__{role}__";
    var postgres = database.Resource.Parent;

    return project
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Provider", PrimaryDatabaseProvider.PostgreSql.ToString())
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Host", postgres.PrimaryEndpoint.Property(EndpointProperty.Host))
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Port", postgres.PrimaryEndpoint.Property(EndpointProperty.Port))
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Database", database.Resource.DatabaseName)
        .WithEnvironment("PrivacyErasureAuthorityDatabase__TlsMode", PrimaryDatabaseTlsMode.Prefer.ToString())
        .WithEnvironment("PrivacyErasureAuthorityDatabase__TrustServerCertificate", "false")
        .WithEnvironment($"{credentialPrefix}Username", postgres.UserNameReference)
        .WithEnvironment($"{credentialPrefix}Password", postgres.PasswordParameter)
        .WithEnvironment("Database__Erasure__Provider", PrimaryDatabaseProvider.PostgreSql.ToString())
        .WithEnvironment("Database__Erasure__Host", postgres.PrimaryEndpoint.Property(EndpointProperty.Host))
        .WithEnvironment("Database__Erasure__Port", postgres.PrimaryEndpoint.Property(EndpointProperty.Port))
        .WithEnvironment("Database__Erasure__Database", database.Resource.DatabaseName)
        .WithEnvironment("Database__Erasure__TlsMode", PrimaryDatabaseTlsMode.Prefer.ToString())
        .WithEnvironment("Database__Erasure__TrustServerCertificate", "false")
        .WithEnvironment($"{canonicalCredentialPrefix}Username", postgres.UserNameReference)
        .WithEnvironment($"{canonicalCredentialPrefix}Password", postgres.PasswordParameter);
}

static IResourceBuilder<ProjectResource> WithExternalPrivacyErasureAuthorityDatabase(
    IDistributedApplicationBuilder builder,
    IResourceBuilder<ProjectResource> project,
    PrimaryDatabaseRole role)
{
    var database = PrivacyErasureAuthorityDatabaseConfiguration.Bind(builder.Configuration, role);
    var credentialPrefix = $"PrivacyErasureAuthorityDatabase__{role}__";
    var canonicalCredentialPrefix = $"Database__Erasure__{role}__";

    project = project
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Provider", database.Provider.ToString())
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Host", database.Host!)
        .WithEnvironment(
            "PrivacyErasureAuthorityDatabase__Port",
            (database.Port ?? 5432).ToString(System.Globalization.CultureInfo.InvariantCulture))
        .WithEnvironment("PrivacyErasureAuthorityDatabase__Database", database.Database!)
        .WithEnvironment("PrivacyErasureAuthorityDatabase__TlsMode", database.TlsMode.ToString())
        .WithEnvironment(
            "PrivacyErasureAuthorityDatabase__TrustServerCertificate",
            database.TrustServerCertificate.ToString())
        .WithEnvironment($"{credentialPrefix}Username", database.Username!)
        .WithEnvironment("Database__Erasure__Provider", database.Provider.ToString())
        .WithEnvironment("Database__Erasure__Host", database.Host!)
        .WithEnvironment(
            "Database__Erasure__Port",
            (database.Port ?? 5432).ToString(System.Globalization.CultureInfo.InvariantCulture))
        .WithEnvironment("Database__Erasure__Database", database.Database!)
        .WithEnvironment("Database__Erasure__TlsMode", database.TlsMode.ToString())
        .WithEnvironment(
            "Database__Erasure__TrustServerCertificate",
            database.TrustServerCertificate.ToString())
        .WithEnvironment($"{canonicalCredentialPrefix}Username", database.Username!);

    var password = builder.AddParameter(
        $"privacy-authority-{role.ToString().ToLowerInvariant()}-{project.Resource.Name}-password",
        () => database.Password!,
        publishValueAsDefault: false,
        secret: true);
    return project
        .WithEnvironment($"{credentialPrefix}Password", password)
        .WithEnvironment($"{canonicalCredentialPrefix}Password", password);
}

static IResourceBuilder<ProjectResource> WithEmbeddedPrivacyErasureAuthority(
    IResourceBuilder<ProjectResource> project,
    string localPath,
    string busyTimeoutSeconds)
{
    const string containerPath = "/app/data/privacy_erasure_authority.db";

    return project
        .WithReplicas(1)
        .WithEnvironment("PrivacyErasureAuthorityEmbedded__Path", localPath)
        .WithEnvironment("PrivacyErasureAuthorityEmbedded__WriterReplicaCount", "1")
        .WithEnvironment("PrivacyErasureAuthorityEmbedded__BusyTimeoutSeconds", busyTimeoutSeconds)
        .PublishAsDockerFile(container => container
            .WithEnvironment("PrivacyErasureAuthorityEmbedded__Path", containerPath)
            .WithVolume("islamu-event-privacy-erasure-authority-data", "/app/data"));
}

static PrivacyErasureAuthorityTopology ParsePrivacyErasureTopology(string value)
{
    if (string.Equals(
            value,
            nameof(PrivacyErasureAuthorityTopology.EmbeddedSqlite),
            StringComparison.OrdinalIgnoreCase))
    {
        return PrivacyErasureAuthorityTopology.EmbeddedSqlite;
    }

    if (string.Equals(
            value,
            nameof(PrivacyErasureAuthorityTopology.ExternalDatabase),
            StringComparison.OrdinalIgnoreCase))
    {
        return PrivacyErasureAuthorityTopology.ExternalDatabase;
    }

    if (string.Equals(
            value,
            nameof(PrivacyErasureAuthorityTopology.CoLocated),
            StringComparison.OrdinalIgnoreCase))
    {
        return PrivacyErasureAuthorityTopology.CoLocated;
    }

    throw new InvalidOperationException(
        "PRIVACY_ERASURE_AUTHORITY_TOPOLOGY must be EmbeddedSqlite, CoLocated, or ExternalDatabase.");
}

static HostingTopology ParseHostingTopology(string? rawValue)
{
    if (string.IsNullOrWhiteSpace(rawValue))
    {
        return HostingTopology.Split;
    }

    if (string.Equals(rawValue.Trim(), nameof(HostingTopology.Split), StringComparison.OrdinalIgnoreCase))
    {
        return HostingTopology.Split;
    }

    if (string.Equals(rawValue.Trim(), nameof(HostingTopology.Standalone), StringComparison.OrdinalIgnoreCase))
    {
        return HostingTopology.Standalone;
    }

    throw new InvalidOperationException(
        "Hosting:Topology must be Split or Standalone.");
}

static IResourceBuilder<ProjectResource> WithProfileSecretMode(
    IResourceBuilder<ProjectResource> project,
    AspireRunMode runMode,
    IConfiguration configuration)
{
    project = project
        .WithEnvironment("ISLAMU_ASPIRE_MODE", runMode.ToString())
        .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

    SecretProviderType provider = SecretAuthorityConfiguration.GetRequiredProvider(configuration);
    return runMode == AspireRunMode.FullLocal && provider == SecretProviderType.Infisical
        ? project
            .WithEnvironment("SecretProvider__Provider", nameof(SecretProviderType.Environment))
            .WithEnvironment("SecretProvider__Infisical__Url", "")
            .WithEnvironment("SecretProvider__Infisical__ProjectId", "")
            .WithEnvironment("SecretProvider__Infisical__ClientId", "")
            .WithEnvironment("SecretProvider__Infisical__ClientSecret", "")
            .WithEnvironment("SecretProvider__Infisical__Environment", "")
        : WithSelectedSecretAuthority(project, configuration);
}

static IResourceBuilder<ProjectResource> WithSelectedSecretAuthority(
    IResourceBuilder<ProjectResource> project,
    IConfiguration configuration)
{
    SecretProviderType provider = SecretAuthorityConfiguration.GetRequiredProvider(configuration);
    if (provider is SecretProviderType.Environment or SecretProviderType.UserSecrets)
    {
        return project
            .WithEnvironment("SecretProvider__Provider", provider.ToString())
            .WithEnvironment("SecretProvider__Infisical__Url", "")
            .WithEnvironment("SecretProvider__Infisical__ProjectId", "")
            .WithEnvironment("SecretProvider__Infisical__ClientId", "")
            .WithEnvironment("SecretProvider__Infisical__ClientSecret", "")
            .WithEnvironment("SecretProvider__Infisical__Environment", "");
    }

    var infisical = ReadInfisicalBootstrap(configuration);

    return project
        .WithEnvironment("SecretProvider__Provider", provider.ToString())
        .WithEnvironment("SecretProvider__Infisical__Url", infisical.Url)
        .WithEnvironment("SecretProvider__Infisical__ProjectId", infisical.ProjectId)
        .WithEnvironment("SecretProvider__Infisical__ClientId", infisical.ClientId)
        .WithEnvironment("SecretProvider__Infisical__ClientSecret", infisical.ClientSecret)
        .WithEnvironment("SecretProvider__Infisical__Environment", infisical.Environment);
}

static InfisicalBootstrapSettings ReadInfisicalBootstrap(IConfiguration configuration)
{
    static string Read(
        IConfiguration configuration,
        string key,
        string? fallback = null)
    {
        string environmentKey = key switch
        {
            "ProjectId" => "INFISICAL_PROJECT_ID",
            "ClientId" => "INFISICAL_CLIENT_ID",
            "ClientSecret" => "INFISICAL_CLIENT_SECRET",
            "Environment" => "INFISICAL_ENV",
            _ => $"INFISICAL_{key.ToUpperInvariant()}",
        };
        return configuration[$"SecretProvider:Infisical:{key}"]
            ?? configuration[environmentKey]
            ?? fallback
            ?? string.Empty;
    }

    return new InfisicalBootstrapSettings(
        Url: Read(configuration, "Url"),
        ProjectId: Read(configuration, "ProjectId"),
        ClientId: Read(configuration, "ClientId"),
        ClientSecret: Read(configuration, "ClientSecret"),
        Environment: Read(configuration, "Environment"));
}

static string FindRepositoryRoot(string startDirectory)
{
    var current = new DirectoryInfo(startDirectory);

    while (current is not null)
    {
        if ((File.Exists(Path.Combine(current.FullName, "Explore.slnx")) || File.Exists(Path.Combine(current.FullName, "Explore.sln")))
            && Directory.Exists(Path.Combine(current.FullName, "cerbos", "policies")))
        {
            return current.FullName;
        }

        current = current.Parent;
    }

    return startDirectory;
}

static (string Image, string Tag) ResolveImageAndTag(string image, string defaultTag)
{
    var trimmed = image.Trim();
    var lastSlash = trimmed.LastIndexOf('/');
    var lastColon = trimmed.LastIndexOf(':');

    if (lastColon > lastSlash && lastColon < trimmed.Length - 1)
    {
        return (trimmed[..lastColon], trimmed[(lastColon + 1)..]);
    }

    return (trimmed, defaultTag);
}

static IResourceBuilder<ProjectResource> ConfigureConfigurationManifestOwner(
    IResourceBuilder<ProjectResource> resource,
    bool ownsConfigurationManifest,
    string configuredMode,
    string manifestPath) =>
    resource
        .WithEnvironment(
            "CONFIGURATION_MANIFEST_MODE",
            ownsConfigurationManifest ? configuredMode : "Off")
        .WithEnvironment("CONFIGURATION_MANIFEST_PATH", manifestPath);

static string ConfiguredValue(IConfiguration configuration, string key, string fallback) =>
    string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!;

static bool UsesSvixProvider(string provider) =>
    string.Equals(provider, WebhookOptions.ProviderSvix, StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, WebhookOptions.ProviderComposite, StringComparison.OrdinalIgnoreCase);

internal enum HostingTopology
{
    Split,
    Standalone
}

internal enum AspireRunMode
{
    FullLocal,
    DefaultLocal,
    ExternalInfra,
    LocalDataExternalPlatform
}

internal static class AspireRunModeExtensions
{
    public static bool UsesLocalData(this AspireRunMode runMode) =>
        runMode is AspireRunMode.FullLocal or AspireRunMode.DefaultLocal or AspireRunMode.LocalDataExternalPlatform;

    public static AspireRunMode Parse(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return AspireRunMode.DefaultLocal;
        }

        var normalized = rawValue.Trim()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Equals("full", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("localfull", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("fulllocal", StringComparison.OrdinalIgnoreCase))
        {
            return AspireRunMode.FullLocal;
        }

        if (normalized.Equals("default", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("localdefault", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("defaultlocal", StringComparison.OrdinalIgnoreCase))
        {
            return AspireRunMode.DefaultLocal;
        }

        if (normalized.Equals("lite", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("locallite", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("externalinfra", StringComparison.OrdinalIgnoreCase))
        {
            return AspireRunMode.ExternalInfra;
        }

        if (normalized.Equals("core", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("localcore", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("localdataexternalplatform", StringComparison.OrdinalIgnoreCase))
        {
            return AspireRunMode.LocalDataExternalPlatform;
        }

        throw new InvalidOperationException(
            $"Unsupported ISLAMU_ASPIRE_MODE '{rawValue}'. Use FullLocal, DefaultLocal, ExternalInfra, or LocalDataExternalPlatform.");
    }
}

internal sealed record LocalPlatformResources(
    IResourceBuilder<ContainerResource> Keycloak,
    IResourceBuilder<ContainerResource> KeycloakInit,
    IResourceBuilder<ParameterResource> KeycloakBlazorClientSecret,
    IResourceBuilder<ContainerResource> Cerbos,
    IResourceBuilder<ContainerResource> Minio,
    IResourceBuilder<ContainerResource> MinioBootstrap,
    IResourceBuilder<ContainerResource>? Svix,
    IResourceBuilder<ContainerResource>? WeblateDb,
    IResourceBuilder<ContainerResource>? Weblate,
    IResourceBuilder<ContainerResource>? Coop,
    IResourceBuilder<ContainerResource>? CoopMigrations,
    IResourceBuilder<ContainerResource>? CoopClient,
    IResourceBuilder<ContainerResource>? PgAdmin,
    IResourceBuilder<ContainerResource>? Osprey,
    IResourceBuilder<ContainerResource>? Prometheus,
    IResourceBuilder<ContainerResource>? Grafana);

internal sealed record InfisicalBootstrapSettings(
    string Url,
    string ProjectId,
    string ClientId,
    string ClientSecret,
    string Environment);
