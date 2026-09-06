<!-- ABOUTME: Describes authentication, authorization, and trust boundaries for the platform. -->
<!-- ABOUTME: Focuses on enforced BFF, MediatR, provider, tenancy, and fallback behavior. -->

# Security

## Legal-Identity Trust Boundaries

Tenant directory identity is tenant-scoped untrusted input until the Application
handler validates and normalizes it. The authenticated API derives tenant and
actor scope from trusted request context, uses optimistic concurrency, and
never accepts a body-supplied tenant authority. Public DTOs expose only the
normalized facts required for accountability; audit actors, concurrency
metadata, and blocker payloads stay private.

Capability failures use stable reason codes and telemetry must not include
legal names, registration identifiers, emails, or URLs. Anonymous settings and
shell composition fail closed rather than crossing tenant, branding, or
instance boundaries. Paid acceptance is replay-safe because the server
recomposes identity, organizer-recipient, provider, policy, schedule, line, and
money evidence and compares the exact disclosure revision before handoff.

> **Audience:** Operators | Contributors | AI agents
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-08-27
> **Source Anchors:** `docker-compose.yml`, `src/Event.Standalone/Dockerfile`, `Event.Standalone/Program.cs`, `Event.Standalone/Middleware/CombinedApiBridgeMiddleware.cs`, `Explore.Secrets/Database/PrimaryDatabaseConfiguration.cs`, `Explore.Blazor/Hosting/`, `Explore.Blazor/Services/InProcessEventApiTransport.cs`, `Event.Web.BffHosting/Security/EventBffRequestEnricher.cs`, `Event.Web.BffHosting/Security/BffProxyHeaderSanitizer.cs`, `Explore.API/BackgroundServices/PrivacyErasureStartupGate.cs`, `Explore.API/Scheduling/MaintenanceSweepJobs.cs`, `Explore.API/Controllers/PrivacyErasureController.cs`, `Explore.API/HealthChecks/PrivacyErasureReadinessHealthCheck.cs`, `Explore.Application/Services/RetainedAuthorityPrivacyErasureWorkflow.cs`, `Explore.Infrastructure/PrivacyErasureCredentialCleanupService.cs`, `Explore.Infrastructure/Services/Privacy/PrivacyErasureReplayService.cs`, `Explore.Persistence/Repositories/PrivacyErasureProviderWorkRepository.cs`, `Explore.Domain/PrivacyErasure*.cs`, `docs/AUTHORIZATION.md`

## Paid Commerce Trust Boundary

Paid acceptance is tenant-qualified immutable evidence, not a browser claim.
Official-instance status and legal identity come only from instance-owned
server configuration; payment operations are a separate authority, and the
merchant/organizer remains separate from both. Every new claim and provider
handoff requires a current acceptance revision plus the exact persisted
organizer actor, payment-connection ID, Connect platform ID, external account
ID, merchant country, tenant directory document/revision, instance identity,
payment operations, provider, policy, and composition facts. Claim validation
compares this recipient lineage before creating a payment attempt or dispatch
write. Missing or changed evidence, policy review requirements, or global/event
stop-sale removes HAL sale affordances and blocks direct endpoints. Existing
signed webhook intake, reconciliation, support, refund paths, and reads
intentionally remain available during stop-sale.

### Purchase Authority Boundary

`ReserveTicketPurchaseCommandHandler` accepts event, order, policy, access-mode, actor-selector, and transport operation-key inputs, but no tenant, account, enforcement-key, verified-contact hash, or quantity authority. It manually validates the request, resolves tenant from `ITenantContext`, derives quantity from persisted order lines, and delegates actor/contact resolution to `TicketPurchaseAuthorityResolver`.

Authenticated personal and group/organization purchases share the acting account as their hard-ceiling key while retaining the independently authorized actor as pinned context. This blocks context switching without collapsing unrelated members of the same group. Verified-contact mode requires the order's persisted `IsEmailVerified` fact and hashes its normalized email immediately. Name-only mode is bound to one order and intentionally has no hard cross-order identity claim.

The persistence boundary stores immutable policy lineage, cumulative authority usage, and a tenant-qualified operation key/fingerprint. Canonically ordered PostgreSQL advisory locks are acquired before the serializable snapshot so a waiter observes the winner. A replay with the exact fingerprint is safe; changed scope returns a stable conflict. No provider API, email, webhook, or other external I/O runs in that transaction, and failure output contains only generic codes plus the non-sentinel order identity.

## Security Model

Per [ADR-027](adr/ADR-027-first-class-authentication-provider-matrix.md),
Local Identity, Keycloak, and AT Protocol are first-class primary
authentication authorities, while fine-grained resource authorization remains
provider-neutral:

- Local Identity issues embedded platform JWTs without external identity infrastructure.
- Keycloak provides OIDC, centralized sessions, MFA, and enterprise federation.
- AT Protocol may be an optional linked login or the sole passwordless primary authority.
- `Explore.Blazor` (BFF server) handles OIDC/OAuth challenges and session cookies.
- Dedicated admin hosts use the embedded control-plane shell inside `Explore.Blazor` and the same server-owned OIDC session boundary.
- `Explore.Blazor.Client` (WASM) does not directly manage access tokens.
- `Explore.API` isolates Local/Keycloak bearer validation and AT Protocol bootstrap/session schemes before applying resource-level authorization.
- `ISLAMU Event Domain` remains authoritative for tenant memberships, legal entities, and event access control.

When AT Protocol is primary, a verified DID may JIT-create a passwordless
`User`, personal `Actor`, and global `UserExternalLogin`. This creates no role.
Interactive administrator authority remains setup-secret-bound and configured
administrator authority remains exact-DID, generation, and fingerprint bound.

## BFF/API Topology and Trust Boundary

`Hosting:Topology=Split` is the default composition: `Explore.Blazor` and `Explore.API` run as separate hosts, and the BFF uses YARP for `/api/*`. `Hosting:Topology=Standalone` explicitly starts `Event.Standalone`, which composes those same host modules in one process. The Standalone `Combined` profile does not register YARP or create a loopback proxy; the one-process bridge routes browser `/api/*` requests into the existing API pipeline in-process.

Topology is a composition choice, not an implicit database switch. The Split deployment uses `docker-compose.yml`; the standalone Dockerfile packages one directly-run container with SQLite defaults and no Compose descriptor. AppHost topology selection never changes the configured provider; SQLite must still be selected through structured provider settings when intended.

`Database:Schema` is a PostgreSQL/SQL Server deployment namespace, not a tenant
authorization boundary. Tenant filters, resource authorization, and
least-privilege runtime/migrator roles remain mandatory. SQLite instances use
separate local files, and MariaDB/MySQL instances use separate databases; all
three flat-provider families retain the fixed `ie_` prefix.

The process boundary changes, but the trust boundary does not. The bridge is responsible only for translating a BFF session into an API request; API `MultiAuth`, endpoint authorization, MediatR resource authorization, tenant filters, rate limits, and HAL link filtering remain authoritative.

### Cookie-to-API token conversion

For an authenticated BFF browser request in either Topology, the flow is:

1. The browser sends an HttpOnly BFF cookie to the BFF; it never receives the access token.
2. The BFF resolves the server-held access token and trusted tenant, setup-secret, and support-access context.
3. It strips browser-controlled authorization, API-key, setup-secret, tenant, support-access, cookie, and unsafe correlation headers, then adds only server-derived values.
4. The API validates the reconstructed bearer token and creates the API principal. The BFF cookie principal is not forwarded as API identity.

In Split, YARP carries the sanitized request to the API host. In Standalone, `CombinedApiBridgeMiddleware` applies the same rules in-process and clears the cookie principal before the API middleware runs. A valid cookie with no usable server-held token fails `401`; it cannot fall through as cookie authority or as a browser-provided bearer/API key. Requests without a valid BFF cookie preserve normal external bearer-token and API-key behavior.

The `InProcessEventApiHttpMessageHandler` used by server-side generated API clients is an isolated in-process API/BFF bridge: it creates a fresh service scope and HTTP context and deliberately excludes browser cookies, `Host`, and ambient principals. It preserves HTTP request/response semantics, not ambient authority.

### Antiforgery boundaries

Unsafe browser `/api/*` requests require the BFF antiforgery token: the BFF issues `XSRF-TOKEN` and the client returns it as `X-CSRF-TOKEN`. In Split, the BFF proxy validates this before YARP forwarding. In Standalone, the Combined bridge validates it before API dispatch. Direct API bearer-token and API-key clients do not traverse a browser-cookie boundary and are not subject to BFF antiforgery.

The documented onboarding/setup exceptions remain narrow: they use their existing setup credentials, server-owned state, authorization where applicable, and rate limiting. Do not make new exceptions for the one-process topology.

### Standalone limitations

Standalone reduces deployment and operational isolation: UI and API availability, deployment cadence, process resources, and scaling are coupled. It also removes the YARP network-hop/proxy diagnostic surface. Use Split when independently scaled hosts, separate deployment failure domains, or a network boundary between BFF and API is required. Do not treat one process as an authorization shortcut: token secrecy, privileged-header sanitation, API authorization, tenant isolation, and antiforgery remain mandatory.

