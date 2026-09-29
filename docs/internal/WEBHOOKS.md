ABOUTME: Operator and integrator guide for outgoing ISLAMU Event webhooks.
ABOUTME: Covers outgoing providers plus signed Svix and Stripe Connect incoming callback behavior.

# Webhooks

> **Audience:** Operators | Integrators | Contributors | AI agents
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-08-14
> **Source Anchors:** `Explore.Application/Webhooks/`, `Explore.Infrastructure/Webhooks/`, `Explore.Infrastructure/HealthChecks/`, `Explore.API/Controllers/WebhooksController.cs`, `Explore.API/Controllers/WebhookEndpointsController.cs`, `Explore.API/Controllers/WebhookMessagesController.cs`, `Explore.API/Controllers/WebhooksControllerBase.cs`, `Explore.API/Controllers/IncomingWebhooksController.cs`, `Explore.AppHost/AppHost.cs`, `docker-compose.yml`, `.env.example`, `docs/API.md`, `docs/SECURITY-MODEL.md`

Webhooks in this document are outgoing product notifications sent by ISLAMU Event to external systems. Incoming provider callbacks are separate API ingestion routes; see [INTEGRATIONS.md](INTEGRATIONS.md).

## Delivery Model

Outgoing delivery is provider-based:

```text
domain/application event
  -> WebhookEventEnvelope
  -> webhook_messages evidence ledger row
  -> IWebhookDeliveryProvider
  -> Local, Svix, Composite, DryRun, or Disabled
```

```mermaid
flowchart TD
    Event[Domain Event Occurs<br/>e.g. EventPublished, TicketMinted] --> Outbox[(Database Outbox)]
    
    Outbox --> Router{WEBHOOKS_PROVIDER}
    
    subgraph LocalMode["Mode: Local (In-Process)"]
        Router -->|Local| Dispatcher[Built-in Webhook Dispatcher]
        Dispatcher --> SSRF[SSRF Security Filter<br/>Blocks 127.0.0.1, RFC 1918, Cloud Metadata]
        SSRF --> Sign[Sign HMAC-SHA256<br/>webhook-signature header]
        Sign --> HTTPClient[Direct HTTP POST]
        HTTPClient -->|Transient Failure| LocalBackoff[8-Step Exponential Backoff with Jitter]
        LocalBackoff --> Dispatcher
    end

    subgraph SvixMode["Mode: Svix (Enterprise Dedicated)"]
        Router -->|Svix| SvixClient[Svix API Client Bridge]
        SvixClient --> SvixServer[Svix Server Container v1.96.1]
        SvixServer --> SvixDB[(Dedicated PostgreSQL)]
        SvixServer --> SvixQueue[(Dedicated Redis)]
        SvixServer --> ExternalWorkers[Svix Distributed Workers]
    end

    HTTPClient --> Subscriber[Subscriber Webhook Endpoint]
    ExternalWorkers --> Subscriber

    classDef local fill:#eff6ff,stroke:#3b82f6,stroke-width:2px;
    classDef svix fill:#faf5ff,stroke:#a855f7,stroke-width:2px;
    class LocalMode local;
    class SvixMode svix;
```

ISLAMU owns the primary event catalog and `webhook_messages` ledger even when Svix performs final delivery. That keeps audit, provider switching, payload retention, and local fallback under the application boundary.

Incoming registration-provider callbacks are not outgoing webhooks. `POST /api/integrations/registration/{provider}/{bindingId}/callback` reuses the incoming-webhook message/effect ledger with effect kind `registration.provider_submission`, acknowledges non-oversize deliveries with `202 Accepted`, and parks unverifiable or unsafe evidence for organizer reconciliation. Outgoing `Webhooks:*` mode does not enable, disable, or authenticate that callback route.

## Provider Modes

