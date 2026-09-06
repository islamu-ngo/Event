---
description: >-
  Practical answers about deployment, operations, security, integrations, and
  current limits.
---

# Self-Hoster & Adopter FAQ

## Is ISLAMU Event production-ready?

It is pre-1.0 software with API version `0.1`. Evaluate it against your requirements, pin exact versions, keep release evidence, back up before upgrades, and test restores. Breaking contract changes are allowed before v1 and are recorded in the API changelog.

## What is the smallest supported deployment?

The standalone container is the smallest path. It runs one application process with durable SQLite state. It is suitable for evaluation and smaller installations, but it is not a stateless container: back up the primary database, the dedicated privacy-erasure authority file, storage data, and every other documented durable mount together.

## When should I use split Docker Compose?

Use split Compose when you want PostgreSQL and independently operated supporting services. Validate with `docker compose config --quiet`, complete `keycloak-init`, run database migration before API and UI startup, and wait for health rather than treating process start as readiness.

## Can I deploy to Coolify, Azure, or AWS?

Yes, as adopter-owned adaptation. The repository documents a Cerbos-on-Coolify runbook and uses Traefik-compatible routing patterns, but it does not provide a one-click full ISLAMU Event Coolify template. .NET Aspire can model resources and target cloud environments, but no turnkey Azure or AWS production template is shipped.

## Is Kubernetes or Helm supported?

No repository-supported Kubernetes or Helm package is currently shipped. Do not translate Compose instructions into a production cluster without independently defining storage, migrations, health, secret delivery, tenancy, network policy, backup, and rollback.

## Which identity and authorization systems are required?

Choose Local Identity, Keycloak, or AT Protocol as the primary browser identity authority; Keycloak is not required for the other modes. Primary AT Protocol can create a passwordless account for a verified DID, while optional AT Protocol sign-in requires an existing exact account link. Authorization is a separate explicit runtime choice between Cerbos and local DB-backed RBAC; changing identity providers does not grant roles or create a second authorization model.

## What happens if Cerbos is unavailable?

Authorization fails closed. A Cerbos or tenant BYO-PDP failure does not silently switch to local RBAC. Switching providers is an explicit operator action. Check the Cerbos health endpoint, policy distribution, runtime intent, network reachability, and application authorization health.

## Why is an Edit or Delete button missing?

The UI follows HAL `_links` returned by the server. A missing action usually means the current resource state, tenant, policy, or authorization result does not allow it. Do not work around the absence by inspecting roles or claims in the client.

## How is tenant context resolved?

In multi-tenant mode the platform evaluates trusted BFF context, excludes the admin host, then considers custom domain and subdomain resolution. If no tenant can be resolved, the request fails closed with `404`. Single-tenant mode binds the configured/default tenant.

## Can I switch from single-tenant to multi-tenant later in the admin UI?

Deployment mode is selected before first-run onboarding with `DEPLOYMENT_MODE=multi_tenant`; single-tenant is the default when absent. It is not a casual day-two UI toggle. Plan domain routing, tenant identity, data authority, and backup topology before onboarding.

## Where do secrets belong?

Secrets originate from Infisical, explicit environment injection documented by `.env.example`, or the deliberately selected shared .NET User Secrets authority in Development/Testing only. Do not put credentials in source, AppHost code, fixtures, committed settings, screenshots, or configuration manifests. A selected provider failure remains a failure; it is not silently reclassified as unconfigured.

## What must I back up?

Back up every authoritative database, the selected privacy-erasure authority store, local or S3-compatible object metadata/data as applicable, and provider configuration needed to reconnect without embedding secrets in the backup description. Keep encrypted, access-controlled copies and regularly prove a restore in an isolated environment.

## Why does privacy erasure need special recovery handling?

The erasure authority prevents deleted subject data from reappearing after a stale primary-database restore. `EmbeddedSqlite` uses a dedicated local authority file, `CoLocated` shares the application database, and `ExternalDatabase` uses separate PostgreSQL. Each topology has different restore obligations; never restore one authority from another topology's instructions.

## Does check-in work offline?

No. Check-in is online and server-authoritative. Connectivity loss denies validation; there is no offline validation queue or implemented emergency-exception admission path. Design venue connectivity and fallback operating procedures around that boundary.

## Are payments and refunds implemented?

Organizer-direct Stripe Connect payments and durable provider-backed refund workflows are implemented. Browser returns are navigation only; signed provider evidence and reconciliation establish terminal state. The platform does not claim escrow, universal provider liability, accounting, tax, invoice, banking, or guaranteed payout timing.

## Is email automatic for every notification?

No. Durable in-app notifications and email are sibling delivery channels only where an explicit notification intent creates them. SSE and Web Push only prompt refresh. Configure SMTP and its readiness health for email delivery; do not assume every inbox row becomes an email.

## Is Listmonk bundled?

No. Listmonk synchronization is optional, disabled by default, and connects to an external Listmonk instance. Configure its URL, list, behavior, username, and API key through the supported settings and secret paths, then test the connection and readiness check.

## Which webhook mode should a self-hoster choose?

`Local` is the smallest outgoing mode. It signs Svix-compatible envelopes, retries within a bounded policy, rejects redirects and unsafe private/metadata targets by default, and exposes readiness. `Svix` and `Composite` require an explicitly operated self-hosted Svix profile; managed Svix SaaS is not a selectable supported profile.

## Can MCP directly change repository data?

No. The optional `/mcp` endpoint is stateless Streamable HTTP, API-key-first for external clients, and proposal-first for mutations. It does not provide stateful sessions, legacy SSE, remote tool execution, direct repository mutation, or product-hosted stdio.

## Does federation mean ActivityPub or a hosted AT Protocol server?

No. Current federation is selective AT Protocol client integration: OAuth for linked users, governed outbound records, and exact-collection Jetstream ingestion. ActivityPub, WebFinger, first-party PDS/AppView hosting, wildcard subscriptions, and full public protocol-server behavior are not implemented.

## Where are Swagger, Scalar, and OpenAPI?

Development/Testing can expose `/swagger`, Scalar, and `/openapi/islamu-event.json`. Production Compose runs the Production environment, where those interactive descriptions are not exposed by default. Treat any production exposure as an explicit operator decision with normal authentication, authorization, TLS, and rate-limit review.

## What should I collect before asking for help?

Collect the exact pinned version/tag, deployment mode, database/provider topology, redacted configuration status, health endpoints, relevant correlation or trace IDs, recent migration/upgrade step, and bounded logs with secrets and PII removed. Never paste raw credentials, provider payloads, admission tokens, erasure receipts, or connection strings into a support request.