Across the three application composition roots (`Explore.API`, `Explore.Blazor`, and `Event.Standalone`), AppHost defaults to Split and explicit Standalone uses `WithHttpEndpoint(name: "http")` for a dynamic/non-guaranteed internal HTTP endpoint plus explicit HTTPS `https://localhost:7180`; direct `Event.Standalone` launch profiles reserve `http://localhost:5180`. Returning to Split changes topology only, not data. Standalone does not select SQLite or provide `docker-compose.yml`. The canonical protected surface is `/api/...` with non-URL API versioning, never `/api/v1/...` (see [the support matrix](ARCHITECTURE.md#hosting-topology)).

Topology rollback is process-level only: `Hosting:Topology` controls how local AppHost composes processes and does not reverse migrations or data commits. For schema/data rollback after topologies are switched, use the migration backup/restore workflow.

## Participant Admission Readiness Boundary

`ParticipantAdmissionEligibility` is the non-PII authority projection for one tenant-qualified ticket assignment and participant. It records subject linkage, completion time, a canonical consent-record reference, approval, and terminal revocation. Typed answers and consent text remain in the existing registration evidence aggregates.

`ParticipantAdmissionReadinessRules` is the sole Domain decision surface. It evaluates confirmed-order authority, payment, subject ownership, mandatory completion, consent, approval, and revocation. `AdmissionIssuanceRepository` and `AdmissionCheckInRepository` receive the same `IParticipantAdmissionReadinessAuthority` and evaluate it inside their existing transaction after acquiring the assignment fence.

Subject completion derives the account from `ICurrentUserService`; organizer decisions derive the tenant-local actor from `IActorRepository`. Public command contracts carry only event, order, assignment, and participant UUIDs. They cannot supply tenant, subject account, operator actor, answers, consent text, payment state, or readiness disposition.

Approval revocation also transitions an existing active `AdmissionTicket` to revoked inside the same unit of work. Scanner callers receive only bounded admission outcomes, not the missing completion, consent, or approval detail.

The private HTTP resource is exact-scoped by event, order, participant, and assignment. It returns only assignment identity, a bounded readiness code, a bounded support code, active-credential availability, and server-authorized HAL actions. Account ownership, linked-subject identity, organizer permission, and guest capability are resolved in the Application layer; absent and invalid capability variants collapse to the same not-found response. Every success and failure is `private, no-store` with `no-referrer`.

The same-origin BFF forwards reads through the generated client and carries a guest order capability only in the dedicated request header. Completion, approval, and revocation require cookie authority, antiforgery before rate limiting, and the API's independent authorization. Incoming browser tenant headers and body authority are ignored because the BFF route contains only resource lineage.

## Ticket Transfer And Credential Rotation Boundary

`TicketTransferPolicy` is catalog-versioned configuration for one tenant and ticket type. `AdmissionTicketTransfer` is the append-only offer/acceptance record; it stores source and recipient subject references, bounded status/timestamps, the offered credential generation, and only a claim-capability digest. The active `AdmissionTicket` remains the holder and credential authority.

Transfer persistence uses the shared admission fence in canonical assignment → eligibility → ticket → transfer order. Under that fence, acceptance validates the current capability, expiry, hop, generation, check-in state, and recipient ownership before atomically changing holder, moving readiness to the recipient, rotating the keyed credential digest, invalidating active recovery capabilities, consuming the claim, and staging pointer-only outbox notification evidence. Cancellation, correction, reissue, revocation, recovery, and check-in use compatible ordering, so a concurrent loser returns a bounded outcome rather than overwriting the winner.

Commerce does not move with the holder. Registration order, purchaser account, order line, amount, currency, payment/refund allocation, and append-only check-in history remain unchanged. The transfer response exposes only transfer/ticket identifiers, a closed status code, a closed support code, hop, expiry, credential generation, and server-computed HAL relations. It contains no tenant, account, participant, purchaser, payment, contact, or capability identity.

Application CQRS handlers derive tenant, account, time, event start, operation identity, capability hash, and credential material server-side. Claim capabilities are high-entropy exact-resource values persisted only as digests and consumed once. Absent, malformed, expired, consumed, stale-generation, wrong-resource, and wrong-tenant attempts collapse to generic private unavailable responses. Replacement admission credentials are likewise stored only as keyed digests; plaintext is returned once in the dedicated redacted response contract.

The same-origin transfer BFF forwards only through `IEventApiClient`. The claim crosses browser and API boundaries exclusively in `X-Ticket-Transfer-Capability`; tenant headers and body authority are ignored. Writes require cookie authorization, antiforgery before partitioned rate limiting, and API reauthorization. The optional startup configuration `RateLimiting:TicketTransferWrite:PermitLimit` / `WindowSeconds` defaults to 10 requests per 60 seconds and is clamped to 1–100 permits and 1–3,600 seconds; it contains no secret and requires process restart to change. All transfer responses, including authentication and antiforgery short-circuits, are `private, no-store` with `no-referrer`. The Blazor client calls only the BFF and renders offer, accept, cancel, correction, and reissue controls solely from HAL relations.

## Privacy-erasure Authority Boundary

The platform ships one authority-first User-erasure workflow with three storage
topologies. `EmbeddedSqlite` keeps the ledger in a dedicated file, `CoLocated`
keeps it beside the application database, and `ExternalDatabase` keeps it in a
separate PostgreSQL database. Only `ExternalDatabase` provides independent
restore replay guarantees.

Authority facts are typed, immutable, monotonic User facts with bounded policy
and reason codes only. They do not carry names, email, addresses, arbitrary
JSON, SQL, or executable selectors. The runtime authority role is function-only;
the migrator role owns schema and grant management.

The local User fence and SHA-256 receipt hash are persisted before any PII
enumeration. Local dispositions, policy-version coverage, checkpoint advance,
and receipt status run in one serializable transaction. Remote provider calls
do not run in that transaction. Account deletion returns a short-lived receipt
only after local commit; status access uses the dedicated receipt auth scheme,
is not cacheable, and stays free of subject or intent existence leaks.

Actor tombstoning precedes provider-identity erasure. Each owned
`AtprotoIdentity` runs its aggregate-owned `EraseForPrivacy` transition, which
replaces the live DID with a non-parseable deletion tombstone and clears
provider metadata without letting Application code mutate identity fields
individually. This prevents later verified-metadata refresh from resurrecting
erased authority.

Startup replay reads the retained authority before host start, refuses sequence
gaps or checkpoint mismatch, reapplies uncaptured policy versions, and leaves a
fresh application database at sequence zero. A repeated replay is a no-op once
the checkpoint matches the retained authority.

Provider work claims use serializable lease fences and a monotonic fence token.
Stale claims cannot settle a newer claim, and unknown work can only be moved by
explicit reconciliation to completed or retry-scheduled state. Expired provider
locators and receipt hashes are cleared by bounded cleanup, with dry-run support
for operator review.

The readiness check reports only topology, restore-replay protection, caught-up
state, and aggregate provider/cache backlog counts. Remaining gaps are explicit:
no generalized compaction, no legal hold, and no claim that `CoLocated` restores
outside the primary restore contract.

The restore proof uses a real pre-erasure application dump, restores it into a
fresh database, and leaves the external authority untouched. Replay removes the
restored PII and converges the local mirror, checkpoint, and outbox once; a
repeated replay is a no-op. This proves the implementation mechanism, not that
any configured deployment has an independent backup lifecycle.

See [Privacy Erasure](PRIVACY_ERASURE.md) for the workflow and operator checklist.

## Security CI Gates

Security-sensitive changes are protected by both workflow checks and GitHub repository settings. See [CI_CD_GOVERNANCE.md](CI_CD_GOVERNANCE.md) for the required/advisory matrix, fork PR policy, and branch-protection guidance.

Current security gates:

- `Security Integration Tests` exercises auth, Keycloak, Cerbos, and policy-contract scenarios for matching paths and on schedule.
- `Cerbos Policy Validation` compiles static policies and policy tests with a fixed Cerbos binary version.
- `CodeQL Advanced` publishes code-scanning results for C#, JavaScript/TypeScript, and GitHub Actions.
- `Dependency Review` blocks vulnerable dependency changes on pull requests.
- Secret scanning and push protection must be enabled in GitHub repository or organization settings; they are not controlled by application runtime configuration.

## Authentication Flow (Current)

1. User authenticates through a browser BFF OpenID Connect flow.
2. The BFF stores the auth session in an HttpOnly cookie.
3. In Split, calls to `/api/*` are proxied by YARP from the BFF to the API; in Standalone, the Combined bridge dispatches them in-process to the same API pipeline.
4. The BFF adds the server-held bearer token through the shared BFF request-enrichment path; the API validates that token rather than accepting the cookie as API authority.
5. Embedded control-plane routes use the same BFF session; their actions remain authorized by API/Application policies and advertised through HAL links.

## JWT Bearer Configuration (API)

- Authority: Keycloak OIDC metadata endpoint.
- Multi-client audience validation: `islamu-event-api`, `islamu-event-blazor`.
- Custom `AudienceValidator`: checks both `aud` claim and `azp` (Keycloak authorized party) claim. Accepts if either contains a valid audience.
- Clock skew tolerance: 5 minutes.
- Dev mode: accepts self-signed certificates, suppresses HTTPS metadata requirement.
- Detailed JWT event logging on: `OnMessageReceived`, `OnAuthenticationFailed`, and `OnChallenge`.

### ATProto bootstrap and first-party session schemes

The `MultiAuth` policy selector preserves the Keycloak and API-key branches and adds two purpose-separated ATProto schemes:

- `AtprotoBootstrap` is valid only for `POST /api/auth/atproto/session`. The BFF signs a one-minute ES256 assertion with the OAuth-client key ring and binds issuer, audience, tenant, exact DID, explicit Person/Organization/Group classification, method, path, `iat`, expiry, and single-use `jti`. The assertion carries no user authority. Browser-supplied bootstrap headers are stripped, and the BFF injects a server-created assertion only for the private bridge request.
- The API atomically consumes the bootstrap `jti` in the durable idempotency table before dispatch. PostgreSQL `INSERT ... ON CONFLICT DO NOTHING` makes concurrent replay have exactly one winner across API instances.
- The private bridge is excluded from API discovery and generated browser clients, rate-limited as a write, request-size bounded, and returned with `no-store`. It accepts opaque CarpaNet session material only over the server-to-server BFF boundary.
- Infrastructure restores the OAuth session through CarpaNet, permits token refresh through the constrained ATProto transport, calls the user's PDS `com.atproto.server.getSession`, and requires the authenticated DID, returned DID, expected canonical HTTPS PDS, and linked tenant identity to agree before any write.
- API claim/body boundaries parse live identifiers into `AtprotoDid` before Application dispatch. Verification, current-session, prepared-session, and token-issuer contracts keep that typed value through Domain behavior; only JWT, provider, repository, and response egress unwrap the exact scalar value.
- Exact DID verification proves only the external source Actor. Promotion to a new Organization or Group preserves that Actor in place. Consolidation into an existing canonical Actor additionally requires a signed target ID and concurrency stamp plus active current-tenant OrgAdmin or GroupAdmin authority over an approved participation; missing, stale, cross-kind, suspended, deleted, or unauthorized targets fail before reference movement.
- OAuth-session encryption is prepared once before retryable work. One serializable transaction applies onboarding or consolidation and persists the prepared session on every database retry; cache invalidation and first-party JWT issuance occur only after commit. Merge evidence stores the identity ID and a bounded SHA-256 DID digest rather than the raw DID.
- `AtprotoSession` accepts only ES256 first-party tokens from the separate session-JWT key ring, with exact issuer/audience, known `kid`, valid lifetime, tenant claim, `auth_provider=atproto`, DID claim, and a platform user `Guid` in `sub`. Configured lifetime is constrained to one through sixty minutes.
- Current-session read, refresh, and revoke require both that `AtprotoSession` bearer token and a separate one-minute BFF session-bridge assertion bound to tenant, user, DID, method, path, and single-use `jti`. Refresh is serialized with a PostgreSQL advisory lock. Revoke attempts the remote provider operation but always removes the exact local encrypted session in `finally`; remote failure cannot preserve local authority.
- The browser cookie stores the first-party platform JWT, never a PDS access token, refresh token, or DPoP private key. Protected OAuth state and cross-host handoffs use the primary relational store through the private machine-authenticated API, with candidate-bound single-winner consumption.
- `ApiBackedAtprotoTransientStore` checks exact candidate equality and expiry again after the complete consume response arrives. A candidate expiring during delivery is rejected even if browser proof and session remain live; its committed deletion is never retried or recreated, and no authenticated cookie is issued from that result.
- A separate `__Host-event-atproto-proof` cookie is Secure, HttpOnly, SameSite=Lax, host-only and fixed at fifteen minutes. Each flow stores only its independent identifier, HMAC binding and proof expiry. Same-origin callbacks and destination handoffs validate possession before consuming; a canonical callback for another origin issues no authenticated cookie. Sign-in rechecks proof after provider exchange. Wrong-browser rejection preserves the destination handoff; proof-cookie completion never slides or rewrites the shared proof.
- The `atproto-authentication` health check validates canonical BFF identity, the OAuth signing ring, and state/session adapter registration, then performs a signed synthetic database create/read/consume probe. Its transport deadline is two seconds, without retries/hedging; completed results are cached for ten seconds. It does not certify a user PDS, discovery endpoints, or session-encryption/session-JWT key rings. Disabled ATProto is Healthy; unavailable ATProto primary is Unhealthy, while unavailable optional ATProto with explicit Local Identity/Keycloak primary is Degraded. Liveness does not probe the store.

OAuth session JSON, access/refresh tokens, DPoP material, JWTs, and JWK private values must never appear in logs, traces, metrics, URLs, OpenAPI, WASM authentication state, or generated clients. Verification failures use bounded reason codes; provider exceptions and response bodies are not reflected to callers.

### ATProto transient-service privilege

The private transient store authenticates an instance BFF service, not a user.
Only the exact POST create/read/consume/probe routes under
`/api/auth/atproto/transient/` use its dedicated assertion scheme and narrow
tenant-resolution exception. A bootstrap assertion, platform JWT or
browser-supplied tenant hint cannot substitute for that authority.

`X-Atproto-Transient-Assertion` carries an ES256 assertion with distinct
issuer `event-atproto-transient-bff`, audience `event-atproto-transient-api`,
subject `event-blazor-bff`, and use `atproto-transient`. Its method, exact path,
operation, purpose and `body_sha256` bind the exact buffered request bytes.
The maximum lifetime is 30 seconds plus five seconds of validation skew,
independent of user-token validation settings. Duplicate security fields,
unknown signing keys, request-provided key URLs and credential conflicts
must fail closed without another authentication scheme taking over.

Route-specific throttling precedes buffering; the private timeout covers the
body read and dispatch. The 80-KiB request limit precedes cryptographic and
storage work; ciphertext is separately bounded to 64 KiB UTF-8. A durable,
instance-scoped replay claim is required before action dispatch. Only the
initial OAuth-state lookup may omit the expected tenant; handoff access and
consumption require it. Replay admission and consumption reject outer
transactions so their success signals cannot precede durable commit.

The dedicated `probe` operation accepts only the signed body
`{"purpose":"health_probe"}`; callers cannot supply a tenant, locator, or
payload. `ProbeAtprotoTransientCommandHandler` generates tenantless random
non-secret data with a thirty-second expiry, creates it, reads it back, and
conditionally consumes it. Success is an empty `204`, never an identifier or
payload. Ordinary operations cannot request `health_probe`. A failed probe
may leave only this expiring synthetic row; the bounded minute cleanup removes
it alongside expired authentication rows and replay claims. Replay claims
remain through acceptance expiry including the five-second skew allowance.

Private responses use no-store and cannot be replayed by generic response
caching or idempotency middleware. Assertions, locators and ciphertext never
enter diagnostics. Browser YARP access is denied as well as header-sanitized,
and no public OpenAPI, HAL or generated-client surface exposes the store.
See [ADR-014](adr/ADR-014-atproto-session-trust-bridge.md#private-transient-service-transport).

### Global Actor and credential moderation

Only an authenticated instance administrator can suspend or reinstate a global Actor or exact `AtprotoIdentity`. The CQRS requests use the existing instance-setting update permission for `global-actor-moderation`, and each handler independently rechecks instance-admin status before target lookup. Tenant context, tenant-admin status, participation, DID ownership claims, and route input never grant authority over global state.

Actor suspension blocks the represented subject instance-wide. Identity suspension blocks only that exact DID credential instance-wide. Identity reinstatement clears moderation state without changing `IsActive`, because verified credential activity and moderation are independent facts. Real suspend and reinstate transitions append immutable moderation records. Same-state retries are successful no-ops and append nothing.

Every accepted moderation request invalidates Event list, detail, and discovery data in both `HybridCache` and the ASP.NET Core output-cache tags on the handling replica. The default output-cache store is process-local; `HybridCache`/`IDistributedCache` does not distribute output-cache tag eviction, so other replicas may serve stale discovery, detail, home, or sitemap output until the policy TTL expires. Cross-replica output-cache invalidation is deferred. The public query still rechecks current Actor, participation, presentation, record, and exact DID identity state, so cache invalidation is not the authorization boundary.

### Organization participation evidence

Legitimacy evidence belongs to `OrganizationTenant`, never the global Organization or Actor. An organization administrator can reserve and upload only an `application/pdf` private-owner Document through the organization-specific BFF session route. The server binds the upload session to the ambient tenant and exact pending participation; the browser supplies only the global Organization ID and file metadata, never a participation ID, storage owner ID, provider, object key, or destination.

Submission accepts only a finalized active private Document with the exact tenant, participation owner kind, participation owner ID, purpose, file type, and content type. Evidence rows use composite tenant foreign keys and retain the document against storage update, deletion, and orphan reconciliation. Tenant administrators review evidence separately; an evidence approval never mutates or auto-approves the participation.

Evidence API and HAL representations expose bounded document display metadata, review state, timestamps, and concurrency only. Protected document download and review actions exist only as authorized HAL links. Provider keys, object URIs, reviewer identity, tenant IDs, participation IDs, and document content are excluded from DTOs, logs, metrics, ProblemDetails, OpenAPI browser inputs, and generated clients.

## Auth Diagnostic Safety

OIDC and BFF challenge failures must expose only safe diagnostic handles:

- Browser redirects use `challengeError=1`, a normalized `errorCode`, and a correlation ID.
- Browser redirects must not include `errorDetail`, raw exception messages, provider response bodies, client IDs, client-secret length, client-secret prefix, tokens, or secret-derived metadata.
- Production-path logs use structured error codes, correlation IDs, failure categories, and boolean presence flags where needed. They must not log raw provider error bodies, raw exception text from identity-provider callbacks, client-secret prefixes, client-secret lengths, tokens, or refresh-token grant payloads.
- Development-only diagnostics such as `/auth/debug` remain a local troubleshooting surface and must never include secret values.

Use `ISafeAuthDiagnosticsPolicy` for BFF auth challenge and OIDC remote-failure redirects so user-facing errors stay generic while operators can correlate failures through logs and traces.

## Keycloak Identity Email Boundary

Keycloak-backed identity lifecycle email is account-authority owned. ISLAMU Event may request a Keycloak required-action email and record a local delegation audit, but Keycloak owns the action token, email template rendering, SMTP handoff, and delivery attempt.

- Email verification, password reset, email update verification, MFA, and other Keycloak required-action emails must not be routed through `EmailDispatchOutbox`, `IEmailService`, RabbitMQ, the Quartz scheduler, or product unsubscribe flows.
- Local results, logs, telemetry, and delegation audit rows may include only safe status, action, account-authority kind, local intent/delegation ids, HTTP status code, and normalized reason codes.
- They must not include Keycloak admin tokens, provider secrets, raw Keycloak response bodies, action tokens, rendered email subjects or bodies, theme output, SMTP passwords, or secret-derived metadata.
- Keycloak email theme customization changes Keycloak-owned templates only. It does not make ISLAMU Event the sender or decision owner for identity lifecycle messages.
- Sharing SMTP infrastructure with a self-hosted Keycloak realm is delivery plumbing only. The credential email decision and provider-side delivery state remain with Keycloak.

## ATProto/PDS Identity Email Boundary

ATProto/PDS identity lifecycle email is also account-authority owned. The PDS that hosts the account owns account email confirmation, password reset, email update, account migration/security messages, and any SMTP/provider delivery state for those credential flows.

- ISLAMU Event and a future ISLAMU Identity Microservice may request or audit account-authority actions, but they must not mint PDS confirmation codes, reset links, migration confirmation codes, or PDS credential email bodies.
- External PDS hosts and future ISLAMU-operated PDS cells are still account authorities for their hosted accounts. Operating the infrastructure does not make ISLAMU Event the product-email sender for PDS credential lifecycle messages.
- PDS account email is private account-hosting data. Local logs, support views, product notification flows, and delegation audit must not expose raw PDS email confirmation tokens, reset links, migration codes, SMTP credentials, or rendered PDS credential email content.
- Product notification email addresses are separate from PDS identity email. If ATProto login does not provide a verified notification-safe email, ISLAMU Event must use a separately verified app-level notification email or in-app notifications.
- Product email dispatch may use the current user email only when the synced identity claim explicitly marked it verified. Unverified or missing identity email must fall back to in-app notification rather than creating `EmailDispatchOutbox` rows.

## Header and Secret Hardening

Browser-facing BFF hosts emit security headers before HTML, error, static asset, and proxied responses are written:

- `Content-Security-Policy` keeps scripts self-hosted with the Blazor WebAssembly runtime allowances, blocks framing with `frame-ancestors 'none'`, limits forms to `self`, allows the documented Google font endpoints, and allows `data:`, `https:`, and `blob:` images so event images and browser-generated downloads keep working.
- `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, and `Referrer-Policy: strict-origin-when-cross-origin` are set at the BFF boundary.
- `Permissions-Policy: camera=(), microphone=(), geolocation=(self), payment=()` permits geolocation only to the first-party BFF origin for the explicit Home Discovery action. Camera, microphone, and payment remain disabled; the API host continues to disable geolocation.

In YARP transforms:

- `X-Tenant-Slug` is forwarded when route or request context provides an explicit tenant hint.
- Browser-supplied `X-Support-Access-*` headers are stripped before proxying. The BFF may add `X-Support-Access-Session-Id` only from server-owned support-access session storage after the authenticated actor has an active session bound to the current OIDC session.
- Any incoming or stale proxied `X-Setup-Secret` header is removed first. The BFF then resolves a setup secret through `ISetupSecretResolver` in this source order:
  1. BFF-owned setup handshake/session state,
  2. protected BFF-issued setup cookie,
  3. explicit local/development/bootstrap configuration fallback.
- Inbound request headers are never trusted as setup-secret sources. Browser-controlled `X-Setup-Secret` values must be stripped and ignored by both YARP and server-side forwarding handlers.

This prevents stale outgoing proxy headers and browser-controlled privileged headers from leaking across requests. Treat the setup secret as bootstrap-only sensitive material; the BFF protects the setup cookie with a 30-minute time-limited ASP.NET Core Data Protection payload, applies the same rolling inactivity limit to server-side setup sessions, and forwards only resolver output to downstream API calls.

## Embedded Control Plane Boundary

The control-plane UI is an admin-host shell inside the existing browser BFF, not a separate application or management API:

- `Explore.Blazor` authenticates through Keycloak OIDC Authorization Code flow plus PKCE and keeps tokens server-side.
- The browser receives only the HttpOnly BFF session cookie and display-safe page payloads. It must not receive access tokens, refresh tokens, client secrets, setup secrets, API keys, instance-admin authority claims, or raw OIDC diagnostics.
- `Bff:AdminHosts` selects the embedded shell, and optional `Bff:AdminHostAllowedIpRanges` restricts those hosts by IP/CIDR. Configured admin hosts are excluded from tenant custom-domain/subdomain resolution.
- Host classification is routing and shell selection only. `Explore.API` and Application/MediatR authorization remain authoritative for every action.
- Control-plane services use generated `IEventApiClient` contracts, and UI affordances come from generated HAL `_links`; local claim checks must not unlock actions.
- Browser-supplied privileged headers are stripped before proxying. Trusted tenant hints, setup-secret forwarding, and support-access forwarding remain server-owned BFF decisions.

## Support Access Trust Boundary

Admin support access is a persisted, time-boxed support session, not an impersonation cookie or tenant role grant.

- The real actor identity remains authoritative. `ICurrentUserService.UserId` continues to identify the authenticated instance admin; support metadata lives separately in `ISupportAccessContext`.
- The BFF stores only an opaque support-access session reference in server-side distributed cache, keyed to the authenticated user and OIDC `sid`. The browser does not receive access tokens, target-tenant role claims, or support-access authority claims.
- Runtime support context is explicit-header-only. Ordinary API requests without a BFF/server-injected `X-Support-Access-Session-Id` are treated as inactive even if the actor has a persisted active session.
- `SupportAccessSessionService` validates the forwarded session against persisted state, actor id, resolved tenant id, expiry, mode, and instance governance settings. Disabled support access, missing sessions, stopped sessions, expired sessions, actor mismatch, tenant mismatch, and write-mode-disabled sessions fail closed.
- Support access never creates `TenantUserRoleGrant` rows and never replaces tenant membership. Resource authorization must continue through MediatR, the runtime authorization provider, Cerbos/local fallback parity, and HAL link filtering.
- `SupportAccessAuditMiddleware` records bounded API request evidence for active support sessions after authorization. It captures method, route pattern/name, status/outcome, correlation id, trace id, actor, target tenant, and session id, without raw request bodies, cookies, tokens, provider responses, or unbounded reason text.
- Tenant-facing support-access evidence is read-only. The Blazor tenant settings view resolves the current tenant through the BFF/API status path and renders audit drill-in only from the API/HAL `audit-events` link.
- Audit persistence failures are warning-level operational events and do not change the original API response; security-sensitive lifecycle events still belong in the support-access command transaction where the command handler creates the session/audit records.

## Incoming Webhook Public Ingestion

Some machine callbacks are intentionally anonymous because the provider signature is the authentication boundary. The Svix operational callback at `POST /api/integrations/svix/operational` and the registration-provider callback at `POST /api/integrations/registration/{provider}/{bindingId}/callback` are public-ingestion exceptions.

- Verify signatures over the raw body before parsing JSON. For Svix-compatible callbacks, verification uses `svix-id`, `svix-timestamp`, and `svix-signature` with a bounded timestamp tolerance and fixed-time signature comparison.
- Enforce a configured body-size limit before dispatching to provider-specific verification or Application commands.
- Treat the provider message ID as the replay/idempotency key. Duplicate verified deliveries are acknowledged but must not re-run side effects.
- Persist only the durable idempotency ledger fields needed for processing: tenant binding when present, provider name, provider message ID, idempotency key, event type, payload hash, redacted headers, and bounded status/failure metadata.
- ProblemDetails, logs, metrics, and traces must not include raw callback bodies, signature headers, authorization headers, secrets, tokens, tenant/user identifiers, provider message IDs, or raw verification exceptions.
- Registration provider callbacks add provider/binding route metadata server-side, capture only a bounded retained message/effect pointer, and acknowledge non-oversize invalid or duplicate deliveries with `202 Accepted` to avoid provider retry storms and tenant enumeration. The worker validates the Data Protection receipt (`Explore.RegistrationProviderCallbackReceipt` / `v1`) before any registration effect.

## Registration Provider Browser Embed Boundary

External registration embeds are browser-only presentation, not authority. The BFF route `/bff/registration-provider-embed/tenants/{tenantId}/events/{eventId}/workflows/{workflowId}/requirements/{requirementId}/channels/{channelId}/bindings/{bindingId}` is authenticated, same-origin, no-store, and accepts no query string. It fetches a server-generated launch descriptor via the generated API client, rejects lineage mismatches and non-approved HTTPS URLs, blocks local/private literal hosts, and emits a route-specific CSP: `default-src 'none'; frame-src {approved-origin}; frame-ancestors 'self'; base-uri 'none'; form-action 'none'; object-src 'none'; script-src 'none'; style-src 'none'`. The HTML contains only a sandboxed iframe (`allow-forms allow-same-origin allow-scripts`) and a `noopener noreferrer` new-tab fallback. Iframe load or navigation never completes a requirement; clients poll server-owned order/requirement status.

## Outgoing Webhook Egress

Outgoing LocalProvider endpoints are user-configured URLs, so delivery is an SSRF-sensitive egress boundary.

- Block loopback, localhost, RFC1918/private, link-local, metadata, and internal DNS destinations by default.
- Allow private CIDRs only through explicit operator configuration for deliberate self-hosted/internal delivery.
- Disable redirects and use bounded connect/request timeouts.
- Sign LocalProvider requests with Svix-compatible `svix-id`, `svix-timestamp`, and `svix-signature` headers over the raw body.
- Store endpoint signing material through secret refs and rotate through the endpoint rotation route; never return old or current secret material after the allowed one-time reveal path.
- Health checks and metrics may report provider mode, queue counts, bounded failure categories, and secret-resolution booleans only. They must not expose endpoint URLs, query strings, payload JSON, secret refs, tokens, authorization headers, full responses, or raw transport exceptions.

SvixProvider keeps the Svix API token server-side behind `webhooks.svix.auth_token`. App Portal URLs are generated by the backend and are short-lived; browser clients never receive the Svix API token.

## Event Report And Moderation Privacy

Event reporting separates reporter-facing status from moderator-facing review workflows.

- Public report options are content-light and anonymous only for published-event reportability discovery.
- Report submission and `my reports` reads require the authenticated current user. Reporter-facing responses contain status/reason/event/timestamp/contact-consent metadata only; they exclude evidence text, reporter hashes, provider workflow data, moderation cases, decisions, signals, and internal notes.
- Moderator queue/detail reads and moderation actions require event-resource authorization before handlers load or mutate report state.
- Moderator projections remain data-minimized even for authorized management callers. They expose workflow state, reason/priority/status, current case, authorized evidence text on detail reads, safe signal summaries, provider type, sync state, retry counts, and HAL action affordances. They do not expose stable reporter user/actor identifiers, evidence creator identifiers, decision moderator identifiers, raw provider case/signal identifiers, provider URLs, provider correlation identifiers, reporter fingerprints, raw provider payloads, raw provider errors, or unsafe notes.
- Blazor and other clients must use HAL rels (`report-event`, `moderation-reports`, `triage-report`, `assign-report`, `decide-report`, and `execute-report-decision`) as the action affordance source instead of local role/claim checks.
- Managed reporting routing uses the same privacy boundary. Tenant routing-state, tenant dashboard, and control-plane operations surfaces expose only redacted provider target identifiers, configured flags, aggregate queue/provider-sync counts, and server-emitted HAL actions (`routing-state`, `edit`, `test-osprey-provider`, `test-coop-provider`). Raw endpoint URLs, API keys, webhook secrets, provider payloads, callback signatures, raw provider errors, report evidence, and tenant lists are never returned by read DTOs, HAL resources, generated response models, logs, telemetry, or disabled-state text. Provider test actions are readiness checks only; they do not dispatch external HTTP requests.

Approved planned email privacy rules split reporter case-update consent from follow-up contact consent; both default false, are reporter-withdrawable through a HAL-gated action, and are revalidated immediately before provider handoff. Withdrawal suppresses queued work before that fence but cannot recall an already in-flight SMTP handoff. Anonymous reports cannot opt into email without a separately approved reporter-PII design. `EventReportDecision` remains the sole decision authority; outcome email materializes only after idempotent enforcement completion, never on Osprey signals, escalation, failed enforcement, or an unprocessed Coop callback.

Lifecycle-email destination authority is explicit and limited to two sources. `TenantUserVerifiedEmail` requires a current composite tenant membership and verified persisted address. `ManagedTenantAdministratorInvitation` additionally requires same-tenant persisted managed-provisioning authority and the exact decoded invitation destination. Incompatible pre-1.0 delivery ledgers are transactionally reset rather than granted a synthetic legacy authority; preserved inbox notifications and unrelated business/audit/settings data are not deleted. Address, subject, body, exact location, report evidence, consent data, and provider errors are excluded from logs, metrics, health, pointer payloads, and deduplication keys.

## Event Location Privacy

Venue data is the platform's sharpest personal-data edge: a private home address identifies a
household, not an account. The boundary is enforced server-side and mirrored, never decided, by the UI.

- **Purpose partitioning.** Public, attendee, and management reads are separate operations with
  separate response contracts. The anonymous public read never varies by authentication cookie and is
  the only cacheable one; attendee, management, review, and both writes are `private, no-store`.
- **Fail closed.** Unknown or unclassified venues resolve to hidden, not public. A private home's
  public label is always the generic `Private venue`; its rooms, room descriptions, street, postcode,
  and coordinates are never on the public contract at all. Access instructions, entry details, and
  door codes have no route purpose on any contract.
- **Entitlement plus time.** Exact fields require the venue policy, the merged instance/tenant
  governance ceiling, the requester's registration coverage, *and* the server clock passing the reveal
  instant. The browser clock is never consulted.
- **Audited exact reads.** Management reads append a PII-free `EventLocationExactReadAudit` record
  before returning, so who looked at an exact address is answerable without storing the address again.
- **Consent-backed ownership.** A location becomes a private home only when an authenticated actor
  supplies an explicit versioned acknowledgement, and ownership transfers only to the consenting user
  themselves. Ownership is never inferred from `CreatedBy`.
- **Erasure converges.** Erasing a user tombstones their owned home's identifying labels and rooms,
  flags every affected `EventLocation` for privacy review, and emits correction intents inside the same
  transaction. Organizers clear the flag only by pointing at a usable venue or an explicit TBA.
- **Affordance gating.** Blazor renders Edit and remediation strictly from `_links["edit"]` and
  `_links["remediate-location"]` on that specific resource. Structured data (JSON-LD) is built only
  from the anonymous public projection and only from venue name, city, and country — never from the
  attendee projection, and never with street, postcode, or coordinates.

## Event Registration Privacy

Event registration reads are self-service by default. Attendee identity is not a generic event-registration read concern.

- Generic registration list, registration detail, and by-session reads require the authenticated current user and return only registration rows owned by that user.
- `GET /api/eventregistration/by-user/{userId}` is self-only. A route user id that does not match the authenticated current user returns `403 Forbidden` before MediatR dispatch.
- Client/API registration DTOs must not serialize registrant user ids, full names, or email addresses. A server-only `UserId` may remain on Application DTOs only when hidden from JSON and used for internal authorization/HAL context.
- Organizer or admin attendee-management workflows need a separate resource-authorized management projection before exposing attendee identity. Do not reuse self-read DTOs or anonymous/public event projections for attendee rosters.

Registration-form authoring is an authenticated, event-scoped control plane. All reads are private/no-store, all writes use the authenticated write rate limit and strong quoted concurrency preconditions, and MediatR authorization runs before repository access. The server enriches form authorization from the persisted parent Event; tenant IDs, organizer controller identities, machine status, and event-role assignments are never trusted from request bodies. Cerbos and local fallback both deny community contributors, listing submitters, tenant-only curators, instance administrators, machines, ambiguous organizer state, and unrelated tenant/event assignments.

Form DTOs may expose field governance, consent purpose code/text version, lifecycle status, provenance, schema hash, and concurrency stamps needed for authoring. They must not expose provider question IDs, registrant answers, PII, provider payloads, claims, roles, or capability booleans. Publication preflight and RFC 7807 failures identify bounded field/rule validation codes only and never echo answer data. Published versions are immutable; publication pins only artifacts generated from the current relational aggregate through the Application publication facade.

Walk-in optional-questionnaire discovery is anonymous but fails closed. The API emits the `optional-questionnaire` HAL relation and descriptor only for one active standalone attachment whose requirement is nonblocking and whose exact event-owned form version remains published with all pinned artifacts. Missing, deleted, foreign, incomplete, or mode-incompatible graphs return the same `404` shape and do not disclose attachment state. Attachment writes remain authenticated, event-authorized, rate-limited, and protected by strong optimistic concurrency.

### Promotion Code And Redemption Boundary

Promotion codes are bearer-like discount inputs, not identities or authorization claims. Plaintext is accepted only on create, publish, rotate, or order-apply request paths and is never persisted. Browser-safe management resources and order resources expose a masked label only; successful create and rotate command responses are the sole response exception and return one-time `issuedCode`. Persistence stores an HMAC-SHA256 digest, positive key version, active/retired state, and tenant/Event/catalog/currency scope as shadow metadata. Digests, key versions, secret references, and reservation identifiers are excluded from OpenAPI, generated clients, logs, metrics, traces, health, and HAL, while plaintext appears in generated request models only where submission is required and in those two issued-code responses.

Digest input is the normalized uppercase-trimmed code bound to tenant and Event. The backend resolves the dedicated instance secret `promotions.code_lookup_hmac_key` through `SecretBinding` with qualifier `v{keyVersion}` and environment binding `PROMOTIONS_CODE_LOOKUP_HMAC_KEY`; each qualified key is base64 and at least 32 bytes. `Promotions:CodeLookup:ActiveKeyVersion` selects the version for new code rows. Lookup computes candidates only for persisted scoped key versions, so planned key rotation must provision the new qualified key before activating it and retain every old qualified key until no active code row references that version. Code rotation retires the old digest row instead of rewriting historical reservations. Plaintext `issuedCode` is present only in successful create and rotate command responses and must be treated as one-time display material.

Redemption runs in a serializable transaction under tenant filters and deterministic definition/code locks. Code matching is constrained by tenant, Event, catalog version, active code, and key version before definition scope, currency, window, lifecycle, limits, purchaser identity, one-active-order slot, and pinned fee-policy facts are applied. Active and consumed reservations count toward limits. Purchaser precedence is account, then verified normalized email only without an account, then purchaser actor only without either stronger identity. This prevents one order from being counted under multiple purchaser identities while avoiding a claim that an unverified guest email is verified authority.

The API deliberately maps an empty or overlong submitted code, wrong account ownership, missing/invalid/expired guest capability, wrong scope, unknown or retired code, exhausted limits, and conflicting active reservation to the same registration-order `404`. The failure body never confirms whether a code exists. Syntactically malformed JSON is rejected earlier by the global safe `400` validation boundary. Guest apply/remove are `PublicTransactional`, require `Idempotency-Key`, use the 10-per-60-second effective-IP policy with no queue, and receive the capability only in `X-Registration-Order-Capability`. Browser requests remain subject to BFF antiforgery validation. Idempotency replay protection prevents cached response metadata from weakening no-store behavior.

Discount arithmetic is checked integer minor-unit arithmetic. The resulting order and line snapshots, masked label, and reservation identity are persistence facts, while public DTOs expose only the amounts and masked label needed to explain pricing. Platform fees and optional contributions are recomputed from the post-discount organizer amount; the contribution remains separate from organizer earnings and the promotion discount. Free finalization consumes the reservation atomically with lifecycle work. Cancellation, rejection, waitlisting, and recovery release an active reservation once. Organizer revocation is immediate at the server-owned decision instant and prevents new redemption without rewriting accepted reservation or snapshot history; there is no caller-scheduled effective time. No Phase 17 path creates a Stripe Checkout session, `PaymentAttempt`, payment webhook effect, payment-success transition, refund, dispute, or reconciliation record; navigation or an order reaching `AwaitingPayment` is not proof of payment.

### Registration Payment Navigation Boundary

Payment start and retry are durable local operations: they claim or reuse a `PaymentAttempt` and dispatch effect or requeue only a parked pre-handoff effect. They never synchronously create a provider Checkout session. `Unknown`, `Processing`, `Succeeded`, and `NeedsReconciliation` are not retryable. Status responses are private/no-store and exclude provider accounts, provider object/request IDs, idempotency material, capabilities, PII, and raw errors.

The browser follows only the same-origin `checkout-redirect` HAL relation. Its rate-limited antiforgery POST executes in browser fetch so Set-Cookie reaches the browser under InteractiveServer, Auto, and WebAssembly; guest capability exists only in `X-Registration-Order-Capability`. A dedicated random checkout-session cookie is independent of renewable authentication cookies. Both checkout cookies are Secure, HttpOnly, SameSite=Strict, PathBase-scoped, and short-lived. The compact protected payload contains only audience/order/session-digest/nonce metadata; the allowlisted target remains server-side. Consuming GET requires the exact `Sec-Fetch-Site: same-origin` value; `same-site`, `cross-site`, `none`, and missing are rejected before ticket validation. Anonymous issue limiting uses trusted post-forwarding effective IP plus resolved tenant only, while authenticated limiting uses stable user ID; caller cookies cannot change either partition. Issue uses abort-aware prepare/commit with server rollback, while JS operation IDs and AbortControllers cancel stale fetches on replacement, route disposal, or .NET cancellation. Wrong context, aborted requests, and host rotation do not burn or overwrite a valid nonce. Split uses Redis Lua compare-and-delete atomics and fails closed; standalone bounds and scavenges memory. No bearer enters paths, traces, logs, JSON, or browser-readable storage.

The API accepts a configured Checkout destination only when it is HTTPS on the default port, has no user info or fragment, and its normalized IDN host exactly matches `Payments:Stripe:AllowedCheckoutHosts`; wildcard hosts are invalid. The payment drain accepts `PublicBaseUrl` only as an HTTPS base URL with no query or fragment and preserves a normalized application subpath. Missing or invalid configuration defers new Checkout handoff without changing free-order finalization or stopping authoritative reconciliation.

## BFF Antiforgery Contract

Unsafe browser requests through the BFF at `/api/*`, including anonymous requests, must validate antiforgery tokens. Split validates at the YARP proxy boundary; Standalone validates in `CombinedApiBridgeMiddleware` before API dispatch. Direct `Explore.API` bearer-token and API-key callers do not cross the browser BFF boundary and are not subject to BFF antiforgery validation.

- Token issuance: `UseAntiforgeryTokenMiddleware` calls `IAntiforgery.GetAndStoreTokens` on non-static `GET` requests and writes the request token to the readable `XSRF-TOKEN` cookie. Static assets bypass issuance so the antiforgery service does not disable browser caching for immutable UI resources.
- Header contract: clients send the token back in the `X-CSRF-TOKEN` header. This matches the BFF `AddAntiforgery` configuration.
- Browser client path: `BrowserCredentialsMessageHandler` sends browser credentials, and `BffAntiforgeryMessageHandler` adds `X-CSRF-TOKEN` for `POST`, `PUT`, `PATCH`, and `DELETE` requests.
- Server self-call path: `BffCookieForwardingHandler` forwards captured cookies and mirrors `XSRF-TOKEN` into `X-CSRF-TOKEN` when InteractiveServer code calls BFF endpoints.
- Endpoint validation: unsafe minimal BFF endpoints call `.ValidateAntiforgery()`, which returns `400 Antiforgery validation failed` for missing or invalid tokens.
- API-path validation: unsafe `/api/*` requests validate through `EventApiProxyExtensions` before YARP forwarding in Split and through `CombinedApiBridgeMiddleware` before API dispatch in Standalone. Existing setup-secret and anonymous onboarding/bootstrap decisions remain outside this browser-antiforgery check.
- Protected endpoint families include auth refresh, support-access start/stop, storage upload session/proxy, payment Checkout redirect resolution, preference mutations, and appearance profile mutations.
- InteractiveServer storage upload self-calls use a short-lived Data Protection protected `X-ISLAMU-BFF-SELF-CALL` token bound to method, path, host, and authenticated user. That token lets same-process server calls satisfy the same endpoint filter without turning browser-originated storage uploads into an antiforgery exception.
- Documented exceptions are setup-secret bootstrap endpoints and `/bff/auth/refresh-session/internal`; these remain constrained by setup credentials, server-owned setup/session state, authorization where applicable, and rate limiting because they run before or outside normal browser antiforgery semantics.

Do not add new unsafe `/bff/*` endpoints without either `.ValidateAntiforgery()` or a documented bootstrap/internal exception with equivalent compensating controls.

### Public Transactional Capability Foundation

Public transactional capabilities are opaque, scoped, single-purpose, expiring bearer values and never prove user identity. Admission recovery now implements this foundation with keyed lookup digests, constant-time comparison, active-key rotation, atomic single-use consumption, and encrypted delivery intents. Plaintext capabilities are revealed only through their intended delivery boundary and are never persisted, logged, exposed as browser authority claims, or returned by administrative status surfaces.

ADR-022 and ADR-023 apply the same foundation to paid checkout, ticket recovery, transfer acceptance, admission credentials, and scanner operation. Stripe onboarding and Checkout return URLs are navigation only; raw signed webhook evidence plus account/payment retrieval and reconciliation determine provider truth. Paid admission requires the confirmed order, one exact reconciled success observation, its succeeded provider-neutral payment attempt, and matching currency/minor-unit snapshots; missing authority keeps the durable finalization effect retryable. Successful refunds reach admission only through persisted line allocations and accepted commercial snapshots: full matching ticket-line allocation revokes, while partial and unrelated add-on allocation preserves. Order and event cancellation use identifier-only outbox messages and idempotent transactional credential revocation. Admission QR content is a versioned high-entropy opaque credential with no PII, amount, email, display ID, or authorization claims, and persistence keeps only a keyed lookup digest. Transfer and reissue rotate the credential. Scanner capabilities are separately hashed, tenant/event/target/action/expiry scoped, revocable, and excluded from ordinary logs and metrics. Initial admission validation is online; offline signing remains deferred pending key-custody and revocation design.

Admission recovery requests are anonymous `PublicTransactional` writes protected by the exact
per-IP policy and idempotency middleware, while a chained limiter independently applies the
tenant-scoped recovery budget. Capability consumption retains the dedicated tenant limiter and
deliberately bypasses idempotency replay: the keyed, expiring capability is atomically single-use,
and replaying a cached successful bearer response would violate that authority.

### Admission Check-In Authority Boundary

Phase 21 admission check-in keeps staff and scanner authority deliberately separate:

| Path | Authentication | Target authority | Prohibited authority |
|---|---|---|---|
| Staff `/api/events/{eventId}/admission/check-ins` | Existing BFF/JWT staff authentication | Event check-in permission; request body carries `TargetId` | Scanner bearer, roster/payment/registration authority outside the authorized event. |
| Scanner `/api/admission/scanner/check-ins` | Dedicated `AdmissionScanner` authentication scheme | Authenticated capability supplies the one exact target | Caller-selected target, staff-role substitution, roster/payment/registration authority. |

Every write remains authenticated. Capability authentication is necessary but not sufficient: the
Application layer rechecks tenant, event, exact target, allowed action, expiry, and revocation scope
on every operation. One `AdmissionScannerCapability` is bound to **one exact `AdmissionTargetId`**;
a separate target requires a separate capability. This prevents a bearer from choosing or spanning
door scopes.

Capability plaintext is consumed only by the dedicated authentication service and is never forwarded
as a controller or orchestration value. Issuance persists a keyed digest and supports exactly one
plaintext disclosure to the successful issuance winner. Reads and revocations expose masked data
only. The issuance action suppresses generic HTTP idempotency response storage because its own
tenant-qualified `IssueRequestId` fence returns plaintext only to the winner; an optional
`Idempotency-Key` can neither persist nor replay that response. Credentials, capability values,
ticket/actor/device identifiers, raw scan input, and free-form
reasons are excluded from ProblemDetails, logs, metrics, traces, and export-safe audit.

Scanner paths select the dedicated authentication scheme before rate limiting. Valid capabilities
partition the limiter and idempotency fingerprint by the authenticated capability UUID, while
invalid traffic remains in the anonymous partition; neither the plaintext header nor its digest is
stored as an identity. Capability issuance requires an existing active target under the same
tenant/event and a `PlatformManaged` participation configuration. Revocation verifies the routed
event before mutating the capability, preventing event-A authority from revoking event-B scanners.
Revoking one capability is the device-containment control. Stopping an `AdmissionTarget` is the
stronger serialization barrier: staff check-in, scanner check-in, and new scanner-capability
issuance all take the same target fence and fail closed until authorized restore. Dependency
failure reports the target state as `Unavailable` rather than fabricating a durable `Stopped`
decision.

Public failures are deliberately generic: malformed, revoked, expired, wrong-tenant, wrong-event,
and wrong-target authority return the same bounded rejection. Internal append-only audit facts may
record only one closed undo reason code (`OperatorCorrection`, `DuplicateScan`, `WrongTarget`, or
`ExceptionalReconciliation`), never operator prose. A generic response must not be expanded into a
diagnostic oracle.

The client action boundary is HAL. `check-in-admissions` is the entry relation, and check-in, undo,
issuance, revocation, stop, restore, and reconciliation controls are rendered only when the server
emits their relation. A missing relation is a denial; cached roles, claims, status guesses, or scanner
state cannot recreate it. See [Admission Check-In Operations](OPERATIONS.md#admission-check-in-operations-phase-21)
for the incident and restore procedures.

## Storage Upload Session Binding

The Blazor BFF upload proxy is an SSRF-sensitive boundary because browser uploads could otherwise try to make the server send bytes to attacker-chosen destinations. Browser callers must not control provider, tenant, destination URL, object key, local path, or max-size policy.

- Browser upload flow starts with `/bff/storage/upload-session`. The BFF asks the API for a provider-neutral upload session and stores only the approved session metadata under an opaque BFF upload-session id.
- `/bff/storage/upload-proxy` accepts `uploadSessionId`, `contentType`, and `file`, not a trusted raw `uploadUrl`, provider object key, or filesystem path. It resolves the BFF session server-side and rejects missing, expired, cross-user, content-type-mismatched, size-mismatched, or unknown sessions.
- The BFF streams bytes to the API upload-session content endpoint. The API owns provider selection and writes to the selected `IFileStorageProvider`.
- Arbitrary HTTPS URLs, private/internal hosts, local filesystem paths, or presigned-looking attacker values must not be proxied merely because they resemble storage destinations.
- Upload sessions are short-lived, user-bound, content-type-bound, and consumed after successful upload. This keeps the browser path bound to a server-issued upload intent without duplicating tenant storage policy in the UI layer.
- Upload-session reservation rejects incoherent access metadata before storage-policy resolution or quota reservation. `public_image` requires a safe-raster MIME/extension pair and an image purpose; image purposes cannot be paired with non-raster metadata.
- Raster upload finalization accepts only exact, parameter-free `image/jpeg`, `image/png`, `image/gif`, `image/webp`, or `image/avif` declarations with matching extensions. The complete bounded container must match its declaration and be structurally framed through exact EOF before provider storage; non-raster signature checks retain prefix streaming.
- `SafeRasterContentPolicy` in Application is the one shared authority used by upload finalization, safe image-reference checks, AI image ingress, public delivery eligibility, the ATProto gateway, and PostgreSQL thumbnail materialization. Browser/AI paths use its JPEG/PNG/GIF/WebP subset; server/ATProto paths may additionally accept AVIF.
- Storage metadata updates cannot change byte identity: file type, MIME, extension, size, checksum, provider, and object key remain server-owned. Access updates are evaluated as the merged existing-plus-requested state so separate update groups cannot promote unsafe bytes into a public image.
- This structural guarantee does not mean the raster is decoded, sanitized, fully codec-valid, or malware-free. Pixel dimensions, decompression behavior, content moderation, and malware scanning remain outside this policy; existing upload-byte limits remain authoritative.
- BFF/API storage logs must not include raw upstream response bodies, presigned URLs, signatures, tokens, object keys, filesystem paths, filenames, or object secrets. Use safe fields such as status code, presence booleans, bounded provider labels, and session failure code.

Server-owned internal finalization, download, deletion, and reconciliation code may call a provider directly after applying its trust-boundary checks. No provider upload URL or destination is exposed to browser upload code; browser-facing paths use only the upload-session contract.

The untrusted upload allowlist deliberately excludes SVG and other active document formats. Trusted, packaged static SVG icons and illustrations remain valid presentation assets because they do not cross the storage upload boundary.

## Storage Object Download Boundary

Storage download access uses stable storage object IDs, never browser-supplied provider keys or local paths. Metadata/list/detail routes are authenticated and authorized with `islamuevent_storage_object:view`; content streaming uses `download`; presigned URL generation uses `presigned_download`. The dedicated anonymous route is `GET /api/storageobject/{id}/public`, which is limited by the storage content reader to active, non-deleted `public_image` objects with an image purpose and an exact safe-raster MIME/extension pair. Eligibility is checked before a provider is resolved or opened.

Authenticated content reads and presigned-download decisions retain lifecycle, tenant-filter, authentication, and owner checks. Structurally eligible raster metadata may be presented inline through the API; authenticated non-raster content is returned with a sanitized attachment disposition. Streamed responses preserve range processing, Last-Modified, checksum ETags, `nosniff`, and restrictive CSP headers.

Presigned URLs are bearer credentials. API responses containing them must not be output-cached, must send no-store cache metadata, and must not log the URL, signature, token, object key, bucket path, or raw provider error. The presigned response intentionally keeps `ObjectKey` empty, and the provider request forces an attachment response-content-disposition using the sanitized display name. Browser image projections never sign raw object keys; metadata-backed storage images use the stable `/api/storageobject/{id}/public` route, while explicitly external URLs remain external. Consumers must treat a returned presigned URL as short-lived secret material.

## Email Dispatch Operator Boundary

EmailDispatch status and delivery controls are operational APIs, not general tenant data reads. `GET /api/admin/email-dispatch/status`, tenant pause/resume, park, and replay all require authentication plus MediatR resource authorization against `islamuevent_email_dispatch`. Status uses `view`, tenant pause/resume uses `manage_tenant`, parking uses `park`, and replay uses `replay`.

Only tenant administrators for the resolved tenant and instance administrators should receive these operator decisions from Cerbos or local fallback. Regular authenticated users must receive `403 Forbidden`. The status projection must stay sanitized: no recipient email, subject, plain text or HTML body, reply-to, provider message id, raw SMTP/provider error, object key, token, or secret-derived metadata. HAL `replay` and `park` links are the only client affordance source for row-level controls.

Dispatch-time unsubscribe handling is part of the same boundary. The worker checks persisted `UserNotificationPreference` after claiming a row and before SMTP handoff for mapped lifecycle categories; opted-out rows become terminal `Skipped` outcomes instead of provider failures or retries. Outgoing email may contain opaque unsubscribe tokens in `List-Unsubscribe` headers and body links, but admin status APIs, logs, metrics, and health details must not expose those tokens or rendered message bodies. `Skipped` rows are terminal and must not receive replay or park HAL affordances.

Forwarded-host trust for direct API traffic:

- `Explore.API` only applies `X-Forwarded-Host`, `X-Forwarded-For`, and `X-Forwarded-Proto` when a trusted proxy boundary is configured through `ForwardedHeadersTrust`.
- Host-derived tenant resolution must use normalized `Request.Host` after trusted forwarded-header processing, not raw `X-Forwarded-Host`.
- If no trusted proxy boundary is configured, the API ignores forwarded host/IP headers and falls back to the direct request host and remote IP.
- The Split BFF uses the same validated proxy/IP network trust model for `X-Forwarded-For` and `X-Forwarded-Proto`, defaults to loopback-only trust, and never enables `X-Forwarded-Host`.
- BFF endpoint antiforgery validation runs after authentication and authorization but before endpoint rate limiting, so invalid checkout CSRF attempts cannot consume guest checkout permits.

## Authorization Boundary

Server-side enforcement is layered:

1. API endpoint-level attributes (`[AllowAnonymous]`, `[Authorize]`).
2. Application MediatR pipeline `AuthorizationBehavior`:
   - Checks `IAuthorizedRequest` interface — commands/queries declare required permissions.
   - Checks `[AuthorizeResource]` attribute — declarative resource-level authorization.
   - Optionally enhanced by `ISecureRequest` — provides dynamic resource context for fine-grained permission evaluation.
3. Runtime provider (`RuntimeAuthorizationProvider`) deciding Cerbos vs fallback.

See [AUTHORIZATION.md](AUTHORIZATION.md) for the full provider model, request patterns, and role boundary details.

Hard deny behavior:

- `AuthorizationBehavior` throws `AuthorizationException` on deny.
- API global exception handler returns HTTP `403 Forbidden` via RFC 7807 ProblemDetails.

Paid-event publication repeats its policy, organizer, connection, currency, disclosure, and commerce-authority checks in the server-side publish transaction; browser preflight is advisory UI state only. The organizer payment connection and policy reads are authenticated `private, no-store` resources. Browser policy responses omit policy and tenant identifiers. Browser-visible connection state is limited to status, merchant country, charge-capability state, requirements state, supported currencies, and readiness timestamp. It must not contain provider, platform, account, tenant, actor, connection, lineage, or evidence identifiers. Hosted onboarding exposes only an absolute HTTP(S) URL and whether an existing connection was reused; return and refresh redirects never assert readiness.

## Runtime Authorization Providers

Provider selection:

- Tenant BYO Cerbos (if configured) has priority.
- Else instance setting `AuthorizationProvider` chooses:
  - `"cerbos"` -> `CerbosAuthorizationService`
  - default/other -> `FallbackAuthorizationService`

Failure behavior:

- Instance Cerbos failure denies all authorized requests (fail-closed). The operator explicitly chose Cerbos; falling back to a potentially more permissive local RBAC would silently bypass intended policies.
- Instance provider-mode read failures also enter the Cerbos fail-closed path and log only safe failure-type metadata; they do not default open to local RBAC.
- BYO Cerbos:
  - Any PDP failure -> provider-instance fallback `SafeMode` (deny all except instance admin path).
  - There is no fail-open configuration. The `cerbos.failure_mode` setting was deleted; BYO PDP outages always fail closed into safe mode.
  - BYO config resolver failures activate provider-instance safe mode instead of silently using local RBAC.
  - `cerbos.mode=custom_endpoint` with a blank PDP endpoint preserves BYO mode/failure mode and any explicit BYO Admin API config; runtime authorization activates safe mode rather than falling back to the instance PDP.

Runtime failure logs must not include raw PDP/Admin API endpoints, Admin API credentials, JWTs/tokens, response bodies, or exception objects/messages. Log failure type, request/correlation identifiers, counts, modes, and actions only.

## Policy Topology

Authorization policies are organized in three tiers:

### Static Policies (Disk)

Static resource policy files plus `derived_roles.yaml` live in `cerbos/policies/`. Avoid relying on an exact count in docs; architecture tests and policy parity checks are the safer source of truth.

- **`derived_roles.yaml`**: Resolves instance admin, tenant admin, and org admin roles from principal attributes and resource context.
- **Resource policies** (`{kind}.yaml`): Each defines rules per derived role and `authenticated_user`. Instance admin gets wildcard `"*"`, tenant/org admin get CRUD, authenticated user gets `"view"`.
- **Standard actions**: `view`, `create`, `update`, `delete`.
- **Extended actions**: `manage_members`, `lock`, `unlock`, `viewsharedcontacts`, `exportsharedcontacts`, `sync_diff`, `sync_apply`.

### Package Publishing (Admin API Store)

Cerbos policy publishing is package-based. `IPolicyPackageService` builds the bundled policy package from the static policy manifest and source files, then pushes that package through the configured Cerbos Admin API endpoint and triggers reload/status handling.

- Setup, admin UI, and zero-touch boot sync use the same package service rather than ad-hoc runtime role-policy generation.
- Manual ZIP fallback exports the same bundled package for operators to install with Cerbos tooling when Admin API sync is unavailable.
- Custom role CRUD remains represented through the application authorization catalog and policy package model; dynamic role-derived policy generation is deferred until it is reintroduced as a package-manifest contributor.

### BYO Cerbos (Per-Tenant Override)

Tenants may point to their own Cerbos PDP endpoint via tenant settings:

- Receives the same `AuthorizationCheck` payloads as the instance PDP.
- `AuthorizationCheck.Scope` enables per-tenant policy resolution within the BYO PDP.
- Optional BYO Admin API endpoint and credentials can target package sync/status independently of the PDP endpoint.
- Failure modes: any PDP failure activates provider-instance safe mode (deny all except instance admin); `open` is deprecated and ignored at runtime.
- A blank custom PDP endpoint is treated as a BYO runtime configuration error, not as an instruction to use the instance PDP. Any explicit BYO Admin API config is still preserved for package operations.
- Only applies to resource checks. Setting access always uses the instance provider.

### Authorization Catalogs

| Catalog | File | Purpose |
|---|---|---|
| `AuthorizationActions` | `Application/Authorization/AuthorizationActions.cs` | Action string constants matching Cerbos policy action names |
| `ResourceKinds` | `Application/Authorization/ResourceKinds.cs` | Resource kind string constants matching Cerbos policy file names |
| `ResourceDescriptors` | `Application/Authorization/ResourceDescriptors.cs` | DTO → authorization metadata extractors (kind, id, attributes, scope) |

### Custom Property Authorization Policies

Five resource policies govern custom property operations:

| Policy File | Resource Kind | Actions | Notes |
|---|---|---|---|
| `custom_property_template.yaml` | `islamuevent_custom_property_template` | view, create, update, delete, sync_diff, sync_apply | Template/runtime definition CRUD + sync operations. Tenant admin can manage templates within their tenant; hard purge routes additionally require the API Admin role. |
| `custom_property_value.yaml` | `islamuevent_custom_property_value` | view, create, update, delete | Runtime value CRUD. Org admin can manage values for their organization's entities. |
| `custom_property_projection.yaml` | `islamuevent_custom_property_projection` | view, update | Projection admin (rebuild, drain). Tenant admin can trigger rebuilds and drain dirty scopes. |
| `custom_property_governance.yaml` | `islamuevent_custom_property_governance` | view | Governance reporting. Tenant admin can view governance recommendations. |
| `platform_namespace.yaml` | `islamuevent_platform_namespace` | view, create, update, delete | Platform-reserved namespace protection. **Explicit deny** for tenant admin and org admin on write operations. Only instance admin can write. |

#### Endpoint-to-Policy Mapping

| Endpoint | Controller | Action | Resource Kind | Policy Rule |
|---|---|---|---|---|
| `GET /api/event/{id}/custom-property-definitions` | `EventCustomPropertyDefinitionController` | view | `islamuevent_custom_property_template` | AllowAnonymous |
| `POST /api/event/{id}/custom-property-definitions` | `EventCustomPropertyDefinitionController` | create | `islamuevent_custom_property_template` | Authorize |
| `PATCH /api/eventcustomproperty/{id}` | `EventCustomPropertyController` | update | `islamuevent_tenant` | Authorize; persisted definition binds tenant authority |
| `PATCH /api/eventsessioncustomproperty/{id}` | `EventSessionCustomPropertyController` | update | `islamuevent_tenant` | Authorize; persisted definition binds tenant authority |
| `PATCH /api/custompropertydefinition/{id}` | `CustomPropertyDefinitionController` | update | `islamuevent_tenant` | Authorize; persisted definition binds tenant authority |
| `DELETE /api/event/{id}/custom-property-definitions/{defId}` | `EventCustomPropertyDefinitionController` | delete | `islamuevent_custom_property_template` | Authorize |
| `DELETE /api/eventcustomproperty/{defId}/purge` | `EventCustomPropertyController` | update/delete | `islamuevent_custom_property_template` | Admin role |
| `DELETE /api/eventsessioncustomproperty/{defId}/purge` | `EventSessionCustomPropertyController` | update/delete | `islamuevent_custom_property_template` | Admin role |
| `DELETE /api/custompropertydefinition/{defId}/purge` | `CustomPropertyDefinitionController` | update/delete | `islamuevent_custom_property_template` | Admin role |
| `GET /api/event/{id}/custom-property-values` | `EventCustomPropertyValueController` | view | `islamuevent_custom_property_value` | AllowAnonymous |
| `POST /api/event/{id}/custom-property-values` | `EventCustomPropertyValueController` | create | `islamuevent_custom_property_value` | Authorize |
| `PUT /api/event/{id}/custom-property-values/{valId}` | `EventCustomPropertyValueController` | update | `islamuevent_custom_property_value` | Authorize |
| `DELETE /api/event/{id}/custom-property-values/{valId}` | `EventCustomPropertyValueController` | delete | `islamuevent_custom_property_value` | Authorize |
| `POST /api/admin/custom-property-projections/rebuild` | `CustomPropertyProjectionAdminController` | update | `islamuevent_custom_property_projection` | Authorize |
| `POST /api/admin/custom-property-projections/drain-dirty-scopes` | `CustomPropertyProjectionAdminController` | update | `islamuevent_custom_property_projection` | Authorize |
| `GET /api/admin/custom-property-projections/status` | `CustomPropertyProjectionAdminController` | view | `islamuevent_custom_property_projection` | Authorize |
| `GET /api/admin/custom-property-projections/events/{eventId}` | `CustomPropertyProjectionAdminController` | view | `islamuevent_custom_property_projection` | Authorize; `exposureCeiling` limits row visibility |
| `GET /api/admin/custom-property-projections/sessions/{eventSessionId}` | `CustomPropertyProjectionAdminController` | view | `islamuevent_custom_property_projection` | Authorize; `exposureCeiling` limits row visibility |
| `GET /api/custom-property-governance/recommendations` | `CustomPropertyGovernanceController` | view | `islamuevent_custom_property_governance` | Authorize |

#### Platform Namespace Protection

The `platform_namespace` policy enforces a hard boundary around the `platform` namespace:

- **Instance admin**: Full CRUD (wildcard `"*"`).
- **Tenant admin / Org admin**: **Explicit deny** on `create`, `update`, `delete`. Can only `view`.
- **Authenticated user**: `view` only.

This ensures platform-defined property definitions (e.g., standardized fields shared across all tenants) cannot be modified by tenant-level administrators. The deny rule takes precedence over any derived role grants.

## Scoped Policy Resolution

Authorization checks carry explicit scope context via `AuthorizationCheck.Scope` (containing `TenantId` and/or `OrganizationId`). This enables fine-grained policy routing:

### Resolution Order

1. **Check scope** — `AuthorizationCheck.Scope?.TenantId` is preferred when set by a resource descriptor.
2. **Ambient context** — `ITenantContext.TenantId` is used as fallback when check scope is null.
3. **Cerbos scope field** — The effective tenant ID populates the Cerbos resource `scope` field only when `Cerbos:UsePolicyScope=true`, enabling per-tenant policy overrides within a shared PDP.

By default, runtime HATEOAS checks keep tenant context in resource attributes and do not set Cerbos resource scope. If `Cerbos:UsePolicyScope=true` is enabled, the instance Cerbos PDP must run with `engine.lenientScopeSearch=true` and have a complete scoped-policy chain; otherwise Cerbos can return missing decisions and permission-bound HAL links fail closed.

### Override Strategy

| Tier | Policies | Override Behavior |
|---|---|---|
| Instance PDP | Static (disk) + Dynamic (DB store) | Baseline for all tenants |
| BYO PDP | Tenant-controlled | Full override — all checks route to tenant endpoint |
| Scoped policies (planned) | Per-tenant Cerbos scoped resources | Selective override — only matching scopes diverge |

### Contract Enforcement

JSON schemas in `cerbos/policies/_schemas/` enforce structural contracts across all tiers:

- **Principal schema** (`principal.json`): Validates `isInstanceAdmin`, `tenantMemberships`, `orgMemberships` on every check.
- **Resource schemas** (`{kind}.json`): Validate required attributes (e.g., `tenantId`, `actorId`) per resource kind.
- **Enforcement mode**: `warn` (logs validation errors without denying). Set to `reject` in production to force-deny malformed checks.
- **BYO alignment**: BYO PDPs should adopt the same schemas to maintain contract parity. Schema files are distributed alongside static policies.

## Claim Fallback Rules in Code

`Explore.Application.Authentication.PlatformIdentityPrincipalExtensions` is the single authority for turning a
`ClaimsPrincipal` into a platform user id. `IUserContext` and `EventControllerBase` both delegate to it, so
there is one chain rather than the three divergent ones that previously coexisted.

Order, accepting only GUID-parseable values:

- `sub` -> `ClaimTypes.NameIdentifier` -> `sid` -> `internal_user_id`

Notes:

- `internal_user_id` is a BFF-enriched local-user claim added after external identity resolution. It is the
  **last** link in the chain, not a separate one: the provider claims come first because for platform-managed
  accounts the provider subject *is* the local user id, which keeps a single identifier authoritative.
- When the subject is not a GUID at all (ATProto DIDs, Google subjects), the chain yields `null`. Resolve the
  linked local account with `IMediator.ResolveCurrentUserIdAsync(principal, ct)` rather than reading a different
  claim — a `null` result is an authentication outcome to map, not a reason to fall back elsewhere.
- Purpose-bound schemes (API key, setup secret, managed control plane, ATProto session, privacy-erasure receipt)
  validate their own claims at the authentication boundary and deliberately do **not** route through this chain.
- A few BFF-only helpers stop at `sub` -> `ClaimTypes.NameIdentifier` where the server-authenticated session is
  already authoritative.

## Client-Side Authorization Scope

Blazor client checks are UX-only:

- route/menu/button visibility,
- reduced unauthorized UI paths.

They are not security enforcement. Security enforcement remains server-side through API and MediatR authorization.

## Blazor Auth-State Serialization Boundary

`Explore.Blazor` serializes only display-safe identity hints into the browser authentication state:

- allowed: display/name hints such as `name`, `preferred_username`, `given_name`, and `family_name`,
- excluded: `sub`, `sid`, `ClaimTypes.NameIdentifier`, `internal_user_id`, tenant identifiers, roles, permissions, admin claims, email, tokens, and any action-authority claims.

Browser-visible authorization, tenancy, feature access, and action affordances must come from BFF/API/HAL/status endpoints, not from serialized claims. Server-side claims may still enrich the BFF principal for API calls, token forwarding, setup flows, and server-only authorization decisions.

## Admin Claims Enrichment

`BffAdminClaimsTransformation`:

- calls API endpoint `api/User/admin-authority`,
- adds admin claims to the server-side BFF principal for server decisions and downstream API context,
- resolves and adds `internal_user_id` by matching external identity (`provider + provider subject`) to local user records,
- caches positive results for 5 minutes and negative results for 30 seconds.

These admin and internal-user claims are intentionally not serialized as browser authority. Blazor UI affordances must use BFF status endpoints, API/HAL `_links`, or other server-confirmed contracts.

Post-onboarding provider management safety:

- `GET /api/instance/settings/auth-provider` and canonical `PATCH /api/instance/settings/auth-provider` accept either active setup-secret authority or authenticated instance-admin authority.
- Authentication update flow denies requests that would disable all providers linked to the current admin account (self-lockout prevention).
- `GET /api/instance/settings/authz-provider` and canonical `PATCH /api/instance/settings/authz-provider` accept either active setup-secret authority or authenticated instance-admin authority. The PATCH route stores the selected runtime authorization provider.
- Anonymous setup endpoints and exact canonical auth/authz GET/PATCH requests carrying `X-Setup-Secret` share one IP-keyed `setup:{ip}` fixed window, which defaults to 5 requests per 60 seconds. Authenticated endpoints that require both normal authorization and `SetupSecretRequired` (`PATCH /api/instanceonboarding/profile` and `POST /api/instanceonboarding/complete`) use a separate IP-keyed `setup-authenticated:{ip}` window with the same configured limit, so anonymous setup traffic cannot exhaust the instance-claim budget. All four provider operations declare typed `429 ProblemDetails`. The named `SetupSecret` policy and the setup-secret branch of `Write` use `NoLimiter`, so they don't create duplicate quota state. Bearer-authenticated GET requests without a setup-secret header do not enter either setup bucket, while bearer PATCH requests without setup-secret authority remain separate under the per-user `Write` policy. Setup-secret authority fails closed when setup mode is inactive.
- Authorization update flow permits exactly one active provider: local RBAC or Cerbos. Cerbos endpoint changes are verified before the setting is applied.
- If Cerbos is selected and unavailable, authorized requests fail closed. Recovery is an explicit operator action: switch the authorization provider setting back to local RBAC; the runtime does not silently fail over.

If enrichment fails, authentication still continues and server-side authorization remains authoritative.

## Security Headers (API)

`SecurityHeadersMiddleware` adds defensive headers to every response:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` |

Non-GET responses additionally receive `Cache-Control: no-store` and `Pragma: no-cache` to prevent caching of mutation responses.

## CORS Policies

Five CORS policies are configured in `Program.cs`:

| Policy | Origins | Methods | Credentials | Use Case |
|---|---|---|---|---|
| `InternalAppPolicy` | Configurable | All | Yes | Internal app communication (BFF ↔ API) |
| `ExternalAppPolicy` | Configurable | Specific set | No | External API consumers |
| `InternalWebsitePolicy` | Configurable `Cors:AllowedOrigins` with default fallback | All | Yes | Internal website |
| `ExternalWebsitePolicy` | Configurable | `GET`, `OPTIONS` only | No | External read-only |
| `DevPolicy` | All origins | All | No | Development only |

## External API Keys

Non-interactive callers (direct API consumers, integrations, automation) authenticate with long-lived `X-API-Key` credentials instead of JWT bearer tokens. The security model is designed so credential material and principal authority are strictly separated.

### Credential / Principal Separation

- **Credential material** is a `{keyId}.{secret}` pair. The `keyId` is a stable, indexable identifier; the `secret` is a high-entropy random value that is *never* stored in plaintext.
- **Principal authority** is derived from the key row (`OwnerType`, `OwnerId`, `TenantId`, `Scopes`), **not** from the credential itself. Rotating a secret does not change authority. Revoking or reissuing a key does not change the owner identity.
- The credential is a lookup-and-verify token. The principal is reconstructed from the stored row on every request.

### Hashing

- Secrets are stored as SHA256 hashes in `ExternalApiKey.SecretHash`. The full plaintext is never persisted.
- Verification recomputes the SHA256 hash and uses `CryptographicOperations.FixedTimeEquals` to reduce timing-oracle risk.
- The raw `{keyId}.{secret}` value is returned once at creation time; losing it requires revoking and issuing a replacement key.

### Revocation And Replacement

- Current API surfaces support creating keys, updating key policy, and revoking keys. The lookup table contains a `PendingRotation` status for future overlap workflows, but the inspected API surface does not expose a dedicated rotate endpoint.
- Secret values are returned **once only**, at creation time, in the HTTP response body. The secret is then discarded server-side and cannot be re-derived.
- Clients that lose a secret must revoke the old key and issue a replacement — the platform cannot recover it.

### Raw Key Logging Prohibited

- The `X-API-Key` header value must never appear in logs, traces, or metrics.
- `ILogger` calls inside `ApiKeyAuthenticationHandler` log only `keyId` and outcome — the secret segment is discarded after parsing.
- Business metrics (`explore.external_api_keys.authentication_attempts`) tag `tenant_id`, `owner_type`, and `outcome`, but never the credential.
- Correlation IDs and request logs redact the `Authorization`/`X-API-Key` headers before emission.

### Tenant Isolation

- API key rows are tenant-scoped (`TenantId` FK) except for `InstanceAdmin` keys, whose credential row is nullable because it belongs to the platform operator rather than one tenant. Every non-auth query applies the `Tenant` query filter.
- API-key auth lookups are the **only API-key path** permitted to bypass the tenant filter — narrowly scoped to `GetByKeyIdForAuthentication` via `IgnoreTenantFilter`.
- `ApiTenantPostAuthenticationMiddleware` enforces that the API-key `TenantId` matches the resolved request tenant. Mismatches return `404 Not Found` (not `401` — to avoid leaking tenant existence).
- `InstanceAdmin` API keys do not implicitly make tenant-scoped API/MCP execution tenantless. If the request carries an explicit tenant hint, post-auth middleware binds that tenant for the request. If a tenant-scoped API or MCP request has no resolved tenant, it fails closed with `404` and `code=tenant_required`. Only explicit host-administration API routes may continue without tenant context.
- Tenant user authority is rooted in `TenantUserRoleGrant`, which must reference a matching `(TenantId, TenantUserId)` pair and a tenant-scoped role. Effective tenant-admin checks also require the linked `TenantUser` to be active and not soft-deleted.
- Organization membership reads are administrative, identity-bearing resources. `OrganizationMemberDto` includes tenant, organization, user, email/name, role, and position data; list/detail routes therefore require authenticated `islamuevent_organization_member:view` authorization and are denied to regular authenticated users. Do not reuse this DTO for public organization profile display; add a separate safe public projection if product requirements later need anonymous member/profile data.
- Footer management writes are tenant-administration actions. The API must resolve the current user and current tenant before dispatch, then footer link-group/link/reorder/settings commands authorize as `ResourceKinds.Tenant` with `AuthorizationActions.Update` and tenant attributes. A missing actor fails as authentication required; a signed-in user without tenant-admin or instance-admin authority receives `403 Forbidden`.

### Scope Model

- Each key holds an explicit `Scopes` set (e.g., `events:read`, `admin:tenant`, `admin:instance`).
- Scopes are bounded by the owner type (`ExternalApiKeyScopeCeiling`): a `User`-owned key cannot hold `admin:tenant`, a `Tenant`-owned key cannot hold `admin:instance`. Attempts to create or update a key with out-of-ceiling scopes are rejected at validator level.
- Authorization evaluators apply scope gates before any owner-authority check (see `MachineScopeMapping.ScopesPermit`). A key with `events:read` alone cannot perform mutations regardless of owner authority.
- MCP scopes are deliberately narrow: `mcp:read` permits generic MCP read discovery, while private event-management MCP reads also require the existing event read scope gate (`events:read`, `events:write`, or tenant/admin equivalent accepted by `MachineScopeMapping`). `mcp:propose` is required for MCP proposal tools/prompts and permits proposal creation without granting event write, event confirmation, or arbitrary user-write authority. SDK authorization filters hide event-management reads from API keys that only have `mcp:read`, hide proposal tools from API keys that lack `mcp:propose`, and MediatR authorization still fail-closes the call path.

### Machine Principal

- `IMachinePrincipalAccessor` exposes the parsed `ApiKeyPrincipalContext` to both authorization providers for a uniform decision path.
- Cerbos principals synthesize `isInstanceAdmin`/`tenantMemberships`/`orgMemberships` from owner type; the local `FallbackAuthorizationService` applies symmetric logic so both backends emit identical decisions for identical calls.
- A machine principal never receives admin-enrichment claims (`is_system_admin`, `is_tenant_admin`, etc.) — those are reserved for interactive user flows. All authority derives from owner type + scopes.

## HATEOAS Authorization

The HATEOAS link generation system is authorization-aware:

1. **`HateoasAuthorizationEvaluator`** performs batch permission checks for all links in a response.
2. Static checks (authentication, role requirements, condition lambdas) run first.
3. Remaining links with `PermissionResourceKind` are batched into a single `IsAllowedBatchAsync()` call. Link identity includes resource kind, resource id, action, optional scope, and canonicalized attributes.
4. On batch authorization failure, all permission-bound links are **denied** (fail-closed).
5. Admin/sync controllers that manually build HAL responses run definitions through the same evaluator before materializing links.
6. Clients never see links they cannot execute and must not recreate action gates from local roles or claims.

Event moderation links follow the same rule. The active moderation affordances are `moderate-light`, `moderate-heavy`, and eligible `unmoderate`; clients must render them only from HAL `_links`, never from local admin-role checks. Instance and tenant administrators can receive moderation links without receiving event edit/update/delete links. Heavy redaction is irreversible, redacts event-owned text, detaches event images, deletes provider-backed objects through the storage abstraction, and sends generic attendee notifications without event identity. Unmoderation is limited to the latest reversible light-moderation record.

Moderated events remain hidden from public discovery and exact public event URLs. Authorized management detail, actor-profile management lists, and moderation-history reads use the event `view-management` action. The moderation-history API and moderation telemetry are safe metadata surfaces only: they must not include original titles, descriptions, slugs, URLs, image identifiers, object keys, storage paths, bucket names, provider endpoints, raw provider errors, or arbitrary moderator free text.

### Reporting Intake Governance

Tenant reporting-intake reads expose an effective policy, including source,
instance-lock state, publication-safety state, and HAL relations. Browser
clients must require the server-authored `edit` relation before offering a
mutation. They must not reproduce authorization from roles or claims and must
not calculate whether publication policy makes disablement safe.

The update command re-evaluates instance locks and publication safety on the
server. This closes the time-of-check/time-of-use gap between rendering an
editable control and submitting a change. The administration UI reloads the
effective policy after both successful and rejected updates, maps 401 to a
reauthentication instruction, maps 403 to an access-denied message, and never
renders downstream response bodies.

Disabling reporting intake does not disable external provider routing, delete
existing reports, or suppress independent correction, legal, or copyright
contact channels. Product copy must preserve that distinction without making
legal or religious guarantees.

Related authorization references:

- [AUTHORIZATION.md](AUTHORIZATION.md) — provider model, resource checks, and fallback behavior.
- [AUTHORIZATION_PATTERNS.md](AUTHORIZATION_PATTERNS.md) — handler/request authoring patterns.
- [API.md](API.md) — API authentication, API-key routing, and error contracts.
- [BLAZOR.md](BLAZOR.md) — BFF proxy/token/setup-secret boundaries.

## Row-Level Security (RLS) — Prototype Support

**Status:** Prototype tenant-session infrastructure exists; production table policies are not enabled yet.

**Current tenant isolation:** EF Core named query filters (`HasQueryFilter(name: "Tenant", ...)`) and tenant-safe database foreign keys are the current production enforcement layers. EF tenant filters now fail closed when `TenantContext` is missing; approved system/admin paths must opt in through an explicit bypass reason. RLS is still defense-in-depth work, not the authority for application authorization.

**Implemented prototype pieces:**
- `Explore.Persistence/Security/PostgresTenantSessionInterceptor.cs` sets PostgreSQL session setting `app.current_tenant_id` with `set_config(..., false)` whenever EF Core opens a connection.
- Runtime registration is disabled by default and guarded by `Persistence:EnableRlsTenantSession`.
- `Event.Persistence.IntegrationTests/TenantIsolation/PostgresTenantSessionRlsPrototypeTests.cs` proves a forced RLS policy filters tenant A, tenant B, and missing-tenant access through a non-superuser app-style role.
- No production migration currently enables RLS on tenant tables.

**Why RLS matters for defense-in-depth:**
- Direct database access (migrations, reporting, debugging, data exports) bypasses EF query filters.
- A compromised application layer could disable filters and leak cross-tenant data.
- PostgreSQL RLS adds kernel-level row filtering that cannot be bypassed from SQL.

**Policy pattern proven by the prototype:**

```sql
CREATE POLICY tenant_isolation ON events
    USING (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid)
    WITH CHECK (tenant_id = NULLIF(current_setting('app.current_tenant_id', true), '')::uuid);
```

The `missing_ok=true` form plus `NULLIF(..., '')` makes absent tenant context fail closed instead of raising a cast error. The interceptor sets an empty string when `ExploreDbContext.TenantContext` is missing or returns `Guid.Empty`.

**Production rollout prerequisites:**
1. Use a non-superuser, non-`BYPASSRLS` application database role. PostgreSQL superusers always bypass RLS, even when a table uses `FORCE ROW LEVEL SECURITY`.
2. Keep migration/maintenance credentials separate from the runtime app role so migrations and operator maintenance can intentionally bypass RLS.
3. Audit all direct `IDbContextFactory<ExploreDbContext>` callers and system/admin paths before enabling policies on real tables; factory-created contexts do not automatically receive scoped property injection.
4. Enable RLS table families in bounded migrations with integration tests for tenant access, absent-tenant denial, cross-tenant denial, and required host-admin/system paths.
5. Apply first to high-value tenant tables such as events, event_sessions, organizations, groups, actors, event_registrations, storage_objects, audit_logs, notifications, configuration_change_logs, tenant_user_role_grants, tenant_setting_overrides, and tenant_settings_documents.

**Risks:**
- Connection pooling: Session variables must be set every time EF opens a connection. Npgsql resets pooled connection state on close by default, and the interceptor rebinds the tenant on open.
- Role design: A superuser or `BYPASSRLS` runtime connection makes policies ineffective.
- System/admin reads: cross-tenant maintenance paths need explicit role/session design before real table policies are enabled.
- Performance: RLS adds a predicate to every query. Indexes on `tenant_id` (already exist) mitigate this.
- Migrations: Must run with a maintenance role that bypasses RLS intentionally.

## Ticketing Recovery Trust Boundary

Ticketing recovery is deployment/operator authority, never request-body,
tenant-header, browser, scheduler, or restored-row authority. The accepted
manifest is tenant-qualified and binds release/schema, checkpoint/object
cutoff, retained key version, authority/provider/idempotency floors, worker
fence, and bearer generations. A digest identifies exact replay; the HMAC key
remains in Infisical or environment under
`ticketing.recovery_manifest_hmac_key`.

Recovery starts `RecoveryOnly`. Validation cannot open writes. Pre-restore
recovery capabilities are cancelled, active admission credentials are revoked,
and digest-free reissue intents commit in the same serializable local
transaction before worker/sales reopening. In-flight provider work becomes
`Unknown`; stale fences cannot resolve it. Operators must supply authoritative
provider evidence before retry or dead-letter. Provider I/O never occurs inside
the recovery transaction.

Health is deliberately fixed-cardinality and PII-free. Operator actions require
the existing authenticated instance-administration boundary and are advertised
only by server HAL affordances when their state transition applies. HAL does
not authorize the mutation: server policy, exact tenant/operation identity,
state, and fence are revalidated. Direct SQL, blind replay, copied tenant
cursors, and synthetic key/fence/idempotency facts are unsupported.

## Configuration-manifest trust boundary

`ConfigurationManifest` is a startup-only instance administration input, not a
browser authority document. Only the configured owning host reads the bounded
regular file. The contract closes every object, resolves the current instance
from trusted server context, and applies the instance section before tenant
sections. A tenant entry cannot select another instance or write instance,
provider, topology, secret, PII, or sovereign-payment state.

`ValidateOnly` performs no writes. `Bootstrap` performs preflight before opening
one serializable transaction, acquires canonical instance and tenant mutation
locks, writes configuration plus value-free outcome evidence, and commits
durable post-commit effects atomically. Existing tenant bootstrap results are
wholesale skips; the feature is intentionally not a continuous desired-state
reconciler. Failed validation, concurrency, persistence, or effect staging does
not authorize partial state.

Whole-instance export requires explicit instance/Control Plane authorization.
The API resolves the current instance, emits at most 4 MiB, and excludes
credentials, secret bindings, provider accounts, topology, personal data, and
sovereign payment operations. Browser actions exist only when the control-plane
overview emits the matching HAL relation; the authenticated BFF revalidates the
relation and invokes a fixed generated API operation without exposing bearer
tokens or following a browser-provided URL.

## Configuration-manifest payment authority

The paid-policy manifest extension is a Tier 0 financial boundary. It accepts
one strict `tenant.paid_event_policy` document containing only non-secret
tenant narrowing and an expected active instance-policy version. The validator
closes the JSON object, rejects tenant IDs and unknown members, constructs the
Domain policy, and proves it cannot broaden the instance ceiling before any
write.

CQRS and manifest mutations share `PaidEventPolicyMutationBoundary`.
Authenticated commands enter a serializable transaction and acquire canonical
instance/tenant named locks. Manifest bootstrap acquires those same keys in its
outer transaction and calls the in-transaction path. A stale instance revision
or tenant-policy collision fails as a concurrency conflict; tenant, settings,
branding, policy, outbox, and audit state roll back together.

The API returns typed authority facts for the active instance revision,
inherited versus tenant-narrowed effective values, the manifest-compatible
field taxonomy, and the sovereign-locked taxonomy. These facts are
explanatory only. HAL `_links.edit` remains the sole browser action capability;
the Blazor client never derives edit permission from authority facts, roles,
claims, or local policy inspection.

Operator identity, official status/origin, provider profiles and credentials,
connected accounts, charge type, buyer acceptance/PII, sale control, provider
handoff, reconciliation, disputes, liability, negative balances, and refund
execution never enter the manifest payload. Export metadata names those
boundaries but omits their values. Logs and telemetry must not record supplied
manifest values, secrets, buyer data, or provider payloads.
