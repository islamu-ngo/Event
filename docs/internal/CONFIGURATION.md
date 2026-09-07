ABOUTME: Documents runtime configuration sources and precedence for API, BFF, and shared infrastructure.
ABOUTME: Focuses on non-inferable key names, mapping behavior, and settings cascade rules.

# Configuration

## Headless Instance Onboarding (Configured Administrator)

Instance onboarding reads exactly seven keys from the deployment environment or
the selected secret authority. Nothing is hardcoded in source, and there is no
fallback source: if the selected authority doesn't supply a key, the key is
absent.

| Key | Interactive | ConfiguredAdministrator |
|---|---|---|
| `INSTANCE_BOOTSTRAP_MODE` | `Interactive` | `ConfiguredAdministrator` |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | must be absent/empty | required, `keycloak` or `atproto` |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | must be absent/empty | required, exact subject |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | must be absent/empty | required, positive integer |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | must be absent/empty | required |
| `INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME` | must be absent/empty | optional, only with last name |
| `INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME` | must be absent/empty | optional, only with first name |

Both matrices are closed. A missing mode, an unknown mode string, any configured
key present under `Interactive`, any required key missing under
`ConfiguredAdministrator`, or one profile name without the other is a startup
failure, not a warning.

Subject meaning depends on the provider key:

- `keycloak`: the subject is paired with the existing `Keycloak:Authority`
  issuer. That issuer stays where it already lives; onboarding never introduces
  a second issuer setting.
- `atproto`: the subject must be the canonical ATProto DID for the account.

Deployment mode authority is unchanged too. `Deployment:Mode` remains ordinary
existing configuration; onboarding reads it and never redefines it.

The binding generation is a positive integer you own. Raise it when you
intentionally change the configured administrator; never reuse or lower it.

## Legal-Identity Configuration Boundaries

General instance accountability is startup-bound at
`Instance:OperatorIdentity`. The `.env` representation uses
`INSTANCE__OPERATORIDENTITY__*` and includes the UUIDv7 operator ID, public and
legal names, official-instance flag/origin, operator kind, jurisdiction,
optional registration identifier, public contact email, website, legal notice,
terms, and privacy URLs. API and Standalone hosts validate this section at
runtime startup and fail closed when it is incomplete or malformed. OpenAPI
generation uses its explicit build-time mode and does not weaken runtime hosts.

Tenant directory identity is not an environment setting. Each tenant owns one
canonical `tenant.directory-operator-identity` typed settings document created
atomically with the tenant and branding document. `Payments:CheckoutGovernance`
contains payment operations only: complaint/refund/dispute/reconciliation
owners, activation status, refund language, statement descriptor, and charge
type. Legacy operator identity keys under Checkout governance are unsupported.

Cosmetic branding remains independently configurable and is never a legal
identity fallback.

> **Audience:** Operators | Contributors | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-15
> **Source Anchors:** `src/Explore.Secrets/Database/PrimaryDatabaseConfiguration.cs`, `src/Event.MigrationService/Extensions/ConfigurationExtensions.cs`, `src/Event.Standalone/Program.cs`, `src/Event.Standalone/appsettings.json`, `src/Event.Standalone/Dockerfile`, `Explore.API/Extensions/ConfigurationExtensions.cs`, `Explore.API/Controllers/ListmonkIntegrationSettingsController.cs`, `Explore.API/Controllers/PlatformMonetizationSettingsController.cs`, `Explore.Application/Configuration/PromotionCodeLookupOptions.cs`, `Explore.Application/DTOs/Integrations/ListmonkIntegrationSettingsDto.cs`, `Explore.Application/Features/PlatformMonetization/`, `Explore.Infrastructure/Integrations/Listmonk/ListmonkSyncService.cs`, `Explore.Infrastructure/Services/Registration/PromotionCodeDigestService.cs`, `Explore.Blazor/Extensions/ConfigurationExtension.cs`, `Explore.Blazor/Extensions/YarpProxyExtensions.cs`, `Event.Web.BffHosting/Authentication/EventBffKeycloakAuthenticationOptions.cs`, `Event.Web.BffHosting/Proxy/EventApiBaseAddressResolver.cs`, `Explore.Application/Features/EventReporting/EventReportSubmissionOptions.cs`, `Explore.Application/Services/AccountAuthorityLifecycleEmailOptions.cs`, `Explore.Application/Notifications/AccountAuthorityKind.cs`, `Explore.Application/Notifications/NotificationRoutingOptions.cs`, `Explore.Infrastructure/Configuration/ModerationProviderOptions.cs`, `Explore.Infrastructure/Configuration/OspreyProviderOptions.cs`, `Explore.Infrastructure/Configuration/CoopProviderOptions.cs`, `Explore.API/Services/CoopWebhookSignatureValidator.cs`, `Explore.Infrastructure/Services/HierarchicalSettingsResolver.cs`, `Explore.Infrastructure/Services/Keycloak/KeycloakLifecycleEmailOptions.cs`, `Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs`, `Explore.Infrastructure/Storage/LocalFileStorageProvider.cs`, `Explore.Infrastructure/Storage/S3ConfigResolver.cs`, `Explore.Infrastructure/StorageReconciliationSettings.cs`, `Explore.Infrastructure/Mail/SmtpConfigResolver.cs`, `Explore.Infrastructure/Services/SetupSecretProvider.cs`, `Explore.Domain/Constants/GovernanceSettingKeys.cs`, `Explore.Domain/Constants/InfrastructureSecretSettingKeys.cs`, `Explore.Domain/Secrets/SecretDefinitionRegistry.cs`, `docs/SECRETS.md`

## Environment Variables & Secrets Authority

The canonical reference catalogue of all deployment, identity, database, and integration environment variables is maintained in the **public documentation**:

👉 **[Environment Variables Reference (Public Documentation)](../public/documentation/readme/configuration-and-operations/environment-variables.md)**

Runtime secret binding remains authoritative in `Explore.Domain/Secrets/SecretDefinitionRegistry.cs`. Deployment topology and port mappings are governed by `docker-compose.yml`. For operational runbooks and secret provider configuration (Environment vs. Infisical), consult [SECRETS.md](SECRETS.md).

<!-- BEGIN GENERATED ENVIRONMENT CATALOGUE -->
## Generated Environment Catalogue

This bounded section is generated from the package-free Core catalogue. Runtime secret binding remains authoritative in `SecretDefinitionRegistry`; Compose topology remains owned by `docker-compose.yml`.

Source anchors: `src/Event.Setup.Core/Environment/`, `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs`, `.env.example`, and `docker-compose.yml`.

```bash
dotnet run --project eng/setup-assistant/EnvironmentCatalogueGenerator/EnvironmentCatalogueGenerator.csproj --configuration Release -- --write
dotnet run --project eng/setup-assistant/EnvironmentCatalogueGenerator/EnvironmentCatalogueGenerator.csproj --configuration Release -- --check
```

| Key | Category | Sensitivity | Requirement | Restart | Surfaces |
|---|---|---|---|---|---|
| `PUBLIC_BASE_URL` | platform | public | required | process | dotenv, compose, startup |
| `API_HTTP_PORT` | deployment | public | optional | deployment | dotenv, compose, startup |
| `UI_HTTP_PORT` | deployment | public | optional | deployment | dotenv, compose, startup |
| `KEYCLOAK_HTTP_PORT` | identity | public | optional | process | dotenv, compose, startup |
| `MAILPIT_SMTP_PORT` | integration | public | optional | deployment | dotenv, compose |
| `MAILPIT_UI_PORT` | integration | public | optional | deployment | dotenv, compose |
| `DEPLOYMENT_MODE` | deployment | public | optional | deployment | dotenv, compose, startup |
| `SECRET_PROVIDER` | security | sensitive | required | process | dotenv, compose, startup |
| `DATABASE_PROVIDER` | database | public | defaulted | process | dotenv, compose, startup |
| `DATABASE_HOST` | database | public | required | process | dotenv, compose, startup |
| `DATABASE_PORT` | database | public | optional | process | dotenv, compose, startup |
| `DATABASE_NAME` | database | public | required | process | dotenv, compose, startup |
| `DATABASE_SCHEMA` | database | public | defaulted | process | dotenv, compose, startup |
| `DATABASE_RUNTIME_USERNAME` | database | public | required | process | dotenv, compose, startup |
| `DATABASE_RUNTIME_PASSWORD` | database | sensitive | required | process | dotenv, compose, startup |
| `DATABASE_MIGRATOR_USERNAME` | database | public | required | process | dotenv, compose, startup |
| `DATABASE_MIGRATOR_PASSWORD` | database | sensitive | required | process | dotenv, compose, startup |
| `DATABASE_TLS_MODE` | database | public | defaulted | process | dotenv, compose, startup |
| `AUTHENTICATION_PROVIDER` | platform | public | optional | process | dotenv, compose, startup |
| `ATPROTO_LOGIN_ENABLED` | platform | public | optional | process | dotenv, compose, startup |
| `INSTANCE_BOOTSTRAP_ADMIN_PROVIDER` | identity | public | required | process | dotenv, startup |
| `INSTANCE_BOOTSTRAP_ADMIN_SUBJECT` | identity | sensitive | required | process | dotenv, startup |
| `INSTANCE_BOOTSTRAP_BINDING_GENERATION` | identity | public | required | process | dotenv, startup |
| `INSTANCE_BOOTSTRAP_ADMIN_EMAIL` | identity | sensitive | required | process | dotenv, startup |
| `INSTANCE_BOOTSTRAP_ADMIN_FIRST_NAME` | identity | sensitive | optional | process | dotenv, startup |
| `INSTANCE_BOOTSTRAP_ADMIN_LAST_NAME` | identity | sensitive | optional | process | dotenv, startup |
| `AUTHENTICATION_LOCAL_JWT_KEY` | security | secret | optional | process | dotenv, compose, startup |
| `AUTHENTICATION_LOCAL_LOCKOUT_THRESHOLD` | security | public | defaulted | process | dotenv, compose, startup |
| `AUTHENTICATION_LOCAL_LOCKOUT_DURATION_MINUTES` | security | public | defaulted | process | dotenv, compose, startup |
| `IDENTITY_DATABASE_TOPOLOGY` | integration | public | defaulted | process | dotenv, compose, startup |
| `KEYCLOAK_ENDPOINT` | identity | secret | required | process | dotenv, compose, startup |
| `KEYCLOAK_REALM` | identity | secret | required | process | dotenv, compose, startup |
| `KEYCLOAK_BLAZOR_CLIENT_ID` | identity | public | required | process | dotenv, compose, startup |
| `KEYCLOAK_BLAZOR_CLIENT_SECRET` | identity | secret | required | process | dotenv, compose, startup |
| `KEYCLOAK_DB_DATABASE` | integration | public | optional | deployment | dotenv, compose |
| `KEYCLOAK_DB_USERNAME` | integration | public | optional | deployment | dotenv, compose |
| `KEYCLOAK_DB_PASSWORD` | integration | secret | optional | deployment | dotenv, compose |
| `KEYCLOAK_ADMIN` | integration | public | optional | deployment | dotenv, compose |
| `KEYCLOAK_ADMIN_PASSWORD` | integration | secret | optional | deployment | dotenv, compose |
| `LOCAL_STORAGE_ROOT_PATH` | storage | public | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_HOST` | messaging | secret | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_PORT` | messaging | secret | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_FROM_ADDRESS` | messaging | secret | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_FROM_NAME` | messaging | secret | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_USERNAME` | messaging | secret | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_PASSWORD` | messaging | secret | optional | capability | dotenv, compose, startup |
| `MAIL_SMTP_ENCRYPTION` | messaging | public | optional | capability | dotenv, compose, startup |
| `ERASURE_TOPOLOGY` | platform | public | optional | process | dotenv, compose, startup |
| `ERASURE_EMBEDDED_PATH` | platform | public | optional | process | dotenv, compose, startup |
| `SETUP_SECRET` | platform | secret | required | process | dotenv, compose, startup |
| `INSTANCE_BOOTSTRAP_MODE` | identity | public | required | process | dotenv, startup |
| `INSTANCE__OPERATORIDENTITY__OPERATORID` | identity | public | required | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__PUBLICNAME` | identity | public | required | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__LEGALNAME` | identity | public | required | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__ISOFFICIALINSTANCE` | identity | public | optional | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__OPERATORKINDCODE` | identity | public | required | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__JURISDICTIONCOUNTRYCODE` | identity | public | required | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__PUBLICCONTACTEMAIL` | identity | sensitive | optional | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__WEBSITEURL` | identity | public | optional | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__LEGALNOTICEURL` | identity | public | optional | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__TERMSURL` | identity | public | optional | process | dotenv, compose, startup |
| `INSTANCE__OPERATORIDENTITY__PRIVACYURL` | identity | public | optional | process | dotenv, compose, startup |
| `CONFIGURATION_MANIFEST_MODE` | deployment | public | defaulted | deployment | dotenv, compose, startup |
| `CONFIGURATION_MANIFEST_PATH` | deployment | public | optional | deployment | dotenv, compose, startup |
| `CONFIGURATION_MANIFEST_HOST_DIRECTORY` | deployment | public | optional | deployment | dotenv, compose, startup |
| `MINIO_API_PORT` | integration | public | optional | deployment | compose |
| `MINIO_CONSOLE_PORT` | integration | public | optional | deployment | compose |
| `CERBOS_HTTP_PORT` | deployment | public | optional | deployment | compose, startup |
| `CERBOS_GRPC_PORT` | deployment | public | optional | deployment | compose, startup |
| `SVIX_HTTP_PORT` | integration | public | optional | deployment | compose |
| `WEBLATE_HTTP_PORT` | integration | public | optional | deployment | compose |
| `COOP_HTTP_PORT` | integration | public | optional | deployment | compose |
| `COOP_CLIENT_HTTP_PORT` | integration | public | optional | deployment | compose |
| `OSPREY_BIDI_STREAM_PORT` | integration | public | optional | deployment | compose |
| `OSPREY_SYNC_ACTION_PORT` | integration | public | optional | deployment | compose |
| `MAILPIT_TAG` | integration | public | optional | deployment | compose |
| `MAILPIT_MAX_MESSAGES` | integration | public | optional | deployment | compose |
| `FORMBRICKS_HTTP_PORT` | integration | public | optional | deployment | compose |
| `FORMBRICKS_WEBAPP_URL` | integration | public | optional | deployment | compose |
| `FORMBRICKS_DATABASE_NAME` | integration | public | optional | deployment | compose |
| `FORMBRICKS_DATABASE_USER` | integration | public | optional | deployment | compose |
| `FORMBRICKS_DATABASE_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `FORMBRICKS_NEXTAUTH_SECRET` | integration | sensitive | required | deployment | compose |
| `FORMBRICKS_ENCRYPTION_KEY` | integration | sensitive | required | deployment | compose |
| `FORMBRICKS_CRON_SECRET` | integration | sensitive | required | deployment | compose |
| `FORMBRICKS_HUB_API_KEY` | integration | sensitive | required | deployment | compose |
| `FORMBRICKS_CUBEJS_API_SECRET` | integration | sensitive | required | deployment | compose |
| `INFISICAL_URL` | security | public | optional | process | compose, startup |
| `INFISICAL_PROJECT_ID` | security | public | optional | process | compose, startup |
| `INFISICAL_CLIENT_ID` | security | public | optional | process | compose, startup |
| `INFISICAL_CLIENT_SECRET` | security | sensitive | optional | process | compose, startup |
| `INFISICAL_ENV` | security | public | optional | process | compose, startup |
| `DATABASE_TRUST_SERVER_CERTIFICATE` | database | public | defaulted | process | compose, startup |
| `KEYCLOAK_API_CLIENT_SECRET` | identity | secret | required | process | compose, startup |
| `KEYCLOAK_BLAZOR_REDIRECT_URIS` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_BLAZOR_WEB_ORIGINS` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_HOST` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_PORT` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_FROM` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_FROM_DISPLAY_NAME` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_AUTH` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_SSL` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_STARTTLS` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_REPLY_TO` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_REPLY_TO_DISPLAY_NAME` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_ENVELOPE_FROM` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_USER` | identity | public | optional | process | compose, startup |
| `KEYCLOAK_SMTP_PASSWORD` | identity | sensitive | optional | process | compose, startup |
| `KEYCLOAK_REQUIRE_HTTPS_METADATA` | identity | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_PROVIDER` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_CONNECTION_STRING` | integration | secret | optional | process | compose, startup |
| `IDENTITY_DATABASE_HOST` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_PORT` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_NAME` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_SCHEMA` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_TLS_MODE` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_TRUST_SERVER_CERTIFICATE` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_RUNTIME_USERNAME` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_RUNTIME_PASSWORD` | integration | secret | optional | process | compose, startup |
| `IDENTITY_DATABASE_MIGRATOR_USERNAME` | integration | public | optional | process | compose, startup |
| `IDENTITY_DATABASE_MIGRATOR_PASSWORD` | integration | secret | optional | process | compose, startup |
| `SETUP_SECRET_REQUIRED` | platform | sensitive | optional | process | compose, startup |
| `HOSTING_REPLICA_COUNT` | deployment | public | defaulted | deployment | compose, startup |
| `PROMOTIONS_CODE_LOOKUP_ACTIVE_KEY_VERSION` | platform | public | optional | process | compose, startup |
| `PROMOTIONS_CODE_LOOKUP_HMAC_KEY` | security | secret | optional | process | compose, startup |
| `CONTROL_PLANE_MANAGED_MODE` | deployment | public | optional | deployment | startup |
| `CONTROL_PLANE_URL` | deployment | public | optional | deployment | startup |
| `CONTROL_PLANE_INSTANCE_ID` | deployment | public | optional | deployment | startup |
| `CONTROL_PLANE_REGISTRATION_TOKEN` | deployment | sensitive | optional | deployment | startup |
| `CONTROL_PLANE_MAXIMUM_TENANT_COUNT` | deployment | public | optional | deployment | startup |
| `CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL` | deployment | public | optional | deployment | startup |
| `USE_COMMERCIAL_LUCKYPENNY` | platform | public | optional | process | compose, startup |
| `LUCKYPENNY_LICENSE_KEY` | platform | sensitive | optional | process | compose, startup |
| `AUTOMAPPER_COMMERCIAL_VERSION` | platform | public | optional | process | compose, startup |
| `MEDIATR_COMMERCIAL_VERSION` | platform | public | optional | process | compose, startup |
| `GEOCODING_PROVIDER` | integration | public | optional | capability | compose, startup |
| `GEOCODING_ENDPOINT` | integration | public | optional | capability | compose, startup |
| `GEOCODING_LANGUAGE` | integration | public | optional | capability | compose, startup |
| `GEOCODING_COUNTRY_CODES` | integration | public | optional | capability | compose, startup |
| `GEOCODING_MAXIMUM_RESULTS` | integration | public | optional | capability | compose, startup |
| `GEOCODING_MAXIMUM_RESPONSE_BYTES` | integration | public | optional | capability | compose, startup |
| `GEOCODING_DATASET_VERSION` | integration | public | optional | capability | compose, startup |
| `GEOCODING_TOTAL_TIMEOUT_MILLISECONDS` | integration | public | optional | capability | compose, startup |
| `GEOCODING_MAXIMUM_RETRY_COUNT` | integration | public | optional | capability | compose, startup |
| `GEOCODING_RETRY_DELAYS_MILLISECONDS` | integration | public | optional | capability | compose, startup |
| `GEOCODING_READINESS_TIMEOUT_MILLISECONDS` | integration | public | optional | capability | compose, startup |
| `GEOCODING_SELECTION_LIFETIME_SECONDS` | integration | public | optional | capability | compose, startup |
| `PROVISIONING_TRUSTED` | platform | public | optional | process | startup |
| `PROVISIONING_MODE` | platform | public | optional | process | startup |
| `MANAGED_CLIENT_EXTERNAL_PROVIDER` | platform | public | optional | process | startup |
| `PHYSICAL_TENANCY_MODE` | platform | public | optional | process | startup |
| `API_ENDPOINT` | platform | public | optional | process | compose, startup |
| `CONTROL_PLANE_PUBLIC_ORIGIN` | deployment | public | optional | deployment | compose, startup |
| `INSTANCE__OPERATORIDENTITY__OFFICIALORIGIN` | identity | public | optional | process | compose, startup |
| `INSTANCE__OPERATORIDENTITY__REGISTRATIONIDENTIFIER` | identity | public | optional | process | compose, startup |
| `PAYMENTS_STRIPE_MODE` | platform | public | optional | process | compose, startup |
| `PAYMENTS_ORGANIZER_DIRECT_PROVIDER_CODE` | platform | public | optional | process | compose, startup |
| `PAYMENTS_ORGANIZER_DIRECT_CONNECT_PLATFORM_ID` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__COMPLAINTOWNER` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__REFUNDOWNER` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__DISPUTEOWNER` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__RECONCILIATIONOWNER` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__ACTIVATIONSTATUS` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__REFUNDPOLICYLANGUAGETAG` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__STATEMENTDESCRIPTOR` | platform | public | optional | process | compose, startup |
| `PAYMENTS__CHECKOUTGOVERNANCE__CHARGETYPE` | platform | public | optional | process | compose, startup |
| `STRIPE_PLATFORM_SECRET_KEY` | platform | secret | optional | process | compose, startup |
| `STRIPE_WEBHOOK_SECRET` | platform | secret | optional | process | compose, startup |
| `ADMISSIONS_CREDENTIAL_LOOKUP_HMAC_KEY` | security | secret | optional | process | startup |
| `ADMISSIONS__CREDENTIALLOOKUP__ACTIVEKEYVERSION` | security | public | optional | process | startup |
| `ADMISSIONS_SCANNER_CAPABILITY_HMAC_KEY` | security | secret | optional | process | startup |
| `ADMISSIONS_RECOVERY_CAPABILITY_HMAC_KEY` | security | secret | optional | process | startup |
| `ADMISSIONS__RECOVERY__ACTIVEKEYVERSION` | security | public | optional | process | startup |
| `ADMISSIONS__RECOVERY__CAPABILITYLIFETIMEMINUTES` | security | public | optional | process | startup |
| `ADMISSIONS__RECOVERY__RATELIMITBUCKETCOUNT` | security | public | optional | process | startup |
| `ADMISSIONS__RECOVERY__RATELIMITPERMITCOUNT` | security | public | optional | process | startup |
| `ADMISSIONS__RECOVERY__RATELIMITWINDOWSECONDS` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__ENABLED` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__EXPECTEDRELEASEREVISION` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__EXPECTEDSCHEMAREVISION` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__MINIMUMRETAINEDKEYVERSION` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__MINIMUMAUTHORITYFLOOR` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__MINIMUMPROVIDERCURSOR` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__MINIMUMIDEMPOTENCYFLOOR` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__MINIMUMWORKERFENCE` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__WARNINGOLDESTDUESECONDS` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__UNHEALTHYOLDESTDUESECONDS` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__BACKLOGTHRESHOLD` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__DECLAREDRPOMINUTES` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__DECLAREDRTOMINUTES` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__MANIFESTSIGNINGKEYREFERENCE` | security | public | optional | process | startup |
| `TICKETING__RECOVERY__RETAINEDKEYVERSIONS__0` | security | public | optional | process | startup |
| `TICKETING_RECOVERY_MANIFEST_HMAC_KEY` | security | secret | required | process | startup |
| `AUTHORIZATION_PROVIDER` | platform | public | optional | process | compose, startup |
| `CERBOS_GRPC_ENDPOINT` | integration | secret | optional | capability | compose, startup |
| `CERBOS_HTTP_ENDPOINT` | integration | public | optional | capability | compose, startup |
| `CERBOS_USE_TLS` | integration | public | optional | capability | compose, startup |
| `CERBOS_PLAINTEXT_MODE` | integration | public | optional | capability | compose, startup |
| `CERBOS_ADMIN_USERNAME` | integration | secret | optional | capability | compose, startup |
| `CERBOS_ADMIN_PASSWORD_HASH` | integration | sensitive | optional | capability | compose, startup |
| `CERBOS_ADMIN_PASSWORD` | integration | secret | optional | capability | compose, startup |
| `CERBOS_PG_URL` | integration | public | optional | capability | compose, startup |
| `CERBOS_POSTGRES_USER` | integration | public | optional | deployment | compose |
| `CERBOS_POSTGRES_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `CERBOS_POSTGRES_DB` | integration | public | optional | deployment | compose |
| `STORAGE_S3_ENDPOINT` | storage | secret | optional | capability | compose, startup |
| `STORAGE_S3_PUBLIC_ENDPOINT` | storage | secret | optional | capability | compose, startup |
| `STORAGE_S3_REGION` | storage | secret | optional | capability | compose, startup |
| `STORAGE_S3_BUCKET_NAME` | storage | secret | optional | capability | compose, startup |
| `STORAGE_S3_ACCESS_KEY_ID` | storage | secret | optional | capability | compose, startup |
| `STORAGE_S3_SECRET_ACCESS_KEY` | storage | secret | optional | capability | compose, startup |
| `LOCAL_STORAGE_CREATE_ROOT_IF_MISSING` | storage | public | optional | capability | compose, startup |
| `STORAGE_RECONCILIATION_ENABLED` | storage | public | defaulted | capability | compose, startup |
| `STORAGE_RECONCILIATION_DRY_RUN` | storage | public | defaulted | capability | compose, startup |
| `STORAGE_RECONCILIATION_QUARANTINE_MISSING_OBJECTS` | storage | public | optional | capability | compose, startup |
| `STORAGE_RECONCILIATION_QUARANTINE_ORPHAN_LOCAL_FILES` | storage | public | optional | capability | compose, startup |
| `STORAGE_RECONCILIATION_DELETE_QUARANTINED_OBJECTS` | storage | public | optional | capability | compose, startup |
| `AI_PROVIDER` | integration | public | optional | capability | compose, startup |
| `AI_ENDPOINT` | integration | public | optional | capability | compose, startup |
| `AI_MODEL_ID` | integration | public | optional | capability | compose, startup |
| `AI_API_KEY` | integration | sensitive | optional | capability | compose, startup |
| `AI_TOOL_PROPOSALS_ENABLED` | integration | public | defaulted | capability | compose, startup |
| `MCP_ENABLED` | platform | public | defaulted | process | compose, startup |
| `MCP_ENDPOINT_PATH` | platform | public | optional | process | compose, startup |
| `MCP_STATELESS` | platform | public | optional | process | compose, startup |
| `MCP_ENABLE_LEGACY_SSE` | platform | public | optional | process | compose, startup |
| `WEB_PUSH_ENABLED` | messaging | public | defaulted | capability | compose, startup |
| `VAPID_SUBJECT` | messaging | public | optional | capability | compose, startup |
| `VAPID_PUBLIC_KEY` | messaging | public | optional | capability | compose, startup |
| `VAPID_PRIVATE_KEY` | messaging | sensitive | optional | capability | compose, startup |
| `MESSAGING_URI` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_ENABLED` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_CONNECTION_STRING_NAME` | messaging | sensitive | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_CONNECTION_STRING` | messaging | sensitive | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_EXCHANGE_NAME` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DISPATCH_QUEUE_NAME` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DISPATCH_ROUTING_KEY` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_EXCHANGE_NAME` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_QUEUE_NAME` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_ROUTING_KEY` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PARKING_QUEUE_NAME` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PARKING_ROUTING_KEY` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_CLIENT_PROVIDED_NAME` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_CONSUMER_ID` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PREFETCH_COUNT` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_ENABLED` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_CONSUMER_ID` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_DEAD_LETTER_REPLAY_PREFETCH_COUNT` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISH_TIMEOUT_SECONDS` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_POLLING_INTERVAL_SECONDS` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_BATCH_SIZE` | messaging | public | optional | capability | compose, startup |
| `EMAIL_DISPATCH_RABBITMQ_PUBLISHER_RETRY_DELAY_SECONDS` | messaging | public | optional | capability | compose, startup |
| `WEBHOOKS_ENABLED` | integration | public | defaulted | capability | compose, startup |
| `WEBHOOKS_PROVIDER` | integration | public | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_BASE_URL` | integration | public | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_ENVIRONMENT` | integration | public | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_PROVIDER_VERSION` | integration | public | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_CAPABILITY_POLICY_VERSION` | integration | public | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_AUTH_TOKEN_SECRET_REF` | integration | sensitive | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET_REF` | integration | sensitive | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_AUTH_TOKEN` | integration | secret | optional | capability | compose, startup |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET` | integration | secret | optional | capability | compose, startup |
| `SVIX_TAG` | integration | public | optional | deployment | compose |
| `SVIX_DB_DATABASE` | integration | public | optional | deployment | compose |
| `SVIX_DB_USERNAME` | integration | public | optional | deployment | compose |
| `SVIX_DB_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `SVIX_DB_DSN` | integration | public | optional | deployment | compose |
| `SVIX_REDIS_DSN` | integration | public | optional | deployment | compose |
| `SVIX_QUEUE_TYPE` | integration | public | optional | deployment | compose |
| `SVIX_CACHE_TYPE` | integration | public | optional | deployment | compose |
| `SVIX_JWT_SECRET` | integration | sensitive | optional | deployment | compose |
| `WEBLATE_IMAGE` | integration | public | optional | deployment | compose |
| `WEBLATE_SITE_DOMAIN` | integration | public | optional | deployment | compose |
| `WEBLATE_ADMIN_NAME` | integration | public | optional | deployment | compose |
| `WEBLATE_ADMIN_EMAIL` | integration | sensitive | optional | deployment | compose |
| `WEBLATE_ADMIN_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `WEBLATE_POSTGRES_USER` | integration | public | optional | deployment | compose |
| `WEBLATE_POSTGRES_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `WEBLATE_POSTGRES_DB` | integration | public | optional | deployment | compose |
| `REPORTING_ENABLED` | observability | public | defaulted | none | compose, startup |
| `REPORTING_MODE` | observability | public | optional | none | compose, startup |
| `REPORTING_SYNC_REPORTS` | observability | public | optional | none | compose, startup |
| `REPORTING_EVALUATE_SIGNALS` | observability | public | optional | none | compose, startup |
| `REPORTING_MIRROR_REVIEW_QUEUE` | observability | public | optional | none | compose, startup |
| `REPORTING_EXECUTE_DECISIONS` | observability | public | optional | none | compose, startup |
| `REPORTING_HEALTH_STUCK_PROVIDER_SYNC_MINUTES` | observability | public | optional | none | startup |
| `REPORTING_HEALTH_FAILED_PROVIDER_SYNC_WARNING_THRESHOLD` | observability | public | optional | none | startup |
| `REPORTING_OSPREY_ENABLED` | observability | public | optional | none | compose, startup |
| `REPORTING_OSPREY_ENDPOINT_URL` | observability | public | optional | none | compose, startup |
| `REPORTING_OSPREY_API_KEY` | observability | sensitive | optional | none | compose, startup |
| `REPORTING_OSPREY_ALLOW_LOCAL_PROVIDER_ENDPOINTS` | observability | public | optional | none | compose, startup |
| `REPORTING_COOP_ENABLED` | observability | public | optional | none | compose, startup |
| `REPORTING_COOP_ENDPOINT_URL` | observability | public | optional | none | compose, startup |
| `REPORTING_COOP_API_KEY` | observability | sensitive | optional | none | compose, startup |
| `REPORTING_COOP_ALLOW_LOCAL_PROVIDER_ENDPOINTS` | observability | public | optional | none | compose, startup |
| `REPORTING_COOP_WEBHOOK_SECRET` | observability | sensitive | optional | none | compose, startup |
| `COOP_IMAGE` | integration | public | optional | deployment | compose |
| `COOP_MIGRATIONS_IMAGE` | integration | public | optional | deployment | compose |
| `COOP_CLIENT_IMAGE` | integration | public | optional | deployment | compose |
| `COOP_NODE_ENV` | integration | public | optional | deployment | compose |
| `COOP_OTEL_SERVICE_NAME` | integration | public | optional | deployment | compose |
| `COOP_UI_URL` | integration | public | optional | deployment | compose |
| `COOP_SESSION_SECRET` | integration | sensitive | optional | deployment | compose |
| `COOP_DATABASE_USER` | integration | public | optional | deployment | compose |
| `COOP_DATABASE_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `COOP_DATABASE_NAME` | integration | public | optional | deployment | compose |
| `COOP_WAREHOUSE_ADAPTER` | integration | public | optional | deployment | compose |
| `COOP_ANALYTICS_ADAPTER` | integration | public | optional | deployment | compose |
| `COOP_SCYLLA_HOSTS` | integration | public | optional | deployment | compose |
| `COOP_SCYLLA_USERNAME` | integration | public | optional | deployment | compose |
| `COOP_SCYLLA_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `COOP_SCYLLA_LOCAL_DATACENTER` | integration | public | optional | deployment | compose |
| `COOP_SCYLLA_SSL` | integration | public | optional | deployment | compose |
| `OSPREY_IMAGE` | integration | public | optional | deployment | compose |
| `OSPREY_RUST_LOG` | integration | public | optional | deployment | compose |
| `OSPREY_POD_IP` | integration | public | optional | deployment | compose |
| `LISTMONK_ENABLED` | integration | public | defaulted | capability | compose, startup |
| `LISTMONK_INSTANCE_URL` | integration | public | optional | capability | compose, startup |
| `LISTMONK_DEFAULT_LIST_ID` | integration | public | optional | capability | compose, startup |
| `LISTMONK_PRECONFIRM_SUBSCRIPTIONS` | integration | public | optional | capability | compose, startup |
| `LISTMONK_SYNC_ON_REGISTRATION` | integration | public | optional | capability | compose, startup |
| `LISTMONK_API_USERNAME` | integration | secret | optional | capability | compose, startup |
| `LISTMONK_API_KEY` | integration | secret | optional | capability | compose, startup |
| `ERASURE_WRITER_REPLICA_COUNT` | platform | public | optional | process | compose, startup |
| `ERASURE_BUSY_TIMEOUT_SECONDS` | platform | public | optional | process | compose, startup |
| `DATABASE_ERASURE_HOST` | database | public | optional | process | compose, startup |
| `DATABASE_ERASURE_PORT` | database | public | optional | process | compose, startup |
| `DATABASE_ERASURE_NAME` | database | public | optional | process | compose, startup |
| `DATABASE_ERASURE_RUNTIME_USERNAME` | database | public | optional | process | compose, startup |
| `DATABASE_ERASURE_RUNTIME_PASSWORD` | database | sensitive | optional | process | compose, startup |
| `DATABASE_ERASURE_MIGRATOR_USERNAME` | database | public | optional | process | compose, startup |
| `DATABASE_ERASURE_MIGRATOR_PASSWORD` | database | sensitive | optional | process | compose, startup |
| `DATABASE_ERASURE_TLS_MODE` | database | public | optional | process | compose, startup |
| `DATABASE_ERASURE_TRUST_SERVER_CERTIFICATE` | database | public | optional | process | compose, startup |
| `DATABASE_SERVER_VERSION` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_TOPOLOGY` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_EMBEDDED_PATH` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_WRITER_REPLICA_COUNT` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_BUSY_TIMEOUT_SECONDS` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_HOST` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_PORT` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_DATABASE` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_TLS_MODE` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_TRUST_SERVER_CERTIFICATE` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_RUNTIME_USERNAME` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_RUNTIME_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_MIGRATOR_USERNAME` | integration | public | optional | deployment | compose |
| `PRIVACY_ERASURE_AUTHORITY_MIGRATOR_PASSWORD` | integration | sensitive | optional | deployment | compose |
| `SETUP_SECRET_FILE` | integration | sensitive | optional | deployment | compose |
| `KEYCLOAK_INTERNAL_URL` | integration | public | optional | deployment | compose |
| `REDIS_CONNECTION_STRING` | integration | sensitive | optional | deployment | compose |
| `SETUP_SECRET_BINDING_COMMITMENT_HMAC_KEY` | security | secret | optional | process | startup |
| `KEYCLOAK_CLIENT_ID` | identity | secret | optional | process | startup |
| `KEYCLOAK_ADMIN_USERNAME` | identity | secret | optional | process | startup |
| `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` | platform | secret | optional | process | startup |
| `ATPROTO_SESSION_ENCRYPTION_KEYRING` | platform | secret | optional | process | startup |
| `ATPROTO_SESSION_JWT_PRIVATE_JWKS` | platform | secret | optional | process | startup |
| `REGISTRATION_PROVIDER_API_TOKEN` | platform | secret | optional | process | startup |
| `REGISTRATION_PROVIDER_WEBHOOK_SECRET` | platform | secret | optional | process | startup |
| `POSTGRESQL_HOST` | database | secret | required | process | startup |
| `POSTGRESQL_PORT` | database | secret | required | process | startup |
| `POSTGRESQL_DATABASE` | database | secret | required | process | startup |
| `POSTGRESQL_USERNAME` | database | secret | required | process | startup |
| `POSTGRESQL_PASSWORD` | database | secret | required | process | startup |
| `ANALYTICS_POSTHOG_PUBLIC_KEY` | observability | secret | optional | none | startup |
| `ANALYTICS_POSTHOG_HOST` | observability | secret | optional | none | startup |
| `ANALYTICS_PERSONAL_API_KEY` | observability | secret | optional | none | startup |
| `LOCALIZATION_TMS_API_KEY` | platform | secret | optional | process | startup |
| `CONTROL_PLANE_REGISTRATION_CREDENTIALS` | deployment | secret | optional | deployment | startup |
| `AI_OPENAI_API_KEY` | integration | secret | optional | capability | startup |
| `AI_ANTHROPIC_API_KEY` | integration | secret | optional | capability | startup |
<!-- END GENERATED ENVIRONMENT CATALOGUE -->

## Runtime Configuration Sources

The system separates ordinary configuration from secret authority:

1. non-secret static settings (`appsettings*.json`, environment variables),
2. one explicit secret authority (`Environment`, `Infisical`, or Development/Testing-only `UserSecrets`),
3. non-secret governance settings in database (`SystemSetting` + `TenantSetting`).

Secrets have an additional ownership contract that applies across the platform:

- Application-managed database settings are non-secret only.
- Deployment-managed secret values remain in explicit environment injection or Infisical; local Development/Testing may explicitly use the shared User Secrets store. UI surfaces show only value-free ownership/status metadata.
- Purpose-bound setup credentials are request/job scoped and never become persisted runtime secret values.

The selected secret authority is absolute for that deployment. Infisical failure
never falls back to environment, appsettings, User Secrets, or database values.
Runtime consumers receive explicit `Resolved`, `Unconfigured`, `Unavailable`,
`Unauthorized`, or `Invalid` outcomes. Required capabilities fail closed for every
non-resolved state; optional capabilities report a bounded disabled/degraded state
without provider coordinates. Successful resolutions are cached process-locally for
up to five minutes and invalidated on safe binding metadata changes; failures are
not cached as successful absence. See [Secrets Management](SECRETS.md#runtime-resolution-outcomes-and-bounded-freshness).

## Configuration Manifest Contract

The governed editor and operator contract is
[`schemas/configuration-manifest-v1alpha2.schema.json`](../../schemas/configuration-manifest-v1alpha2.schema.json).
It uses JSON Schema Draft 2020-12 with immutable identifier
`https://schemas.islamu.org/event/configuration-manifest/v1alpha1/schema.json`.
That URL is currently an identifier, not a promise that a public endpoint serves the
bytes. Documentation must not describe it as downloadable until deployment evidence
proves the published bytes match the checked-in artifact.

