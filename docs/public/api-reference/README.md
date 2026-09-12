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
