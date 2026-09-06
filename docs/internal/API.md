ABOUTME: Comprehensive API architecture reference covering middleware pipeline, rate limiting, caching, HATEOAS, specification pattern, and all runtime behavior.
ABOUTME: Authoritative source for Explore.API patterns — middleware order, request protection, content negotiation, filtering, and error handling.

# API Architecture

## Structured Legal-Identity Contracts

Authenticated tenant administration exposes:

| Route | Operation | Contract |
|---|---|---|
| `GET /api/tenant/settings/documents/directory-operator-identity` | `GetTenantDirectoryOperatorIdentityDocument` | HAL document with grouped payload, capability readiness, concurrency revision, and authorized links |
| `PATCH /api/tenant/settings/documents/directory-operator-identity` | `PatchTenantDirectoryOperatorIdentityDocument` | Presence-aware legal/contact/link groups plus expected concurrency revision |

The PATCH binds tenant scope from the authenticated context, validates the draft
in the Application handler, records actor audit, invalidates public caches, and
returns `409` for stale revisions. HAL `_links.edit` is the client affordance
authority.

Anonymous public settings and shell responses contain separate
`directoryOperator` and `instanceOperator` objects. If the exact tenant identity
is unavailable for `PublicDisclosure`, both endpoints return non-cacheable RFC
7807 `503` with stable `code: tenant_identity_unavailable`; no partial shell or
branding fallback is cached.

Paid checkout composition contains structured `directoryOperator`.
`PaidOrderAcceptanceDisclosureDto` contains grouped `organizerMerchant`,
`tenantDirectoryOperator`, and `instanceOperator` evidence plus a versioned
acceptance template. Removed breaking fields include the former
branding-derived directory prose and the prior flattened operator/provider
acceptance properties. The checked-in OpenAPI document and generated NSwag
client are the only browser contract source.

> **Audience:** Integrators | Contributors | AI agents
> **Status:** Implemented
> **Owner:** API
> **Last Verified:** 2026-08-26
> **Source Anchors:** `Explore.API/Program.cs`, `Explore.API/Controllers/`, `Explore.API/Middleware/`, `Explore.API/Hateoas/`, `Explore.API/Authentication/`, `Explore.API/Extensions/`, `Explore.API/OpenApi/`, `Explore.API/Explore.API.csproj`, `Explore.Blazor.Client/Explore.Blazor.Client.csproj`, `Event.API.IntegrationTests/Features/ContractInvariantsTests.cs`, `Event.API.IntegrationTests/Features/OpenApiParityTests.cs`

## Scope
This document describes the full API behavior in `Explore.API`: the middleware pipeline, rate limiting, request timeouts, caching strategy, HATEOAS implementation, specification pattern, error handling, content negotiation, and client-generation flow.

### Concurrency Request Headers

Controllers using `EventControllerBase.TryParseConcurrencyStamp` require one
strong quoted `If-Match` value containing a non-empty, hyphenated GUID. Bare
GUIDs, weak tags, wildcard/list values, unmatched or repeated quotes, and empty
GUIDs return validation errors before dispatch. Surrounding header whitespace
is permitted; the parsed stamp reaches the handler for the existing stale-write
check. No client timestamp or new concurrency authority is introduced.

For task-first integration guidance, use [API_COOKBOOK.md](API_COOKBOOK.md). Generated OpenAPI output remains the endpoint and DTO reference; Scalar is a development/testing UI over that contract.

### Generated C# Client Shape

The OpenAPI document defines wire shape; repository generation policy defines the checked-in C# shape. Pinned NSwag first emits POCO syntax, then `eng/tools/Explore.GeneratedContracts` converts structurally eligible response/value schemas into nominal records without changing JSON names, requiredness, nullability, HAL relations, operation methods, or wire payloads. Protocol inputs, nested request graphs, HAL resources, inherited schemas, clients, exceptions, file wrappers, and explicitly mutable UI/service contracts remain classes. Generated record properties are init-only except `[JsonExtensionData] AdditionalProperties`, which stays settable for System.Text.Json AOT compatibility.

This is a source-level breaking change for consumers of the generated C# client: use object initializers or `with` copies instead of post-construction mutation. It is not a wire-format compatibility layer, and the pre-v1 repository carries no legacy generated-client variant.

See [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md) for exact eligibility, privacy-safe diagnostics, generation steps, and focused tests.

## Runtime Endpoints
### Development And Testing
- API: `https://localhost:7039`
- Swagger UI: `https://localhost:7039/swagger`
- Scalar API reference: mapped by `MapScalarApiReference()` in Development and Testing.
- OpenAPI document: `https://localhost:7039/openapi/islamu-event.json`

### Docker Compose
- API: `http://localhost:7039`
- Compose runs the API with `ASPNETCORE_ENVIRONMENT=Production`, so Swagger UI, Scalar, and `/openapi/islamu-event.json` are not exposed there unless the environment is intentionally changed.

### Operational Endpoints

`/alive`, `/health`, and `/metrics` are runtime operational endpoints, not generated OpenAPI controller operations. Storage local-first readiness is reported through the `storage` health check, and the dry-run-first reconciliation worker posture is reported through `storage-reconciliation`. These health payloads use bounded status/failure fields and must not expose filesystem paths, object keys, bucket names, endpoints, access keys, or secrets.

### Account Erasure

`DELETE /api/user` requires normal login authorization plus a UUIDv7
`Idempotency-Key` header. On first acceptance it returns `202 Accepted`,
`Location: /api/privacy-erasure/status`, `Retry-After: 5`, and a short-lived
receipt in the body. The receipt is revealed exactly once; repeat submissions
for the same intent return the accepted status without minting a second
receipt.

`GET /api/privacy-erasure/status` is the receipt-authenticated status route.
Send `Authorization: ErasureReceipt <receipt>`; OpenAPI documents the
`PrivacyErasureReceipt` `apiKey` scheme on the `Authorization` header, not
bearer auth. Responses are `private, no-store` and only expose bounded
`fenced`, `provider_pending`, or `completed` status plus aggregate provider-work
counts and settlement timestamps. Missing, invalid, wrong, and expired receipts
all return `401` without revealing whether the subject or receipt exists.

The status DTO is intentionally bounded: it does not expose provider locators,
payloads, or raw failure text. Provider settlement and replay are worker-owned
concerns; this route only reports the current fence state.

The optional MCP adapter is not an OpenAPI controller group. It is mapped at the startup `Mcp:EndpointPath` only when `Mcp:Enabled=true`, then gated at runtime by hierarchical `mcp.enabled` settings after tenant/auth resolution. The endpoint is mapped anonymously so MCP SDK authorization filters can expose anonymous-safe registry discovery and public event list/detail/program/session read tools, while protected reads such as `list_my_events`, `get_event_creation_context`, `get_event_publish_readiness`, the `event_management_context` resource template, program/custom-property/registration/team/template/sync context tools, and other scoped tools/resources/prompts still require a valid bearer or API-key principal. API keys need `mcp:read` plus event read-equivalent scope authority for those protected event-management reads. Runtime MCP governance never changes endpoint path or stateless transport mode.

---

## Middleware Pipeline (Exact Order)

The middleware pipeline in `Program.cs` is ordered precisely. Changing order will break behavior:

1. **API Exception Handling** — `UseApiExceptionHandling()`. ProblemDetails-based chained `IExceptionHandler` (Validation → Global).
2. **Forwarded Headers** — `UseForwardedHeaders()`. Applies trusted `X-Forwarded-*` values before host-derived tenant resolution.
3. **Security Headers** — `UseSecurityHeaders()`. Adds defensive headers to every response.
4. **Correlation ID** — `UseCorrelationId()`. Reads `X-Correlation-ID` or `X-Request-ID`, uses `HttpContext.TraceIdentifier` when absent, pushes to Serilog `LogContext`.
5. **Request Logging** — `UseRequestLogging()`. Structured Serilog logging: method, path, status, duration, userId, tenantId, correlationId.
6. **Response Compression** — `UseResponseCompression()`. Brotli + Gzip at `CompressionLevel.Fastest`. Enabled for HTTPS. Additional MIME types: `application/json`, `application/hal+json`.
7. **HTTPS Redirection** — `UseHttpsRedirection()`.
8. **HATEOAS Prefer Header** — `UseHateoas()`. RFC 7240 `Prefer` header processing (`return=minimal` strips `_links`).
9. **Routing** — `UseRouting()`.
10. **Tenant Resolution (pre-auth)** — `UseMiddleware<ApiTenantResolutionMiddleware>()`. Resolves `X-Tenant-Slug` and normalized host hints for `/api` and `/mcp` requests; API-key requests may defer binding until after authentication.
11. **Request Timeouts** — `UseRequestTimeouts()`. Three configurable tiers (see below).
12. **Auth Conflict Guard** — `UseMiddleware<ApiAuthenticationConflictMiddleware>()`. Rejects conflicting auth inputs before standard authentication runs.
13. **Authentication** — `UseAuthentication()`. JWT Bearer via Keycloak.
14. **Tenant Resolution (post-auth)** — `UseMiddleware<ApiTenantPostAuthenticationMiddleware>()`. Finalizes API-key tenant binding, mismatch handling, and fail-closed auth behavior.
15. **MCP Runtime Gate** — `UseMiddleware<McpRuntimeGateMiddleware>()`. Applies only to the configured MCP path after tenant/auth context exists. Returns `404` when startup mapping is enabled but runtime `mcp.enabled` resolves false.
16. **Request Localization** — `UseRequestLocalization()`.
17. **Idempotency** — `UseMiddleware<IdempotencyMiddleware>()`. Implements `Idempotency-Key` header for write operations (POST/PUT/PATCH/DELETE). Caches responses by (Key, TenantId) and replays on duplicate requests within 24-hour window.
18. **Rate Limiter** — `UseRateLimiter()`. Eight tiered policies (see below).
19. **Authorization** — `UseAuthorization()`.
20. **Support Access Audit** — `SupportAccessAuditMiddleware`. For active BFF/server-forwarded support-access sessions, records bounded request evidence after authorization without changing the response if audit persistence fails.
21. **Output Cache** — `UseOutputCache()`. Eight cache policies (see below).
22. **ETag** — `UseETag()`. SHA256-based weak ETags, 304 Not Modified support.

---

## API Versioning

Three-reader non-URL versioning — clients may use any of the following; all three are read simultaneously via `ApiVersionReader.Combine`:

1. **Media-type strategy**: `Accept: application/json;v=0.1` or `application/hal+json;v=0.1`.
2. **Query-string strategy**: `?api-version=0.1` appended to the request URL.
3. **Custom-header strategy**: `X-Api-Version: 0.1` request header.
4. Default API version is `0.1` when unspecified (`AssumeDefaultVersionWhenUnspecified = true`).
5. Version is reported in response headers via `Asp.Versioning` middleware (`ReportApiVersions = true`).
6. **URL-segment versioning is intentionally NOT supported** — every endpoint has exactly one canonical path (`/api/controller`). This invariant is enforced by the `NoUrlSegmentVersioning` contract test so that `operationId`, `RouteNames`, and HAL link generation stay stable across versions.

## Controller Conventions

1. Controllers are thin: receive request → dispatch MediatR command/query → assemble HATEOAS response → return HTTP result.
2. Business logic belongs in handlers/services, never controllers.
3. Every endpoint has named routes (via `RouteNames` constants) for HATEOAS link generation.
4. Endpoints include `[ProducesResponseType]` and XML doc summaries for OpenAPI quality.
5. Published record collections are defensive snapshots. Controllers replace immutable extension data with `SetItem`/record copies rather than mutating indexers, and convert `ReadOnlyMemory<byte>` to an MVC byte array only at the file-result boundary.
6. **No Generic CRUD or Lookup Base Controllers**:
   Controllers are concrete HTTP transport adapters, not business entities. Never introduce generic base classes (such as `CrudControllerBase<...>` or `LookupControllerBase<...>`) to abstract endpoint actions. Generic action inheritance creates an inflexible "framework inside a framework", obscures route names and compile-time uniqueness, degrades OpenAPI metadata (`[EndpointSummary]`, `[EndpointDescription]`, operation IDs), and restricts customization when backlog features diverge (custom validation, lifecycle state transitions, Cerbos authorization policies, multipart uploads, sub-resources).
7. **Approved Base Classes & Composition**:
   - **Root Base (`EventControllerBase`)**: Exposes request-scoped principal identity (`CurrentUserId`, `RequiredUserId`) and strong ETag/concurrency parsing (`TryParseConcurrencyStamp`).
   - **Domain-Family Base Classes**: Permitted only when two or more split controllers share an exact, multi-step domain protocol or security check (e.g. `RegistrationOrderControllerBase` for guest vs. authenticated checkout; `InstanceSettingsControllerBase` for setup-secret vs. admin).
   - **Composition Over Inheritance**: Shared mechanics belong in `CommandFailurePolicy`, `IResourceAssembler`, MediatR commands/queries, and extension methods (`ToCommandValidationProblem`, `ToNotFoundProblem`), leaving controller actions explicit, declarative, and independent.

### Grouped Entity PATCH Contracts

Tag, Tenant metadata, tenant navigation links, footer link groups, footer links, control-plane tenant-plan drafts, current-user appearance localization, user appearance profiles, UI themes, EventLocation disclosure, EventSession agenda items, EventSession groups, EventSession speaker assignments, EventTemplate, EventSessionTemplate, and shared/Event/EventSession custom-property definitions use route-ID or current-resource `PATCH`. Their bodies contain only nullable logical groups; omitted groups preserve persisted values, and identity comes from the route plus trusted tenant context rather than body-owned IDs. Template PATCH uses metadata and definitions groups: supplied definitions atomically replace definitions and nested options, while omission preserves the existing set. Template detail reads expose the required concurrency stamp, and sync diff/apply/history remain dedicated operations. Custom-property definition PATCH uses metadata, validation, and options groups; the shared definition additionally exposes its entity-type relation group. Supplying options atomically replaces the option set, while omitting options leaves it untouched. Template and custom-property definition updates require the observed concurrency stamp through strong `If-Match`; Event and EventSession projection refresh remains inside the write transaction. Session-group and speaker updates also require strong `If-Match`; group list/detail reads expose that stamp. Islamic and Tech aspects use separate `POST` create operations and grouped `PATCH` update operations. Appearance active-profile selection, current theme mode, profile archive, Tenant lifecycle, navigation reorder, footer reorder, and tenant-plan publish/archive/clone remain dedicated actions rather than generic property groups. UI-theme PATCH keeps the observed row version at the wrapper level and validates the merged metadata/state/palette candidate before one transactional update.

`OptionalUpdate<T>` preserves three distinct JSON states: an omitted member leaves the persisted value unchanged, an explicitly supplied `null` clears a nullable value, and a supplied non-null value replaces it. Record request bodies keep constructor/member names and types aligned for ASP.NET Core and `System.Text.Json`; validation metadata remains on the constructor parameter or bound member used by the public contract. Do not add compatibility readers or aliases for removed authority fields.

Current caller/tenant authority never comes from a request body. Controllers derive it from `EventControllerBase`, `ITenantContext`, an authoritative route/persisted resource, or a purpose-bound trusted adapter, then place it on the Application request only when authorization or business intent needs it. A body `UserId`/`TenantId` is valid only as an explicit target that is independently authorized and tenant-checked.

Tenant navigation and footer-link URLs accept relative paths or HTTPS URLs by default. The instance-only `security.require_https_external_urls` setting defaults to `true`; setting it to `false` permits HTTP only for deployments that explicitly trust an HTTP-only private network.

### Event Provenance, Public Actions, And Organizer Claims

Event reads expose typed provenance plus reviewed `Active` public actions. External destinations are stored as `EventPublicAction` records rather than caller-supplied redirect URLs.

- Anonymous `GET /api/events/{eventId}/public-actions` and `GET /api/events/{eventId}/public-actions/{actionId}` return only active reviewed actions for a published public event.
- Anonymous `GET /api/events/{eventId}/public-actions/{actionId}/redirect` resolves the stored action by `eventId` and `actionId`, returns `302`, and is `no-store`. It never accepts a destination or return URL from the request.
- Public-action DTOs instruct clients to open external destinations in a new tab with `rel="noopener noreferrer"`; these values are fixed by the server.
- Authenticated `POST /api/events/{eventId}/public-actions`, `PUT /api/events/{eventId}/public-actions/{actionId}`, and `DELETE /api/events/{eventId}/public-actions/{actionId}` manage actions through event authorization. Updating a destination returns it to pending review; deletion requires the current concurrency stamp in `If-Match`.
- Organizer-claim list/detail, submit, withdraw, and review operations live under `/api/events/{eventId}/organizer-claims`; claimant-scoped reads use `GET /api/actors/{claimantActorId}/organizer-claims`. All claim reads are authenticated and `no-store`; withdraw requires `If-Match`.
- Event-bound claim requests and HAL checks authorize as `islamuevent_event_organizer_claim` while carrying server-only parent-event and claim metadata. Withdrawal uses `withdraw-organizer-claim`; before provider evaluation, the server loads the persisted claim and claimant actor and supplies claimant user, organization, or group ownership as non-serialized authorization attributes. Request route/body ownership is never trusted. Public action, claim, correction, and unsafe-link affordances require a published public event; withdrawal and review candidates require a pending or evidence-required claim. Clients must use `_links`, not provenance, actor ids, status inference, or role claims, to decide which controls to render.

### EventLocation Disclosure Endpoints

Venue disclosure is decided per event, not per venue. The same physical `Location` can be fully public for one event and withheld for another, so every read is a separate purpose-specific operation and the response shape differs by purpose. All of them live under `/api/events/{eventId:guid}/locations` and address venues by `EventLocationId`; the physical `LocationId` appears only on the management contract.

- Anonymous `GET /api/events/{eventId}/locations` returns `EventLocationPublicDto` for a published public event. It never varies by authentication cookie and carries no exact street, postcode, or coordinates.
- Authenticated `GET /api/events/{eventId}/locations/my-access` returns `EventLocationAttendeeDto` for the caller's own registration coverage. Exact fields appear only when policy, instance/tenant governance, entitlement, and the server-side reveal time all allow it.
- Authenticated `GET /api/events/{eventId}/locations/{eventLocationId}/management` returns `HalResource<EventLocationManagementDto>` after `event:view-management` authorization, and appends a PII-free exact-read audit record before returning.
- Authenticated `GET /api/events/{eventId}/locations/management` returns every EventLocation on the event as a HAL collection; `GET /api/events/{eventId}/locations/review` is the same read filtered to `NeedsPrivacyReview`.
- `PATCH /api/events/{eventId}/locations/{eventLocationId}/disclosure` updates the seven visibility flags, the full-details audience, and the reveal instant. It requires both `ExpectedPolicyVersion` and `ExpectedConcurrencyStamp` in the body.
- `POST /api/events/{eventId}/locations/{eventLocationId}/remediation/confirm` clears a privacy review, and only for a usable active physical venue or an explicit to-be-announced association.
- Attendee, management, review, and both write routes are `Cache-Control: private, no-store`. The anonymous public read is the only cacheable one.
- `state` is a stable snake_case string (`hidden`, `to_be_announced`, `available`, `private_venue`, `unavailable`, `needs_privacy_review`) on every purpose contract.
- Clients must gate Edit and remediation controls on `_links["edit"]` and `_links["remediate-location"]`, never on local roles or claims. Each row in a collection carries its own links.

