namespace Explore.API.Extensions;

using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Explore.Secrets.Configuration;
using Explore.Secrets.Database;

public static class ConfigurationExtensions
{
    /// <summary>
    /// Adds Infisical secrets and maps them to canonical .NET configuration keys.
    /// </summary>
    public static void AddSecretAuthorityConfiguration(
        this IConfigurationBuilder configBuilder,
        string environmentName)
    {
        var bootstrapConfig = configBuilder.Build();
        IConfiguration authority = SecretAuthorityConfiguration.Build(
            bootstrapConfig,
            environmentName,
            "/keycloak", "/database", "/database/erasure", "/database/identity", "/api", "/blazor",
            "/cerbos", "/mcp", "/ai", "/storage", "/smtp", "/integrations/listmonk");
        var isolatedAuthority = new ConfigurationBuilder().AddConfiguration(authority);
        PrivacyErasureAuthorityDatabaseConfiguration.ProjectDiscreteConfiguration(isolatedAuthority);
        ApplyMapping(configBuilder, isolatedAuthority.Build());
    }

    /// <summary>
    /// Maps Infisical secret names to .NET configuration keys.
    /// </summary>
    /// <remarks>
    /// Canonical Infisical keys:
    ///   /database: DATABASE_PROVIDER, DATABASE_HOST, DATABASE_PORT, DATABASE_NAME, DATABASE_SCHEMA, etc.
    ///   /api:      DEPLOYMENT_MODE (single_tenant or multi_tenant)
    ///   /keycloak: KEYCLOAK_ENDPOINT, KEYCLOAK_REALM
    ///   /cerbos:   CERBOS_GRPC_ENDPOINT, CERBOS_HTTP_ENDPOINT, CERBOS_USE_POLICY_SCOPE
    ///   /api|/mcp: MCP_ENABLED, MCP_ENDPOINT_PATH, MCP_STATELESS, MCP_ENABLE_LEGACY_SSE
    ///   /ai:       AI_ENDPOINT, AI_MODEL_ID, AI_API_KEY, AI_PROVIDER
    ///   /storage:  STORAGE_S3_ENDPOINT, STORAGE_S3_BUCKET_NAME, STORAGE_S3_ACCESS_KEY_ID, etc.
    ///   /smtp:     MAIL_SMTP_HOST, MAIL_SMTP_PORT, MAIL_SMTP_USERNAME, MAIL_SMTP_PASSWORD, etc.
    ///   /api:      VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY, VAPID_SUBJECT
    ///   /api:      USE_COMMERCIAL_LUCKYPENNY, LUCKYPENNY_LICENSE_KEY (Lucky Penny dual-versioning)
    /// </remarks>
    private static void ApplyMapping(IConfigurationBuilder configBuilder, IConfiguration config)
    {
        var rawRealm = config["KEYCLOAK_REALM"] ?? config["Keycloak:Realm"];
        var baseUrl = config["KEYCLOAK_ENDPOINT"];
        var explicitAuthority = config["Keycloak:Authority"];
        var rawKeycloakClientId = ReadFirst(
            config,
            "Keycloak:ClientId",
            "KEYCLOAK_CLIENT_ID",
            "KEYCLOAK_BLAZOR_CLIENT_ID");
        var rawKeycloakClientSecret = ReadFirst(
            config,
            "Keycloak:ClientSecret",
            "Keycloak:BlazorClientSecret",
            "KEYCLOAK_BLAZOR_CLIENT_SECRET");

        string? keycloakAuthority = null;
        if (!string.IsNullOrEmpty(explicitAuthority))
        {
            keycloakAuthority = explicitAuthority;
        }
        else if (!string.IsNullOrWhiteSpace(baseUrl) && !string.IsNullOrWhiteSpace(rawRealm))
        {
            keycloakAuthority = $"{baseUrl.TrimEnd('/')}/realms/{rawRealm}";
        }

        var metadataAddress = keycloakAuthority != null
            ? $"{keycloakAuthority}/.well-known/openid-configuration"
            : null;
        var keycloakClientId = !string.IsNullOrWhiteSpace(rawKeycloakClientId)
            ? rawKeycloakClientId
            : keycloakAuthority is null
                ? null
                : "islamu-event-blazor";
        // Preserve the operator's raw input (bare host:port or full URL). Normalization happens only
        // at gRPC channel creation time so we don't surface a misleading scheme back to the UI/storage.
        var cerbosGrpcEndpoint = ReadFirst(config, "Cerbos:GrpcEndpoint", "CERBOS_GRPC_ENDPOINT")?.Trim();
        var cerbosHttpEndpoint = ReadFirst(config, "Cerbos:HttpEndpoint", "CERBOS_HTTP_ENDPOINT")?.Trim();
        var cerbosUsePolicyScope = NormalizeBoolean(
            ReadFirst(
                config,
                "Cerbos:UsePolicyScope",
                "CERBOS_USE_POLICY_SCOPE",
                "CERBOS__USE_POLICY_SCOPE"));
        var cerbosUseTls = NormalizeBoolean(
            ReadFirst(
                config,
                "Cerbos:UseTls",
                "CERBOS_USE_TLS",
                "CERBOS__USE_TLS"));
        var cerbosPlaintextMode = NormalizeBoolean(
            ReadFirst(
                config,
                "Cerbos:PlaintextMode",
                "CERBOS_PLAINTEXT_MODE",
                "CERBOS__PLAINTEXT_MODE"));
        var authorizationProvider = ReadFirst(config, "Authorization:Provider", "AUTHORIZATION_PROVIDER")?.Trim();
        var authenticationProvider = ReadFirst(config, "Authentication:Provider", "AUTHENTICATION_PROVIDER")?.Trim();
        var atprotoLoginEnabled = NormalizeBoolean(
            ReadFirst(config, "Authentication:AtprotoLoginEnabled", "ATPROTO_LOGIN_ENABLED"));
        var deploymentMode = NormalizeDeploymentMode(config["DEPLOYMENT_MODE"]);
        var managedControlPlaneEnabled = NormalizeBoolean(ReadFirst(
            config,
            "CONTROL_PLANE_MANAGED_MODE",
            "ManagedControlPlane:Enabled"));
        var managedControlPlaneUrl = ReadFirst(
            config,
            "CONTROL_PLANE_URL",
            "ManagedControlPlane:ControlPlaneUrl");
        var managedInstanceId = ReadFirst(
            config,
            "CONTROL_PLANE_INSTANCE_ID",
            "ManagedControlPlane:ManagedInstanceId");
        var managedRegistrationToken = ReadFirst(
            config,
            "CONTROL_PLANE_REGISTRATION_TOKEN",
            "ManagedControlPlane:RegistrationToken");
        var managedMaximumTenantCount = ReadFirst(
            config,
            "CONTROL_PLANE_MAXIMUM_TENANT_COUNT",
            "ManagedControlPlane:MaximumTenantCount");
        var managedTenantAdministratorSignInUrl = ReadFirst(
            config,
            "CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL",
            "ManagedControlPlane:TenantAdministratorSignInUrl");
        var mcpEnabled = NormalizeBoolean(
            ReadFirst(
                config,
                "MCP_ENABLED",
                "MCP__ENABLED",
                "Api:McpEnabled",
                "Api:Mcp:Enabled")) ?? "true";
        var mcpEndpointPath = NormalizeMcpEndpointPath(
            ReadFirst(
                config,
                "MCP_ENDPOINT_PATH",
                "MCP__ENDPOINT_PATH",
                "Api:McpEndpointPath",
                "Api:Mcp:EndpointPath")) ?? "/mcp";
        var mcpStateless = NormalizeBoolean(
            ReadFirst(
                config,
                "MCP_STATELESS",
                "MCP__STATELESS",
                "Api:McpStateless",
                "Api:Mcp:Stateless")) ?? "true";
        var mcpEnableLegacySse = NormalizeBoolean(
            ReadFirst(
                config,
                "MCP_ENABLE_LEGACY_SSE",
                "MCP__ENABLE_LEGACY_SSE",
                "Api:McpEnableLegacySse",
                "Api:Mcp:EnableLegacySse")) ?? "true";
        var aiEndpointUrl = ReadFirst(config, "AI_ENDPOINT", "AI__ENDPOINT", "AiProvider:EndpointUrl");
        var aiModelId = ReadFirst(config, "AI_MODEL_ID", "AI__MODEL_ID", "AiProvider:ModelId");
        var aiApiKey = ReadFirst(config, "AI_API_KEY", "AI__API_KEY", "AiProvider:ApiKey");
        var aiProviderMasterCode = ReadFirst(config, "AI_PROVIDER", "AI__PROVIDER", "AiProvider:Provider")?.Trim().ToUpperInvariant().Replace('-', '_');
        var aiProviderId = MapProviderMasterCodeToId(aiProviderMasterCode);
        var aiProviderDefaultsAvailable = aiProviderMasterCode is "OPENAI" or "ANTHROPIC"
            ? !string.IsNullOrWhiteSpace(aiApiKey) && !string.IsNullOrWhiteSpace(aiModelId)
            : !string.IsNullOrWhiteSpace(aiEndpointUrl) && !string.IsNullOrWhiteSpace(aiModelId);
        var smtpHost = ReadFirst(config, "MAIL_SMTP_HOST", "SMTP_HOST", "Smtp:Host");
        var smtpPort = ReadFirst(config, "MAIL_SMTP_PORT", "SMTP_PORT", "Smtp:Port");
        var smtpEncryption = ReadFirst(config, "MAIL_SMTP_ENCRYPTION", "SMTP_SECURITY", "Smtp:Encryption");
        var smtpFromAddress = ReadFirst(config, "MAIL_SMTP_FROM_ADDRESS", "SMTP_FROM_ADDRESS", "Smtp:FromAddress");
        var smtpFromName = ReadFirst(config, "MAIL_SMTP_FROM_NAME", "SMTP_FROM_NAME", "Smtp:FromName");
        var listmonkEnabled = NormalizeBoolean(ReadFirst(config, "LISTMONK_ENABLED", GovernanceSettingKeys.Integrations.Listmonk.Enabled));
        var listmonkInstanceUrl = ReadFirst(config, "LISTMONK_INSTANCE_URL", GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl);
        var listmonkDefaultListId = ReadFirst(config, "LISTMONK_DEFAULT_LIST_ID", GovernanceSettingKeys.Integrations.Listmonk.DefaultListId);
        var listmonkPreconfirmSubscriptions = NormalizeBoolean(ReadFirst(config, "LISTMONK_PRECONFIRM_SUBSCRIPTIONS", GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions));
        var listmonkSyncOnRegistration = NormalizeBoolean(ReadFirst(config, "LISTMONK_SYNC_ON_REGISTRATION", GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration));
        var listmonkApiUsername = ReadFirst(config, "LISTMONK_API_USERNAME", SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername);
        var listmonkApiKey = ReadFirst(config, "LISTMONK_API_KEY", SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey);
        var vapidPublicKey = ReadFirst(config, "VAPID_PUBLIC_KEY", "WebPush:VapidPublicKey");
        var vapidPrivateKey = ReadFirst(config, "VAPID_PRIVATE_KEY", "WebPush:VapidPrivateKey");
        var vapidSubject = ReadFirst(config, "VAPID_SUBJECT", "WebPush:VapidSubject");
        var webPushEnabled = NormalizeBoolean(ReadFirst(config, "WEB_PUSH_ENABLED", "WebPush:Enabled"))
            ?? (!string.IsNullOrWhiteSpace(vapidPublicKey)
                && !string.IsNullOrWhiteSpace(vapidPrivateKey)
                && !string.IsNullOrWhiteSpace(vapidSubject)
                    ? "true"
                    : null);

        var mappedConfig = new Dictionary<string, string?>
        {
            ["SETUP_SECRET"] = config["SETUP_SECRET"],
            ["Keycloak:Realm"] = null,
            ["Keycloak:Authority"] = null,
            ["Keycloak:MetadataAddress"] = null,
            ["Keycloak:ClientId"] = null,
            ["Keycloak:ClientSecret"] = null,
            ["Authentication:Local:JwtKey"] = null,
            ["IdentityDatabase:ConnectionString"] = null,
            ["IdentityDatabase:Runtime:Password"] = null,
            ["IdentityDatabase:Migrator:Password"] = null,
            ["Database:Provider"] = null,
            ["Database:Host"] = null,
            ["Database:Port"] = null,
            ["Database:Database"] = null,
            ["Database:Name"] = null,
            ["Database:Schema"] = null,
            ["Database:Runtime:Username"] = null,
            ["Database:Runtime:Password"] = null,
            ["Database:Migrator:Username"] = null,
            ["Database:Migrator:Password"] = null,
            ["Database:TlsMode"] = null,
            ["Database:TrustServerCertificate"] = null,
            ["Database:ServerFlavor"] = null,
            ["Database:ServerVersion"] = null,
            ["Database:Erasure:Provider"] = null,
            ["Database:Erasure:Host"] = null,
            ["Database:Erasure:Port"] = null,
            ["Database:Erasure:Database"] = null,
            ["Database:Erasure:Name"] = null,
            ["Database:Erasure:Runtime:Username"] = null,
            ["Database:Erasure:Runtime:Password"] = null,
            ["Database:Erasure:Migrator:Username"] = null,
            ["Database:Erasure:Migrator:Password"] = null,
            ["Database:Erasure:TlsMode"] = null,
            ["Database:Erasure:TrustServerCertificate"] = null,
            ["PrivacyErasureAuthorityDatabase:Provider"] = null,
            ["PrivacyErasureAuthorityDatabase:Host"] = null,
            ["PrivacyErasureAuthorityDatabase:Port"] = null,
            ["PrivacyErasureAuthorityDatabase:Database"] = null,
            ["PrivacyErasureAuthorityDatabase:Runtime:Username"] = null,
            ["PrivacyErasureAuthorityDatabase:Runtime:Password"] = null,
            ["PrivacyErasureAuthorityDatabase:Migrator:Username"] = null,
            ["PrivacyErasureAuthorityDatabase:Migrator:Password"] = null,
            ["PrivacyErasureAuthorityDatabase:TlsMode"] = null,
            ["PrivacyErasureAuthorityDatabase:TrustServerCertificate"] = null,
            ["ManagedControlPlane:RegistrationToken"] = null,
            ["AiProvider:ApiKey"] = null,
            [SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername] = null,
            [SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey] = null,
            ["WebPush:VapidPublicKey"] = null,
            ["WebPush:VapidPrivateKey"] = null,
            ["WebPush:VapidSubject"] = null,
            ["Licensing:LuckyPenny:LicenseKey"] = null,
        };

        static void TrySet(IDictionary<string, string?> dict, IConfiguration root, string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(root[key]))
            {
                dict[key] = root[key];
                return;
            }

            if (string.IsNullOrWhiteSpace(value))
                return;
            dict[key] = value;
        }