The current envelope is exact and case-sensitive:

- `$schema`: `https://schemas.islamu.org/event/configuration-manifest/v1alpha1/schema.json`
- `apiVersion`: `configuration.islamu.org/v1alpha1`
- `kind`: `ConfigurationManifest`
- required `spec.instance.settings` and `spec.instance.documents` closed maps
- required `spec.tenants` array containing one or more unique tenant entries

The same envelope configures single-tenant and multi-tenant deployments. The
instance and tenant catalogs are independent explicit allowlists; adding a
setting to the general registry never makes it manifest-owned. Credentials,
provider configuration, deployment topology, operational locks, audit state,
PII, and persistence identities remain excluded. `additionalProperties: false`
closes every typed object.

Generate or verify the artifact with the repository-pinned .NET SDK:

```bash
dotnet run \
  --project eng/configuration-manifest-schema/src/ISLAMU.ConfigurationManifest.SchemaGenerator/ISLAMU.ConfigurationManifest.SchemaGenerator.csproj \
  --configuration Release -- \
  --write schemas/configuration-manifest-v1alpha2.schema.json

dotnet run \
  --project eng/configuration-manifest-schema/src/ISLAMU.ConfigurationManifest.SchemaGenerator/ISLAMU.ConfigurationManifest.SchemaGenerator.csproj \
  --configuration Release -- \
  --check schemas/configuration-manifest-v1alpha2.schema.json
```

Never hand-edit the generated artifact. JSON Schema supports editor completion and
preflight diagnostics, but the bounded runtime reader and Application validator remain
authoritative for duplicate keys, exact UTF-8 bytes, publication-policy
cross-references, sensitive-key rejection, and bootstrap eligibility.

The ingestion option contract recognizes `CONFIGURATION_MANIFEST_MODE=Off`,
`ValidateOnly`, or `Bootstrap`, plus optional absolute
`CONFIGURATION_MANIFEST_PATH`. Startup application of those options is activated in the
post-migration bootstrap phase; the schema and reader contract alone do not perform
database writes.

## Setup Live Control-Plane Boundary

Setup live control is an authenticated, tenant-scoped outer adapter over the
generated API client. It does not add an appsetting, connection string, saved
profile, credential file, process environment variable, command-line token, or
portable-artifact field. The adapter accepts only an HTTPS target, obtains a
fresh bearer from its caller-owned ephemeral provider for each request, and
keeps the one-time enrollment capability in memory until expiry, revocation,
rotation failure, transport/authentication failure, cancellation after
dispatch, explicit clear, or disposal.

The server returns enrollment authority only through
`X-Setup-Enrollment-Capability`. Every mutation uses a fresh UUIDv7
`Idempotency-Key`; every follow-up request must match the exact tenant, actor,
enrollment, scope, capability generation, and expiry. Clients must follow the
server-authored HAL relation and method before invoking the generated client
operation. Absence, stale authority, and provider failures fail closed without
falling back to portable source identity or provider coordinates.

Secret-binding readiness and operation receipts are value-free. A secret write
is allowed only for the exact server-reported `Ready` binding key and sends raw
bytes once to the target's selected-authority writer; Setup cannot read the
value back. Environment and .NET User Secrets are not live-write providers.
Infisical writes require the already-selected stored authority and target-local
coordinates, which never enter Setup contracts, logs, metrics, or responses.
Recovery is to revoke the enrollment and clear the adapter's in-memory state;
there is no credential-persistence recovery path.

`eng/setup-assistant/generated/setup-live-release-capabilities.json` is the
canonical release gate. D2 closes with `targetEnabled` and every live-control
capability set to `false`; the backend and outer adapter therefore do not imply
an activated CLI, browser, desktop, or shared-presentation workflow.

## Aspire Hosting Topology

`Hosting:Topology` is an AppHost-only setting for local Aspire composition; it
does not change a deployed application's configuration source or migrate data.
Its environment form is `Hosting__Topology`.

| Key / environment variable | Values and default | Effect |
|---|---|---|
| `Hosting:Topology` / `Hosting__Topology` | `Split` (default) or `Standalone` | `Split` registers API plus Blazor; `Standalone` registers only `Event.Standalone`. Unknown values fail AppHost startup. |
| `CONTROL_PLANE_PUBLIC_ORIGIN` | Exact browser-facing admin origin; AppHost fallback `http://admin.localhost:7002` | Forwarded to the API or combined host for admin-link generation and used as the selected BFF admin-host value. |
| `Bff:AdminHosts:0` / `Bff__AdminHosts__0` | Same exact public admin origin | AppHost injects this onto the selected BFF surface. Set it explicitly only when running the BFF/combined host outside AppHost. |
| `ASPNETCORE_URLS` | Host binding chosen by the launch profile unless overridden | AppHost uses `WithHttpEndpoint(name: "http")` for dynamic internal HTTP and explicit HTTPS `https://localhost:7180`; direct `Event.Standalone` launch profiles reserve `http://localhost:5180` (and `https://localhost:7180` for the HTTPS profile). |

For an explicit local combined run, set the topology and an admin origin that
matches the browser-facing endpoint:

```bash
Hosting__Topology=Standalone \
CONTROL_PLANE_PUBLIC_ORIGIN=https://admin.localhost:7180 \
aspire run --apphost src/Explore.AppHost/Explore.AppHost.csproj
```

In `Split`, AppHost wires Blazor to API service discovery and waits for API
readiness. In `Standalone`, the one host owns API and BFF/UI startup once and
uses the in-process API bridge; that bridge remains the BFF/API trust boundary,
so no `API_ENDPOINT` or loopback URL is needed for it. Keycloak callback,
web-origin, and logout registration target the selected BFF/combined resource.

### Formbricks mirror deployment inputs

The optional Formbricks stack is deployment-owned infrastructure. Docker Compose starts it only with `--profile formbricks`; Aspire registers the same isolated dependency graph only for `ISLAMU_ASPIRE_MODE=FullLocal`. `DefaultLocal`, `ExternalInfra`, and `LocalDataExternalPlatform` do not register Formbricks resources.

| Key | Required | Purpose |
|---|---|---|
| `FORMBRICKS_WEBAPP_URL` | No; local default `http://localhost:3005` | Browser-facing Formbricks origin and NextAuth URL. |
| `FORMBRICKS_HTTP_PORT` | No; default `3005` | Compose host port for the Formbricks web app. |
| `FORMBRICKS_DATABASE_NAME`, `FORMBRICKS_DATABASE_USER`, `FORMBRICKS_DATABASE_PASSWORD` | No for local Compose | Credentials for the profile's isolated PostgreSQL database. |
| `FORMBRICKS_NEXTAUTH_SECRET` | Yes | NextAuth signing secret. |
| `FORMBRICKS_ENCRYPTION_KEY` | Yes | Formbricks encryption key for 2FA and single-use links. |
| `FORMBRICKS_CRON_SECRET` | Yes | Formbricks cron authorization secret. |
| `FORMBRICKS_HUB_API_KEY` | Yes | Shared app-to-Hub API key. |
| `FORMBRICKS_CUBEJS_API_SECRET` | Yes | Cube JWT/API authentication secret. |

Leave required secrets blank in the checked-in example; the Compose migration validates and fails closed. Aspire `FullLocal` generates persistent secret parameters when values are absent. Provider API tokens and webhook secrets remain tenant-scoped ISLAMU secret bindings and are not any of the container-bootstrap values above.

AppHost assigns standalone HTTP dynamically through `WithHttpEndpoint(name: "http")`; HTTPS remains explicitly `https://localhost:7180` for the combined endpoint. Direct `Event.Standalone` launch profiles reserve `http://localhost:5180`.

This configuration does not make SQLite the standalone default and does not
enable a standalone Docker Compose deployment. Those remain explicit provider
and packaging work; `docker-compose.yml` continues to use the Split services.

