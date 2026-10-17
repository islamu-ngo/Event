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

## Reconciliation Worker Recovery

If the database accepts a reconciliation batch but its acknowledgement is
lost, the worker recovers that original, still-live batch before doing more
work. It keeps the same lease token and deadline. Recovery does not extend
the lease, increase retry counters or reserve another batch.

A cancelled worker may briefly use a separate, bounded recovery read to
establish which claims committed, but it does not proceed to payment-provider
calls after cancellation. Expired or replaced claims cannot authorize payment
processing. A later scheduler pass acquires expired work with a new ownership
fence rather than reviving the old one.

The cross-tenant admission connection receives only bounded claim identifiers
and health totals. Payment details and settlement remain isolated to the exact
tenant. Do not grant Global direct payment-table access or manually extend
leases to bypass recovery. Database claim recovery is separate from resolving
an uncertain Stripe request: original connected-account context and provider
operation keys still apply.

These recovery changes have source regression coverage prepared; database
execution and deployment assurance remain queued until programme verification.

## 2. Refund Workflows

### Recovering One Refund Operation

Retry the original refund operation with the same account, authority, reason
and amount choice. Omitting the amount means refunding the remainder captured
by that first reservation; it does not recalculate the remainder on each retry.
An explicit amount is a different input. Exact recovery returns the original
refund even after its reservation exhausts capacity. Changing inputs under
the same operation conflicts without exposing the earlier refund.

Buyer refunds and application-fee refunds have separate durable handoff
ownership. A timeout, crash, cancelled request or pending marker never proves
that the provider received nothing. Recovery reads the original provider
evidence rather than blindly repeating a mutation. Only positively recorded
pre-handoff failure permits another dispatch owner.

The Stripe refund mutation window is 23 hours from its first committed handoff;
it is not renewed and is independent of checkout deadlines. Expiry or an empty
provider listing does not release refund capacity or cancel the obligation.
Use provider evidence and authorized manual reconciliation when the result
remains uncertain. Signed callback success takes precedence over stale local
unknown results. Removing an initiating operator does not erase an already
committed refund obligation.

These changes have native regression source prepared. Generated database
updates and real provider/concurrency assurance remain queued for the security
programme; this documentation is not deployment certification.

### Uncertain Provider Requests

A rejected checkout or replacement request leaves the previous order and
inventory deadlines unchanged. It also leaves the prior payment attempt's
active-slot ownership unchanged. A queued replacement is reusable only when
its composition and buyer acceptance are still current; a stale disclosure
must be reviewed rather than silently accepted.

The Stripe adapter does not automatically repeat mutation requests after a
timeout, connection failure or retryable response. Such an outcome can mean
that Stripe accepted the request but the acknowledgement was lost. Checkout,
refund, cancellation and onboarding operators must reconcile the original
operation rather than create another operation or assume nothing happened.

Keep the original provider operation key and connected-account context.
Automatic unknown-checkout redrive requires an explicit matching provider
retention guarantee. Stripe's configured floor is 24 hours, measured
conservatively from the original attempt creation with a one-minute margin.
Extending a checkout deadline never extends that replay window. If the
guarantee is missing, expired or the pinned provider API revision changes, the
operation remains uncertain without another mutation. Retrieve a known session
or use authorized manual reconciliation; do not create a new key to bypass it.
Durable native payment and refund processing owns any subsequent redrive;
transport retries do not bypass its lease or authority checks. Reusing a key
after the provider's retention window is not a duplicate-prevention guarantee.
When evidence cannot establish the original outcome, use reconciliation or
manual review instead of issuing a fresh mutation. Existing request timeouts
remain bounded; disabling SDK retries does not remove those deadlines.

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