### Private Home Ownership Endpoints

A private home is somebody's household, so ownership is claimed by the incoming owner with explicit versioned consent — never assigned to a third party.

- `POST /api/location/{id}/private-home` classifies a location as a private home and records the authenticated actor as its consenting owner.
- `POST /api/location/{id}/private-home/ownership` transfers ownership to the authenticated actor. The domain requires the consenting user and the new owner to be the same person.
- Both require the current concurrency stamp in `If-Match` and a body containing `consentAcknowledged: true` plus a `consentVersion`. A missing or false acknowledgement is a refusal, not a default, and is rejected before the location is loaded.
- `CreateLocationDto`, `UpdateLocationDto`, and nested event creation's `CreateEventLocationDto` accept manual address fields but no raw `latitude` or `longitude`. Manual address transitions clear any previously derived coordinates; clients must remove coordinate controls and assignments rather than send aliases. Authorized reads are unchanged: `LocationDto` and purpose-specific attendee/management/disclosure field contracts retain their policy-controlled coordinate values.
- The generic geographic browse routes `GET /api/location/by-city/{city}` and `GET /api/location/by-country/{country}` are removed. They enumerated exact venue addresses, including private homes, without any disclosure evaluation.

### Private Address Acquisition

- `POST /api/geocoding/address-suggestions` accepts only `searchText`, `limit`, and an optional organization target. Tenant, actor, provider, credentials, coordinates, source, and visibility are server-owned.
- The operation is authenticated, bounded by the dedicated `AddressSuggestions` rate-limit policy, and always returns `Cache-Control: private, no-store`, including validation, authorization, and throttling errors. Request bodies and search text are not logged.
- Provider `None` still returns eligible local rows. The Application handler derives tenant and user context, and Persistence enforces active tenant/user membership plus creator, organization, or tenant-approved visibility in one query.
- Results are a HAL collection containing source, visibility, concurrency, and links. Clients invoke tenant approval only when `_links["approve-tenant-address"]` is present; the named `POST /api/location/{id}/address-approval` operation requires the current concurrency stamp in `If-Match`.
- Browser calls use the existing `/api/*` BFF proxy. Unsafe requests require antiforgery; browser-supplied bearer, API-key, and tenant headers are removed before forwarding.

### Template Sync Endpoints

- Event sync routes live under `/api/events/{eventId:guid}/template-sync/{diff|apply|history}`.
- Event-session sync routes live under `/api/event-sessions/{sessionId:guid}/template-sync/{diff|apply|history}`.
- `diff` returns the operator-visible template delta for a requested target version.
- `apply` accepts an explicit sync plan plus `BaseProvenanceVersion` and uses the `Complex` timeout policy.
- Stale-base and concurrent-update conflicts return `409 Conflict` with ProblemDetails types `/problems/stale_sync_base` and `/problems/concurrent_update`.

### Support Access Endpoints

Support-access routes live under `/api/support-access` and require authentication. They model support access as a persisted session for the real actor rather than as impersonation claims.

- `GET /api/support-access/current` returns the authenticated actor's current active support-access session when the API can validate one for the current request or recover one for BFF status refresh.
- `POST /api/support-access/sessions` starts a short-lived support-access session for a target tenant. The handler enforces governance settings, mode caps, ticket/reference requirements, one-active-session constraints, and authorization through the support-access resource kind.
- `POST /api/support-access/sessions/{sessionId}/stop` stops the authenticated actor's active session.
- `POST /api/support-access/sessions/{sessionId}/force-stop` force-stops a session for emergency revocation through the higher-privilege support-access action.
- `GET /api/support-access/tenants/{targetTenantId}/sessions` returns bounded session history.
- `GET /api/support-access/tenants/{targetTenantId}/sessions/{sessionId}/audit-events` returns bounded support-access audit evidence.

All session and audit responses are HAL resources or HAL collections. Link policies decide start/stop/force-stop/audit affordances; clients must not recreate those decisions from roles, claims, or cached support state.

### Storage Object Delivery And Upload Endpoints

Storage object metadata and general download routes are authenticated, resource-protected contracts because metadata can include provider object keys, storage provider labels, lifecycle state, and tenant-owned file details.

- `GET /api/storageobject` and `GET /api/storageobject/{id}` require authentication plus `islamuevent_storage_object:view`.
- `GET /api/storageobject/{id}/content` requires authentication plus `islamuevent_storage_object:download`; safe raster content is served inline, while SVG, HTML, documents, and other general content use a sanitized attachment disposition. The content reader enforces lifecycle and visibility before opening the server-owned provider key.
- `GET /api/storageobject/{id}/presigned-url` requires authentication plus `islamuevent_storage_object:presigned_download`, returns no provider object key, forces an attachment disposition at the provider, and is marked no-store. Browser image presentation does not use presigned URLs.
- `GET /api/storageobject/{id}/public` is the only anonymous storage content route. It serves only active safe public raster images by storage object ID and never accepts provider object keys, filesystem paths, or arbitrary URLs from the browser.
- Public writes use provider-neutral upload sessions: `POST /api/storageobject/upload-sessions`, `PUT /api/storageobject/upload-sessions/{uploadSessionId}/content`, and `DELETE /api/storageobject/upload-sessions/{uploadSessionId}`. Browser clients reach this flow only through the authenticated same-origin BFF session/proxy contract.
- The legacy direct-upload `POST /api/storageobject/generate-upload-url` operation and caller-authored `POST /api/storageobject` metadata creation are removed. There is no compatibility route, DTO, generated-client method, or fallback path before v1.0.
- Clients must discover `content`, `presigned-download`, and `public-image` affordances from HAL `_links`; local role/claim checks are not authoritative.

`SafeRasterContentPolicy` in Application is the single metadata and container authority for storage finalization, image references, AI image ingress, public delivery, and ATProto thumbnail validation. Browser and AI inputs accept exact JPEG, PNG, GIF, and WebP; server and ATProto paths additionally accept AVIF. All five server formats require matching MIME/extension metadata and a structurally complete container through exact EOF, so truncated content and bytes appended after the active container are rejected. This policy does not claim pixel decoding, sanitization, full codec validity, content moderation, or malware scanning.

### Email Dispatch Admin Endpoints

EmailDispatch admin routes live under `/api/admin/email-dispatch` and are authenticated operator APIs for Basic Dispatch Mode. They expose tenant-scoped delivery state and controls without exposing recipient email, subject, body, provider message ids, or raw provider errors.

- `GET /api/admin/email-dispatch/status` requires a tenant id query value and authorizes `islamuevent_email_dispatch:view`.
- `PUT /api/admin/email-dispatch/tenants/{tenantId}/pause` and `DELETE /api/admin/email-dispatch/tenants/{tenantId}/pause` authorize `islamuevent_email_dispatch:manage_tenant`.
- `PUT /api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/park` authorizes `islamuevent_email_dispatch:park`.
- `POST /api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/replay` authorizes `islamuevent_email_dispatch:replay`.
- `POST /api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/resolve-without-replay?reason=...` authorizes `islamuevent_email_dispatch:resolve` and transitions only `DeadLettered`, `Parked`, or `Unknown` rows to terminal `Skipped` state.
- `POST /api/admin/email-dispatch/tenants/{tenantId}/outbox/{outboxId}/reconcile?outcome=Delivered|NotDelivered&reason=...&providerMessageId=...` authorizes `islamuevent_email_dispatch:reconcile` and atomically aligns an `Unknown` outbox, latest attempt, receipt, and notification delivery. `Delivered` settles the graph as delivered; `NotDelivered` queues it safely. Generic replay does not accept `Unknown`.
- `GET /api/admin/email-dispatch/control` returns sanitized global processor state. `PUT|DELETE /api/admin/email-dispatch/control/pause` pauses/resumes admission, and `PUT|DELETE /api/admin/email-dispatch/control/rate-limit` sets/clears the bounded global SMTP-per-minute override. These routes authorize as the instance setting `email-dispatch.processor`, so tenant administrators cannot use them.
- HAL item links for `replay`, `park`, `resolve-without-replay`, and `reconcile`, plus global `pause`/`resume` and rate links, use the same resource/action metadata; clients must render controls only when the server includes the link.
- `Skipped` is terminal. Sent, skipped, and `ContentRedactedAt` rows are not replayable or parkable; redacted rows permanently omit all delivery-control affordances.

### Notification Preference Endpoints

Notification preference routes are authenticated private preference endpoints. They return a HAL `NotificationPreferenceMatrixDto` and expose mutation affordances only through `_links`.

- `GET /api/notification/preferences/me`, `PATCH /api/notification/preferences/me`, and `PUT /api/notification/preferences/me/mute` manage the current user's matrix.
- `GET|PATCH /api/organization/{id}/notification-preferences` and `PUT /api/organization/{id}/notification-preferences/mute` manage organization-scoped defaults/overrides through organization resource authorization.
- `GET|PATCH /api/group/{id}/notification-preferences` and `PUT /api/group/{id}/notification-preferences/mute` manage group-scoped defaults/overrides through group resource authorization.
- Response `_links.self`, `_links.save`, and `_links.set-mute` are the only UI authority for rendering save and mute controls. Clients must not infer preference editability from roles or claims.
- Matrix PATCH bodies contain an optional `cells` group. Omitted cells preserve stored choices; an absent or empty group fails validation. Command handlers validate every supplied cell before opening the write transaction, so required or broader-locked cells reject the whole request without partial writes.
- `PATCH /api/actor-subscriptions/actors/{targetActorId}/notification-level` takes actor identity only from the route. Its body contains `expectedConcurrencyStamp` and an optional `notificationLevel` group; the group is required for a non-empty patch, and a missing subscription returns typed `404` ProblemDetails.

### Browser Web Push Endpoints

- `GET /api/notification/web-push/config` is anonymous and returns only `{ enabled, publicKey }`; the VAPID private key is never part of the API contract.
- `GET /vapid-public-key` is anonymous and returns only the VAPID public key as `text/plain`; Blazor consumes it through the generated API client and notification service.
- `GET /api/notification/web-push/subscription?deviceIdentifier=...` returns the authenticated current user's safe current-device status without endpoint, `p256dh`, or auth material.
- `POST /api/notification/web-push/subscriptions` creates or refreshes the authenticated user's tenant-scoped device subscription.
- `DELETE /api/notification/web-push/subscriptions/{subscriptionId}` deactivates only a subscription owned by the authenticated tenant/user.
- Current-user preference resources advertise `subscribe-web-push`; active subscription resources advertise `unsubscribe`. Blazor must gate both actions from those HAL links.

### Localization Admin Endpoints

Localization admin routes live under `/api/admin/localization` and require
authentication. They expose provider configuration, secret status, static bundle
health, and no-TMS bundle operations without exposing TMS secret values or raw
provider errors.

- `GET /api/admin/localization/configuration` returns localization governance
  and metadata such as `TmsApiKeyConfigured`; it never returns the plaintext API
  key.
- `POST /api/admin/localization/test-connection` tests the configured provider
  from the server side.
- `POST /api/admin/localization/tms-api-key/rotate` stores the write-only TMS
  API key through the shared secret-binding flow.
- `PUT /api/admin/localization/governance` updates non-secret localization
  governance settings.
- `POST /api/admin/localization/export-from-tms?languageCode={code}` pulls the
  configured Tolgee/Weblate language into the writable static bundle cache.
- `GET /api/admin/localization/bundle?languageCode={code}` returns the merged
  static bundle for offline/no-TMS operators without calling live providers.
- `POST /api/admin/localization/bundle` validates and writes a flat static bundle
  JSON payload, then invalidates translation caches for that language.
- `GET /api/admin/localization/bundle-health` reports whether the writable bundle
  path is usable.

Static bundle imports accept only flat `ui.*` and `lookup.*` string dictionaries.
ProblemDetails and logs must not include raw bundle content or TMS credentials.

### Listmonk Integration Settings Endpoints

Listmonk settings are exposed under `/api/integrations/listmonk`:

| Route | Route name | Auth | Purpose |
|---|---|---|---|
| `GET /api/integrations/listmonk/settings` | `GetListmonkIntegrationSettings` | `[AllowAnonymous]`, public classification | Returns sanitized `ListmonkIntegrationSettingsDto` for admin UI state. |
| `PUT /api/integrations/listmonk/settings` | `UpdateListmonkIntegrationSettings` | `[Authorize]` | Updates non-secret Listmonk settings. |
| `POST /api/integrations/listmonk/credentials/rotate` | `RotateListmonkIntegrationCredentials` | `[Authorize]` | Rotates the write-only API username/key secret bindings. |
| `POST /api/integrations/listmonk/test-connection` | `TestListmonkIntegrationConnection` | `[Authorize]` | Runs a server-side connectivity check with resolved settings and credentials. |

### Event Ticketing & Catalog Management Endpoints

Authenticated event-scoped ticket catalog versions, draft authoring, ticket types, and capacity pool management endpoints are exposed under `/api/events/{eventId:guid}/ticketing`:

| Route | Route Name | Auth | Purpose |
|---|---|---|---|
| `GET /api/events/{eventId}/ticketing` | `GetEventTicketCatalogManagement` | `[Authorize]` | Returns the event ticket catalog management DTO with catalog version, ticket types, capacity pools, normalized lookup metadata, and HAL affordances. Instance monetization is not embedded. |
| `POST /api/events/{eventId}/ticketing/draft` | `CreateEventTicketCatalogDraft` | `[Authorize]` | Creates a new draft catalog version with currency code. |
| `POST /api/events/{eventId}/ticketing/draft:clone` | `CloneEventTicketCatalogDraft` | `[Authorize]` | Clones active published catalog version into a working draft version. |
| `POST /api/events/{eventId}/ticketing/ticket-types` | `CreateEventTicketType` | `[Authorize]` | Adds a new ticket type (pricing mode, price, capacity pool assignment) to draft catalog. |
| `PUT /api/events/{eventId}/ticketing/ticket-types/{ticketTypeId}` | `UpdateEventTicketType` | `[Authorize]` | Updates an existing ticket type configuration. |
| `DELETE /api/events/{eventId}/ticketing/ticket-types/{ticketTypeId}` | `DeleteEventTicketType` | `[Authorize]` | Removes a ticket type from draft catalog. |
| `POST /api/events/{eventId}/ticketing/capacity-pools` | `CreateEventCapacityPool` | `[Authorize]` | Creates a shared capacity pool with oversell policy and seat allocations. |
| `PUT /api/events/{eventId}/ticketing/capacity-pools/{capacityPoolId}` | `UpdateEventCapacityPool` | `[Authorize]` | Updates capacity pool limits and oversell policy. |
| `DELETE /api/events/{eventId}/ticketing/capacity-pools/{capacityPoolId}` | `DeleteEventCapacityPool` | `[Authorize]` | Deletes a capacity pool. |
| `GET /api/events/{eventId}/ticketing/publication-preflight` | `GetPaidEventPublicationPreflight` | `[Authorize]`, `private, no-store` | Returns the current paid-publication readiness and blockers. |
| `PUT /api/events/{eventId}/ticketing/commercial-disclosures` | `UpdateEventTicketCatalogCommercialDisclosures` | `[Authorize]` | Updates the merchant, refund-policy, and support disclosures required for a paid catalog. |
| `GET /api/events/{eventId}/ticketing/payment-connection` | `GetEventOrganizerPaymentConnection` | `[Authorize]`, `private, no-store` | Returns bounded readiness for the exact event organizer's payment connection. |
| `POST /api/events/{eventId}/ticketing/payment-connection/onboarding` | `StartEventOrganizerPaymentOnboarding` | `[Authorize]`, `private, no-store` | Starts or reuses hosted organizer onboarding and returns an absolute HTTP(S) URL plus a reuse flag. |
| `GET /api/events/{eventId}/ticketing/payment-connection/onboarding/return` | `ReturnEventOrganizerPaymentOnboarding` | `[Authorize]`, `private, no-store` | Redirects to Studio ticketing; it does not mark the connection ready. |
| `GET /api/events/{eventId}/ticketing/payment-connection/onboarding/refresh` | `RefreshEventOrganizerPaymentOnboarding` | `[Authorize]`, `private, no-store` | Redirects to Studio ticketing for a new onboarding attempt; it does not mark the connection ready. |
| `POST /api/events/{eventId}/ticketing/publish` | `PublishEventTicketCatalog` | `[Authorize]` | Recomputes paid readiness inside the publish transaction before promoting a draft catalog. |

Ticketing money fields use integer minor units in `long ...Minor` properties. Percentages use integer basis points, where `10_000 = 100%`. Catalog, ticket-type, and capacity-pool mutations must follow the exact relation on the returned HAL resource, including `create-draft`, `clone-draft`, `create-type`, `create-pool`, `publish`, `edit`, and `delete`.

Paid-event policy settings are separate from instance monetization:

| Route | Route Name | Auth | Purpose |
|---|---|---|---|
| `GET /api/instance/settings/paid-event-policy` | `GetInstancePaidEventPolicySettings` | `[Authorize]`, Admin, `private, no-store` | Returns the active instance ceiling. |
| `PUT /api/instance/settings/paid-event-policy` | `UpdateInstancePaidEventPolicySettings` | `[Authorize]`, Admin, `private, no-store` | Revises the instance ceiling. |
| `GET /api/tenants/{tenantId}/settings/paid-event-policy` | `GetTenantPaidEventPolicySettings` | `[Authorize]`, `private, no-store` | Returns the instance ceiling, tenant override, and effective policy. |
| `PUT /api/tenants/{tenantId}/settings/paid-event-policy` | `UpdateTenantPaidEventPolicySettings` | `[Authorize]`, `private, no-store` | Revises a tenant-only narrowing of that ceiling. |

The paid-management resources expose actions only through their exact HAL relations: `preflight`, `commercial-disclosures`, `payment-connection`, `start-onboarding`, and `publish`. A paid publish relation requires both fresh readiness and `manage-paid-event-commerce` for the event's persisted organizer actor. Policy responses expose policy values without policy or tenant identifiers. The connection response exposes only status, merchant country, charge-capability state, requirements state, supported currencies, and readiness timestamp. It omits provider, platform, account, tenant, actor, connection, lineage, and evidence identifiers. Checkout, capture, refunds, disputes, transfers, payouts, and legal/tax/invoice support are separate API capabilities; admission and online QR check-in are documented in [Admission And Registration](ADMISSION_AND_REGISTRATION.md#6-phase-21-online-check-in-model).

