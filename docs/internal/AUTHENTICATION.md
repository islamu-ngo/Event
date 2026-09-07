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

Local login consumes `identifier`, not the retired `email` request member.
Address-shaped identifiers use native email lookup; usernames without `@` use
native username lookup. Both resolve the same exact Local subject and existing
application binding. An absent credential email remains nullable in successful
authentication data and is omitted from JWT email claims, never replaced with
a synthetic address.

Local authentication decisions use closed `LocalAuthOutcome` and `LocalAuthFailure` enums. Failure
factories reject undefined values and cannot carry session authority; successful
results have no failure. Named factory construction identifies every field.
`FailureCode` is an explicit wire mapping, and the API selects HTTP descriptors
using the typed failure rather than string comparisons. Ordinary `ToString()`
formatting of the login request, response and issued-token record is value-free.
This does not sanitize JSON serialization or structured object destructuring:
never log credential objects or token-bearing responses through those paths.

## Local Token Contract

`LocalJwtTokenGenerator`:

* resolves `AUTHENTICATION_LOCAL_JWT_KEY` through `ISecretResolver`;
* requires a Base64-decoded HMAC-SHA256 key of at least 256 bits;
* uses fixed issuer and audience values shared with the isolated Local bearer handler;
* issues short-lived tokens with `sub`, `auth_provider=local`, `local_session_stamp`, `email_verified`, approved profile claims, and role claims;
* fails closed for missing, malformed, or undersized keys.

After valid password and lockout checks, ordinary access-token issuance requires explicit `LocalCredentialState.Ready` metadata. `LocalIdentityCredentialStateStore` reads the exact `Explore.LocalIdentity` / `CredentialState` user-token slot through an untracked query in the same selected Identity database that backs `UserManager`. Versioned metadata carries typed state and nonempty operation/application-user identifiers, never a password or security stamp. Missing, malformed, duplicate-property, unsupported-version/state, or Pending metadata fails closed; database failures return `authentication_failed`, and cancellation propagates. Existing tracked token rows cannot override a newer committed state. ChangeRequired can obtain only the restricted authority below, never an ordinary token.

State admission is bound to the security stamp captured immediately after password
verification. One untracked join reads current metadata and the native user stamp
together, then compares the stamp ordinally before returning either state. A login
that checked a temporary password cannot observe a concurrently replaced Ready
state and silently inherit ordinary authority. Reloading the newer stamp while
retaining the old password proof would defeat this check.

`LocalSessionAuthority` owns the nonempty Local subject, original password-checked
security stamp and claimed verification Boolean. `LocalJwtTokenSubject` requires
that authority; it has no unbound subject overload or duplicate verification fact.
Ready login and ordinary bearer admission share `ILocalIdentityAuthService`
validation. A fresh untracked token/user join must match the stamp and verification
fact, with a nonblank password hash, Ready metadata, a Create-or-Reset/Replaced
receipt and exact active application binding. Verification must match even when
delivery is disabled: a stale verified JWT must not restore revoked verification
through user synchronization, nor silently acquire a newer verification fact.

The isolated Local JWT handler requires HS256 and ordinary `JWT` type. After native
signature/issuer/audience/lifetime validation, `OnTokenValidated` parses authority
from one authenticated Local identity with unique canonical claims and invokes the
fresh check before claims enrichment. Typed `Invalid` and `Unavailable` outcomes
both fail authentication with bounded HTTP 401; cancellation propagates. No legacy
stamp-less token is accepted. These are admission-time reads, not a distributed
snapshot or a lock over subsequent business mutations; handlers still enforce
their own transactional authorization invariants.

For Ready but unverified credentials, the service then reads uncached instance `email.delivery_enabled` through `ISystemSettingRepository`. Missing intent uses the disabled default; strict JSON `false` permits issuance without changing `EmailConfirmed` or the resulting `email_verified=false` claim. Strict JSON `true` refuses issuance with `email_verification_required`/401, even if SMTP is missing or unhealthy. Malformed intent or a repository failure returns bounded `authentication_failed`/503; cancellation propagates. Tenant settings and transport/secret resolution do not participate in this decision. Already-verified credentials do not need the intent read, but still require Ready credential state.

The same fresh instance intent gates unverified ordinary bearer admission. The BFF exposes only the allowlisted verification-required reason to the login UI; other provider error bodies remain hidden. Recovery, verification delivery and administrative enrollment remain unfinished. Keycloak and AT Protocol retain their own authentication and verification authority regardless of Event email-delivery intent.

### Local browser session authority