| Mode | Behavior | Use |
|---|---|---|
| `Disabled` | Creates no outgoing delivery work. Incoming callbacks still work. | Minimal installs. |
| `Local` | Built-in endpoint CRUD, subscription filtering, signed POST, retry attempts, delivery logs, manual retry, and safety checks. | Self-hosters and simple integrations. |
| `Svix` | API publishes authoritative messages to Svix; Svix owns endpoint fanout, delivery history, retries, and App Portal management. | Larger deployments and advanced webhook operations. |
| `Composite` | Uses authoritative local audit plus Svix delivery path. | Advanced installs that need local visibility and Svix delivery. |
| `DryRun` | Creates authoritative messages without outbound delivery. | Development and test validation. |

LocalProvider is intentionally not a Svix clone. It does not include transformations, OAuth or mTLS endpoint auth, a customer-facing advanced portal, FIFO endpoints, polling endpoints, or advanced analytics.

## Typed Provider Capability Authority

Provider features are represented by twelve stable single-bit lookup IDs. Runtime decisions do
not infer support from a provider name: they resolve the configured mode and exact supported
version, then intersect provider proof with the verified consumer binding and instance governance.
Unknown flags, an unsupported tuple, a missing binding, or managed Svix SaaS fail closed.

| ID | Code | Local authority | Self-hosted Svix v1.96.1 proof |
|---:|---|---|---|
| 1 | `ENDPOINT_MANAGEMENT` | Yes | Yes |
| 2 | `PROVIDER_ATTEMPT_VISIBILITY` | No | No |
| 4 | `REPLAY` | No | No |
| 8 | `PAYLOAD_INSPECTION` | No | Yes |
| 16 | `APP_PORTAL` | No | Yes |
| 32 | `EVENT_CATALOG` | Yes | Yes |
| 64 | `PROVIDER_RETENTION_CONTROL` | No | No |
| 128 | `APPLICATION_THROTTLING` | No | No |
| 256 | `ENDPOINT_THROTTLING` | No | No |
| 512 | `TRANSFORMATIONS` | No | No |
| 1024 | `ORDERING` | No | No |
| 2048 | `OPERATIONAL_CALLBACKS` | No | No |

`Composite` is the union of the Local authority and the governed, verified Svix authority.
`Disabled` and `DryRun` advertise no provider capabilities. This matrix is deliberately narrower
than the full set of internal delivery operations: a feature is not advertised as provider
authority until its control path and version evidence use this contract.

Consumer creation rejects unavailable provider modes before persistence. Local endpoint create,
update, secret rotation, test, and archive operations require Local endpoint-management authority;
pure Svix endpoint management stays in the provider portal. Consumer read models expose all twelve
lookup triples, source codes (`LOCAL` or `SVIX`), the resolution version, and bounded reason codes.
HAL emits the App Portal relation only when the authoritative DTO capability, verified persisted
binding, governance ceiling, and authorization all agree. Blazor renders actions only from HAL and
uses capability metadata solely for safe operator explanations.

## Event Catalog And Payloads

`GET /api/webhooks/event-types` exposes the primary event catalog with names, groups, schema versions, JSON Schema, and example envelopes. Initial public event types include event lifecycle, registration, report, moderation, and organization verification events.

Payloads use a stable envelope:

```json
{
  "id": "018f0000-0000-7000-8000-000000000001",
  "type": "event.published",
  "version": 1,
  "occurredAt": "2026-07-04T10:00:00Z",
  "tenantId": "018e4e5c-7f00-7000-8000-000000000001",
  "data": {}
}
```

Sensitive moderation payloads are minimized. Heavy-redaction events must not include unsafe event title, slug, URL, image URI, storage path/key, organizer identity, provider endpoint, raw provider error, or free-form unsafe content.

Ordinary message list/detail responses expose retention and hash metadata but never raw payload material. `GET /api/webhooks/messages/{messageId}/payload` is the only management-plane payload read. It requires the distinct `webhook:view-payload` authorization, performs an explicit tenant-scoped lookup, checks the authoritative UTC retention cutoff even before asynchronous cleanup clears the bytes, and appends a mandatory `PAYLOAD_VIEWED` audit before returning the base64-encoded exact bytes. Audit failure suppresses the response.

The payload response is always `no-store`/`no-cache`. Missing IDs and cross-tenant IDs return the same generic `404`; a known tenant-local message whose retention ended or whose bytes were cleared returns `410`. Message HAL resources emit `payload` only when the retention/state checks pass and the authorization provider allows `webhook:view-payload`; clients must not infer access from roles, claims, or the visible retention timestamp.

