using System.Collections.Frozen;
using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Domain.Secrets;

/// <summary>
/// Source-of-truth catalog of platform secrets.
/// <para>
/// Every allowed setting key, its permissible scopes, its allowed source types and its default
/// Infisical coordinates are declared here. The domain invariant <c>SecretBinding.Create</c> consults
/// the registry to reject illegal combinations before persistence — no resolver or handler may bind
/// a secret that is not listed here.
/// </para>
/// <para>
/// Folder layout matches the user specification verbatim:
/// <list type="bullet">
///   <item><c>/api</c> — SETUP_SECRET</item>
///   <item><c>/storage</c> — STORAGE_S3_*</item>
///   <item><c>/keycloak</c> — KEYCLOAK_*</item>
///   <item><c>/atproto</c> — ATPROTO_*</item>
///   <item><c>/cerbos</c> — CERBOS_GRPC_ENDPOINT</item>
///   <item><c>/postgresql</c> — POSTGRESQL_*</item>
///   <item><c>/smtp</c> — MAIL_SMTP_*</item>
///   <item><c>/analytics</c> — ANALYTICS_POSTHOG_*</item>
///   <item><c>/ai</c> — AI_*</item>
/// </list>
/// </para>
/// </summary>
public static class SecretDefinitionRegistry
{
    private static readonly FrozenDictionary<string, SecretDefinition> DefinitionsByKey = BuildDefinitions();

    /// <summary>All platform secret definitions keyed by canonical setting key (lower.dot.case).</summary>
    public static IReadOnlyDictionary<string, SecretDefinition> All => DefinitionsByKey;

    /// <summary>Returns <c>true</c> when the platform recognizes the given setting key.</summary>
    public static bool IsKnown(string settingKey) => DefinitionsByKey.ContainsKey(settingKey);

    /// <summary>Returns the definition or <c>null</c> when the key is unknown.</summary>
    public static SecretDefinition? TryGet(string settingKey) =>
        DefinitionsByKey.TryGetValue(settingKey, out var def) ? def : null;

    /// <summary>Returns the definition; throws <see cref="KeyNotFoundException"/> when unknown.</summary>
    public static SecretDefinition GetRequired(string settingKey) =>
        TryGet(settingKey) ?? throw new KeyNotFoundException(
            $"Unknown secret key '{settingKey}'. Add a definition to SecretDefinitionRegistry first.");

    public static SecretRotationProfile GetRotationProfile(string settingKey)
    {
        _ = GetRequired(settingKey);
        var mode = settingKey switch
        {
            Keys.SetupSecret or Keys.Authentication.LocalBootstrapPassword => SecretRotationMode.UnsupportedLive,
            Keys.Promotions.CodeLookupHmacKey
                or Keys.Admissions.CredentialLookupHmacKey
                or Keys.Admissions.RecoveryCapabilityHmacKey
                or Keys.Admissions.ScannerCapabilityHmacKey
                or Keys.Atproto.OAuthClientPrivateJwks
                or Keys.Atproto.SessionEncryptionKeyRing
                or Keys.Atproto.SessionJwtPrivateJwks => SecretRotationMode.OverlapRollout,
            _ => SecretRotationMode.CoordinatedRestart,
        };

        return mode switch
        {
            SecretRotationMode.OverlapRollout => new(
                OwnerFor(settingKey),
                mode,
                CandidateValidationRequired: true,
                EveryReplicaAcknowledgementRequired: true,
                StaleReplicaAction: "drain-and-fail-closed",
                RollbackAction: "reactivate-previous-credential",
                RevocationGate: "after-all-replica-acknowledgements",
                BreakGlassAction: "coordinated-restart"),
            SecretRotationMode.CoordinatedRestart => new(
                OwnerFor(settingKey),
                mode,
                CandidateValidationRequired: true,
                EveryReplicaAcknowledgementRequired: true,
                StaleReplicaAction: "stop-before-deadline",
                RollbackAction: "restore-previous-deployment-value-and-restart",
                RevocationGate: "after-restart-readiness-converges",
                BreakGlassAction: "maintenance-restart"),
            _ => new(
                OwnerFor(settingKey),
                mode,
                CandidateValidationRequired: true,
                EveryReplicaAcknowledgementRequired: false,
                StaleReplicaAction: "not-applicable",
                RollbackAction: "retain-current-value",
                RevocationGate: "manual-operator-only",
                BreakGlassAction: "replace-and-restart"),
        };
    }

