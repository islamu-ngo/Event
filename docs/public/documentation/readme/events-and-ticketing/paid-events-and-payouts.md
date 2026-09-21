---
description: Operate organizer-direct Stripe Connect payments, reconciliation, refunds, and payout boundaries.
---

# Paid Events & Payouts

ISLAMU Event utilizes **Stripe Connect** as its primary payment adapter. The commercial model is **organizer-direct**: attendees pay the event organizer’s connected Stripe account directly, rather than funds pooling into platform escrow.

---

## 1. Organizer-Direct Commerce Flow

1. **Provider Onboarding**: The organizer completes Stripe-hosted Express/Standard onboarding via organization settings (see [Administration Guide](../administration-and-branding/admin-guide.md#4-organization--group-governance)).
2. **Checkout Session**: When an attendee registers for a paid ticket (see [Ticketing & Check-In](ticketing-and-check-in.md)), a Stripe Checkout session is created in the connected account's context.
3. **Webhook Reconciliation**: Payment truth is established strictly by signed Stripe webhook events (see [Webhooks & Callbacks](../integrations-and-ai/webhooks.md)). A browser redirect back to the app is never treated as financial settlement.
4. **Admission Issuance**: Cryptographic admission tickets are generated only after signed webhook reconciliation succeeds.

> [!IMPORTANT]
> **Operator Legal Identity Prerequisite:**  
> Paid checkout and ticket publication retain their persisted operator-identity readiness gates. Review **Required before paid events** in the [getting-started checklist](../administration-and-branding/admin-guide.md#getting-started-after-setup); installation completion and disclosure readiness alone do not imply commerce readiness. Configure the current document through authenticated administration. Environment identity values are optional bootstrap seeds. Merchant, recipient, revision, terms and payment-acceptance checks remain independently enforced.

---

## 2. Refund Workflows

The platform implements durable, provider-backed refund state machines:
* **Attendee Requests**: Attendees can request refunds through their self-service order portal.
* **Organizer Approval**: Organizers evaluate refund requests and approve or reject them directly in the management console.
* **Partial Refunds**: Support for partial line-item refunds or full order cancellations.
* **Finality Guarantee**: An order is marked "Refunded" only after Stripe delivers signed webhook confirmation of fund return.

---

## 3. Payout Boundaries & Operator Responsibility

Provider-managed schedules determine payout timing directly to the organizer's bank account. ISLAMU Event does not act as an escrow agent, banking institution, or tax accounting authority. Operators must ensure their public terms and refund policies comply with local e-commerce laws.

---

## Related Guides & Next Steps

* **[Ticketing & Check-In](ticketing-and-check-in.md)** — Manage ticket capacity, admissions, and gate check-in.
* **[Webhooks & Callbacks](../integrations-and-ai/webhooks.md)** — Verify signed Stripe webhook intake and replay windows.
* **[Platform Monetization Policy](../administration-and-branding/admin-guide.md#platform-monetization)** — Configure platform application fees on ticket sales.
* **[Operator Legal Identity Reference](../configuration-and-operations/environment-variables.md#9-operator-legal-identity-production-gate)** — Mandatory production legal disclosures.
