<!-- ABOUTME: Architectural decision record for Event product boundaries and conditional protected payout. -->
<!-- ABOUTME: Keeps specialist business systems external and makes delayed payout an approval-only profile. -->

# ADR-024: External Business Integrations And Protected Payout Boundaries

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-08-13 |
| **Deciders** | ISLAMU Event Platform - Product, Architecture, Security, Operations, and I-VSD workstreams |
| **Supersedes** | The Event-owned tax, invoice, and accounting direction retained in `deferred-design-records.md` |
| **Superseded by** | - |

## Context

Paid event operation requires promotions, payments, refunds, disputes, admission, check-in, waitlists, and event-bound add-ons. It does not require ISLAMU Event to become an email-marketing, CRM, accounting, tax, invoicing, banking, or escrow product. These specialist domains have different legal authorities, retention duties, configuration, and provider availability.

The optional promise that funds are controlled until an event also has materially different legal, provider, operational, and Islamic-finance implications from the default `OrganizerDirect` profile. It cannot be a tenant toggle or a prerequisite for ordinary paid events.

## Decision

### Event-owned breadth

1. Event owns event discovery, publication, organizer authority, ticket catalogs, capacity, promotions, orders, payments, refunds, disputes, waitlists, event-bound add-ons, ticket delivery, admission, check-in, transfer, and the immutable commercial facts needed to explain those operations.
2. Waitlist entries and offers are separate from orders until an offer atomically reserves capacity. Offers are bounded, idempotent, and expiry-safe across ticket types sharing a pool.
3. Event-bound add-ons have separate catalog, order-line, inventory, and fulfillment concepts. They never become ticket entitlements, participant assignments, admission credentials, or check-in state.
4. A payment receipt is not a legal invoice. Event may retain external document references and bounded sync status but never guesses that an invoice, credit note, tax result, or accounting entry exists.

### External specialist systems

1. Listmonk or another approved marketing system owns lists, campaigns, templates, scheduling, delivery, and campaign analytics. Event owns consent and the bounded post-commit contact facts that may be transferred.
2. Qonto or another approved finance system owns bookkeeping, accounting, tax determination, legal invoice and credit-note numbering, document issuance, e-invoicing, and finance retention.
3. External integrations are optional, asynchronous, post-commit, actor-bound, least-privilege, and independently reconcilable. They never block core Stripe checkout, refunds, admission, or self-hosted operation.
4. A provider-specific adapter is added only for an approved use case. No speculative universal marketing, finance, tax, or accounting provider framework is created.

### Conditional protected payout

1. `ProtectedDelayedPayout` is not part of the default payment profile. `OrganizerDirect` remains complete and truthful without it.
2. Phase 24 may start only when all four evidence gates are current and accepted:
   - Stripe confirms the exact connected-account, platform-control, Dashboard, country-corridor, holding-limit, loss-liability, refund, and dispute contract;
   - qualified Belgian/EU counsel accepts the payment-service, consumer, holding, and disclosure allocation;
   - qualified Islamic-finance review addresses delayed balances, fees, reserves, negative balances, uncertainty, and stewardship;
   - an accountable operator owns reserves, complaints, disputes, reconciliation, release, incident response, and recovery drills.
3. Missing, expired, contradictory, or preview-only evidence disables the profile. The profile must not use raw or undocumented Stripe operations.
4. The profile uses an explicit immutable `SettlementReleaseAt` or equivalent milestone. Public event end time, midnight, browser state, or a schedule guess never authorizes release.
5. Release requires the milestone, provider eligibility, no cancellation or blocking review, resolved required refunds, and compliance with country-specific holding limits.
6. The profile is never described as escrow. Manual payouts, payout scheduling, reserves, or platform controls must be disclosed using their actual provider and legal meaning.

## Rejected Alternatives

1. Building campaign composition or marketing automation into Event.
2. Building an Event-owned ledger, tax engine, invoice numbering system, or accounting dashboard.
3. Treating a receipt as an invoice or a queued integration request as provider success.
4. Treating general add-ons as admission tickets.
5. Making Qonto, Listmonk, or another specialist provider mandatory for paid events.
6. A generic provider framework before a second real provider proves the shared contract.
7. Enabling delayed payout from tenant configuration or inferred event end time.
8. Calling provider payout controls escrow or promising protection without operational and legal authority.
9. Blocking `OrganizerDirect` while optional protected-payout approval is absent.

## Consequences

- ISLAMU Event remains focused on event operation while retaining durable integration facts and health.
- Specialist systems can evolve or be replaced without changing Event's core commercial and admission aggregates.
- Self-hosters can operate paid events without a marketing or finance-provider account.
- Phase 24 remains blocked until all named approval evidence exists; its absence is an expected safe state.
- The Event/external-system ownership matrix in `dev/report/event-platform-boundary-and-external-business-integrations.md` is the implementation handoff for future optional connectors.

## Related

- `dev/report/event-platform-boundary-and-external-business-integrations.md`
- `islamic-value-sensitive-design/consultations/i-vsd-paid-event-payments-consultation.md`
- `dev/active/registration-data-collection/registration-data-collection-plan.md` D29-D30
- ADR-022: Paid Event Commerce And Stripe Connect
- ADR-023: Admission Credential, Check-In, Transfer, And Recovery
