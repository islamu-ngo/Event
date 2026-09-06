ABOUTME: Contributor guide for the Blazor BFF host and client application boundaries.
ABOUTME: Keeps token handling, proxying, render policy, service state, and client generation source-grounded.

# Blazor Frontend Architecture

> **Audience:** Contributors | Frontend | AI agents
> **Status:** Implemented
> **Owner:** Frontend
> **Last Verified:** 2026-08-15
> **Source Anchors:** `Explore.Blazor/Program.cs`, `Explore.Blazor/Hosting/`, `Explore.Blazor/Extensions/`, `Explore.Blazor/Services/InProcessEventApiTransport.cs`, `Event.Standalone/Program.cs`, `Event.Standalone/Middleware/CombinedApiBridgeMiddleware.cs`, `Event.Web.BffHosting/Security/EventBffRequestEnricher.cs`, `Explore.Blazor/Components/ControlPlane/`, `Explore.Blazor.Client/Explore.Blazor.Client.csproj`, `Explore.Blazor.Client/Components/Discovery/`, `Explore.Blazor.Client/Services/`, `Explore.Blazor.Client/Services/EventPromotionService.cs`, `Explore.Blazor.Client/Services/RegistrationOrderService.cs`, `Explore.Blazor.Client/Layout/`, `Explore.Blazor.Client/Pages/Studio/StudioEventPromotions.razor`, `Explore.Blazor.Client/Pages/Registration/OrderRecovery.razor`, `Explore.Blazor.Client/Pages/Registration/GuestOrderRecovery.razor`, `Explore.Blazor.Client/Pages/Admin/Instance/`, `docs/RENDER_POLICIES.md`, `docs/DESIGN_SYSTEM.md`

## Scope

This document is the contributor-facing guide for `Explore.Blazor` and `Explore.Blazor.Client`. It focuses on the BFF trust boundary, proxy behavior, embedded control-plane shell, render-policy consumption, service/state patterns, and generated API client workflow.

Use the specialized docs for deep detail:

| Area | Canonical doc |
|---|---|
| API contract, HAL, idempotency | [API.md](API.md) |
| Task-first API usage | [API_COOKBOOK.md](API_COOKBOOK.md) |
| Auth/security model | [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md) |
| Render policy and public SEO behavior | [RENDER_POLICIES.md](RENDER_POLICIES.md) |
| Design tokens, wrappers, CSS layers | [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) |
| Accessibility rules | [ACCESSIBILITY.md](ACCESSIBILITY.md) |
| Localization stack | [LOCALIZATION.md](LOCALIZATION.md) |

## Project Roles

| Project | Role | Must not own |
|---|---|---|
| `Explore.Blazor` | Server/BFF host: OIDC login, HttpOnly cookie session, Split YARP proxy or Standalone in-process bridge, BFF endpoints, token forwarding, antiforgery, rate limiting. | Raw domain logic or browser token storage. |
| `Explore.Blazor.Client` | Razor UI, pages/components, embedded control-plane pages, scoped UI state, typed service layer, generated API DTO consumption. | API authorization decisions, raw access-token persistence, direct controller logic, or backend contract mirrors. |

The browser never owns access tokens. Interactive UI calls go through the BFF or generated client services; API remains the hard authorization boundary.

## Generated Client Record Ownership

`schemas/openapi_islamu-event.json` and `src/Explore.Blazor.Client/nswag.json` remain the canonical generated-client inputs. The `GenerateApiClient` MSBuild target runs pinned NSwag 14.6.3, fixes the known `void` response artifact, applies `eng/tools/Explore.GeneratedContracts`, and performs final byte normalization.

Use [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md) when changing record eligibility, mutable exclusions, AOT extension data, or generated-client construction.

- Plain generated response/value contracts are nominal record classes with init-only properties. Construct them with object initializers and create changed snapshots with `with`.
- Protocol input graphs, HAL resources, inherited contracts, `EventApiClient`, exceptions, file wrappers, and the reasoned mutable edit/service inventory remain classes.
- `[JsonExtensionData] AdditionalProperties` remains settable on records because System.Text.Json AOT populates extension data after construction.
- Generated records emit value-free diagnostic text; `ToString()` never enumerates PII, tokens, free text, or other member values.
- Mutable generated class names are explicit in `eng/tools/Explore.GeneratedContracts/mutable-generated-contracts.txt`; adding or removing an entry requires a consuming behavior test.
- Generated output is never hand-edited. Run `dotnet msbuild src/Explore.Blazor.Client/Explore.Blazor.Client.csproj -target:GenerateApiClient -property:Configuration=Release`, then the focused client and architecture tests.

## Hosting Topology: Split and Standalone

`Explore.API`, `Explore.Blazor`, and `Event.Standalone` are the three application composition roots. AppHost defaults `Hosting:Topology` to `Split`; `Hosting:Topology=Standalone` is an explicit opt-in that starts `Event.Standalone`, not a different API contract or authorization model.

| Topology | Composition | Browser `/api/*` path | BFF-to-API typed clients |
|---|---|---|---|
| Split | `Explore.Blazor` and `Explore.API` run as separate hosts. | YARP forwards the request to the API host. | Normal outbound HTTP transport. |
| Standalone | `Event.Standalone` composes the reusable API and Blazor host modules in one process and on one listener. | The Combined bridge dispatches into the existing API pipeline in-process; YARP is not registered and there is no loopback/self-proxy. | `InProcessEventApiHttpMessageHandler` dispatches a synthetic request through the API pipeline. |

The Standalone topology keeps one API endpoint graph: controller routes, API versioning, authentication schemes, authorization, rate limits, and API middleware remain API-owned. The Blazor `Combined` profile omits only Split transport concerns (YARP and remote-API readiness); it retains the BFF, OIDC session, Razor/static assets, SignalR, render policy, antiforgery issuance, and BFF endpoint graph.

Version and boundary invariants stay fixed across both topologies: `Explore.API` continues to own `/api/*` behavior, including route policy and version parsing from the `Accept` media-type parameter, `?api-version=`, or `X-Api-Version`. Standalone does not add URL-version segments or alternate API policy.

Operationally, AppHost selects Standalone for local topology runs; direct `Event.Standalone` launch profiles remain available for development. `docker-compose.yml` remains Split-only, and no standalone Docker descriptor exists yet; a Standalone topology selection does not set the primary provider to SQLite.

### One-process bridge and cookie-to-API token flow

An authenticated browser request to `/api/*` in Standalone crosses an explicit in-process trust boundary:

1. `CombinedApiBridgeMiddleware` classifies the BFF cookie session. A request without that session continues to the normal API `MultiAuth` selection, so external bearer-token and API-key clients keep their existing behavior.
2. For a valid BFF session, the bridge validates antiforgery for unsafe requests before it forwards anything. It resolves the server-held access token, trusted tenant hint, setup secret, and support-access session through `EventBffRequestEnricher`.
3. Browser-supplied `Authorization`, API-key, setup-secret, tenant, support-access, cookie, and unsafe correlation headers are removed. The bridge adds only the trusted server-derived values.
4. The cookie principal is cleared before the API pipeline runs. The API authenticates the reconstructed bearer token through `MultiAuth`; the BFF cookie is never the API principal.

This cookie-to-API token conversion fails closed: a valid BFF cookie without a usable server-held access token receives `401` (apart from the documented anonymous onboarding/setup paths), rather than falling through as the cookie identity or an attacker-supplied credential.

