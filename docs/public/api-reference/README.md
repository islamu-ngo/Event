---
description: HAL/REST integration guidance for ISLAMU Event API version 0.1.
---

# API Reference

ISLAMU Event exposes a versioned HAL/REST API assembled by thin ASP.NET Core controllers over MediatR application requests. The current API version is `0.1` and remains pre-v1: pin generated contracts and review the API changelog before upgrading.

## Environments

Development/Testing commonly uses `https://localhost:7039`. Docker Compose runs the API in Production at `http://localhost:7039` behind the adopter's reverse proxy/TLS boundary.

Swagger, Scalar, and `/openapi/islamu-event.json` are Development/Testing descriptions. They are not exposed by default in Production Compose and must not be presented as an unrestricted public integrator contract.

## Authentication

Use exactly one mechanism:

```http
Authorization: Bearer <access-token>
```

or

```http
X-API-Key: <tenant-or-scope-key>
```

Do not send both. Browser users normally reach the API through the BFF. Direct integrations use scoped API keys or an explicitly supported bearer flow.

## Tenant context

Tenant context is resolved from the request host or `X-Tenant-Slug`; a scoped API key may finalize binding. The server validates this against trusted/persisted authority. Never put an authoritative user or tenant identity in a request body and expect it to override the authenticated context.

## Settings scopes

Personal preferences use the existing `/api/settings/user` routes. Tenant and
instance settings retain their separate administrator checks; selecting a route
does not grant authority to change a higher scope. The settings implementation
uses dedicated capability handlers without changing URLs, operation IDs,
response formats or the `Settings` client group.
Tenant setting reads remain private/no-store and retain server-provided HAL
affordances; batch writes retain their strict default and existing conflict rules.
The `/api/settings/instance/atproto-federation` routes require instance-administrator
authority and expose only the registered ATProto administrator settings. Setup
authority does not grant access to this capability.
SMTP disable remains a preview-then-confirm flow: request a fresh preview, then
submit its confirmation token and revision with the exact acknowledgement.
The server rechecks administrator authority and scope when applying the change.
Stale confirmations fail rather than disabling a changed configuration; preview
and disable responses remain private/no-store.

## Role-grant identity labels

Role-grant detail and list responses can contain null `userEmail` and
`userFullName` when profile PII is absent. The schema and generated client now
describe that existing behavior accurately. Grant and user identifiers remain
available under the same access rules; a missing label is not an erasure-status
signal or a change in authority.

## Optional event labels

Event detail and list responses can contain null actor and lookup display labels
when that related data is absent. OpenAPI and generated clients now describe
those existing null values accurately; required property presence is unchanged.
Render an optional label without treating it as a change in event ownership,
permissions or publication state. HAL links remain the action authority.
MCP event search and detail outputs preserve the same optional-label semantics.
Session detail and list responses also permit a null parent `eventTitle` when
the related event data is absent; this does not change the parent identifier.

## Optional location addresses

Location detail and list responses can contain a null `address`; location detail
can also contain a null `postcode`. The schema and generated clients acknowledge
those existing values when address data is absent. Missing address text is not an
erasure-status signal and does not grant access to private location data.

## Version negotiation

Use one of:

```http
Accept: application/hal+json;v=0.1
X-Api-Version: 0.1
```

or `?api-version=0.1`. Requests without an explicit version default to `0.1`. URL-segment versioning is intentionally unsupported so canonical paths, operation IDs, and HAL links remain stable.

## Contract rules

* HAL is the default where available.
* `Prefer: return=minimal` suppresses generated links when affordances are not needed.
* Pagination is 1-based: page `1`, size `20`, maximum size `100`.
* Failures use RFC 7807 ProblemDetails with stable problem codes and bounded tracing metadata.
* Retryable documented writes use a stable per-operation UUIDv7 `Idempotency-Key`.
* Operational `/alive`, `/health`, and `/metrics` endpoints are outside generated controller operations.

## Event program summaries

`GET /api/event/{id}/program-summary` returns the public program with local-day
groupings and readiness warnings. Hidden, draft, private, deleted, cross-tenant
or missing events do not become visible through this read. Public venue fields
remain limited by the event's location disclosure policy.