### Event Promotion And Order Redemption Endpoints

Organizer promotion management is authenticated, event-scoped, and `private, no-store` for reads. Every operation authorizes `manage-paid-event-commerce` against the persisted Event organizer; the feature is available only for a platform-managed Event and an exact scoped ticket catalog.

| Route | Route Name | Protection | Purpose |
|---|---|---|---|
| `GET /api/events/{eventId}/promotions?ticketCatalogVersionId={id}` | `GetEventPromotions` | `[Authorize]`, `private, no-store` | Lists safe definition versions and masked active-code labels for one exact catalog. |
| `GET /api/events/{eventId}/promotions/{promotionDefinitionId}` | `GetEventPromotion` | `[Authorize]`, `private, no-store` | Returns one safe promotion-management HAL resource. |
| `POST /api/events/{eventId}/promotions` | `CreateEventPromotionDraft` | `[Authorize]`, `Write`, `Idempotency-Key` | Creates a draft and returns the submitted code once as `issuedCode`; no plaintext or digest is present in the management DTO. |
| `PUT /api/events/{eventId}/promotions/{promotionDefinitionId}` | `ReviseEventPromotion` | `[Authorize]`, `Write`, `Idempotency-Key` | Creates the next draft version from a published definition; it does not mutate the published version. |
| `POST /api/events/{eventId}/promotions/{promotionDefinitionId}/publish` | `PublishEventPromotion` | `[Authorize]`, `Write`, `Idempotency-Key` | Publishes a draft and persists only the versioned digest and masked suffix for its submitted code. |
| `POST /api/events/{eventId}/promotions/{promotionDefinitionId}/revoke` | `RevokeEventPromotion` | `[Authorize]`, `Write`, `Idempotency-Key` | Immediately revokes a published definition at the server-owned decision instant; callers cannot schedule an effective time. |
| `POST /api/events/{eventId}/promotions/{promotionDefinitionId}/code:rotate` | `RotateEventPromotionCode` | `[Authorize]`, `Write`, `Idempotency-Key` | Retires the active code, creates its replacement, and returns the replacement plaintext once as `issuedCode`. |

The management resource DTO exposes only event/catalog/version/currency, definition group/version/status, eligibility, discount parameters, UTC window, redemption limits, and a masked `promotionCodeDisplayLabel`. Tenant, actor, and organizer identifiers are server-only HAL authorization metadata marked `[JsonIgnore]`. Digests, lookup-key versions, active/retired storage flags, and reservation IDs are absent from JSON and generated clients. Plaintext appears in generated request models only where a caller must submit a code; among responses, only successful create and rotate command wrappers expose the one-time `issuedCode`. Collection HAL advertises `create-promotion`; draft items may advertise only `publish`; published items may advertise `revise-promotion`, `revoke`, and `rotate-promotion-code`. Link presence, not a role or status guess, is the client action authority.

Authenticated and capability-scoped guest orders expose matching apply/remove operations only while the order is `READY_FOR_CHECKOUT`:

| Route | Route Name | Protection |
|---|---|---|
| `POST /api/events/{eventId}/registration-orders/{orderId}/promotion` | `ApplyAuthenticatedRegistrationOrderPromotion` | `[Authorize]`, `Write`, `Idempotency-Key` |
| `DELETE /api/events/{eventId}/registration-orders/{orderId}/promotion` | `RemoveAuthenticatedRegistrationOrderPromotion` | `[Authorize]`, `Write`, `Idempotency-Key` |
| `POST /api/events/{eventId}/registration-orders/guest/{orderId}/promotion` | `ApplyGuestRegistrationOrderPromotion` | `[AllowAnonymous]`, `PublicTransactional`, `Idempotency-Key`, `X-Registration-Order-Capability` |
| `DELETE /api/events/{eventId}/registration-orders/guest/{orderId}/promotion` | `RemoveGuestRegistrationOrderPromotion` | `[AllowAnonymous]`, `PublicTransactional`, `Idempotency-Key`, `X-Registration-Order-Capability` |

Guest operations use the fixed 10-per-60-second effective-IP `public_transactional` window with queue limit zero. Browser-originated unsafe requests still cross the BFF antiforgery boundary. Both guest responses protect idempotency replay metadata. Authenticated operations use the per-user `Write` policy. The capability is header-only and is compared with the opaque hash on the exact tenant/Event/order row after order-expiry validation; it never enters a URL, response body, HAL link, log, or generated DTO.

Apply accepts `{ "code": "..." }`. Lookup normalizes the code, computes candidates for only the key versions already represented by active scoped codes, then rechecks tenant, Event, catalog, currency, window, eligibility, limits, one-active-reservation, and pinned fee-policy facts inside one serializable transaction. Wrong order ownership/capability, an empty or overlong submitted code, unknown or retired code, wrong scope, exhausted limit, inactive definition, an existing promotion, and pricing mismatch all collapse to the same registration-order `404`; the API does not reveal which predicate failed. Syntactically malformed JSON remains subject to the API's global safe `400` validation contract before the redemption handler runs. Success returns only the masked applied label plus promotion discount, platform fee, platform contribution, and final due totals. Order reads expose aggregate pre-discount, discount, post-discount, organizer-directed, fee, organizer-earnings, contribution, and final totals in one currency; `organizerDirectedTotalMinor` equals `postDiscountOrganizerDirectedTotalMinor`. They do not expose raw code, digests, key versions, promotion IDs, reservation IDs, or authorization booleans.

Order HAL exposes exactly one of `apply-promotion` or `remove-promotion` in `READY_FOR_CHECKOUT`; authenticated links pass through normal registration-order authorization and guest links remain capability-bound at execution. Promotion removal clears the applied snapshot and reprices from pinned facts. Positive totals proceed to the payment contract below; zero totals continue to use the provider-free `finalize` relation.

### Registration Payment Endpoints

Account-owned and capability-scoped guest orders expose idempotent payment start and retry plus private authoritative status. Guest writes are `PublicTransactional`, require `Idempotency-Key`, and accept `X-Registration-Order-Capability` only as a header. Account writes require authentication and current-account ownership. Status and start responses are HAL resources with only safe state, timestamps, bounded failure codes, and redirect/retry availability; they exclude provider account/session/payment/request identifiers, idempotency keys, capabilities, PII, and raw provider errors.

| Route | Operation | Protection |
|---|---|---|
| `POST /api/events/{eventId}/registration-orders/{orderId}/payment` | `StartAuthenticatedRegistrationPayment` | `[Authorize]`, Write, idempotent, private/no-store |
| `GET /api/events/{eventId}/registration-orders/{orderId}/payment` | `GetAuthenticatedRegistrationPayment` | `[Authorize]`, private/no-store |
| `POST /api/events/{eventId}/registration-orders/{orderId}/payment/retry` | `RetryAuthenticatedRegistrationPayment` | `[Authorize]`, Write, idempotent, private/no-store |
| `GET /api/events/{eventId}/registration-orders/{orderId}/payment/checkout-target` | `GetAuthenticatedRegistrationPaymentCheckoutTarget` | `[Authorize]`, current-account access, private/no-store; BFF resolver use |
| `POST /api/events/{eventId}/registration-orders/guest/{orderId}/payment` | `StartGuestRegistrationPayment` | `[AllowAnonymous]`, PublicTransactional, capability, idempotent, private/no-store |
| `GET /api/events/{eventId}/registration-orders/guest/{orderId}/payment` | `GetGuestRegistrationPayment` | `[AllowAnonymous]`, capability, private/no-store |
| `POST /api/events/{eventId}/registration-orders/guest/{orderId}/payment/retry` | `RetryGuestRegistrationPayment` | `[AllowAnonymous]`, PublicTransactional, capability, idempotent, private/no-store |
| `GET /api/events/{eventId}/registration-orders/guest/{orderId}/payment/checkout-target` | `GetGuestRegistrationPaymentCheckoutTarget` | `[AllowAnonymous]`, capability, private/no-store; BFF resolver use |
| `GET /api/events/{eventId}/registration-orders/{orderId}/payment/studio` | `GetStudioRegistrationPayment` | `[Authorize]`, exact `manage-paid-event-commerce`, private/no-store |

Payment state is one of `Created`, `Processing`, `RequiresAction`, `Unknown`, `Failed`, `Cancelled`, `Succeeded`, or `NeedsReconciliation`. Start only claims or reuses the durable attempt/dispatch effect and never calls the provider synchronously. Retry is available for a parked pre-handoff dispatch with no provider session or an authoritative terminal `Failed`/`Cancelled` attempt; ambiguous, processing, succeeded, and reconciliation-required states never advertise it. Idempotency identity binds the tenant key to the resolved path/route values and a SHA-256 capability scope; raw capability values are never persisted or logged. Successful replay restores protected `Cache-Control: private, no-store` metadata. `checkout-redirect` is an antiforgery-protected, rate-limited, PathBase-aware same-origin BFF `POST` executed by browser fetch in every Blazor render mode. Anonymous rate partitions use only trusted effective remote IP plus resolved tenant; authenticated partitions use stable user ID, never checkout or antiforgery cookies. Issue prepares the compact cookie, rechecks `RequestAborted`, commits the bounded server-side target, rechecks again, and only then writes cookies; cancellation triggers compare-and-delete rollback. The constant GET requires `Sec-Fetch-Site: same-origin` exactly before validating origin, PathBase, tenant/order, expiry, dedicated-session digest, and current allowlist, then peeks and atomically consumes the target. Split requires healthy Redis and Combined standalone uses bounded expiry scavenging. URL paths and traces contain no bearer ticket, capability, PII, provider account, API token, or provider URL.

Checkout creation runs asynchronously from the Quartz `payment-reconciliation-drain` job. It uses the canonical public HTTPS origin for success/cancel navigation, the persisted connected account and idempotency key, and the configured Checkout-host allowlist. Signed Connect callbacks only persist a normalized inbox envelope and make reconciliation due. Authoritative connected-account Checkout and PaymentIntent retrieval, exact amount/currency/application-fee matching, and fenced monotonic settlement determine payment truth; browser return navigation never does.

Terminal `Failed` or `Cancelled` retry persists release of the old active slot before creating one replacement. Capability-aware replay recognizes only safe `Created + Pending` or `DispatchPending + Failed` replacement states and returns the same attempt/effect; `Unknown` and `Succeeded` remain forbidden, and expired guest capability remains `404`. A normalized `PublicBaseUrl` subpath is preserved in Checkout success/cancel URLs.

### Admission Ticket Recovery & Gate Credential Endpoints

Admission ticket self-service recovery, credential rotation, and account-owned ticket reads live under `/api/tickets`. See [ADMISSION_AND_REGISTRATION.md](ADMISSION_AND_REGISTRATION.md) for domain concepts and zero-knowledge security architecture.

| Route | Route Name | Protection | Purpose |
|---|---|---|---|
| `POST /api/tickets/recovery` | `RequestAdmissionTicketRecovery` | `[AllowAnonymous]`, `PublicTransactional`, `Idempotency-Key`, `public_transactional` plus chained tenant recovery budget | Initiates self-service ticket recovery. Accepts `{ "email": "..." }` and returns indistinguishable `202 Accepted` to prevent email enumeration. |
| `POST /api/tickets/recovery/consume` | `ConsumeAdmissionTicketRecovery` | `[AllowAnonymous]`, `Public`, `private, no-store`, `AdmissionTicketRecoveryPolicy`, `X-Admission-Ticket-Recovery-Capability` | Consumes a single-use recovery capability token passed via header; rotates the capability and delivers the rendered QR SVG and printable ticket DTO. |

Key invariants:
- **Zero-Knowledge Bearers**: Plaintext QR barcodes and tokens are never persisted in the database; only versioned keyed HMAC-SHA-256 lookup digests (`LookupDigest`) are stored.
- **Wire Payload Format**: Standardized `islamu-admission:v1:<43-char-base64url-bearer>` (63 characters total).
- **Indistinguishable Email Responses**: `POST /api/tickets/recovery` returns identical `202 Accepted` payloads regardless of whether the email address has active tickets.
- **Two-layer request throttling**: recovery request traffic must pass both the per-IP `public_transactional` policy and the global tenant-scoped recovery budget. Capability consumption keeps the dedicated recovery limiter.
- **No consume-response replay**: capability consumption is intentionally not idempotency-cached. The keyed, expiring capability is atomically single-use, and replaying a cached successful bearer response would violate that authority.
- **Single-Use Capabilities**: Recovery capability tokens expire and rotate automatically upon consumption or re-request.

### Online Admission Check-In Endpoints

Phase 21 check-in is online and server-authoritative. Staff routes use ordinary authenticated event
authority; scanner routes use only the opaque `X-Admission-Scanner-Capability` scheme bound to one
tenant, event, target, action set, and expiry. Every response is private/no-store, mutation
failures are bounded RFC 7807 responses, and UI controls come only from returned HAL relations.

| Route | Route Name | Protection | Purpose |
|---|---|---|---|
| `GET /api/events/{eventId}/admission/scanner-capabilities` | `ListAdmissionScannerCapabilities` | Staff `event_check_in:view` | Lists masked capabilities for the event. |
| `POST /api/events/{eventId}/admission/scanner-capabilities` | `IssueAdmissionScannerCapability` | Staff `event_check_in:manage`, `Idempotency-Key` | Issues one target-scoped capability and discloses plaintext once. |
| `DELETE /api/events/{eventId}/admission/scanner-capabilities/{scannerCapabilityId}` | `RevokeAdmissionScannerCapability` | Staff `event_check_in:manage` | Revokes one device authority without stopping other target channels. |
| `POST /api/events/{eventId}/admission/check-ins` | `CheckInAdmission` | Staff `event_check_in:manage`, `Idempotency-Key` | Checks in one opaque credential against one body `targetId`. |
| `POST /api/events/{eventId}/admission/check-ins/batch` | `BatchCheckInAdmissions` | Staff `event_check_in:manage`, `Idempotency-Key` | Processes 1–100 business outcomes independently in input order; a dependency outage aborts the remaining queue with `503`. |
| `GET /api/events/{eventId}/admission/check-ins/{checkInId}` | `GetAdmissionCheckIn` | Staff `event_check_in:view` | Returns bounded fact detail and current undo affordance. |
| `POST /api/events/{eventId}/admission/check-ins/{checkInId}/undo` | `UndoAdmissionCheckIn` | Staff `event_check_in:manage`, `Idempotency-Key` | Appends a compensation linked to the exact active fact. |
| `GET /api/events/{eventId}/admission/check-ins/summary?targetId={targetId}` | `GetAdmissionCheckInSummary` | Staff `event_check_in:view` | Returns exact-target aggregate counts without roster data. |
| `GET /api/events/{eventId}/admission/check-ins/audit?cursor={cursor}&pageSize={pageSize}` | `GetAdmissionCheckInAudit` | Staff `event_check_in:view` | Traverses an export-safe immutable keyset page of 1–100 facts. |
| `GET /api/events/{eventId}/admission/check-ins/health?targetId={targetId}` | `GetAdmissionCheckInHealth` | Staff `event_check_in:view` | Returns `Active`, `Stopped`, or `Unavailable` plus dependency state. |
| `POST /api/events/{eventId}/admission/check-ins/operations/stop` | `StopAdmissionCheckIn` | Staff `event_check_in:manage` | Stops every admission channel for the exact target. |
| `POST /api/events/{eventId}/admission/check-ins/operations/restore` | `RestoreAdmissionCheckIn` | Staff `event_check_in:manage` | Restores the exact target after dependency recovery. |
| `POST /api/events/{eventId}/admission/check-ins/operations/reconcile` | `ReconcileAdmissionCheckIn` | Staff `event_check_in:manage` | Appends a bounded post-incident decision without rewriting facts. |
| `POST /api/admission/scanner/check-ins` | `ScannerCheckInAdmission` | `AdmissionScanner`, `Idempotency-Key` | Performs one check-in using the authenticated capability target. |
| `POST /api/admission/scanner/check-ins/batch` | `ScannerBatchCheckInAdmissions` | `AdmissionScanner`, `Idempotency-Key` | Processes a bounded target-fixed scanner batch and aborts remaining work on dependency outage. |
| `POST /api/admission/scanner/check-ins/{checkInId}/undo` | `ScannerUndoAdmissionCheckIn` | `AdmissionScanner`, `Idempotency-Key` | Compensates the exact active fact when the capability permits undo. |

Undo request bodies carry `reasonCode`, restricted to `OperatorCorrection`, `DuplicateScan`,
`WrongTarget`, or `ExceptionalReconciliation`; free-form reason text is not accepted or persisted.
Invalid credential, capability, tenant, event, and target states remain indistinguishable. A
dependency failure returns bounded `503`; scanner clients stop intake and retain no offline queue.

### Registration Form Authoring Endpoints

Authenticated registration-form authoring is rooted at `/api/events/{eventId:guid}/registration-workflows`. The event `manage-registration-workflow` relation is emitted only after server-side authorization confirms a verified organizer controller or an explicit event registration-manager assignment. Community contributors, listing submitters, tenant-only curators, machine principals, ambiguous organizers, and unrelated tenants receive neither the relation nor authoring data.

The surface provides event-purpose workflow read/create/update; requirement create/update/delete; form create/read; version create-or-clone/read; draft section, field, option, and bounded-rule mutations; publication preflight; and publication. Reads are authenticated and `private, no-store`. Writes use the `WritePolicy`, require a strong quoted `If-Match` containing the observed parent/root concurrency stamp, and return `409 Conflict` for stale observations. Preflight rejects empty forms, incomplete choice options, unresolved or forward condition references, and explicit-consent fields without both a purpose code and text version. Publication always invokes the Application publication facade and atomically pins the exact generated data, UI, logic, and mapping artifacts plus their lowercase SHA-256 hash; callers never supply schema JSON or hashes.

The canonical preflight operation is `POST /api/events/{eventId:guid}/registration-forms/{formId:guid}/versions/{versionId:guid}/preflight` (`GetRegistrationFormPublishPreflight`); the former `publish:preflight` spelling is not part of the contract. The generated OpenAPI document, NSwag client, and `API_CONTRACT_INVENTORY.md` are regenerated from this route and checked for byte-level determinism.

Draft authoring resources expose only state-valid, permission-checked HAL relations such as `edit`, `add-section`, `add-field`, `add-option`, `add-rule`, `preflight`, and `publish`. Published versions expose read, preflight, and new-version navigation only; child mutation relations are omitted. DTOs expose normalized lookup IDs/codes/names, lifecycle status, provenance, schema hash, and concurrency stamps, with no provider question IDs, claims, roles, or local capability booleans.