The three application composition roots (`Explore.API`, `Explore.Blazor`, and `Event.Standalone`) therefore share one API route convention: `/api/...` is canonical and API versioning uses `Accept`, `?api-version=`, or `X-Api-Version`; do not add a path-version segment (see [the support matrix](ARCHITECTURE.md#hosting-topology)). Switching back to the Split default changes only AppHost composition; it is not a data rollback.

### Keycloak onboarding metadata

The API compatibility layer maps `KEYCLOAK_CLIENT_ID` and `KEYCLOAK_BLAZOR_CLIENT_ID` to `Keycloak:ClientId`. It maps the server-only `KEYCLOAK_BLAZOR_CLIENT_SECRET` or `Keycloak:BlazorClientSecret` value to `Keycloak:ClientSecret`. Existing canonical `Keycloak:*` values retain precedence because compatibility aliases only fill missing keys. `Keycloak:Audience` identifies the API audience and is never used as the browser client ID.

Onboarding treats Keycloak deployment metadata as usable only when both the effective authority and client ID are nonblank. In application-managed mode, a complete enabled stored tuple wins; otherwise a complete deployment tuple is a bootstrap fallback and is reported as detected and configured. When authority and client ID are explicitly deployment-managed, the deployment tuple is authoritative and stored values for those fields are ignored. Partial deployment metadata fails closed and does not report Keycloak as configured.

Public setup reads, administrator configuration reads, status responses, and control-plane summaries expose only sanitized authority, client ID, enabled/detected state, and secret ownership metadata. They always return an empty `KeycloakClientSecret`. The effective secret is available only to trusted server-side paths such as BFF refresh and realm synchronization.

The browser BFF is a confidential OIDC client. A new Keycloak configuration therefore requires a client secret. Blank secret input is accepted only when the server's authoritative ownership metadata says an effective secret is already configured and the browser value is merely redacted; save and update handlers derive that state from `IAuthProviderConfigurationService` and never trust request ownership metadata.

## Deployment CI/CD Secrets

GitHub Actions deployment secrets are workflow secrets, not runtime app settings and not `SecretProvider` keys. Cerbos policy publishing reads repository secrets through the `${{ secrets.* }}` context while the job remains gated by the `production` GitHub Environment. Coolify deploy credentials are environment-scoped secrets so production deployments can require approval before those secrets are released to deploy jobs.

| Environment | Secret / variable | Purpose |
|---|---|---|
| `production` secret | `COOLIFY_DEPLOY_API_WEBHOOK` | Coolify API application deployment webhook. |
| `production` secret | `COOLIFY_DEPLOY_UI_WEBHOOK` | Coolify UI application deployment webhook. |
| `production` secret | `COOLIFY_DEPLOY_TOKEN` | Bearer token used when invoking production Coolify webhooks. |
| `production` variable | `PRODUCTION_URL` | Public environment URL shown on the GitHub deployment environment. |
| `production` variable | `PRODUCTION_API_URL` | Required API base URL for production `/alive` and `/health` smoke checks. |
| `production` variable | `PRODUCTION_UI_URL` | Required UI base URL for production `/alive` and `/health` smoke checks. |
| Repository secret | `CERBOS_SERVER` | Production Cerbos Admin API gRPC address used by the main-branch policy publish job. |
| Repository secret | `CERBOS_USERNAME` | Production Cerbos Admin API username used by `cerbosctl`. |
| Repository secret | `CERBOS_PASSWORD` | Production Cerbos Admin API password used by `cerbosctl`. |
| Repository secret | `CERBOS_CA_CERT_PEM` | Optional private CA certificate PEM for production Cerbos Admin API TLS verification. |
| `staging` secret | `COOLIFY_DEPLOY_API_STAGING_WEBHOOK` | Coolify staging API application deployment webhook. |
| `staging` secret | `COOLIFY_DEPLOY_UI_STAGING_WEBHOOK` | Coolify staging UI application deployment webhook. |
| `staging` secret | `COOLIFY_DEPLOY_TOKEN` | Bearer token used when invoking staging Coolify webhooks. |
| `staging` variable | `STAGING_URL` | Public environment URL shown on the GitHub deployment environment. |
| `staging` variable | `STAGING_API_URL` | Optional API base URL for `/alive` and `/health` smoke checks. |
| `staging` variable | `STAGING_UI_URL` | Optional UI base URL for `/alive` and `/health` smoke checks. |

Keep the existing secret names until the deploy workflows are consolidated. If names are normalized later, update the workflow and this table in the same change.

## Core Static Sections

Commonly consumed sections in code:

- `Keycloak:*` (authority, metadata, client IDs/secrets)
- `AccountAuthorityLifecycleEmail:*` (Application-level identity-email delegation switch)
- `KeycloakLifecycleEmail:*` (Keycloak Admin REST required-action email adapter)
- `Bff:Authentication:*` (explicit browser-BFF authority, metadata, client ID/secret, callback paths, and cookie policy overrides)
- `Database:*` (structured primary database selection and role credentials)
- `Cors:AllowedOrigins`
- `ForwardedHeadersTrust:*`
- `RateLimiting:*`
- `RequestTimeouts:*`
- `Cerbos:*`
- `Authorization:Provider` (deployment-owned `local`/`cerbos` selector; blank means manual onboarding)
- `Deployment:*`
- `PublicBaseUrl`, `App:PublicBaseUrl`, or `Application:PublicBaseUrl`
- `Bff:AdminHosts` and `Bff:AdminHostAllowedIpRanges`
- `Storage:Local:*` (deployment-managed local filesystem storage)
- `StorageReconciliation:*` (dry-run-first storage drift sweep; runs as the `storage-reconciliation` Quartz job)
- `s3.*` governance settings (non-secret S3 endpoint, bucket, region, and URL policy)
- `SecretProvider:*`
- `SecretRefresh:*`
- `EmailDispatchProcessor:*` (Basic Dispatch Mode background worker)
- `EmailDispatchRabbitMq:*` (optional RabbitMQ Dispatch Mode transport foundation)
- `EmailDispatchRetention:*` (bounded email content redaction; `email-dispatch-retention-cleanup` Quartz job)
- `IdempotencyCleanup:*` (expired write-retry replay-cache cleanup; `idempotency-cleanup` Quartz job)
- `AiRetentionCleanup:*` (tenant-scoped AI conversation retention; `ai-retention-cleanup` Quartz job)
- `WebhookRetention:*` (webhook message/attempt retention; `webhook-retention-cleanup` Quartz job)
- `OrganizerPaymentReadinessReconciliation:*` (`organizer-payment-readiness-reconciliation` Quartz job)
- `Payments:Stripe:*` and `Payments:OrganizerDirect:*` (Stripe mode, exact Checkout hosts, and non-secret commerce identity)
- `PrivacyErasure:ProviderPollingInterval` and `PrivacyErasure:RetentionCleanupEnabled` (`privacy-erasure-credential-cleanup` Quartz job)

> **Maintenance sweeps run on the scheduler.** The keys above are unchanged from when these ran as in-process
> timer loops — same section, same `Enabled` flag, same interval value — but the work is now Quartz jobs.
> Two consequences follow: a sweep whose `Enabled` flag is false is not registered at all (rather than
> starting and immediately returning), and **`Scheduler:Quartz:Enabled=false` disables these sweeps too**.
> An operator turning the scheduler off must intend retention and reconciliation to stop.
> See `docs/OPERATIONS.md` for the job catalog and the upgrade note on changed log-line shape.
- `AiProvider:*` (AI provider readiness/egress validation foundation)
- `Reporting:*` (local event-report submission limits, evidence retention, and provider runtime mode)
- `Listmonk:*` (deployment bootstrap for subscriber synchronization defaults and credentials)
- `IntegrationSyncProcessor:*` (`integration-sync-drain` cadence, lease recovery, batches, and retries)
- `Webhooks:*` (outgoing webhook provider mode, LocalProvider delivery limits, and Svix server-side integration)
- `Mcp:*` (optional Model Context Protocol adapter posture)
- `Persistence:*` (database runtime options)

### Persistence Configuration

The primary database is configured only through the structured `Database`
section. Raw application connection strings are not a supported deployment
input. Connection strings are constructed inside the process with the native
Npgsql, Microsoft.Data.Sqlite, Microsoft.Data.SqlClient, or MySqlConnector
builder after validation.

Shared endpoint fields live at the section root; credentials are separated by
process role:

```json
{
  "Database": {
    "Provider": "PostgreSql",
    "Host": "db.example.internal",
    "Port": 5432,
    "Database": "islamu_event",
    "Schema": "islamu_event",
    "TlsMode": "Required",
    "TrustServerCertificate": false,
    "Runtime": {
      "Username": "islamu_runtime",
      "Password": "deployment-secret"
    },
    "Migrator": {
      "Username": "islamu_migrator",
      "Password": "deployment-secret"
    }
  }
}
```

`Explore.API` and normal application data access bind the `Runtime` role.
`Event.MigrationService` binds the `Migrator` role and is the deployed owner of
application, Data Protection, and exactly one authority migration path selected
by the configured topology. The standalone process is the documented
in-process exception: it applies the same responsibilities before binding HTTP.
Do not put migrator credentials in the API or Blazor containers.

| Provider | Required shape | Default port / namespace | Operational boundary |
|---|---|---|---|
| `PostgreSql` | Host, database, and role credentials; `ServerFlavor`/`ServerVersion` forbidden | `5432`; configurable schema, default `islamu_event`; clean unprefixed table names | Full primary support; only provider supported by the external erasure authority. Quartz scheduling is supported on every provider. |
| `Sqlite` | `Database` is a persisted local file path; host, port, credentials, flavor, and version forbidden | no port; forced fixed table prefix `ie_` | Single application instance only. In-memory, URI, and network paths are rejected. |
| `SqlServer` | Host, database, and role credentials; `ServerFlavor`/`ServerVersion` forbidden | `1433`; configurable schema, default `islamu_event`; clean unprefixed table names | Full primary support; `Quartz` and `HostedService` email dispatch are both available. |
| `MariaDb` | Host, database, role credentials, `ServerFlavor=MariaDb`, and explicit `ServerVersion` | `3306`; forced fixed table prefix `ie_` | Full primary support; explicit version selects the EF SQL dialect. Prefer a separate database per deployment instance. |
| `MySql` | Host, database, role credentials, `ServerFlavor=MySql`, and explicit `ServerVersion` | `3306`; forced fixed table prefix `ie_` | Full primary support; explicit version selects the EF SQL dialect. Prefer a separate database per deployment instance. |

#### Relational namespace and instance isolation

`Database:Schema` (Compose `DATABASE_SCHEMA`) is always populated and validated,
defaulting to `islamu_event`, but its table-placement effect is provider-specific:

| Provider family | Isolation boundary | Resulting table example | Multi-instance guidance |
|---|---|---|---|
| PostgreSQL / SQL Server | Configured database schema | `islamu_event.users` | Give each application instance a distinct schema when sharing one database, and grant both runtime and migrator roles access to that schema. |
| SQLite | Database file plus forced fixed prefix | `ie_users` | Give each instance its own durable local file and one writer. Never share one SQLite file between instances. |
| MariaDB / MySQL | Selected database plus forced fixed prefix | `ie_users` | Prefer one database per instance on the same server, for example `islamu_event_prod` and `islamu_event_staging`. |

PostgreSQL and SQL Server call `HasDefaultSchema` before the shared model is
materialized, so application and Data Protection table names remain clean and
the schema supplies the namespace. SQLite, MariaDB, and MySQL do not use
`Database:Schema` for table placement; the model applies `ie_` to every owned
table and to its EF migration history tables. The field may remain at its
default for those providers, but changing it does not disable or rename the
prefix.

This boundary covers the application-owned relational model. Quartz scheduler
tables are co-located in the same database under the `Scheduler:Quartz:TablePrefix`
prefix (default `QRTZ_`); they are raw ADO tables and are not part of any EF Core
model or migration chain. Deployments that share one database between ISLAMU
instances must give each instance a distinct `Scheduler:Quartz:SchedulerName`, or
use separate databases, so the instances do not contend for the same scheduler rows.

Changing `Database:Schema` points the application at another namespace; it is
not an automatic rename or data move. Back up the source, provision grants,
run `Event.MigrationService` against the target schema, and use an explicitly
planned export/import when existing data must move.

Supported TLS values are named values only: `Prefer`, `Required`, or
`Disabled`. Server providers default to `Required`. With
`TrustServerCertificate=false`, required TLS performs certificate and host
verification. Setting `TrustServerCertificate=true` is accepted only with
`TlsMode=Required` and deliberately bypasses certificate validation; reserve it
for controlled development environments. SQLite requires its transport-neutral
default (`TlsMode=Prefer`, trust disabled).

| Provider family | `Disabled` | `Prefer` | `Required`, trust false | `Required`, trust true |
|---|---|---|---|---|
| PostgreSQL | Npgsql `Disable` | Npgsql `Prefer` | Npgsql `VerifyFull` | Npgsql `Require` |
| SQL Server | `Encrypt=false` | `Encrypt=true`, certificate validated | `Encrypt=true`, certificate validated | `Encrypt=true`, certificate trusted without validation |
| MariaDB / MySQL | MySqlConnector `None` | MySqlConnector `VerifyFull` | MySqlConnector `VerifyFull` | MySqlConnector `Required` |

The checked-in local Compose defaults use PostgreSQL `Prefer`; production
operators should select `Required` with a trusted CA/hostname. The CI matrix's
disabled or trusted-ephemeral-certificate settings are test-lane exceptions,
not production guidance.

Environment variables use normal .NET double-underscore mapping. The native
application keys are `Database__Provider`, `Database__Host`,
`Database__Port`, `Database__Database`, `Database__Schema`,
`Database__TlsMode`, `Database__TrustServerCertificate`,
`Database__ServerFlavor`, `Database__ServerVersion`,
`Database__Runtime__Username`, `Database__Runtime__Password`,
`Database__Migrator__Username`, and `Database__Migrator__Password`.

`Database__Schema` is validated for every provider so the structured contract
stays uniform. Only PostgreSQL and SQL Server use it as the model namespace;
SQLite, MariaDB, and MySQL retain `ie_` regardless of its value.

The standalone image is run directly with `docker run --env-file .env` and
normal .NET `Database__*` keys. The legacy single-underscore `DATABASE_*`
names are Compose interpolation inputs for the Split descriptor only; they are
not application compatibility aliases. Kubernetes packaging is deferred and
has no supported manifest in this repository.

### Standalone provider overrides

The standalone default is `Sqlite` at `/app/data/islamu_event.db`. It uses WAL
and a 30-second SQLite timeout, needs one durable local named volume, and permits
exactly one web replica (`Hosting__ReplicaCount=1`). Mount that volume at
`/app/data`; the embedded authority defaults to
`/app/data/privacy_erasure_authority.db` beside the primary database.

For a server provider, put the shared endpoint fields plus separate migrator
and runtime credentials in a protected `.env` file or secret store. The one
standalone process uses the migrator role before binding HTTP and the runtime
role afterward.

| Provider | Shared native fields | Migration phase | Runtime phase |
|---|---|---|---|
| PostgreSql | `Database__Provider=PostgreSql`, `Database__Host=<db-host>`, `Database__Port=5432`, `Database__Database=<database>`, `Database__Schema=islamu_event`, `Database__TlsMode=Required`, `Database__TrustServerCertificate=false` | `Database__Migrator__Username=${DB_MIGRATOR_USERNAME}`, `Database__Migrator__Password=${DB_MIGRATOR_PASSWORD}` | `Database__Runtime__Username=${DB_RUNTIME_USERNAME}`, `Database__Runtime__Password=${DB_RUNTIME_PASSWORD}` |
| SqlServer | `Database__Provider=SqlServer`, `Database__Host=<db-host>`, `Database__Port=1433`, `Database__Database=<database>`, `Database__Schema=islamu_event`, `Database__TlsMode=Required`, `Database__TrustServerCertificate=false` | `Database__Migrator__Username=${DB_MIGRATOR_USERNAME}`, `Database__Migrator__Password=${DB_MIGRATOR_PASSWORD}` | `Database__Runtime__Username=${DB_RUNTIME_USERNAME}`, `Database__Runtime__Password=${DB_RUNTIME_PASSWORD}` |
| MariaDb | `Database__Provider=MariaDb`, `Database__Host=<db-host>`, `Database__Port=3306`, `Database__Database=<database>`, `Database__TlsMode=Required`, `Database__TrustServerCertificate=false` (defaults to LTS `11.4`; override with `Database__ServerVersion=<version>` e.g. `10.11`) | `Database__Migrator__Username=${DB_MIGRATOR_USERNAME}`, `Database__Migrator__Password=${DB_MIGRATOR_PASSWORD}` | `Database__Runtime__Username=${DB_RUNTIME_USERNAME}`, `Database__Runtime__Password=${DB_RUNTIME_PASSWORD}` |
| MySql | `Database__Provider=MySql`, `Database__Host=<db-host>`, `Database__Port=3306`, `Database__Database=<database>`, `Database__TlsMode=Required`, `Database__TrustServerCertificate=false` (defaults to LTS `8.4`; override with `Database__ServerVersion=<version>` e.g. `8.0`) | `Database__Migrator__Username=${DB_MIGRATOR_USERNAME}`, `Database__Migrator__Password=${DB_MIGRATOR_PASSWORD}` | `Database__Runtime__Username=${DB_RUNTIME_USERNAME}`, `Database__Runtime__Password=${DB_RUNTIME_PASSWORD}` |

For example, a PostgreSQL standalone `.env` uses native keys directly:

```dotenv
Database__Provider=PostgreSql
Database__Host=postgres.example.internal
Database__Port=5432
Database__Database=islamu_event
Database__Schema=islamu_event
Database__TlsMode=Required
Database__TrustServerCertificate=false
Database__Migrator__Username=islamu_event_migrator
Database__Migrator__Password=<migrator-password>
Database__Runtime__Username=islamu_event_runtime
Database__Runtime__Password=<runtime-password>
```

Pass it directly to the image; migration completes before the listener starts:

```bash
docker run --rm --name islamu-event-standalone --env-file .env \
  --mount source=event_standalone_data,target=/app/data \
  -p 8080:8080 islamu/event-standalone
```

Provider-specific migrations are intentionally separate. PostgreSQL
application and Data Protection migrations live in `Explore.Persistence`;
SQLite, SQL Server, and MySQL use `Explore.Persistence.Migrations.{Provider}`
and `Explore.Persistence.DataProtection.Migrations.{Provider}`. MariaDB remains
a distinct runtime provider with its own server-flavor and version dialect, but
it owns no migration assembly of its own: it resolves the MySQL application and
Data Protection migration assemblies, so no MariaDB-specific migration project
or migration command exists. The histories are
`__EFMigrationsHistory` and `__EFDataProtectionMigrationsHistory` in the
configured schema (default `islamu_event`), or the corresponding fixed
`ie_`-prefixed tables where schemas are not supported. The prefix is deliberately
not configurable (`DATABASE_PREFIX`, `DATABASE_RUNTIME_PREFIX`,
`DATABASE_MIGRATOR_PREFIX`, `Database:Prefix`, `Database:Runtime:Prefix`, and
`Database:Migrator:Prefix` are rejected), so an operator-supplied value cannot
exceed provider identifier limits when combined with long table names.
Generated migrations and snapshots are never hand-edited. The selected authority
migration path remains separate from those provider-native application and Data
Protection assemblies and their distinct history tables; MigrationService never
combines authority paths.

The current pre-v1 application histories start from one generated initial per
provider:

| Provider | Generated application initial |
|---|---|
| PostgreSQL | `20260828035010_InitialApplication` |
| SQLite | `20260828040252_InitialApplication` |
| SQL Server | `20260828040310_InitialApplication` |
| MariaDB | `20260828040320_InitialApplication` |
| MySQL | `20260828040329_InitialApplication` |

These initials replace the former development application chains; they are not
incremental upgrades from those removed histories. Recreate the disposable
development application database, then let MigrationService apply the selected
initial. Preserve Data Protection and privacy-erasure authority storage unless
their own documented recovery procedure explicitly requires restoration.

SQLite adds three non-negotiable deployment rules: use durable local storage,
mount the file into both migration and API processes at the same path, and run
one application instance. Startup sets a 30-second busy timeout through the
native connection builder; MigrationService enables WAL after applying the
application migration. The primary file must not be named
`privacy_erasure_authority.db`.

| Key | Default | Description |
|---|---:|---|
| `Persistence:EnableRlsTenantSession` | `false` | PostgreSQL-only tenant-session interceptor (`PostgresTenantSessionInterceptor`). Binds `app.current_tenant_id` on every connection open so that database-level Row-Level Security (RLS) policies enforced by `PostgresTenantRowLevelSecurityModel` isolate tenant data at the storage layer. |

### PostgreSQL Row-Level Security (RLS) Defense-in-Depth

When using PostgreSQL as the primary database provider, the platform enforces tenant isolation both at the EF Core layer (via `QueryFilterNames.Tenant` global query filters) and at the storage layer via native PostgreSQL Row-Level Security:

1. **Dynamic Policy Application:** `PostgresTenantRowLevelSecurityModel.ApplyAsync` enumerates all tenant-scoped tables dynamically from EF Core `IModel` metadata during database migration and API bootstrap, issuing `ALTER TABLE ... ENABLE ROW LEVEL SECURITY`, `ALTER TABLE ... FORCE ROW LEVEL SECURITY`, and idempotent `CREATE POLICY tenant_isolation` statements.
2. **Vertically Partitioned & Child Entities:** Tables without a direct `tenant_id` column (such as `EventSessionIslamicAspect`, `ExternalApiKeyQuota`, and `LocationPii`) are protected through model-derived relational `EXISTS (SELECT 1 FROM parent ...)` policies linked to their tenant-bearing principal table.
3. **Fail-Closed Session Semantics:** Policies evaluate `tenant_id = nullif(current_setting('app.current_tenant_id', true), '')::uuid`. When `app.current_tenant_id` is unconfigured or empty, the expression yields `NULL`, returning 0 rows and rejecting cross-tenant inserts with SQLSTATE `42501` (`insufficient_privilege`). This prevents data leaks even under raw SQL execution or accidental `IgnoreQueryFilters()` bypasses.
4. **Role Privileges:** Application runtime database connections operate under roles with `NOBYPASSRLS` and without `SUPERUSER`, guaranteeing that table owners and runtime connections are strictly constrained by RLS.

### Privacy-erasure authority topology

`PrivacyErasure:Authority:Topology` accepts `EmbeddedSqlite` (the default),
`CoLocated`, or `ExternalDatabase`. The removed `None` value fails validation;
an in-memory authority cannot remain consistent with a persisted replay
checkpoint across process restart.

| Topology | Supported Primary Providers | Authority Storage Placement |
|---|---|---|
| `EmbeddedSqlite` *(default)* | **All 5 providers** (PostgreSQL, SQLite, SQL Server, MariaDB, MySQL) | Dedicated local `/app/data/privacy_erasure_authority.db` file. |
| `CoLocated` | **PostgreSQL or SQLite only** | Primary application database (shares application connection). |
| `ExternalDatabase` | **All 5 providers** | Separate physical PostgreSQL database instance. |

`EmbeddedSqlite` stores authority facts in the dedicated
`/app/data/privacy_erasure_authority.db` file; it never shares the primary
database or primary SQLite file. The file uses a private cache, WAL, a bounded
busy timeout, one writer, restrictive permissions, and a separately mounted
durable volume. It works seamlessly with any primary application database provider.

`CoLocated` stores authority facts in the primary application database and uses
the existing application database credentials. It currently supports primary
`PostgreSql` and `Sqlite` (due to provider-native locking and WAL single-writer semantics). Configuring `CoLocated` with `SqlServer`, `MariaDb`, or `MySql` fails closed during DI composition before adapter/database I/O, with safe diagnostics that never expose credentials or connection strings.

`ExternalDatabase` uses a dedicated authority migration against a different
physical PostgreSQL database with function-only (`SECURITY DEFINER`) runtime access; startup rejects an authority target that resolves to
the application database even when endpoint identity is obscured by different
credentials. The primary database keeps only the replay checkpoint in
`EmbeddedSqlite` and `ExternalDatabase`. `CoLocated` additionally keeps the
retained authority rows there because the primary database is its sole sink.

| Topology | Operator backup units | `restoreReplayProtection` |
|---|---|---:|
| `EmbeddedSqlite` | Primary database backup plus the dedicated authority-file backup | `true` when the authority file is excluded from the primary restore |
| `CoLocated` | One primary database backup containing authority rows | `false` |
| `ExternalDatabase` | Primary database backup plus an independently managed external PostgreSQL authority backup | `true` when the external authority has an independent restore lifecycle |

| Key | Default | Description |
|---|---:|---|
| `PrivacyErasure:CurrentPolicyVersion` | `1` | Compiled local disposition policy version used for replay coverage. Increment only with corresponding disposition code and replay tests. |
| `PrivacyErasure:ReceiptLifetime` | `7.00:00:00` | Short-lived status-receipt lifetime; must be greater than zero and no more than 30 days. |
| `PrivacyErasure:MaximumBackupHorizon` | `365.00:00:00` | Longest supported application-backup horizon used to derive retained-authority lifecycle requirements. |
| `PrivacyErasure:AuthorityRetentionSafetyMargin` | `30.00:00:00` | Additional authority-retention margin beyond the maximum backup horizon. |
| `PrivacyErasure:RetentionCleanupEnabled` | `true` | Registers the existing receipt-hash/provider-locator cleanup job. It does not authorize authority compaction; authority maintenance additionally requires an explicit legal-hold sequence set. |
| `PrivacyErasure:RetentionCleanupDryRun` | `true` | Keeps receipt-hash/provider-locator cleanup non-mutating. Authority-ledger dry run uses `EvaluateRetentionAsync` and is separate from apply. |
| `PrivacyErasure:RetentionCleanupBatchSize` | `100` | Bounds one credential-cleanup pass; valid range `1..1000`, matching the authority-maintenance request ceiling. |
| `PrivacyErasureAuthorityEmbedded:Path` | `/app/data/privacy_erasure_authority.db` | Absolute local authority file used only by `EmbeddedSqlite`; URI and network paths are rejected. |
| `PrivacyErasureAuthorityEmbedded:WriterReplicaCount` | `1` | Must be exactly `1`; startup rejects multi-writer embedded deployments. |
| `PrivacyErasureAuthorityEmbedded:BusyTimeoutSeconds` | `30` | SQLite busy timeout; valid range `1..300`. |
| `Database:Erasure:Provider` / `PrivacyErasureAuthorityDatabase:Provider` | none | Must be `PostgreSql` for `ExternalDatabase`. Other providers fail validation. |
| `Database:Erasure:Host`, `Port`, `Database`, `TlsMode`, `TrustServerCertificate` | none | Structured external authority endpoint and TLS policy under `/database/erasure`. |
| `Database:Erasure:Runtime:Username`, `Password` | none | API-only function-execution credential for `ExternalDatabase`; its username must differ from the migrator username. |
| `Database:Erasure:Migrator:Username`, `Password` | none | MigrationService-only schema/grant credential for `ExternalDatabase`; role collapse with runtime fails startup/provisioning. |

Compose and Infisical map `ERASURE_TOPOLOGY` (or `PRIVACY_ERASURE_AUTHORITY_TOPOLOGY`),
`ERASURE_EMBEDDED_PATH`, `ERASURE_WRITER_REPLICA_COUNT`, and
`ERASURE_BUSY_TIMEOUT_SECONDS` to the embedded .NET keys.
For the external topology they map `DATABASE_ERASURE_HOST` (or Infisical `/database/erasure/DATABASE_HOST`),
`PORT`, `NAME` / `DATABASE`, `TLS_MODE`, `TRUST_SERVER_CERTIFICATE`, and the `RUNTIME_*` /
`MIGRATOR_*` credential families to `Database:Erasure:*` and `PrivacyErasureAuthorityDatabase:*`.
External fields are ignored by `EmbeddedSqlite` and `CoLocated`. Back up the embedded file/volume independently;
include co-located authority rows in the primary database backup.
Aspire creates a distinct local authority PostgreSQL resource whenever
`ExternalDatabase` is selected in a profile that uses local data. Profiles
without local data use operator-provided external infrastructure.

`PrivacyErasure:Durability:Mode`, and raw authority connection
strings are removed and are never translated. This repository is pre-v1 and in
development mode: reset affected development data after taking any required
export, then select `EmbeddedSqlite`, `CoLocated`, or `ExternalDatabase`.
There is no fallback, translation, dual write, or compatibility shim for removed
or unsupported authority contracts. Application code and implementation agents
never delete databases, containers, volumes, or backups.

### Localization Configuration

Localization runtime settings are governance-backed, not static appsettings. The
important keys are documented in [LOCALIZATION.md](LOCALIZATION.md):

- `localization.default_language`
- `localization.enabled_languages`
- `localization.fallback_language`
- `localization.client_picker_enabled`
- `localization.force_offline_mode`
- `localization.tms_provider`
- `localization.tms_api_url`
- `localization.tms_project_id`
- `localization.tms_component`

The TMS credential is the shared secret-binding key `localization.tms_api_key`.
Set or rotate it through the authorized localization admin API so the value is
stored as a server-side secret binding. Do not put Tolgee/Weblate API keys in
governance settings, Blazor payloads, generated clients, logs, metrics, or
OpenAPI examples.

### Listmonk Integration Configuration

Listmonk runtime settings are governance-backed. Static configuration and local
environment variables are deployment bootstrap values only; once an operator
saves settings through the admin API, saved application settings are the runtime
authority.

Bootstrap keys accepted by local env/Compose/Infisical compatibility mapping:

- `LISTMONK_ENABLED`
- `LISTMONK_INSTANCE_URL`
- `LISTMONK_DEFAULT_LIST_ID`
- `LISTMONK_PRECONFIRM_SUBSCRIPTIONS`
- `LISTMONK_SYNC_ON_REGISTRATION`
- `LISTMONK_API_USERNAME`
- `LISTMONK_API_KEY`

`LISTMONK_API_USERNAME` and `LISTMONK_API_KEY` are server-side secret bindings.
Browser/admin DTOs expose only `ApiUsernameConfigured` and `ApiKeyConfigured`
flags, never raw credential values. Rotate credentials through the authorized
Listmonk integration API.

Quartz `integration-sync-drain` uses the existing `IntegrationSyncProcessor` section; the name is retained to avoid a configuration rename, not because a hosted processor still exists. `Enabled` registers or removes the owned job, `PollingIntervalSeconds` controls its interval, `BatchSize` bounds one pass, `ProcessingLeaseTimeoutSeconds` controls stale-claim recovery, and the retry-delay/max-attempt keys remain Infrastructure state-machine controls. An unkeyed Listmonk request with an uncertain outcome is parked and requires evidence-based operator recovery; changing retry settings never makes it eligible for blind replay.

`IntegrationSyncProcessor:HealthDueWarningThreshold` defaults to `1000`; `HealthStaleWarningThreshold` and `HealthAmbiguousWarningThreshold` default to `1`. These positive bounded thresholds drive the aggregate `queue-drains` readiness check and never expose tenant, subscriber, or provider identity.

Static no-TMS/fallback bundles are written to
`{ContentRoot}/App_Data/Localization/Bundles/{code}.json`. That path is local
filesystem storage unless the deployment mounts it to a shared persistent
volume.

### Public URL Configuration

`PublicBaseUrl` is the preferred static key for the instance's externally reachable HTTPS base URL. The fallback lookup order is `PublicBaseUrl`, then `App:PublicBaseUrl`, then `Application:PublicBaseUrl`.

The value must be an absolute `http` or `https` URL. Public deployments should use `https`. It is used by public URL builders and by the email dispatch drain when creating absolute unsubscribe URLs for `List-Unsubscribe` headers and visible unsubscribe footers. If no valid public base URL is configured, categorized email can still send when preferences allow it, but the dispatch path omits unsubscribe URLs because relative links are not valid in email headers.

Payment Checkout requires HTTPS with no user info, query, or fragment. A normalized application subpath is supported, for example `https://events.example.org/events`; runtime normalization adds one trailing slash and preserves `/events` in Stripe callbacks and BFF navigation. Missing or invalid configuration defers new Checkout dispatch with `checkout_return_origin_invalid`; it does not block free-order finalization or payment reconciliation.

### Keycloak Identity Lifecycle Email Configuration

Identity lifecycle email for Keycloak-backed accounts is account-authority owned. The logical ownership settings are:

| Logical setting | Expected value | Meaning |
|---|---|---|
| `auth.provider` | `Keycloak` | The account authority for Keycloak-backed credentials is Keycloak. |
| `auth.identity_email_owner` | `AccountAuthority` | Verification, password reset, email update, MFA, and required-action messages are not ISLAMU product emails. |
| `auth.account_authority_kind` | `Keycloak` | ISLAMU may request a Keycloak action and record local delegation audit, but Keycloak owns action tokens, templates, SMTP handoff, and message delivery. |

Runtime configuration currently uses the .NET sections below:

| Key | Default | Description |
|---|---:|---|
| `AccountAuthorityLifecycleEmail:Enabled` | `false` | Enables ISLAMU-initiated identity lifecycle delegation requests. Disabled paths return safe local outcomes and make no provider call. |
| `AccountAuthorityLifecycleEmail:ProviderConfigured` | `false` | Declares that the selected account authority is configured. `false` returns a safe provider-not-configured outcome. |
| `AccountAuthorityLifecycleEmail:AccountAuthorityKind` | `Keycloak` | Account-authority kind used for local delegation decisions. |
| `KeycloakLifecycleEmail:Enabled` | `false` | Enables the Infrastructure Keycloak Admin REST adapter for required-action email requests. |
| `KeycloakLifecycleEmail:BaseUrl` | unset | Keycloak base URL. Public deployments should use HTTPS. Loopback/private URLs are rejected unless explicitly allowed for local development. |
| `KeycloakLifecycleEmail:Realm` | unset | Realm whose users receive required-action emails. |
| `KeycloakLifecycleEmail:AdminUsername` / `AdminPassword` | unset | Server-side admin credential used only by Infrastructure to obtain an admin token. Never expose in browser payloads, health data, logs, traces, or support bundles. |
| `KeycloakLifecycleEmail:AdminClientId` | `admin-cli` | Client id used for the admin password grant. |
| `KeycloakLifecycleEmail:DefaultClientId` | unset | Optional client id passed to Keycloak `execute-actions-email` when a request does not provide one. |
| `KeycloakLifecycleEmail:DefaultLifespanSeconds` | unset | Optional default required-action link lifetime passed to Keycloak. |
| `KeycloakLifecycleEmail:AllowLocalUrls` | `false` | Local-development escape hatch for loopback/private Keycloak URLs. Keep `false` in shared and public deployments. |

`keycloak.smtp_mode` is an operational policy label, not an ISLAMU SMTP provider switch. Use `managed` when Keycloak/provider SMTP is managed outside this deployment. Use a self-hosted/shared-SMTP mode only to configure the Keycloak realm SMTP server with deployment-owned credentials. In both modes, Keycloak remains the sender and owner of identity lifecycle emails; shared SMTP credentials do not transfer email decision ownership to ISLAMU Event.

`keycloak.theme_sync_enabled` is a future automation policy for applying platform-managed Keycloak theme assets. Keycloak email themes live under the Keycloak theme `email` type and customize templates such as password-reset or execute-actions messages. Theme sync changes Keycloak-owned templates only; it does not route identity lifecycle mail through `EmailDispatchOutbox`, `IEmailService`, RabbitMQ, or the Quartz scheduler.

Local development may point a self-hosted Keycloak realm SMTP configuration at Mailpit for inspection. That is still Keycloak realm SMTP plumbing. Product Basic Dispatch Mailpit settings under `email.*` remain separate and should not be treated as production defaults for Keycloak.

### ATProto/PDS Identity Lifecycle Email Configuration

ATProto/PDS account lifecycle email follows the same account-authority rule, but the authority is the PDS that hosts the account. ISLAMU Event is an ATProto OAuth relying party and event-federation client; it is not a PDS server.

| Logical setting | Expected value | Meaning |
|---|---|---|
| `auth.provider` | `ATProto` for an enabled, ready linked-account login | The BFF implements confidential ATProto OAuth and issues its normal protected application cookie only after the API verifies the linked DID/PDS session. |
| `auth.identity_email_owner` | `AccountAuthority` | PDS email confirmation, password reset, email change, migration, and security messages are not ISLAMU product emails. |
| `auth.account_authority_kind` | `AtprotoPds` or `IslamuOperatedPds` | External PDS hosts own their hosted-account lifecycle email. A future ISLAMU-operated PDS cell also owns its own PDS credential lifecycle email because the PDS owns and verifies the credential token. |

Do not add a global `emails.provider = PDS` switch. PDS SMTP is account-authority transport, not a product email provider for `EmailDispatchOutbox` or `IEmailService`. If a future ISLAMU-operated PDS cell uses shared SMTP or local Mailpit, that only configures the PDS cell's account-email delivery path; it does not transfer product notification ownership away from ISLAMU Event.

AT Protocol account email access is private account-hosting data. Product email flows must not assume a verified email claim is available from ATProto login. When `email` or `email_verified` is unavailable or unverified, use a separately verified app-level notification email or in-app notifications instead of reusing PDS credential email semantics.

### AT Protocol OAuth Configuration

AT Protocol login is enabled by the instance governance setting `auth.atproto_login_enabled`. Disabled login is omitted from `/auth/providers`; enabled login remains unavailable until every local prerequisite below is valid. The readiness check is passive and never signs in to a user PDS.

| Key | Default | Purpose |
|---|---|---|
| `Atproto:PublicUrl` | empty | Exact browser-facing HTTPS origin used for the URL `client_id` and callback. Paths, credentials, queries, fragments, Unicode host spelling, and trailing-dot aliases are rejected. |
| `Atproto:CallbackPath` | `/signin-atproto` | Root-relative OAuth callback path published in client metadata. |
| `Atproto:AllowDevelopmentLoopback` | `false` | Allows exact loopback targets only in Development transport policy. Browser login still requires HTTPS, including localhost, for its host-only proof cookie. |
| `Atproto:StateLifetimeSeconds` | `300` | SDK state lifetime, clamped to 30–600 seconds; storage additionally caps expiry at the browser-proof deadline minus the two-minute handoff budget. |
| `Atproto:TenantOrigins` | empty | Explicit HTTPS login origins with tenant ID and slug; recovered state must still match the current configured mapping. |
| `Atproto:ClientName` | empty | Optional display name in client metadata. |
| `Atproto:ClientUri` | empty | Optional canonical HTTPS client homepage in client metadata. Invalid values are omitted. |
| `Atproto:LogoUri` | empty | Optional canonical HTTPS logo URL in client metadata. Invalid values are omitted. |
| `Atproto:PolicyUri` | empty | Optional canonical HTTPS privacy-policy URL in client metadata. Invalid values are omitted. |
| `Atproto:TermsOfServiceUri` | empty | Optional canonical HTTPS terms URL in client metadata. Invalid values are omitted. |
| `Atproto:Jwt:SessionLifetime` | `00:15:00` | First-party API session JWT lifetime. API startup validation requires one through sixty minutes. |
| `RateLimiting:AtprotoAuthentication:PermitLimit` | `10` | Per-IP fixed-window limit shared by the ATProto challenge and callback endpoints; runtime clamps it to 1–1000. |
| `RateLimiting:AtprotoAuthentication:WindowSeconds` | `60` | ATProto authentication rate-limit window; runtime clamps it to 1–3600 seconds. |

The governance value `auth.atproto_public_url` records the administrator-facing provider URL, but it does not replace the static runtime authority `Atproto:PublicUrl`; keep them consistent. `ATPROTO_OAUTH_CLIENT_PRIVATE_JWKS` maps to `Atproto:OAuthClientPrivateJwks` in the BFF and must come from server-side secret configuration. Durable session encryption and first-party API session signing use `ATPROTO_SESSION_ENCRYPTION_KEYRING` and `ATPROTO_SESSION_JWT_PRIVATE_JWKS`, documented in [SECRETS.md](SECRETS.md#atproto-oauth-session-envelopes).

Transient OAuth state and handoffs always use the primary database through the private BFF/API bridge. `Atproto:UseSingleNodeMemoryStore` and `Atproto:HandoffLifetimeSeconds` are removed, without aliases. Handoffs have a fixed two-minute maximum, capped by the fifteen-minute browser-proof deadline. The proof cookie is reused without sliding or per-flow deletion; near expiry, a new challenge returns `429` with a bounded `Retry-After` rather than rotating it under active flows.

BFF Data Protection is a separate prerequisite. With no cache connection configured, preserve its native key directory; replicas must share that persistent directory and the existing `islamu-event` application discriminator. An explicitly configured cache connection still selects Redis key persistence, so an entirely Redis-free deployment must remove that selection too. Do not silently relocate keys. Restart in-flight logins after this cutover or any loss of their proof, protection keys, or required signing key.

The `atproto-authentication` readiness check is deliberately local and passive. When enabled, it validates the public URL/callback, the BFF OAuth signing ring, and that the OAuth state/session adapter services are registered; it does not test Redis connectivity, parse the Infrastructure encryption ring or API session-JWT ring, contact a PDS, or perform OAuth discovery. Those dependencies fail through their own health checks or at the bounded operation that uses them. Readiness exposes only `enabled` and a bounded failure code; logs, metrics, health JSON, and support output must not contain handles, DIDs, endpoint queries, tokens, JWK values, or provider response bodies.

### AT Protocol Events Governance And Workers

ATProto login and ATProto Events are independent. The effective administrator capability must be enabled for both tenant-visible Jetstream discovery and new eligible event/RSVP outbox enqueue. Outbound work additionally requires the event owner's self-scoped consent, linked DID, restorable OAuth session, successful local publication, and a complete privacy-safe community record.

| Governance key | Default | Scope | Purpose |
|---|---|---|---|
| `federation.atproto_events_enabled` | `false` | Instance through Tenant; lockable | One switch for both inbound event fetching/presentation and eligible outbound event/RSVP publication. Instance administrators can lock tenant overrides. |
| `federation.atproto_event_validation_profile` | `platform` | Instance through Tenant; lockable | `platform` retains normal event publish readiness. `community_lexicon` requires only community-required business fields while retaining authorization, ownership, tenant, privacy, reference, storage, concurrency, and supplied-value validation. |
| `federation.atproto_publish_my_events` | `false` | User only | Personal publication consent. Administrators cannot grant it on a user's behalf and it is not lockable. |

| Static key | Default | Bounds / purpose |
|---|---:|---|
| `Atproto:PdsSync:Enabled` | `true` | Registers Quartz `pds-sync-drain`; it never changes governance or user consent. |
| `Atproto:PdsSync:PollingIntervalSeconds` | `5` | 1–300 seconds. |
| `Atproto:PdsSync:BatchSize` | `20` | 1–100 fenced claims per pass. |
| `Atproto:PdsSync:MaxConcurrency` | `10` | 1 through the configured batch size. |
| `Atproto:PdsSync:LeaseDurationSeconds` | `90` | 30–900 seconds. Expired processing claims are reclaimable. |
| `Atproto:PdsSync:HealthDueWarningThreshold` | `1000` | Aggregate due-row readiness threshold. |
| `Atproto:PdsSync:HealthStaleWarningThreshold` | `1` | Aggregate expired-lease readiness threshold. |
| `Atproto:PdsSync:HealthDeadLetterWarningThreshold` | `1` | Aggregate terminal-row readiness threshold. |
| `Atproto:Jetstream:Endpoint` | `https://jetstream1.us-east.bsky.network` | Fixed HTTPS origin without credentials, path, query, or fragment. |
| `Atproto:Jetstream:MaxMessageSizeBytes` | `2113536` | Bounded near the verified community-record maximum; startup validation accepts 2,097,152–2,162,688 bytes. |
| `Atproto:Jetstream:LeaseDurationSeconds` | `60` | One shared canonical consumer lease; startup validation accepts 15–300 seconds. |
| `Atproto:Jetstream:LeaseRenewalSeconds` | `20` | Renews the shared lease every 5–120 seconds and must remain shorter than `LeaseDurationSeconds`. |
| `Atproto:Jetstream:CapabilityPollMilliseconds` | `5000` | Polls for an effective enabled scope without opening per-tenant sockets. |
| `Atproto:Jetstream:RetryMinimumMilliseconds` | `1000` | Reconnect backoff floor; startup validation accepts 10–60,000 milliseconds. |
| `Atproto:Jetstream:RetryMaximumMilliseconds` | `30000` | Reconnect backoff ceiling; it must be at least `RetryMinimumMilliseconds` and no more than 300,000 milliseconds. |
| `Atproto:Jetstream:AllowedDids` | empty | Optional unique DID filter, maximum 10,000. Empty discovers all public publishers of the exact event/RSVP collections; a non-empty list restricts ingestion to curated DIDs. |

The PDS worker calls CarpaNet only for committed outbox rows. Event creation/update request transactions never call a PDS. Projection coverage, privacy, unsafe values, or encoded-size overflow prevent PDS enqueue; the implementation never truncates or silently omits public snapshot values to force a record through.

### Dedicated Admin Host Configuration

`Bff:AdminHosts` is a deployment-managed list of exact hosts or origins that should render the existing Blazor app's control-plane shell instead of the public/tenant shell. Examples: `admin.example.org`, `https://admin.example.org`. Wildcards are rejected because they can overlap tenant subdomains or tenant custom domains.

`Bff:AdminHostAllowedIpRanges` is optional. When set, values must be exact IP addresses or CIDR ranges, for example `203.0.113.10` or `203.0.113.0/24`. The allowlist applies only to configured admin hosts. If a request arrives on an admin host with a missing remote IP or an address outside the allowed ranges, the BFF returns `403`.

Admin-host classification runs after trusted forwarded-header processing. Reverse proxies must forward `X-Forwarded-Host`, `X-Forwarded-Proto`, and `X-Forwarded-For` so the BFF sees the browser-facing host, scheme, and client IP.

### Storage Static Configuration

Local-first storage is deployment-managed. The filesystem root is bound from static configuration and is not a tenant/admin database setting.

| Key | Default | Description |
|---|---|---|
| `Storage:Local:RootPath` | provider default unless Compose/Aspire overrides | API-owned local storage root. Compose sets `/app/storage-data/local` and mounts it to `local_storage_data`; Aspire sets `storage-data/aspire-local` under the repository root. |
| `Storage:Local:CreateRootIfMissing` | `true` in Compose/Aspire overrides | Allows startup/health/provider code to create the local root when the deployment grants write permission. |

Optional S3-compatible storage composes non-secret `s3.*` governance with
`storage.s3.access_key_id` and `storage.s3.secret_access_key` resolved exclusively
through the selected Environment, Infisical, or local User Secrets authority. There is no database or
`IConfiguration` credential fallback.

`StorageReconciliation:*` controls the API-hosted drift worker and is validated at startup:

| Key | Default | Description |
|---|---:|---|
| `StorageReconciliation:Enabled` | `true` | Enables the hosted worker and reconciliation health check. Disabled reports intentional degraded health. |
| `StorageReconciliation:DryRun` | `true` | Report-only mode. Mutating flags are ignored while dry-run is true. |
| `StorageReconciliation:InitialDelaySeconds` | `45` | Initial worker delay after API startup. |
| `StorageReconciliation:PollingIntervalMinutes` | `360` | Worker interval between passes. |
| `StorageReconciliation:BatchSize` | `500` | Maximum metadata rows or inventory objects per pass. |
| `StorageReconciliation:MissingObjectQuarantineGraceHours` | `24` | Age threshold before missing backing objects can quarantine metadata. |
| `StorageReconciliation:OrphanFileQuarantineGraceHours` | `24` | Age threshold before orphan local files can be moved to quarantine. |
| `StorageReconciliation:DeleteGraceHours` | `720` | Age threshold before delete-eligible metadata can be physically deleted and soft-deleted. |
| `StorageReconciliation:QuarantineMissingObjects` | `false` | Enables metadata quarantine for missing backing objects when `DryRun=false`. |
| `StorageReconciliation:QuarantineOrphanLocalFiles` | `false` | Enables local orphan quarantine when `DryRun=false`. |
| `StorageReconciliation:DeleteQuarantinedObjects` | `false` | Enables idempotent provider delete plus metadata soft-delete when `DryRun=false`. |

### AI Provider Static Configuration

`AiProvider:*` is the deployment/admin-controlled readiness and egress-validation surface for Infrastructure AI adapters. The governance keys below remain the tenant/runtime source for assistant availability; this static section lets operators validate provider wiring without putting endpoint URLs or credentials in browser-controlled request data.

| Key | Default | Description |
|---|---:|---|
| `AiProvider:Enabled` | `false` | Enables provider readiness evaluation. Disabled reports healthy-disabled and performs no provider network call. |
| `AiProvider:Provider` | `none` | Supported values: `none`, `openai`, `openai-compatible`, `anthropic`, `anthropic-compatible`, and `azure-openai`. `fake` is additionally available only in `Development` and `Testing`; production-like environments reject it and do not register its provider or strategy. `openai` uses OpenAI's Responses API; `anthropic` uses Anthropic's Messages API; compatible providers target configured endpoints. |
| `AiProvider:EndpointUrl` | unset | Admin/deployment-controlled provider base URL. Optional for `openai` (`https://api.openai.com/v1`) and `anthropic` (`https://api.anthropic.com/v1`); required for `openai-compatible`, `anthropic-compatible`, and `azure-openai`. Azure OpenAI must use HTTPS. Must have no embedded credentials, query string, or fragment. Local/private endpoints require explicit opt-in. |
| `AiProvider:ApiKey` | unset | Sensitive provider credential. Required for `openai` and `anthropic`; optional for local/self-hosted compatible endpoints that do not require authentication. Never expose in browser payloads, health data, logs, metrics, traces, screenshots, or issue templates. |
| `AiProvider:ModelId` | unset | Default model identifier for the concrete adapter. Required for OpenAI Responses, Anthropic Messages, compatible, and Azure OpenAI providers when enabled. For Azure OpenAI this is the deployment name passed to the Azure SDK. Health/metrics use only boolean presence flags, not the raw model ID. |
| `AiProvider:AzureCredentialMode` | `api-key` | Azure OpenAI credential mode: `api-key` or `default-azure-credential`. Prefer `default-azure-credential` for Azure-hosted deployments with managed identity. |
| `AiProvider:AzureTenantId` | unset | Optional tenant ID used to constrain `DefaultAzureCredential` for Azure OpenAI. Leave unset for the SDK default chain. |
| `AiProvider:AllowLocalProviderEndpoints` | `false` | Allows loopback/link-local/private provider URLs for deliberate self-hosted/local-model deployments. Keep `false` for public SaaS/provider endpoints. |
| `AiProvider:MaxInputTokens` | `8000` | Provider request input budget seed. Handlers must still enforce prompt/reference bounds. |
| `AiProvider:MaxOutputTokens` | `1024` | Provider response budget seed. |
| `AiProvider:Temperature` | `0.2` | Provider sampling temperature. Must be between 0 and 2. |
| `AiProvider:TimeoutSeconds` | `30` | Provider call timeout budget. Must be between 1 and 300. |
| `AiProvider:RetentionDays` | `30` | Retention seed; enforcement is separate from provider health. |
| `AiProvider:DailyMessageLimit` | `50` | Abuse/cost-control seed; enforcement is separate from provider health. |

### Webhook Static Configuration

`Webhooks:*` controls outgoing product webhooks only. Incoming integration callbacks under `/api/integrations/*` remain available independently of the outgoing provider mode.

| Key | Default | Description |
|---|---:|---|
| `Webhooks:Enabled` | `true` | Master switch for outgoing product webhooks. |
| `Webhooks:Provider` | `Local` | Supported values are `Disabled`, `Local`, `Svix`, `Composite`, and `DryRun`. |
| `Webhooks:DefaultPayloadRetentionDays` | `14` | Default retention window for canonical webhook payload bodies. |
| `Webhooks:Local:MaxAttempts` | `8` | LocalProvider retry ceiling. |
| `Webhooks:Local:TimeoutSeconds` | `15` | LocalProvider total request timeout. |
| `Webhooks:Local:ConnectTimeoutSeconds` | `3` | LocalProvider connect timeout. |
| `Webhooks:Local:BlockPrivateNetworks` | `true` | Blocks loopback, private, link-local, metadata, localhost, and internal DNS destinations by default. |
| `Webhooks:Local:AllowedPrivateCidrs` | empty | Operator allow-list for deliberate private-network delivery. Keep empty for public/SaaS deployments. |
| `Webhooks:Svix:BaseUrl` | unset | Optional absolute HTTP(S) self-hosted Svix base URL. Aspire injects the current endpoint only when `Svix` or `Composite` is selected; Compose uses `http://svix:8071` with the opt-in `webhooks` profile. |
| `Webhooks:Svix:AuthTokenSecretRef` | `webhooks.svix.auth_token` | Server-side secret binding for the Svix API token. |
| `Webhooks:Svix:OperationalWebhookSecretRef` | `webhooks.svix.operational_webhook_secret` | Secret binding for incoming Svix operational callback verification. |
| `Webhooks:Svix:AppPortalEnabled` | `true` | Enables backend-only App Portal access URL generation. |
| `Webhooks:Svix:SyncEventTypesOnStartup` | `true` | Syncs the canonical event catalog to Svix when provider mode is `Svix` or `Composite`. |

The existing `WebhookDeliveryProcessor` section now configures Quartz `local-webhook-delivery-drain`: `Enabled`, `InitialDelaySeconds`, and `PollingIntervalSeconds` control registration and cadence, while batch, concurrency, fairness, lease, and health keys still configure the Infrastructure drain. `WebhookProviderPublicationProcessor` similarly configures `webhook-provider-publication-drain`; it remains disabled by default, and its provider-identity, unknown-outcome, retry, and reconciliation settings are unchanged.

`WebhookBulkReplay:*` controls Quartz `webhook-bulk-replay-drain`. It does not enable a
Svix cloud service or provider-native replay:

| Key | Default | Description |
|---|---:|---|
| `WebhookBulkReplay:Enabled` | `true` | Enables queued operation processing. Preview and management reads remain available when disabled. |
| `WebhookBulkReplay:InitialDelaySeconds` | `10` | Bounded startup delay, from 0 through 3600 seconds. |
| `WebhookBulkReplay:PollingIntervalSeconds` | `5` | Polling cadence, from 1 through 3600 seconds. |
| `WebhookBulkReplay:OperationsPerPass` | `10` | Maximum operations processed per pass, from 1 through 100. |
| `WebhookBulkReplay:MaximumItemsPerOperation` | `100` | Runtime ceiling per operation; must be from 1 through the hard 1000-item ceiling. |
| `WebhookBulkReplay:MaximumReservedItemsPerTenant` | `500` | Queued/executing requested-item capacity per tenant; must be at least the per-operation ceiling. |
| `WebhookBulkReplay:MaximumFilterWindowDays` | `30` | Maximum explicit preview/schedule window, from 1 through 365 days. |
| `WebhookBulkReplay:HealthQueuedWarningThreshold` | `100` | Aggregate queued-operation readiness threshold. |
| `WebhookBulkReplay:HealthExecutingWarningThreshold` | `100` | Aggregate executing-operation readiness threshold. |

`WebhookProviderPublicationProcessor:HealthDueWarningThreshold` defaults to `1000`; `HealthStaleWarningThreshold` and `HealthUnknownWarningThreshold` default to `1`. All three feed the aggregate `queue-drains` readiness check only when provider publication is enabled.

Local development can source Svix secrets from `WEBHOOKS_SVIX_AUTH_TOKEN` and `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET`; both remain intentionally empty while Local is selected. Development seeding creates missing instance secret bindings only when values are configured. See [WEBHOOKS.md](WEBHOOKS.md) for provider behavior and runbooks.

### Registration Provider Framework Configuration

Registration providers have no static `RegistrationProviders:*` adapter configuration. Provider connections are tenant-owned data managed through the authenticated registration-provider management API. Credentials are referenced by `SecretBinding` rows only:

| Secret definition key | Default Infisical path/key | Scope | Purpose |
|---|---|---|---|
| `registration_provider.api_token` | `/registration-providers/REGISTRATION_PROVIDER_API_TOKEN` | Tenant | Provider API/OAuth credential reference. Google Forms accepts a raw access token or JSON with `access_token`; refresh envelopes must contain `refresh_token`, `client_id`, and `client_secret`. |
| `registration_provider.webhook_secret` | `/registration-providers/REGISTRATION_PROVIDER_WEBHOOK_SECRET` | Tenant | Provider callback signing secret reference for providers that use shared callback secrets, such as Microsoft Forms. Google Forms Pub/Sub does not use this key because it validates Google-signed OIDC tokens. |

Use `SecretBinding.Qualifier` (bounded to 128 characters) to distinguish multiple tenant connections that use the same definition key. Approved origins are connection data, not appsettings: each origin must be an HTTPS origin only and cannot target local, private, link-local, multicast, or metadata hosts. Launch descriptors and embeds are generated from those origins by the server; operators must not add raw iframe HTML or provider URLs to configuration files.

Google Forms connection metadata is also stored on the tenant connection, not in appsettings. `GrantedOAuthScopes` must be exactly the read/import set (`openid email https://www.googleapis.com/auth/forms.body.readonly https://www.googleapis.com/auth/forms.responses.readonly`) or that set plus `https://www.googleapis.com/auth/forms.body` for managed provisioning. `ProviderIdentity` is required. `PublicBaseUrl` must pin `https://docs.google.com`; `ManagementApiBaseUrl` must stay on `https://forms.googleapis.com`. `WebhookSecretBindingId` must be unset because Google Pub/Sub uses OIDC, and supplied shared-secret bindings are rejected for the Google descriptor. `PubSubConfigurationReference` is required; JSON accepts `topicName`/`topic`, `audience`, and `serviceAccountEmail`/`email`, while semicolon text accepts `topic`, `audience`, and `serviceAccountEmail`; see [Google Forms Pub/Sub Integration](integrations/google-forms-pubsub.md#connection-fields).

### Promotion Code Lookup Configuration

Promotion-code lookup has one non-secret static option and one separately bound secret family:

| Key | Default | Description |
|---|---:|---|
| `Promotions:CodeLookup:ActiveKeyVersion` | `1` | Positive HMAC-key version used for newly published or rotated codes. Environment-variable form: `Promotions__CodeLookup__ActiveKeyVersion`. Startup validation rejects values below `1`. |

The HMAC material is not stored under this configuration section. It resolves through the instance-only secret definition `promotions.code_lookup_hmac_key` with qualifier `v{ActiveKeyVersion}`; for version `1`, the exact qualifier is `v1`. The default initial source coordinates are Infisical `/promotions/PROMOTIONS_CODE_LOOKUP_HMAC_KEY` or environment variable `PROMOTIONS_CODE_LOOKUP_HMAC_KEY`, but either source still requires the matching qualified `SecretBinding`. During overlap, each retained qualifier must reference its own immutable external value, for example version-specific Infisical keys or environment-variable names.

Changing `ActiveKeyVersion` changes only new code writes. Lookup reads inspect the key versions referenced by active code rows and resolve each matching qualifier, so operators must retain older qualified secrets until no active promotion code uses them. See [SECRETS.md](SECRETS.md#promotion-code-lookup-hmac-key) for value format and the rotation sequence.

### Reporting Static Configuration

`Reporting:*` controls local event-report intake and the moderation-provider runtime mode introduced by the event-reporting bounded context. Submission remains local-first: it creates the canonical report, event target, encrypted reporter-text evidence, local case, and provider-sync outbox intent before any external integration can run.

Provider runtime keys are infrastructure-only switches. They control `RuntimeModerationProviderResolver` and do not disable local report submission:

| Key | Default | Description |
|---|---:|---|
| `Reporting:Enabled` | `true` | Enables provider synchronization contracts. `false` returns provider-disabled results for external sync/evaluation paths while local intake and queue management remain available. |
| `Reporting:Mode` | `LocalOnly` | Active provider mode. Supported values: `Disabled`, `LocalOnly`, `Osprey`, `Coop`, and `Composite`. `LocalOnly` performs no network calls. |
| `Reporting:SyncReports` | `true` | Allows report-sync contract execution when provider mode is not disabled. Set `false` to keep outbox dispatch from attempting provider sync work. |
| `Reporting:EvaluateSignals` | `false` | Enables safety-signal evaluation only for `Osprey` or `Composite` modes. When enabled with a configured Osprey endpoint, the runtime resolver calls the Osprey signal adapter after local report sync. |
| `Reporting:MirrorReviewQueue` | `false` | Enables external review-case mirroring only for `Coop` or `Composite` modes. When enabled with a configured Coop endpoint, the runtime resolver mirrors local report cases after local report sync. |
| `Reporting:ExecuteDecisions` | `true` | Allows provider decision execution to delegate to the local decision executor. Set `false` to fail decision execution contracts with a non-retryable disabled result. |
| `Reporting:EvidenceMode` | `MetadataOnly` | Evidence-sharing posture for provider envelopes. Supported values are `MetadataOnly`, `SafeSummaryOnly`, and `ReporterText`; `ReporterText` is rejected for `Disabled` and `LocalOnly` modes. |

Control-plane health uses additional static reporting keys. These affect only aggregate operations dashboards and do not change report intake, routing, or provider dispatch behavior:

| Key | Default | Description |
|---|---:|---|
| `Reporting:Health:StuckProviderSyncMinutes` | `120` | Pending provider-sync links older than this window are counted as stuck in the control-plane `moderation-reporting` operations card. |
| `Reporting:Health:FailedProviderSyncWarningThreshold` | `1` | Failed provider-sync links at or above this count produce the `moderation_reporting_provider_sync_failures` warning. |

Hierarchical reporting settings add tenant provider targets on top of the instance baseline. Static `Reporting:*`, `Reporting:Osprey:*`, and `Reporting:Coop:*` remain the instance-level runtime authority, and local canonical reporting is always required before any external provider sync. Tenant settings are additive only: they can enable tenant-owned Osprey or Coop targets when instance administrators unlock them, but they cannot weaken an enabled instance Osprey or Coop baseline.

| Hierarchical key | Default | Scope | Description |
|---|---:|---|---|
| `governance.lock_tenant_reporting_providers` | `true` | Instance | Broad lock for all tenant-owned reporting providers. Keep locked unless tenant admins may configure their own moderation providers. |
| `governance.lock_tenant_osprey_provider` | `true` | Instance | Provider-specific lock for tenant Osprey targets. |
| `governance.lock_tenant_coop_provider` | `true` | Instance | Provider-specific lock for tenant Coop targets. |
| `reporting.tenant_external_sync_enabled` | `true` | Tenant | Gates tenant-added external targets only. It does not disable local intake or instance baseline providers. |
| `reporting.enable_tenant_osprey_provider` | `false` | Tenant | Enables a tenant-owned Osprey target when the broad and Osprey-specific locks are open and endpoint credentials are configured. |
| `reporting.enable_tenant_coop_provider` | `false` | Tenant | Enables a tenant-owned Coop target when the broad and Coop-specific locks are open and endpoint credentials are configured. |
| `reporting.osprey_routing_mode` | `both` | Tenant | Requested Osprey routing posture: `instance`, `tenant`, or `both`. Dispatch must still preserve instance-first/additive behavior. |
| `reporting.coop_routing_mode` | `both` | Tenant | Requested Coop routing posture: `instance`, `tenant`, or `both`. Dispatch must still preserve local-first provider sync semantics. |
| `reporting.evidence_mode` | `MetadataOnly` | Tenant | Tenant evidence posture for reporting provider envelopes. Supported values are `MetadataOnly`, `SafeSummaryOnly`, and `ReporterText`; runtime safety gates still apply. |
| `reporting.osprey_endpoint_url` | unset | Tenant | Tenant-owned Osprey-compatible endpoint URL. |
| `reporting.coop_endpoint_url` | unset | Tenant | Tenant-owned Coop-compatible endpoint URL. |
| `reporting.osprey_api_key` | unset | Tenant secret | Sensitive tenant Osprey credential. Never expose in browser DTOs, HAL links, logs, metrics, traces, screenshots, or support bundles. |
| `reporting.osprey_webhook_secret` | unset | Tenant secret | Sensitive tenant Osprey callback/signing secret for deployments that use one. |
| `reporting.coop_api_key` | unset | Tenant secret | Sensitive tenant Coop credential. Never expose in browser DTOs, HAL links, logs, metrics, traces, screenshots, or support bundles. |
| `reporting.coop_webhook_secret` | unset | Tenant secret | Sensitive tenant Coop callback HMAC secret. |

Tenant routing updates accept endpoint and secret fields only at the write boundary. Omitted or blank API-key/webhook-secret fields preserve the existing tenant secret; there is no implicit clear-secret behavior. Rotate by writing a new value through an authorized tenant settings action or by rotating the backing deployment secret, and verify through configured/readiness flags rather than secret readback.

Tenant provider test actions are readiness checks over effective routing state. They validate lock state, provider enablement, tenant target presence, and configured endpoint/API-key flags without making external HTTP calls and without returning endpoint URLs, API keys, webhook secrets, provider payloads, or raw provider errors.

Osprey adapter keys live under `Reporting:Osprey:*`. The adapter is Infrastructure-only and posts a safe JSON evaluation request to the configured endpoint; the request contains tenant/report/event/case IDs, normalized report/case/priority codes, timestamps, idempotency/correlation IDs, and evidence mode only. It does not send reporter text, reporter IP hashes, user-agent hashes, raw provider payloads, event titles, slugs, or URLs. Public research did not identify an official .NET Osprey SDK or stable public REST contract, so the current adapter targets a configurable Osprey-compatible HTTP JSON/RPC facade and can be swapped to generated gRPC/proto wiring once a deployment supplies that contract. The local `osprey` Compose/Aspire resource uses the Roost coordinator image and exposes coordinator ports; do not set `Reporting:Osprey:EndpointUrl` to that coordinator port unless a compatible HTTP facade is in front of it.

| Key | Default | Description |
|---|---:|---|
| `Reporting:Osprey:Enabled` | `false` | Enables the concrete Osprey signal adapter. `Reporting:Mode` must still be `Osprey` or `Composite`, and `Reporting:EvaluateSignals` must be `true` before runtime signal evaluation happens. |
| `Reporting:Osprey:EndpointUrl` | unset | Deployment-controlled Osprey-compatible base URL. Required when the adapter is enabled. Must be HTTP/HTTPS, absolute, and must not include embedded credentials, query string, or fragment. Local/private endpoints require explicit opt-in. |
| `Reporting:Osprey:EvaluatePath` | `/api/v1/evaluate` | Relative evaluation path appended to `EndpointUrl` unless the endpoint already ends with the same path. Must start with `/`. |
| `Reporting:Osprey:ApiKey` | unset | Optional sensitive credential. If configured with the default header name, it is sent as `Authorization: Bearer <token>`. Never expose this value in browser payloads, health data, logs, metrics, traces, screenshots, or issue templates. |
| `Reporting:Osprey:ApiKeyHeaderName` | `Authorization` | Header used for `ApiKey`. Custom names are allowed only when they are valid HTTP header names. |
| `Reporting:Osprey:EventType` | `event_report` | Stable event type sent in the evaluation body so an Osprey-compatible facade can route rules. |
| `Reporting:Osprey:TimeoutSeconds` | `10` | Per-request timeout budget. Must be between 1 and 300. Timeouts are treated as retryable provider failures. |
| `Reporting:Osprey:AllowLocalProviderEndpoints` | `false` | Allows loopback/link-local/private provider URLs for deliberate self-hosted deployments. Keep `false` for public/provider-hosted endpoints. |

Asynchronous Osprey signal callbacks are accepted at `POST /api/integrations/moderation/osprey/callback`. This endpoint does not use the outbound `Reporting:Osprey:ApiKey`; it uses the platform API-key authentication scheme (`X-API-Key`) plus the `ModerationIntegration.OspreyCallback` authorization policy. Provision a tenant-scoped API key with event moderation authority, such as `events:write`, tenant admin, or instance admin equivalent scope, for the external Osprey-compatible callback worker. Callback payloads must include tenant, report, and event identifiers; the Application layer rechecks that they match the ambient API-key tenant before recording signals. The endpoint stores bounded signal metadata and external sync markers only; it must not receive or log raw provider payloads, reporter text, event titles, slugs, URLs, IP hashes, user-agent hashes, or provider secrets.

Coop adapter keys live under `Reporting:Coop:*`. The adapter is Infrastructure-only and posts local report-case metadata to a configurable Coop-compatible review queue ingest endpoint. Public ROOST Coop sources describe REST-style ingest and decision webhooks but do not publish a stable OpenAPI/GraphQL contract, authentication scheme, error catalog, or rate-limit policy; this implementation therefore uses a conservative JSON envelope and flexible response mapping until a deployment supplies a stricter contract. The mirror request contains tenant/report/event/case IDs, queue/status/priority/reason codes, timestamps, idempotency/correlation IDs, and an evidence descriptor. It does not send reporter text, reporter IP hashes, user-agent hashes, raw provider payloads, event titles, slugs, or URLs.

| Key | Default | Description |
|---|---:|---|
| `Reporting:Coop:Enabled` | `false` | Enables the concrete Coop review queue adapter. `Reporting:Mode` must still be `Coop` or `Composite`, and `Reporting:MirrorReviewQueue` must be `true` before runtime mirroring happens. |
| `Reporting:Coop:EndpointUrl` | unset | Deployment-controlled Coop-compatible base URL. Required when the adapter is enabled. Must be HTTP/HTTPS, absolute, and must not include embedded credentials, query string, or fragment. Local/private endpoints require explicit opt-in. |
| `Reporting:Coop:MirrorPath` | `/api/v1/items` | Relative review-case ingest path appended to `EndpointUrl` unless the endpoint already ends with the same path. Must start with `/`. |
| `Reporting:Coop:ApiKey` | unset | Optional sensitive credential. If configured with the default header name, it is sent as `Authorization: Bearer <token>`. Never expose this value in browser payloads, health data, logs, metrics, traces, screenshots, or issue templates. |
| `Reporting:Coop:ApiKeyHeaderName` | `Authorization` | Header used for `ApiKey`. Custom names are allowed only when they are valid HTTP header names. |
| `Reporting:Coop:ItemType` | `event_report` | Stable item type sent in the mirror body so a Coop-compatible facade can route report cases. |
| `Reporting:Coop:TimeoutSeconds` | `10` | Per-request timeout budget. Must be between 1 and 300. Timeouts are treated as retryable provider failures. |
| `Reporting:Coop:AllowLocalProviderEndpoints` | `false` | Allows loopback/link-local/private provider URLs for deliberate self-hosted deployments. Keep `false` for public/provider-hosted endpoints. |
| `Reporting:Coop:WebhookSecret` | unset | Required for inbound Coop decision callbacks. The API rejects callbacks with `503` until this shared secret is configured. Treat it as a deployment secret; never expose it to browsers, logs, health checks, traces, screenshots, or issue templates. |
| `Reporting:Coop:WebhookSignatureHeaderName` | `X-Coop-Signature` | Header containing the HMAC-SHA256 signature. Valid values are raw hex/base64, `sha256=<signature>`, or `v1=<signature>`; multiple comma-separated candidates are supported for rotation windows. |
| `Reporting:Coop:WebhookTimestampHeaderName` | `X-Coop-Timestamp` | Header containing the signed timestamp. Unix seconds and invariant-culture date/time values are accepted. |
| `Reporting:Coop:WebhookToleranceSeconds` | `300` | Maximum absolute clock skew for signed callbacks. Must be between 30 and 86400. |
| `Reporting:Coop:WebhookMaxBodyBytes` | `65536` | Raw callback body size limit before JSON parsing. Must be between 1024 and 1048576. |

Coop callbacks are accepted at `POST /api/integrations/moderation/coop/callback`. The endpoint requires platform API-key authentication plus timestamped HMAC-SHA256 verification over `timestamp + "." + rawBody`. Verified bytes are retained in `incoming_webhook_messages`, and one durable `IncomingWebhookEffectOutbox` pointer is created in the same transaction. A separate worker loads the retained callback and dispatches `ProcessCoopDecisionCallbackCommand`; the pointer completes only after command success and an applied-effect receipt commit together.

The pointer drain shares the bounded incoming-processing options below. Set `Enabled=false` and restart the API to remove both owned Quartz drains during an incident; callback intake remains available and durable work accumulates. Increase limits only after checking PostgreSQL and command-handler capacity.

| Key | Default | Description |
|---|---:|---|
| `Webhooks:IncomingProcessing:Enabled` | `true` | Registers `incoming-webhook-intake-drain` and `incoming-webhook-effect-drain`. Disabling pauses work without deleting retained callbacks or pointers. |
| `Webhooks:IncomingProcessing:BatchSize` | `50` | Maximum effect pointers claimed in one bounded batch. Range `1..1000`. |
| `Webhooks:IncomingProcessing:MaxConcurrentItems` | `8` | Maximum concurrent claims executed by one process. Range `1..128`. |
| `Webhooks:IncomingProcessing:LeaseSeconds` | `120` | Fenced claim lease duration. Active workers renew at approximately one third of this duration. Range `5..3600`. |
| `Webhooks:IncomingProcessing:MaxAttempts` | `8` | Attempt ceiling before terminal dead-letter. Range `1..100`. |
| `Webhooks:IncomingProcessing:InitialRetryDelaySeconds` | `30` | Initial bounded retry delay. Range `1..86400`. |
| `Webhooks:IncomingProcessing:MaxRetryDelaySeconds` | `3600` | Maximum bounded retry delay. Range `1..86400`. |
| `Webhooks:IncomingProcessing:PollIntervalSeconds` | `5` | Quartz interval for both drains. Range `1..3600`. |
| `Webhooks:IncomingProcessing:EffectBacklogWarningThreshold` | `500` | `/health/webhooks/coop-effects` degrades when due work reaches this safe aggregate count. |
| `Webhooks:IncomingProcessing:EffectStaleLeaseWarningThreshold` | `1` | Coop-effect readiness degrades when stale claims reach this safe aggregate count. |
| `Webhooks:IncomingProcessing:IntakeBacklogWarningThreshold` | `500` | Aggregate incoming-intake readiness threshold. |
| `Webhooks:IncomingProcessing:IntakeStaleLeaseWarningThreshold` | `1` | Aggregate expired intake-lease readiness threshold. |

Local report-intake keys are application-layer submission controls:

| Key | Default | Description |
|---|---:|---|
| `Reporting:RequireAuthenticatedReporter` | `true` | Requires a resolved authenticated user before accepting a report. Keep enabled for the initial user-facing flow. |
| `Reporting:MaxReportsPerUserPerHour` | `10` | Per-reporter hourly ceiling across event reports. `0` disables this pre-write quota. |
| `Reporting:MaxReportsPerEventPerUserPerDay` | `3` | Per-reporter, per-event daily ceiling. `0` disables this pre-write quota. |
| `Reporting:DuplicateWindowHours` | `24` | Window for rejecting duplicate reports with the same reporter identity, event, and reason code. Values below `1` are normalized to `1`. |
| `Reporting:ReporterTextRetentionDays` | `180` | Retention horizon stamped on reporter-text evidence. Values below `1` are normalized to `1`. |
| `Reporting:MaxReporterTextLength` | `4000` | Maximum reporter text length accepted by the command validator. Values below `1` are normalized to `1`. |
| `Reporting:DefaultQueueCode` | `default` | Local report-case queue assigned at intake. Blank values normalize to `default`. |
| `Reporting:CaseSlaHours` | `48` | Sole report-response SLA input and local case due-at offset. Inclusive range `1..720`; invalid startup configuration fails validation. |
| `Reporting:ReporterFingerprintPepper` | empty | Optional deployment secret used by the API to HMAC tenant-scoped reporter IP and User-Agent fingerprints before they reach the Application layer. Set this in production/self-hosted deployments. |

Reporter text is protected through ASP.NET Core Data Protection before persistence and stored only in `EventReportEvidence.TextBodyEncrypted`. The outbox payload intentionally excludes reporter text, reporter IP hash, and user-agent hash. Reporting metrics use bounded outcome/failure tags and must not include reporter content, event titles, slugs, URLs, provider payloads, or raw errors.

### Lifecycle-email settings

`Reporting:CaseSlaHours` remains the sole report-response SLA input; no minimum/maximum business-day settings are permitted. Report submission snapshots the resolved `1..720` hour value, template version, and rendered receipt copy in the same transaction as the report and delivery graph, so later configuration cannot rewrite queued copy. `EmailDispatch:EventReminderLeadTimeHours` is the sole reminder lead setting, defaults to `24`, and accepts the inclusive range `1..168`; invalid configuration fails startup validation and the scheduler repeats the range check defensively. The reminder targets the earliest covered published session after a parent registration becomes Approved. A future session whose due-at is already past becomes due immediately after commit, while a started session creates no reminder.

Reminder in-app and email channels are both optional under `ReminderOptional` and the `event-updates` preference category. An enabled in-app preference creates an immediate in-app reminder; a disabled preference creates a skipped channel decision. Email work is materialized only when the snapshotted email preference is enabled and the current persisted address is verified. A disabled email preference records `email_preference_disabled` without persisting an `EmailDispatchOutbox`, recipient address, rendered email body, or scheduler pointer; missing or unverified addresses likewise create a typed skipped email delivery. Dispatch-time checks may narrow an enabled snapshot but never broaden a disabled snapshot.

Retention, SMTP/fanout global and per-tenant concurrency, fair scheduling, token-bucket rate limits, high/low backlog watermarks, and persisted pause controls will be added under their owning options groups in Tasks 1.5, 1.6, and 3.6. Those tasks must document exact keys/defaults with validators when runtime bindings exist; this approved policy does not invent duplicate settings in advance.

Event-report observability uses `Explore.Business` counters with bounded labels only: `explore.event_reports.submissions` (`tenant_id`, `outcome`, `failure_category`), `explore.event_reports.workflow_actions` (`tenant_id`, `action`, `outcome`, `failure_category`), `explore.event_reports.provider_syncs` (`tenant_id`, `provider`, `outcome`, `failure_category`), and `explore.event_reports.provider_callbacks` (`tenant_id`, `provider`, `outcome`, `failure_category`). Controller and dispatcher logs may include tenant/report/event/case IDs, action/provider/outcome/failure codes, and retryability flags, but must not log reporter text, IP/User-Agent values or hashes, event titles, slugs, URLs, provider payload bodies, provider credentials, raw callback signatures, or raw exception text.

### MCP Adapter Static Configuration

The Model Context Protocol adapter is enabled by default in development and self-hosted startup configuration, unless `Mcp:Enabled=false`/`MCP_ENABLED=false` is set. The API composes the official MCP SDK services and maps the Streamable HTTP endpoint at `/mcp` by default. Use [MCP_DEBUGGING.md](MCP_DEBUGGING.md) for redacted local client templates and smoke guidance. It remains an adapter over the AI Tool Contract Registry rather than a second tool authority. The implementation wires configuration, health, endpoint registration, read-only registry discovery, anonymous-safe public event reads, authenticated event-management reads/resources, first-class registry-projected proposal tools, safe AI conversation resources, event-management confirmation prompts, and proposal-first tool mutation through the normal MediatR/API confirmation path.

| Key | Default | Description |
|---|---:|---|
| `Mcp:Enabled` | `true` | Enables the API-hosted MCP endpoint. Set `false` only when the endpoint must be unmapped at startup. |
| `Mcp:EndpointPath` | `/mcp` | Route prefix for the Streamable HTTP MCP endpoint. Bare values such as `mcp` are normalized to `/mcp` before validation and endpoint mapping. |
| `Mcp:Stateless` | `true` | Uses stateless Streamable HTTP so API replicas do not require MCP session affinity. Startup validation rejects `false`. |
| `Mcp:EnableLegacySse` | `true` | Startup ceiling for future legacy-SSE governance only. Runtime legacy SSE remains unavailable and the health check reports `legacySseRuntimeEnabled=false`. |

Runtime MCP governance is stored in the hierarchical settings cascade. These values can disable an already mapped startup endpoint without restarting the API, but they cannot map a new endpoint when `Mcp:Enabled=false` at startup and cannot make endpoint path/stateless mode runtime-editable.

| Setting key | Default | Scope | Description |
|---|---:|---|---|
| `mcp.enabled` | `true` | Instance/Tenant | Runtime adapter switch. Effective MCP exposure is `Mcp:Enabled && resolved(mcp.enabled)`. Instance administrators can disable the adapter without changing route shape. |
| `mcp.enable_legacy_sse` | `false` | Instance/Tenant | Records legacy-SSE governance intent only. Current runtime keeps `legacySseRuntimeEnabled=false` even when startup and DB values are true. |
| `governance.lock_tenant_mcp` | `true` | Instance | Prevents tenant administrators in multi-tenant mode from overriding `mcp.enabled`. |
| `governance.lock_tenant_mcp_legacy_sse` | `true` | Instance | Prevents tenant administrators in multi-tenant mode from overriding `mcp.enable_legacy_sse`. |

MCP must not expose provider credentials, endpoint URLs, model IDs, prompts, provider responses, tool payloads, tenant IDs, or raw provider errors in configuration diagnostics, health data, logs, metrics, or browser responses. Mutating MCP tools must keep using the proposal/confirmation workflow and must never write repositories directly.

Operational expectations:

- Keep `Mcp:Enabled=true` or unset for the default local/self-hosted endpoint at `/mcp`. Use `mcp.enabled=false` for runtime rollback without restart, or set startup `Mcp:Enabled=false` only when the endpoint must be unmapped.
- Keep `Mcp:EndpointPath` and `Mcp:Stateless` startup-only. Runtime/admin settings must not change route shape or session posture after startup.
- Keep `Mcp:Stateless=true`; the initial adapter is designed for stateless Streamable HTTP and API replicas without MCP session affinity. Startup validation rejects `false`.
- `Mcp:EnableLegacySse` defaults to `true` as a startup ceiling for future governance, but the current runtime does not enable legacy SSE because the SDK's legacy mode requires stateful in-memory sessions and weaker backpressure than Streamable HTTP.
- Do not add a product `stdio` MCP host to the API deployment. `stdio` remains deferred by [ADR-011](adr/ADR-011-local-mcp-stdio-diagnostic-host.md) unless a future local-only diagnostic host is separately approved and verified.
- Treat MCP as API-key-first for external clients. The endpoint is mapped anonymously so SDK authorization filters can expose explicitly anonymous-safe registry discovery, while scope-aware MCP authorization policies and normal MediatR authorization still gate scoped operations. Valid `X-API-Key` requests with `mcp:read` can use generic MCP read resources; protected event-management reads require `mcp:read` plus event read-equivalent scope authority. `mcp:propose` is required to discover/call proposal tools or proposal prompts, and no key, invalid keys, or revoked keys can use only anonymous-safe capabilities. Valid MCP API-key traffic is rate-limited per key ID; no-key, invalid-key, and revoked-key traffic remains rate-limited per remote IP and must not echo credentials in `429` bodies.
- Keep SDK registration explicit. The API host uses `WithTools<T>()`, `WithResources<T>()`, `WithPrompts<T>()`, and registry-projected tool options instead of assembly-wide discovery so transport/startup behavior remains reviewable and avoids avoidable reflection pressure. Native AOT compatibility is not promised until a dedicated publish profile is added and tested.
- For production, expose MCP only through the same trusted HTTPS boundary as the API. Do not rely on local certificate bypasses or `curl -k` in production client instructions.
- Treat projected `propose_*` MCP tools as ergonomic wrappers only. Their payload fields come from ATCR JSON schemas, SDK annotations are hints, and execution still persists a proposed action for API/HAL confirmation.
- Treat MCP protocol evolution as ADR-gated. There is no configuration-only switch for stateful sessions, resource subscriptions, sampling, elicitation, roots, progress notifications, list-changed notifications, or client-specific compatibility shims.
- Verify `/health` includes `mcp-adapter` before exposing MCP. Disabled MCP reports intentional degraded readiness posture; enabled MCP reports healthy configuration posture with `enabled`, `startupEnabled`, `runtimeEnabled`, `legacySseStartupCeiling`, `legacySseRuntimeRequested`, and `legacySseRuntimeEnabled` safe booleans, not tenant IDs or secrets.

The `ai-provider` readiness check reports safe booleans such as `endpointConfigured`, `apiKeyConfigured`, and `modelConfigured`; it never reports raw endpoint URLs, API keys, prompts, responses, model IDs, provider request IDs, or provider exception bodies.

### Cerbos Authorization Configuration

Cerbos runtime settings are the first implemented consumer of the shared secrets ownership metadata:

- `Authorization:Provider` is the authoritative deployment selector. `AUTHORIZATION_PROVIDER` maps to it without overriding an existing canonical value. Accepted values are blank, `local`, and `cerbos`; any other explicit value fails startup validation.
- Blank/unset provider intent does not infer Cerbos from endpoint or credential presence. The onboarding page selects Local RBAC by default and keeps Cerbos behind the native **Advanced: use Cerbos PDP** disclosure.
- Explicit `local` is deployment-managed, reports authorization ready, skips the provider-choice page, and performs no Cerbos endpoint or policy call.
- Explicit `cerbos` is deployment-managed and selected by runtime authorization immediately, so failures deny rather than falling back to Local. The API background worker verifies the instance PDP gRPC health service and then publishes the bundled policy package specifically to the instance Admin API, never an ambient tenant BYO target. It retries transient startup failures within the configured bound. Configured status becomes ready only after both operations succeed; automatic navigation skips the choice page while reconciliation is pending or ready, and a final failure is exposed as locked remediation from the instance setup task.
- `Cerbos:GrpcEndpoint` can prefill onboarding/admin forms as deployment bootstrap. Once an operator saves an application-managed endpoint, the saved setting takes precedence unless the key is explicitly deployment-managed.
- `Cerbos:UsePolicyScope` defaults to `false`. Keep it false for bundled root policies; enable it only when the PDP has tenant-scoped policy files and `engine.lenientScopeSearch=true`.
- `Cerbos:AdminApi:*` configures explicit policy package sync/status operations only. Runtime authorization uses the gRPC PDP and never waits for the Admin API. Admin requests have a 10-second HTTP timeout.
- `Cerbos:AdminApi:AdminUsername` and `Cerbos:AdminApi:AdminPassword` may come from environment variables or Infisical. When both are present, onboarding uses them by default and keeps the one-time override form collapsed. When either is missing, onboarding opens the one-time credential form. A complete UI pair overrides deployment credentials for one request and is never persisted.
- `Secrets:Ownership:DeploymentManagedKeys` can mark `cerbos.grpc_endpoint` or `*` as deployment-managed. Deployment-managed endpoint fields are read-only in UI and ignore application-managed DB values for that field.
- Setup and post-onboarding administration reuse the existing endpoint verification, package download/sync, and local-fallback capabilities. They do not add a Cerbos resource inventory or an arbitrary policy-decision test API.

Background reconciliation binds from `Cerbos:PolicyBootSync`:

| Key | Default | Description |
|---|---:|---|
| `InitialDelaySeconds` | `5` | Delay after API startup before deployment-provider reconciliation begins. |
| `RetryDelaySeconds` | `3` | Delay between failed automatic reconciliation attempts. |
| `MaxAttempts` | `5` | Maximum automatic attempts per process start. |
| `TimeoutSeconds` | `60` | Maximum time for each verification-and-publish attempt. |

For a Coolify-managed external Cerbos PDP, use [CERBOS_COOLIFY.md](CERBOS_COOLIFY.md) for the Docker Image tag, PostgreSQL schema bootstrap, Admin API password hash, gRPC routing, and `cerbosctl` upload flow. Compose and Aspire local infrastructure use the repository `cerbos/` folder directly.
- When `Authorization:Provider` is blank, governance settings select the active provider (`AuthorizationProvider`). Explicit deployment intent takes precedence. Governance still owns whether tenant customization is enabled and per-tenant BYO values such as `cerbos.mode`, `cerbos.custom_endpoint`, custom Admin API endpoint, and custom Admin API credentials.

Endpoint and secret safety rules:

- Non-local PDP/Admin API endpoints must use safe TLS-capable URLs. Unsafe endpoint changes are rejected before provider settings are persisted or sync/cache invalidation runs.
- Runtime failure logs must not include raw PDP/Admin API endpoints, Admin API credentials, JWTs/tokens, response bodies, or exception objects/messages.
- A tenant with `cerbos.mode=custom_endpoint` and a blank PDP endpoint remains in BYO mode. Runtime authorization activates safe mode instead of falling back to the instance PDP, while any explicit BYO Admin API configuration is still preserved for package operations.
- Any BYO PDP failure activates provider-instance safe mode for local fallback decisions. There is no fail-open setting: the `cerbos.failure_mode` governance key was deleted because it was parsed and then ignored at runtime, so it could only mislead an operator into believing they had configured fallback behaviour.
- The privileged package-status endpoint may inspect Admin API policy hashes for operator drift diagnostics. This inspection is explicit and never runs from runtime authorization or HAL capability planning.

### Email Dispatch Scheduler Configuration

Basic Dispatch Mode uses the selected primary database as the durable source of
truth and the existing SMTP abstraction as the transport. It does **not**
require RabbitMQ. Registration confirmation creates an `EmailDispatchOutbox`
row in the registration transaction. The Quartz scheduler can trigger the shared
drain on every supported primary provider, including SQLite; `HostedService`
remains available as a scheduler-free timer alternative. Both triggers call the
same drain service and preserve the same outbox, retry, and idempotency semantics.

Static dispatch settings bind from `EmailDispatchProcessor` and are validated at startup with `ValidateOnStart`:

| Key | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables Basic Dispatch Mode. When disabled, the `email-dispatch` readiness check reports `Degraded` intentionally. |
| `Mode` | `Quartz` | Selects `Quartz`, `HostedService`, or `Disabled`. `Quartz` uses the durable ADO job store on every supported primary provider; `HostedService` is the portable timer wrapper over the same drain. |
| `PollingIntervalSeconds` | `5` | Delay between polling loops. Must be greater than zero. |
| `BatchSize` | `50` | Maximum rows claimed per loop. Valid range `1..1000`. |
| `MaxRowsPerTenantPerBatch` | `5` | Fair-round cap for one tenant in a batch. Valid range `1..BatchSize`, up to `1000`. |
| `MaxConcurrentDispatches` | `8` | Cross-replica global `Processing` ceiling. Valid range `1..256`. |
| `MaxConcurrentDispatchesPerTenant` | `2` | Cross-replica per-tenant `Processing` ceiling. Valid range `1..MaxConcurrentDispatches`, up to `256`. |
| `GlobalSmtpRateLimitPerMinute` | `120` | Shared PostgreSQL-backed global SMTP admissions per one-minute window. Valid range `1..100000`. |
| `TenantSmtpRateLimitPerMinute` | `30` | Shared PostgreSQL-backed admissions per tenant per one-minute window. Valid range `1..GlobalSmtpRateLimitPerMinute`, up to `100000`. |
| `OptionalBacklogHighWatermark` | `1000` | Active core-backlog level that enables persisted optional-reminder deferral. Valid range `1..1000000`. |
| `OptionalBacklogLowWatermark` | `500` | Backlog level below which optional reminders resume. Valid range `0..999999` and must be lower than the high watermark. |
| `MaxAttemptCount` | `5` | Worker-level cap used with per-row `MaxAttempts` before dead-lettering. Must be greater than zero. |
| `InitialRetryDelaySeconds` | `5` | Base retry delay for failed SMTP dispatch. Must be greater than zero. |
| `MaxRetryDelaySeconds` | `3600` | Maximum retry delay cap. Must be greater than or equal to `InitialRetryDelaySeconds`. |
| `ProcessingLeaseTimeoutSeconds` | `900` | Maximum age for a `Processing` lease before recovery. Unfenced claims become immediately retryable; claims with provider-handoff evidence become `Unknown`. Must be greater than zero. |
| `HealthDueDispatchWarningThreshold` | `1000` | Degrades `email-dispatch` readiness when due `Pending`/`RetryScheduled` outbox rows reach this count. Must be between 1 and 100000. |
| `HealthStaleProcessingWarningThreshold` | `1` | Degrades `email-dispatch` readiness when stale `Processing` rows reach this count. Must be between 1 and 10000. |
| `HealthUnknownWarningThreshold` | `1` | Degrades readiness when active non-paused `Unknown` rows requiring reconciliation reach this count. Must be between 1 and 10000. |
| `HealthDeadLetterWarningThreshold` | `1` | Degrades `email-dispatch` readiness when `DeadLettered` rows reach this count. Must be between 1 and 10000. |
| `HealthOldestPendingWarningSeconds` | `900` | Degrades readiness when the oldest active due row reaches this age. Must be between 1 and 604800. |
| `HealthTenantBacklogWarningThreshold` | `250` | Degrades readiness when a sampled active tenant backlog reaches this count. Must be between 1 and 100000. |
| `HealthTenantSampleLimit` | `10` | Maximum tenant backlog samples used by readiness. Metrics expose rank only, never the tenant GUID. Must be between 1 and 100. |
| `ConsumerId` | machine name | Drain identity recorded in receipts and logs. Must not be blank. |
| `VerboseLogging` | `false` | Enables additional drain logs when troubleshooting. Logs must remain free of bodies, recipients, and secrets. |

Concurrency, optional-work hysteresis, and SMTP rate state are PostgreSQL authorities shared by every API replica and every trigger. The `smtp` row in `email_dispatch_processor_states` stores instance pause state, a nullable `GlobalSmtpRateLimitPerMinute` override, the global bucket, and optional-reminder hysteresis; `email_dispatch_tenant_controls` stores pause state and each tenant bucket. The authenticated instance control API accepts an override from `1..100000`; clearing it restores the static configured value and resets the global bucket. Rate windows use the database clock. A new or expired bucket starts full, a rate decrease clamps retained tokens, and admission locks global state before tenant state and decrements both in one transaction.

Rate deferral is not an SMTP attempt. It releases the processing lease as `RetryScheduled` with `smtp_rate_deferred`, schedules the row at the later exhausted-bucket refill boundary, and creates no attempt, receipt, or `provider_handoff_started` evidence. Consequently rate pressure cannot consume retry budget or dead-letter a message without provider I/O.

SMTP host, port, security, and sender identity come from `email.*` governance.
`smtp.username` and `smtp.password` resolve exclusively through the selected
external authority; no database credential keys or aliases exist. Local Mailpit may
seed only non-secret delivery governance. RabbitMQ Dispatch Mode is not part of Basic mode.

The `email-dispatch` readiness payload reports active non-paused `dueDispatchCount`, `retryScheduledCount`, `staleProcessingCount`, `unknownCount`, `parkedCount`, `deadLetteredCount`, `oldestActivePendingAgeSeconds`, persisted `optionalReminderDeferralActive`, and sanitized `globalPaused`/`globalSmtpRateLimitOverrideActive` booleans. A deliberate global pause degrades readiness while backlog counts stay visible. Future retries remain informational until due, and `Parked` remains visible without degrading readiness. Public health serialization redacts tenant/user/provider identifiers, addresses, subjects, bodies, event titles, reasons, actors, and evidence.

### Notification Fanout Processor Configuration

Recipient-level notification fanout uses PostgreSQL occurrences and run leases as its only work authority. Static settings bind from `NotificationFanoutProcessor` and fail startup validation when concurrency, paging, lease, backpressure, or readiness limits are unsafe:

| Key | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables API-hosted recipient fanout processing. Disabled mode degrades `notification-fanout` readiness. |
| `PollingIntervalSeconds` | `5` | Delay between completed claim rounds. Valid range `1..300`. |
| `PageSize` | `250` | Maximum deterministic attendee records read before each durable cursor checkpoint. Valid range `1..1000`. |
| `MaxClaimsPerRound` | `8` | Local claim/execution bound per poll. Must be between `1` and `MaxActiveClaims`, up to `256`. |
| `MaxActiveClaims` | `8` | PostgreSQL-enforced active claim ceiling shared by every replica. Valid range `1..256`. |
| `MaxActiveClaimsPerTenant` | `2` | PostgreSQL-enforced active claim ceiling for one tenant. Must not exceed `MaxActiveClaims`. |
| `ClaimLeaseSeconds` | `120` | Lease horizon renewed by the page processor before audience work. Valid range `30..3600`. |
| `OptionalReminderBacklogHighWatermark` | `1000` | Active non-reminder occurrence backlog that durably enables reminder deferral. Valid range `1..1000000`. |
| `OptionalReminderBacklogLowWatermark` | `500` | Backlog at or below which deferred reminders resume. Must be lower than the high watermark. |
| `HealthDueOccurrenceWarningThreshold` | `1000` | Degrades readiness when runnable due occurrences reach this count. Valid range `1..1000000`. |
| `HealthExpiredClaimWarningThreshold` | `1` | Degrades readiness when expired processing leases reach this count. Valid range `1..10000`. |
| `HealthOldestDueWarningSeconds` | `900` | Degrades readiness when the oldest due occurrence business age reaches this value. Valid range `1..604800`. |
| `ConsumerId` | machine name | Bounded lease owner identity. Required and limited to 200 characters; it is not exposed in metrics or health payloads. |

Global claim admission is serialized with a PostgreSQL advisory lock, then tenant, event-precedence, and occurrence locks are acquired in that order. Exact post-lock counts enforce both active ceilings across replicas; `MaxClaimsPerRound` is only a local optimization. One fair round chooses at most one occurrence per tenant by priority descending, occurrence time, and occurrence ID.

The singleton `notification_fanout_processor_states` row persists optional-reminder hysteresis across hosts and restarts. Backlog pressure counts due and processing non-reminder occurrences. While deferred, reminder occurrences and their run rows remain durable but are excluded from claim selection; required and other core work continues. No occurrence is superseded or deleted by backpressure.

The `notification-fanout` readiness payload exposes enabled state, aggregate due/core/reminder/active/expired/remaining occurrence counts, the current processed-recipient count for unfinished runs, lifetime superseded occurrence count, oldest due age, and the durable reminder-deferral boolean. It never exposes consumer, tenant, event, session, run, occurrence, recipient, template payload, or address data.

Email content retention binds from `EmailDispatchRetention` and is validated at startup:

| Key | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables the API-hosted retention processor. Disabling it stops new redaction passes without restoring already-redacted content. |
| `DryRun` | `false` | Counts one bounded eligible batch without changing rows. Enable this before changing retention policy in an existing deployment. |
| `InitialDelaySeconds` | `60` | Delay after API startup before the first cleanup pass. Must be zero or greater. |
| `PollingIntervalMinutes` | `60` | Delay between cleanup passes. Must be greater than zero. |
| `MaxTenantsPerPass` | `100` | Maximum tenant cohorts examined per pass. Oldest eligible cohorts are selected first. |
| `BatchSize` | `500` | Maximum parent dispatch rows processed per tenant. Attempts, receipts, and notification delivery metadata follow each selected parent in the same transaction. |
| `RetentionDays` | `180` | Age after `Sent` or `Skipped` before content is redacted. Must be greater than zero. `DeadLettered`, `Unknown`, and `Parked` rows remain unredacted until replay succeeds or an operator explicitly resolves them to `Skipped`; purged tenants are redacted immediately. |

Redaction clears recipient, subject, plain/HTML body, reply-to, provider/correlation identifiers, and content-bearing attempt/receipt/delivery fields while retaining typed, non-PII identifiers, categories, state, and timestamps. `ContentRedactedAt` is a permanent replay and provider-handoff fence; changing these settings cannot reconstruct redacted material.

Quartz host settings bind from `Scheduler:Quartz`:

| Key | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables the Quartz scheduler host for all platform jobs. Disabling it stops every queue drain, retention sweep, reconciliation job, and deadline trigger; EmailDispatch readiness is unhealthy when its mode is `Quartz`. |
| `SchedulerName` | `islamu-event-scheduler` | Logical scheduler identity recorded in the persistent store. Keep it stable for a deployment; give co-located instances distinct names when they share one database. |
| `InstanceId` | `AUTO` | `AUTO` lets Quartz generate a unique per-process id. Clustering requires `AUTO`. |
| `MaxConcurrency` | processor count | Maximum scheduler worker concurrency. Must be greater than zero. |
| `UsePersistentStore` | `true` | Uses the durable ADO job store. When `false` the scheduler runs in memory and loses scheduled work on restart. |
| `TablePrefix` | `QRTZ_` | Prefix for the co-located scheduler tables. Letters, digits, and underscores only, because it is inlined into DDL. |
| `ClusteringEnabled` | `false` | Enables the database-backed clustering protocol. Requires `UsePersistentStore=true` and `InstanceId=AUTO`. |
| `ClusterCheckinIntervalSeconds` | `20` | Node check-in interval when clustering is enabled. Must be greater than zero. |
| `ApplySchemaOnStartup` | `true` | Applies the embedded idempotent scheduler DDL during API startup. Disable only when a separate migration job owns the scheduler schema. |
| `ValidateSchemaOnStartup` | `true` | Asks Quartz to verify the job-store tables and columns while the scheduler initializes. It exists because the failure it catches is otherwise invisible: a missing *optional* column makes Quartz log a warning and keep running with that feature silently disabled — this platform shipped exactly such a defect (`MISFIRE_ORIG_FIRE_TIME`). With validation on, provider DDL drift fails startup and names the offending table instead. Requires `UsePersistentStore=true`; turn it off only as an operator escape hatch while a schema problem is being repaired, never to make a development build start. |
| `StatusEndpointEnabled` | `false` | Exposes the read-only scheduler status endpoint. Keep disabled unless instance operators explicitly need scheduler internals. |
| `StatusEndpointPath` | `/admin/scheduler` | Absolute non-root path for the status endpoint when enabled. |
| `StatusEndpointAuthorizationPolicy` | `quartz_instance_admin` | Host authorization policy for the status endpoint. Must not be blank or anonymous when the endpoint is enabled. The API enforces this policy on the path before any scheduler state is read. |
| `AdminApiEnabled` | `false` | Exposes the first-party scheduler administration API under `/api/admin/scheduler`. When disabled every action answers `404`, so HAL-driven clients omit the scheduler section entirely. Requires `Enabled=true`. |
| `AdminApiReadOnly` | `true` | Restricts the administration API to reads. Write affordances are withheld from HAL and mutating requests are refused before the scheduler is touched, so an advertised control and an accepted action can never disagree. |
| `DashboardEnabled` | `false` | Mounts the first-party Quartz.NET Blazor dashboard. Honoured only by the combined `Event.Standalone` host, where Razor components and the scheduler share a process; the split-mode API host ignores it. Requires `Enabled=true`. |
| `DashboardReadOnly` | `true` | Disables the dashboard's own mutating controls. Independent of `AdminApiReadOnly`. |
| `DashboardPath` | `/quartz` | Absolute non-root base path for the dashboard UI. |
| `DashboardAuthorizationPolicy` | `quartz_instance_admin` | Authorization policy applied across the dashboard's pages, SignalR circuit, and static assets. Must not be blank or anonymous when the dashboard is enabled. |

Two scheduler cadences are fixed in code rather than exposed as configuration keys, because changing either
in isolation would break the guarantee the pair provides:

| Cadence | Value | Why it is not a setting |
|---|---|---|
| `inventory-hold-expiry` | Per order, at its earliest hold expiry | Derived from the order's own `ExpiresAt`; there is no interval to tune. |
| `inventory-hold-expiry-reconciliation` | Cron `0 */5 * * * ?` | It is a safety net behind the per-order deadline, not the primary release path. Lowering it does not make holds expire sooner — the deadline already does that — and raising it only widens the window in which a *missed* deadline goes uncorrected. |

#### Scheduler monitoring and accountability

Independent of the operator surfaces below, the scheduler always publishes:

- A **`scheduler` readiness check** on `/health`, tagged `ready`. It reports `Degraded` when scheduling is disabled
  or the scheduler is in standby, and `Unhealthy` when the scheduler is shut down, unavailable while enabled, or has
  jobs whose triggers are in the error state. A paused scheduler or an error-stuck job is therefore visible to
  monitoring rather than silently doing nothing.
- **Structured audit records** for every attempted control action — successes and refusals alike — carrying the
  acting principal, action, target job, outcome, and correlation id. These go to the host's structured log pipeline
  for forwarding to a log store or SIEM.
- An **`explore.scheduler.admin_actions`** OpenTelemetry counter, labelled by action and outcome, with no tenant or
  principal identity.

#### Choosing a scheduler operator surface

| Surface | Topology | Enable with | What it gives |
|---|---|---|---|
| Status endpoint | Any | `StatusEndpointEnabled` | A single read-only JSON document for scripted checks. No UI. |
| Administration API + admin UI | Split **and** standalone | `AdminApiEnabled` | Scheduler lifecycle, job list with trigger states and fire times, and pause/resume/run-now actions, served as HAL and rendered by the **Background Scheduler** section under Instance Settings. |
| Quartz.NET dashboard | Standalone only | `DashboardEnabled` | The upstream Quartz dashboard at `DashboardPath`, including calendars, execution history, and live logs. |

The administration API and the dashboard are independent; enabling one does not
enable the other. The administration API is the portable choice because it works
in both topologies and follows the platform's own authorization and HAL
affordance rules. The dashboard is a deeper upstream tool and only exists where
Blazor and the scheduler share a process.

Quartz holds scheduler state only. It must not contain email bodies, recipients, subjects, SMTP credentials, provider message IDs, raw exceptions, tenant secrets, or access tokens; one-off triggers carry pointer-only JSON. The product/operator source of truth remains `EmailDispatchOutbox` and the HAL-gated EmailDispatch admin API, not the scheduler status endpoint.

#### Scheduler schema ownership

Quartz tables are **not** EF Core migrations. The API ships provider-specific,
idempotent DDL as embedded resources and applies it at startup when
`ApplySchemaOnStartup` is true. The scripts only ever create missing tables and
indexes; they contain no `DROP` or `TRUNCATE` statements, so re-running them
cannot lose scheduler state.

### Email Dispatch RabbitMQ Configuration

RabbitMQ Dispatch Mode is optional transport infrastructure over the same PostgreSQL `EmailDispatchOutbox` state machine. The implemented path declares topology, publishes pointer-only dispatch messages with mandatory routing and publisher confirms, consumes with manual ACK after durable drain outcomes, exposes a readiness check, and wires Aspire local development. It does **not** replace the Basic SMTP worker and does not make RabbitMQ required for non-Aspire/basic deployments.

Static RabbitMQ transport settings bind from `EmailDispatchRabbitMq` and are validated at startup with `ValidateOnStart`:

| Key | Default | Description |
|---|---|---|
| `Enabled` | `false` | Enables optional RabbitMQ Dispatch Mode transport checks/publishing. Disabled mode is health-safe and leaves Basic Dispatch Mode independent. |
| `ConnectionStringName` | `messaging` | Named connection string/resource used to resolve RabbitMQ, including Aspire `ConnectionStrings:messaging` or `MESSAGING_URI`. Must not be blank. |
| `ConnectionString` | unset | Optional direct AMQP URI override. Do not log or expose raw values. |
| `ExchangeName` | `explore.email-dispatch` | Durable direct exchange for pointer-only dispatch messages. |
| `DispatchQueueName` | `explore.email-dispatch.dispatch` | Durable dispatch queue bound to the exchange. |
| `DispatchRoutingKey` | `email-dispatch.dispatch` | Routing key used for mandatory pointer publishes. |
| `DeadLetterExchangeName` | `explore.email-dispatch.dlx` | Durable direct DLX for rejected/poison messages in later consumer slices. |
| `DeadLetterQueueName` | `explore.email-dispatch.dead-letter` | Durable DLQ bound to the DLX. |
| `DeadLetterRoutingKey` | `email-dispatch.dead-letter` | DLQ routing key configured on the dispatch queue. |
| `ParkingQueueName` | `explore.email-dispatch.parking` | Durable parking queue for future operator replay/parking tooling. |
| `ParkingRoutingKey` | `email-dispatch.parking` | Parking queue routing key. |
| `ClientProvidedName` | `explore-email-dispatch-rabbitmq` | RabbitMQ client identity for broker/operator diagnostics. |
| `ConsumerId` | `explore-email-dispatch-rabbitmq-consumer` | Stable consumer identity that manual-ack RabbitMQ deliveries record in `EmailDispatchReceipt`. Must not be blank. |
| `PrefetchCount` | `10` | Bounded unacknowledged delivery window for the manual-ack consumer. Must be greater than zero; `0` is not allowed because RabbitMQ treats it as unbounded. |
| `DeadLetterReplayEnabled` | `false` | Enables the optional DLQ replay worker. Keep disabled until operators intentionally want RabbitMQ DLQ redrive/parking. |
| `DeadLetterReplayConsumerId` | `explore-email-dispatch-dlq-replay` | Stable consumer tag for the DLQ replay worker. Must not be blank. |
| `DeadLetterReplayPrefetchCount` | `5` | Bounded unacknowledged delivery window for DLQ replay. Must be greater than zero. |
| `PublishTimeoutSeconds` | `15` | Timeout around topology/publish confirm work. Must be greater than zero. |
| `PublisherPollingIntervalSeconds` | `5` | Delay between pointer-publisher scans for due `EmailDispatchOutbox` rows. Must be greater than zero. |
| `PublisherBatchSize` | `100` | Maximum due rows the pointer publisher reads per pass. Must be greater than zero. |
| `PublisherRetryDelaySeconds` | `30` | Minimum delay before retrying RabbitMQ pointer publish attempts for still-pending rows. Must be greater than zero. |

The RabbitMQ payload is `EmailDispatchPointer`: tenant ID, stable `PublishEventId`, kind, source IDs, and optional event/registration/user IDs only. It intentionally excludes recipient email, subject, plain text body, HTML body, reply-to, provider message IDs, raw provider errors, and SMTP credentials. The pointer publisher records producer metadata on `EmailDispatchOutbox` (`RabbitMqLastPublishedAt`, `RabbitMqLastPublishAttemptAt`, `RabbitMqPublishAttemptCount`, and `RabbitMqLastPublishFailureCategory`) without changing SMTP delivery state. The DLQ replay worker validates pointer metadata against the PostgreSQL row before redriving; unsafe payloads are routed to the parking queue instead of being blindly replayed.

### Idempotency Cleanup Configuration

Write-operation idempotency uses PostgreSQL `idempotency_records` as a short-lived replay cache keyed by `(Idempotency-Key, TenantId)`. Expired records are not eligible for replay reads. The cleanup processor is an API-hosted background service that physically deletes only records whose `ExpiresAt` is older than the configured grace window.

Static cleanup settings bind from `IdempotencyCleanup` and are validated at startup with `ValidateOnStart`:

| Key | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables the hosted cleanup loop. When disabled, the `idempotency-cleanup` readiness check reports `Degraded` intentionally. |
| `DryRun` | `false` | Counts eligible rows and emits metrics/logs without deleting. Use this before enabling destructive cleanup in a new environment. |
| `InitialDelaySeconds` | `30` | Delay before the first cleanup pass after API startup. Must be zero or greater. |
| `PollingIntervalMinutes` | `60` | Delay between cleanup passes. Must be greater than zero. |
| `BatchSize` | `500` | Maximum expired rows counted/deleted per pass. Must be greater than zero. |
| `ExpirationGraceHours` | `24` | Safety buffer after `ExpiresAt` before a row is eligible for physical delete. Must be zero or greater. |

Cleanup is instance/system-scoped because idempotency rows are ephemeral replay-cache entries, not tenant-owned source-of-truth or compliance evidence. Logs, health data, and metrics expose only bounded settings/counts; they must not include raw idempotency keys, request paths, response bodies, or tenant IDs.

### AI Retention Cleanup Configuration

AI assistant history retention is tenant-owned source-of-truth data, so the hosted cleanup worker iterates active tenants, binds tenant context per tenant, resolves each tenant's `ai_assistant.retention_days`, and then invokes the tenant-filtered redaction cleanup path. The worker does not bypass tenant filters and must not log prompts, provider responses, tool payloads, selected reference content, tenant IDs, or provider secrets.

Static scheduler settings bind from `AiRetentionCleanup` and are validated at startup with `ValidateOnStart`:

| Key | Default | Description |
|---|---:|---|
| `Enabled` | `true` | Enables the API-hosted AI retention cleanup loop. When disabled, the `ai-retention-cleanup` readiness check reports `Degraded` intentionally. |
| `DryRun` | `false` | Counts eligible expired AI conversations across active tenants without redacting or soft-deleting rows. Use this before destructive cleanup in a new environment. |
| `InitialDelaySeconds` | `30` | Delay before the first cleanup pass after API startup. Must be zero or greater. |
| `PollingIntervalMinutes` | `60` | Delay between cleanup passes. Must be greater than zero. |
| `MaxTenantsPerPass` | `100` | Maximum active tenant lookups processed per cleanup pass. Must be greater than zero. |

The per-tenant retention window still comes from the governance setting `ai_assistant.retention_days` (default 30 days). Static `AiRetentionCleanup:*` settings only control the scheduler posture, dry-run mode, and pass bounds.

## Secret Provider Configuration

`Explore.Secrets` binds provider config from `SecretProvider`:

- `SecretProvider:Provider` (required: `Environment`, `Infisical`, or `UserSecrets`; the latter is Development/Testing only)
- `SecretProvider:FailFast`
- `SecretProvider:Infisical:*` (project/client credentials, paths, environment)

Refresh behavior binds from `SecretRefresh` and runs via hosted `SecretRefreshService`.

## Setup Secret And Managed Provisioning Bootstrap

`SetupSecretProvider` reads setup and managed-provisioning bootstrap keys directly from configuration at API startup.

| Key | Default | Purpose |
|---|---|---|
| `SETUP_SECRET` | generated file fallback | Env-only startup secret for interactive first-run onboarding. A non-empty value is always authoritative, removes any generated file, and remains valid until onboarding completes. When empty, the API creates one random secret at `SETUP_SECRET_FILE`, reuses it while that file survives, and deletes it after onboarding completes. The secret value is never logged. |
| `SETUP_SECRET_FILE` | platform temp: `/tmp/islamu-event/setup-secret`; split Compose: `/app/bootstrap/setup-secret`; standalone: `/app/data/setup-secret` | Server-only generated-secret path. The file is created atomically with owner read/write permissions (`0600`) for explicit host-administrator retrieval. Split Compose backs `/app/bootstrap` with `setup_data`; standalone uses its existing data volume. An unmounted temp path survives process/container restarts but not container replacement. |
| `SETUP_SECRET_REQUIRED` | `true` | Controls whether interactive setup endpoints can validate a setup secret. `false` is effective only when trusted managed provisioning is explicitly configured; otherwise the provider fails closed and still requires a setup secret. |
| `PROVISIONING_TRUSTED` | `false` | Must be `true` before managed-provider provisioning can disable interactive setup-secret validation. |
| `PROVISIONING_MODE` | unset | Trusted values are managed-provider modes such as `managed-provider`, `managed_provider`, `managed-hosting`, or `managed`. Other values do not disable setup-secret validation. |
| `MANAGED_CLIENT_EXTERNAL_PROVIDER` | unset | Stable external provider key for the managed provisioning operator, for example an ERP or hosting-provider key. Required when `SETUP_SECRET_REQUIRED=false`. |
| `PHYSICAL_TENANCY_MODE` | unset | Deployment posture such as shared database or dedicated deployment. Required when `SETUP_SECRET_REQUIRED=false` so the operator has declared the physical tenancy model. |
| `CONTROL_PLANE_MANAGED_MODE` | `false` | Enables managed-control-plane bootstrap. When true, API maps `ManagedControlPlane:*` settings and runs managed registration/provisioning workers under bounded policy. |
| `CONTROL_PLANE_URL` | unset | Optional only when managed mode is enabled. URL for the control plane API (must be absolute HTTPS or HTTP loopback). |
| `CONTROL_PLANE_INSTANCE_ID` | unset | Required when managed mode is enabled; parsed as deployment `Guid`. |
| `CONTROL_PLANE_REGISTRATION_TOKEN` | unset | Directional bootstrap credential material for the initial registration exchange. |
| `CONTROL_PLANE_MAXIMUM_TENANT_COUNT` | unset | Upper capacity bound for managed tenant allocation. |
| `CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL` | unset | Optional absolute HTTPS (or loopback) URL used for directed tenant-administrator sign-in experiences. |

Important safety behavior:

- Omitted `SETUP_SECRET_REQUIRED` defaults to `true`.
- `SETUP_SECRET_REQUIRED=false` without all trusted managed-provisioning keys is ignored and the API still requires a setup secret.
- `SETUP_SECRET_REQUIRED=false` with trusted managed provisioning does **not** make setup-secret-protected endpoints public. `ValidateSecret` returns false and those endpoints reject anonymous/no-secret calls; managed provider automation must use the authorized provisioning endpoint instead.
- Raw setup secrets are never written to logs or terminal output.
- Startup logs only an operator retrieval command such as `docker cp <container-name>:<setup-secret-path> ./setup-secret`; they never contain the value. The file is deleted when onboarding locks, and a non-empty `SETUP_SECRET` removes and overrides any stale generated file.
- A read-only filesystem with no writable mount must supply `SETUP_SECRET`; startup fails closed with explicit guidance instead of printing a credential.
- Rolling or multi-replica API deployments must supply one shared explicit `SETUP_SECRET` from the platform secret manager. Compose pins the API to one replica; generated-file mode is for a single API instance.
- Losing an uncompleted generated secret is an availability event: replacing an ephemeral container creates a new secret and invalidates the old one. Completed onboarding remains authoritative in the database, so no replacement secret is generated afterward.
- The API does not expire setup authority relative to process startup. The BFF instead issues a 30-minute rolling setup session; successful status and synchronization calls refresh it, while 30 minutes without setup activity requires the operator to enter `SETUP_SECRET` again.

`/setup` is a separate pre-authentication operator gateway. Browser-provided privileged headers are always removed; the BFF and API accept setup authority only from their trusted server-owned setup-secret sources. Access tokens, setup secrets, provider administrator credentials, and raw provider responses must not enter browser storage, browser-facing DTOs, logs, traces, screenshots, or support artifacts.

After authentication, onboarding is presented as one server-derived task overview. It composes the existing onboarding status, provider verification/sync, and preflight endpoints rather than persisting a second client-side workflow model. A configured authentication-provider task keeps a HAL-authorized management action: before launch it opens `/onboarding/auth-provider`; after launch it opens `/settings/instance?section=auth-providers`, where an instance administrator can run Keycloak doctor, preview synchronization, apply additive repairs, and rotate the managed client secret. Configured credential metadata must not hide that management affordance; missing or errored authoritative provider state fails closed and must not be converted into client-inferred readiness. `Deployment:Mode` and `Bff:AdminHosts` remain operator/deployment configuration and are display-only in onboarding; the UI must not offer them as setup choices.

## API Compatibility Mapping (Infisical -> .NET keys)

`Explore.API.Extensions.ConfigurationExtensions` maps compatibility names into canonical .NET keys. Most mappings use `TrySet`, so existing canonical keys are not overwritten; `CERBOS_GRPC_ENDPOINT` explicitly assigns `Cerbos:GrpcEndpoint` when present.

- `DEPLOYMENT_MODE` (Infisical `/api`) -> `Deployment:Mode` (`single_tenant`/`multi_tenant` normalized to `SingleTenant`/`MultiTenant`)
- `MCP_ENABLED`, `MCP_ENDPOINT_PATH`, `MCP_STATELESS`, `MCP_ENABLE_LEGACY_SSE` (Infisical `/api` or `/mcp`) -> `Mcp:Enabled`, `Mcp:EndpointPath`, `Mcp:Stateless`, `Mcp:EnableLegacySse`; when absent, defaults are `true`, `/mcp`, `true`, and `true`; bare endpoint paths such as `mcp` normalize to `/mcp`, and `MCP_ENABLE_LEGACY_SSE` is a startup ceiling only
- `KEYCLOAK_ENDPOINT` + `KEYCLOAK_REALM` (Infisical `/keycloak`) -> `Keycloak:Authority`, `Keycloak:MetadataAddress`
- Keycloak mapper defaults -> `Keycloak:Audience=islamu-event-api`, `Keycloak:RequireHttpsMetadata=true`
- `CONTROL_PLANE_MANAGED_MODE` -> `ManagedControlPlane:Enabled`
- `CONTROL_PLANE_URL` -> `ManagedControlPlane:ControlPlaneUrl`
- `CONTROL_PLANE_INSTANCE_ID` -> `ManagedControlPlane:ManagedInstanceId`
- `CONTROL_PLANE_REGISTRATION_TOKEN` -> `ManagedControlPlane:RegistrationToken`
- `CONTROL_PLANE_MAXIMUM_TENANT_COUNT` -> `ManagedControlPlane:MaximumTenantCount`
- `CONTROL_PLANE_TENANT_ADMINISTRATOR_SIGN_IN_URL` -> `ManagedControlPlane:TenantAdministratorSignInUrl`
- `AUTHORIZATION_PROVIDER` (Infisical `/api` or `/cerbos`) -> `Authorization:Provider` (blank, `local`, or `cerbos`)
- `CERBOS_GRPC_ENDPOINT` (Infisical `/cerbos`) -> `Cerbos:GrpcEndpoint`
- `CERBOS_HTTP_ENDPOINT` (Infisical `/cerbos`) -> `Cerbos:HttpEndpoint` and the instance Admin API endpoint list
- `CERBOS_ADMIN_USERNAME` and `CERBOS_ADMIN_PASSWORD` are canonical Environment-authority names; they are never remapped into `IConfiguration`
- `CERBOS_USE_POLICY_SCOPE` (Infisical `/cerbos`) -> `Cerbos:UsePolicyScope` (`true`/`false`, also accepts `1`/`0`, `yes`/`no`, `on`/`off`)
- S3 credentials use only `STORAGE_S3_ACCESS_KEY_ID` and
  `STORAGE_S3_SECRET_ACCESS_KEY` under Environment authority. Legacy
  `ISLAMU_EVENT_*` and `S3Settings:*` aliases are removed.

Keycloak base URL: `KEYCLOAK_ENDPOINT` (Infisical `/keycloak`). No hardcoded fallback — if not set, Keycloak mapping is skipped.

Storage naming rules:

- local filesystem runtime settings use `Storage:Local:*`;
- local filesystem Compose/environment overrides use `Storage__Local__*`;
- optional S3-compatible governance uses `s3.*` settings;
- optional S3-compatible credentials use canonical `STORAGE_S3_ACCESS_KEY_ID` and
  `STORAGE_S3_SECRET_ACCESS_KEY` only when Environment authority is selected;
- reconciliation worker settings use `StorageReconciliation:*` or `StorageReconciliation__*`;
- Infisical/domain secret definitions use the `STORAGE_S3_*` key family under storage paths;
- do not expose or persist deployment-managed local filesystem paths through tenant/admin setting keys.

## Blazor Server Compatibility Mapping

`Explore.Blazor.Extensions.ConfigurationExtensions` maps Keycloak, Google, and API base URL keys for the BFF host.

API base URL: `API_ENDPOINT` (Infisical `/blazor`) maps into `ExploreApi:BaseUrl`. Runtime YARP resolution checks `ExploreApi:BaseUrl`, then Aspire service discovery keys `services__explore-api__https__0` and `services__explore-api__http__0`, then falls back to `https://localhost:7039/` when no value is configured at all.

Important behavior:

- `KEYCLOAK_CLIENT_ID` maps to `Keycloak:ClientId`; when a Keycloak authority is resolved and no client id is provided, the BFF defaults to `islamu-event-blazor`.
- `Keycloak:ClientSecret` is explicitly overridden when `KEYCLOAK_BLAZOR_CLIENT_SECRET` (Infisical) is present.
- `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET` map to `Google:ClientId` and `Google:ClientSecret`.

Compose-managed Keycloak adds one bootstrap-specific rule: `docker/keycloak/keycloak-init.sh` writes `KEYCLOAK_BLAZOR_CLIENT_SECRET` into the imported `islamu-event-blazor` client before API/Blazor startup is allowed to complete. Neither checked-in realm export contains a production client secret. Compose fails closed when the BFF secret is absent; local Aspire instead creates a persisted secret parameter when no deployment value is configured and injects it consistently into Keycloak, API, and Blazor. `KEYCLOAK_API_CLIENT_SECRET` is a legacy/future optional sync input only; the checked-in realm export treats `islamu-event-api` as a bearer-only audience target with no static client secret, and the current API bearer-token validation path does not consume an API client secret. The Keycloak admin username/password are used only by the one-shot Compose init job and must not be stored as runtime application settings.

The managed-realm synchronizer replaces the BFF client callback allow-list with exact login, logout, and web-origin values. Compose uses its exact localhost defaults; local Aspire derives the same localhost/admin-localhost URI set from the allocated Blazor HTTP/HTTPS ports, including isolated dynamic ports. Nonblank `KEYCLOAK_BLAZOR_REDIRECT_URIS`, `KEYCLOAK_BLAZOR_WEB_ORIGINS`, and `KEYCLOAK_BLAZOR_LOGOUT_REDIRECT_URIS` values override that Aspire derivation. Reverse-proxied deployments must supply exact public values in those formats. Wildcards and the `+` web-origin shortcut are not repository defaults.
- `Keycloak:RequireHttpsMetadata` is set to `true` when Keycloak input is mapped.

External-Keycloak onboarding uses a different secret boundary. The setup UI can send a one-time Keycloak bootstrap username/password to `POST /api/InstanceOnboarding/auth-provider-configuration/keycloak-bootstrap` through the BFF. That credential is request-scoped input for the Infrastructure Keycloak Admin API adapter; it is not a configuration key, not a governance setting, not a secret-provider key, and not persisted by ISLAMU. Successful bootstrap persists only the normal runtime Keycloak auth-provider configuration: authority, Blazor client ID, and Blazor client secret.

External bootstrap URL safety is enforced before network calls. Keycloak base URLs must be absolute HTTP/HTTPS URLs without embedded user info, query string, or fragment. Literal localhost, loopback, link-local, unspecified, and multicast IP hosts are rejected by the Infrastructure adapter; self-hosted/internal DNS hostnames remain allowed so operators can use private Keycloak service names intentionally.

## Embedded Control Plane Configuration

The control-plane shell uses the existing `Explore.Blazor` BFF authentication, API service discovery, cookie, and proxy configuration. `Bff:AdminHosts` selects dedicated hostnames that render the embedded operator shell; `Bff:AdminHostAllowedIpRanges` can further restrict those hosts. There is no separate control-plane client secret, Infisical path, API endpoint, or application project.

The embedded control-plane shell uses the same server-side BFF session as the public host and never serializes access tokens, refresh tokens, client secrets, or instance-admin authority claims into browser-visible authentication state. Browser-visible control-plane actions must still come from API/HAL/status endpoints, not local role inspection.

## Governance Settings (Database)

Governance keys are centralized in `Explore.Domain.Constants.GovernanceSettingKeys`.

Major groups:

- `deployment.*`
- `tenants.*`
- `routing.*` and `routing.render_policy.*`
- `events.*`
- `organizations.*`
- `modules.*`
- `branding.*`
- `domains.*`
- `email.*`
- `s3.*`
- `authorization.*`
- `support_access.*`
- `cerbos.*`
- `analytics.*`
- `ai_assistant.*`
- `ui_shell.*` and `ui_shell_preferences.*`
- `auth.*`
- `federation.*`
- `localization.*`

Values are stored as JSON-serialized strings in `SystemSetting.Value` and `TenantSetting.Value`.

Sensitive runtime credentials use a separate secret-setting key space. Do not expose actual values in documentation, logs, screenshots, or issue templates.

Platform monetization is application-managed instance data, not an `appsettings`, environment-variable, secret-provider, or hierarchical governance-key family. Instance administrators manage its active versioned fee and contribution records through `GET|PUT /api/instance/settings/platform-monetization`. Fresh records default disabled and zero; tenant settings cannot override them.

| Concern | Governance key family | Secret-bearing key family |
|---|---|---|
| SMTP | `email.*` | `smtp.username`, `smtp.password` |
| Optional S3-compatible storage | `s3.*` | `storage.s3.access_key_id`, `storage.s3.secret_access_key` |
| Authentication | `auth.*` | `auth.keycloak_client_secret`, `auth.google_client_secret` |
| Cerbos admin credentials | `cerbos.*` | Deployment configuration or request-scoped one-time sync input; never persisted by onboarding |
| AI assistant | `ai_assistant.*` | `ai_assistant.api_key` |
| Support access | `support_access.*` | none |

`SecretDefinitionRegistry` recognizes provider folders for `/api`, `/storage`, `/keycloak`, `/cerbos`, `/postgresql`, `/smtp`, `/analytics`, and `/ai`. The `/smtp` folder uses `MAIL_SMTP_HOST`, `MAIL_SMTP_PORT`, `MAIL_SMTP_USERNAME`, `MAIL_SMTP_PASSWORD`, `MAIL_SMTP_FROM_ADDRESS`, and `MAIL_SMTP_FROM_NAME`. Blazor maps Google client values from `/blazor`; do not claim Google is part of the current secret-catalog folder list unless the registry changes.

### External Link Transport Policy

| Key | Scope | Type | Default | Description |
|---|---|---|---|---|
| `security.require_https_external_urls` | Instance only, non-lockable | bool | `true` | Requires tenant navigation and footer external links to use HTTPS. Relative paths remain valid. Set to `false` only for an explicitly trusted HTTP-only private network. |

The hosted ISLAMU deployment keeps this setting enabled. Disabling it is an operator decision for private-network self-hosting and does not weaken the application's own HTTPS redirection or HSTS configuration.

### Workspace Shell Settings

Workspace-shell policy uses lockable Instance/Tenant settings. Personal layout state uses non-lockable User-only preferences and must never be written by tenant administrators.

The authenticated shell resolves navigation defaults, user-override policy, and the organizer default workspace through the existing settings cascade. The public-experience shell resolves anonymous rail visibility through the same cascade. User preference persistence is registered here and implemented separately in Phase 7.

| Key | Scope | Type | Default | Allowed values | Description |
|---|---|---|---|---|---|
| `ui_shell.rail_public_visibility` | Instance → Tenant, lockable | string | `"AuthenticatedOnly"` | `AuthenticatedOnly`, `Always` | Controls whether anonymous visitors see the workspace rail. |
| `ui_shell.default_nav_mode.events` | Instance → Tenant, lockable | string | `"Docked"` | `Docked`, `Collapsed` | Default Events workspace navigation mode. |
| `ui_shell.default_nav_mode.studio` | Instance → Tenant, lockable | string | `"Docked"` | `Docked`, `Collapsed` | Default Studio workspace navigation mode. |
| `ui_shell.default_nav_mode.ai` | Instance → Tenant, lockable | string | `"Docked"` | `Docked`, `Collapsed` | Default AI workspace navigation mode. |
| `ui_shell.allow_user_nav_override` | Instance → Tenant, lockable | bool | `true` | — | Allows users to override tenant navigation-mode defaults. |
| `ui_shell.organizer_default_workspace` | Instance → Tenant, lockable | string | `"Events"` | `Events`, `Studio` | Default workspace for authenticated organizers. |
| `ui_shell_preferences.layout.v1` | User only, non-lockable | JSON | `null` | — | Versioned workspace-shell layout snapshot. |
| `ui_shell_preferences.last_workspace` | User only, non-lockable | string | `""` | — | Last valid workspace selected by the user. |
| `ui_shell_preferences.last_actor` | User only, non-lockable | string | `""` | — | Last managed actor selected by the user. |
| `ui.settings.last_scope.v1` | User only, non-lockable | string | `""` | — | Last re-authorized administrative Settings scope used by the dedicated hub/selector. |

The default `null` layout means no server snapshot exists yet, which allows the client to promote a tenant-discriminated anonymous snapshot on first authenticated use. Viewport-projected dock state is never persisted.

## AI Assistant Settings (Governance)

AI assistant configuration is governed through `ai_assistant.*` keys. The Application layer resolves these into `AiAssistantSettingGroup`; provider SDKs and concrete network clients stay behind Infrastructure adapters and must not leak into Domain, Application DTOs, browser responses, logs, or tests.

Canonical keys:

| Key | Type | Default | Description |
|---|---|---|---|
| `ai_assistant.enabled` | bool | `false` | Master enable switch. Disabled remains the safe default until provider health, egress validation, auth, quotas, and retention gates are implemented. |
| `ai_assistant.provider` | string | `"none"` | Tenant/runtime provider intent. Static `AiProvider:*` controls concrete Infrastructure wiring and supports `none`, `openai`, `openai-compatible`, `anthropic`, `anthropic-compatible`, and `azure-openai`; `fake` is registered only in `Development` and `Testing`. Production-like hosts reject and cannot resolve `fake`. Real providers require model configuration, while API keys are optional only for compatible endpoints that do not enforce authentication. |
| `ai_assistant.endpoint_url` | string | `""` | Provider base URL for self-hosted, OpenAI-compatible, Anthropic-compatible, or Azure OpenAI adapters. First-class `openai` and `anthropic` providers use their official API defaults when this is empty. This is deployment/admin-controlled; browser or request payloads must never choose outbound provider hosts. |
| `ai_assistant.api_key` | string | `""` | Sensitive provider credential. Required for first-class OpenAI Responses API and Anthropic Messages API calls; optional for local/self-hosted compatible endpoints that do not require authentication. Treat as write-only/redacted; never expose to Blazor, API responses, logs, screenshots, traces, or issue templates. |
| `ai_assistant.model_id` | string | `""` | Default model ID. Real providers are not considered configured unless a model ID is present; compatible endpoints may leave API key empty when the provider accepts unauthenticated requests. |
| `ai_assistant.allowed_model_ids` | JSON array | `[]` | Optional model picker allow-list. The default model is always included in the effective allow-list; duplicate and blank entries are ignored by availability resolution. |
| `ai_assistant.max_input_tokens` | int | `8000` | Prompt/context budget used before provider calls. Handlers must still enforce bounded context and prompt length. |
| `ai_assistant.max_output_tokens` | int | `1024` | Maximum requested provider completion size. |
| `ai_assistant.temperature` | decimal | `0.2` | Provider sampling temperature. Keep low for structured assistant workflows. |
| `ai_assistant.timeout_seconds` | int | `30` | Provider call timeout budget. Cancellation tokens must still flow through all calls. |
| `ai_assistant.retention_days` | int | `30` | Default persisted conversation retention window. The Phase 8 retention cleanup operation uses this tenant-resolved value to select expired conversations, supports dry-run, redacts prompt/action/reference/failure content, and soft-deletes expired conversation shells. Scheduling/operator automation is tracked separately from the cleanup primitive. |
| `ai_assistant.daily_message_limit` | int | `50` | Per-user daily assistant message limit enforced before provider calls. |
| `ai_assistant.daily_tenant_message_limit` | int | `1000` | Per-tenant daily assistant user-message limit enforced before provider calls. |
| `ai_assistant.concurrent_run_limit` | int | `1` | Per-user concurrent assistant run limit. Existing idempotency replays are allowed before this quota check. |
| `ai_assistant.selected_reference_limit` | int | `8` | Maximum selected references that future reference-aware prompts may pack into one request. |
| `ai_assistant.tool_proposals_enabled` | bool | `false` | Allows provider output to become persisted proposed actions only. Mutating tools still require server validation, HAL affordance checks, user confirmation, idempotency, and audit before execution. |
| `ai_assistant.streaming_enabled` | bool | `false` | Reserved for a future streaming transport. Current AI assistant run progress uses authenticated polling through `GET /api/ai/assistant/conversations/{conversationId}/runs/{runId}`; keep this disabled until streaming transport buffering, cancellation, timeout, authentication, logging safety, and polling fallback are explicitly implemented and verified. |
| `ai_assistant.allow_anonymous_access` | bool | `false` | Legacy/public-availability flag for safe bootstrap surfaces only. Private conversation/history/send/action endpoints must remain authenticated. |

Important notes:

- `AiAssistantSettingGroup.IsConfigured` treats `fake` as configured for deterministic tests, but Infrastructure registers and validates that provider only in `Development` and `Testing`. `openai-compatible` requires `ai_assistant.endpoint_url` and `ai_assistant.model_id`; `ai_assistant.api_key` is optional and sent only when configured.
- Provider output is untrusted data. It may produce structured action candidates, but those candidates must be persisted as proposals and require explicit confirmation before any write command runs.
- Do not log raw prompts, model responses, selected reference content, provider request IDs tied to content, endpoint credentials, or provider exception bodies.
- Provider endpoint URLs are deployment/admin-controlled. Browser payloads and per-request DTOs must never choose outbound provider hosts.
- Assistant send requests are also protected by API rate limiting under `RateLimiting:AiAssistant` and by Application-level per-user daily, per-tenant daily, and per-user concurrent-run quotas. Quota failures return safe ProblemDetails and do not call the provider.
- Tenant delegation/admin editing for the expanded provider/model/limit settings is intentionally separate from defining the keys. Do not assume a key is tenant-admin editable until the tenant policy service and UI explicitly expose it.

## Support Access Settings (Governance)

Admin support access is governed through instance-only `support_access.*` keys. Defaults are fail-closed: support access is disabled, write mode is disabled, ticket/reference capture is required, and each actor is restricted to one active session.

Canonical keys:

| Key | Type | Default | Scope | Description |
|---|---|---|---|---|
| `support_access.enabled` | bool | `false` | Instance | Global kill switch for support-access session validation and new session creation. |
| `support_access.max_read_only_minutes` | int | `30` | Instance | Maximum duration for read-only support-access sessions. |
| `support_access.max_write_minutes` | int | `10` | Instance | Maximum duration for write-capable support-access sessions. |
| `support_access.allow_write_mode` | bool | `false` | Instance | Allows write-capable support-access sessions only when explicit runtime authorization also permits them. |
| `support_access.require_ticket_reference` | bool | `true` | Instance | Requires a bounded ticket/reference value before a session can be started. |
| `support_access.one_active_session_per_actor` | bool | `true` | Instance | Matches the database invariant that allows one active support-access session per actor. |

Support-access sessions are persisted in the application database, actor-bound, target-tenant-bound, mode-bound, time-boxed, and audited. They must not create `TenantUserRoleGrant` rows and must not replace `ICurrentUserService.UserId`; the real actor remains the current user for audit.

The BFF stores only an opaque active-session reference and forwards `X-Support-Access-Session-Id` from server-owned state. Browser-supplied `X-Support-Access-*` headers are ignored. Changing `support_access.enabled` to `false` is the operational kill switch for new starts and runtime validation of forwarded sessions.

## Analytics Settings (Governance)

Analytics configuration is governed entirely through `analytics.*` keys.
The runtime abstraction is optional by design: instance admins can lock a shared provider,
leave settings unlocked so tenants can bring their own provider, or disable analytics entirely.

Canonical keys:

| Key | Type | Default | Description |
|---|---|---|---|
| `analytics.provider` | string | `"none"` | Active provider: `none`, `posthog`, `plausible`, `rybbit`, `rudderstack` |
| `analytics.enabled` | bool | `false` | Master enable switch for analytics emission |
| `analytics.consent_mode` | string | `"pseudonymous"` | Privacy mode: `anonymous`, `pseudonymous`, `identified` |
| `analytics.transport_mode` | string | `"direct"` | Browser transport: `direct`, `proxy`, `relay` |
| `analytics.api_key` | string | `""` | Public or write key used by the active provider |
| `analytics.endpoint_url` | string | `""` | Provider base URL, especially important for self-hosted deployments |
| `analytics.personal_api_key` | string | `""` | Sensitive key used for advanced provider features such as PostHog feature flags |

Important notes:

- `analytics.endpoint_url` is the canonical endpoint key. Do not introduce `analytics.endpoint`.
- There is no canonical `analytics.site_id` governance key in the current abstraction.
- The analytics settings follow the standard settings cascade: system setting -> tenant override -> system default.
- Sensitive keys should still be treated carefully in UI and operational workflows even when stored as governance values.
- `analytics.transport_mode=relay` is the only mode that does not require a browser-exposed `analytics.api_key`; the browser posts first-party events to `/api/a/t` and the server uses the resolved provider settings.
- `analytics.transport_mode=proxy` still uses the provider script/client in the browser, but the script host and ingest host should usually point at a first-party reverse-proxy path through `analytics.endpoint_url`.
- `analytics.consent_mode=identified` only enables identify semantics for providers that explicitly support them today (`posthog`, `rudderstack`).

Cookie consent and privacy governance keys:

| Key | Type | Default | Description |
|---|---|---|---|
| `analytics.global_disable_client_tracking` | bool | `false` | Emergency kill switch — disables all **browser-side** analytics immediately. Server-side relay endpoints and server analytics continue normally. Scope: browser SDK initialization only. |
| `analytics.cookie_consent_enabled` | bool | `false` | Whether the cookie consent banner is shown to end users |
| `analytics.decline_behavior` | enum | `"cookieless"` | What happens when a user declines consent: `disable` (no analytics) or `cookieless` (privacy-preserving analytics) |
| `analytics.consent_cookie_lifetime_days` | int | `180` | How long the consent preference cookie persists (ICO recommends 6 months) |
| `analytics.posthog_cookieless_mode` | enum | `"on_reject"` | PostHog cookieless mode: `off`, `always` (never stores on device), `on_reject` (cookieless after decline) |
| `analytics.posthog_person_profiles` | enum | `"identified_only"` | PostHog person profile creation: `always`, `identified_only`, `never` |
| `analytics.posthog_session_replay` | bool | `false` | PostHog session recording (non-essential, requires consent) |
| `analytics.posthog_autocapture` | bool | `false` | PostHog autocapture of clicks/inputs (non-essential) |
| `analytics.posthog_heatmaps` | bool | `false` | PostHog heatmap data collection (non-essential) |
| `analytics.posthog_toolbar` | bool | `false` | PostHog toolbar for on-page debugging |

Storage-mode-driven consent rules:

- The cookie banner requirement is **not** determined by provider name alone. It is determined by whether the provider's configured runtime mode stores or accesses non-essential data on the user's device.
- `plausible` and `rybbit`: cookieless by design, no banner required by default.
- `posthog` with `cookieless_mode=always`: no banner required (no device storage).
- `posthog` with `cookieless_mode=on_reject`: banner required; decline switches to cookieless analytics instead of total silence.
- `posthog` with `cookieless_mode=off` and any non-essential feature enabled: banner required.
- `rudderstack`: treated as "full consent required" for v1.
- The computed storage profile (`Cookieless`, `ConsentManaged`, `FullConsent`) drives all runtime behavior through `IAnalyticsRuntimeProfileResolver`.

Consent cookie design:

- Cookie name is tenant-scoped: `explore_cc_{stableShortKey}` where the stable key is derived from the first 8 hex characters of the tenant's immutable GUID (not the mutable subdomain slug). This prevents cookie orphaning when a tenant renames their subdomain.
- Cookie value is minimal: `accepted` or `declined` only. No timestamps, user IDs, or tracking data.
- Cookie scope: per effective public host/tenant experience. `SameSite=Lax`, `Secure`, `path=/`. Consent is not shared across subdomains or tenants.
- The consent cookie itself is classified as strictly necessary (remembering the user's choice).

Post-onboarding management note:

- Instance admins can update analytics governance values through `PUT /api/InstanceOnboarding/analytics-governance`.
- Instance admins can update authentication provider governance values through `PATCH /api/instance/settings/auth-provider`.
- Instance admins can update the active authorization provider through `PATCH /api/instance/settings/authz-provider`.
- Setup and instance-admin policy sync endpoints accept an optional complete one-time Admin API username/password pair. An empty request selects deployment credentials; partial pairs are rejected.
- Secret values (`keycloak`/`google` client secrets) continue to use secret-setting storage, not plain governance values.

## Settings Cascade Rules

`HierarchicalSettingsResolver` resolves settings through the current scope hierarchy:

1. instance/system setting,
2. tenant setting when a tenant scope is present and the instance setting is not locked,
3. organization setting when an organization scope is present and upstream settings allow delegation,
4. group setting when a group scope is present and upstream settings allow delegation,
5. user setting when a user scope is present and upstream settings allow delegation,
6. default setting definition value.

Cache behavior uses hierarchical cache keys such as `HierSettings:System` and scope-specific keys for tenant, organization, group, and user settings. The resolver honors lock flags so a higher-scope locked value prevents lower-scope overrides.

For local storage, deployment-managed `Storage:Local:*` is the root authority.
`S3ConfigResolver` and `SmtpConfigResolver` read non-secret policy through the
hierarchical settings resolver and read private credentials only through
`ISecretResolver`. Authority failures fail the owning capability closed.

## Deployment Mode Configuration

Static deployment config is bound from `Deployment` section (`DeploymentSettings`):

- `Mode`: `SingleTenant` or `MultiTenant` (default `SingleTenant`)
- `DefaultTenantId`
- `HidePlatformAdminInSingleTenant` (default `true`)
- `DefaultTenantSubdomain`

First-run onboarding mode is controlled only by API configuration. Set `DEPLOYMENT_MODE=multi_tenant` in the Infisical `/api` folder before setup for a multi-tenant deployment. If `DEPLOYMENT_MODE` is absent, onboarding is single-tenant only. Invalid deployment-mode values fail safely to single-tenant setup.

Deployment mode and dedicated BFF admin hosts are read-only operator configuration in the onboarding UI, not onboarding choices. Normal admin UI does not switch deployment mode after launch. Choose multi-tenant mode before first launch; otherwise the convention-first path launches a single-tenant site and provisions the configured default-tenant state.

## Reverse Proxy Trust Configuration

`Explore.API` and the Split `Explore.Blazor` BFF bind trusted forwarded-header settings from `ForwardedHeadersTrust`:

- `ForwardLimit` (default `1`, valid range `1`-`10`)
- `TrustLoopbackProxy` (checked-in API/BFF, Compose, and Aspire default `true` for loopback only)
- `KnownProxies` (exact IPv4/IPv6 address list)
- `KnownNetworks` (bounded IPv4/IPv6 CIDR list)

The combined proxy/network list is capped at 32 entries. Startup fails for malformed values, wildcard addresses, `/0` trust-all networks, or an out-of-range forward limit. Do not trust a whole Docker bridge range by default; configure the exact ingress proxy address, or the narrowest operator-owned CIDR when that address is not stable.

| Deployment input | .NET configuration key | Default (when omitted) | Purpose |
|---|---|---|---|
| `BFF_FORWARDED_HEADERS_FORWARD_LIMIT` | `ForwardedHeadersTrust:ForwardLimit` | `1` | Max trusted upstream proxy hops |
| `BFF_FORWARDED_HEADERS_TRUST_LOOPBACK` | `ForwardedHeadersTrust:TrustLoopbackProxy` | `true` | Trust localhost/loopback ingress |
| `BFF_FORWARDED_HEADERS_KNOWN_PROXY` | `ForwardedHeadersTrust:KnownProxies:0` | `127.0.0.1` | Single upstream proxy IP |
| `BFF_FORWARDED_HEADERS_KNOWN_NETWORK` | `ForwardedHeadersTrust:KnownNetworks:0` | `::1/128` | Upstream proxy CIDR subnet |
| `API_FORWARDED_HEADERS_FORWARD_LIMIT` | `ForwardedHeadersTrust:ForwardLimit` | `1` | API max trusted upstream proxy hops |
| `API_FORWARDED_HEADERS_TRUST_LOOPBACK` | `ForwardedHeadersTrust:TrustLoopbackProxy` | `true` | API loopback ingress trust |
| `API_FORWARDED_HEADERS_KNOWN_PROXY` | `ForwardedHeadersTrust:KnownProxies:0` | `127.0.0.1` | API upstream proxy IP |
| `API_FORWARDED_HEADERS_KNOWN_NETWORK` | `ForwardedHeadersTrust:KnownNetworks:0` | `::1/128` | API upstream proxy CIDR |

When absent from `.env`, Compose and Aspire use safe loopback defaults (`ForwardLimit=1`, `TrustLoopbackProxy=true`, `KnownProxies=["127.0.0.1"]`, `KnownNetworks=["::1/128"]`), allowing single-server and localhost reverse-proxy setups (e.g. Nginx, Caddy, Traefik) to work out of the box with zero configuration. Operators deploying behind external VPC proxies, ALBs, or Kubernetes ingress subnets can override these variables as needed. Additional entries use normal numbered .NET keys such as `ForwardedHeadersTrust__KnownProxies__1`.

Important behavior:

- if no trusted proxy boundary is configured, forwarded host/IP processing is disabled in the API host;
- the BFF accepts only `X-Forwarded-For` and `X-Forwarded-Proto` from configured ingress and never accepts `X-Forwarded-Host`;
- host-derived tenant resolution and proxy-aware rate limiting rely on normalized request values after trusted forwarded-header processing, not on raw `X-Forwarded-*` headers.

Runtime nuance:

- Before onboarding completes, tenant resolution uses a single-tenant fallback so setup endpoints remain reachable.
- Onboarding persists the configured API deployment mode into the database.
- After onboarding, deployment mode is operator-controlled. Runtime admin switching is disabled in the normal governance UI; change mode only through an explicit operator migration path.

## Localization / TMS Settings (Governance)

Keys in `GovernanceSettingKeys.Localization`:

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `localization.default_language` | string | `"en"` | Default language code (ISO 639-1) |
| `localization.tms_provider` | int | `0` | TMS provider: 0=None (offline), 1=Tolgee, 2=Weblate |
| `localization.tms_api_url` | string | `null` | Base URL for the TMS REST API |
| `localization.tms_project_id` | string | `null` | TMS project identifier |
| `localization.tms_component` | string | `null` | Weblate component slug (Weblate only) |

Current localization/TMS settings are governance settings. Do not document TMS API keys or tokens as `SecretProvider`-backed until secret definitions or resolver support exist in source.

See [LOCALIZATION.md](LOCALIZATION.md) for full architecture.

## Address Geocoding

Address geocoding is startup configuration because it controls an outbound
network boundary. It is not a tenant setting and cannot be enabled by a browser
or request payload.

| Environment key | Default | Purpose |
|---|---|---|
| `GEOCODING_PROVIDER` | `None` | `None` disables outbound geocoding; `Photon` enables the optional adapter |
| `GEOCODING_ENDPOINT` | empty | Explicit operator-owned or separately contracted HTTPS Photon base URL |
| `GEOCODING_LANGUAGE` | `en` | Bounded result language tag |
| `GEOCODING_COUNTRY_CODES` | empty | Comma-separated ISO two-letter country allowlist |
| `GEOCODING_MAXIMUM_RESULTS` | `10` | Provider result ceiling; maximum `20` |
| `GEOCODING_MAXIMUM_RESPONSE_BYTES` | `65536` | Provider response byte ceiling; maximum `1048576` |
| `GEOCODING_DATASET_VERSION` | empty | Operator-pinned dataset/configuration fingerprint retained with private provenance |
| `GEOCODING_TOTAL_TIMEOUT_MILLISECONDS` | `5000` | Total request and retry budget; maximum `5000` |
| `GEOCODING_MAXIMUM_RETRY_COUNT` | `2` | Transient retry ceiling; maximum `2` |
| `GEOCODING_RETRY_DELAYS_MILLISECONDS` | `200,500` | Comma-separated delays whose count matches the retry ceiling and whose sum fits the total budget |
| `GEOCODING_READINESS_TIMEOUT_MILLISECONDS` | `2000` | Single `/status` readiness request budget; maximum `2000` |
| `GEOCODING_SELECTION_LIFETIME_SECONDS` | `300` | Protected provider-selection lifetime; allowed range `30`–`900` |

`None` is healthy and performs zero provider calls. `Photon` requires an
explicit endpoint, language, country allowlist, and dataset fingerprint.
`https://photon.komoot.io` is a public demonstration service and is rejected in
production; it is never an implicit fallback.

Photon has no API-key setting. Do not put provider records, queries, addresses,
coordinates, selection tokens, endpoint URLs, tenant identifiers, or user
identifiers in logs, metrics, traces, health data, or validation messages.
Operational release hashes, dataset-manifest hashes, capacity evidence, and
restore evidence belong to the operator deployment manifest described in
[SELF_HOSTING.md](SELF_HOSTING.md), not application options or secrets.

The API compatibility projection maps every documented `GEOCODING_*` key to
the canonical `Geocoding:*` options section. Comma-separated country codes and
retry delays become indexed configuration values. Explicit structured
`Geocoding:*` values take precedence. Aspire forwards the same contract to both
split API and standalone topologies; Compose forwards it through the
`geocoding-env` anchor.

## Custom Property Quotas (Governance)

Hard-limit quota definitions for Layer 3 custom properties (Rule 16). Each has a tenant-overridable default and a platform maximum.

| Key | Type | Default | Description |
|---|---|---|---|
| `custom_properties.max_definitions_per_tenant_per_entity_scope` | int | `500` | Max definitions per (Org/Group/Event). Max: 5000. |
| `custom_properties.max_definitions_per_event` | int | `100` | Max runtime definitions per Event. Max: 1000. |
| `custom_properties.max_definitions_per_event_session` | int | `50` | Max runtime definitions per Session. Max: 500. |
| `custom_properties.max_options_per_definition` | int | `200` | Max option rows per definition. Max: 2000. |
| `custom_properties.max_multi_value_rows_per_value` | int | `20` | Max rows for multi-valued property. Max: 200. |
| `custom_properties.projection_rebuild_batch_size` | int | `500` | Batch size for projection worker. Max: 5000. |
| `custom_properties.projection_discovery_enabled` | bool | `false` | Tenant feature flag for projection-backed search/filter. |

## Tenant Delegation & Locking (Governance)

| Key | Type | Default | Description |
|---|---|---|---|
| `governance.lock_tenant_smtp` | bool | `false` | Prevent tenant from overriding instance SMTP |
| `governance.lock_tenant_storage` | bool | `false` | Prevent tenant from overriding instance storage policy |
| `governance.lock_tenant_analytics` | bool | `false` | Prevent tenant from overriding instance analytics |
| `governance.lock_tenant_ai_assistant` | bool | `false` | Prevent tenant from overriding instance AI assistant |

## Event List Visibility (Governance)

| Key | Type | Default | Description |
|---|---|---|---|
| `event_list.browse_mode` | string | `"Standard"` | Default browse experience |
| `event_list.page_size` | int | `20` | Default items per page |
| `event_list.card.show_organizer` | bool | `true` | Show organization in cards |
| `event_list.card.show_price` | bool | `true` | Show price in cards |
| `event_list.card.show_tags` | bool | `true` | Show tags in cards |

## External API Key Defaults

Non-interactive callers use long-lived `{keyId}.{secret}` credentials. Per-key policy defaults are applied at create time by `ExternalApiKeyQuotaDefaults` and `ExternalApiKeyScopeCeiling`.

### Quota Defaults by Owner Type

| Owner Type | Default Period | Default Request Limit | Rationale |
|---|---|---|---|
| `User` (`1`) | `Daily` | `1,000` | Per-user automation, usually tied to a single developer |
| `Organization` (`2`) | `Monthly` | `10,000` | Team-scale automations and integrations |
| `Group` (`3`) | `Monthly` | `5,000` | Smaller scope than an org but shared by multiple members |
| `Tenant` (`4`) | `Monthly` | `50,000` | Tenant-wide admin automation |
| `InstanceAdmin` (`5`) | `None` | unlimited | Platform operator usage, rate-limited only by node-local policies |

All defaults are overridable per key via `PUT /api/ExternalApiKey/{id}`.

### Scope Ceilings by Owner Type

- `User`: `events:read`, `events:write`, `users:read`, `users:write`, `lookups:read`, `registrations:write`, `api-keys:manage`
- `Organization`: User scopes plus `organizations:read`, `organizations:write`
- `Group`: User scopes plus `groups:read`, `groups:write`
- `Tenant`: All of the above plus `admin:tenant`
- `InstanceAdmin`: All scopes including `admin:instance`

Validators (`CreateExternalApiKeyDtoValidator`, `UpdateExternalApiKeyPolicyDtoValidator`) reject requests containing scopes above the owner ceiling.

### Forwarded Headers Interaction

External API keys are often presented by callers behind reverse proxies. The `ForwardedHeadersTrust` settings (see above) determine which proxies are trusted to forward `X-Forwarded-For`/`X-Forwarded-Host`. When an API-key caller comes through a trusted proxy, the rate-limit partition key remains `api-key:{keyId}` only after the key authenticates successfully. Empty, malformed, invalid, revoked, or inactive API-key attempts use the anonymous/IP partition; the forwarded IP is also used for `LastUsedIp` telemetry and logging when trusted.

When `TrustLoopbackProxy=true` (Aspire-style local development), loopback proxies are trusted for forwarded headers but untrusted proxies still have their `X-Forwarded-*` headers dropped before middleware sees them.

## Browser Web Push

Web Push is configured under `WebPush`. The API compatibility loader maps `VAPID_PUBLIC_KEY`, `VAPID_PRIVATE_KEY`, and `VAPID_SUBJECT` from Infisical `/api` or process environment. When all three are present, Web Push is enabled unless `WEB_PUSH_ENABLED=false` explicitly disables it.

Aspire forwards the same optional variable names to the API. When configured, `VAPID_PRIVATE_KEY` is modeled as a secret parameter so its value is not rendered as ordinary resource configuration; absent VAPID values do not create unresolved parameter prompts.

| Environment variable | .NET key | Required when enabled | Exposure |
|---|---|---|---|
| `WEB_PUSH_ENABLED` | `WebPush:Enabled` | optional kill switch | server only |
| `VAPID_SUBJECT` | `WebPush:VapidSubject` | yes | server only; `mailto:` or HTTPS URI |
| `VAPID_PUBLIC_KEY` | `WebPush:VapidPublicKey` | yes | intentionally returned by `/vapid-public-key` and the public configuration endpoint |
| `VAPID_PRIVATE_KEY` | `WebPush:VapidPrivateKey` | yes | secret; never returned to browsers |

Generate one P-256 VAPID key pair with a trusted Web Push tool, store it in deployment secrets, and reuse it across restarts. Changing the pair invalidates the application-server identity expected by existing browser subscriptions; rotate deliberately and expect users to resubscribe. Startup validation rejects malformed subjects and key lengths before the dispatch worker starts.

Delivery policy is server-owned. Account-security refreshes retain for 5 minutes at high urgency; trust-safety for 1 hour at high urgency; registration/event updates for 6 hours at normal urgency; billing/legal and organization/group/product updates for 24 hours at normal or low urgency; marketing for 6 hours at very-low urgency. Every message sets a category `Topic` so the push service can coalesce pending refreshes, and retries stop at the TTL boundary.

## Paid-Event Configuration Boundary

`Payments:Stripe:Mode` accepts only `Test` or `Live` and defaults to `Test`; the platform secret must use the matching `sk_test_` or `sk_live_` prefix, and Stripe account/webhook livemode evidence must match before readiness is trusted. Task 16.5 also binds non-secret commerce identity from `Payments:OrganizerDirect`: `ProviderCode` and `ConnectPlatformId`. Both must be configured for paid publication and hosted onboarding; neither belongs in browser DTOs.

| Deployment input | .NET key / secret binding | Default | Purpose |
|---|---|---|---|
| `PublicBaseUrl` | `PublicBaseUrl` | blank | Canonical public HTTPS base URL used for Checkout return URLs |
| `Payments__Stripe__Mode` | `Payments:Stripe:Mode` | `Test` | Isolates Test and Live evidence and key prefixes |
| `Payments__Stripe__AllowedCheckoutHosts__0` | `Payments:Stripe:AllowedCheckoutHosts:0` | `checkout.stripe.com` | Built-in HTTPS Checkout destination host; no wildcard syntax |
| `Payments__OrganizerDirect__ProviderCode` | `Payments:OrganizerDirect:ProviderCode` | blank | Non-secret provider identity required for paid publication |
| `Payments__OrganizerDirect__ConnectPlatformId` | `Payments:OrganizerDirect:ConnectPlatformId` | blank | Non-secret self-hoster platform identity |
| `STRIPE_PLATFORM_SECRET_KEY` | `payments.stripe.platform_secret_key` | blank | Instance/server-only provider credential |
| `STRIPE_WEBHOOK_SECRET` | `payments.stripe.webhook_secret` | blank | Instance/server-only Connect endpoint signing secret |
| `RateLimiting__RegistrationPaymentCheckoutIssue__PermitLimit` | `RateLimiting:RegistrationPaymentCheckoutIssue:PermitLimit` | `10` | Node-local checkout-ticket issue permits, clamped to `1..100` |
| `RateLimiting__RegistrationPaymentCheckoutIssue__WindowSeconds` | `RateLimiting:RegistrationPaymentCheckoutIssue:WindowSeconds` | `60` | Node-local fixed window, clamped to `1..3600` seconds |

Checkout tickets are stateless in both Split `Explore.Blazor` and combined `Event.Standalone`. Neither host stores nonces in Redis or in memory: the provider Checkout URL, the audience (scheme, host, path base, tenant, event, order), a digest of the dedicated checkout-session cookie (not the authentication BFF session), and a 5-minute expiry are carried inside one ASP.NET Core Data Protection payload of at most 3,072 ASCII bytes. `ConnectionStrings:cache` is therefore not required for checkout.

Both hosts require a readable Data Protection key ring with application name `islamu-event`. A separate Split BFF uses the native local key store without `ConnectionStrings:cache`, or the Redis key ring when cache is configured; the API's database keys do not cover that separate process. Combined `Event.Standalone` instead registers API services first: without cache, its BFF shares the already-registered `DataProtectionKeyContext` database ring; configuring cache selects Redis later for the whole process. Multi-replica Split BFF deployments must share their ring, and choosing Redis for keys makes Redis availability relevant again. See [PAYMENTS.md](PAYMENTS.md) and [HOSTING_ARCHITECTURE.md](HOSTING_ARCHITECTURE.md).

`OrganizerPaymentReadinessReconciliation` controls the hosted readiness worker. `Enabled` defaults to `true`; `BatchSize` defaults to `25` and is `1..100`; `StaleIntervalMinutes` defaults to `5` and is `1..1440`; `PollingIntervalSeconds` defaults to `60` and is `5..3600`; `InitialDelaySeconds` defaults to `5` and is `0..300`. The worker is not registered in `Testing` and is not registered when disabled.

Instance and tenant paid-event policies are database-governed versioned settings rather than deployment configuration. The instance policy is the ceiling; tenant policy can only narrow allowed organizer kinds and currencies, risk ceilings, review thresholds, and cannot weaken mandatory refund protections or verification/review floors. Direct-charge Checkout and reconciliation are implemented; refunds and disputes remain deferred. Organizers connect only their own eligible actor merchant account; no administrator fallback merchant exists. `ProtectedDelayedPayout` remains absent unless its separate approvals are current.

## Configuration Manifest Startup

Configuration bootstrap uses three non-secret deployment inputs:

| Input | Default | Meaning |
|---|---|---|
| `CONFIGURATION_MANIFEST_MODE` | `Off` | Exact value `Off`, `ValidateOnly`, or `Bootstrap` |
| `CONFIGURATION_MANIFEST_PATH` | `/etc/islamu-event/bootstrap/configuration-manifest.json` in container examples | Absolute path visible to the owning process |
| `CONFIGURATION_MANIFEST_HOST_DIRECTORY` | `./deploy/bootstrap` in Compose/Aspire examples | Host directory projected into the owning process; it is not an application setting |

`Off` performs no file discovery. `ValidateOnly` performs bounded regular-file,
strict UTF-8 JSON, contract, catalog, and complete proposed-state validation
without writes. The first successful `Bootstrap` applies the approved instance
section before tenant sections in one serializable transaction, creates absent
tenants, and records the normalized instance-section digest and bootstrap
generation. A same-section rerun never reapplies historical instance values:
new tenants are validated against fresh Day 2 instance authority, while
existing tenant slugs are skipped wholesale. A changed instance section fails
with `configuration_manifest_instance_already_bootstrapped`; it is never treated
as reconciliation or forced over current administration state.

In split topology, only `Event.MigrationService` receives these inputs; API
replicas never own or reconcile manifests. In Standalone topology, only
`Event.Standalone` owns the sequence. Aspire computes an absolute host path
from `CONFIGURATION_MANIFEST_HOST_DIRECTORY`; Compose sets
`CONFIGURATION_MANIFEST_PATH` to the container path. `.env` selects the mode,
container-visible path, and host mount directory; it never contains manifest
business values and setting a path does not mount or copy a file.

The canonical JSON Schema is
`schemas/configuration-manifest-v1alpha2.schema.json`. Container images also
publish it at
`/app/schemas/configuration-manifest-v1alpha2.schema.json`. Schema validation
is an authoring aid; startup always reruns the complete server-owned validator.

## Paid-event policy manifest boundary

`instance.paid_event_policy` and `tenant.paid_event_policy` are the only
payment-related manifest documents. Both are typed `schemaVersion: 1`
documents. Public input cannot select a stored revision. The first bootstrap
creates or revises the instance policy through the canonical paid-policy
mutation boundary, and each tenant narrowing is bound internally to the exact
effective instance revision selected by the same transaction. Same-section
reruns use the fresh active Day 2 revision instead of replaying historical
instance values. Tenant payloads may contain only:

- payment enablement, eligible-organizer subsets, and the local-verification
  floor;
- ordered currency subsets and a default inside that subset;
- the complete refund-protection floor;
- per-currency amount, count, rolling-window, and high-value-review ceilings;
- first-paid-event and far-future review thresholds.

The server constructs the typed policy versions and runs
`PaidEventPolicyRules.ValidateTenantPolicy` before mutation. Unknown fields,
unsupported currencies, invalid risk pairs, weaker refund protection,
instance-ceiling broadening, stale authority, and direct tenant identifiers
fail closed. The manifest handler then calls the same
`PaidEventPolicyMutationBoundary` used by paid-policy CQRS commands inside the
outer manifest transaction and canonical instance/tenant named locks.

Manifests do **not** own operator identity, official origin or status, provider
profiles or credentials, connected accounts, charge type, buyer acceptance,
sale-control state, provider handoff, reconciliation, disputes, liability,
negative balances, or refund execution. Those remain persisted sovereign
authorities. In particular, there is no manifest stop-sale boolean; use the
audited stop/resume/review workflows for operational governance.

Exports make this boundary machine-readable. `metadata.export.authorityScope`
is `InstanceAndTenants`, `sovereignValuesOmitted` is `true`, and
`sovereignLockedFields` carries the fixed platform-owned field taxonomy.
Overrides emits a paid-policy document only when an active tenant narrowing
exists. Portable emits the active tenant narrowing or the flattened active
instance policy, always pinned to the current instance-policy version. Portable
is therefore an authoring aid for a reviewed target, not authority to broaden a
different instance ceiling and not a backup.

## Reporting Intake Administration

Tenant administrators manage the effective reporting-intake policy in **Tenant
settings > Policies**. The UI treats the API's HAL response as authoritative:
an `edit` relation is required before the control becomes writable, and an
enabled policy can be disabled only when the server also returns
`CanDisable=true`. Instance locks and server-authored safety reasons remain
visible in read-only form. The browser does not infer permission or publication
safety from roles, claims, or local configuration.

Reporting intake and external reporting-provider routing are separate controls.
Disabling tenant intake stops new reports at this tenant; it does not disable an
external provider, delete existing reports, or remove independent correction,
legal, or copyright contact channels published by the tenant. These statements
describe product behavior, not a legal or religious guarantee.

After every accepted update, the UI reloads the effective HAL policy instead of
assuming the requested value became authoritative. Session expiry, access
denial, transport failure, instance locking, or a changed publication-safety
decision therefore fail closed and do not leave a locally calculated writable
state.

### Tenant Administrator Exports

When the reporting-intake HAL resource advertises
`export-configuration-overrides` or `export-configuration-portable`, the same
Policies page presents only those authorized export choices. The browser starts
the download through the authenticated same-origin BFF endpoint; it never calls
the privileged API export endpoint directly and never receives an access token.

- **Overrides** contains tenant-owned non-secret overrides. Inherited and
  default values remain absent so another environment keeps ownership of them.
- **Portable** contains flattened effective non-secret values for transfer.
  Operators must review target defaults, locks, and policy before applying it.

Both views use the server-authored deterministic attachment filename and omit
secret values while retaining non-secret omission metadata. Neither view is a
backup and neither can restore secret material. Managed reconciliation,
field-ownership tracking, drift/conflict policy, deletion semantics, and
takeover rules remain deferred; there is no dormant `Reconcile` runtime mode or
placeholder UI.

## Related

- [API.md](API.md)
- [SECRETS.md](SECRETS.md)
- [SELF_HOSTING.md](SELF_HOSTING.md)
- [MULTI_TENANCY.md](MULTI_TENANCY.md)
- [RENDER_POLICIES.md](RENDER_POLICIES.md)
- [OPERATIONS.md](OPERATIONS.md)
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md)
- [LOCALIZATION.md](LOCALIZATION.md)
