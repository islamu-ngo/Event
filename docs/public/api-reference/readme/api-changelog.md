---
description: Curated pre-v1 API contract changes and migration expectations.
---

# API Changelog

The draft HTTP API version is `0.1`. In pre-release development before v1, breaking changes are made freely whenever they simplify the contract or restore architectural invariants.

## Current mainline contract

* HAL representations are default where available; clients use `_links` for resource actions.
* API version negotiation uses media type, query, or `X-Api-Version`; URL-segment versions are not supported.
* Failures use RFC 7807 ProblemDetails rather than failed success-shaped command bodies.
* Pagination is 1-based with default size `20` and maximum `100`.
* Bearer and `X-API-Key` authentication are mutually exclusive.
* Tenant context comes from host/header/scoped key authority, not request-body identity.
* Retryable documented writes use tenant-scoped idempotency keys.
* Interactive OpenAPI surfaces are Development/Testing by default.

## Recent externally visible themes

### Audience resource metadata (2026-09-23)

Private/no-store `GET /api/event/{eventId}/resources` and
`GET /api/eventresource/{id}` expose currently authorized resource metadata,
safe public teasers and independently visible alternative links. Lists return
HAL items and protected continuation, not global totals. Page size defaults
to 20 and is limited to 100. Cursors expire after 15 minutes and cannot move
between tenants, events or reader contexts; invalid state returns 400.

Every request and continuation rechecks current authority, with a final
disclosure check after HAL work. Hidden/nonexistent resources share 404.
Management notes, audience rules, backing keys and destinations are excluded.
No download, access or publication capability is enabled by this addition.

### Event-resource management drafts (2026-09-23)

The authenticated management API adds private/no-store HAL representations for
semantic event-resource drafts: `GET /api/eventresource/{id}/management`,
`GET /api/event/{eventId}/resources/management`, and `GET
/api/eventresource/{id}/audit`, plus create, update, archive, delete,
unpublish, moderation, and reserved publish state routes beneath `/api/event`
and `/api/eventresource`.

`POST /api/event/{eventId}/resources` requires a client-retained UUIDv7
`resourceId`. Every management write requires `Idempotency-Key`; a replay is
authorized against current authority rather than receiving an authorization
bypass. Update and state bodies require `expectedVersion`, so stale requests
conflict. Follow only current HAL actions: archive is terminal but an archived
resource may still expose deletion. The publish route rejects incomplete drafts;
no publish relation is emitted.

Management pages expose no global count, total pages or count-derived links;
parent authority cannot reveal provider-denied resources on other pages.

Drafts contain semantic metadata, audience rules, availability intent, and a
delivery-type placeholder only. They do not accept delivery inputs and expose
no delivery, access, or download affordance. Audit records are minimal
action/outcome/reason/time/retained-manager entries committed with mutations.
Retention zero suppresses new audit rows and purges existing rows; normal expiry
removes complete rows, while subject erasure clears manager attribution without
deleting the shared resource. This is organizer/API guidance; the native CQS,
provider A/B authorization snapshots, and serializable mutation protocol remain
internal implementation details.

### Operator form choices (2026-09-21)

Authenticated clients can read `GET /api/operator-identity-metadata` through the
generated `GetOperatorIdentityFormOptionsAsync` operation. It supplies canonical
operator-kind codes, runtime country display choices, shared field limits and
label/help identifiers without returning saved identity values. An unavailable
country catalogue is explicit; do not replace it with invented choices.
Registration identifiers remain optional, and registration-authority options are
explicitly unsupported rather than a new list of authorities.

The resource is private/no-store and independent of directory publication. Its
`self` and `refresh` links do not authorize identity edits. Normal authenticated
instance identity documents advertise the `form-options` lookup; use the identity
document's own mutation links and revision for saves. Setup-secret access is not
extended. No database migration or deployment setting is required.

### Operations and configuration

Operational control-plane reads, safe health output, configuration-manifest workflows, privacy-erasure topology, and explicit managed-mode interfaces have been added or tightened. Optional managed interfaces remain disabled by default.

### Events and commerce

Public event slugs and Open Graph imagery, modular aspects, custom properties, registration/admission separation, buyer commerce reads, organizer refund actions, material-change response, and refund campaign operations are represented in the current contract. Provider-confirmed evidence controls payment/refund status.

### Communications and integrations

Web Push, SMTP/outbox behavior, sanitized Listmonk settings/test/credential-rotation operations, forms, webhook modes, MCP proposals, and selective federation have explicit contracts and limitations.

### Security and tenancy

Cerbos intent is explicit and fail closed. HAL action generation follows current authorization. Tenant resolution, secret-provider states, private/no-store commerce responses, and erasure-receipt handling have been hardened without compatibility aliases for removed pre-v1 shapes.

## What counts as a breaking change

Record removals, renames, authentication/authorization changes, request/response/problem changes, pagination/cursor changes, and generated-client changes. Each entry should name affected routes/schema/methods, old and new behavior, consumers, migration guidance or compatibility window, target release, and verification evidence.

## Canonical sources

The repository's `docs/API_CHANGELOG.md` is the detailed date-indexed contract log. The governed OpenAPI artifact is `schemas/openapi-islamu-event.json`. Regenerate all governed client artifacts after server contract changes; do not edit generated files manually.

At API v1.0, breaking schema diffs become blocking. Until then, treat every upgrade as a deliberate contract migration.
