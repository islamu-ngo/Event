<!-- ABOUTME: Architectural decision record for paid events, organizer merchant authority, and Stripe Connect. -->
<!-- ABOUTME: Defines OrganizerDirect, immutable commercial snapshots, refund protection, and provider reconciliation. -->

# ADR-022: Paid Event Commerce And Stripe Connect

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-08-13 |
| **Deciders** | ISLAMU Event Platform - Architecture, Security, Registration, and I-VSD workstreams |
| **Supersedes** | The payment deferral in ADR-018 and the payment record in `deferred-design-records.md` |
| **Superseded by** | - |

## Context

ADR-018 deliberately stops positive registration orders at `AwaitingPayment`. Paid events now require a concrete merchant, immutable currency and recipient facts, durable provider attempts, truthful refund state, and reliable handling of delayed or duplicate provider evidence. The event organizer actor is the commercial authority; listing contribution and instance or tenant administration do not imply authority to receive proceeds.

The I-VSD payment consultation identifies Stripe Connect direct charges on the organizer's connected account as the safest first self-hosted profile. It also rejects hidden administrator recipients, blanket no-refund behavior, and claims that ordinary Stripe payouts are held until an event.

## Decision

### Merchant and policy authority

1. `OrganizerDirect` is the only first-release payment profile. Direct charges are created in the event organizer actor's connected-account context. There is no tenant-admin, instance-admin, or pooled fallback merchant.
2. Every self-hosted deployment supplies and operates its own Stripe Connect platform credentials. ISLAMU credentials and official-instance trust never extend to unrelated deployments.
3. Effective paid-event policy is the intersection of hard product invariants, the instance ceiling, tenant narrowing, organizer/event choice, and live provider/account capability. A tenant can narrow but never broaden instance policy.
4. Paid publication requires an eligible organizer actor, permitted actor kind, required local verification, an active actor-bound connected account, charge readiness, one supported currency, refund/support disclosures, and current provider capability.
5. Connected-account replacement affects future publication and payment only. Historical recipient, merchant country, currency, profile, and policy snapshots are immutable.
6. Buyer acceptance records separate organizer merchant, tenant directory
   operator, general instance operator, and payment-operation evidence. Tenant
   branding is never an identity source. The server recomposes the exact
   versioned disclosure before provider handoff, and any authority revision
   invalidates stale acceptance.

### Money, promotions, and payment attempts

1. Published catalogs and orders use one explicit ISO currency and integer minor units. Venue location may suggest a currency but never selects it. Internal foreign exchange and adaptive pricing remain outside scope.
2. Promotions are versioned local Event state. Live unexpired reservations count against redemption limits and release exactly once on expiry or cancellation. Stripe receives only the final immutable charge composition.
3. `PaymentAttempt` is provider-neutral and independent from order, approval, inventory, registration attempt, and refund state. It persists order, amount, currency, merchant, connected account, provider identity, and a durable idempotency key before provider handoff.
4. Application owns narrow capabilities for connected-account readiness, Checkout/payment retrieval, refund retrieval, and the separately gated payout profile. Infrastructure alone owns Stripe SDK clients, options, models, exceptions, and transport details.
5. Stripe-hosted Checkout direct charges use the official stable `Stripe.net` SDK through one configured instance-based `StripeClient`. Per-request connected-account and idempotency values are required. Preview packages, undocumented parameters, raw requests, global API-key configuration, a provider factory, and a generic Stripe service are forbidden.

### Provider truth and transaction boundaries

1. A hosted onboarding return URL is navigation only. Account retrieval, required capabilities, requirements, restrictions, and reconciliation determine readiness.
2. A Checkout return URL or accepted provider request is not payment proof. Signed webhook evidence and scheduled retrieval/reconciliation advance monotonic local state.
3. Provider calls execute outside business transactions from durable work. Local transactions persist attempt identity, operation claims, state transitions, and required outbox facts.
4. Connect webhooks verify the exact raw body and `Stripe-Signature`, deduplicate provider event IDs, retain a minimal normalized envelope, and acknowledge safely. Callback controllers never mutate payment or order aggregates directly.
5. Duplicate, delayed, out-of-order, and ambiguous outcomes are normal inputs. They must be idempotent or reconciled rather than retried as new creates.

### Refunds, disputes, and buyer protection

1. A versioned runtime refund floor cannot be weakened by organizer terms. Organizer cancellation, duplicate or incorrect charging, and material non-delivery require the configured mandatory remedy, subject to applicable law.
2. `RefundAttempt` is independent from payment and order state and remains pinned to the original connected account. `Requested` and `Pending` are never presented as `Succeeded`.
3. Cancellation stops new sales locally, commits one durable refund operation per captured payment, then processes Stripe calls after commit. Webhooks and reconciliation own terminal truth.
4. Disputes remain provider evidence projected for organizer and operator action. Local state never claims to overrule Stripe or card-network outcomes.
5. `ProtectedDelayedPayout` is excluded from this decision's default profile and is governed by ADR-024.

## Supported Stripe Capability Matrix

The approved Event payment surface is limited to:

- Connect hosted onboarding, account retrieval, requirements, capabilities, restrictions, and account-event reconciliation;
- connected-account direct-charge hosted Checkout and payment retrieval;
- signed Connect webhook intake;
- refund creation/retrieval and dispute projection in the original connected-account context;
- scheduled reconciliation of non-terminal or ambiguous operations;
- payout controls only if ADR-024's separate approval gate is later satisfied.

Stripe Billing, subscriptions, Tax, Invoicing, Payment Links, Terminal, Issuing, Treasury, previews, undocumented parameters, and raw API requests are outside this workstream.

## Rejected Alternatives

1. Destination charges or separate charges and transfers as the self-hosted default.
2. An instance-admin or tenant-admin fallback merchant.
3. Rewriting historical recipients after account or organizer changes.
4. Provider I/O inside order, capacity, cancellation, or refund transactions.
5. Treating onboarding return, Checkout return, request acceptance, or browser state as provider truth.
6. Floating-point money, implicit currency, mixed-currency orders, or internal FX.
7. Provider-owned promotion truth or reservation-unaware redemption counts.
8. Editable terms text as the refund engine.
9. Calling normal payouts held-until-event or escrow.

## Consequences

- Paid events require more durable state and reconciliation, but merchant, buyer, and operator responsibility remain explicit.
- Stripe implementation stays isolated in Infrastructure behind Application-owned use-case ports.
- Historical payment and refund operations remain interpretable after organizer, account, policy, or catalog changes.
- `OrganizerDirect` remains usable without the optional protected-payout profile.
- Production launch still requires live Stripe configuration evidence, legal review, Islamic-finance review where applicable, and owned incident operations; this ADR is not legal or Sharia certification.

## Related

- `islamic-value-sensitive-design/consultations/i-vsd-paid-event-payments-consultation.md`
- `dev/active/registration-data-collection/registration-data-collection-plan.md` D21-D25
- ADR-002: Transactional Outbox Pattern
- ADR-017: Event Participation Authority Model
- ADR-018: Registration Order And Ticketing Aggregate
- ADR-024: External Business Integrations And Protected Payout Boundaries
