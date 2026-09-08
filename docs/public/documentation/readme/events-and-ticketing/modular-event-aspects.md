---
description: Use typed Islamic and technology event data without weakening the core model.
---
<!-- ABOUTME: Explains typed event aspects and the visitor capabilities that bound participation. -->
<!-- ABOUTME: Separates organizer data extensions from account onboarding and allocation authority. -->

# Modular Event Aspects

ISLAMU Event separates universally shared event/session fields from optional typed sector aspects. This preserves a clean, lean core domain model while allowing rich, first-class relational extensions.

---

## Three-Tier Event Model Layers

1. **Core Domain Fields**: Universally shared concepts: title, description, schedule, venue location, organizer ownership, capacity, and publication lifecycle state.
2. **Typed Sector Aspects**: Relational models for sector-specific event and session data (e.g. Islamic event details, prayer accommodations, speaker credentials, technology workshop requirements).
3. **[Governed Custom Properties](custom-properties.md)**: Controlled long-tail fields and attendee registration questionnaires that do not belong in the core relational schema.

> [!NOTE]
> Typed aspects are structured database entities with strongly typed foreign keys and indices, not unstructured, opaque JSON property bags.

---

## Feature Module Gating

Sector capabilities are governed by tenant feature flags (e.g. `Mod_Islamic` or `Mod_Tech`):
* When a module is enabled, its sector-specific fields, filters, and UI editors become active.
* If a module is disabled for a tenant, its filters and validation rules are cleanly bypassed rather than partially applied.
* Public API responses omit disabled module attributes, keeping responses compact.

---

## Architectural Design Boundary

* Use a **Typed Aspect** when the concept possesses universal community semantics, dedicated validation, query indices, or lifecycle hooks (e.g. prayer times, halal catering, tech tracks).
* Use a **[Governed Custom Property](custom-properties.md)** for organizer-specific, one-off questions (e.g. "T-shirt size", "Dietary allergies", "Emergency contact").
* Neither mechanism may ever be used to bypass [Authorization](../security-and-identity/authorization.md), [Payment Truth](paid-events-and-payouts.md), or [Admission Issuance](ticketing-and-check-in.md).

## Visitor Access And Account-Required Participation

Tenant public-experience settings distinguish visitor access from the directory's
display mode:

| Visitor access mode | New native participation |
| --- | --- |
| `FullRegistrationAndAuth` | Anonymous participation and only the account-based capabilities actually available from usable providers. |
| `AnonymousOnly` | Anonymous participation; no new AccountRequired configuration. |
| `DirectoryListingOnly` | No new native allocations. Existing lawful registration status and cancellation remain available. |

Local sign-in is an operator/existing-credential function, not public signup.
A Local-only instance can offer guest participation, but Local credentials alone
do not make AccountRequired event configuration usable.

For configurable providers such as Keycloak or Google, an operator must declare
public onboarding `Allowed` and configure its actual HTTPS signup destination.
`Unknown`, `Denied`, a missing URL or a successful provider-discovery probe does
not establish signup availability. Enabled, usable AT Protocol onboarding can
contribute even when it is not the primary provider. Follow the provider-specific
action offered by the server rather than constructing a signup URL.

Create and Studio controls use the server's capability. An unavailable
AccountRequired choice is not silently converted to anonymous registration.
Before disabling the last eligible onboarding path or tightening visitor mode,
explicitly amend affected AccountRequired event configurations, including drafts;
otherwise the settings change returns a conflict.

Operator login, listing information, walk-in handling and external registration
remain separate from native allocation. A restrictive visitor setting does not
erase an existing registration or grant broader authority to access it.

## Anonymous Reservations And Retry

Before creating a guest reservation, the browser requests a short-lived challenge
for the exact event and ticket selection. A background worker performs the
verification work with progress and cancellation; there is no cognitive puzzle
or external CAPTCHA service. Issuing or cancelling this work before submission
does not reserve a seat.

Fresh challenges last two minutes. The default work difficulty is 18 bits and
operators can select 16 through 22. These are bounded starting values, not a
guarantee for every mobile device. If the browser cannot complete the work, use
the existing staff-assisted registration or approval process. There is no
unprotected public reservation alternative.

If a submitted request times out or its response is lost, the page retains the
original private request and offers an explicit retry. Do not change the ticket
selection or start over while that outcome is uncertain. A valid exact retry
recovers the same committed order and capability without extending the hold or
reserving another seat. Historical recovery is bounded to the original challenge
expiry plus 24 hours; an expired proof cannot create a new reservation.

Ticket limits, capacity, approval and visitor policy still apply to new
allocations. Process IP/subnet and concurrency limits supplement durable tenant
and event challenge budgets, whose defaults are 600 and 120 issues per minute.
Rate-limited issuance does not reserve inventory. Operators should tune only the
documented bounded controls and retain the shared persistent protection keys
across replicas and restarts.

The challenge and returned guest capability are private bearer material. Do not
copy them into diagnostics, analytics or support tickets. The browser does not
add storage or tracking for challenge retries.

---

## Related Guides & Next Steps

* **[Custom Properties Governance](custom-properties.md)** — Design custom attendee registration forms.
* **[Ticketing & Check-In](ticketing-and-check-in.md)** — Manage capacity, admission tickets, and QR validation.
* **[Paid Events & Payouts](paid-events-and-payouts.md)** — Connect Stripe accounts and manage paid tickets.
* **[Administration Guide](../administration-and-branding/admin-guide.md)** — Enable and disable sector modules in the admin console.