Participation requirements attach through authenticated `POST /api/events/{eventId}/participation/requirements/{requirementId}` and detach idempotently through `DELETE` on the same route. Both operations require the current participation-configuration stamp in a strong quoted `If-Match` header and are authorized as registration-form `attach`/`detach` actions. The workflow management projection exposes persisted `isAttached` state, while each requirement resource emits exactly one matching, permission-checked `attach` or `detach` HAL relation as the action authority. A valid walk-in standalone questionnaire is discovered anonymously at `GET /api/events/{eventId}/participation/optional-questionnaire`; the HAL `optional-questionnaire` relation is the only client affordance and the endpoint returns non-leaking `404` when the active published descriptor is unavailable.

### Instance Platform Monetization Endpoints

Platform fees and optional instance contributions are a separate versioned settings resource:

| Route | Route Name | Auth | Purpose |
|---|---|---|---|
| `GET /api/instance/settings/platform-monetization` | `GetInstancePlatformMonetizationSettings` | `[Authorize]`, Admin classification | Returns the active fee and contribution revisions as a HAL resource. |
| `PUT /api/instance/settings/platform-monetization` | `UpdateInstancePlatformMonetizationSettings` | `[Authorize]`, Admin classification, `Write` rate limit | Creates replacement fee and contribution revisions after expected-version checks. |

Both Application handlers recheck instance-administrator authority before repository access. Fresh defaults are fee disabled with zero basis points and no fixed charges, plus contribution disabled with a zero default option. The response emits `edit` only when the caller may update the `platform-monetization` instance setting. Tenant administrators, organizers, and curators have no monetization management path.

---

## Rate Limiting (8 Tiers)

Configured in `RateLimitingExtensions.cs`. All settings are configurable via `appsettings.json` under `RateLimiting` section.

### Global (IP Token Bucket)
- **Policy**: `global` — applied to all endpoints by default.
- **Mechanism**: Token bucket per successfully authenticated API key ID when present, otherwise per remote IP address. Empty, malformed, invalid, revoked, or inactive API keys do not create key partitions and remain in the anonymous/IP partition.
- **Defaults**: 200 tokens, replenish 40 tokens per 10 seconds.
- **IP Resolution**: uses `HttpContext.Connection.RemoteIpAddress`; trusted forwarded-header middleware updates the effective remote/host values earlier in the pipeline.
- **Exemption**: Localhost (`127.0.0.1`, `::1`) is exempt.

### Authenticated (Sliding Window)
- **Policy**: `authenticated` — for authenticated user endpoints.
- **Mechanism**: Sliding window per API key ID when present, otherwise per `User.Identity.Name`.
- **Defaults**: 200 requests per 60-second window, 4 segments.

### Write (Fixed Window)
- **Policy**: `write` — for mutation endpoints (`POST`, `PUT`, `DELETE`).
- **Mechanism**: Fixed window per API key ID when present, otherwise per `User.Identity.Name`.
- **Defaults**: 30 requests per 60-second window.

### PublicIngestion (Fixed Window)
- **Policy**: `PublicIngestion` — for anonymous signed machine callback endpoints.
- **Mechanism**: Fixed window per IP address.
- **Defaults**: 60 requests per 60-second window.

### SetupSecret (Fixed Window)
- **Policy**: `setup_secret` — for instance bootstrap endpoints.
- **Mechanism**: Fixed window per IP address.
- **Defaults**: 5 requests per 60-second window.

### AnalyticsRelay (Fixed Window)
- **Policy**: `AnalyticsRelay` — for anonymous browser analytics relay traffic.
- **Mechanism**: Fixed window per IP address.
- **Defaults**: 120 requests per 60-second window.

### AiAssistant (Fixed Window)
- **Policy**: `AiAssistant` — for AI assistant send/model/action endpoints.
- **Mechanism**: Fixed window per API key ID when present, otherwise per authenticated user ID.
- **Defaults**: 12 requests per 60-second window.

### EventOpenGraphImage (Process-Wide Concurrency)
- **Policy**: `EventOpenGraphImage` — for public event Open Graph image rendering.
- **Mechanism**: Concurrency limiter with one fixed `EventOpenGraphImage` partition shared by all requests in the API process.
- **Defaults**: 2 concurrent renders, queue limit 0.

### Rejection Behavior
- Returns `429 Too Many Requests` with RFC 6585 `ProblemDetails`.
- Includes `Retry-After` when available plus `X-RateLimit-Limit` and `X-RateLimit-Remaining`.

### Testing Override
In `Testing` environment, all rate limiters, including `EventOpenGraphImage`, are replaced with `NoLimiter` (disabled). Integration factories can opt back into rate limiting to verify `429`, `Retry-After`, and partition behavior.

---

## Request Timeouts (3 Tiers)

Configured in `RequestTimeoutExtensions.cs`. All settings configurable via `RequestTimeouts` section.

| Policy | Default | Use Case |
|---|---|---|
| `Default` | 30 seconds | Standard operations |
| `Lookup` | 10 seconds | Fast lookup queries |
| `Complex` | 60 seconds | Complex queries, exports |

Timeout expiry returns `504 Gateway Timeout`.

---

## Caching (3 Layers)

### Layer 1: Output Cache (HTTP Response Level)
Applied via `[OutputCache(PolicyName = "...")]` on controller endpoints.

| Policy | Duration | Vary By | Use Case |
|---|---|---|---|
| `LookupData` | 1 hour | `X-Tenant-Slug`, `Host` | Lookup tables (event types, languages, etc.) |
| `PublicData` | 1 hour | `X-Tenant-Slug`, `Host` | Anonymous lookup endpoints |
| `ListData` | 30 seconds | `X-Tenant-Slug`, `Host`, `Authorization`, query: `pageNumber`, `pageSize` | Collection listings |
| `DetailData` | 60 seconds | `X-Tenant-Slug`, `Host`, `Authorization`, route: `id` | Single-entity detail views |
| `TenantNav` | 5 minutes | `X-Tenant-Slug`, `Host` | Tenant navigation/config endpoints |
| `PublicExperienceShell` | 30 seconds | `X-Tenant-Slug`, `Host` | Public shell and experience settings |
| `SystemConfig` | 10 seconds | `Host` | System configuration checks |
| `SitemapData` | 30 minutes | `X-Tenant-Slug`, `Host` | Sitemap output |

The default ASP.NET Core output-cache store is process-local. `HybridCache` or
`IDistributedCache` does not distribute output-cache entries or tag eviction:
immediate tag eviction is guaranteed only on the replica handling the request,
while other replicas may serve stale output until the policy TTL expires.
Cross-replica output-cache invalidation is deferred without a dedicated
distributed output-cache dependency.

### Layer 2: HybridCache (Application Level — L1 + L2)
Injected into MediatR handlers, not controllers. Provides in-memory L1 + distributed L2 caching with stampede protection.

| Setting | Value |
|---|---|
| Default expiration | 30 minutes |
| Local cache expiration | 5 minutes |
| Max payload size | 10 MB |
| Max key length | 512 characters |

**Read-through pattern** (query handlers): `_cache.GetOrCreateAsync(key, factory, options)`.
**Invalidation** (command handlers): `_cache.RemoveAsync(key)`.

### Layer 3: ETag Middleware
- Computes SHA256-based weak ETags on `application/json` and `application/hal+json` responses.
- Returns `304 Not Modified` when client sends `If-None-Match` header matching current ETag.
- Applied globally after output cache in the pipeline.
- Uses `RecyclableMemoryStream` for efficient memory handling. Bodies larger than 256 KB skip ETag computation.

---

## Security Headers

Added by `SecurityHeadersMiddleware` to every response:

| Header | Value |
|---|---|
| `X-Content-Type-Options` | `nosniff` |
| `X-Frame-Options` | `DENY` |
| `Referrer-Policy` | `strict-origin-when-cross-origin` |
| `Permissions-Policy` | `camera=(), microphone=(), geolocation=(), payment=()` |
| `Content-Security-Policy` | `default-src 'none'; frame-ancestors 'none'` |

Non-GET responses additionally receive:
- `Cache-Control: no-store`
- `Pragma: no-cache`

---

## Auth And Authorization

### JWT Bearer Configuration
- Authority: Keycloak OIDC metadata endpoint.
- Multi-client audience validation: `islamu-event-api`, `islamu-event-blazor`.
- Custom `AudienceValidator`: checks both `aud` claim and `azp` (Keycloak authorized party) claim.
- Clock skew tolerance: 5 minutes.
- Dev mode: accepts self-signed certificates.
- Minimal JWT event logging: `OnAuthenticationFailed` (Warning), `OnChallenge` (Debug). PII-leaking handlers removed.

### Endpoint Auth Pattern
- `GET`: usually `[AllowAnonymous]`
- `POST/PUT/DELETE`: `[Authorize]`
- Privileged operations: role/policy constrained
- User ID extraction fallback order: `sub` → `nameidentifier` → `sid`.

### MediatR Authorization Behavior
`AuthorizationBehavior` in the pipeline checks:
1. `IAuthorizedRequest` interface — commands/queries declare required permissions.
2. `[AuthorizeResource]` attribute — declarative resource-level authorization.
3. `ISecureRequest` — provides dynamic resource context for permission evaluation.

Denied requests throw `AuthorizationException` → mapped to `403 Forbidden` by exception handler.

### External API Keys (Direct Callers)

Non-interactive callers authenticate with long-lived `X-API-Key` credentials in the form `{keyId}.{secret}`. The endpoint contract is otherwise identical to JWT callers — only the credential presentation and principal shape differ.

**Owner Types** are normalized lookup rows. Write contracts use `externalApiKeyOwnerTypeId`; read contracts expose `externalApiKeyOwnerTypeId`, `externalApiKeyOwnerTypeCode`, and `externalApiKeyOwnerTypeName` so clients do not depend on domain enum serialization.

| ID | Code | Owner | Tenant Binding | Effective Authority |
|---|---|---|---|---|
| `1` | `USER` | User | Required | Key inherits the owner user's memberships (tenant/org/group admin claims) |
| `2` | `ORGANIZATION` | Organization | Required | Acts as organization admin for the owning org within the bound tenant |
| `3` | `GROUP` | Group | Required | Acts as group admin for the owning group within the bound tenant |
| `4` | `TENANT` | Tenant | Required | Acts as tenant admin for the bound tenant |
| `5` | `INSTANCE_ADMIN` | Instance Admin | **Nullable credential** | Platform operator; tenant-scoped API/MCP execution requires an explicit tenant hint, while explicit host-administration routes may run without tenant context |

**Scope Model** (`ExternalApiKeyScopes`): `events:read`, `events:write`, `organizations:read`, `organizations:write`, `groups:read`, `groups:write`, `users:read`, `users:write`, `lookups:read`, `mcp:read`, `registrations:write`, `mcp:propose`, `api-keys:manage`, `admin:tenant`, `admin:instance`. Effective permissions are the intersection of (a) the scopes on the key and (b) the owner's authority ceiling (`ExternalApiKeyScopeCeiling`). Keys cannot hold scopes above their owner type. Anonymous-safe MCP event tools expose only published public event data; program/session tools first require the event detail query to resolve a `Published` + `Public` event before returning lower-level program or session data. Protected MCP event-management reads such as `list_my_events`, `get_event_creation_context`, `get_event_publish_readiness`, `event_management_context`, and the Phase 5 program/custom-property/registration/team/template/sync contexts require a user bearer session or, for API keys, both `mcp:read` and the existing event read scope gate (`events:read`, `events:write`, or tenant/admin equivalent accepted by `MachineScopeMapping`). `mcp:read` alone does not grant private event-management discovery. These reads derive user and tenant context from the authenticated request and existing Application services rather than accepting caller-supplied user, tenant, role, or claim data. The management-context resource derives edit/delete/publish/publish-readiness/add-session/session-create-context availability from REST HAL `_links`, not from MCP-side role checks. The standalone `get_event_publish_readiness` tool also requires the REST HAL `publish-readiness` affordance before it calls `GetEventPublishReadinessRequest`, so MCP cannot broaden publish readiness beyond the current management action surface. MCP-specific scopes are least-privilege AI-conversation and adapter scopes: `mcp:read` allows scoped MCP read discovery, `mcp:read` plus event read scope authority allows protected event-management reads, and `mcp:propose` is required to discover/call MCP proposal tools and prompts without granting event writes or confirmation authority.

**Authentication Flow**:
1. `ApiKeyAuthenticationHandler` parses the `X-API-Key` header, splits `{keyId}.{secret}`.
2. Repository lookup via `IgnoreTenantFilter` (auth path only) returns the stored key.
3. Secret is SHA256-hashed and verified with fixed-time comparison against `SecretHash`.
   Failed authentication attempts are recorded with bounded `outcome`, `tenant_id`, and `owner_type` tags only; raw API keys, secrets, and request paths are never metric tags.
4. `ApiTenantPostAuthenticationMiddleware` asserts the API-key `TenantId` matches the resolved request tenant. Tenant-bound keys with mismatched hints return `404 Tenant mismatch`.
5. `InstanceAdmin` keys remain nullable credentials, but tenant-scoped API and MCP requests must resolve an execution tenant through the normal tenant hint/host pipeline. With an explicit tenant hint, the middleware binds that tenant for the request; without one, tenant-scoped API/MCP calls fail closed with `404` and `code=tenant_required`. Only explicit host-administration API routes continue without tenant context.
6. Principal is materialized with claims `explore:api-key:id`, `explore:tenant:id` (absent for the persisted InstanceAdmin credential), `explore:api-key:owner:type`, `explore:api-key:owner:id`, and repeated `explore:api-key:scope` claims.
7. `TouchUsageMetadata` updates `LastUsedAt`/`LastUsedIp` (5-minute throttle per key).

**Machine Principal Authorization**: `IMachinePrincipalAccessor` exposes the current `ApiKeyPrincipalContext` to both authorization providers. `CerbosPrincipalBuilder.BuildMachinePrincipalAsync` emits a Cerbos principal with `is_machine=true`, `api_key_id`, `owner_type`, `owner_id`, `scopes`, and synthesized `isInstanceAdmin`/`tenantMemberships`/`orgMemberships` attributes derived from owner type. `FallbackAuthorizationService` applies `MachineScopeMapping.ScopesPermit` as a fast-reject gate before dispatching to owner-type-specific authority checks — so machine principals evaluate consistently against both local and Cerbos-backed authorization.

**Management Endpoints** (`/api/ExternalApiKey`):

| Verb | Route | Purpose | Response |
|---|---|---|---|
| `GET` | `/api/ExternalApiKey` | List keys visible to the caller | HAL collection |
| `GET` | `/api/ExternalApiKey/{id}` | Key detail (metadata only, no secret) | HAL resource |
| `POST` | `/api/ExternalApiKey` | Create key — secret revealed **once** in response | HAL resource + secret field |
| `PUT` | `/api/ExternalApiKey/{id}` | Update policy (scopes, expiry, quotas) | HAL resource |
| `DELETE` | `/api/ExternalApiKey/{id}` | Revoke key (soft delete, status=Revoked) | `204 No Content` |
| `GET` | `/api/ExternalApiKey/usage-report` | Tenant admins see their tenant; instance admins see platform-wide | Aggregated report |

Create/revoke/update emit business metrics (`created`, `revoked`, `policy_updated`) tagged with `tenant_id` and `owner_type`.

### Managed Provider Provisioning

Trusted ERP, CRM, and managed-hosting operators provision customers through the managed-provider composition endpoint:

| Verb | Route | Route Name | Purpose | Response |
|---|---|---|---|---|
| `POST` | `/api/managed-provider-provisioning/clients:ensure` | `EnsureManagedProviderClientProvisioned` | Create or rehydrate a provider-customer tenant, external admin identity, tenant-local user state, tenant-admin grant, and optional approved organizer actor. | `BaseCommandResponse<ManagedProviderClientProvisioningResultDto>` |

Security and tenancy rules:

- The endpoint is `[Authorize]`, classified as `EndpointClass.Admin`, and explicitly checks `IAdminContext.IsInstanceAdminAsync`; ERP customer/admin identities are never treated as instance administrators by this path.
- Provider automation identifies the customer with stable `providerKey`, `externalSystem`, and `externalCustomerId`; OIDC user linkage uses stable `identityProvider` + `subject`, not mutable email or display names.
- The Application command writes provider-neutral `ExternalBinding` records for the customer tenant, tenant-local user state, user actor, external login, and optional organizer records. Existing bindings rehydrate the original IDs for retry-safe provisioning.
- Tenant-local user status/profile/moderation state is stored in `TenantUser` and `TenantUserProfile`; the global `User` remains the auth/account anchor.
- Do not send arbitrary tenant headers as provisioning authority. The command creates or resolves the tenant from trusted bindings and returns the resulting internal IDs.
- Send `Idempotency-Key` on HTTP retries. The durable source of truth is still the `ExternalBinding` uniqueness model, so a replay with the same provider/customer IDs returns the existing provisioning result.

### Tenant User Role Grants

Tenant role authority is exposed through explicit role-grant endpoints, not the former tenant-member contract:

| Verb | Route | Route Name | Purpose | Response |
|---|---|---|---|---|
| `GET` | `/api/tenant-user-role-grants` | `GetTenantUserRoleGrants` | List active/revoked tenant-local role grants for tenant admins in the resolved tenant or instance admins. | HAL collection of `TenantUserRoleGrantListDto` |
| `GET` | `/api/tenant-user-role-grants/{id}` | `GetTenantUserRoleGrantById` | Retrieve one tenant-local role grant for an authorized tenant or instance admin. | HAL resource of `TenantUserRoleGrantDto` |
| `POST` | `/api/tenant-user-role-grants` | `CreateTenantUserRoleGrant` | Grant a tenant-scoped role to an active `TenantUser`. | `BaseCommandResponse<Guid>` |
| `DELETE` | `/api/tenant-user-role-grants/{id}` | `RevokeTenantUserRoleGrant` | Revoke a grant and populate revoke audit fields. | `204 No Content` |

Contract rules:

- Create accepts `TenantUserId` and tenant-scoped `RoleId`; tenant identity is derived from `ITenantContext`, not request body `TenantId`.
- Read DTOs are identity-bearing administrative projections. `GET` routes require authentication plus `islamuevent_tenant_user_role_grant` resource authorization for action `view`; regular authenticated users cannot enumerate or inspect tenant role grants.
- Handlers validate that the `TenantUser` belongs to the resolved tenant, is active, is not soft-deleted, and that the role has tenant scope.
- Grant changes are create/revoke, not update-in-place. HAL detail resources may expose `revoke`; collection resources may expose `create`; clients must render actions from `_links`.
- Cerbos/local authorization uses resource kind `islamuevent_tenant_user_role_grant` with `view`, `create`, and `delete` actions.

### Organization Members

Organization membership endpoints expose identity, role, and position data for organization administration:

| Verb | Route | Route Name | Purpose | Response |
|---|---|---|---|---|
| `GET` | `/api/organizationmember/{organizationId}` | `GetOrganizationMembersByOrganization` | List members of one organization for authorized organization or tenant administrators. | HAL collection of `OrganizationMemberDto` |
| `GET` | `/api/organizationmember/member/{id}` | `GetOrganizationMemberById` | Retrieve one organization member for authorized organization or tenant administrators. | HAL resource of `OrganizationMemberDto` |
| `POST` | `/api/organizationmember` | `AddOrganizationMember` | Add or invite an organization member inside the resolved tenant. | `BaseCommandResponse<Guid>` |
| `PUT` | `/api/organizationmember/role` | `UpdateOrganizationMemberRole` | Change a member role or position. | `BaseCommandResponse<Guid>` |
| `DELETE` | `/api/organizationmember/{id}` | `DeleteOrganizationMember` | Remove a member. | `204 No Content` |

Contract rules:

- `OrganizationMemberDto` is an identity-bearing administrative projection. It includes `tenantId`, `organizationId`, `userId`, `userFullName`, `userEmail`, role, and position fields; it is not a public organization profile DTO.
- Member list/detail reads require authentication plus MediatR resource authorization for resource kind `islamuevent_organization_member` and action `view`. Regular authenticated users without tenant-admin or organization-admin authority receive `403`.
- List reads authorize with the resolved tenant id and route organization id. Detail reads authorize by member id, and `AuthorizationBehavior` enriches the resource attributes from the repository before evaluating Cerbos/local fallback policy.
- Create and HAL collection affordances carry the resolved tenant id and organization id so tenant-admin and organization-admin checks use the same resource/action context as the API path.
- Clients must gate member-management UI from HAL `_links` such as collection `create` and item edit/delete links, not from local role or claim inspection.

### Webhook Management

Outgoing product webhooks are managed under `/api/webhooks`. These routes configure ISLAMU Event sending webhook events to external systems through the selected outgoing provider (`Disabled`, `Local`, `Svix`, `Composite`, or `DryRun`). Incoming provider callbacks such as Coop, Osprey, payment, email, or future Svix operational callbacks remain separate integration endpoints under `/api/integrations/*`; they do not require the outgoing provider to be enabled.

| Verb | Route | Route Name | Purpose | Response |
|---|---|---|---|---|
| `GET` | `/api/webhooks/event-types` | `GetWebhookEventTypes` | Public canonical event catalog with schema/example metadata. | `IReadOnlyList<WebhookEventTypeDto>` |
| `GET` | `/api/webhooks/consumers` | `GetWebhookConsumers` | Tenant-scoped webhook consumers/integration owners visible to the caller. | HAL collection of `WebhookConsumerDto` |
| `GET` | `/api/webhooks/consumers/{consumerId}` | `GetWebhookConsumerById` | One tenant-scoped webhook consumer. | HAL resource of `WebhookConsumerDto` |
| `POST` | `/api/webhooks/consumers` | `CreateWebhookConsumer` | Create a tenant-scoped consumer for Local/Svix/Composite/DryRun/Disabled mode. | `BaseCommandResponse<Guid>` |
| `GET` | `/api/webhooks/endpoints` | `GetWebhookEndpoints` | Tenant-scoped webhook endpoints, optionally filtered by consumer. | HAL collection of `WebhookEndpointDto` |
| `GET` | `/api/webhooks/endpoints/{endpointId}` | `GetWebhookEndpointById` | One tenant-scoped webhook endpoint with enabled event subscriptions. | HAL resource of `WebhookEndpointDto` |
| `POST` | `/api/webhooks/endpoints` | `CreateWebhookEndpoint` | Create a Local/Svix-mirrored endpoint and subscribe it to enabled event types. | `BaseCommandResponse<Guid>` |
| `PUT` | `/api/webhooks/endpoints/{endpointId}` | `UpdateWebhookEndpoint` | Update a tenant-scoped webhook endpoint URL, delivery controls, and event type subscriptions. | `BaseCommandResponse<Guid>` |
| `DELETE` | `/api/webhooks/endpoints/{endpointId}` | `DeleteWebhookEndpoint` | Archive a tenant-scoped webhook endpoint while preserving delivery history. | `204 No Content` |
| `POST` | `/api/webhooks/endpoints/{endpointId}/rotate-secret` | `RotateWebhookEndpointSecret` | Rotate the endpoint signing secret reference while preserving a bounded previous-secret overlap window. | `BaseCommandResponse<Guid>` |
| `POST` | `/api/webhooks/endpoints/{endpointId}/test` | `TestWebhookEndpoint` | Schedule a signed LocalProvider test delivery for one active tenant-scoped endpoint. | `BaseCommandResponse<Guid>` |
| `GET` | `/api/webhooks/messages` | `GetWebhookMessages` | Tenant-scoped canonical webhook messages and provider handoff state. | HAL collection of `WebhookMessageDto` |
| `GET` | `/api/webhooks/messages/{messageId}` | `GetWebhookMessageById` | One tenant-scoped webhook message without raw payload material. | HAL resource of `WebhookMessageDto` |
| `GET` | `/api/webhooks/messages/{messageId}/payload` | `GetWebhookMessagePayload` | Separately authorized exact payload bytes while the tenant-scoped retention window remains open. | `WebhookMessagePayloadDto` |
| `GET` | `/api/webhooks/delivery-attempts` | `GetWebhookDeliveryAttempts` | Tenant-scoped LocalProvider delivery attempts, optionally filtered by message or endpoint. | HAL collection of `WebhookDeliveryAttemptDto` |
| `GET` | `/api/webhooks/delivery-attempts/{attemptId}` | `GetWebhookDeliveryAttemptById` | One tenant-scoped delivery attempt with safe HTTP outcome metadata. | HAL resource of `WebhookDeliveryAttemptDto` |
| `POST` | `/api/webhooks/delivery-attempts/{attemptId}/retry` | `RetryWebhookDeliveryAttempt` | Schedule a manual retry from a failed or abandoned LocalProvider delivery attempt. | `BaseCommandResponse<Guid>` |
| `GET` | `/api/webhooks/bulk-replays/preview` | `PreviewWebhookBulkReplay` | Preview bounded eligible and excluded counts for an explicit UTC/consumer/endpoint/event filter. | `WebhookBulkReplayPreviewDto` |
| `GET` | `/api/webhooks/bulk-replays` | `GetWebhookBulkReplays` | List recent tenant-scoped durable replay operations. | HAL collection of `WebhookBulkReplayOperationDto` |
| `GET` | `/api/webhooks/bulk-replays/{operationId}` | `GetWebhookBulkReplayById` | Poll one durable replay operation and its normalized lifecycle evidence. | HAL resource of `WebhookBulkReplayOperationDto` |
| `POST` | `/api/webhooks/bulk-replays` | `ScheduleWebhookBulkReplay` | Queue an idempotent bounded Local replay operation after server-side eligibility re-evaluation. | `202 Accepted` with `BaseCommandResponse<Guid>` |
| `POST` | `/api/webhooks/bulk-replays/{operationId}/cancel` | `CancelWebhookBulkReplay` | Cancel a still-queued operation using its observed concurrency version. | `BaseCommandResponse<Guid>` |
| `POST` | `/api/webhooks/svix/app-portal` | `OpenSvixAppPortal` | Generate short-lived backend-only Svix App Portal access. | `WebhookProviderPortalAccessDto` |

Contract rules:

- `GET /event-types` is anonymous and cacheable lookup data. It exposes registry-driven schema/example metadata and includes persisted event type IDs after startup catalog synchronization so management clients can create endpoint subscriptions.
- Consumer DTOs expose normalized `consumerKindId/name`, `statusId/name`, and `providerModeId/name`; they never expose endpoint secrets.
- Consumer create derives `tenantId` from `ITenantContext`, validates domain enum IDs in the Application handler, sets status to `Active`, and returns conflict ProblemDetails for duplicate tenant-local names.
- Endpoint DTOs expose normalized status fields, provider endpoint ids, bounded timeout/retry/rate-limit settings, last success/failure timestamps, and enabled subscription event types. They never expose `secretRef` or secret material.
- Endpoint create derives `tenantId` from `ITenantContext`, requires an active tenant-local consumer, validates an absolute HTTP(S) URL, stores only the supplied secret reference, rejects duplicate tenant/consumer URLs, and fails closed when requested event type IDs are missing, duplicated, disabled, or unknown.
- Endpoint update replaces URL, delivery controls, and the enabled event-type subscription set after validating all requested event types. It does not rotate signing secrets; secret rotation remains a separate route.
- Endpoint delete is a soft archive operation. Archived endpoints leave active lists and lose mutation HAL affordances while preserving canonical delivery history.
- Endpoint secret rotation accepts `newSecretRef` and optional `previousSecretValidForSeconds` only. It never accepts or returns raw signing secret material, rejects unchanged secret references, increments `secretVersion`, stores the old reference as `previousSecretRef`, and sets a bounded `previousSecretValidUntil` transition window. Repeated calls without an `Idempotency-Key` create distinct rotations.
- Endpoint test scheduling creates a canonical `webhook.test` message plus one LocalProvider delivery attempt for the target endpoint. It requires an active Local or Composite consumer endpoint; Svix-managed endpoint tests belong in the Svix App Portal because Svix owns provider-side endpoint delivery/replay semantics.
- Message DTOs expose tenant, event type, event id, aggregate reference, consumer/provider state, payload hash, and retention timestamps. They intentionally do not expose `payloadJson` or raw sensitive event data.
- Payload reads require the distinct `webhook:view-payload` action. The dedicated response base64-encodes the canonical bytes and includes only content type/encoding, hash, byte length, retention cutoff, and retrieval time. The action writes a mandatory `PAYLOAD_VIEWED` audit before returning data and fails closed if audit persistence fails.
- Payload responses set `Cache-Control: no-store,no-cache` and `Pragma: no-cache`. A missing or cross-tenant message returns the same generic `404`; a known tenant-local message whose bytes are expired or cleared returns `410`. HAL emits `payload` only while the bytes are retained and the caller passes the separate permission check.
- Delivery attempt DTOs expose endpoint/message references, attempt number, status, bounded HTTP status/failure/duration metadata, next retry time, and a safe response-body preview only. They do not expose endpoint secrets, request payloads, authorization headers, or full endpoint responses.
- Manual retry is attempt-based. Only failed or abandoned attempt detail resources may expose `retry`, and the command delegates scheduling to the LocalProvider delivery drain service.
- Bulk replay requires `webhook:bulk-replay`. Preview and execution use the message `MaterializedAt` half-open interval `[fromUtc,toUtc)`, with optional exact consumer, endpoint, and event-type filters. Only terminal Local targets (`DEAD_LETTERED` or `ABANDONED`) can become eligible. Active holds, expired/cleared payloads, inactive endpoints, nonterminal/succeeded Local work, and every provider publication are excluded and counted; provider conflict, unknown, and manual-reconciliation states have distinct exclusion counts and are never guessed or blindly republished.
- Scheduling requires an operator reason and stable `operationKey`. Reusing the key with identical canonical filters returns the existing operation; changing any parameter returns `409`. Configured operation and per-tenant reserved-item ceilings are checked under a tenant advisory lock. The worker rechecks eligibility in its transaction and only changes eligible Local targets to `RETRY_DUE`; ordinary Local claim workers continue to enforce tenant/endpoint fairness, in-flight limits, rate limits, signing, and retry policy. Cancellation is available only in `QUEUED` and requires `expectedConcurrencyVersion`; worker start and cancellation therefore resolve without an ABA race.
- HAL collection resources may expose `create`; active endpoint detail resources may expose `update`, `rotate-secret`, `test`, and `delete`; archived endpoint detail resources expose no mutation affordances. Message resources may expose `delivery-attempts`, `provider-publications`, and the separately authorized retained `payload` relation; retryable attempt detail resources may expose `retry`; Svix or Composite consumer detail resources may expose `open-provider-portal`. Clients must render webhook actions from `_links`, not client-side role checks.
- The Svix App Portal route returns only short-lived URL/token data. The Svix API token is resolved server-side through the configured secret provider and is never sent to Blazor.

### Moderation Reporting Routing And Dashboards

Managed reporting routing APIs expose tenant-owned provider configuration, readiness, and dashboards without returning provider secrets or report payloads.

| Verb | Route | Route Name | Purpose | Response |
|---|---|---|---|---|
| `GET` | `/api/tenant/settings/moderation-reporting/routing-state` | `GetModerationReportingRoutingState` | Current-tenant routing state, lock flags, provider target configured flags, and HAL affordances. | HAL resource of `ReportingRoutingStateDto` |
| `PATCH` | `/api/tenant/settings/moderation-reporting/routing-state` | `UpdateModerationReportingRoutingSettings` | Update supplied policy, Osprey, or Coop groups when their instance delegation locks allow it. Nested credential input is write-only; omitted groups and secret leaves preserve existing values. | `BaseCommandResponse<Guid>` |
| `POST` | `/api/tenant/settings/moderation-reporting/routing-state/test/{provider}` | `TestModerationReportingProvider` | Readiness-check a tenant Osprey or Coop target without external HTTP dispatch or secret output. | `BaseCommandResponse<Guid>` |
| `PATCH` | `/api/instance/settings/moderation-reporting/locks` | `UpdateInstanceModerationReportingProviderLocks` | Independently update general, Osprey, or Coop reporting-provider delegation lock groups. | `BaseCommandResponse<Guid>` |
| `GET` | `/api/tenant/settings/moderation-reporting/dashboard` | `GetTenantModerationReportingDashboard` | Current-tenant aggregate queue and provider-sync health. | HAL resource of `TenantModerationReportingDashboardDto` |
| `GET` | `/api/admin/control-plane/operations` | `GetControlPlaneOperations` | Instance control-plane operations status now includes aggregate `moderation-reporting` provider-sync and tenant lock-impact metrics. | HAL resource of `ControlPlaneOperationsDto` |

Contract rules:

- Routing-state and dashboard reads are tenant-scoped and redacted. They may expose provider target identifiers, configured flags, aggregate counts, and HAL links, but never raw endpoint URLs, API keys, webhook secrets, provider payloads, correlation IDs, report evidence, or raw provider errors.
- Tenant routing PATCH bodies contain optional `policy`, `osprey`, and `coop` groups. Omitted groups preserve persisted values. The general provider lock blocks every routing patch, while provider-specific locks block only a supplied matching provider group.
- Tenant update commands accept endpoint URLs and secret values only as request input. Provider credentials are nested explicitly under the matching provider group. Response DTOs, HAL resources, generated response models, logs, metrics, traces, screenshots, and ProblemDetails must not echo those values.
- Provider test actions are readiness checks over effective routing state. They validate lock state, provider enablement, tenant target presence, and configured endpoint/API-key flags; they do not call external provider endpoints.
- HAL rels `routing-state`, `edit`, `test-osprey-provider`, and `test-coop-provider` are the client action source of truth. Clients must not recreate hidden actions from local roles or claims.
- Reporter-owned communication consent uses `PATCH /api/event-reports/my/{reportId}/communication-consent` with one required `consent` group containing both purpose-specific choices. Ownership, privacy-erasure fencing, audit-neutral unchanged requests, transactional persistence, and user-scoped cache invalidation remain enforced by the existing command handler.

### Incoming Integration Webhooks

Incoming webhooks are provider callbacks received by ISLAMU Event. They are separate from outgoing product webhooks and continue to work when the outgoing provider is `Disabled`, `Local`, `Svix`, `Composite`, or `DryRun`.

| Verb | Route | Route Name | Auth | Purpose |
|---|---|---|---|---|
| `POST` | `/api/integrations/moderation/osprey/callback` | `ModerationIntegrationOspreyCallback` | API-key policy `ModerationIntegration.OspreyCallback` | Records bounded Osprey-compatible moderation signals on the local report without executing moderation actions. |
| `POST` | `/api/integrations/moderation/coop/callback` | `ModerationIntegrationCoopCallback` | API-key policy `ModerationIntegration.CoopCallback` plus signed raw-body HMAC verification | Atomically retains the verified callback and its unique Coop decision-effect pointer. A fenced background worker dispatches the existing decision command and completes the pointer only after command success. |
| `POST` | `/api/integrations/registration/{provider}/{bindingId}/callback` | `RegistrationProviderCallback` | `[AllowAnonymous]` plus provider verifier as authentication | Reads up to 256 KiB, verifies binding/proof through provider-neutral callback services, captures one `registration.provider_submission` incoming-webhook effect, and returns `202 Accepted` for accepted, duplicate, malformed, unknown, stale, or parked evidence except oversize payloads (`413`). |
| `GET` | `/api/admin/incoming-webhook-effects/status?tenantId={tenantId}&limit={limit}` | `GetIncomingWebhookEffectStatus` | Authenticated plus `Webhooks.ViewDelivery` authorization | Returns tenant-scoped safe effect lifecycle rows and HAL item affordances; limit range is `1..200`. |
| `POST` | `/api/admin/incoming-webhook-effects/tenants/{tenantId}/{effectOutboxId}/redrive` | `RedriveIncomingWebhookEffect` | Authenticated plus `Webhooks.RedriveIncoming` authorization | Redrives an eligible dead-lettered effect when `expectedProcessingGeneration` still matches and the retained callback remains replayable. |
| `POST` | `/api/integrations/svix/operational` | `IntegrationSvixOperationalCallback` | `[AllowAnonymous]` with Svix-compatible signature verification as authentication | Accepts Svix operational callbacks without requiring the outgoing provider mode to be Svix. Tenant-addressed payloads are captured in the incoming webhook ledger; instance-level operational payloads are verified and acknowledged without side effects. |

Incoming callback rules:

- Raw request bodies are read before JSON parsing and verified against provider signatures where the provider supplies a signature.
- Signed callbacks enforce bounded body sizes, timestamp tolerance, and constant-time signature comparison.
- Verified tenant-scoped callbacks are stored in `incoming_webhook_messages` before any Application side effect. Coop decision callbacks create a specialized pointer in the intake transaction; command dispatch occurs later outside that transaction.
- Duplicate provider message IDs are treated idempotently and do not re-run side effects.
- The incoming webhook ledger stores the tenant and provider message identifiers needed for idempotency, plus payload hashes, bounded status/failure metadata, and redacted headers only. Logs, metrics, and ProblemDetails use bounded provider/outcome/failure categories and must not include raw payloads, signature headers, secrets, tokens, authorization headers, tenant/user identifiers, provider message IDs, or raw provider errors.
- Coop effect status exposes only internal lifecycle identifiers/state, bounded failure category/detail, attempts, generation/fence, and timestamps. It excludes callback bytes, callback hash, signed provider decision ID, headers, and raw exceptions. HAL emits `redrive` only for a dead-lettered row and remains the client action authority.

### Registration Provider Management

Provider-neutral registration integration management is authenticated, event-scoped, private/no-store, and available only when the parent Event HAL exposes `manage-registration-channels` or `view-registration-provider-health`. It does not include a concrete Phase 10 provider adapter.

