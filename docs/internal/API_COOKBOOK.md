ABOUTME: Task-first API integration cookbook for direct callers and contributors.
ABOUTME: Summarizes authentication, tenant context, HAL, errors, pagination, idempotency, and generated-reference usage.

# API Cookbook

> **Audience:** Integrators | Contributors
> **Status:** Implemented
> **Owner:** API
> **Last Verified:** 2026-05-06
> **Source Anchors:** `docs/API.md`, `Explore.API/Controllers/`, `Explore.API/Middleware/IdempotencyMiddleware.cs`, `Explore.API/Hateoas/ResourceAssemblerBase.cs`, `Explore.API/Extensions/AuthenticationExtensions.cs`

## Scope

This cookbook explains how to call the API safely without duplicating every endpoint. Use [API.md](API.md) for authoritative conventions and the generated OpenAPI/Scalar reference for exact request and response DTOs.

## Find The Generated Reference

| Environment | Reference endpoint |
|---|---|
| Development/Testing | OpenAPI JSON at `/openapi/event-api.json`, Swagger UI at `/swagger`, and the Scalar API reference mapped by `MapScalarApiReference()` |
| Docker Compose | `http://localhost:7039` for the API base URL; expose the reference endpoints through the same API service when enabled for the environment. |

Generated reference is the endpoint source of truth. This cookbook focuses on cross-cutting calling rules.

## Choose An Authentication Mode

| Caller type | Header pattern | Notes |
|---|---|---|
| Browser/BFF user | `Authorization: Bearer <token>` | JWT Bearer tokens are validated against Keycloak configuration. |
| Direct integration | `X-API-Key: <key>` | API-key authentication maps the key to owner type, owner identifier, scopes, and tenant context. |

Do not send both `Authorization` and `X-API-Key` on the same request. The API has an authentication conflict guard and rejects conflicting auth inputs before normal authentication runs.

## Bind Tenant Context

Tenant context is resolved before and after authentication:

1. Prefer the tenant host or `X-Tenant-Slug` for tenant-aware API calls.
2. API-key requests may finalize tenant binding after the key is authenticated.
3. Treat `X-Tenant-Id` as legacy/back-compat context, not the primary integration pattern.

If a direct caller receives tenant mismatch or authorization failures, verify that the API key owner and tenant slug refer to the same tenant boundary.

## Request HAL Or Minimal Responses

The API defaults to HAL-oriented responses where resources can include `_links` affordances. Use those links for navigation and available actions instead of hard-coding follow-up routes.

If a write client only needs the resource state and not link affordances, send:

```http
Prefer: return=minimal
```

`Prefer: return=minimal` suppresses generated link material. Collection resources still carry item and paging structure; they do not receive generated collection links when minimal output is requested.

## Page Through Collections

Pagination is 1-based:

- Default `pageNumber`: `1`.
- Default `pageSize`: `20`.
- Maximum `pageSize`: `100`.

Use explicit page parameters for integration jobs so replay and monitoring are predictable. Do not assume unbounded collection endpoints.

## Handle ProblemDetails Errors

Validation and global exception handlers return RFC 7807 ProblemDetails-style responses. Error responses can include trace, timestamp, and correlation information for support.

Common handling rules:

- `400` with validation details means the request contract is invalid; fix the payload before retrying.
- `401` means authentication failed or was absent.
- `403` means the caller authenticated but lacks permission or tenant scope.
- `409` indicates a conflict such as stale template-sync base or concurrent update; reload the latest state before retrying.
- `429` means a rate-limit policy rejected the request; respect retry/backoff behavior.

## Make Write Calls Idempotent

For write operations (`POST`, `PUT`, `PATCH`, `DELETE`), send a stable idempotency key when retrying across network failures:

```http
Idempotency-Key: <stable-operation-key>
```

Eligible responses have a default 24-hour replay window, bounded by any shorter
feature disclosure deadline. The API retains at most 1 MiB before response
capture switches to direct forwarding. Replayable responses are bounded JSON
(or blank content type), `2xx` through `4xx` except `400`/`415`, and must remain
fully captured without an explicit response start/flush. Large, streamed,
non-JSON, validation and server-error responses cannot prove business rollback.
Their claims remain fenced rather than becoming safe retries.

Reuse a key only for the identical method, target, content and caller authority.
Changed requests receive `409 idempotency_key_reuse`. A matching operation with
an unavailable response receives `409 idempotency_response_unavailable`; inspect
the current authorized resource or feature-owned operation status before deciding
on another business action. Do not rotate the key automatically after a timeout,
abort, serialization failure or this conflict: the first operation may already
have committed. Receipt expiry scrubs the cached body but preserves the identity
fence, so waiting 24 hours does not reopen the operation.

Authentication, authorization and tenant lifecycle admission remain ahead of
replay. Protected receipts are private no-store and record-purpose-bound;
one-time/API-key issuance bypasses generic receipt storage. The implementation
and native rollback-proof boundary are described in [API](API.md#bounded-response-capture-and-ambiguous-operation-outcomes).

## Example Flow: Public Browse

1. Call the generated reference to confirm the current endpoint shape.
2. Request the public collection endpoint with explicit `pageNumber` and `pageSize`.
3. Follow HAL links when present.
4. Continue until collection metadata indicates no more pages.

Public browse endpoints are often anonymous, but the generated reference remains authoritative for endpoint-specific requirements.

## Example Flow: Direct Caller Mutation

1. Create an API key in the relevant admin scope. See [ADMIN_GUIDE.md](ADMIN_GUIDE.md).
2. Send `X-API-Key` and tenant context (`X-Tenant-Slug` or tenant host).
3. Send `Idempotency-Key` for create/update/delete calls that may be retried.
4. Use `Prefer: return=minimal` if link affordances are not needed.
5. On `409`, inspect its problem code and reload the authorized resource or
   operation status. Reconcile unavailable outcomes before planning a new action;
   do not resubmit an uncertain committed action with a new key.

## Example Flow: Template Sync

1. Read the sync diff for the target event or session.
2. Present the diff to an operator for approval.
3. Apply the sync plan with the base provenance version returned by the diff flow.
4. If the API returns stale-base or concurrent-update `409`, discard the old plan, reload the diff, and apply a new plan.

## Integration Checklist

- [ ] Use one authentication mode per request.
- [ ] Bind tenant context with host or `X-Tenant-Slug`.
- [ ] Respect HAL links when action availability matters.
- [ ] Use explicit pagination.
- [ ] Parse ProblemDetails and log correlation/trace details.
- [ ] Use idempotency keys for retryable writes.
- [ ] Use generated OpenAPI/Scalar docs for DTO fields and endpoint-specific status codes.

## Related Documentation

- [API.md](API.md) — authoritative API architecture and conventions.
- [API_CHANGELOG.md](API_CHANGELOG.md) — API-specific changes.
- [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md) — authentication, authorization, and trust boundaries.
- [ADMIN_GUIDE.md](ADMIN_GUIDE.md) — API-key administration surfaces.
