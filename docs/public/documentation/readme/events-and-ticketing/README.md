---
description: Typed event data, governed properties, admission, check-in, payments, refunds, and payouts.
---

# Events & Ticketing

ISLAMU Event keeps event domain content, attendee registration, payment truth, admission issuance, and physical check-in as distinct authorities. This section explains each lifecycle and the invariants operators must preserve.

---

## In this Section

* **[Modular Event Aspects](modular-event-aspects.md)** — Relational sector models (Islamic-event details, prayer times, speakers, technology tracks) and feature module gating.
* **[Custom Properties](custom-properties.md)** — Governed custom registration questions, privacy exposure ceilings, property retirement, and GDPR data scrubbing.
* **[Ticketing & Check-In](ticketing-and-check-in.md)** — Registration vs. admission, cryptographic QR credentials, attendee recovery, and day-of-event check-in gates.
* **[Paid Events & Payouts](paid-events-and-payouts.md)** — Organizer-direct Stripe Connect onboarding, webhook reconciliation, refund workflows, and payout boundaries.

---

## Operating Invariant

Before publishing a paid or controlled-entry event, verify module policy, registration capacity, provider-confirmed payment/refund state, admission issuance, credential recovery, and exact-target check-in. Current server-issued [HAL links](../security-and-identity/authorization.md#the-golden-rule-of-client-ui-affordances) govern every operator affordance.

## Removing a Scheduled Item

Deleting a session, session group, or agenda item removes that item's venue and room references together. It does not delete the shared venue or another scheduled item's location assignment.

---

## Related Guides & Next Steps

* **[Administration Guide](../administration-and-branding/admin-guide.md)** — Configure platform monetization and organization verified badges.
* **[Email SMTP Notifications](../communications-and-notifications/email-smtp.md)** — Reliable ticket delivery and event confirmation emails.
* **[Webhooks & Callbacks](../integrations-and-ai/webhooks.md)** — Reconcile external ticket sales and payment status changes.
* **[Privacy Erasure & GDPR](../security-and-identity/privacy-erasure.md)** — How attendee registrations and custom answers are scrubbed on account deletion.