        static void TrySetCollection(
            IDictionary<string, string?> dict,
            IConfiguration root,
            string key,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value)
                || root.GetSection(key).GetChildren().Any())
            {
                return;
            }

            string[] values = value.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToArray();
            for (var index = 0; index < values.Length; index++)
            {
                dict[$"{key}:{index}"] = values[index];
            }
        }

        // Keycloak
        TrySet(mappedConfig, config, "Keycloak:Realm", rawRealm);
        TrySet(mappedConfig, config, "Keycloak:Authority", keycloakAuthority);
        TrySet(mappedConfig, config, "Keycloak:MetadataAddress", metadataAddress);
        TrySet(mappedConfig, config, "Keycloak:ClientId", keycloakClientId);
        TrySet(mappedConfig, config, "Keycloak:ClientSecret", rawKeycloakClientSecret);
        TrySet(mappedConfig, config, "Keycloak:Audience", "islamu-event-api");
        TrySet(mappedConfig, config, "Keycloak:RequireHttpsMetadata", "true");

        TrySet(mappedConfig, config, "Smtp:Host", smtpHost);
        TrySet(mappedConfig, config, "Smtp:Port", smtpPort);
        TrySet(mappedConfig, config, "Smtp:Encryption", smtpEncryption);
        TrySet(mappedConfig, config, "Smtp:FromAddress", smtpFromAddress);
        TrySet(mappedConfig, config, "Smtp:FromName", smtpFromName);

        TrySet(mappedConfig, config, GovernanceSettingKeys.Integrations.Listmonk.Enabled, listmonkEnabled);
        TrySet(mappedConfig, config, GovernanceSettingKeys.Integrations.Listmonk.InstanceUrl, listmonkInstanceUrl);
        TrySet(mappedConfig, config, GovernanceSettingKeys.Integrations.Listmonk.DefaultListId, listmonkDefaultListId);
        TrySet(mappedConfig, config, GovernanceSettingKeys.Integrations.Listmonk.PreconfirmSubscriptions, listmonkPreconfirmSubscriptions);
        TrySet(mappedConfig, config, GovernanceSettingKeys.Integrations.Listmonk.SyncOnRegistration, listmonkSyncOnRegistration);
        TrySet(mappedConfig, config, SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiUsername, listmonkApiUsername);
        TrySet(mappedConfig, config, SecretDefinitionRegistry.Keys.Integrations.Listmonk.ApiKey, listmonkApiKey);

        TrySet(mappedConfig, config, "WebPush:Enabled", webPushEnabled);
        TrySet(mappedConfig, config, "WebPush:VapidPublicKey", vapidPublicKey);
        TrySet(mappedConfig, config, "WebPush:VapidPrivateKey", vapidPrivateKey);
        TrySet(mappedConfig, config, "WebPush:VapidSubject", vapidSubject);

        // Primary Database (agnostic /database or DATABASE_* in Infisical)
        var dbName = ReadFirst(config, "DATABASE_NAME", "DATABASE_DATABASE", "Database:Name", "Database:Database");
        TrySet(mappedConfig, config, "Database:Provider", ReadFirst(config, "DATABASE_PROVIDER", "Database:Provider"));
        TrySet(mappedConfig, config, "Database:Host", ReadFirst(config, "DATABASE_HOST", "Database:Host"));
        TrySet(mappedConfig, config, "Database:Port", ReadFirst(config, "DATABASE_PORT", "Database:Port"));
        TrySet(mappedConfig, config, "Database:Database", dbName);
        TrySet(mappedConfig, config, "Database:Name", dbName);
        TrySet(mappedConfig, config, "Database:Schema", ReadFirst(config, "DATABASE_SCHEMA", "Database:Schema"));
        TrySet(mappedConfig, config, "Database:Runtime:Username", ReadFirst(config, "DATABASE_RUNTIME_USERNAME", "Database:Runtime:Username", "DATABASE_USERNAME", "Database:Username"));
        TrySet(mappedConfig, config, "Database:Runtime:Password", ReadFirst(config, "DATABASE_RUNTIME_PASSWORD", "Database:Runtime:Password", "DATABASE_PASSWORD", "Database:Password"));
        TrySet(mappedConfig, config, "Database:Migrator:Username", ReadFirst(config, "DATABASE_MIGRATOR_USERNAME", "Database:Migrator:Username"));
        TrySet(mappedConfig, config, "Database:Migrator:Password", ReadFirst(config, "DATABASE_MIGRATOR_PASSWORD", "Database:Migrator:Password"));
        TrySet(mappedConfig, config, "Database:TlsMode", ReadFirst(config, "DATABASE_TLS_MODE", "Database:TlsMode"));
        TrySet(mappedConfig, config, "Database:TrustServerCertificate", NormalizeBoolean(ReadFirst(config, "DATABASE_TRUST_SERVER_CERTIFICATE", "Database:TrustServerCertificate")));
        TrySet(mappedConfig, config, "Database:ServerFlavor", ReadFirst(config, "DATABASE_SERVER_FLAVOR", "Database:ServerFlavor"));
        TrySet(mappedConfig, config, "Database:ServerVersion", ReadFirst(config, "DATABASE_SERVER_VERSION", "Database:ServerVersion"));

        // Privacy Erasure Authority Database (/database/erasure)
        var erasureDbName = ReadFirst(config, "ERASURE_DATABASE_NAME", "Database:Erasure:Name", "Database:Erasure:Database");
        TrySet(mappedConfig, config, "Database:Erasure:Provider", ReadFirst(config, "ERASURE_DATABASE_PROVIDER", "Database:Erasure:Provider"));
        TrySet(mappedConfig, config, "Database:Erasure:Host", ReadFirst(config, "ERASURE_DATABASE_HOST", "Database:Erasure:Host"));
        TrySet(mappedConfig, config, "Database:Erasure:Port", ReadFirst(config, "ERASURE_DATABASE_PORT", "Database:Erasure:Port"));
        TrySet(mappedConfig, config, "Database:Erasure:Database", erasureDbName);
        TrySet(mappedConfig, config, "Database:Erasure:Name", erasureDbName);
        TrySet(mappedConfig, config, "Database:Erasure:Runtime:Username", ReadFirst(config, "ERASURE_DATABASE_RUNTIME_USERNAME", "Database:Erasure:Runtime:Username"));
        TrySet(mappedConfig, config, "Database:Erasure:Runtime:Password", ReadFirst(config, "ERASURE_DATABASE_RUNTIME_PASSWORD", "Database:Erasure:Runtime:Password"));
        TrySet(mappedConfig, config, "Database:Erasure:Migrator:Username", ReadFirst(config, "ERASURE_DATABASE_MIGRATOR_USERNAME", "Database:Erasure:Migrator:Username"));
        TrySet(mappedConfig, config, "Database:Erasure:Migrator:Password", ReadFirst(config, "ERASURE_DATABASE_MIGRATOR_PASSWORD", "Database:Erasure:Migrator:Password"));
        TrySet(mappedConfig, config, "Database:Erasure:TlsMode", ReadFirst(config, "ERASURE_DATABASE_TLS_MODE", "Database:Erasure:TlsMode"));
        TrySet(mappedConfig, config, "Database:Erasure:TrustServerCertificate", NormalizeBoolean(ReadFirst(config, "ERASURE_DATABASE_TRUST_SERVER_CERTIFICATE", "Database:Erasure:TrustServerCertificate")));
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Provider", config["PrivacyErasureAuthorityDatabase:Provider"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Host", config["PrivacyErasureAuthorityDatabase:Host"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Port", config["PrivacyErasureAuthorityDatabase:Port"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Database", config["PrivacyErasureAuthorityDatabase:Database"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Runtime:Username", config["PrivacyErasureAuthorityDatabase:Runtime:Username"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Runtime:Password", config["PrivacyErasureAuthorityDatabase:Runtime:Password"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Migrator:Username", config["PrivacyErasureAuthorityDatabase:Migrator:Username"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:Migrator:Password", config["PrivacyErasureAuthorityDatabase:Migrator:Password"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:TlsMode", config["PrivacyErasureAuthorityDatabase:TlsMode"]);
        TrySet(mappedConfig, config, "PrivacyErasureAuthorityDatabase:TrustServerCertificate", config["PrivacyErasureAuthorityDatabase:TrustServerCertificate"]);

        // Cerbos
        if (!string.IsNullOrWhiteSpace(cerbosGrpcEndpoint))
        {
            mappedConfig["Cerbos:GrpcEndpoint"] = cerbosGrpcEndpoint;
        }
        if (!string.IsNullOrWhiteSpace(cerbosHttpEndpoint))
        {
            mappedConfig["Cerbos:HttpEndpoint"] = cerbosHttpEndpoint;
            mappedConfig["Cerbos:AdminApi:Endpoints:0"] = cerbosHttpEndpoint;
        }
        TrySet(mappedConfig, config, "Cerbos:UsePolicyScope", cerbosUsePolicyScope);
        TrySet(mappedConfig, config, "Cerbos:UseTls", cerbosUseTls);
        TrySet(mappedConfig, config, "Cerbos:PlaintextMode", cerbosPlaintextMode);
        TrySet(mappedConfig, config, "Authorization:Provider", authorizationProvider);

        // Authentication provider selection is a two-axis contract: one primary provider plus
        // an independent ATProto login toggle. Explicit structured values retain precedence.
        TrySet(mappedConfig, config, "Authentication:Provider", authenticationProvider);
        TrySet(mappedConfig, config, "Authentication:AtprotoLoginEnabled", atprotoLoginEnabled);
        TrySet(mappedConfig, config, "Authentication:Local:JwtKey",
            ReadFirst(config, "Authentication:Local:JwtKey", "AUTHENTICATION_LOCAL_JWT_KEY"));
        TrySet(mappedConfig, config, "Authentication:Local:LockoutThreshold",
            ReadFirst(config, "Authentication:Local:LockoutThreshold", "AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD"));
        TrySet(mappedConfig, config, "Authentication:Local:LockoutDurationMinutes",
            ReadFirst(config, "Authentication:Local:LockoutDurationMinutes", "AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES"));
        TrySet(mappedConfig, config, "IdentityDatabase:Topology",
            ReadFirst(config, "IdentityDatabase:Topology", "IDENTITY_DATABASE_TOPOLOGY"));
        TrySet(mappedConfig, config, "IdentityDatabase:Provider",
            ReadFirst(config, "IdentityDatabase:Provider", "IDENTITY_DATABASE_PROVIDER"));
        TrySet(mappedConfig, config, "IdentityDatabase:ConnectionString",
            ReadFirst(config, "IdentityDatabase:ConnectionString", "IDENTITY_DATABASE_CONNECTION_STRING"));
        TrySet(mappedConfig, config, "IdentityDatabase:Host",
            ReadFirst(config, "IdentityDatabase:Host", "IDENTITY_DATABASE_HOST"));
        TrySet(mappedConfig, config, "IdentityDatabase:Port",
            ReadFirst(config, "IdentityDatabase:Port", "IDENTITY_DATABASE_PORT"));
        TrySet(mappedConfig, config, "IdentityDatabase:Name",
            ReadFirst(config, "IdentityDatabase:Name", "IDENTITY_DATABASE_NAME"));
        TrySet(mappedConfig, config, "IdentityDatabase:Schema",
            ReadFirst(config, "IdentityDatabase:Schema", "IDENTITY_DATABASE_SCHEMA"));
        TrySet(mappedConfig, config, "IdentityDatabase:TlsMode",
            ReadFirst(config, "IdentityDatabase:TlsMode", "IDENTITY_DATABASE_TLS_MODE"));
        TrySet(mappedConfig, config, "IdentityDatabase:TrustServerCertificate",
            NormalizeBoolean(ReadFirst(
                config,
                "IdentityDatabase:TrustServerCertificate",
                "IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE")));
        TrySet(mappedConfig, config, "IdentityDatabase:Runtime:Username",
            ReadFirst(config, "IdentityDatabase:Runtime:Username", "IDENTITY_DATABASE_RUNTIME_USERNAME"));
        TrySet(mappedConfig, config, "IdentityDatabase:Runtime:Password",
            ReadFirst(config, "IdentityDatabase:Runtime:Password", "IDENTITY_DATABASE_RUNTIME_PASSWORD"));
        TrySet(mappedConfig, config, "IdentityDatabase:Migrator:Username",
            ReadFirst(config, "IdentityDatabase:Migrator:Username", "IDENTITY_DATABASE_MIGRATOR_USERNAME"));
        TrySet(mappedConfig, config, "IdentityDatabase:Migrator:Password",
            ReadFirst(config, "IdentityDatabase:Migrator:Password", "IDENTITY_DATABASE_MIGRATOR_PASSWORD"));

        // Deployment
        TrySet(mappedConfig, config, "Deployment:Mode", deploymentMode);
        TrySet(mappedConfig, config, "PrivacyErasure:Authority:Topology",
            ReadFirst(config, "PrivacyErasure:Authority:Topology", "ERASURE_DATABASE_TOPOLOGY"));
        TrySet(mappedConfig, config, "ManagedControlPlane:Enabled", managedControlPlaneEnabled);
        TrySet(mappedConfig, config, "ManagedControlPlane:ControlPlaneUrl", managedControlPlaneUrl);
        TrySet(mappedConfig, config, "ManagedControlPlane:ManagedInstanceId", managedInstanceId);
        TrySet(mappedConfig, config, "ManagedControlPlane:RegistrationToken", managedRegistrationToken);
        TrySet(mappedConfig, config, "ManagedControlPlane:MaximumTenantCount", managedMaximumTenantCount);
        TrySet(mappedConfig, config, "ManagedControlPlane:TenantAdministratorSignInUrl", managedTenantAdministratorSignInUrl);

        // Optional server-side address geocoding.
        TrySet(mappedConfig, config, "Geocoding:Provider",
            ReadFirst(config, "GEOCODING_PROVIDER", "Geocoding:Provider"));
        TrySet(mappedConfig, config, "Geocoding:Endpoint",
            ReadFirst(config, "GEOCODING_ENDPOINT", "Geocoding:Endpoint"));
        TrySet(mappedConfig, config, "Geocoding:Language",
            ReadFirst(config, "GEOCODING_LANGUAGE", "Geocoding:Language"));
        TrySetCollection(mappedConfig, config, "Geocoding:CountryCodes",
            ReadFirst(config, "GEOCODING_COUNTRY_CODES"));
        TrySet(mappedConfig, config, "Geocoding:DatasetVersion",
            ReadFirst(config, "GEOCODING_DATASET_VERSION", "Geocoding:DatasetVersion"));
        TrySet(mappedConfig, config, "Geocoding:MaximumResults",
            ReadFirst(config, "GEOCODING_MAXIMUM_RESULTS", "Geocoding:MaximumResults"));
        TrySet(mappedConfig, config, "Geocoding:MaximumResponseBytes",
            ReadFirst(config, "GEOCODING_MAXIMUM_RESPONSE_BYTES", "Geocoding:MaximumResponseBytes"));
        TrySet(mappedConfig, config, "Geocoding:TotalTimeoutMilliseconds",
            ReadFirst(config, "GEOCODING_TOTAL_TIMEOUT_MILLISECONDS", "Geocoding:TotalTimeoutMilliseconds"));
        TrySet(mappedConfig, config, "Geocoding:MaximumRetryCount",
            ReadFirst(config, "GEOCODING_MAXIMUM_RETRY_COUNT", "Geocoding:MaximumRetryCount"));
        TrySetCollection(mappedConfig, config, "Geocoding:RetryDelaysMilliseconds",
            ReadFirst(config, "GEOCODING_RETRY_DELAYS_MILLISECONDS"));
        TrySet(mappedConfig, config, "Geocoding:ReadinessTimeoutMilliseconds",
            ReadFirst(config, "GEOCODING_READINESS_TIMEOUT_MILLISECONDS", "Geocoding:ReadinessTimeoutMilliseconds"));
        TrySet(mappedConfig, config, "Geocoding:SelectionLifetimeSeconds",
            ReadFirst(config, "GEOCODING_SELECTION_LIFETIME_SECONDS", "Geocoding:SelectionLifetimeSeconds"));

        // MCP
        TrySet(mappedConfig, config, "Mcp:Enabled", mcpEnabled);
        TrySet(mappedConfig, config, "Mcp:EndpointPath", mcpEndpointPath);
        TrySet(mappedConfig, config, "Mcp:Stateless", mcpStateless);
        TrySet(mappedConfig, config, "Mcp:EnableLegacySse", mcpEnableLegacySse);

        // AI provider defaults from deployment secrets. Runtime governance can still override
        // application-managed settings through the hierarchical settings system.
        TrySet(mappedConfig, config, "AiProvider:EndpointUrl", aiEndpointUrl);
        TrySet(mappedConfig, config, "AiProvider:ModelId", aiModelId);
        TrySet(mappedConfig, config, "AiProvider:ApiKey", aiApiKey);
        if (aiProviderDefaultsAvailable)
        {
            TrySet(mappedConfig, config, "AiProvider:Enabled", "true");
            TrySet(mappedConfig, config, "AiProvider:Provider", aiProviderId ?? "3");
        }

        // Lucky Penny dual-versioning (AutoMapper 15+ / MediatR 13+ commercial licensing).
        // USE_COMMERCIAL_LUCKYPENNY and LUCKYPENNY_LICENSE_KEY come from Infisical /api folder.
        // Version secrets (AUTOMAPPER_COMMERCIAL_VERSION, MEDIATR_COMMERCIAL_VERSION) are build-time
        // MSBuild properties only — they are not mapped to runtime configuration.
        TrySet(mappedConfig, config, "Licensing:LuckyPenny:Enabled",
            NormalizeBoolean(ReadFirst(config, "USE_COMMERCIAL_LUCKYPENNY", "Licensing:LuckyPenny:Enabled")));
        TrySet(mappedConfig, config, "Licensing:LuckyPenny:LicenseKey",
            ReadFirst(config, "LUCKYPENNY_LICENSE_KEY", "Licensing:LuckyPenny:LicenseKey"));

        configBuilder.AddInMemoryCollection(mappedConfig);
    }

    private static string? NormalizeDeploymentMode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var normalized = rawValue.Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        return normalized.Equals("MultiTenant", StringComparison.OrdinalIgnoreCase)
            ? "MultiTenant"
            : normalized.Equals("SingleTenant", StringComparison.OrdinalIgnoreCase)
                ? "SingleTenant"
                : null;
    }

    private static string? ReadFirst(IConfiguration config, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = config[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? NormalizeBoolean(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var value = rawValue.Trim();
        if (bool.TryParse(value, out var result))
        {
            return result ? "true" : "false";
        }

        return value.ToLowerInvariant() switch
        {
            "1" or "yes" or "on" => "true",
            "0" or "no" or "off" => "false",
            _ => null
        };
    }

    private static string? MapProviderMasterCodeToId(string? masterCode) => masterCode switch
    {
        "NONE" => "1",
        "FAKE" => "2",
        "OPENAI_COMPATIBLE" => "3",
        "ANTHROPIC_COMPATIBLE" => "4",
        "OPENAI" => "5",
        "AZURE_OPENAI" => "6",
        "ANTHROPIC" => "7",
        _ => null
    };

    private static string? NormalizeMcpEndpointPath(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var path = rawValue.Trim();

        return path.StartsWith('/')
            ? path
            : $"/{path}";
    }
}
