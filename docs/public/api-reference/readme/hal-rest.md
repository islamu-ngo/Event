---
description: >-
  Media types, hypermedia affordances, pagination, errors, concurrency, and
  idempotency.
---

# HAL/REST Contract

## Media types and minimal responses

Request HAL explicitly when you need navigable affordances:

```http
Accept: application/hal+json;v=0.1
```

Use `Prefer: return=minimal` when a machine client does not need generated links. This reduces representation size but also removes the client's action authority; do not use it for a UI that must decide which mutations to show.

## Hypermedia is the action contract

A resource or collection may expose relations such as `self`, `edit`, `delete`, `check-in`, `undo-check-in`, `request-refund`, `create-refund`, or administration actions. Follow the link currently returned for the current caller, tenant, resource state, and policy.

Do not construct mutation URLs from naming conventions or enable controls from local roles/claims. A link may disappear after state, tenant, policy, concurrency, or provider changes. Refresh the representation after a mutation or authorization-relevant event.

## Private setup journey

`GET /api/instanceonboarding/journey` requires active setup credentials forwarded
by the BFF or a signed-in instance administrator. It returns a private, no-store
snapshot; follow its current HAL actions. Unauthenticated requests receive 401,
and signed-in callers without setup or administrator authority receive 403.
Finishing setup does not grant public access to a Provisioning directory.

## Operator identity form metadata

After normal sign-in, follow the instance identity document's `form-options` link
for shared form vocabulary. The metadata resource returns no saved names, contact
emails, registration values or credentials, and offers no edit authority.
Use its stable `labelId` and `helpId` keys for localized labels and visible,
programmatically associated guidance. Country display names come from installed
runtime globalization data; `countryState: Unavailable` means the selector must
show an unavailable state, not silently invent choices. Domain validation remains
authoritative, including country-code shape and field length limits.

Registration identifiers are optional. `registrationAuthorityState: NotSupported`
and an empty authority list mean there is no authority registry to present.
Metadata describes disclosure and commerce requirements separately, not draft-save
requirements. Follow the value-bearing identity document's authorized save link
and revision; metadata `self` and `refresh` are read-only affordances.

## Registration provider administration

Registration provider management remains one `RegistrationProviderManagement` API/SDK
group under `/api/tenants/{tenantId}/events/{eventId}/registration-providers`. It covers
connections and approved origins; external schema imports, bindings and mappings;
channels and launch descriptors; and health, queue and reconciliation.

All of these endpoints require authentication, including reads, and use private,
no-store responses. Follow the permission-checked HAL links for available actions.
The server's capability split does not change endpoint URLs, operation names, API
version `0.1`, or JSON/HAL media types; no client routing or SDK-group migration is required.

## Pagination

List operations use 1-based pages:

```http
GET /api/events?page=1&pageSize=20
```

Default page size is `20`; maximum is `100`. Preserve server-provided navigation links rather than calculating routes that may not carry all query/version context.

## Writes, concurrency, and idempotency

Use the HTTP method and target from HAL. Where the endpoint documents replay protection, generate one UUIDv7 per logical operation and reuse it only for retries of that same operation:

```http
Idempotency-Key: 0198f4a6-7b8c-7def-8123-456789abcdef
```

The platform scopes retained write replays by tenant and key for a bounded window. A new business action needs a new key. Idempotency does not replace optimistic concurrency, resource-version checks, provider reconciliation, or domain validation.

## ProblemDetails

Failed commands never return a success-shaped command body. Errors use `application/problem+json` and may include:

```json
{
  "type": "https://errors.islamu.example/problem-code",
  "title": "Request could not be completed",
  "status": 409,
  "detail": "A bounded public explanation.",
  "traceId": "...",
  "correlationId": "...",
  "timestamp": "..."
}
```

Treat `type`/problem code and HTTP status as the stable machine-facing signal. Production responses hide internal parser paths and unhandled exception detail. Authentication, authorization, not-found, concurrency, validation, and quota failures are normalized by shared handling.

## Privacy and caching

Private account, commerce, refund, and erasure responses are `no-store`. Never persist provider IDs, admission bearer material, idempotency material, erasure receipts, raw provider errors, or PII from diagnostic responses. Health and metrics are operational surfaces, not data-export APIs.

## Guest registration capabilities

Guest order start/read/lifecycle, requirements, participants, promotions and account
claim keep their existing URLs and `GuestRegistrationOrder` API/SDK group.
Keep order and attempt capabilities in their dedicated headers, not URLs.
Follow the returned HAL actions and retain the required idempotency/challenge
proofs for writes. Account claim remains authenticated; an order capability alone
does not establish account authority.
