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
