<!-- ABOUTME: Canonical architecture for Local Identity, Keycloak, AT Protocol, BFF sessions, and switching. -->
<!-- ABOUTME: Defines JIT identity convergence, token isolation, persistence ownership, and recovery boundaries. -->

# Authentication

## Invariants

1. Exactly one primary authentication provider is active: Keycloak (`AuthenticationProviderKind.Keycloak = 1`), AT Protocol (`AuthenticationProviderKind.Atproto = 2`), or Local Identity (`AuthenticationProviderKind.Local = 4`).
2. Persisted provider state uses the normalized `authentication_providers` lookup and integer foreign keys. Provider-name strings exist only at HTTP, configuration, and protocol boundaries.
3. AT Protocol is either an independent linked-account capability or the sole primary authority. Primary AT Protocol forces its login axis on and disables other new-login providers.
4. The browser receives only an encrypted, HttpOnly BFF cookie. Raw access tokens remain in server-side authentication properties and circuit state.
5. Local and Keycloak JWTs use isolated bearer handlers. MultiAuth selects a bounded scheme from the unvalidated issuer, then that scheme performs full signature, issuer, audience, and lifetime validation.
6. Switching the primary provider changes only new-login admission. Existing sessions retain their originating validation and refresh scheme until normal expiry.
7. Administrator bootstrap and provider switching match the normalized `(provider_kind, provider_account_key)` identity. Email is never sufficient unless the provider supplied a verified-email claim.
8. `UserExternalLogin` is instance-global identity authority. Tenant participation exists only through `TenantUser`; a provider binding never derives authorization from a tenant ID.
9. Public Local enrollment is closed. Instance email-delivery intent governs unverified Local token issuance, independently of SMTP availability and tenant overrides. It never changes provider-owned verification facts.

## Clean Architecture Flow

Local HTTP requests enter through `LocalAuthController` or the antiforgery-protected BFF endpoints. Controllers create immutable Local authentication commands and dispatch through MediatR:

```text
Browser
  -> POST /bff/auth/local/login
  -> generated Explore API client
  -> POST /api/auth/local/login
  -> LocalLoginCommand
  -> ILocalIdentityAuthService
  -> ASP.NET Core Identity stores
  -> LocalJwtTokenGenerator
  -> SyncUserCommand
  -> HttpOnly BFF cookie
```

Application handlers manually instantiate FluentValidation validators, require Local Identity to be the active primary provider, and synchronize the platform `User` aggregate before returning a token. Synchronization failure withholds the token. The public registration API/BFF routes and their command, DTOs, validator and service method are removed without compatibility aliases.

`LocalIdentityAuthService` owns password hashing, normalized-email uniqueness, UUIDv7 credential identities, failed-access counters, dummy verification for unknown accounts, and lockout. The Domain `User` remains the platform profile/authorization aggregate; `LocalIdentityUser` remains a credential record. Repositories continue returning Domain entities, not authentication DTOs.

## Local Token Contract

`LocalJwtTokenGenerator`:

* resolves `AUTHENTICATION_LOCAL_JWT_KEY` through `ISecretResolver`;
* requires a Base64-decoded HMAC-SHA256 key of at least 256 bits;
* uses fixed issuer and audience values shared with the isolated Local bearer handler;
* issues short-lived tokens with `sub`, `auth_provider=local`, `email_verified`, approved profile claims, and role claims;
* fails closed for missing, malformed, or undersized keys.

After valid password and lockout checks, `LocalIdentityAuthService` reads uncached instance `email.delivery_enabled` through `ISystemSettingRepository` for unverified credentials. Missing intent uses the disabled default; strict JSON `false` permits issuance without changing `EmailConfirmed` or the resulting `email_verified=false` claim. Strict JSON `true` refuses issuance with `email_verification_required`/401, even if SMTP is missing or unhealthy. Malformed intent or a repository failure returns bounded `authentication_failed`/503; cancellation propagates. Tenant settings and transport/secret resolution do not participate in this decision. Already-verified credentials do not need the intent read.

The BFF exposes only the allowlisted verification-required reason to the login UI; other provider error bodies remain hidden. This issuance gate does not itself revoke existing sessions or implement recovery, verification delivery, or administrative enrollment. Those require their dedicated credential-lifecycle boundaries. Keycloak and AT Protocol retain their own authentication and verification authority regardless of Event email-delivery intent.

Local login carries `SuppressIdempotencyResponseStorage` and `PrivateNoStore` metadata. Every request must evaluate current admission policy, even when a client repeats an `Idempotency-Key`; the generic middleware must neither persist a token-bearing response nor replay a prior success. Browser cache headers alone do not disable application-level idempotency storage.

## Persistence Topologies

### Colocated

`ExploreDbContext` applies Identity mappings alongside the application model. Convention-derived names avoid aggregate collision:

* `LocalIdentityUser` -> `local_identity_users`
* `LocalIdentityRole` -> `local_identity_roles`
* `AuthenticationProvider` -> `authentication_providers`

SQLite and MySQL use the provider namespace prefix (`ie_`). `UseSnakeCaseNamingConvention()` and the provider namespace policy own physical names; Identity mappings must not hard-code `ToTable(...)`.