The native cookie validation callback invokes the host's existing session handler
before missing-token and unexpired-token early returns. `BffAdminClaimsTransformation`
validates Local provider metadata and the original server-held token, then uses the
private `AdminAuthority` client to call `GET /api/user`. The returned user ID must
match the trusted canonical Local subject. The API remains the cryptographic and
current-credential authority; an external token for the same user is not a substitute.
This check precedes synchronization and cached administrator enrichment. Failure
rejects the cookie; request cancellation is checked before destructive cleanup.

`TokenCircuitHandler` captures the original cookie authority at opening and checks
the live principal before later inbound activities. It rechecks subject, session
and provider markers after the awaited API probe. An explicit completed-open flag
distinguishes Blazor's initialization activity from user activity: initialization
itself invokes `OnCircuitOpenedAsync`. Revocation publishes anonymous state through
the native authentication provider and prevents downstream dispatch. The existing
token service latches the scoped revocation and deletes only the original typed
subject/session partition, so a newer login for the same user remains usable.
Missing partition authority never triggers user-wide deletion. Stale handshake
state cannot repopulate a revoked scope. These are activity-time checks, not a
distributed transaction or immediate push invalidation of idle browser tabs.

Local login carries `SuppressIdempotencyResponseStorage` and `PrivateNoStore` metadata. Every request must evaluate current admission policy, even when a client repeats an `Idempotency-Key`; the generic middleware must neither persist a token-bearing response nor replay a prior success. Browser cache headers alone do not disable application-level idempotency storage.

## First-Run Local Enrollment

`POST /api/InstanceOnboarding/complete-local` dispatches the Local onboarding
command through the existing controller. Native SetupSecret authentication,
incomplete setup, current Local-provider admission and native credential
validation precede enrollment. The request carries an operation ID, username,
temporary password, optional email/profile fields and the existing nonsecret
settings contract. It cannot select the subject, grant, provider or session
authority. Generic idempotency body/response storage is suppressed and responses
are private/no-store.

The core commits its selected-store receipt, application linkage, administrator
grants and bootstrap finality before activating ChangeRequired credentials.
Completion returns only `BaseCommandResponse<Guid>`, not a password, challenge
or token. The browser uses the existing login, private replacement and fresh-login
sequence. Authenticated external-provider completion remains a separate action.

Onboarding status is private/no-store. Its nullable `pendingOperationId` is
disclosed only to active native setup authority, allowing a fresh client to
recover the exact interactive operation after a lost response. The `complete-local`
HAL relation uses the same setup/Local/incomplete conditions. A new browser must
reuse that discovered reference rather than reserve a competing operation.

## Administrative Credential Handover

`LocalIdentityAdministrationController` exposes the five instance-owned create,
reset, list, operation-status and reconciliation routes documented in the API
changelog. Application handlers resolve the current actor and read the persisted
platform-admin grant without the role-profile cache. Checks run at entry, before
mutation after awaited preparation, and before disclosing credentials or read
data. Tenant administration and setup-secret possession are not credential
administration authority. GET routing remains anonymous-capable by convention,
but the query handlers reject callers without current instance authority.

Creation accepts only an operation ID and email/profile intent; reset accepts a
new operation ID, expected predecessor/operation concurrency stamp and bounded
reason, with the target supplied by the route. Unknown request fields are rejected.
The server supplies the actor, generates the temporary password, and owns
verification provenance. `LocalCredentialIssueCommandResponse` uses controlled
factories over `BaseCommandResponse<Guid>`; only an issued, current ChangeRequired
result can contain plaintext. Replay and failure cannot carry that payload.

The creation handler composes native pending creation with the existing explicit
reconciliation command, then reads current operation status before handover.
Replay performs status reads only. All five routes are private/no-store; create,
reset and reconciliation suppress generic idempotency response storage. The native
operation ledger owns retry safety, so a cached HTTP success cannot bypass fresh
administrator authorization after grant revocation. If authority is lost
after a commit, the response contains no credential and the client-supplied
operation ID remains the recovery reference. These checks do not form a
distributed role-revocation lock across the application and Identity databases.

`LocalAccountsSection` retains unresolved operations in a component-local
`Dictionary<Guid, Guid?>` keyed by operation ID, with an optional reset target.
HTTP 403/409 after issuance does not prove rollback: only an authoritative result
for that exact operation resolves its pending entry. Cancelling a form or
checking another operation cannot discard an unrelated recovery reference.
Successful issuance, status and reconciliation refresh nonsecret account metadata
under the same cancellation and request-generation guards, retaining the current
page and any one-time handover. This also refreshes predecessor and concurrency
metadata before another reset; oldest-first ordering does not imply that a new
account appears on page one. A separate component-local freshness flag disables
reset rendering, form opening and submission while this metadata is stale.
A failed or cancelled refresh cannot make old predecessor metadata actionable;
only a successful authoritative refresh restores reset availability.