    private static string OwnerFor(string settingKey) =>
        settingKey[..settingKey.IndexOf('.')];

    // -- Canonical keys (lower.dot.case, aligned with governance/key naming convention) --

    public static class Keys
    {
        public const string SetupSecret = "api.setup_secret";
        public const string SetupSecretBindingCommitmentHmacKey =
            "setup.secret_binding_commitment_hmac_key";

        public static class Storage
        {
            public const string Endpoint = "storage.s3.endpoint";
            public const string PublicEndpoint = "storage.s3.public_endpoint";
            public const string BucketName = "storage.s3.bucket_name";
            public const string AccessKeyId = InfrastructureSecretSettingKeys.Storage.AccessKeyId;
            public const string SecretAccessKey = InfrastructureSecretSettingKeys.Storage.SecretAccessKey;
            public const string Region = "storage.s3.region";
        }

        public static class Keycloak
        {
            public const string Realm = "keycloak.realm";
            public const string ClientId = "keycloak.client_id";
            public const string BlazorClientSecret = "keycloak.blazor_client_secret";
            public const string ApiClientSecret = "keycloak.api_client_secret";
            public const string Endpoint = "keycloak.endpoint";
            public const string AdminUsername = "keycloak.admin_username";
            public const string AdminPassword = "keycloak.admin_password";
            public const string DbPassword = "keycloak.db_password";
        }

        public static class Authentication
        {
            public const string LocalJwtKey = "authentication.local.jwt_key";
            public const string LocalBootstrapPassword = "authentication.local.bootstrap_password";
        }

        public static class IdentityDatabase
        {
            public const string ConnectionString = "identity_database.connection_string";
            public const string RuntimePassword = "identity_database.runtime.password";
            public const string MigratorPassword = "identity_database.migrator.password";
        }

        public static class Stripe
        {
            public const string PlatformSecretKey = "payments.stripe.platform_secret_key";
            public const string WebhookSecret = "payments.stripe.webhook_secret";
        }

        public static class Promotions
        {
            public const string CodeLookupHmacKey = "promotions.code_lookup_hmac_key";
        }

        public static class Admissions
        {
            public const string CredentialLookupHmacKey = "admissions.credential_lookup_hmac_key";
            public const string RecoveryCapabilityHmacKey = "admissions.recovery_capability_hmac_key";
            public const string ScannerCapabilityHmacKey = "admissions.scanner_capability_hmac_key";
        }

        public static class Ticketing
        {
            public const string RecoveryManifestHmacKey =
                "ticketing.recovery_manifest_hmac_key";
        }

        public static class Atproto
        {
            public const string OAuthClientPrivateJwks = "auth.atproto.oauth_client_private_jwks";
            public const string SessionEncryptionKeyRing = "auth.atproto.session_encryption_keyring";
            public const string SessionJwtPrivateJwks = "auth.atproto.session_jwt_private_jwks";
        }

        public static class Postgresql
        {
            public const string Host = "postgresql.host";
            public const string Port = "postgresql.port";
            public const string Database = "postgresql.database";
            public const string Username = "postgresql.username";
            public const string Password = "postgresql.password";
        }

        public static class Smtp
        {
            public const string Host = "smtp.host";
            public const string Port = "smtp.port";
            public const string Username = InfrastructureSecretSettingKeys.Email.SmtpUsername;
            public const string Password = InfrastructureSecretSettingKeys.Email.SmtpPassword;
            public const string FromAddress = "smtp.from_address";
            public const string FromName = "smtp.from_name";
        }

        public static class Analytics
        {
            public const string PosthogPublicKey = "analytics.posthog.public_key";
            public const string PosthogHost = "analytics.posthog.host";
            public const string PersonalApiKey = "analytics.personal_api_key";
        }