### External

`ExternalIdentityDbContext` contains only Local Identity credential entities. The context has provider-owned migrations in:

* PostgreSQL: `Explore.Persistence/Identity/Migrations`
* SQLite: `Explore.Persistence.Migrations.Sqlite/Migrations/Identity`
* SQL Server: `Explore.Persistence.Migrations.SqlServer/Migrations/Identity`
* MySQL: `Explore.Persistence.Migrations.MySql/Migrations/Identity`

It uses `__EFIdentityMigrationsHistory`, transformed by provider namespace rules where required. Runtime credentials and migrator credentials are bound separately. `Event.MigrationService` conditionally registers and migrates this context before the application migration sequence. `Event.Standalone` creates a short-lived migrator context before resolving its runtime Identity stores, so external topology never gives schema-owner credentials to request handling.

## Primary Provider Resolution

`IAuthenticationProviderDispatcher` resolves the primary provider in this order:

1. explicit deployment configuration;
2. normalized `Authentication:PrimaryProviderId` governance setting;
3. Local Identity default when neither authority is present.

The dispatcher caches a successful result for one minute. Configuration writes explicitly invalidate it. Unsupported IDs, malformed settings, and read failures block login rather than guessing.

The BFF mirrors the same two-axis model:

* provider discovery exposes Local credentials only when Local is primary;
* inactive Keycloak is hidden from new-login challenges;
* configured Keycloak validation metadata remains registered for old-session refresh;
* AT Protocol discovery is independent when Local Identity or Keycloak is primary;
* AT Protocol-only mode exposes only the ready handle-input flow and rejects Local credential entry points.

## AT Protocol-Only JIT Flow

`AUTHENTICATION_PROVIDER=atproto` is a deployment-owned promise that verified
AT Protocol identities may create passwordless platform accounts. The BFF posts
the handle to its antiforgery-protected challenge endpoint, keeps OAuth material
server-side, and sends a DID-bound one-time bootstrap assertion to the API.

After the Infrastructure gateway independently verifies the PDS session,
`BootstrapAtprotoSessionCommandHandler` enters bootstrap-convergence
serialization. `AtprotoJitAccountProvisioningOperation` re-reads the exact
`ProviderAccountKey` and creates one `User`, personal `Actor`, and global
`UserExternalLogin` when absent. Stable UUIDv7 identifiers survive execution
strategy retries, while the unique provider/key index converges concurrent first
logins. Empty email is valid for passwordless accounts; the application email
index is intentionally non-unique because verified provider identity, not email,
is the merge authority.

AT Protocol identity state commits with the account transaction. When the
target tenant exists, the encrypted OAuth refresh session commits in that same
transaction. A fresh interactive setup may not have a tenant yet, so it receives
the short-lived platform session needed to finish setup without writing an
invalid tenant-scoped refresh row. Tenant participation and durable refresh
state converge on the next authenticated login after onboarding creates the
tenant. Token issuance and administrator-cache invalidation happen only after
the identity transaction commits.

OAuth success never grants administrator authority by itself. Interactive root
assignment remains inside setup-secret-authorized onboarding completion.
Configured-administrator mode retains exact DID, generation, and fingerprint
fencing.

## Provider Switching

The administrator UI requires confirmation and target configuration before changing `PrimaryProviderId`. Application validation performs the authoritative lockout-prevention check: an administrator must already have a usable account with the target provider. The write persists the integer lookup ID, enforces three-way primary-provider exclusivity, forces AT Protocol on and Google off in sole mode, and invalidates runtime provider caches.

Never delete the inactive provider's validation metadata merely because primary selection changed. Remove it only when its configuration is intentionally removed or invalidated after its session-continuity obligation ends.

## Deferred Local Identity Capabilities

The initial implementation intentionally reports these operations as unsupported:

* password reset and recovery;
* authenticated password change;
* email-verification delivery and confirmation;
* two-factor authentication;
* passkeys/WebAuthn;
* external social-login attachment.

They require dedicated token-purpose, notification, recovery, replay-protection, audit, and administrator-support designs. They must not be emulated through generic login or profile endpoints.

## Verification

Required focused coverage includes:

* Local contract and command-handler tests;
* real SQLite password hashing, lockout, strict instance admission policy, and JWT verification;
* provider dispatcher cache/fail-closed tests;
* API cross-issuer and cross-signature isolation;
* BFF antiforgery, HttpOnly cookie, and token non-disclosure tests;
* configured-administrator exact-provider matching;
* provider switching and old-session continuity;
* passwordless AT Protocol JIT, duplicate-login convergence, and zero Local credential rows;
* AT Protocol-only BFF provider discovery and focused handle entry;
* direct-database administrator recovery by exact linked DID;
* native Local HTTP login and retired-registration no-write checks, tenant-override isolation, and committed-policy freshness;
* rendered Local login without signup, bounded verification guidance, onboarding, and responsive component tests;
* external Identity migrations for all four providers with no pending model changes.

See [Operations](OPERATIONS.md#local-identity-operations) for commands and operational checks.