List reads are bounded to 100 items per page and batch current metadata, receipts
and exact bindings. Missing or malformed metadata remains visible without reset
eligibility. The native HAL export and generated client preserve nullable credential
state; unknown metadata must not become a default enum value in the UI.
Status reads expose durable nonsecret audit even when a profile is
missing; GET never activates credentials. Native HAL policies advertise create,
eligible reset and pending-create reconciliation using instance-scoped facts.
The control-plane overview discovers the collection; clients follow links rather
than reconstructing permissions or lifecycle eligibility.

`ILocalCredentialAdministration.CreatePendingAsync` stores the native Identity
password hash, typed Pending metadata and a durable operation receipt in one
selected-store transaction. The receipt fixes the Local subject/platform-user,
personal Actor and provider-login identifiers. Only the successful creation
response contains the temporary password; replay returns the receipt without
changing or returning the password. No plaintext credential is stored in the
operation ledger.

`ReconcileLocalCredentialOperationCommand` carries only the operation ID.
Its handler resolves the caller's current exact provider binding and checks the
uncached database platform-admin role, repeating authorization inside the
primary-store serializable transaction. `AdminContext.ResolveUserIdAsync` does
not cache provider-to-user mappings; role-profile caches remain separate.
The handler creates or verifies the receipt's exact global User, personal Actor
and Local login through entity repositories, without matching email addresses
or granting tenant membership or administrator roles.

After the primary transaction commits and is disposed, the selected Identity
store independently checks the committed binding and current operation/token
ownership. Compare-and-swap updates move Pending to ChangeRequired and rotate
native security/concurrency stamps atomically, leaving the password hash intact.
A failed activation leaves a committed application graph and Pending credentials
that can be reconciled again. A completed activation replay does not rotate
stamps again. There is no distributed transaction across the two databases.

`ILocalCredentialAdministration.ResetAsync` is the trusted native reset seam.
Its nominal request identifies a new operation, the server-resolved initiating
administrator, exact Local subject, expected current operation/concurrency stamp,
and a trimmed audit reason limited to 1,000 characters. The administrative
handler independently authorizes that actor; the inner port is not a public
authorization boundary.

Within one selected Identity serializable transaction, reset requires a coherent
Ready/Replaced or ChangeRequired/ChangeRequired predecessor and its exact active
application binding. It inserts a Reset/ChangeRequired receipt, supersedes the
predecessor, and conditionally updates the token slot, password hash and native
stamps. All writes roll back if any conditional update loses. Native Identity
password validators and hashing are reused. Caller-owned transactions or pending
changes are rejected; cleanup detaches only the attempt-owned receipt.

Reset records its actor and reason separately from original verification
provenance. It preserves the original verifier/time, graph identifiers, email
confirmation, lockout and moderation. Kind-specific database constraints require
reset predecessor metadata and permit verification predating a reset. The stored
predecessor stamp is the value observed before supersession, not its rotated value.
Only the committed winner receives temporary plaintext. Exact historical replay
returns a safe receipt even after a later replacement/reset, without changing
current credentials; conflicting actor, target, predecessor, stamp or reason is
rejected. A lost commit acknowledgement therefore requires status recovery and,
if necessary, a separately authorized new reset—not password recovery from storage.
Request, receipt and result formatting is value-free; audit reasons and transient
passwords must not be destructured into telemetry.

For ChangeRequired login, the store freshly verifies the operation, subject,
checked-password security stamp and exact application binding before the signer
issues an HS256 replacement challenge. Its audience, purpose and token type are
separate from ordinary access; its lifetime is at most five minutes. It contains
only subject, operation, stamp, nonce and timing/purpose claims, not profile or
roles. `LocalAuthOutcome.ReplacementRequired` has `Success=false`, no failure and
no ordinary session payload; the wire exposes `replacementChallenge`, while
internal outcome enums remain JSON-ignored. The login handler skips `SyncUser`
for this result.

`ILocalCredentialAdministration.ReplaceAsync` consumes nominal authority from
the trusted challenge-authentication adapter, not a body-selected account. The
authority record is not itself a cryptographic validator. Within one selected
Identity Serializable transaction, the store rechecks operation/state/stamp,
binding and time; rejects the same password; runs native Identity validators
and hashing; then conditionally updates receipt, state token and password/stamps.
Every update must affect one row or all writes roll back. Time is checked again
after validation and hashing before writes. Fixed 12–128-character bounds are
shared with login. Completion returns no token: log in afresh with the privately
chosen password. Consumed or superseded authority cannot replace it again.