        public static class Localization
        {
            public const string TmsApiKey = InfrastructureSecretSettingKeys.Localization.TmsApiKey;
        }

        public static class Management
        {
            public const string ControlPlaneRegistrationCredentials =
                InfrastructureSecretSettingKeys.Management.ControlPlaneRegistrationCredentials;
        }

        public static class Cerbos
        {
            public const string GrpcEndpoint = "cerbos.grpc_endpoint";
            public const string CustomAdminUsername = InfrastructureSecretSettingKeys.Cerbos.CustomAdminUsername;
            public const string CustomAdminPassword = InfrastructureSecretSettingKeys.Cerbos.CustomAdminPassword;
        }

        public static class Webhooks
        {
            public const string SvixAuthToken = "webhooks.svix.auth_token";
            public const string SvixOperationalWebhookSecret = "webhooks.svix.operational_webhook_secret";
        }

        public static class RegistrationProviders
        {
            public const string ApiToken = "registration_provider.api_token";
            public const string WebhookSecret = "registration_provider.webhook_secret";
        }

        public static class Ai
        {
            public const string OpenAiApiKey = "ai.openai.api_key";
            public const string AnthropicApiKey = "ai.anthropic.api_key";
        }

        public static class Integrations
        {
            public static class Listmonk
            {
                public const string ApiUsername = InfrastructureSecretSettingKeys.Integrations.Listmonk.ApiUsername;
                public const string ApiKey = InfrastructureSecretSettingKeys.Integrations.Listmonk.ApiKey;
            }
        }
    }