## LocalProvider Delivery

LocalProvider endpoints are managed through `/api/webhooks/endpoints`. Endpoint subscriptions filter by event type. Deliveries are HTTPS/HTTP POST requests with:

- `svix-id`
- `svix-timestamp`
- `svix-signature`

The signed content is:

```text
{svix-id}.{svix-timestamp}.{raw-body}
```

The signature is Svix-compatible HMAC-SHA256 over the raw body using `whsec_` secret material, so consumers can verify LocalProvider deliveries with Svix-compatible libraries. Verification must use fixed-time comparison and timestamp tolerance.

During secret rotation, `svix-signature` carries multiple space-separated `v1` values and verification succeeds when any active current/previous secret matches. Repeated `svix-signature` HTTP fields are normalized as separate signature values. Payload bytes, whitespace, UTF-8 encoding, message ID, and timestamp must be verified exactly; JSON must never be parsed and reserialized before verification.

Retry schedule:

| Attempt | Delay |
|---:|---|
| 1 | immediately |
| 2 | 30 seconds |
| 3 | 5 minutes |
| 4 | 30 minutes |
| 5 | 2 hours |
| 6 | 6 hours |
| 7 | 12 hours |
| 8 | 24 hours |

`2xx` responses are success. Non-`2xx`, redirects, timeouts, and network failures are delivery failures. Attempts store bounded status, duration, HTTP status, safe failure category, and optional response preview only.

## LocalProvider Security Defaults

LocalProvider treats endpoint URLs as untrusted egress targets.

- Private, loopback, link-local, localhost, metadata, and internal DNS destinations are blocked by default.
- Redirects are disabled.
- Request and connect timeouts are bounded by `Webhooks:Local:*`.
- Response previews are bounded and never include full bodies by default.
- Logs, metrics, health checks, and ProblemDetails must not include payload JSON, endpoint secrets, full endpoint query strings, authorization headers, full responses, or raw exception text.

Operators can opt into specific private CIDRs with `Webhooks:Local:AllowedPrivateCidrs`, but this should stay empty for internet-facing SaaS.

## SvixProvider

SvixProvider uses the official Svix C# SDK. For self-hosted Svix, the SDK is configured with the deployment base URL through `SvixOptions`, while the API token stays server-side behind the ISLAMU secret resolver. Provider selection is guarded by an exact deployment-kind/environment/provider-version/capability-policy tuple backed by executed conformance evidence. Unknown and zero-evidence tuples fail startup validation and readiness before any secret is resolved.

Mapping:

| ISLAMU | Svix |
|---|---|
| webhook consumer / tenant | application UID |
| event type name | event type |
| webhook message id | event ID and idempotency key |
| payload JSON | message payload |
| payload retention | message retention period |

`POST /api/webhooks/svix/app-portal` creates a backend-generated, short-lived App Portal access URL. Blazor receives only the short-lived URL/token metadata; the Svix API token is never exposed to the browser.

## Optional Local-Full Svix

`local-full` and `local-default` omit Svix when `WEBHOOKS_PROVIDER=Local`. Explicit `Svix` or `Composite` selection starts pinned `svix/svix-server:v1.96.1` through Aspire with PostgreSQL and Redis queue/cache. Both `SVIX_QUEUE_TYPE` and `SVIX_CACHE_TYPE` are `redis`: the queue carries provider work, while the shared cache preserves idempotency semantics across Svix replicas. Do not use the self-hosted in-memory cache in a multi-replica deployment. Aspire injects the current Svix HTTP endpoint and the proven `self-hosted`/`1.96.1`/`svix-self-hosted-1.96.1-v1` tuple into the API; use `aspire describe --format Json` to inspect the actual host port for the running session.

The eleven-case live conformance matrix proves the twelve-hour idempotency behavior, duplicate event identity conflicts, response-loss ambiguity, credential rotation, list/get consistency, endpoint management, App Portal access, event-catalog management, and payload inspection. Self-hosted v1.96.1 does not return message tags from list/get, so request-hash exact lookup is not enabled for that profile. An accepted request followed by response loss therefore remains manual reconciliation rather than being guessed or blindly retried.

