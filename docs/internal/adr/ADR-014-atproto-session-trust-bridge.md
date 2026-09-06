<!-- ABOUTME: Architectural decision record for the AT Protocol OAuth session trust bridge and key purposes. -->
<!-- ABOUTME: Defines server-private verification, first-party JWT issuance, CarpaNet ownership, and rotation boundaries. -->

# ADR-014: AT Protocol Session Trust Bridge

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-18 |
| **Deciders** | ISLAMU Event Platform — Architecture, Security, Authentication workstreams |
| **Supersedes** | FishyFlip intent in the ATProto authentication stubs |
| **Superseded by** | — |

## Context

An AT Protocol authorization produces PDS-bound OAuth/DPoP credentials, not an application access token. The browser-facing BFF must establish a local cookie while the API independently verifies the PDS identity and continues to enforce its authorized-write invariant. Multi-node callbacks, custom tenant hosts, persisted OAuth sessions, and signing-key rotation must not put credentials or identity assertions in browser-visible channels.

## Decision

### Protocol and trust ownership

1. CarpaNet and CarpaNet.OAuth are the sole OAuth implementation. They own identity resolution, authorization-server discovery, PAR, PKCE, DPoP, callback validation, refresh, restore, and signout. FishyFlip, hand-written OAuth, Keycloak brokering, and CarpaNet.AspNetCore are excluded.
2. The BFF cookie is a browser session boundary only. It never contains PDS access or refresh tokens. PDS credentials and the complete CarpaNet session remain server-side.
3. The BFF calls the server-private ATProto session bridge with a short-lived, audience-, route-, method-, tenant-, and `jti`-bound ES256 bootstrap assertion. The assertion authenticates the BFF client but carries no authoritative user identity. The bridge is an authorized write; it is never anonymous, exposed through HAL, or generated into the public API client.
4. The API consumes the assertion `jti` once, restores the submitted CarpaNet session in a temporary store, calls `com.atproto.server.getSession`, and requires the expected DID, token subject, authenticated CarpaNet DID, and PDS response DID to agree. No BFF header or unverified JWT selector result establishes the user.
5. Only after independent verification may the API resolve the exact DID-linked account or apply the primary-provider provisioning rule below, persist the OAuth session, and issue a short-lived first-party ES256 session JWT. YARP forwards that platform JWT; the API selects its bounded issuer branch and then performs full issuer, audience, signature, lifetime, algorithm, and key-id validation.
6. When ATProto is primary, `BootstrapAtprotoSessionCommandHandler` and `AtprotoJitAccountProvisioningOperation` may provision one passwordless `User`, personal `Actor`, and global `UserExternalLogin` for the verified DID under transactional convergence. Optional ATProto under Local Identity or Keycloak requires an existing exact link. Neither path matches or synthesizes email, grants a role, or implies federation/publication consent. Administrator bootstrap remains separately setup-secret-bound or exact configured-DID-bound as documented in [SECURITY-MODEL.md](../SECURITY-MODEL.md).

### Session persistence and transient state

CarpaNet's `IOAuthSessionStore` has two adapters and one durable table: a BFF server-private API adapter and an Infrastructure repository adapter over `UserAuthenticationToken`. The complete versioned `OAuthSessionData` is encrypted as one AES-GCM envelope with subject DID, encryption key ID, safe PDS host, and expiry metadata; plaintext and partial-column fallbacks are forbidden.

OAuth state and cross-host handoff are atomic and single-use through the private primary-database bridge in every environment. `ApiBackedOAuthStateStore` and `AtprotoTenantSessionHandoffStore` share `ApiBackedAtprotoTransientStore`; Redis/memory transient adapters and their mode aliases are removed. Handoff URLs contain only an opaque random code, never a platform JWT, PDS credential, DID, or session payload.

One origin-protected, host-only `__Host-event-atproto-proof` cookie holds a separate 256-bit browser proof for a fixed fifteen minutes. It is Secure, HttpOnly, SameSite=Lax and smaller than 1 KiB. Established cookies are reused without sliding or per-flow deletion; independent random flow identifiers derive HMAC-SHA256 bindings stored inside protected state. Cold first-cookie races fail closed for the losing binding. Near-expiry challenges return a bounded retry deadline without replacing proof under active flows.

The adapters read/decrypt and validate issuer, PDS, current tenant/origin mapping and proof before candidate-bound consumption. Same-origin callbacks check browser possession; a canonical callback originating elsewhere can exchange the provider result but issues only an opaque handoff. Its destination validates the initiating browser before consuming and issuing a cookie. Cookie sign-in rechecks proof after provider exchange. State expiry reserves the two-minute handoff budget; a post-callback handoff is capped by proof expiry, not by already-consumed state expiry.

### Relational transient storage authority

