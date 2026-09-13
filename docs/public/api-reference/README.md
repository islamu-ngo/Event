---
description: HAL/REST integration guidance for ISLAMU Event API version 0.1.
---

# API Reference

ISLAMU Event exposes a versioned HAL/REST API assembled by thin ASP.NET Core controllers over MediatR application requests. The draft HTTP API version is `0.1` (pre-v1 specification): breaking changes may occur during active pre-release development as endpoint contracts are refined.

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

Continue with [HAL/REST Contract](readme/hal-rest.md) and [API Cookbook](readme/api-cookbook.md).