## Self-Hosted Svix Authentication

The supported Svix profile is self-hosted only. Managed Svix SaaS has no selectable
conformance profile and is not a release gate. The managed conformance token fields in
`.env` and `.env.example` remain intentionally empty; neither Infisical nor the Aspire
container supplies cloud API keys.

Self-hosted authentication uses a JWT signed with the server's `SVIX_JWT_SECRET`.
The server does not publish a reusable client token during startup. Its bundled CLI can
generate an organization-scoped bearer token with `svix-server jwt generate`; when the
server runs in a container, execute that command inside the running container and place
the resulting token in `WEBHOOKS_SVIX_AUTH_TOKEN`. The token is credential material and
must not be written to logs, test reports, screenshots, or committed operator evidence.

Aspire cannot treat command output from an already-running container as an environment
input for the API resource. Local mode therefore leaves both Svix application credentials
empty and does not start the Svix resources. When self-hosted Svix is deliberately enabled,
generate a token from the container configured with the selected signing secret, place the
token only in `.env` and the blank deployment template field in `.env.example`, then restart
the API. Never source these values from managed Svix SaaS.

When Svix is explicitly selected, the local AppHost supplies the configured
`SVIX_JWT_SECRET` to the Svix container and maps `WEBHOOKS_SVIX_AUTH_TOKEN` plus
`WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET` into the authoritative application secret
references. Both application credential values remain blank in `.env` and `.env.example`
until the operator deliberately generates/configures them for the running self-hosted
container.

Development database seeding creates missing instance-scoped secret bindings for those environment variables when values are configured. Rotate these values outside local development.

Docker Compose defaults to Local and starts Svix only when the `webhooks` profile is explicitly enabled. Aspire likewise omits its Svix resources unless `WEBHOOKS_PROVIDER` is `Svix` or `Composite`. Local delivery uses the application PostgreSQL work tables directly and requires no webhook-specific Redis, Kafka, CDC, or additional reverse proxy. The same authoritative secret refs are documented in `.env.example`.

## Configuration

| Key | Default | Notes |
|---|---|---|
| `Webhooks:Enabled` | `true` | Master switch for outgoing product webhooks. |
| `Webhooks:Provider` | `Local` | `Disabled`, `Local`, `Svix`, `Composite`, or `DryRun`. |
| `Webhooks:AllowTenantOverride` | `true` | Allows tenant-level provider posture where supported. |
| `Webhooks:DefaultPayloadRetentionDays` | `14` | Default authoritative payload retention window. |
| `Webhooks:Local:MaxAttempts` | `8` | Local retry ceiling. |
| `Webhooks:Local:TimeoutSeconds` | `15` | Total LocalProvider request timeout. |
| `Webhooks:Local:ConnectTimeoutSeconds` | `3` | LocalProvider connect timeout. |
| `Webhooks:Local:BlockPrivateNetworks` | `true` | SSRF protection default. |
| `Webhooks:Svix:BaseUrl` | unset | Set for self-hosted Svix, for example `http://localhost:8071` in Aspire or `http://svix:8071` in Compose. |
| `Webhooks:Svix:Environment` | `production` | Exact conformance environment identifier; Compose/Aspire use `self-hosted`. |
| `Webhooks:Svix:ProviderVersion` | `managed-api-v1` | Exact conformance version identifier; Compose/Aspire use pinned `1.96.1`. |
| `Webhooks:Svix:CapabilityPolicyVersion` | `svix-managed-api-v1` | Exact versioned capability policy; Compose/Aspire use `svix-self-hosted-1.96.1-v1`. |
| `Webhooks:Svix:AuthTokenSecretRef` | `webhooks.svix.auth_token` | Server-side Svix API token secret binding. |
| `Webhooks:Svix:OperationalWebhookSecretRef` | `webhooks.svix.operational_webhook_secret` | Secret used to verify incoming Svix operational callbacks. |
| `Webhooks:Svix:AppPortalEnabled` | `true` | Allows backend App Portal URL generation. |
| `Webhooks:Svix:SyncEventTypesOnStartup` | `true` | Syncs primary event types to Svix on API startup in Svix/Composite mode. |