The database-backed cutover introduces two instance-owned lifecycle entities:
`AtprotoTransientRecord` for protected OAuth state and tenant handoffs, and
`AtprotoTransientAssertionReplay` for machine-assertion replay claims. Both
BFF transient consumers now use this authority through private HTTP contracts.

These entities deliberately do not implement the business tenant-filter
contract. A canonical OAuth callback cannot know its originating tenant until
it restores protected state. Real authentication records nevertheless require
a nonempty tenant binding; only a separately constructed, internal health
probe may omit it. Handoff reads and all authentication consumes require the
expected tenant. This authority does not disable filters on sessions, users,
or other business data, and does not fabricate a tenant for replay claims.

Both tables use UUIDv7 identities and immutable rows. A transient record stores
a closed purpose, lowercase SHA-256 locator digest, tenant binding, bounded
ciphertext and Unix-millisecond expiry. The unique purpose/digest pair rejects
duplicate creation without overwriting or renewing the existing record.
Assertion replay claims use a separately unique digest and expiry; neither
table stores a raw locator or assertion. The BFF retains payload protection,
with separate Data Protection purposes for state and handoff.

Consumption reads an untracked candidate and conditionally deletes its exact
identity, purpose, digest and expected tenant while checking expiry again.
Only one affected row authorizes returning the candidate. Identity matching
prevents a stale reader from consuming a replacement row with the same
locator. The repository owns the durable delete boundary, rejects EF-managed,
ambient and explicitly enlisted outer transactions and non-relational providers,
and must not replay a destructive
operation after an uncertain commit. A lost response requires a fresh login,
not recovery of already-consumed authentication material.

Replay claims also reject all three outer-transaction forms: successful claim
creation must be committed before the authenticated operation is dispatched.
An outer rollback must never reopen an assertion already reported as claimed.

Expiry indexes support bounded cleanup, but eligibility is checked on every
read and consume independently of cleanup. OAuth state lasts at most ten
minutes, further bounded by SDK expiry and the browser-proof handoff budget;
handoffs last at most two minutes. Assertion claims remain until the entire
30-second acceptance window and five-second clock-skew allowance have ended.
Protected payloads are limited to 64 KiB UTF-8. Expired material may remain in
database backups according to operator retention; live-row cleanup does not
promise backup erasure.

`AtprotoTransientCleanupService` owns a bounded deletion pass over both tables,
called by the non-overlapping Quartz `atproto-transient-cleanup` job every
minute. It captures one time and performs at most five 500-row batches per
table (ten delete calls / 5,000 rows total), stopping each table on a short
batch. There is no idempotency-style 24-hour grace. Disabling ATProto login does
not unregister cleanup; the global scheduler switch still governs execution.
Each repository selects fixed expired IDs and performs one non-retrying,
parameterized deletion. Ambiguous acknowledgement stops the pass; native
provider retries must not silently select and delete additional batches.

Every BFF and API host must remain within five seconds of trusted UTC through
monitored clock synchronization. The maximum pairwise clock difference is
therefore ten seconds. Credential cleanup uses the captured time unchanged;
replay cleanup uses that time minus 10,000 milliseconds so an ahead cleanup
host cannot remove a claim still accepted by a behind verifier. The stored
claim expiry remains assertion expiry plus the five-second acceptance skew:
the retention margin must not extend assertion admission after validation.
The replay repository checks this same expiry both before insertion and after
the durable commit, rejecting a claim that expired while persistence awaited.
Any committed claim remains stored on rejection; cleanup owns its removal.
The margin is not a credential grace period and does not cover clocks outside
the supported bound; restore synchronization before serving authentication.

The same schema and winner semantics must hold on PostgreSQL, SQLite, SQL
Server and MySQL/MariaDB. EF Core generates each provider's migration history.
Application entities and mappings, not edited migration output, own schema
corrections. MySQL-family DDL failures require forward repair rather than an
assumption of transactional rollback.

### Private transient-service transport

The transient bridge is an instance service privilege, not a user identity
assertion. Its ordinary routes are the authorized POST operations `create`,
`read`, and `consume` under `/api/auth/atproto/transient/`, using only
`X-Atproto-Transient-Assertion`. The authenticated service subject cannot
establish a platform user, DID, browser session, or general business-data access.

The additional private `probe` operation accepts only
`{"purpose":"health_probe"}`. The Application handler generates tenantless,
random non-secret data with a thirty-second expiry and proves create, read-back,
and conditional consume before returning an empty `204`. No caller-selected
tenant, locator, or payload is admitted. The BFF uses the same signed bridge
without retry/hedging, a two-second deadline, and a ten-second native readiness
cache. Concurrent cache misses are coalesced per BFF singleton, with the
two-second budget covering gate waiting and the probe. Leftover synthetic rows after failure expire and use the ordinary
cleanup pass; no separate health table or persistence abstraction is required.