| Verb | Route | Route Name | Authority | Purpose |
|---|---|---|---|---|
| `GET/POST` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/connections` | `GetRegistrationProviderConnections` / `CreateRegistrationProviderConnection` | Tenant update for connection writes | List or create provider connections with secret-binding IDs only. |
| `GET/PUT/DELETE` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/connections/{connectionId}` | `GetRegistrationProviderConnection` / `UpdateRegistrationProviderConnection` / `DeleteRegistrationProviderConnection` | Tenant update | Read, update, or soft-delete a connection; delete fails while bindings reference it. |
| `PUT` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/connections/{connectionId}/approved-origins` | `ReplaceRegistrationProviderApprovedOrigins` | Tenant update | Replace the SSRF-safe browser/embed origin allowlist. |
| `GET/POST` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/bindings` | `GetRegistrationProviderBindings` / `CreateRegistrationProviderBinding` | `manage-registration-channels` | List or create draft bindings for exact form/version and mode/trust lookup IDs. |
| `GET/PUT/DELETE` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/bindings/{bindingId}` | `GetRegistrationProviderBinding` / `UpdateRegistrationProviderBinding` / `DeleteRegistrationProviderBinding` | `manage-registration-channels` | Manage draft bindings; published mappings are immutable. |
| `PUT` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/bindings/{bindingId}/mappings` | `ReplaceRegistrationProviderMappings` | `manage-registration-channels` | Replace draft field/option mappings. |
| `POST` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/bindings/{bindingId}/publish` | `PublishRegistrationProviderBinding` | `manage-registration-channels` | Publish when drift is nonblocking. |
| `GET/POST/PUT/DELETE` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/workflows/{workflowId}/requirements/{requirementId}/channels[/{channelId}]` | `GetRegistrationChannels` / `CreateRegistrationChannel` / `UpdateRegistrationChannel` / `DeleteRegistrationChannel` | `manage-registration-channels` | Manage native/provider channel order and fallback. |
| `GET` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/workflows/{workflowId}/requirements/{requirementId}/channels/{channelId}/bindings/{bindingId}/launch-descriptor` | `GetRegistrationProviderLaunchDescriptor` | `manage-registration-channels` | Return one server-generated redirect/embed/manual descriptor from approved connection origins. |
| `GET` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/health` | `GetRegistrationProviderHealth` | `view-registration-provider-health` | Return bounded connection validity, callback age, drift, reconciliation lag, queue depth, and capability codes. |
| `GET/POST/POST/POST` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/queue`, `/manual-imports`, `/queue/retry`, `/queue/resolve` | queue/manual/retry/resolve routes | `manage-registration-channels` | List parked issue codes and manage bounded reconciliation without answers or raw provider payloads. |
| `POST` | `/api/tenants/{tenantId}/events/{eventId}/registration-providers/{bindingId}/reconcile` | `PollRegistrationProviderReconciliation` | `manage-registration-channels` | Request bounded provider reconciliation when the exact tuple has `RECONCILIATION`. |

Returned HAL relations include `manage-registration-channels`, `view-registration-provider-health`, `provider-create`, `origins`, `mappings`, `publish`, `manual-import`, `poll`, `retry`, `resolve`, and `launch-descriptor`. Clients must use those links as authority and must not infer access from roles, provider names, drift state, or capability booleans.

Reporter communication contracts use two required, independently selected booleans: `ReportCaseUpdatesConsent` covers acknowledgements, status updates, and final outcomes, while `ReportFollowUpContactConsent` covers requests for clarification or additional evidence. `POST /api/event-reports`, reporter-owned reads, and moderation reads expose both values; anonymous submissions force both to `false`. The pre-1.0 `ReporterContactConsent` field was removed without a compatibility alias, so clients must regenerate from OpenAPI. `PUT /api/event-reports/my/{reportId}/communication-consent` updates both purposes for the authenticated reporter's own report and returns the refreshed HAL resource. My Reports detail and collection items expose `update-communication-consent` only after the current-user `User/Update` authorization-provider check succeeds, and the write handler repeats that exact provider decision before opening its transaction. Tenant and `ReporterUserId` ownership checks remain defense in depth; missing identity, provider denial, non-owner, tenant mismatch, and indeterminate authorization fail closed. Clients must render withdrawal controls only when that relation exists.

---

## HAL / HATEOAS Implementation

### Architecture
The HATEOAS system uses a layered architecture to ensure "Plug-and-Play" compatibility for all consumers:

1. **`ResourceAssemblerBase<TDto, TListDto>`** — Base class for assembling HAL responses. Implements the high-performance **4-Phase Capability Planning Pipeline**. Most families need no assembly behavior of their own and use the concrete generic **`HalResourceAssembler<TDto, TListDto>`**, registered by `AddHalResource<...>`; declare a subclass only when a family genuinely assembles differently.
2. **`ILinkPolicy<TDto>`** / **`ICollectionLinkPolicy<TDto>`** — Per-entity link definitions using the `yield return` pattern.
3. **`LinkDefinition`** — Metadata for a link, including relation, route, and authorization requirements.
4. **`HateoasAuthorizationEvaluator`** — The engine that batches and deduplicated permission checks.
5. **`HateoasLinkGenerator`** — Resolves named routes to absolute URLs.
6. **`RouteNames`** — 100+ named route constants ensuring type-safe link generation.

### The 4-Phase Capability Planning Pipeline
To prevent $N+1$ performance issues, link generation follows a strict pipeline:
1. **Candidate Selection**: Link policies yield all possible link definitions for the resource(s).
2. **Normalization**: The evaluator extracts `AuthorizationCheck` objects from permission-bearing links.
3. **Batch Decisioning**: Deduplicated checks are sent to the `IAuthorizationProvider` in a **single batch call**.
4. **Materialization**: Authorized links are resolved to URLs and embedded into the `_links` object.

### Collection Endpoint Performance ("Get All")
Collection endpoints use `BuildListResourcesWithBatch` to ensure scalability:
- All link definitions for **all items** in a paginated result are collected first.
- These are flattened into one massive batch (potentially hundreds of checks).
- The evaluator deduplicates identical checks (e.g., if multiple items share the same parent).
- **One single gRPC call** (Cerbos) or **one single profile resolution** (Local) authorizes the entire list.

### Content Negotiation
- Default format: `application/hal+json` with `_links` and `_embedded` sections.
- `Prefer: return=minimal` (RFC 7240) strips all `_links` to save bandwidth for non-UI consumers.
- `PreferHeaderMiddleware` reads the `Prefer` header and sets a flag consumed by assemblers.

### Pagination Links
Collection responses include standard pagination links: `self`, `first`, `prev`, `next`, `last`.
Link policies use `ResourceDescriptors` to extract resource metadata from DTOs, ensuring authorization is context-aware.

### Fail-Closed Security
If the batch authorization call fails (e.g., network error to Cerbos), all permission-bound links are **denied** by default. Non-permission links (e.g., `self`) remain unaffected.

### Lifecycle Affordances

Ordinary fixed Event and EventSession lifecycle links are emitted only when the matching Domain lifecycle predicate accepts the DTO's current facts. Event actions use the current `EventStatusId`; restoration additionally requires the server-projected reversible-moderation-record eligibility. Session detail and collection policies use `EventSessionStatusId`, `ParentEventStatusId`, `StartTime`, `EndTime`, and `EndTimeType`, so publish and complete require a Published parent Event and publish requires a valid schedule. A Published Event never advertises `archive`. Heavy redaction remains a distinct Application/API-owned irreversible safety override rather than an ordinary Domain transition affordance.

HAL omits same-target ordinary fixed lifecycle actions because they do not change resource state, although sending such a command directly is a successful idempotent no-op. A true no-op may emit one structured idempotent outcome log after the unit of work completes for observability, but it does not change state or timestamps and produces no durable write, cache invalidation, or metric. Light moderation is the exception: applying it to an already-Moderated Event may repair missing child-session moderation, producing only the effects required by that repair and no duplicate moderation record, outbox, or federation work. Every advertised lifecycle mutation remains authenticated and permission-bound. Clients must follow `_links` rather than reconstructing these Domain decisions from status fields, parent status, schedule fields, roles, or claims.

### Blazor Client Consumption Pattern

**The API's `_links` payload is the single source of truth for action affordances in the Blazor UI.** The server already evaluated every authorization check and only emitted the links the caller is allowed to follow — the client must trust that contract and render UI affordances directly from it.

#### Canonical pattern
Blazor components gate mutation buttons (Edit, Delete, Create, etc.) with extension helpers defined in `Explore.Blazor.Client/Helpers/HalResourceExtensions.cs`:

```csharp
private void CheckEditPermissions()
{
    canEdit = organization?.HasHalLink("edit") ?? false;
}