Retention cleanup is configured independently from delivery:

| Key | Default | Notes |
|---|---|---|
| `WebhookRetention:Enabled` | `true` | Runs the bounded tenant-rotating cleanup processor. |
| `WebhookRetention:DryRun` | `false` | Reports eligible evidence without changing it. |
| `WebhookRetention:InitialDelaySeconds` | `60` | Startup delay before the first cleanup pass. |
| `WebhookRetention:PollingIntervalMinutes` | `60` | Delay between cleanup passes. |
| `WebhookRetention:MaxTenantsPerPass` | `100` | Maximum active tenants selected per pass. |
| `WebhookRetention:BatchSize` | `500` | Per-tenant maximum for each evidence category. |
| `WebhookRetention:InboundPayloadRetentionDays` | `14` | Exact incoming payload bytes. Cannot be shorter than replay. |
| `WebhookRetention:OutboundPayloadRetentionDays` | `14` | Default exact outgoing payload bytes; event contracts may override it. |
| `WebhookRetention:ProcessingAttemptRetentionDays` | `30` | Successful/ordinary processing and delivery evidence. |
| `WebhookRetention:DeadLetterEvidenceRetentionDays` | `90` | Failed, abandoned, conflict, and dead-letter evidence. Cannot be shorter than attempts. |
| `WebhookRetention:ProviderPublicationRetentionDays` | `90` | Terminal provider publications and external mappings. |
| `WebhookRetention:OperationalLogRetentionDays` | `30` | Snapshotted operational-log horizon. |
| `WebhookRetention:AdministrativeAuditRetentionDays` | `365` | Mandatory webhook administrative audit evidence. |
| `WebhookRetention:ReplayWindowDays` | `14` | Minimum incoming replay/redrive window. |

Bulk replay execution is configured independently from ordinary delivery:

| Key | Default | Notes |
|---|---|---|
| `WebhookBulkReplay:Enabled` | `true` | Runs the durable queued replay processor. |
| `WebhookBulkReplay:InitialDelaySeconds` | `10` | Startup delay before the first replay pass. |
| `WebhookBulkReplay:PollingIntervalSeconds` | `5` | Delay between replay passes. |
| `WebhookBulkReplay:OperationsPerPass` | `10` | Maximum durable operations processed per pass. |
| `WebhookBulkReplay:MaximumItemsPerOperation` | `100` | Maximum terminal Local targets reserved and reopened by one operation; hard code ceiling is 1000. |
| `WebhookBulkReplay:MaximumReservedItemsPerTenant` | `500` | Sum of requested items across queued/executing operations for one tenant. |
| `WebhookBulkReplay:MaximumFilterWindowDays` | `30` | Maximum explicit materialization-time preview/schedule window. |

`GET /api/webhooks/bulk-replays/preview` classifies an explicit half-open message
materialization interval plus optional consumer, endpoint, and event type. Eligible work is limited
to retained, unheld `DEAD_LETTERED` or `ABANDONED` Local targets whose endpoint is active. The
preview reports disjoint counts for held, payload-unavailable, endpoint-unavailable, other Local
state, provider conflict, provider unknown, provider manual-reconciliation, and other provider work.
Provider publications are never eligible because the supported self-hosted Svix contract does not
prove a safe provider-native bulk replay operation.

`POST /api/webhooks/bulk-replays` freezes the authoritative filter, limit, normalized reason, preview
evidence, and SHA-256 request identity under a tenant-unique operation key. The worker re-evaluates
all eligibility predicates in a tenant-serialized transaction and changes only the selected Local
targets to `RETRY_DUE`; it never performs HTTP delivery itself. Existing Local workers therefore
retain fair tenant/endpoint claims, global and per-endpoint in-flight ceilings, rate limits, signing,
and retry behavior. `POST /api/webhooks/bulk-replays/{operationId}/cancel` succeeds only for a queued
operation with the caller's expected concurrency version. Schedule, rejection, cancellation,
completion, and failure outcomes write normalized payload-free administrative audit evidence.