The ES256 profile uses issuer `event-atproto-transient-bff`, audience
`event-atproto-transient-api`, subject `event-blazor-bff`, and use
`atproto-transient`. It binds `jti`, `iat`, `exp`, `method`, exact `path`,
closed `operation` and `purpose`, and `body_sha256` for the exact request bytes.
Assertions last at most 30 seconds with five seconds of clock skew. The
OAuth-client public key ring remains the sole verification authority; key
URLs supplied by a request never select or fetch a verification key.

Ingress must enforce the route-specific rate limit before buffering and the
80-KiB request bound before signature verification or database work. The
private request-timeout policy covers buffering as well as dispatch. The protected payload retains
its separate 64-KiB UTF-8 limit. Bounded buffering is rewound for model binding;
duplicate credential headers, security claims or JSON security fields fail
closed. Invalid assertions cannot fall through to a bearer or user scheme.

The API commits the instance replay claim before dispatching the requested
operation. Only initial OAuth-state lookup can recover an unknown tenant;
handoff reads and ordinary consumes bind the expected enabled tenant. No
business tenant filter is disabled. Responses are no-store and do not log
assertions, locators or protected payloads. Generic idempotency or output
caching must not replay a consumed payload.

Browser-facing YARP routes must deny the private route set, independently of
stripping the assertion header. The controller, HAL discovery and public
OpenAPI/generated clients expose no transient-store affordance. Only the
synthetic probe accepts the closed `health_probe` purpose; ordinary
create/read/consume never accept that purpose.

### Cryptographic purpose separation and rotation

Three instance-scoped, rotation-capable secret purposes are mandatory:

- `auth.atproto.oauth_client_private_jwks` signs OAuth `private_key_jwt`, server-private bootstrap, and transient-service assertions with separate validated assertion profiles.
- `auth.atproto.session_encryption_keyring` encrypts persisted CarpaNet OAuth-session envelopes.
- `auth.atproto.session_jwt_private_jwks` signs first-party API session JWTs.

Keys are never reused across these purposes. ES256 signing rings have unique nonblank `kid` values and exactly one active key. Retired public OAuth client keys remain in `/oauth/jwks.json` for the overlap window required by in-flight assertions and bound sessions. Private `d` values remain server-only and are excluded from public JSON, logs, health output, exceptions, traces, and diagnostics. A malformed OAuth-client ring fails BFF readiness closed; malformed session-encryption or session-JWT rings fail closed when their Infrastructure or API consumer uses them.

The BFF's URL-form client ID is its exact canonical HTTPS `/oauth/client-metadata.json` URL. The anonymous metadata and JWKS endpoints serve exact JSON without redirects only on that configured host, use bounded public caching and document size, advertise `private_key_jwt` with ES256 and DPoP-bound access tokens, and publish only EC P-256 public parameters. Loopback helpers may be used only in Development where CarpaNet explicitly supports them; they cannot weaken production canonical-host, HTTPS, key, or egress policy.

## Rejected alternatives

1. Anonymous bridge writes or trusted BFF identity headers — they bypass independent PDS verification and the API write boundary.
2. Sending PDS tokens to the browser or accepting them as platform bearer tokens — their audience and proof binding are not the application's authorization contract.
3. One key for OAuth assertions, envelope encryption, and API JWTs — it expands compromise impact and confuses verifier trust.
4. BFF database access or a second durable OAuth-session table — both violate the BFF boundary or duplicate session authority.
5. Non-atomic cache get/remove or JWT query-string handoff — both permit replay or credential disclosure.
6. Implicit DID-only user creation — it conflicts with existing required identity fields and explicit-linking policy.

## Consequences

- The browser receives only an HttpOnly BFF cookie; the API receives only a short-lived first-party bearer token after PDS verification.
- BFF, API, and Infrastructure need separate adapters and key consumers while Domain remains unaware of CarpaNet types.
- Key rotation requires overlap publication, `kid`-based verification/decryption, multi-node consistency, and consumer-specific verification before retired-key removal.
- CarpaNet outbound traffic passes the constrained transport boundary when an OAuth or PDS operation runs. BFF readiness combines local prerequisites with the signed operational transient probe, not a user PDS, authorization-server discovery, or session-encryption/session-JWT key-ring probe. ATProto-primary failure is Unhealthy; optional ATProto failure with explicit Local Identity/Keycloak primary is Degraded, and disabled ATProto is Healthy. Liveness remains independent.
- In-flight logins must restart after the transient-backend cutover; no legacy reader is retained. Existing BFF Data Protection remains separate: Redis-free hosts persist the native key directory, replicas share it with application discriminator `islamu-event`, and an operator choosing no Redis must also remove explicit Redis key persistence. Required key loss fails closed rather than silently relocating keys.

## Related

- `dev/report/atproto-report.md`
- `dev/active/atproto-auth/atproto-auth-plan.md`
- `dev/active/atproto-auth/atproto-auth-context.md`
- ADR-001: Authorization Provider Architecture
- ADR-015: AT Protocol Event Federation Ownership
