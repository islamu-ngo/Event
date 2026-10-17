---
description: Operate registration, admission credentials, recovery, and online server-authoritative gates.
---

# Ticketing & Check-In Architecture

Registration, admission issuance, and day-of-event check-in are distinct lifecycle stages. A registration records an attendee’s sign-up or purchase intent; an admission entitlement represents the cryptographic token presented at the venue gate.

---

## 1. Admission Issuance & Cryptographic Hashing

* **Entitlement Production**: Triggered automatically upon completing a free event registration or upon receiving verified payment settlement for paid events (see [Paid Events & Payouts](paid-events-and-payouts.md)).
* **Credential Protection**: The database **never** stores plaintext QR ticket tokens or attendee PII in admission tables. Tickets store versioned **HMAC-SHA-256 lookup digests**, guaranteeing that a compromised database snapshot cannot be used to forge valid event entry passes.
* **Session-Level Admissions**: Tickets can optionally expand into session-specific entitlements for multi-track conferences.

---

## Authenticated registration creation and retries

When creating an order with
`POST /api/events/{eventId}/registration-orders`, send an `Idempotency-Key`
containing 1-128 ASCII letters, digits, `.`, `_`, `:` or `-`. Choose one key for
one purchase intent and retain it until the outcome is known. The signed-in
account and resolved tenant come from trusted server authentication, not the
request body. API keys and other purpose-specific credentials do not become
buyer accounts on this route.

If the connection drops, the response is lost or a request is cancelled, retry
the same key, account, tenant, authentication purpose and capability scope with
the same event, catalogue, booking party, ticket selections, quantities,
prices and contribution selection. Reordering ticket selections is harmless;
changing a value is not. An omitted contribution and explicit zero are distinct
requests. A matching committed retry returns the original order without
allocating again or extending its hold deadline, even if new registrations
have since been disabled.

Changing the payload under the same operation returns `409 Conflict`. Missing
or invalid keys return a validation error. If the original order or its
required parent data has expired, been deleted or become inaccessible, recovery
does not disclose it and does not reuse the key. Current authentication is
required on every attempt; possession of a key or order ID never grants access.
Responses are private and are not stored by generic HTTP idempotency replay.

Changing an optional platform contribution during submission takes effect
only when that submission succeeds. If the order cannot advance, its previous
contribution and payable total remain unchanged. State advancement and the
accepted contribution change are committed together.

The supplied browser registration service keeps the pending key and exact
payload for retries within its current session. It will not silently replace
an uncertain purchase with edited fields. A page reload is not proof that the
first request failed: integrations should retain their operation key, and
operators should reconcile the original operation rather than advise creating
a fresh key to bypass an uncertain result.

Creation evidence is saved atomically with the order, lines and holds. The
retained marker contains only digests and internal tenant/order pointers, not
contact details, raw keys, credentials or a copy of the request. Deleting
personal data or expiring ordinary HTTP replay records does not erase this
duplicate-creation fence. Do not manually delete these markers to make a retry
run again. Normal erasure and registration-retention workflows remain the
owners of personal-data disposal.

This source implementation requires the corresponding generated database and
API/client updates and final programme verification before deployment. It does
not change the guest protected-challenge recovery protocol.

---

## 2. Lost-Ticket Recovery

When an attendee requests lost ticket recovery:
* The system responds with an indistinguishable `202 Accepted` response (preventing user enumeration).
* A single-use recovery capability is dispatched via [Durable SMTP Email](../communications-and-notifications/email-smtp.md).
* Accessing the recovery link automatically rotates the ticket token and invalidates the previous QR code atomically.

---

## 3. Server-Authoritative Gate Check-In

* **Online Validation Only**: Gate validation is strictly server-authoritative. If internet connectivity drops at the venue, scans fail closed. The platform intentionally does not implement unverified offline sync queues to prevent double-entry fraud.
* **Target Scoping**: Scanner tokens are cryptographically bounded to a specific event or session target.
* **HATEOAS Affordance Gating**: The volunteer check-in interface displays "Check-In", "Undo", or "Override" buttons strictly based on server-issued [HAL links](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances).

---

## Pre-Door Gate Checklist

1. Verify that email dispatch is healthy for ticket delivery (see [Email SMTP](../communications-and-notifications/email-smtp.md)).
2. Issue a test free and paid ticket through the production path.
3. Scan the test QR code and verify that a second scan flags an idempotent duplicate warning.
4. Verify venue internet connectivity (Wi-Fi or cellular) before doors open.

---

## Related Guides & Next Steps

* **[Paid Events & Payouts](paid-events-and-payouts.md)** — Configure ticket pricing, Stripe Connect, and attendee refund workflows.
* **[Modular Event Aspects](modular-event-aspects.md)** — Relational sector models for tracks, speakers, and prayer schedules.
* **[Email SMTP Configuration](../communications-and-notifications/email-smtp.md)** — Ensure reliable delivery of confirmation and recovery emails.
* **[Authorization & HAL Affordances](../security-and-identity/authorization.md)** — Understand how volunteer gate access is authorized.