`POST /api/auth/local/credential-replacement` accepts the challenge as a Bearer
token and only `newPassword` in its JSON body. Its dedicated native JWT bearer
handler validates the signing key, issuer, audience, algorithm and token type,
then rejects duplicate or extra envelope fields, noncanonical identifiers and
invalid timing. The authority adapter reads exactly one authenticated identity
of the replacement scheme; it never combines claims across identities or exposes
replacement authority as ambient platform identity. Ordinary access tokens cannot
authorize this endpoint, and replacement challenges cannot authorize ordinary
user synchronization.

The controller sends `CompleteLocalCredentialReplacementCommand` through MediatR
to the existing credential administration port. Closed replacement outcomes map
to empty 204 success, bounded 400 password validation, 401 invalid authority or
409 concurrency conflict. Password limits are 12–128 characters; unknown body
fields are rejected. Responses are private/no-store, including authentication
rejection before MVC. Generic idempotency response storage is suppressed: a
repeated key cannot replay a prior password-change success. No session or cookie
is issued by replacement.

Ordinary Local provider reconstruction uses the exact Local issuer and a nonempty
canonical GUID subject as its Local account key. It does not interpret the Local
issuer as an OIDC URL or accept another issuer claiming the Local provider name.
External OIDC and AT Protocol key construction remain separate.
The shared platform-ID reader applies the same Local validation before any GUID
fallback. Rejected Local authority cannot become a providerless identity through
`sub`, name identifier, session ID or `internal_user_id`; valid Local authority
resolves only its canonical subject. Administrator resolution and claims
transformation therefore cannot recover an attacker-selected account ID after
provider reconstruction rejects it.

The BFF recognizes the replacement-only wire shape, including the API's empty
`failureCode`, false `emailVerified` and empty roles. A nonempty failure code or
coexisting ordinary session payload is rejected. It clears the current native
cookie/circuit state, rechecks onboarding/provider admission, and protects the
challenge with a dedicated time-limited Data Protection purpose. The separate
`__Secure-ISLAMU.LocalCredentialChallenge` cookie is HttpOnly, Secure,
SameSite=Strict and scoped to `/bff/auth/local`; its original UTC deadline is at
most five minutes and is never renewed. Token and protected-value limits keep
the cookie bounded without a chunking mechanism.

The browser receives only fixed navigation to `/auth/local/change-password`.
`LocalPasswordChange.razor` submits the new password through `bff.js` to
`POST /bff/auth/local/credential-replacement`, with the existing antiforgery
header and Local authentication rate policy. The BFF reads authority only from
the protected cookie and uses a private Split/Combined client without ordinary
token/header forwarding, pooled cookies, redirects or automatic retries. Only
downstream 204 completes replacement and redirects to fresh `/login`; password
validation and transient failures retain the original deadline, while invalid
or conflicting authority clears the cookie. Every submission clears component
password state. Native input description properties connect unique help/error
targets and retain a single visible error announcement.

Provider admission uses explicit deployment selection when present, otherwise a
successful current nonsecret API configuration response with Local provider ID
4. Missing/null provider data and fetch failures are not treated as Local.
Onboarding status is invalidated and reread before restricted handover and
replacement. The dynamic scheme manager's cached snapshot or a refresh that
silently retains old configuration is not fresh admission evidence. Ordinary
Local BFF principals use the trusted native Cookies scheme while retaining
`auth_provider=local`; the API JWT scheme remains separate.

An early BFF response callback applies private/no-store headers to Local login,
replacement and the password page, including pre-handler binding, antiforgery
and rate-limit failures. Cookie deletion on signout is browser cleanup, not
revocation of a copied challenge; native operation/stamp consumption remains the
replay boundary.

These boundaries are not yet a complete administrative lifecycle. Native reset
and restricted replacement support both Create and Reset operations; provisioning
activation remains Create-only. Administrative authorization/API/UI exposure,
browser cookie/live-circuit revocation and generated provider migrations still need work.
Focused BFF tests substitute downstream HTTP; real browser and Combined-to-native
API end-to-end verification remain separate acceptance gates.

`SyncUserCommandHandler` also requires a pre-established canonical Local subject,
matching User and active personal Actor. Local sign-in cannot create that graph
or adopt another account by email or a supplied User ID. For unlinked external
identities, a sole Local-owned email candidate is excluded; multiple candidates
still require explicit linking. Existing exact external bindings and verified
email matching among non-Local users retain their behavior. The handler rechecks
current bindings and Local ownership inside its serializable write transaction,
so a removed link or newly suspended Local actor cannot be hidden by a pre-read.
Configured administrator claims own a separate transaction. Unlinked claims use
a fresh server User ID, never a DTO-selected existing account. Inside configured
admission, reuse of a Local-owned user requires its current exact configured
provider/account link. This check precedes both writes and completed-claim replay
effects; ordinary synchronization checks cannot protect that separate transaction.

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
