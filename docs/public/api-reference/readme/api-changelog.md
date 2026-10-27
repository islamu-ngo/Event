---
description: Curated pre-v1 API contract changes and migration expectations.
---

# API Changelog

The draft HTTP API version is `0.1`. In pre-release development before v1, breaking changes are made freely whenever they simplify the contract or restore architectural invariants.

## Current mainline contract

### Storage origin and delivery (2026-10-02)

Storage `uri` is a nullable policy-derived route, not a persisted provider or
origin address. Eligible managed public images use `/api/storageobject/{id}/public`;
private and tenant content use authenticated `/api/storageobject/{id}/content`.
Resource files retain their separately authorized resource content route.
External references never acquire managed delivery automatically.
Ordinary storage detail/list and upload-session responses no longer expose
backend provider identity. Source, bucket, key, binding, and version stay private.
Continue to gate actions by HAL links. See [storage operations](../../documentation/readme/integrations-and-ai/storage.md)
for the explicit pre-release provenance cutover procedure.

* HAL representations are default where available; clients use `_links` for resource actions.
* API version negotiation uses media type, query, or `X-Api-Version`; URL-segment versions are not supported.
* Failures use RFC 7807 ProblemDetails rather than failed success-shaped command bodies.
* Pagination is 1-based with default size `20` and maximum `100`.
* Bearer and `X-API-Key` authentication are mutually exclusive.
* Tenant context comes from host/header/scoped key authority, not request-body identity.
* Retryable documented writes use tenant-scoped idempotency keys.
* Interactive OpenAPI surfaces are Development/Testing by default.

## Recent externally visible themes

### Independent participant investigation (2026-10-13)

`POST /api/moderation/participant-investigations/sources` captures a confidential,
incident-specific assessment under current independent native investigator/read
authority and separately adopted investigation custody. Supply exact incident,
material, Human, Session/window, revision and finite UTC deadline coordinates,
plus four bounded grounds fields. Unknown JSON members are rejected. The route
uses a 32 KiB body limit, write-rate limiting and private/no-store responses;
success returns only accepted source identity and revisions. Never log grounds.

The separate attribution/renewal intent may name `ParticipantInvestigation`
basis 5 with `investigationSourceId` and `investigationSourceRevision: 1` for the
identified Human. This opens responsibility under review, not guilt, a strike,
sanction or admission. Those fields are forbidden for other responsibility bases.
Current native custody, incident generations and authenticated original capture
remain required; changing authorization or deadlines alone cannot renew a live
proceeding. Confidential disposal preserves history but ends current qualification.
EventSubmission still names only the actual EventVersion submitter. Native schema
and Client SDK generation/compilation passed; use the named responsibility,
incident-kind and custody-purpose wire values. Hosted/provider/race assurance
remains a programme-exit requirement.

### Moderation policy authoring (2026-10-13)

`GET /api/moderation/policies/current?scope=tenant|instance` returns latest retained
authoring state and separately qualified next-write coordinates for the actual
request context. An installed empty slot is unreserved. Optional UUIDv7
`prospectivePolicyId` keeps the same local creation intent across reads of that
empty slot; it cannot replace an adopted identity, reserve state or grant a write.
Follow current draft/preparation HAL links and reconcile changed displayed heads
deliberately. Candidate preparation never means adoption or activation.

The guided Client page retains original and disabled definitions, appends semantic
revisions and makes inventory/native-mode/declaration edits explicit.
Simple/Advanced changes presentation only. Hosted/provider, browser and
accessibility assurance remain programme-exit requirements.

### Finite moderation material read purposes (2026-10-12)

`POST /api/moderation/case-read-grants/{caseId}` grants a separately qualified
account one finite case/material read purpose. Supply an exact case revision,
source, grantee, UUIDv7 operation ID and finite UTC deadline with at most six
fractional-second digits. The URL controls the case lookup; naming a grantee
does not supply current administrative authority. Success returns only the
committed grant ID. Repeat only the unchanged original intent when recovering a
lost acknowledgement; replay cannot renew an expired or revoked grant.

`POST /api/moderation/case-read-grants/{grantId}/revoke` withdraws the original
revision-1 grant using a separate operation ID. A different operation cannot
borrow an earlier revocation. Both actions use private/no-store responses,
write-rate limits and 16 KiB body bounds. Obtain the opaque read token separately
through the existing read-receipt route; neither granting nor revoking returns
protected material. Generated client and provider/runtime assurance remain
programme-exit requirements.

### Unified instance administration (2026-10-02)

Internal administrator endpoints now use `/api/admin/instance/*` instead of
`/api/admin/control-plane/*`. Regenerate direct clients for the instance-named
operations and contracts, or consume the current HAL links. Old routes are not
retained. Tenant lifecycle, plan management and deployment operations keep their
existing authorization and concurrency rules. External fleet clients continue
to use the unchanged `/api/management/*` connector.
Whole-instance configuration export and import-session routes also live under
`/api/admin/instance/configuration-*`; browser downloads use the same-origin
`/bff/admin/instance/configuration-manifest/export` resource.

### Protected resource links and operator limits (2026-09-24)

`PUT /api/eventresource/{id}/destination` writes a validated HTTPS
destination for an authorized manager using an idempotency key and the current
resource version. The full destination is write-only: management and audience
responses expose at most its safe origin. An eligible visitor follows the
resource's `access` HAL action at `GET /api/eventresource/{id}/access`, which
rechecks current authority and returns a temporary, no-store redirect without
an application bearer token. Show the origin and warn visitors before they
leave the platform. Withdrawing platform access denies later platform
navigation but cannot recall a link already revealed by an external provider;
rotate or revoke that link at the provider when necessary.

Instance administrators use `GET/PUT /api/settings/instance/event-resources`
for private, no-store resource ceilings and the explicit unscanned-document
exception. Tenant administrators use the `EventResources` tenant setting group
and its advertised `edit` HAL relation to narrow inherited limits; a tenant
cannot widen the effective instance policy. Re-read the effective settings
after a rejected or successful mutation instead of trusting an old form.

### Portable resource metadata (2026-09-23)

`GET /api/event/{eventId}/resources/export` adds private/no-store semantic JSON
for currently authorized managers. Parent and per-resource `export` decisions
are distinct from management visibility. Pages default to 20, are capped at 100
and expose no totals. Stale resource revisions or denied rows release no page.

The document preserves authorized metadata, audience qualifiers and timing
intent, while excluding backing locations, destinations, protected envelopes,
audit/attendee history and manager attribution. The management collection
offers the scoped `export` HAL action. No file delivery or import is enabled.

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
generated `GetOperatorIdentityFormOptionsAsync` operation. It supplies authoritative
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

## Authoritative sources

The repository's `docs/API_CHANGELOG.md` is the detailed date-indexed contract log. The governed OpenAPI artifact is `schemas/openapi-islamu-event.json`. Regenerate all governed client artifacts after server contract changes; do not edit generated files manually.

At API v1.0, breaking schema diffs become blocking. Until then, treat every upgrade as a deliberate contract migration.