    private static FrozenDictionary<string, SecretDefinition> BuildDefinitions()
    {
        // Bootstrap also owns the core-vs-optional capability classification; all
        // non-bootstrap definitions activate only their owning capability.
        var nonBootstrapSources = new[]
        {
            SecretSourceType.Infisical,
            SecretSourceType.EnvironmentVariable,
        };
        var bootstrapSources = new[]
        {
            SecretSourceType.Infisical,
            SecretSourceType.EnvironmentVariable,
        };

        var instanceOnly = new[] { SecretScope.Instance };
        var instanceOrTenant = new[] { SecretScope.Instance, SecretScope.Tenant };

        var defs = new List<SecretDefinition>
        {
            // --- api/SETUP_SECRET ---
            new()
            {
                Key = Keys.SetupSecret,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/api",
                DefaultInfisicalKey = "SETUP_SECRET",
                DefaultEnvironmentVariableName = "SETUP_SECRET",
                IsBootstrapSecret = true,
                Description = "First-run setup token authorising instance onboarding.",
            },
            new()
            {
                Key = Keys.SetupSecretBindingCommitmentHmacKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/setup",
                DefaultInfisicalKey = "SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY",
                DefaultEnvironmentVariableName = "SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY",
                IsBootstrapSecret = false,
                Description = "Server-only HMAC key for Setup secret-write commitments.",
            },

            new()
            {
                Key = Keys.Authentication.LocalBootstrapPassword,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/api",
                DefaultInfisicalKey = "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD",
                DefaultEnvironmentVariableName = "INSTANCE_BOOTSTRAP_LOCAL_PASSWORD",
                IsBootstrapSecret = true,
                Description = "Initial Local administrator credential requiring private first-use replacement.",
            },

            // --- storage/STORAGE_S3_* ---
            new()
            {
                Key = Keys.Storage.Endpoint,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/storage",
                DefaultInfisicalKey = "STORAGE_S3_ENDPOINT",
                DefaultEnvironmentVariableName = "STORAGE_S3_ENDPOINT",
                IsBootstrapSecret = false,
                Description = "S3-compatible endpoint URL (internal/backend).",
            },
            new()
            {
                Key = Keys.Storage.PublicEndpoint,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/storage",
                DefaultInfisicalKey = "STORAGE_S3_PUBLIC_ENDPOINT",
                DefaultEnvironmentVariableName = "STORAGE_S3_PUBLIC_ENDPOINT",
                IsBootstrapSecret = false,
                Description = "Publicly-reachable S3 endpoint used for pre-signed URLs.",
            },
            new()
            {
                Key = Keys.Storage.BucketName,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/storage",
                DefaultInfisicalKey = "STORAGE_S3_BUCKET_NAME",
                DefaultEnvironmentVariableName = "STORAGE_S3_BUCKET_NAME",
                IsBootstrapSecret = false,
                Description = "Default S3 bucket name for media + private assets.",
            },
            new()
            {
                Key = Keys.Storage.AccessKeyId,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/storage",
                DefaultInfisicalKey = "STORAGE_S3_ACCESS_KEY_ID",
                DefaultEnvironmentVariableName = "STORAGE_S3_ACCESS_KEY_ID",
                IsBootstrapSecret = false,
                Description = "S3 access key ID.",
            },
            new()
            {
                Key = Keys.Storage.SecretAccessKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/storage",
                DefaultInfisicalKey = "STORAGE_S3_SECRET_ACCESS_KEY",
                DefaultEnvironmentVariableName = "STORAGE_S3_SECRET_ACCESS_KEY",
                IsBootstrapSecret = false,
                Description = "S3 secret access key.",
            },
            new()
            {
                Key = Keys.Storage.Region,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/storage",
                DefaultInfisicalKey = "STORAGE_S3_REGION",
                DefaultEnvironmentVariableName = "STORAGE_S3_REGION",
                IsBootstrapSecret = false,
                Description = "S3 region (e.g. us-east-1).",
            },

            // --- keycloak/KEYCLOAK_* ---
            new()
            {
                Key = Keys.Keycloak.Realm,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_REALM",
                DefaultEnvironmentVariableName = "KEYCLOAK_REALM",
                IsBootstrapSecret = false,
                Description = "Keycloak realm name.",
            },
            new()
            {
                Key = Keys.Keycloak.ClientId,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_CLIENT_ID",
                DefaultEnvironmentVariableName = "KEYCLOAK_CLIENT_ID",
                IsBootstrapSecret = false,
                Description = "Keycloak OIDC client ID (Blazor Server BFF).",
            },
            new()
            {
                Key = Keys.Keycloak.BlazorClientSecret,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_BLAZOR_CLIENT_SECRET",
                DefaultEnvironmentVariableName = "KEYCLOAK_BLAZOR_CLIENT_SECRET",
                IsBootstrapSecret = false,
                Description = "Keycloak OIDC client secret for the Blazor Server BFF (confidential client).",
            },
            new()
            {
                Key = Keys.Keycloak.ApiClientSecret,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_API_CLIENT_SECRET",
                DefaultEnvironmentVariableName = "KEYCLOAK_API_CLIENT_SECRET",
                IsBootstrapSecret = false,
                Description = "Keycloak OIDC client secret for the API service (confidential client).",
            },
            new()
            {
                Key = Keys.Keycloak.Endpoint,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_ENDPOINT",
                DefaultEnvironmentVariableName = "KEYCLOAK_ENDPOINT",
                IsBootstrapSecret = false,
                Description = "Keycloak base URL (e.g. https://auth.example.com).",
            },
            new()
            {
                Key = Keys.Keycloak.AdminUsername,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_ADMIN_USERNAME",
                DefaultEnvironmentVariableName = "KEYCLOAK_ADMIN_USERNAME",
                IsBootstrapSecret = false,
                Description = "Keycloak realm administrator username.",
            },
            new()
            {
                Key = Keys.Keycloak.AdminPassword,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_ADMIN_PASSWORD",
                DefaultEnvironmentVariableName = "KEYCLOAK_ADMIN_PASSWORD",
                IsBootstrapSecret = false,
                Description = "Keycloak realm administrator password.",
            },
            new()
            {
                Key = Keys.Keycloak.DbPassword,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/keycloak",
                DefaultInfisicalKey = "KEYCLOAK_DB_PASSWORD",
                DefaultEnvironmentVariableName = "KEYCLOAK_DB_PASSWORD",
                IsBootstrapSecret = false,
                Description = "Keycloak backing database password (read by Keycloak itself).",
            },
            new()
            {
                Key = Keys.Authentication.LocalJwtKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/api",
                DefaultInfisicalKey = "AUTHENTICATION_LOCAL_JWT_KEY",
                DefaultEnvironmentVariableName = "AUTHENTICATION_LOCAL_JWT_KEY",
                IsBootstrapSecret = false,
                Description = "HMAC signing key for embedded Local Identity access tokens.",
            },
            new()
            {
                Key = Keys.IdentityDatabase.ConnectionString,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/database/identity",
                DefaultInfisicalKey = "IDENTITY_DATABASE_CONNECTION_STRING",
                DefaultEnvironmentVariableName = "IDENTITY_DATABASE_CONNECTION_STRING",
                IsBootstrapSecret = false,
                Description = "Optional external Identity database connection string.",
            },
            new()
            {
                Key = Keys.IdentityDatabase.RuntimePassword,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/database/identity",
                DefaultInfisicalKey = "IDENTITY_DATABASE_RUNTIME_PASSWORD",
                DefaultEnvironmentVariableName = "IDENTITY_DATABASE_RUNTIME_PASSWORD",
                IsBootstrapSecret = false,
                Description = "Runtime credential for an external Identity database.",
            },
            new()
            {
                Key = Keys.IdentityDatabase.MigratorPassword,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/database/identity",
                DefaultInfisicalKey = "IDENTITY_DATABASE_MIGRATOR_PASSWORD",
                DefaultEnvironmentVariableName = "IDENTITY_DATABASE_MIGRATOR_PASSWORD",
                IsBootstrapSecret = false,
                Description = "Migration credential for an external Identity database.",
            },

            new()
            {
                Key = Keys.Stripe.PlatformSecretKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/stripe",
                DefaultInfisicalKey = "STRIPE_PLATFORM_SECRET_KEY",
                DefaultEnvironmentVariableName = "STRIPE_PLATFORM_SECRET_KEY",
                IsBootstrapSecret = false,
                Description = "Stripe platform secret key reference.",
            },
            new()
            {
                Key = Keys.Stripe.WebhookSecret,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/stripe",
                DefaultInfisicalKey = "STRIPE_WEBHOOK_SECRET",
                DefaultEnvironmentVariableName = "STRIPE_WEBHOOK_SECRET",
                IsBootstrapSecret = false,
                Description = "Stripe webhook signing secret reference.",
            },

            new()
            {
                Key = Keys.Promotions.CodeLookupHmacKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/promotions",
                DefaultInfisicalKey = "PROMOTIONS_CODE_LOOKUP_HMAC_KEY",
                DefaultEnvironmentVariableName = "PROMOTIONS_CODE_LOOKUP_HMAC_KEY",
                IsBootstrapSecret = false,
                Description = "Versioned backend-only HMAC key for promotion-code lookup digests.",
            },
            new()
            {
                Key = Keys.Admissions.CredentialLookupHmacKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/admissions",
                DefaultInfisicalKey = "ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY",
                DefaultEnvironmentVariableName = "ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY",
                IsBootstrapSecret = false,
                Description = "Versioned backend-only HMAC key for admission credential lookup digests.",
            },
            new()
            {
                Key = Keys.Admissions.RecoveryCapabilityHmacKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/admissions",
                DefaultInfisicalKey = "ADMISSIONS_RECOVERY_CAPABILITY_HMAC_KEY",
                DefaultEnvironmentVariableName = "ADMISSIONS_RECOVERY_CAPABILITY_HMAC_KEY",
                IsBootstrapSecret = false,
                Description = "Versioned backend-only HMAC key for admission recovery capability digests.",
            },
            new()
            {
                Key = Keys.Admissions.ScannerCapabilityHmacKey,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/admissions",
                DefaultInfisicalKey = "ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY",
                DefaultEnvironmentVariableName = "ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY",
                IsBootstrapSecret = false,
                Description = "Versioned backend-only HMAC key for admission scanner capability digests.",
            },
            new()
            {
                Key = Keys.Ticketing.RecoveryManifestHmacKey,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/ticketing/recovery",
                DefaultInfisicalKey = "TICKETING_RECOVERY_MANIFEST_HMAC_KEY",
                DefaultEnvironmentVariableName = "TICKETING_RECOVERY_MANIFEST_HMAC_KEY",
                IsBootstrapSecret = true,
                Description = "Backend-only HMAC key for signed ticketing recovery manifests.",
            },

            new()
            {
                Key = Keys.Atproto.OAuthClientPrivateJwks,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/atproto",
                DefaultInfisicalKey = "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS",
                DefaultEnvironmentVariableName = "ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS",
                IsBootstrapSecret = false,
                Description = "Rotation-capable ES256 private JWK ring for ATProto OAuth client assertions.",
            },
            new()
            {
                Key = Keys.Atproto.SessionEncryptionKeyRing,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/atproto",
                DefaultInfisicalKey = "ATPROTO_SESSION_ENCRYPTION_KEYRING",
                DefaultEnvironmentVariableName = "ATPROTO_SESSION_ENCRYPTION_KEYRING",
                IsBootstrapSecret = false,
                Description = "Key ring for encrypting persisted ATProto OAuth session envelopes.",
            },
            new()
            {
                Key = Keys.Atproto.SessionJwtPrivateJwks,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/atproto",
                DefaultInfisicalKey = "ATPROTO_SESSION_JWT_PRIVATE_JWKS",
                DefaultEnvironmentVariableName = "ATPROTO_SESSION_JWT_PRIVATE_JWKS",
                IsBootstrapSecret = false,
                Description = "Rotation-capable ES256 private JWK ring for first-party ATProto session JWTs.",
            },

            // --- cerbos/CERBOS_GRPC_ENDPOINT ---
            new()
            {
                Key = Keys.Cerbos.GrpcEndpoint,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/cerbos",
                DefaultInfisicalKey = "CERBOS_GRPC_ENDPOINT",
                DefaultEnvironmentVariableName = "CERBOS_GRPC_ENDPOINT",
                IsBootstrapSecret = false,
                Description = "Cerbos PDP gRPC endpoint (e.g. cerbosgrpc.example.com:443).",
            },
            new()
            {
                Key = Keys.Cerbos.CustomAdminUsername,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/cerbos",
                DefaultInfisicalKey = "CERBOS_ADMIN_USERNAME",
                DefaultEnvironmentVariableName = "CERBOS_ADMIN_USERNAME",
                IsBootstrapSecret = false,
                Description = "Cerbos Admin API username used by server-side policy package sync.",
            },
            new()
            {
                Key = Keys.Cerbos.CustomAdminPassword,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/cerbos",
                DefaultInfisicalKey = "CERBOS_ADMIN_PASSWORD",
                DefaultEnvironmentVariableName = "CERBOS_ADMIN_PASSWORD",
                IsBootstrapSecret = false,
                Description = "Cerbos Admin API password used by server-side policy package sync.",
            },

            new()
            {
                Key = Keys.Webhooks.SvixAuthToken,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/webhook",
                DefaultInfisicalKey = "WEBHOOKS_SVIX_AUTH_TOKEN",
                DefaultEnvironmentVariableName = "WEBHOOKS_SVIX_AUTH_TOKEN",
                IsBootstrapSecret = false,
                Description = "Svix API token used by the backend-only outgoing webhook provider.",
            },
            new()
            {
                Key = Keys.Webhooks.SvixOperationalWebhookSecret,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/webhook",
                DefaultInfisicalKey = "WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET",
                DefaultEnvironmentVariableName = "WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET",
                IsBootstrapSecret = false,
                Description = "Svix endpoint signing secret used to verify incoming operational callbacks.",
            },

            new()
            {
                Key = Keys.RegistrationProviders.ApiToken,
                AllowedScopes = new[] { SecretScope.Tenant },
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/registration-providers",
                DefaultInfisicalKey = "REGISTRATION_PROVIDER_API_TOKEN",
                DefaultEnvironmentVariableName = "REGISTRATION_PROVIDER_API_TOKEN",
                IsBootstrapSecret = false,
                Description = "Tenant registration-provider API token reference.",
            },
            new()
            {
                Key = Keys.RegistrationProviders.WebhookSecret,
                AllowedScopes = new[] { SecretScope.Tenant },
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/registration-providers",
                DefaultInfisicalKey = "REGISTRATION_PROVIDER_WEBHOOK_SECRET",
                DefaultEnvironmentVariableName = "REGISTRATION_PROVIDER_WEBHOOK_SECRET",
                IsBootstrapSecret = false,
                Description = "Tenant registration-provider webhook signing secret reference.",
            },

            // --- postgresql/POSTGRESQL_* (ALL bootstrap) ---
            new()
            {
                Key = Keys.Postgresql.Host,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/postgresql",
                DefaultInfisicalKey = "POSTGRESQL_HOST",
                DefaultEnvironmentVariableName = "POSTGRESQL_HOST",
                IsBootstrapSecret = true,
                Description = "PostgreSQL host.",
            },
            new()
            {
                Key = Keys.Postgresql.Port,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/postgresql",
                DefaultInfisicalKey = "POSTGRESQL_PORT",
                DefaultEnvironmentVariableName = "POSTGRESQL_PORT",
                IsBootstrapSecret = true,
                Description = "PostgreSQL port.",
            },
            new()
            {
                Key = Keys.Postgresql.Database,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/postgresql",
                DefaultInfisicalKey = "POSTGRESQL_DATABASE",
                DefaultEnvironmentVariableName = "POSTGRESQL_DATABASE",
                IsBootstrapSecret = true,
                Description = "PostgreSQL database name.",
            },
            new()
            {
                Key = Keys.Postgresql.Username,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/postgresql",
                DefaultInfisicalKey = "POSTGRESQL_USERNAME",
                DefaultEnvironmentVariableName = "POSTGRESQL_USERNAME",
                IsBootstrapSecret = true,
                Description = "PostgreSQL username.",
            },
            new()
            {
                Key = Keys.Postgresql.Password,
                AllowedScopes = instanceOnly,
                AllowedSources = bootstrapSources,
                DefaultInfisicalPath = "/postgresql",
                DefaultInfisicalKey = "POSTGRESQL_PASSWORD",
                DefaultEnvironmentVariableName = "POSTGRESQL_PASSWORD",
                IsBootstrapSecret = true,
                Description = "PostgreSQL password used to open the primary database.",
            },

            // --- smtp/MAIL_SMTP_* ---
            new()
            {
                Key = Keys.Smtp.Host,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/smtp",
                DefaultInfisicalKey = "MAIL_SMTP_HOST",
                DefaultEnvironmentVariableName = "MAIL_SMTP_HOST",
                IsBootstrapSecret = false,
                Description = "SMTP server hostname.",
            },
            new()
            {
                Key = Keys.Smtp.Port,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/smtp",
                DefaultInfisicalKey = "MAIL_SMTP_PORT",
                DefaultEnvironmentVariableName = "MAIL_SMTP_PORT",
                IsBootstrapSecret = false,
                Description = "SMTP server port.",
            },
            new()
            {
                Key = Keys.Smtp.Username,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/smtp",
                DefaultInfisicalKey = "MAIL_SMTP_USERNAME",
                DefaultEnvironmentVariableName = "MAIL_SMTP_USERNAME",
                IsBootstrapSecret = false,
                Description = "SMTP authentication username.",
            },
            new()
            {
                Key = Keys.Smtp.Password,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/smtp",
                DefaultInfisicalKey = "MAIL_SMTP_PASSWORD",
                DefaultEnvironmentVariableName = "MAIL_SMTP_PASSWORD",
                IsBootstrapSecret = false,
                Description = "SMTP authentication password.",
            },
            new()
            {
                Key = Keys.Smtp.FromAddress,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/smtp",
                DefaultInfisicalKey = "MAIL_SMTP_FROM_ADDRESS",
                DefaultEnvironmentVariableName = "MAIL_SMTP_FROM_ADDRESS",
                IsBootstrapSecret = false,
                Description = "Default From: email address.",
            },
            new()
            {
                Key = Keys.Smtp.FromName,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/smtp",
                DefaultInfisicalKey = "MAIL_SMTP_FROM_NAME",
                DefaultEnvironmentVariableName = "MAIL_SMTP_FROM_NAME",
                IsBootstrapSecret = false,
                Description = "Default From: display name.",
            },

            // --- analytics/ANALYTICS_POSTHOG_* ---
            new()
            {
                Key = Keys.Analytics.PosthogPublicKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/analytics",
                DefaultInfisicalKey = "ANALYTICS_POSTHOG_PUBLIC_KEY",
                DefaultEnvironmentVariableName = "ANALYTICS_POSTHOG_PUBLIC_KEY",
                IsBootstrapSecret = false,
                Description = "PostHog project public API key.",
            },
            new()
            {
                Key = Keys.Analytics.PosthogHost,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/analytics",
                DefaultInfisicalKey = "ANALYTICS_POSTHOG_HOST",
                DefaultEnvironmentVariableName = "ANALYTICS_POSTHOG_HOST",
                IsBootstrapSecret = false,
                Description = "PostHog server URL.",
            },
            new()
            {
                Key = Keys.Analytics.PersonalApiKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/analytics",
                DefaultInfisicalKey = "ANALYTICS_PERSONAL_API_KEY",
                DefaultEnvironmentVariableName = "ANALYTICS_PERSONAL_API_KEY",
                IsBootstrapSecret = false,
                Description = "Backend-only analytics administration API key.",
            },

            // --- localization/LOCALIZATION_TMS_* ---
            new()
            {
                Key = Keys.Localization.TmsApiKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/localization",
                DefaultInfisicalKey = "LOCALIZATION_TMS_API_KEY",
                DefaultEnvironmentVariableName = "LOCALIZATION_TMS_API_KEY",
                IsBootstrapSecret = false,
                Description = "Tolgee/Weblate API token used only by backend TMS providers.",
            },
            new()
            {
                Key = Keys.Management.ControlPlaneRegistrationCredentials,
                AllowedScopes = instanceOnly,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/api",
                DefaultInfisicalKey = "CONTROL_PLANE_REGISTRATION_CREDENTIALS",
                DefaultEnvironmentVariableName = "CONTROL_PLANE_REGISTRATION_CREDENTIALS",
                IsBootstrapSecret = false,
                Description = "Protected directional machine credentials for optional managed mode.",
            },

            // --- integrations/listmonk/LISTMONK_* ---
            new()
            {
                Key = Keys.Integrations.Listmonk.ApiUsername,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/integrations/listmonk",
                DefaultInfisicalKey = "LISTMONK_API_USERNAME",
                DefaultEnvironmentVariableName = "LISTMONK_API_USERNAME",
                IsBootstrapSecret = false,
                Description = "Listmonk API username used by the backend subscriber sync worker.",
            },
            new()
            {
                Key = Keys.Integrations.Listmonk.ApiKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/integrations/listmonk",
                DefaultInfisicalKey = "LISTMONK_API_KEY",
                DefaultEnvironmentVariableName = "LISTMONK_API_KEY",
                IsBootstrapSecret = false,
                Description = "Listmonk API token or password used by the backend subscriber sync worker.",
            },

            // --- ai/AI_* ---
            new()
            {
                Key = Keys.Ai.OpenAiApiKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/ai",
                DefaultInfisicalKey = "AI_OPENAI_API_KEY",
                DefaultEnvironmentVariableName = "AI_OPENAI_API_KEY",
                IsBootstrapSecret = false,
                Description = "OpenAI API key (organisation-scoped).",
            },
            new()
            {
                Key = Keys.Ai.AnthropicApiKey,
                AllowedScopes = instanceOrTenant,
                AllowedSources = nonBootstrapSources,
                DefaultInfisicalPath = "/ai",
                DefaultInfisicalKey = "AI_ANTHROPIC_API_KEY",
                DefaultEnvironmentVariableName = "AI_ANTHROPIC_API_KEY",
                IsBootstrapSecret = false,
                Description = "Anthropic API key.",
            },
        };

        return defs.ToFrozenDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);
    }
}