// In markup:
@if (canEdit) { <AppButton StartIcon="@Icons.Material.Filled.Edit">Edit</AppButton> }
```

The helpers (`HasHalLink(this OrganizationDto, string)`, `HasHalLink(this EventListDto, string)`, etc.) read `_links` from the generated DTO extension data or from typed client models that explicitly preserve HAL links. Collection-level affordances such as `create` must come from the HAL collection `_links` carried through `PaginatedResult<T>.Links`; they must not be inferred from the first row or from an empty-list fallback.

#### Anti-pattern — do not use
**Never gate mutation UI through client-side role checks** (`RoleHelper.CanManage`, `user.IsInRole("OrgAdmin")`, `ClaimsPrincipal` inspection). These duplicate server-side policy, drift over time, and leak authorization logic into the client. If the server didn't emit an `edit` link, the user is not allowed to edit — period.

#### Permitted exceptions
Role/claim inspection is acceptable **only** outside action-gating contexts:
- Navigation menu filtering (`NavMenu.razor` — determines which top-level sections are visible).
- Eligibility previews for empty-state CTAs (`EventCreationEligibilityService` — does the user belong to any org that could create an event?).
- Client-side route guards that short-circuit before an API call (e.g. redirecting anonymous users away from `/my/*`).

All three cases guard entire pages or menus, not per-resource actions, and none substitute for the authorization decisions the API already encoded in `_links`.

#### DTO contract requirements
Every client DTO consumed for affordance gating must:
1. Preserve item `_links` either through `[JsonExtensionData] IDictionary<string, object>? AdditionalProperties` or an explicit `[JsonPropertyName("_links")]` `Links` property when mapping to a UI model.
2. Preserve collection `_links` on paginated wrappers when a page-level action such as `create` is rendered independently of rows.
3. Have a matching `HasHalLink(this TDto, string linkRel)` extension or wrapper method.
4. **Never** have a corresponding standalone permission flag (e.g. `CanEdit: bool`) on the DTO — permission state must flow exclusively through `_links`.

Notification preference matrices follow this contract: `NotificationPreferenceMatrixDto` is served as a HAL resource, and Blazor gates `save` and `set-mute` exclusively from `_links`.

#### Testing
HAL link consumption is protected by three test layers:
- `Event.API.IntegrationTests/Features/Hateoas/HateoasLinkDeserializationTests.cs` — wire-level regression guard that `_links` survive NSwag round-trip.
- `Event.API.IntegrationTests/Features/Hateoas/OrganizationHateoasAuthTests.cs` — verifies authenticated vs. anonymous requests receive different link sets on embedded items.
- `Explore.Blazor.Client.Tests/Pages/Organizations/OrganizationDetailsHateoasTests.cs` — bUnit component test confirms Edit button renders iff `_links.edit` is present and the page never calls `IOrganizationMemberService.GetMembersAsync` on load.
- `Explore.Blazor.Client.Tests/Helpers/EventTemplateHalResourceExtensionsTests.cs` and `Explore.Blazor.Client.Tests/Pages/Admin/EventTemplateListPageTests.cs` — prove event-template collection `create` links survive empty collections and that page-level create is not inferred from row links.

---

## Specification Pattern (Advanced Query Composition)

The application uses a custom **Specification Pattern** for complex filtering, especially on the `Event` entity.

### Core Interfaces
- **`IQuerySpecification<T>`** — Composes `IFilterSpecification<T>` + `ISortSpecification<T>`. Immutable builder pattern.
- **`IFilterSpecification<T>`** — Individual filter producing `Expression<Func<T, bool>>`.
- **`ISortSpecification<T>`** — Sort directives with field name and direction.

### EventQuerySpecification (Fluent Builder)
`EventQuerySpecification` is an **immutable fluent builder** that composes filters via AND logic:

```
spec = spec
    .And(new EventFilter(...))
    .And(new EventSubqueryFilter(...))
    .And(new IslamicAspectFilter(...))
    .And(new EventCustomPropertyProjectionFilter(...))
    .SortByDescending(EventSort.StartUtc);
```

### Filter Types

| Filter Class | What It Handles | Mechanism |
|---|---|---|
| `EventFilter` | Core fields (search, date, status, type, format) | Direct `Expression<Func<Event, bool>>` |
| `EventSubqueryFilter` | Junction tables (categories, tags, locations, languages, registration modes) + JSONB metadata | Subquery with `Any()` / `All()` |
| `IslamicAspectFilter` | Islamic module fields (madhab, gender mode) | Module-conditional — silently ignored when module disabled |
| `TechAspectFilter` | Tech module fields (skill level, stack) | Module-conditional — silently ignored when module disabled |
| `AspectPresenceFilter` | HasIslamicAspect / HasTechAspect flags | Navigation property null check |
| `EventCustomPropertyProjectionFilter` | Projection-backed custom property discovery/filtering | Projection query composed alongside typed filters |

### Tag/Category Tri-State Filtering
Tags and categories support tri-state AND/OR filtering:
- **Include AND**: all specified tags must be present.
- **Include OR**: any specified tag matches.
- **Exclude AND**: exclude only if ALL specified tags present.
- **Exclude OR**: exclude if ANY specified tag present.

Implemented as separate `EventSubqueryFilterType` enum values.

### JSONB Metadata Filtering
Event metadata stored as JSONB supports two filter types:
- **`JsonContains`** — PostgreSQL `@>` operator for value matching.
- **`JsonKeyExists`** — PostgreSQL `?` operator for key existence check.

### Cache Key Generation
`EventQuerySpecification.ToCacheKeySuffix()` deterministically serializes all active filters and sorts into a cache key suffix for HybridCache integration.

---

## Pagination

Standard pagination via `PaginatedResult<T>`:

| Parameter | Default | Max | Description |
|---|---|---|---|
| `pageNumber` | 1 | — | Current page (1-based) |
| `pageSize` | 20 | 100 | Items per page |

`PaginatedResult.NormalizeParameters()` clamps values to valid ranges. Response includes `TotalCount`, `PageNumber`, `PageSize`, `TotalPages`, `HasPrevious`, `HasNext`.

---

## Response Contracts

1. Create/update flows return `BaseCommandResponse<Guid>` with `Success`, `Message`, `Errors`, `Id`.
2. Many delete flows return `bool` and map to `204 NoContent` or `404 NotFound`.
3. Explicit purge flows return `BaseCommandResponse<CustomPropertyPurgeResultDto>` and are admin-only operations that hard-delete only dependency-free custom-property definitions.
4. Query flows return DTOs or `PaginatedResult<TDto>` wrappers.
5. All responses wrapped in HAL format by default.
6. **`BaseCommandResponse<T>` is a success body only.** A failed command never serializes as the command response; it becomes RFC 7807 ProblemDetails. Do not declare `[ProducesResponseType(typeof(BaseCommandResponse<T>), …)]` for a 4xx or 5xx status.

---

## Error Handling

### Handler-Generated Failures

Not every failure is an exception. A command handler that returns `Success = false` with a `FailureCode`
produces a ProblemDetails response too, through one of two API-layer authorities:

| Authority | Use |
|---|---|
| `CommandFailurePolicy` | A capability with its own failure vocabulary. Declare the table once as a `static readonly` field — `ValidatedBy(validation).NotFound(descriptor, codes…).Conflict(title, detail, codes…)` — then call `Policy.Map(this, response)`. Rules match in declaration order; policies are immutable and compose, so a variant is the base policy plus one rule. |
| `MapCommandResponse` | A capability using the shared `FailureCodes` vocabulary: `not_found` → 404, `admin_required` → 403, `authentication_required` → 401, `concurrency_conflict` → 409, anything else → 400 `ValidationProblemDetails`. |

An unmatched failure code falls through to a validation problem rather than collapsing into an untyped 400,
so distinct failures stay distinguishable to clients. Writing a private `switch` over `FailureCode` in a
controller is forbidden — that is how two endpoints in the same product came to fail in two different formats.

### Chained IExceptionHandler Pattern
Exception handling uses .NET 8+ `IExceptionHandler` chain (not middleware):

1. **`ValidationExceptionHandler`** — Catches `FluentValidation.ValidationException` and `Application.Exceptions.ValidationException`. Returns `400 Bad Request` with structured errors dictionary.
2. **`GlobalExceptionHandler`** — Catches everything else:
   - `BadRequestException` → `400`
   - `NotFoundException` → `404`
   - `AuthorizationException` → `403`
   - `QuotaExceededException` → `422` with type `/problems/quota_exceeded`
   - `ConcurrencyConflictException` → `409` with type `/problems/concurrent_update` or `/problems/stale_sync_base`
   - Unhandled → `500` (detail hidden in production)

All responses use **RFC 7807 ProblemDetails** with extensions:
- `traceId` — from `HttpContext.TraceIdentifier`
- `timestamp` — UTC ISO 8601
- `correlationId` — from `X-Correlation-ID` / `X-Request-ID` header or generated UUID

Validation payloads normalize serializer/model-binding paths before returning
them to callers. JSON-path style keys such as `$`, `$._links`, or other
serializer internals are reported as `body` with a generic invalid-body message
so API responses do not leak parser implementation details or unsupported-field
paths. Unsupported media type responses use `415` ProblemDetails with a stable
title and detail.

Custom-property quota failures use stable extensions `code`, `quotaKey`, `limit`,
`scope`, and optional `actual`/`attempted`. The generic API mapper intentionally
does not emit `tenantId`; tenant identifiers are only safe on explicitly
authorized/admin surfaces.

Template-sync conflicts keep business stale-base conflicts distinct from
technical optimistic concurrency:

| Code | HTTP | Problem type | Meaning |
|---|---:|---|---|
| `concurrent_update` | 409 | `/problems/concurrent_update` | A mutable row changed since the client loaded it. Reload and retry. |
| `stale_sync_base` | 409 | `/problems/stale_sync_base` | The template sync base version changed. Recompute the diff before applying. |

The `type` field uses IANA RFC 9110 standard URIs (e.g., `https://tools.ietf.org/html/rfc9110#section-15.5.5` for 404) instead of httpstatuses.com.

Current implementation detail: `ExceptionHandlingExtensions` writes `traceId` from `HttpContext.TraceIdentifier`.

.NET 10 note: handled exceptions can suppress diagnostics by default once an `IExceptionHandler` returns `true`. `UseApiExceptionHandling()` currently calls plain `app.UseExceptionHandler()` with no `SuppressDiagnosticsCallback` override, so treat handled-exception logging/metrics behavior as an explicit runtime decision.

---

## CORS Policies

Five policies configured in `Program.cs`:

| Policy | Origins | Methods | Credentials | Use Case |
|---|---|---|---|---|
| `InternalAppPolicy` | Configurable | All | Yes | Internal app communication |
| `ExternalAppPolicy` | Configurable | Specific set | No | External API consumers |
| `InternalWebsitePolicy` | Configurable (loaded from `CorsSettings:AllowedOrigins`) | All | Yes | Internal website |
| `ExternalWebsitePolicy` | Configurable | `GET`, `OPTIONS` only | No | External read-only |
| `DevPolicy` | All origins | All | Yes | Development only |

---

## Action Filters

### `BlockInSingleTenantAttribute`
Returns `404 Not Found` in single-tenant mode with hiding enabled. Conceals multi-tenant endpoints from discovery.

### `RequireMultiTenantAttribute`
Returns `403 Forbidden` with error payload when endpoint requires multi-tenant mode.

### `SetupSecretRequiredAttribute`
Gates onboarding endpoints behind the setup secret:
- If setup mode is inactive: returns RFC 7807 `410 Gone` with code `setup_already_completed`.
- If `X-Setup-Secret` header is missing/invalid: returns RFC 7807 `403 Forbidden` with code `forbidden`.
- Setup-secret-gated onboarding endpoints use the named `SetupSecret` rate-limit policy and advertise `429 Too Many Requests` as `ProblemDetails` in OpenAPI.
- Uses `TypeFilterAttribute` pattern for DI-aware filtering with `ISetupSecretProvider`.

---

## MediatR Pipeline Behaviors

| Behavior | Purpose |
|---|---|
| `PerformanceBehavior` | Logs requests taking >500ms as warnings |
| `AuthorizationBehavior` | Checks `IAuthorizedRequest` / `[AuthorizeResource]` attribute; throws `AuthorizationException` on deny. Reflection results cached via `ConcurrentDictionary`. Emits OpenTelemetry activity spans on `Explore.Authorization` source with `resource.kind`, `resource.action`, and `request.type` tags. |

---

## AT Protocol Event Federation Contract

| Endpoint | Contract |
|---|---|
| `GET /api/event` | Anonymous HAL collection of `EventDiscoveryItemDto`. Each item is either the existing local `EventListDto` projection or a bounded `FederatedEventDto`; the federated projection does not return raw provider payloads, credentials, DIDs, record keys, or external source URLs. |
| `GET /api/event/federated/{atprotoRecordId}/source` | Anonymous, globally rate-limited `302` to the current tenant-visible normalized HTTPS source. Disabled capability, missing/tombstoned/cross-tenant records, and unsafe targets all return `404`. |
| `GET /api/settings/instance/atproto-federation`; keyed `/api/settings/instance/atproto-federation/{key}` and `/api/settings/instance/atproto-federation/{key}/lock` mutations | Instance-admin HAL surface for the exact capability and validation-profile keys. Update and lock affordances are server-produced. |
| `GET /api/actor`, `GET /api/actor/{id}`, `GET /api/actor/by-did/{did}` | Anonymous canonical global Actor reads. Responses omit tenant participation IDs, private User ownership, and tenant storage-object identifiers. |
| `GET /api/actor/by-tenant/{tenantId}` | Anonymous tenant-contextual Actor collection containing only locally discoverable Actors, with safe approved participation overrides and tenant-local subscription affordances. |
| `GET /api/actor/by-tenant/{tenantId}/{id}` | Anonymous exact tenant-contextual Actor detail. Hidden or cross-tenant targets return `404`; safe approved participation overrides and tenant-local HAL affordances are composed server-side. |
| `POST /api/actor/{actorId}/moderation/suspend` | Suspend the global Actor. The body contains only `reasonCode`; the route selects `Suspend`. |
| `POST /api/actor/{actorId}/moderation/reinstate` | Reinstate the global Actor. The body contains only `reasonCode`; the route selects `Reinstate`. |
| `POST /api/actor/atproto-identities/{identityId}/moderation/suspend` | Suspend one exact global ATProto identity credential. The body contains only `reasonCode`; the route selects `Suspend`. |
| `POST /api/actor/atproto-identities/{identityId}/moderation/reinstate` | Reinstate one exact global ATProto identity credential without changing `IsActive`. The body contains only `reasonCode`; the route selects `Reinstate`. |
| `GET /api/organizations/{organizationId}/legitimacy-evidence` | Authenticated HAL collection of safe retained evidence metadata for the current tenant participation. |
| `POST /api/organizations/{organizationId}/legitimacy-evidence/upload-session` | Organization-admin creation of a server-bound, private PDF Document upload session owned by the pending participation. |
| `POST /api/organizations/{organizationId}/legitimacy-evidence` | Attach the finalized eligible Document as retained participation evidence. |
| `POST /api/organizations/{organizationId}/legitimacy-evidence/{evidenceId}/review` | Tenant-admin approve/reject decision for one pending evidence row; this does not approve the participation. |

`federation.atproto_events_enabled` is the single capability for tenant presentation of inbound community events and eligible outbound event/RSVP enqueue. `federation.atproto_event_validation_profile=community_lexicon` relaxes only the required local business fields for publication; it does not relax supplied-value validation, authorization, privacy, projection completeness, or record validation. Outbound publication additionally requires the owner's self-scoped `federation.atproto_publish_my_events` consent and one exact linked encrypted ATProto session.

The four moderation POST routes are authenticated instance operations. Their commands authorize update of the instance setting resource `global-actor-moderation`, then handlers recheck instance-admin authority before target lookup. A tenant administrator cannot mutate either global state. Same-state retries return success without another aggregate update or moderation record. Every accepted request invalidates HybridCache Event tags and output-cache discovery, detail, home, and sitemap tags.

Event publication is database-first: the committed local lifecycle mutation and immutable `PdsSyncOutbox` intent share one transaction, and CarpaNet PDS I/O occurs later under a fenced worker claim. Every eligible public event/session/aspect/resolved-lookup/EAV value must be mapped natively or rendered into the one community event `description`; coverage, privacy, JSON/DAG-CBOR size, or validation failure prevents enqueue, with no truncation or silent omission. RSVP egress represents only a committed active registration as `community.lexicon.calendar.rsvp#going`, ignores organizer approval state, and remains blocked until the event's settled URI/CID can form the exact `strongRef`.

Ingress uses one globally leased Jetstream consumer for exactly `community.lexicon.calendar.event` and `community.lexicon.calendar.rsvp`. Canonical DID/collection/record-key state, current source version, typed event projection, tenant presentation, quarantine/tombstone effects, and cursor advancement are persisted atomically. Public clients must treat HAL links as action authority: federated items have no write affordances, and `source` exists only when the server can safely resolve the internal redirect route.

Public Event reads require a published, public, non-deleted Event and active Actor. Local User Events additionally require an active `TenantUser`; local Organization and Group Events require approved, visible, unsuspended participation, without rechecking organizer eligibility. Inbound federated Events instead require the current visible tenant presentation, non-tombstoned record, and exact active DID identity owned by the Actor. Anonymous child reads inherit the same parent gate. Authorized management detail remains available through `view-management` when public eligibility fails, and HAL omits public affordances from that management representation.

Inbound projection discovery keeps public Draft, Cancelled, and Completed projections, deduplicates Published projections to the local Event branch, and hides Moderated, Archived, deleted, non-public, tombstoned, stale-presentation, or identity-ineligible projections. The exact source redirect applies the same base gate. Outbound planning skips an ungrounded ineligible Create, converts a grounded ineligible Update to a fenced Delete, and rechecks identity, session, source version, ownership, record key, and CID fences at delivery. RSVP behavior is unchanged.

The removed raw `/api/atprotorecord`, `/api/indexeddid`, `/api/userexternallogin`, `/api/actorkeystore`, and `/api/syncstate` surfaces have no compatibility aliases. Clients cannot assert provider, DID, PDS, key, tenant, user, encrypted signing material, or ingestion cursor state through generic CRUD. Authenticated session-metadata reads and idempotent local session deletion remain credential-free; verified authentication, fenced Jetstream ingestion, and other dedicated federation internals are the only authorities over linked identity and provider-owned state. The checked-in OpenAPI contract and [API Contract Inventory](API_CONTRACT_INVENTORY.md) are the route/schema authority.

Generic Actor `POST`, `PATCH`, and `DELETE` routes are also absent. Actor creation, promotion, identity linking, and retirement are dedicated verified onboarding, federation, moderation, or consolidation workflows rather than browser-authored identity CRUD.

---

## Event Participation Contract

Every event has a typed `EventParticipationConfiguration` read projection with normalized handling-mode, advance-registration-obligation, and optional identity-access lookup facts plus guest-recovery policy and its own concurrency stamp. The former `isRegistrationRequired` and `externalRegistrationUrl` fields do not exist.

Organizers update this isolated resource through authenticated `PATCH /api/events/{eventId}/participation` with the configuration concurrency stamp as one required strong quoted GUID entity tag in `If-Match`. Authorization uses the organizer/assignment-only `manage-registrations` event action; listing contributors, tenant administrators, and instance administrators receive no automatic authority. Clients discover the capability through `configure-participation` rather than inspecting roles or claims.

Public participation is HAL-authored and fail-closed for published public events only. `INFORMATION_ONLY` and `WALK_IN` emit no participation CTA. `EXTERNAL_MANAGED` emits at most one `external-registration` relation selected from reviewed active stored public actions and routed through the stored-ID redirect. `PLATFORM_MANAGED` emits permission-bound `start-registration` for authenticated callers and `sign-in-to-register` for anonymous callers; both target the existing protected native registration operation. Clients must not render a CTA from mode fields or raw URLs. External labels distinguish an unverified source (`View original event page`) from organizer authority (`Register on organizer website`).

The anonymous redirect records only the bounded `explore.event_public_actions.engagements` metric dimensions `action_kind`, `surface`, and `outcome=redirect_issued`. It creates no engagement row, captures no identity or event/action identifier in labels, and never claims that a registration completed.

---

## Event Registration Read Contract

Generic event-registration reads are authenticated self-service contracts. `/api/eventregistration`, `/api/eventregistration/by-session/{eventSessionId}`, `/api/eventregistration/by-user/{userId}`, and `/api/eventregistration/{id}` return only registrations owned by the authenticated user; cross-user route IDs fail with `403 Forbidden`. `EventRegistrationDto` and `EventRegistrationListDto` intentionally omit serialized user identity fields (`userId`, `userFullName`, `userEmail`). Organizer attendee-management views require a separate resource-authorized projection instead of these generic routes.

---

## Business Metrics (OpenTelemetry)

Meter name: `Explore.Business`. Tags vary by counter and include dimensions such as `tenant_id`, `event_type`, `resource`, `action`, `result`, and `owner_type` where those dimensions apply.

| Counter | Description |
|---|---|
| `explore.events.created` | Events created |
| `explore.events.published` | Events published |
| `explore.registrations.created` | Event registrations |
| `explore.event_public_actions.engagements` | Stored public-action redirects issued, using only bounded `action_kind`, `surface`, and `outcome` tags |
| `explore.organizations.created` | Organizations created |
| `explore.authorization.decisions` | Authorization check outcomes |
| `explore.external_api_keys.created` | External API keys created |
| `explore.external_api_keys.revoked` | External API keys revoked |
| `explore.external_api_keys.authentication_attempts` | External API-key authentication attempts with bounded `outcome`/`tenant_id`/`owner_type` tags only |
| `explore.external_api_keys.throttled` | External API-key throttling events |
| `explore.external_api_keys.policy_updated` | External API-key policy updates |
| `explore.external_api_keys.rotated` | External API-key rotations |
| `explore.idempotency.cleanup_runs` | Expired idempotency cleanup attempts by bounded `mode` and `outcome` tags |
| `explore.idempotency.cleanup_rows` | Expired idempotency cleanup eligible/deleted row counts by bounded `mode` and `outcome` tags |
| `explore.notifications.fanout_runs` | Notification fanout run outcomes by bounded `tenant_id`, `fanout_kind`, and `outcome` tags |
| `explore.notifications.fanout_subscribers` | Aggregate notification fanout subscriber decisions by bounded `tenant_id`, `fanout_kind`, and `outcome` tags |
| `explore.event_reports.submissions` | Event-report intake outcomes by bounded `tenant_id`, `outcome`, and `failure_category` tags |
| `explore.event_reports.workflow_actions` | Moderation report triage/assign/decide/execute outcomes by bounded `tenant_id`, `action`, `outcome`, and `failure_category` tags |
| `explore.event_reports.provider_syncs` | Osprey/Coop provider sync outcomes by bounded `tenant_id`, `provider`, `outcome`, and `failure_category` tags |
| `explore.event_reports.provider_callbacks` | Moderation provider callback outcomes by bounded `tenant_id`, `provider`, `outcome`, and `failure_category` tags |
| `event_role_assignment.changed` | Event role assignment changes |

Authorization decisions are also traced via `ActivitySource` named `Explore.Authorization` with `resource.kind`, `resource.action`, and `request.type` tags.

---

## Background Services

### OutboxProcessor
- Polls `outbox_messages` table for pending events at configurable interval (default 5s).
- Processes in batches (default 100) with optimistic locking (`TryMarkAsProcessing`).
- Dispatches via `IOutboxMessageDispatcher`; current routing is handled by `CompositeOutboxMessageDispatcher`, which sends `EventPublishedNotificationFanoutRequested` to internal notification fanout and fails closed for retired external broker event types.
- Exponential backoff retry: `InitialRetryDelaySeconds × 2^retryCount`, capped at `MaxRetryDelaySeconds`.
- Dead-letters messages after `MaxRetryCount` exhausted.
- Configuration section: `OutboxProcessor` (Enabled, PollingIntervalSeconds, BatchSize, MaxRetryCount, InitialRetryDelaySeconds, MaxRetryDelaySeconds, VerboseLogging).

### Notification Refresh SSE

- `GET /api/notification/stream` is an authenticated `text/event-stream` endpoint for one-way notification refresh hints.
- The stream emits `notification-refresh` events with minimal unread-count state only; notification bodies, entity IDs, user IDs, deduplication keys, and PII are not sent through SSE.
- The endpoint disables request timeout, sends no-store/no-cache headers, and sets `X-Accel-Buffering: no`. Do not add `text/event-stream` to response compression or proxy buffering rules.
- Existing notification list/detail/unread APIs remain the source of truth. Blazor keeps polling as fallback if the SSE stream disconnects or is unavailable.

### AI Assistant Run Polling

- AI assistant run progress uses authenticated polling as the supported transport. `POST /api/ai/assistant/conversations/{conversationId}/messages` returns `202 Accepted` with a `Location` route to `GET /api/ai/assistant/conversations/{conversationId}/runs/{runId}`.
- Send-message requests accept `mode: "ask" | "build"`. `ask` is text-only and disables action schemas/tool proposals; `build` permits proposal-only actions such as event-draft creation, still requiring HAL affordance checks and explicit user confirmation before any write side effect runs.
- The run-status response is a HAL resource. It includes `self` and conversation `up` links, plus `cancel-run` only while the run is queued or in progress.
- Streaming is intentionally not part of the current AI assistant contract. `ai_assistant.streaming_enabled` remains disabled until a separate hardening slice covers transport buffering, cancellation, timeout behavior, authentication, logs, and non-streaming fallback.
- Polling responses must remain safe metadata only: status, provider label, model label, timestamps, bounded failure code/message, and HAL links. Do not return prompt content, provider response bodies, tool payloads, provider request IDs tied to content, endpoint URLs, API keys, or raw provider exceptions.

### PdsSyncWorker
- Polls `PdsSyncOutbox` for committed, due AT Protocol event/RSVP delivery intents and reconciles missing active RSVP intents in bounded pages.
- Claims rows with owner, token, monotonic fence, and expiring lease so crashed workers are reclaimable and stale workers cannot settle or fail a successor claim.
- Rechecks effective capability, the owner's current self-consent, exact linked DID/session, source version, public-location privacy, and immutable payload immediately before CarpaNet PDS I/O.
- Retries the same stable record key with bounded exponential backoff; permanent or exhausted failures are dead-lettered with a stable failure code, never a provider response body.
- Settles URI/CID, canonical record ownership/presentation, outbox completion, and the local Event's `AtprotoRecordId` transactionally. RSVP claims remain blocked until the event URI/CID strong reference exists.

---

## Graceful Shutdown

- Grace period: 25 seconds on `SIGTERM`.
- Health checks return `503` during shutdown for load balancer draining.
- Uses cooperative cancellation via `app.Lifetime.StopApplication()`. `Console.CancelKeyPress` sets `isShuttingDown` flag and triggers graceful stop.
- `Kestrel.KeepAliveTimeout`: 30 seconds.
- `Host.ShutdownTimeout`: 30 seconds.

---

## Idempotency

Write operations support the `Idempotency-Key` HTTP header for safe retries:
- Client sends `Idempotency-Key: <UUID>` on POST/PUT/PATCH/DELETE requests.
- Every endpoint marked `RequireIdempotencyKey` publishes a required, non-nullable OpenAPI header parameter; generated clients therefore require the argument rather than relying on an implicit hook.
- Server atomically persists an in-progress claim by `(Key, TenantId)` in PostgreSQL before dispatching the write.
- Duplicate requests within 24 hours replay the cached response with original status code when the original response was persisted.
- Ordinary fixed Event and EventSession lifecycle commands also treat a request for the current target status as a successful no-op: no lifecycle state, audit timestamp, or downstream side effect is changed. HAL does not advertise these same-target actions. Already-Moderated light moderation may instead repair missing child-session moderation; only actual repair effects run, without duplicating the moderation record, outbox, or federation work.
- Reusing the same key for a different write request is rejected with `409 Conflict`. The request identity includes method, normalized target, content type, request-body hash, and a principal fingerprint.
- Capability-scoped payment writes hash the capability into that fingerprint. A matching completed key re-executes the current access guard instead of blindly replaying, so capability revocation or order expiry returns the same uniform `404`; durable payment claim/retry state keeps the repeated valid operation idempotent.
- A matching request while the original claim is in progress receives `409 Conflict` with code `idempotency_request_in_progress`; it must retry for the completed replay.
- Required claim or result persistence failures return `503 Service Unavailable` with code `idempotency_unavailable`, never a successful write response.
- Persisted responses must have status `200` through `499`, body size at or below 1 MB, and blank, `application/json`, or `application/problem+json` content type.
- `5xx`, large, or non-JSON responses are not persisted for replay.
- Keys expire after 24 hours for replay eligibility. Expired rows are ignored by reads; the `idempotency-cleanup` Quartz job physically deletes expired rows after the configured `IdempotencyCleanup:ExpirationGraceHours` safety buffer.
- Entity: `IdempotencyRecord` with `Key`, `TenantId`, request fingerprint fields, `StatusCode`, `ResponseBody`, `CreatedAt`, and `ExpiresAt`.

---

## Response Compression

- Algorithms: Brotli + Gzip at `CompressionLevel.Fastest`.
- Enabled for HTTPS.
- Additional MIME types: `application/json`, `application/hal+json`.

---

## Multi-Tenancy In API

1. Tenant context is resolved per request.
2. Resolution behavior:
   - `SingleTenant`: default tenant is bound immediately.
   - `MultiTenant`: `ApiTenantResolutionMiddleware` resolves trusted `X-Tenant-Slug` first, then normalized `Request.Host.Host` after forwarded-header processing; unresolved non-API-key requests fail closed with `404`.
   - API-key requests may carry a requested tenant hint through pre-auth middleware and are finalized by `ApiTenantPostAuthenticationMiddleware`, which can return `404 Tenant mismatch`, `404 tenant_required`, or `401 API key authentication failed`.
3. EF query filters enforce tenant scoping in persistence.
4. **Hierarchical Settings**: Governance settings follow a 5-tier resolution cascade: User → Group → Organization → Tenant → Instance. Resolution is performed in batch via `HierarchicalSettingsResolver` with support for instance-level locks and single-tenant bypass.

## Key Endpoint Groups

1. Core events:
   - `GET /api/event` — list with full specification pattern filtering
   - `GET /api/event/{id}` — detail with HATEOAS links
   - `GET /api/event/public/{slugCode}/og-image` — anonymous same-origin 1200x630 PNG for eligible published/public events; rechecks eligibility on every request, returns a strong quoted ETag, varies by `Host` and `X-Tenant-Slug`, and returns `304 Not Modified` for a matching `If-None-Match` without shared output caching
   - `GET /api/event/{id}/management-detail` — authenticated management detail, including draft/internal/moderated events visible to the principal through event `view-management`
   - `GET /api/event/{id}/moderation/history` — authenticated safe moderation audit history for authorized management views
   - `GET /api/event/management/by-actor/{actorId}` — authenticated actor-owned management list, including hidden rows authorized by per-event `view-management`
   - `GET /api/event/{id}/publish-readiness` — authenticated/resource-authorized publish readiness diagnostics
   - `POST /api/event` — create
   - `POST /api/event/import` — authenticated import/backfill create with provenance
   - `POST /api/event/with-sessions` — create with sessions in one request
   - `POST /api/event/{id}/publish` — publish after readiness and concurrency validation
   - `POST /api/event/{id}/archive` — archive after concurrency validation
   - `POST /api/event/{id}/cancel` — cancel after concurrency validation
   - `POST /api/event/{id}/moderation/light` — light moderation; exposed by HAL relation `moderate-light` when the caller has moderation authority
   - `POST /api/event/{id}/moderation/heavy` — irreversible heavy redaction; exposed by HAL relation `moderate-heavy` only after backend redaction, storage-deletion retry, and generic notification safety are available
   - `POST /api/event/{id}/moderation/unmoderate` — restore a reversibly light-moderated event to `Published`; exposed by HAL relation `unmoderate` only when the latest moderation record allows unmoderation
2. Event sessions and program items:
   - `GET /api/eventsession` / `GET /api/eventsession/{id}` / `GET /api/eventsession/by-event/{eventId}` — anonymous public session reads; only scheduled, published sessions under public published events are returned
   - `GET /api/eventsession/management/by-event/{eventId}` — authenticated management read that can return draft/internal sessions when authorized
   - `POST /api/eventsession/drafts` — create an unscheduled draft session under an event
   - `POST /api/eventsession/{id}/schedule` — assign a real schedule and local projections after concurrency validation
   - `POST /api/eventsession/{id}/publish` — publish a scheduled session after parent-event/readiness checks
   - `POST /api/eventsession/{id}/cancel` — cancel a draft/submitted/review/approved/published session after concurrency and parent-event lifecycle validation
   - `POST /api/eventsession/{id}/complete` — complete a published session after confirming the parent event is still published
   - `POST /api/eventsession/{id}/archive` — archive a draft, cancelled, or completed session after concurrency and parent-event lifecycle validation
   - `EventSessionDto` and `EventSessionListDto` expose `parentEventStatusId`, nullable `startTime`, `endTime`, and local projection fields. Lifecycle `_links` are computed from the current session status, parent Event status, and full schedule shape; clients must not infer actions from those fields. Use `concurrencyStamp` when following advertised writes.
3. Aspect endpoints:
   - Islamic: `GET /api/event/{id}/aspects/islamic` (`GetEventIslamicAspect`), `POST` (`CreateEventIslamicAspect`), grouped `PATCH` (`UpdateEventIslamicAspect`), and `DELETE` (`DeleteEventIslamicAspect`).
   - Tech: `GET /api/event/{id}/aspects/tech` (`GetEventTechAspect`), `POST` (`CreateEventTechAspect`), grouped `PATCH` (`UpdateEventTechAspect`), and `DELETE` (`DeleteEventTechAspect`).
4. Module governance:
   - `/api/module/*` (`available`, `enabled`, `enable`, `disable`, `schema`)
5. Public experience:
    - `GET /api/publicexperience/settings`
    - `POST /api/a/t` — anonymous-safe analytics relay for relay transport mode
6. Federation:
   - `GET /api/event` — anonymous typed local/federated event discovery; federated items appear only for an effectively enabled tenant and are de-duplicated against local ATProto ownership.
   - Accepted inbound community events are imported internally through `ImportAtprotoFederatedEventCommand` into tenant-local `Event` and `EventSession` rows. There is no public ATProto-import endpoint; the normal event/session read APIs expose the mapped aggregates, while the canonical record retains the complete accepted source JSON.
   - `GET /api/event/federated/{atprotoRecordId}/source` — anonymous, rate-limited redirect to the currently tenant-visible bounded HTTPS source. Clients render this action only from the item-level `source` HAL relation.
   - `GET /api/event/my` — authenticated local event list with optional `atprotoDeliveryStatus` and stable `atprotoDeliveryFailureCode`; no provider body is returned.
   - `GET|PUT|DELETE /api/settings/instance/atproto-federation...` — instance administrator read/update/reset and lock/unlock operations for the two administrator ATProto federation settings, with HAL as action authority.
   - `/api/indexeddid/*` — DID indexing metadata under existing authorization.
   - `/api/auth/atproto/*` — server-private bootstrap/current/refresh/revoke bridge, excluded from public OpenAPI and generated browser clients.
   - No `/api/atprotorecord` CRUD/read contract exists. Lifecycle-owned outbox delivery and canonical Jetstream ingestion are the only `AtprotoRecord` write authorities.
7. Notifications (all `[Authorize]`):
       - `GET /api/notification` — paginated list with `?isRead=` and `?notificationTypeId=` filters
      - `GET /api/notification/{id}` — detail
      - `GET /api/notification/unread-count` — unread count (partial index optimized)
      - `GET /api/notification/stream` — SSE unread-count refresh hints (`text/event-stream`)
      - `PATCH /api/notification/{id}/read` — mark single as read (idempotent)
     - `POST /api/notification/read-all` — bulk mark all as read (YouTube-style, timestamp cutoff)
     - `DELETE /api/notification/{id}` — soft delete
     - `GET /api/notification/preferences/me` — current user's HAL notification preference matrix
      - `PATCH /api/notification/preferences/me` — patch supplied current-user preference cells
      - `PUT /api/notification/preferences/me/mute` — set current-user non-essential notification mute state
      - `GET|PATCH /api/organization/{id}/notification-preferences` plus `PUT .../mute` — organization-scoped notification preferences
      - `GET|PATCH /api/group/{id}/notification-preferences` plus `PUT .../mute` — group-scoped notification preferences
     - `GET /api/notification/web-push/config` — public enabled flag and VAPID public key only
     - `GET /api/notification/web-push/subscription?deviceIdentifier=...` — safe current-device subscription status
     - `POST /api/notification/web-push/subscriptions` — enroll or refresh the current browser
     - `DELETE /api/notification/web-push/subscriptions/{subscriptionId}` — deactivate an owned browser subscription
8. Actor subscriptions (all `[Authorize]`):
   - `GET /api/actor-subscriptions` — current user's paged actor subscriptions
   - `GET /api/actor-subscriptions/actors/{targetActorId}` — current user's subscription state for a target actor
   - `POST /api/actor-subscriptions` — subscribe to an organization/group actor
   - `PATCH /api/actor-subscriptions/actors/{targetActorId}/notification-level` — patch the route-owned subscription notification-level group with a concurrency stamp
   - `DELETE /api/actor-subscriptions/actors/{targetActorId}` — unsubscribe with concurrency stamp
9. Footer management:
   - `GET /api/footer/config`: public footer config (`AllowAnonymous`)
   - `GET /api/footer/settings`: authenticated scalar settings, typed social links, governance locks, and HAL `edit` / `manage-link-groups` capabilities
   - `PATCH /api/footer/settings`: presence-aware `general`, `template`, `description`, `socialLinks`, and `copyright` groups; omitted leaves and instance-locked scalar leaves are preserved
   - `GET /api/footer/link-groups`: list link groups (`Authorize`)
   - `GET /api/footer/link-groups/{id}`: link group detail (`Authorize`)
   - `POST /api/footer/link-groups`: create link group; requires authenticated tenant update authorization
   - `PATCH /api/footer/link-groups/{id}`: update supplied link-group fields; requires authenticated tenant update authorization
   - `DELETE /api/footer/link-groups/{id}`: delete link group; requires authenticated tenant update authorization
   - `POST /api/footer/link-groups/reorder`: reorder link groups; requires authenticated tenant update authorization
   - `POST /api/footer/link-groups/{groupId}/links`: create link in group; requires authenticated tenant update authorization
   - `PATCH /api/footer/links/{id}`: update supplied link fields; requires authenticated tenant update authorization
   - `DELETE /api/footer/links/{id}`: delete link; requires authenticated tenant update authorization
   - Link mutations remain explicit operations and repeat the effective link-group governance check server-side. Clients render link management only when the settings resource includes `manage-link-groups`.
10. Actor appearance:
   - Actor entities include appearance fields (BackgroundColor, BackgroundEffect, BannerColor, BannerPictureId, BackgroundImageId) managed via actor update endpoints.
11. Instance MCP governance:
   - `GET /api/instance/settings/mcp` — instance MCP runtime enablement and tenant override lock state.
   - `PUT /api/instance/settings/mcp` — update `mcp.enabled`, `mcp.enable_legacy_sse`, `governance.lock_tenant_mcp`, and `governance.lock_tenant_mcp_legacy_sse`.
    - Endpoint path and stateless mode remain startup-only and are not exposed as runtime-editable fields.
12. Authenticated UI shell context:
    - `GET /api/ui-shell/context` returns the current tenant's server-authoritative workspace availability, organization/group publisher actors, explicitly authorized settings scopes, deployment mode, organization-centric pinned actor, and resolved navigation defaults.
    - The response is a plain DTO, requires authentication, sends `Cache-Control: private, no-store`, and is never shared with the anonymous public-experience shell. Instance administration alone does not grant Studio or Tenant settings access.

---

## OpenAPI Export And Client Generation

1. Building `Explore.API/Explore.API.csproj` in `Release` runs ASP.NET Core build-time OpenAPI generation and refreshes the checked-in `schemas/openapi_islamu-event.json` contract.
2. Contract invariant and parity tests assert the runtime `/openapi/islamu-event.json` shape without writing generated files.
3. `Explore.ApiContractInventory` writes the committed endpoint inventory to [API_CONTRACT_INVENTORY.md](API_CONTRACT_INVENTORY.md).
4. HAL schema transformers shape OpenAPI schemas so generated clients preserve HAL extension data.
5. `Explore.Blazor.Client/Explore.Blazor.Client.csproj` uses `schemas/openapi_islamu-event.json` as NSwag input and regenerates `Explore.Blazor.Client/Clients/EventApiClient.g.cs` before `CoreCompile`.
6. DTO changes should follow API-first regeneration workflow (see `docs/CONTRIBUTING.md`).

Public HAL detail wrappers must be registered in `Explore.API/OpenApi/HalOpenApiSchemaCatalog.cs`. If a new `HalResourceOf*Dto` wrapper is omitted, OpenAPI can emit an empty wrapper schema and generated clients lose the DTO fields even though runtime HAL responses are correct.

For contract changes, regenerate from server DTOs in this order:

```bash
dotnet build src/Explore.API/Explore.API.csproj --configuration Release --no-restore --verbosity minimal -maxcpucount:1
dotnet run --project eng/tools/Explore.ApiContractInventory/Explore.ApiContractInventory.csproj --configuration Release
dotnet msbuild src/Explore.Blazor.Client/Explore.Blazor.Client.csproj /t:GenerateApiClient /p:Configuration=Release /p:Restore=false /m:1 /v:minimal
```

The API build is the provenance for `schemas/openapi_islamu-event.json`; the named integration test generates `docs/API_CONTRACT_INVENTORY.md`; the NSwag target consumes that schema and generates `Clients/EventApiClient.g.cs`. These artifacts are never hand-edited.

The generated contract now includes `ImportEvent`, `CreateDraftEventSession`, `ScheduleEventSession`, `PublishEventSession`, `CancelEventSession`, `CompleteEventSession`, and `ArchiveEventSession` operations. NSwag emits nullable client properties for draft-capable session schedule fields, so callers must handle `DateTimeOffset?` and `TimeSpan?` for session schedule/local projections.

Before v1.0, intentional breaking API contract changes may be accepted when they make the API, HAL affordances, or generated OpenAPI contract cleaner. They still require an entry in [API_CHANGELOG.md](API_CHANGELOG.md), regenerated OpenAPI/inventory/generated-client artifacts through the documented workflow when applicable, and retained contract-governance evidence. Do not hand-edit `schemas/openapi_islamu-event.json`, `docs/API_CONTRACT_INVENTORY.md`, or generated NSwag client output. At v1.0, breaking schema diffs become blocking per governance.

## Registration Refunds And Event-Change Protection

- Account buyers read the private payment projection at `GET /api/events/{eventId}/registration-orders/{orderId}/payment`. `refundedAmountMinor` follows exact provider-proven buyer-refund evidence even if platform-fee settlement still needs operator action; an individual operation is named `Refunded` only after both legs are exact. Requested, pending, action-required, unknown, failed, and cancelled outcomes remain distinct.
- `POST .../{orderId}/payment/refunds` is an authenticated, account-owner-authorized, rate-limited, idempotent event-cancellation refund request. The `request-refund` HAL relation is its UI authority.
- `POST .../{orderId}/payment/material-change-choice` records the account owner's closed-set `accept_new_terms` or `request_refund` response. The `respond-material-change` relation appears only while a pending durable choice exists.
- Studio reads `GET .../{orderId}/payment/studio` and may create a bounded refund with `POST .../{orderId}/payment/studio/refunds` only through `manage_paid_event_commerce` and the `create-refund` relation. A definitively provider-blocked settlement exposes `retry-refund`; `POST .../{orderId}/payment/studio/refunds/{refundAttemptId}/retry` requeues that same attempt for authoritative reconciliation without creating another buyer refund.
- Event-authorized campaign operations are `GET /api/events/{eventId}/refund-campaigns`, `GET /api/events/{eventId}/refund-campaigns/{campaignId}`, and idempotent `POST .../{campaignId}/resume`. Campaign resources expose only bounded progress counters, omit free-text decision reasons, and emit `resume-refund-campaign` only for resumable state. Resume explicitly requeues provider-blocked attempts; automatic reconciliation never repeats a definitive provider rejection.
- Every response is private/no-store. Provider account, payment/refund IDs, idempotency material, request IDs, raw provider errors, and purchaser PII are not part of the contract.

---

## Ticket Purchase Governance

Purchase authority is exposed as two private, idempotent HAL operations:

| Caller | Route | Authority |
|---|---|---|
| Authenticated account | `POST /api/events/{eventId}/registration-orders/{orderId}/purchase-authority` | Current account plus a server-verified personal/group/organization actor |
| Guest capability | `POST /api/events/{eventId}/registration-orders/guest/{orderId}/purchase-authority` | Opaque order capability plus persisted verified-contact or order-scoped name-only mode |

The JSON request can select access mode and actor context where applicable. It cannot supply tenant, account, normalized-contact hash, enforcement key, quantity, policy version, or idempotency authority. Quantity comes from persisted order lines, current policy lineage is selected inside Application, and the BFF creates the durable operation key.

Both routes use write/public-transactional rate limits, require the HTTP idempotency header, return private/no-store responses, and expose stable `400`/`403`/`404`/`409`/`503` shapes. A successful HAL resource publishes `supportsHardCrossOrderCeiling` and `enforcementScopeCode`; name-only mode reports order scope rather than claiming cross-order person identity.

`RegistrationOrder` HAL resources publish `reserve-purchase-authority` only while the order is currently payable. OpenAPI and `EventApiClient.g.cs` are generated artifacts; the purchase request, result, enum, and flattened `HalResourceOfTicketPurchaseGovernanceResource` schemas are covered by generated-contract tests.

## Fair Return Waitlist API

The private exact resource is `GET|POST|DELETE /api/events/{eventId}/registration-orders/{registrationOrderId}/lines/{registrationOrderLineId}/waitlist`. Offer acceptance and supply withdrawal are subordinate pointer routes. Reads accept the opaque registration-order capability only in `X-Registration-Order-Capability`; writes require authentication, `Idempotency-Key`, rate limiting, replay protection, and private no-store responses.

`FairReturnWaitlistDto` publishes only an opaque resource UUID, bounded position, bounded status/reason codes, and optional offer expiry. A position of zero means unavailable and values above 999 are capped. Server-only `Can*` and stop-control facts are JSON-ignored and drive HAL relations; clients must not infer actions from roles, local claims, status strings, or payment state. Unknown identities, seller conflicts, stale offers, and authority failures use the same private not-found envelope.

The Blazor BFF mirrors these routes under `/bff`, forwards through the generated `IEventApiClient`, retains cookie authority, validates antiforgery before write-rate limiting, and never accepts capabilities in query strings or response bodies.

## Related Docs
- `docs/SECURITY-MODEL.md` — auth, JWT, CORS, security headers
- `docs/ARCHITECTURE.md` — Clean Architecture layers, request flow
- `docs/OPERATIONS.md` — rate limiting config, timeouts, shutdown
- `docs/CODEBASE_INSIGHTS.md` — non-obvious patterns
- `docs/MULTI_TENANCY.md` — tenant resolution and isolation
- `docs/OUTBOX_PATTERN.md` — outbox pattern implementation details
- `docs/FOOTER_MANAGEMENT.md` — footer management system
- `docs/CONTRIBUTING.md` — development workflow