Environment variables used by local profiles:

| Variable | Purpose |
|---|---|
| `WEBHOOKS_PROVIDER` | Compose-friendly provider mode. |
| `WEBHOOKS_SVIX_BASE_URL` | Compose-friendly Svix base URL. |
| `WEBHOOKS_SVIX_ENVIRONMENT`, `WEBHOOKS_SVIX_PROVIDER_VERSION`, `WEBHOOKS_SVIX_CAPABILITY_POLICY_VERSION` | Exact supported self-hosted conformance tuple. |
| `WEBHOOKS_SVIX_AUTH_TOKEN_SECRET_REF` | Authoritative auth token secret ref. |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET_REF` | Authoritative operational webhook secret ref. |
| `WEBHOOKS_SVIX_AUTH_TOKEN` | Dev/deployment source value for the Svix API token binding. |
| `WEBHOOKS_SVIX_OPERATIONAL_WEBHOOK_SECRET` | Dev/deployment source value for the Svix operational callback secret binding. |
| `SVIX_TAG`, `SVIX_DB_DSN`, `SVIX_REDIS_DSN`, `SVIX_QUEUE_TYPE`, `SVIX_CACHE_TYPE`, `SVIX_JWT_SECRET` | Pinned Svix server image and container configuration. Queue and cache must both use shared Redis for the supported profile. |

## Health, Metrics, And Retention

Readiness checks:

- `webhook-local-delivery` reports LocalProvider queue backlog, stale sending leases, and processor settings.
- `webhook-svix-provider` requires the provider-publication processor when Svix is selected and reports the safe conformance tuple, evidence revision/count, exact-lookup availability, bounded capability count/codes, provider selection, App Portal/event-type-sync flags, and whether server-side Svix secrets resolve. It does not expose tokens, secret refs, or provider URLs and does not perform an outbound network probe.
- `/health/webhooks/local` and `/health/webhooks/svix` expose those checks independently; aggregate `/health` remains the traffic-admission view.

Business metrics use bounded labels:

- `explore.webhooks.messages_created`
- `explore.webhooks.delivery_attempts`
- `explore.webhooks.delivery_success`
- `explore.webhooks.delivery_failure`
- `explore.webhooks.endpoint_disabled`
- `explore.webhooks.manual_retries`
- `explore.webhooks.provider_publish_failure`
- `explore.webhooks.retention.cleanup_runs`
- `explore.webhooks.retention.cleanup_items`
- `explore.webhooks.claim_lag`
- `explore.webhooks.processing_outcomes`
- `explore.webhooks.retries_scheduled`
- `explore.webhooks.dead_letters`
- `explore.webhooks.publication_unknown_age`
- `explore.webhooks.manual_reconciliations`
- `explore.webhooks.endpoint_auto_pauses`
- `explore.webhooks.provider_health_checks`

The operational instruments use only the closed `provider`, `operation`, and `outcome`
vocabularies or bounded cleanup category/mode values. They never label tenant, message, endpoint,
event, publication, or URL identity. See [WEBHOOK_OPERATIONS_RUNBOOK.md](WEBHOOK_OPERATIONS_RUNBOOK.md)
for SLOs, alert templates, Local/Svix startup, outage, reconciliation, credential rotation,
auto-pause, retention, and migration/restore procedures.

Every materialized outgoing plan, incoming inbox row, provider publication, and administrative audit snapshots a policy version and its relevant UTC cutoffs. Cleanup rotates through a bounded active-tenant batch, creates a fresh tenant scope per tenant, and commits mutations with a credential-free system audit in one transaction. It excludes nonterminal Local work, unknown/manual-reconciliation publications, live provider idempotency windows, incoming replay windows, and active `WebhookRetentionHold` rows. Payload clearing preserves IDs, hashes, byte lengths, normalized status/outcome evidence, and longer-lived audit; terminal attempts, publications, and audits are pruned only after their independent horizons.

Svix receives the snapshotted outbound payload horizon through the per-message `payloadRetentionPeriod` field. The supported self-hosted mapping uses integer days and caps the provider copy at Svix's documented 90-day default boundary; ISLAMU's local policy/version, minimum identities, hashes, outcomes, publication evidence, and administrative audit remain authoritative when their horizons are longer. Provider delete-on-success is not enabled.

## Provider Switching

Switches affect new messages only. Existing local attempts and Svix delivery history remain where they were created.

Local to Svix:

1. Configure and health-check Svix.
2. Ensure `webhooks.svix.auth_token` resolves server-side.
3. Sync event types.
4. Create or map Svix applications for consumers.
5. Move endpoints into Svix App Portal or require consumers to reconfigure there.
6. Switch `Webhooks:Provider` to `Svix` or `Composite`.

Svix to Local:

1. Switch `Webhooks:Provider` to `Local`.
2. Recreate LocalProvider endpoints and subscriptions.
3. Keep external Svix delivery history read-only in Svix.
4. Continue using ISLAMU `webhook_messages` as the authoritative audit ledger.

Do not promise perfect migration of Svix-only features such as transformations, OAuth, mTLS, endpoint throttling, or portal-only endpoint configuration.

## Stripe Connect Account And Payment Intake

`POST /api/integrations/stripe/connect` accepts signed Stripe Connect account and Checkout callbacks through the durable incoming-message framework. The verifier checks `Stripe-Signature` against the exact UTF-8 body with the nonzero timestamp tolerance, requires the pinned API version and configured `Payments:Stripe:Mode`, resolves exactly one historical connected-account owner, and persists the inbox row before acknowledgment. Account handlers apply only monotonic readiness evidence; duplicate events are idempotent, stale events are ignored, incomplete evidence fails closed, and disabled or replaced connections are not reactivated.

Signature material resolves only through the instance/server-only `payments.stripe.webhook_secret` binding (`/stripe/STRIPE_WEBHOOK_SECRET`). Test and Live deliveries must match `Payments:Stripe:Mode`; changing mode requires matching platform and endpoint secrets and a deliberate webhook endpoint reconfiguration. Never reuse an outgoing webhook secret or expose this binding to browser/admin DTOs.

Payment intake is limited to `checkout.session.completed`, `checkout.session.async_payment_succeeded`, `checkout.session.async_payment_failed`, and `checkout.session.expired`. After signature verification, the retained payload is replaced with a normalized envelope containing only event ID/type, Checkout object ID, connected-account ID, mode, API revision, created time, and the exact-body hash. Customer, card, billing, shipping, and buyer fields are not retained. Provider event ID and event-type/object identity are both unique at database authority; an exact event ID with different signed bytes becomes a payload conflict.

The callback handler only makes a durable payment-reconciliation effect due. The Quartz payment drain performs Checkout and PaymentIntent retrieval outside local transactions using the attempt's immutable connected account, then a fenced local transaction verifies exact IDs, total, currency, and application fee before applying monotonic state. Expired sessions cancel; open buyer-action states remain actionable; processing remains processing; complete unpaid `requires_payment_method` evidence fails; complete/paid evidence without a PaymentIntent remains unknown; only consistent accepted success creates the identifiers-only succeeded observation. Ambiguous Checkout creation without a session retains its exact Unknown dispatch epoch so reconciliation can requeue the same provider idempotency key after restart or missed webhook.

Hosted organizer onboarding and the bounded readiness reconciliation worker are separate from webhook intake. Reconciliation selects stale connections in bounded batches, refreshes provider readiness outside its serializable local apply transaction, and ignores disabled, replaced, or older observations. It logs aggregate counts and bounded failure/request-id samples only.

## Related

- [API.md](API.md#webhook-management)
- [INTEGRATIONS.md](INTEGRATIONS.md)
- [CONFIGURATION.md](CONFIGURATION.md)
- [OPERATIONS.md](OPERATIONS.md)
- [WEBHOOK_OPERATIONS_RUNBOOK.md](WEBHOOK_OPERATIONS_RUNBOOK.md)
- [SECURITY-MODEL.md](SECURITY-MODEL.md)
- [Svix quickstart](https://docs.svix.com/quickstart)
- [Svix App Portal](https://docs.svix.com/app-portal)