Authorized organizers use `GET /api/event/{id}/management-program-summary` to
include draft items and sections. This response is private/no-store and does not
include physical venue details; use the authorized location-management surface
for those details. Anonymous management calls return 401, and insufficient
management authority returns 403.

The MCP `get_public_event_program_summary` tool retains its safe `not_found`
descriptor and bounded output, including a maximum of 100 program items with
truncation metadata. The HTTP summary is not truncated to that MCP budget.
The internal query-dispatch migration changes no routes, response shapes,
configuration, database schema or generated client.

Public and managed program-summary reads now also work on SQLite instead of
failing on agenda time ordering. Agenda items retain sort-priority order, then
chronological start-instant order even when timestamps have different offsets.
Tenant, publication and deletion boundaries are unchanged. This repair requires
no database migration or configuration change; PostgreSQL behavior is retained.

SQLite also preserves the UTC meaning of event-location creation and optional
reveal timestamps when reading stored data. Approved public venue fields are
therefore no longer incorrectly hidden solely because timestamp timezone
metadata was lost. Location policy, reveal restrictions and pending privacy
review still govern disclosure; managed summaries still omit location envelopes.
No data rewrite, database migration or configuration change is required.

## Agenda item management

Authorized organizers can create, edit and delete agenda items through
`/api/eventagendaitem` without the former missing-event-context denial. Existing
permissions remain authoritative: moving an item requires permission on both
its current event and the destination. Supplying an event ID never grants access
to another organizer's item or another tenant's event.

PATCH requires the current strong `If-Match` concurrency stamp. Invalid or
missing stamps return 400; stale edits return 409. Successful moves preserve UTC
instants and recalculate local times and day assignments for the destination.
Failed writes roll back agenda and location-placement changes together.

Public agenda reads retain publication and venue-disclosure restrictions.
Authorized management reads remain private/no-store; exact venue IDs are only
available through management detail. The MCP
`get_event_program_management_context` tool retains these permission boundaries
and omits physical location details. No routes, payload shapes, database
migrations, configuration changes or client regeneration are required.

## Duplicate session language assignments

`POST /api/eventsessionlanguage` returns `400` with the endpoint's existing JSON validation ProblemDetails body, `code: validation_failed`, and an `errors.program` entry when the language is already assigned to that session. Concurrent submissions retain exactly one assignment: the winning create returns `201`, and the duplicate receives the same controlled validation response. A language may still be assigned to a different session. Existing authorization and tenant boundaries apply before mutation.

This corrects previously provider-dependent duplicate-create failures. It needs no database migration, configuration change, or generated client update; the existing unique constraint remains authoritative.

## Unknown session status IDs

`GET /api/eventsessionstatus/{id}` returns `404` with an
`application/problem+json` body and `code: resource_not_found` when the integer
ID is unknown. This corrects the former empty `204` response to match the
published contract; integrations should handle the documented not-found result.
Known IDs still return the same `200` DTO. The anonymous catalogue at
`GET /api/eventsessionstatus`, all ten global lifecycle IDs, and existing cache
policies are unchanged. No configuration, migration, or client regeneration is
required.

## Missing actor profile data

When a visible actor has no profile PII, actor responses default to an empty display
name and a null profile-picture URI rather than failing while reading the profile.
Existing tenant-specific profile overrides still take precedence.
This does not restore erased data or make a hidden actor visible. Existing profile
values, including an anonymized display name, remain unchanged.

## Missing organization profile data

When an organization's contact profile is absent, its base name and contact fields
can be null instead of causing a response failure. Existing authorized
tenant-participation overrides still apply. Do not infer erasure status or a change
in access rights from a missing field; no erased profile is recreated.

Organization names in member and invitation responses can likewise be null when
that organization's profile is absent. Invitation identity and role values remain
available under the existing access rules.

The schema and generated clients explicitly represent these optional contact and
invitation labels, as well as unresolved organization/group approval names, as
nullable. Required field presence and existing values are unchanged.
Organization pages handle missing labels in search, avatars and edit forms;
the existing validation and server-provided action links still apply.

Continue with [HAL/REST Contract](readme/hal-rest.md) and [API Cookbook](readme/api-cookbook.md).