`InProcessEventApiHttpMessageHandler` is the separate server-side bridge used by generated `IEventApiClient` services in the Combined profile. It creates an isolated HTTP context and service scope, copies HTTP semantics and permitted headers, but does not copy browser cookies, `Host`, or an ambient principal. It is an in-process transport implementation, not permission to bypass API authentication or Application authorization.

### Antiforgery boundaries and Standalone limits

Both Topology modes issue `XSRF-TOKEN` from the BFF and require unsafe browser API requests to send it as `X-CSRF-TOKEN`. Split performs this check at the BFF proxy boundary; Standalone performs the same check in the Combined bridge before entering the API pipeline. Direct API bearer-token or API-key callers do not have a browser cookie boundary and are not subject to BFF antiforgery.

Standalone couples UI and API availability, deployment, and scaling to one process. Choose Split when independent host scaling, independently deployed failure domains, or a network boundary between BFF and API is required. Combined removes the network hop and YARP proxy diagnostics; it does not relax token secrecy, header sanitation, API authorization, HAL affordance gating, or the requirement to keep browser tokens out of client storage.

The three application composition roots use the same contract: AppHost defaults to Split, while explicit Standalone uses `WithHttpEndpoint(name: "http")` for dynamic/non-guaranteed internal HTTP and explicit HTTPS `https://localhost:7180`; direct `Event.Standalone` launch profiles reserve `http://localhost:5180`. AppHost waits for migration completion and selected infrastructure, then `Event.Standalone` owns Combined startup/readiness once. Revert by selecting Split again; this does not roll back data. Standalone neither changes the SQLite provider choice nor adds `docker-compose.yml`; API routes remain `/api/...` with non-URL API versioning, never `/api/v1/...` (see [the support matrix](ARCHITECTURE.md#hosting-topology)).

## Render Mode Boundary

`Explore.Blazor` may keep its render-policy-controlled public app behavior, including the existing configurable Interactive Server, WebAssembly, or Auto paths documented in [RENDER_POLICIES.md](RENDER_POLICIES.md).

Configured admin hosts render `Explore.Blazor/Components/ControlPlane/EmbeddedControlPlaneRoutes.razor` inside the same BFF. The embedded shell uses the host-selected effective render mode and the generated client contracts in `Explore.Blazor.Client`; it does not introduce a separate host, project, or backend dependency.

Control-plane UI primitives live under `Explore.Blazor.Client/Components/ControlPlane/`. They follow the same MudBlazor, token, CSS-isolation, and HAL affordance conventions as the rest of the client.

The embedded instance console owns the public tenant-plan workflows. `/admin/instance/plans` creates structured plan drafts and `/admin/instance/plans/{key}` creates or edits version drafts, validates drafts, previews setting diffs, publishes or archives versions with typed confirmation, and clones published versions. Version lifecycle relations live on each `versions[]` resource, never on the root plan. `/admin/instance/tenants/{tenantId}/configuration` switches published plan assignments and applies or rolls them back with typed confirmation; rollback uses the separately returned eligible previous assignment. Every mutation is exposed only from the matching server-emitted HAL relation and matches the resource identifier in the advertised link before rendering or dispatch.

The existing control-plane overview provider grid also renders one server-derived
secret-authority card. Its contract is limited to provider name, bounded status,
and remediation code; it never exposes values, binding identifiers, paths, keys,
URLs, or provider diagnostics. No secret mutation control is rendered because the
overview advertises no such HAL action; deployment-owned recovery stays in the
operator runbook.

Domain inventory remains an Event-owned read model. The domain page follows its HAL `settings` or `edit` relation into `/settings/instance?section=domain`; DNS-provider verification and certificate probing are operator-managed because Event does not expose verification, test, or retry endpoints for that resource.

Whole-instance configuration export lives at
`/settings/instance?section=configuration-manifest` in both deployment modes.
The section explains that the file covers instance configuration and all active
tenants, omits secrets and operational/sovereign records, and is bootstrap
configuration rather than a data backup. Overrides and portable download
actions render only from the matching control-plane overview HAL relations.
The tenant settings navigation never exposes this instance-authority action.

## Public Home Discovery Boundary

`/home` renders the same discovery composition for anonymous and authenticated visitors unless the existing organization-centric shell branch is authoritative. `HomeDiscoveryExperience` owns one persisted `HomeDiscoveryDto`, so PublicSeo prerendering can hydrate without issuing a duplicate discovery request. The obsolete standalone marketing page has been removed.

`HomeDiscoveryService` is the only client orchestrator for `GET /api/public-experience/home`. It resolves URL context before saved user context, sends only `areaId` and `mode`, and persists only `home_discovery.area_id` and `home_discovery.mode`. Area names and centroids come from the server-owned coarse public area DTO; generic location/address DTOs are not consumed.

Browser geolocation is requested only from the explicit “Use my current location” action. The browser compares the one-shot, low-accuracy result with configured coarse centroids, immediately reduces it to an area ID, and never sends or stores the origin. The BFF `Permissions-Policy` is `geolocation=(self)`; camera, microphone, and payment remain disabled. Online mode preserves the selected area so returning to area discovery does not require another location request.

The composite response owns section truth, ordering, and failure status. Each semantic section evaluates inventory independently, so an event can correctly appear in the hero and in every matching upcoming, online, curated, or recent section. The UI uses the manual `HeroCarousel`, the dedicated link-based `UpcomingEventList`, the production `EventCard` for spotlight and compact rails, and native `EventHorizontalRail`; edit/delete affordances remain HAL-gated. Upcoming returns at most 18 items so responsive layouts can expose one, two, or three six-item columns; other standard sections return at most 10 items. Up to two explicit curated rails are included, each section has a one-second budget, and the full composition has a three-second budget.

## BFF Endpoint Families

BFF endpoints are split by concern in `Explore.Blazor/Extensions/` and wired through the server host.

| Endpoint family | Representative routes | Source |
|---|---|---|
| Authentication | `/auth/challenge`, `/auth/login`, `/auth/signout`, `/auth/status`, `/auth/providers`, `/auth/debug`, `/signin-atproto` | `BffAuthEndpoints.cs` |
| Auth refresh support | `/bff/auth/refresh-schemes`, `/bff/auth/refresh-session`, `/bff/auth/refresh-session/internal` | `BffAuthEndpoints.cs` |
| AT Protocol OAuth publication | `/oauth/client-metadata.json`, `/oauth/jwks.json` | `AtprotoOAuthEndpointExtensions.cs` |
| User/session view | `/bff/me` | `BffPreferenceEndpoints.cs` |
| Preferences and appearance | `/bff/theme`, `/bff/language`, `/bff/direction`, `/bff/ui-themes`, `/bff/appearance/*` | `BffPreferenceEndpoints.cs` |
| White-label manifest | `/manifest.webmanifest` | `BffManifestEndpoints.cs` |
| Configuration manifest export | `/bff/control-plane/configuration-manifest/export` | `BffConfigurationManifestEndpoints.cs` |
| Setup secret | `GET/POST/DELETE /bff/setup-secret`, `/bff/setup-secret/sync` | `BffSetupSecretEndpoints.cs` |
| Storage upload proxy | `/bff/storage/upload-session`, `/bff/storage/upload-proxy` | `BffStorageEndpoints.cs` |
| Registration provider embed host | `/bff/registration-provider-embed/tenants/{tenantId}/events/{eventId}/workflows/{workflowId}/requirements/{requirementId}/channels/{channelId}/bindings/{bindingId}` | `BffRegistrationProviderEmbedEndpoints.cs` |
| Support access | `/bff/support-access/current`, `/bff/support-access/sessions`, `/bff/support-access/sessions/current/stop`, `/bff/support-access/tenants/{targetTenantId}/sessions`, `/bff/support-access/tenants/{targetTenantId}/sessions/{sessionId}/audit-events`, `/bff/support-access/sessions/{sessionId}/force-stop` | `BffSupportAccessEndpoints.cs` |

Keep new BFF endpoints in the smallest matching extension file. `BffEndpointExtensions.cs` should remain the facade/orchestrator, not a dumping ground for endpoint logic. `AntiforgeryEndpointExtensions.cs` provides `ValidateAntiforgery()` request-token validation filters for state-changing BFF endpoints. The manifest endpoint resolves public-experience branding through the server-side API client and falls back to generic install metadata when branding cannot be read; do not reintroduce a static tenant-branded manifest file.

Configuration-manifest download is an authenticated, safe `GET` through the
fixed same-origin BFF route. Immediately before forwarding, both the client
adapter and BFF reload the control-plane overview and require the exact
`export-configuration-overrides` or `export-configuration-portable` HAL
relation. The BFF never follows a browser-provided URL or HAL href, calls only
the generated canonical API operation, buffers at most 4 MiB, and accepts only
the exact manifest media type and per-view filename. Responses are `no-store`;
downstream failures preserve safe status codes without forwarding provider
bodies. Access tokens and raw API authority remain server-side.

Registration-provider embeds are BFF-mediated only. The client builds only the same-origin BFF URL from the server lineage; the BFF fetches the launch descriptor from the API, rejects query strings, lineage mismatch, unavailable descriptors, non-embed modes, non-HTTPS targets, and local/private literal hosts, then emits a per-route CSP with exact `frame-src`. The iframe is sandboxed and paired with a new-tab fallback. `RegistrationProviderLaunchState` treats iframe load/navigation as display status only; completion authority is the polled order/requirement status from API/HAL resources.

The Studio integrations route is `/studio/events/{eventId}/integrations`. It renders when the event resource contains `manage-registration-channels` or `view-registration-provider-health`; health-only access hides mutation controls. Connection, origin, binding, mapping, publish, channel, manual-import, poll, retry, and resolve actions are dispatched only through the matching HAL links and checked again by `RegistrationProviderIntegrationService` before the generated client call.

## Proxy, Bridge, And Token Forwarding

There are two related but separate transport paths:

1. **Browser-to-API path** — in Split, `YarpProxyExtensions.cs` proxies `/api/*` calls; in Standalone, `CombinedApiBridgeMiddleware` applies the same enrichment in-process. Both paths:
   - forwards the server-side access token as `Authorization: Bearer ...`,
   - forwards trusted tenant context when route context is available,
   - strips any browser-supplied `X-Setup-Secret` value and forwards only the value returned by `ISetupSecretResolver`,
   - strips browser-supplied `X-Support-Access-*` values and forwards only the BFF-owned support-access session id when an active actor-bound session is stored server-side.
2. **Server-side typed client path** — `HttpClientExtensions.cs` registers outgoing clients with separate handlers:
   - `AccessTokenForwardingHandler`,
   - `TenantHeaderForwardingHandler`,
   - `SetupSecretForwardingHandler`,
   - `SupportAccessForwardingHandler`,
   - `BffCookieForwardingHandler` for self/BFF calls that must preserve cookie/XSRF context.

All server-side handlers use `UseCookies = false` where applicable to avoid pooled `CookieContainer` leakage between requests.

## Dedicated Admin Host Classification

Dedicated control-plane hostnames are static BFF configuration, not tenant database routing state. Configure exact hosts or origins under `Bff:AdminHosts`, for example `admin.example.org` or `https://admin.example.org`. Wildcards are invalid because admin hosts must not overlap tenant subdomains or tenant custom domains.

Host classification runs against `HttpContext.Request.Host` after `UseEventBffForwardedHeaders()` applies trusted forwarded headers. The tenant subdomain and custom-domain resolvers skip configured admin hosts, so `admin.example.org` is not resolved as tenant slug `admin` under `example.org` and is not looked up as a tenant-owned custom domain.

Optional admin-host IP allowlisting is configured with `Bff:AdminHostAllowedIpRanges` using exact IP addresses or CIDR ranges, for example `203.0.113.10` or `203.0.113.0/24`. When the allowlist is set, admin-host requests fail closed with `403` if the remote address is missing or outside the configured ranges. Public and tenant hosts are not affected by this allowlist.

Supported now: exact admin-host classification, optional IP/CIDR allowlisting, server-side BFF cookies, and the existing CSP/frame protections. Deferred: per-admin-host cookie domain/name switching, a separate mutation rate-limit policy, and per-admin-host CSP variants. MFA for instance administrators should be enforced by the identity provider policy rather than by browser-side checks.

## Setup Secret Boundary

Setup-secret handling is intentionally BFF-owned:

1. The browser sees only BFF-mediated cookie/session state, not the raw setup-secret persistence model.
2. Setup-secret forwarding uses `ISetupSecretResolver` with this trusted source order: BFF-owned setup handshake/session state, protected BFF-issued setup cookie, then explicit local/development/bootstrap configuration fallback. Inbound request headers are never a setup-secret source.
3. The setup cookie is protected with time-limited ASP.NET Core Data Protection, `HttpOnly`, invalidated by the BFF setup-secret endpoints, and `Secure` outside local development. The cookie and server-side entry use a 30-minute rolling inactivity timeout refreshed by successful status and synchronization calls.
4. `SameSite=Lax` is intentional because onboarding may cross top-level OIDC redirects before the first administrator completes setup.
5. Setup-secret validation is rate-limited at the BFF edge and again at the API edge.
6. The BFF limiter partitions requests by authenticated user when available, then antiforgery/session cookie state, then IP as the final fallback.

When debugging onboarding, check both BFF setup-secret endpoints and API setup-secret validation rather than adding client-side storage shortcuts.

## Onboarding UI Contract

`/setup` is the dedicated pre-authentication operator gateway and renders through `SetupLayout`, separate from authenticated application/admin navigation. After provider authentication, the setup experience becomes one task overview composed from existing server onboarding-status, provider-status/sync, and preflight services.

Contributor rules:

1. The task list and completion state are server-derived. Refresh/retry calls the services again; components do not persist a competing workflow state machine in browser storage.
2. The reusable task component is display-only. It renders server status, warnings, blockers, remediation, and links; it does not calculate authorization, deployment mode, provider readiness, or completion. Completed tasks may retain management links: when the server exposes `manage-authentication`, the configured authentication-provider task opens `/onboarding/auth-provider` before launch and `/settings/instance?section=auth-providers` after launch so operators can diagnose, repair, or reconcile the Keycloak realm.
3. Deployment mode and dedicated BFF admin hosts are read-only deployment facts in this UI. They are not controls or onboarding choices.
4. Blocking preflight items disable launch. Ordinary warnings do not; a server-classified serious warning may expose an explicit acknowledgement control. The completion request remains idempotent and server-guarded.
5. On successful single-tenant launch, follow the server-confirmed events/instance-settings handoff. On multi-tenant platform launch, navigate to `/admin/instance`; do not require or synthesize a tenant. First-tenant creation/onboarding is a separate tenant-scoped flow.
6. All onboarding copy is resolved through `ITranslationService`. Preserve semantic headings, labeled controls, keyboard focus, and live status announcements through the existing accessibility announcer service for validation, refresh, provider verification, preflight, and launch outcomes.
7. Setup-secret authority, platform-admin authority, tenant context, tenant-admin authority, and provider readiness are server decisions. Never derive onboarding actions or handoffs from serialized claims or configured-credential flags; use server status and HAL links. Missing or errored authoritative task/provider state fails closed and must not produce a locally synthesized completion state or management affordance.
8. Access/refresh/provider tokens, setup secrets, provider administrator credentials, and raw provider responses never enter browser storage, browser-facing DTOs, logs, traces, or support diagnostics.
9. Authorization onboarding is one responsive column. Blank deployment intent renders native Local/Cerbos radio controls with Local selected and Cerbos behind a native `<details>` disclosure. Explicit Local and successfully reconciling/ready Cerbos skip the chooser from server status; final Cerbos failure opens a locked remediation view. Endpoint presence alone must not select Cerbos, and the browser must not start automatic verification or policy publication.
10. Authorization-page skipping is independent from authentication management. Deployment-provided Keycloak metadata or secrets never remove the pre-launch provider editor or the post-launch HAL-authorized admin route used for realm creation, diagnosis, repair, reconciliation, and secret rotation.

## Support Access Boundary

Admin support access follows the same BFF-owned trust model as tokens, tenant hints, and setup secrets:

1. Browser code never owns support-access authority claims, tenant role grants, target-tenant tokens, or a durable impersonated identity.
2. `POST /bff/support-access/sessions` starts a session through the API and stores only an opaque active-session reference in `IBffSupportAccessSessionStore`, keyed to the authenticated user and OIDC `sid`.
3. `GET /bff/support-access/current` rechecks the API current-session endpoint and refreshes or clears the BFF store. This lets the shell recover after a refresh without making persisted active sessions ambient authority for ordinary API calls.
4. `POST /bff/support-access/sessions/current/stop` resolves the active owned session from the store or API current-session endpoint, stops it through the API, and clears the BFF store on success.
5. `GET /bff/support-access/tenants/{targetTenantId}/sessions` and `GET /bff/support-access/tenants/{targetTenantId}/sessions/{sessionId}/audit-events` expose bounded history and audit evidence through the same API authorization/HAL pipeline used by the operator console and tenant evidence view.
6. `POST /bff/support-access/sessions/{sessionId}/force-stop` is an antiforgery-protected emergency revocation path. On success it clears the current BFF store entry when the revoked session is the actor's active session.
7. `BffProxyHeaderSanitizer`, YARP transforms, and `SupportAccessForwardingHandler` remove all inbound `X-Support-Access-*` values before adding a trusted `X-Support-Access-Session-Id` from server-side state.
8. The API validates that forwarded session id against persisted support-access state, actor identity, resolved tenant, expiry, mode, and governance settings on each request that asks for support context.
9. Blazor UI affordances still come from BFF-confirmed state and API/HAL `_links`, not from local roles or serialized claims. The global shell banner is a visibility/safety control, not an authorization source.
10. Tenant admins review support-access evidence from tenant settings through `TenantSupportAccessEvidenceSection`. The view is read-only, resolves the current tenant through the tenant onboarding status endpoint, and renders audit drill-in only when a session resource contains the `audit-events` HAL link.

## Dynamic Auth Scheme Management & AT Protocol OAuth

Dynamic authentication scheme management allows the server BFF host (`Explore.Blazor`) to register, configure, update, and remove authentication schemes (Keycloak OIDC, Google SSO, and AT Protocol OAuth) at runtime without requiring an application restart.

### Dynamic Scheme Architecture

1. **`DynamicAuthSchemeManager`**: Implements `IDynamicAuthSchemeManager`. It reads provider settings from API endpoints (`GetInstanceOnboardingAuthProviderConfigurationAsync`) and environment variables (`Keycloak:Authority`, `Google:ClientId`, etc.).
2. **Startup & Setup Hydration**: At initial startup, environment-configured providers are registered first. Post-onboarding configuration or setup secret synchronization calls `RefreshSchemesAsync(setupSecret)` to reload settings from the backend API.
3. **Scheme Registration & Invalidation**: Dynamically updates cached `OpenIdConnectOptions` in `IOptionsMonitorCache<OpenIdConnectOptions>` and manages active schemes in `IAuthenticationSchemeProvider`.
4. **Scheme Refresh Endpoint**: `/bff/auth/refresh-schemes` allows authorized admin workflows or onboarding completion to trigger immediate scheme re-evaluations across the BFF.

### AT Protocol OAuth Boundary

AT Protocol authentication uses a custom authentication handler (`AtprotoAuthenticationHandler`) rather than standard OIDC:

1. **Client Metadata Publication**: `GET /oauth/client-metadata.json` publishes canonical client metadata (including client ID, HTTPS callback redirect URIs, scope `atproto transition:generic`, grant types `authorization_code` and `refresh_token`, and token auth method `private_key_jwt`).
2. **Public Key Set (JWKS)**: `GET /oauth/jwks.json` serves public keys from `AtprotoClientKeyProvider` for rotation-aware ES256 client assertion signing.
3. **PKCE & DPoP Tokens**: The flow enforces mandatory PKCE (`S256`) and DPoP (Demonstrating Proof-of-Possession) bound access tokens.
4. **Session Persistence & Identity Resolution**: `ApiBackedOAuthStateStore` and `AtprotoTenantSessionHandoffStore` share the private `ApiBackedAtprotoTransientStore`; the BFF has no database reference or alternate transient cache. `ApiBackedOAuthSessionStore` retains durable OAuth-session ownership, and `AtprotoIdentityCache` remains discovery-only.
5. **Browser Correlation**: `AtprotoBrowserProof` protects one fixed-expiry, host-only HTTPS cookie and derives an independent HMAC binding per flow. State/handoff adapters validate recovered origin, tenant and browser binding before candidate-bound consumption. Cross-origin canonical callbacks issue only opaque handoffs; the destination issues the first-party session cookie after proof validation. Proof is checked again immediately before cookie sign-in.

## Auth Diagnostic Boundary

Authentication challenge and OIDC callback failures are intentionally safe by default:

1. Server redirects back to `/login` include only `challengeError=1`, a normalized `errorCode`, and a correlation ID.
2. Raw IdP exception text, token endpoint response bodies, client IDs, client-secret prefixes, client-secret lengths, and `errorDetail` query values are not browser-visible.
3. BFF logs use safe structured fields from `ISafeAuthDiagnosticsPolicy`; provider response bodies and secret-derived metadata stay out of production-path logs.
4. The login page renders a generic failure message and provider choices. It must not render legacy `errorDetail` query values if an old URL is reused.

## BFF Antiforgery Boundary

Cookie-authenticated BFF mutations use a double-submit-style antiforgery contract:

1. `UseAntiforgeryTokenMiddleware` issues a JavaScript-readable `XSRF-TOKEN` cookie on non-static `GET` requests by calling `IAntiforgery.GetAndStoreTokens`. Static assets bypass token issuance so ASP.NET antiforgery does not mark images, scripts, and styles `no-store`.
2. `Program.cs` configures ASP.NET Core antiforgery to validate the `X-CSRF-TOKEN` request header.
3. `BrowserCredentialsMessageHandler` sends browser credentials, and `BffAntiforgeryMessageHandler` adds `X-CSRF-TOKEN` for `POST`, `PUT`, `PATCH`, and `DELETE` requests when the token cookie is present.
4. `BffCookieForwardingHandler` preserves cookie/XSRF context for InteractiveServer self-calls that legitimately call BFF endpoints from the server.
5. Unsafe preference and appearance BFF endpoints must call `.ValidateAntiforgery()`. Missing or invalid tokens return `400` with `Antiforgery validation failed`.
6. Positive protected examples include `/bff/auth/refresh-schemes`, `/bff/auth/refresh-session`, `/bff/support-access/sessions`, `/bff/support-access/sessions/current/stop`, `/bff/support-access/sessions/{sessionId}/force-stop`, `/bff/storage/upload-session`, `/bff/storage/upload-proxy`, and the preference/appearance mutation endpoints.
7. InteractiveServer storage-upload self-calls use a short-lived Data Protection protected `X-ISLAMU-BFF-SELF-CALL` token bound to the method, path, host, and authenticated user so they can pass the same endpoint filter without exposing browser tokens or setup secrets.
8. Documented exceptions are setup-secret bootstrap endpoints and `/bff/auth/refresh-session/internal`; these use separate credentials/authorization constraints because initial setup and server-side onboarding calls cannot reliably satisfy browser antiforgery semantics.

## Storage Upload Proxy Boundary

Browser-mediated storage uploads use BFF-owned upload sessions rather than caller-supplied destination URLs:

1. The browser asks `/bff/storage/upload-session` for an upload session. The BFF calls the provider-neutral API upload-session endpoint server-side and stores the approved session metadata in distributed cache.
2. The BFF accepts only exact, parameter-free `image/jpeg`, `image/png`, `image/gif`, or `image/webp` declarations with a matching simple `.jpg`/`.jpeg`, `.png`, `.gif`, or `.webp` file extension.
3. The browser receives an opaque `uploadSessionId` and expiry. It does not receive a raw provider object key, local path, or trusted upload destination.
4. The browser uploads bytes to `/bff/storage/upload-proxy` with `uploadSessionId`, `contentType`, and `file`. The proxy rechecks the form declaration, multipart file declaration, extension, authenticated user, reserved content type, expected size, and consumed state before streaming bytes to the API upload-session content endpoint.
5. `/bff/storage/upload-proxy` rejects SVG, BMP, AVIF, MIME parameters, MIME/extension mismatches, arbitrary HTTPS URLs, local filesystem paths, provider object keys, and presigned-looking values before forwarding.
6. Upload sessions are short-lived, user-bound, content-type-bound, and consumed after successful proxy upload. Both storage BFF endpoints remain protected by authorization and antiforgery validation.
7. Event images, User profile pictures, and Organization logos use this provider-neutral session/proxy path exclusively. Browser code has no direct provider PUT or caller-authored storage-record fallback.
8. The AI composer advertises the same four formats and requires the browser MIME, simple extension, and detected byte signature to agree before base64 encoding. The API remains authoritative for complete raster validation.

The Application boundary and ATProto importer additionally accept AVIF, but AVIF is not part of the current browser/AI chooser contract. Rejecting uploaded SVG does not remove deliberate static SVG presentation assets such as trusted application icons and illustrations; those packaged assets are not user-controlled storage content.

## Auth-State Serialization Boundary

The browser authentication state is intentionally display-only:

1. `Explore.Blazor` configures `AddAuthenticationStateSerialization` with `AuthStateSerializationPolicy.SerializeDisplaySafeClaimsAsync`.
2. Serialized claims are limited to display/name hints: `name`, `preferred_username`, `given_name`, and `family_name`.
3. Serialized claims exclude email, `sub`, `sid`, `ClaimTypes.NameIdentifier`, `internal_user_id`, tenant identifiers, roles, permissions, admin claims, tokens, and any action-authority claims.
4. UI authorization, tenancy, feature access, and action affordances must come from BFF status endpoints, API/HAL `_links`, or other server-confirmed service responses.
5. Browser claim checks are not an authority source. If a UI action needs authority metadata and no HAL/status contract exists, record the missing affordance instead of synthesizing it from roles or cached claims.

## API Client Generation

`Explore.Blazor.Client/Explore.Blazor.Client.csproj` runs the NSwag generation target before `CoreCompile`:

1. API DTO/controller contract changes are made first.
2. The API build refreshes the checked-in build-time OpenAPI contract at `schemas/openapi_islamu-event.json`.
3. The Blazor client build regenerates `Explore.Blazor.Client/Clients/EventApiClient.g.cs`.
4. Pages/components consume application services, not `EventApiClient` directly.

Generated DTOs preserve HAL `_links` through extension data. Per-resource UI affordances must be gated by HAL links from the API, not by duplicating role checks in Razor components. Admin authorization-provider setup/sync UI surfaces server-confirmed status, sync, and manual-package download affordances; deployment-managed retry refreshes the authoritative DTO after the server completes reconciliation, and the browser never owns Cerbos Admin API credentials or access tokens.

`EventApiClient.g.cs` and every NSwag DTO remain generated classes; correct the API/OpenAPI source and rerun `GenerateApiClient` rather than patching generated output or wrapping it solely to obtain record syntax. Handwritten immutable presentation results, filters, and persistence snapshots may be sealed records. Forms, edit models, component/service identity state, auth/PII hydration state, and framework-deserialized BFF payloads remain classes when their lifecycle is mutable.

Local record state is only shallowly immutable. Snapshot constructors or `init` members copy mutable input collections when later caller mutation would violate the published state; member-list results expose defensive `IReadOnlyList<T>` snapshots and upload bytes expose copied `ReadOnlyMemory<byte>` without changing base64 JSON behavior. Record equality still compares collection members by their own equality semantics. `AppJsonSerializerContext` registers only local/browser-safe contracts actually used by AOT paths. Provider credential contracts are not registered, and local-storage round trips use the source-generated options.

### Localization Admin Service Boundary

Localization administration in `Explore.Blazor.Client` goes through
`ILocalizationAdminService` and the BFF/API proxy path. The client can request
configuration, test provider connectivity, rotate/write-only TMS secrets through
server endpoints, export from TMS, and import/export static bundles. It never
calls Tolgee/Weblate directly and never receives plaintext TMS API keys.

Static bundle import/export service methods are client hooks over
`/api/admin/localization/bundle`; UI components should keep raw bundle JSON out
of logs/snackbars and rely on the service result message for safe feedback.

### Webhook Management UI

Webhook management uses generated API clients plus client service wrappers. Components must preserve API HAL resources and render create/update/delete/test/retry/rotate/open-portal actions only when the matching `_links` relation is present.

LocalProvider endpoint management is handled in ISLAMU Event. Svix advanced endpoint management is delegated to the backend-generated App Portal route, exposed in the UI only when the API emits the provider portal affordance. The browser never receives the Svix API token, endpoint signing secrets, secret refs, raw payload JSON, or full delivery response bodies.

### Registration Client Outcome Handling

Registration components consume `IEventRegistrationService` and shared workflow helpers rather than calling `EventApiClient` directly. The generated create-registration client contract still returns `BaseCommandResponseOfGuid`; no NSwag regeneration is required when only the server message semantics change and the response shape stays stable.

Blazor registration flows must classify that command response through `EventListRegistrationWorkflow.ResolveOutcome` so the modal, event list, and preview workspace share the same user-facing states:

1. `Confirmed` for normal successful create responses.
2. `Waitlisted` when the API reports that one or more selected sessions were waitlisted.
3. `AlreadyRegistered` when an idempotent repeat submit returns the existing registration.
4. `Failed` for safe service-layer failures.

`EventRegistrationService` converts generated-client `ApiException` values into bounded `FailureCode`, `Message`, and `Errors` values. Components should display those safe messages and log exceptions with structured context; they must not show raw exception text, provider response bodies, database details, bearer-token errors, or generated-client stack details. Public event detail, list, preview, and sidebar surfaces render native participation only from `start-registration` or `sign-in-to-register`, and external participation only from `external-registration`. The sign-in relation reuses the existing login interception; it does not add an API login route. They never infer an action from participation mode, roles, claims, or a raw DTO URL.

Studio participation configuration lives at `/studio/events/{eventId}/registration`. Both the sidebar entry and direct route fail closed unless the event resource contains `configure-participation`. The editor preserves the participation resource's own concurrency stamp, submits through `IEventService`, and keeps dependent mode, obligation, identity-access, and recovery choices within the Domain-valid combinations. Attendee-facing participation links never authorize the Studio route.

Create Event collects participation handling explicitly and defaults only to `INFORMATION_ONLY + NOT_APPLICABLE`. Registration policy remains an independent scope/admission choice and never implies platform-managed, guest, or recovery behavior.

### Studio Promotions And Checkout

Studio promotion management lives at `/studio/events/{eventId}/promotions`. The event shell exposes the section only when the event resource has `create-promotion` and identifies a ticket-catalog version. The loaded promotion collection must independently advertise `create-promotion`; each item must independently advertise `revise-promotion`, `publish`, `revoke`, or `rotate-promotion-code` before the matching control is rendered or dispatched. Revoke sends no caller-selected effective timestamp; the server owns the immediate decision time. Status, roles, claims, and local booleans never substitute for those exact relations.

`StudioEventPromotions` calls `IEventPromotionService`; `EventPromotionService` is the only promotion-specific wrapper over the NSwag-generated `IEventApiClient`. Management reads contain masked display labels, not lookup digests or internal code identifiers. Create and code-rotation responses reveal the organizer-entered plaintext code once so it can be copied. The component clears that value and all code inputs on acknowledgement, event/catalog identity change, mutation failure, and disposal; subsequent reads continue to show only the masked label.

Authenticated and guest checkout recovery use the same server-authored order state but different transport authority. `/registration/events/{eventId}/orders/{orderId}` uses the BFF-held authenticated session. `/registration/guest/events/{eventId}/orders/{orderId}` requires the opaque capability captured from `X-Registration-Order-Capability` at guest-order creation and kept only in the scoped `IGuestRegistrationOrderCapabilityStore`; the generated client sends it on guest operations. Neither page stores bearer tokens or capabilities in browser storage, logs them, or derives guest authority from route IDs.

Both recovery pages call `IRegistrationOrderService`, clear the entered code as soon as an apply request starts, and render apply/remove/finalize controls only from `apply-promotion`, `remove-promotion`, and `finalize`. They display the server-calculated pre-discount organizer amount, promotion discount, post-discount organizer amount, platform fee, voluntary contribution, and final total as separate fields. A zero-due order can finalize when the server emits `finalize`.

The Phase 18 attendee recovery UI consumes `start-payment`, `payment-status`, `checkout-redirect`, and `retry-payment` only through `IRegistrationOrderService`. Each explicit start or retry generates a fresh UUIDv7 idempotency key; guest operations preserve the same-session capability only in scoped memory. `checkout-redirect` is relative so tenant PathBase remains authoritative. `BffClient` imports `bff.js` and executes issue with browser `fetch`, same-origin credentials, the readable antiforgery token, and optional header-only guest capability; it never uses InteractiveServer `BffSelfClient` for the cookie response. Each issue has a UUIDv7 operation ID and JS `AbortController`. A newer issue aborts the previous operation; .NET cancellation and disposal call the JS abort function. `PaymentStatusPanel` rechecks the exact payment/HAL identity before exposing a native `target="_blank" rel="opener"` link to the constant BFF path; preparing the ticket and opening checkout are separate user gestures so popup blocking cannot convert the flow into an untracked failure. The panel consumes the generated contract's exact PascalCase values `Created`, `Processing`, `RequiresAction`, `Unknown`, `Failed`, `Cancelled`, `Succeeded`, and `NeedsReconciliation`. Nonterminal states use one tracked serial polling task with progressive 3/5/10/15-second capped backoff, require the current `payment-status` relation, and stop at the earliest authoritative payment/order expiry. Expiry removes retry/checkout affordances, renders truthful non-success copy, and reloads the order once. Route disposal cancels the tracked task. Focused actionable errors use focus as their sole announcement channel, while nonfocused transitions use the global accessibility announcer without a duplicate local live region. Bounded load, poll, mutation, and browser-bridge failures are rendered without raw exception text or unhandled console errors. Succeeded reloads the order whether it arrives from start/retry, polling, or the first post-return status read. Provider success/cancel callbacks land on `/registration/payment-recovery`, whose copy explicitly does not confirm payment, instructs the user to return to the original registration tab, and offers a typed browser action that focuses the opener and closes the checkout tab; absent/closed openers fail safely with visible guidance and a Browse events fallback. Studio order-list and payment-detail loads both use generation, cancellation, event/order/relation, and disposal guards so prior-event responses cannot overwrite current navigation. Studio loads the bounded status, last-update, and reconciliation projection only when an order advertises `studio-payment-status`, and each order owns a unique heading ID. No provider, account, session, payment, request, customer, or mutation surface is exposed.

A configured `PublicBaseUrl` application subpath becomes the BFF `PathBase`. Relative HAL issue links, constant consume links, and payment-recovery redirects therefore remain under prefixes such as `/events`; the client validates them against `NavigationManager.BaseUri` without falling back to root paths.

The Split BFF stores one-time Checkout targets only in Redis and fails closed when that store is unavailable; ordinary distributed-cache memory fallback does not apply to payment tickets. Combined standalone uses a bounded in-memory store with expiry scavenging. Both profiles validate the same exact Checkout-host allowlist and expose no provider URL to component state.

Promotion loads and mutations are cancellation- and disposal-aware so stale completions cannot replace a newer event/order state. Guest route changes clear the previous order before capability lookup and fail closed when the new event/order tuple has no stored capability. Load/success messages use polite announcements, failures use alert/assertive announcements, and one-time or mutation feedback receives programmatic focus. Scoped styles use design tokens and logical properties so the same structure remains keyboard-visible and RTL-safe.

### Studio Ticket Catalog Management

Studio ticket authoring lives at `/studio/events/{eventId}/tickets`. Navigation and direct-route availability use an OR gate: the event must contain `manage-ticket-types` or `manage-capacity-pools`. The two relations do not imply each other. Ticket-type create/edit/delete controls require ticket authority plus the exact catalog or item HAL relation, while capacity-pool controls require pool authority plus their own exact relation.

`EventTicketCatalogEditor` loads through `IEventTicketingService` and keeps the generated API client behind that scoped service. Catalog lifecycle buttons come from `create-draft`, `clone-draft`, and `publish`; item controls come from `create-type`, `create-pool`, `edit`, and `delete`. Pricing uses integer minor-unit contracts and normalized lookup metadata. The client does not infer authority from roles, event provenance, catalog status alone, or local DTO booleans.

For paid drafts, the editor follows `preflight`, `commercial-disclosures`, `payment-connection`, `start-onboarding`, and `publish` only when each exact relation is present. Preflight is display state, not publish authority: the server repeats it when publishing. Payment connection UI displays only status, merchant country, charge-capability state, requirements state, supported currencies, and readiness timestamp. Starting onboarding navigates only to the returned absolute HTTP(S) URL; return and refresh redirects do not make the UI treat the connection as ready. Instance and tenant Admin policy sections use their policy resource's `edit` relation, display the instance ceiling/effective tenant policy read-only otherwise, and never derive authority from admin claims.

### Instance Monetization Administration

The instance settings layout includes a Monetization section backed by `IPlatformMonetizationService` and the separate `GET|PUT /api/instance/settings/platform-monetization` resource. It displays fee and contribution revisions read-only when HAL omits `edit`, and enables save controls only when that exact relation exists. Fee and contribution defaults remain disabled and zero. Contribution heading, body, and basis-point options come from the API and are not hardcoded client authority or checkout values.

### Community Event Provenance And Claims

`COMMUNITY_REPORTED` provenance is immutable disclosure metadata. `EventCard` renders its "Community reported" badge in every layout outside tenant-configurable card fields, while `EventDetail` and `EventDetailsSidebar` reuse `EventProvenancePanel` for typed DTO facts. The panel may open only sanitized same-origin HAL redirect links in a new tab with opener isolation; it must never render a raw external DTO destination or invent source dates, domains, or verification state.

Correction, unsafe-link reporting, claim submission, and withdrawal remain HAL-driven through `suggest-correction`, `report-external-link`, `claim-event`, and item-level `withdraw-claim`. Fixed reporting intents submit reason `other` with `event_correction_suggestion` or `unsafe_external_link` as the subcategory. The signed-in claimant collection is filtered to the displayed event, but withdrawal visibility comes only from each claim item's `withdraw-claim` link; roles, browser claims, and local claimant-ID comparisons are never authorization sources. Reviewed public actions load through `public-actions`, render only sanitized item-level redirect links, and visibly disclose the DTO-provided destination domain without using the raw destination URL.

### Event Management And Moderation UI

Event detail and profile surfaces are HAL-driven management views:

1. `EventService.GetEventByIdAsync` tries public detail first. If the public route returns `404` for a hidden/moderated event, it falls back to authenticated `GetEventManagementDetailsAsync`; `401`, `403`, and `404` on the management route still fail closed.
2. Actor profile event lists merge public actor events with `GetManagedEventsByActorAsync` results when the current principal is authorized. Managed duplicates win so status and HAL data stay authoritative.
3. Event topbar actions must read link relations from the event DTO. Light moderation uses `moderate-light`, heavy redaction uses `moderate-heavy`, reversible restore uses `unmoderate`, and safe audit history uses `moderation-history`.
4. Event session reads default to the anonymous-safe public program route. Management views may pass `includeManagedSessions: true` to `EventService.GetSessionsByEventAsync` only from a server-provided HAL management context; the service then merges public sessions with the authorized management collection and falls back to public data on `401`, `403`, or `404`.
5. The Blazor service exposes explicit `ModerateEventLightAsync`, `ModerateEventHeavyAsync`, and `UnmoderateEventAsync` methods. Do not reintroduce a generic `ModerateEventAsync` compatibility alias; the API contract intentionally split light and heavy semantics.
6. Moderated event cards and sidebars may show a red/error "Moderated" state only after the API returned the authorized management DTO. Public visitors should not receive moderated event data at all.
7. Heavy redaction is destructive and must require explicit user confirmation in UI before calling the API. The API remains authoritative for redaction, deletion, notification, and authorization outcomes.

### AI Assistant Client Surface

The product assistant follows the same BFF and service-layer boundary:

1. Razor components use `IAiAssistantClientService`, not `IEventApiClient` directly.
2. `AiAssistantClientService` calls generated API client methods through the BFF/YARP path and returns safe command results or HAL resources.
3. `AiAssistantConversationState` owns selected conversation, reference search results, selected references, loading/error state, and HAL affordance helpers.
4. Confirm/Reject proposal buttons are rendered only when the proposed action has `confirm-action` / `reject-action` links.
5. Reference event affordances are rendered only when the reference HAL resource has an `event` link.
6. AI run progress uses polling through the run-status endpoint; no Blazor streaming transport is active for assistant runs.
7. Browser code must not display or log raw provider payloads, full prompt content, API keys, endpoint URLs, model secrets, or local authorization claims.

### Notification Preference Matrix

Notification preference UI consumes the generated API client through `INotificationService` and renders the reusable `Components/Notifications/NotificationPreferenceMatrix` component.

1. `/settings/personal/notifications` renders the focused current-user matrix; `/settings/personal` includes it in searchable View all.
2. Organization and group profile pages render scoped notification-preference tabs using the same component with organization or group scope ids.
3. Save and global-mute controls render only when the HAL resource includes `save` and `set-mute` links. Components must not inspect roles or claims to decide whether preference cells are editable.
4. The matrix sends only generated DTOs through the service layer; Razor components do not call `EventApiClient` directly.
5. The component announces load, save, mute, and error states through the accessibility announcer service and uses scoped CSS with logical properties.
6. Current-user settings render `WebPushSubscriptionPanel` only when the matrix includes the `push` channel. Enrollment is gated by the `subscribe-web-push` HAL relation; unsubscribe is gated by the subscription resource's `unsubscribe` relation.
7. `IWebPushBrowserInterop` loads `/js/web-push.js`. `Notification.requestPermission()` and `PushManager.subscribe()` run only from the explicit Enable action, never during page load or component initialization.
8. `/push-service-worker.js` suppresses OS popups while an app window is visible, posts a refresh message to the notification bell, replaces displayed notifications by tag, summarizes excessive visible entries, and focuses an existing same-origin window on click.
9. `INotificationService.GetVapidPublicKeyAsync()` calls the NSwag-generated `GetVapidPublicKeyAsync()` operation through the BFF's exact `/vapid-public-key` proxy route. Browser code receives only the plain-text public key; access tokens and the VAPID private key remain behind the BFF/API boundary.

## Render And Public Experience

Route rendering decisions are governed by runtime policy services and documented in [RENDER_POLICIES.md](RENDER_POLICIES.md). In Blazor code, the important contributor rules are:

1. Do not hardcode server/static/interactive route behavior in components.
2. Use `RuntimeRenderPolicyService` and the public-experience services instead of duplicating route-group logic.
3. `PublicExperienceService` caches public experience settings and shell data client-side for five minutes.
4. Tenant public-experience settings are source data; component code should treat them as inputs and degrade safely when optional sections are absent.

The broader hierarchical settings cascade belongs in configuration/render-policy docs, not in component code.

## Service And State Patterns

Pages should stay thin. They call scoped services that encapsulate generated-client calls, mapping, and UI-friendly error handling.

Use URL/query state for filters and pagination whenever it represents navigable state. Use scoped services for cross-component UI state only when URL state is insufficient.

Source-grounded examples:

| Pattern | Examples |
|---|---|
| Layout/shell state | `DockLayoutState`, `UiShellState`, `MainLayout.razor.cs`, `WorkspaceNavigationHost.razor.cs`, `Workspaces/EventsWorkspaceNavigation.razor.cs` |
| AI assistant state & client service | `AiAssistantState`, `AiAssistantConversationState`, `IAiAssistantClientService`, `AiAssistantClientService` |
| Control plane & admin settings | `InstanceOnboardingService`, `TenantPublicExperienceAdminService`, `TenantBrandingSettingsAdminService`, `AtprotoFederationSettingsService`, `CustomPropertyAdminService`, `FooterAdminService`, `ListmonkIntegrationSettingsService`, `TenantStorageSettingsAdminService` |
| Support access management | `SupportAccessClientService`, `SupportAccessConsoleSection.razor`, `TenantSupportAccessEvidenceSection.razor` |
| Localization & accessibility | `LocalizationAdminService`, `TranslationService`, `LanguagePreferenceService`, `AccessibilityAnnouncerService`, `AccessibilityFocusService` |
| Image storage & upload handling | `ImageUploadClient`, `ImageStorageService`, `ImageFileReaderService`, `ImagePreviewService` |
| Web push & interop | `NotificationService`, `WebPushBrowserInterop`, `BrowserActionInterop`, `AnalyticsInterop`, `CookieConsentInterop` |
| Cross-component event bridge | `CookieConsentStateService`, `AppearanceState`, `TenantNavLinksState` |
| Public-experience & lookup cache | `PublicExperienceService`, `HomeDiscoveryService`, `LookupCacheService` |
| Render & routing decisions | `RuntimeRenderPolicyService`, `StartupRoutingService` |

Keep component lifecycle async and cancellation-aware for long-running loads. UI authorization is for affordance and navigation clarity only; API authorization remains authoritative.

### Actor profiles and organization evidence

- `/actors/{actorId}` renders the canonical global Actor profile; `/t/{tenantId}/actors/{actorId}` renders the tenant-contextual projection and links back to the canonical URL.
- `ActorProfile` obtains data through `IActorService`. It does not inspect roles or claims; subscribe controls come only from item/detail HAL.
- `OrganizationTenantEvidencePanel` is present only when the organization detail HAL contains `legitimacy-evidence`. Upload requires both `prepare-evidence-upload` and `submit-evidence`; item review and protected document actions require `review-evidence` and `document`.
- Browser PDF upload starts through the organization-specific BFF route. The UI never receives or submits the tenant participation storage owner.

### Workspace shell contract

`WorkspaceRegistry` is the compile-time source of canonical rail order: Events, Studio, AI, then Settings. `AppWorkspaceRail` filters that ordered list using authentication and server-returned `WorkspaceAvailabilityDto`; it does not alphabetize destinations or infer capabilities from claims. The same semantic navigation is CSS-projected to the Xs bottom rail, while `WorkspaceNavigationHost` swaps contextual providers without re-registering the shell dock.

`UiShellState` owns route-derived active workspace, session-only Personal Settings origin, last routes, and revocation fallback. `ShellPreferencesService` restores only server-authorized workspace, actor, and Settings-scope values; authenticated dock state uses server-backed user settings, anonymous state uses tenant-discriminated local storage, and viewport/content-floor projection never persists.

Personal Settings has a separate in-page information architecture. `/settings/personal` is searchable View all and `/settings/personal/{section}` is the focused deep link; one local metadata registry owns section order, labels, keywords, and render types. `SettingsLayout` places `Personal settings sections` before content in the DOM, projects it as a sticky vertical column below the fixed app bar on desktop, and returns it to normal document flow above content below `59.997em`. `SettingsScopeSelector` remains a distinct `Settings scopes` navigation and renders only when the server returned at least one administrative scope.

## Ticket Purchase Governance Boundary

`BffTicketPurchaseEndpoints` owns the same-origin browser boundary:

- `/bff/events/{eventId}/registration-orders/{orderId}/purchase-authority` requires cookie authentication and antiforgery;
- `/bff/events/{eventId}/registration-orders/guest/{orderId}/purchase-authority` requires antiforgery and forwards the opaque order capability from `X-Registration-Order-Capability`;
- both create UUIDv7 idempotency keys server-side, apply a partitioned rate limit, ignore unknown tenant/quantity/policy fields, and return private/no-store responses;
- OAuth bearer tokens remain in the BFF handler chain and never enter browser code.

`TicketPurchaseGovernancePanel` renders only when the order HAL resource includes `reserve-purchase-authority`. Authenticated purchases show the stable account rule. Guest purchases offer verified-contact and name-only modes in a native `fieldset`; name-only mode displays an assertive, order-scoped limitation. Submission goes through `ITicketPurchaseGovernanceService`, not directly to the API, and status changes use a polite live region.

The panel is mounted on authenticated and guest order-recovery pages. Its scoped CSS uses logical properties and existing MudBlazor/design tokens, preserving RTL layout, native focus behavior, keyboard operation, and theme contrast.

## Participant Readiness Boundary

`BffParticipantReadinessEndpoints` exposes one same-origin exact-resource route. Anonymous guest reads forward the opaque order capability in `X-Registration-Order-Capability`; authenticated reads use the BFF cookie/token pipeline. Completion, approval, and revocation require cookie authorization, antiforgery before the dedicated partitioned rate limit, and independent API authorization. No browser request supplies tenant, user, organizer actor, readiness, or lifecycle state.

`ParticipantReadinessPanel` is mounted for each assigned participant in `RegistrationParticipantEditor`. `IParticipantReadinessService` consumes only the PII-minimal generated HAL resource. The panel maps bounded status/support codes through `ITranslationService`, renders complete/approve/revoke controls only when the matching HAL relation exists, disables all actions until the exact asynchronous operation completes, and renders generic errors without exception or private evidence.

Each panel owns assignment-qualified heading and outcome IDs so multiple participants remain valid in one document. Loading and nonfocused transitions use polite status semantics; mutation errors render an assertive focusable outcome before focus moves to it. Scoped CSS uses logical block/inline spacing and border properties for RTL safety.

## Styling, Accessibility, Localization, And Analytics

Do not duplicate the specialized docs in this guide.

| Topic | Blazor-specific rule | Deep doc |
|---|---|---|
| Styling | Use shared wrappers and CSS isolation; avoid ad hoc MudBlazor overrides. | [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md) |
| Accessibility | Preserve semantic labels, keyboard flow, and testable states in components. | [ACCESSIBILITY.md](ACCESSIBILITY.md) |
| Localization | Use `ITranslationService`, `LanguageProvider`, `LanguagePicker`, `MudBlazorLocalizer`, and RTL integration points; do not hardcode onboarding status text. | [LOCALIZATION.md](LOCALIZATION.md) |
| Analytics | `AnalyticsInitializer` owns browser bootstrap and consent-sensitive pageview tracking; server business metrics stay in handlers. | [OPERATIONS.md](OPERATIONS.md) |

## Common Pitfalls

1. Calling `EventApiClient` directly from a Razor component instead of through a UI service.
2. Gating edit/delete buttons with client roles instead of API-emitted HAL links.
3. Storing tokens or setup secrets in browser storage.
4. Adding custom correlation headers where .NET/OpenTelemetry `Activity` flow already handles trace propagation.
5. Updating DTOs without refreshing `schemas/openapi_islamu-event.json` and rebuilding the generated client.
6. Repeating render-policy, design-system, localization, or accessibility reference tables inside Blazor-specific docs.
7. Enabling an AI assistant button without the corresponding HAL link from the API.
8. Reconstructing onboarding tasks, authority, deployment-mode choices, or completion state from claims or browser-local state instead of server status/HAL resources.

## Related Docs

- [API.md](API.md)
- [API_COOKBOOK.md](API_COOKBOOK.md)
- [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md)
- [DEPLOYMENT_MODES.md](DEPLOYMENT_MODES.md)
- [SELF_HOSTING.md](SELF_HOSTING.md)
- [RENDER_POLICIES.md](RENDER_POLICIES.md)
- [DESIGN_SYSTEM.md](DESIGN_SYSTEM.md)
- [ACCESSIBILITY.md](ACCESSIBILITY.md)
- [LOCALIZATION.md](LOCALIZATION.md)
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md)
