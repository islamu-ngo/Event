<!-- ABOUTME: I-VSD consultation for Stripe Connect paid events, organizer funds, refunds, payout timing, and multi-currency. -->
<!-- ABOUTME: Defines safe self-hosted defaults, ISLAMU hosted safeguards, clean-room evidence, and required legal and scholarly escalation. -->

# I-VSD Paid Event Payments Consultation: Stripe Connect, Refunds, Payouts, and Multi-Currency

Last Updated: 2026-08-13

## Scope

This report evaluates the future paid-event payment design for ISLAMU Event. It covers:

- Stripe Connect as the first payment provider.
- Organizer-owned payment accounts and the prohibition on redirecting ticket proceeds to an instance or tenant administrator.
- Configurable eligibility for organizations, groups, and individual organizers.
- Refunds, event cancellation, disputes, and delayed payouts.
- Multi-currency configuration for local and global deployments.
- Fraud, scam, malicious-operator, and hostile-fork risks in self-hosted deployments.
- The differences between the official ISLAMU-hosted service and unrelated self-hosted instances.
- The minimum domain, integration, reconciliation, and disclosure boundaries required by a later payment ADR.

This is a design consultation, not an implementation. The current registration domain intentionally stops a positive order at `AwaitingPayment`; payment provider, tax, refund, dispute, and payout behavior requires a separate approved ADR before code is added.

## Claim Boundary

This report provides I-VSD design reasoning, technical research, and product-risk analysis. It is not a fatwa, Sharia certification, legal opinion, accounting opinion, payment-institution determination, PCI certification, or guarantee that Stripe will approve a particular Connect configuration or country corridor. Stripe capabilities, payment methods, account availability, settlement currencies, and contractual responsibilities must be confirmed for the platform account and connected-account country at implementation time.

## Findings

| # | Finding | I-VSD principle | Domain | Severity |
|---|---|---|---|---|
| 1 | The safest general-purpose flow is a Stripe Connect **direct charge on the organizer's connected merchant account**, with Stripe collecting its processing fees and taking connected-account negative-balance responsibility where Stripe approves the configuration. | Trust (`Amanah`), non-harm (`Lā Darar`) | Strategic, technical | Critical |
| 2 | Destination charges and separate charges and transfers place refunds, disputes, fees, and negative platform balances on the platform. They are unsuitable as the default for an openly self-hostable event product. | Justice (`'Adl`), responsible stewardship | Financial, operational | Critical |
| 3 | Stripe-managed loss risk and strict platform payout control are competing choices. When Stripe is the connected account's losses collector, Stripe says the platform cannot pause that account's payments or payouts. | Truthfulness (`Sidq`), trust | Financial, technical | Critical |
| 4 | Stripe manual payouts are **not escrow**. Outside the United States and Thailand, Stripe currently requires payout within 90 days; a connected account with Dashboard control can also retain some manual-payout capability. | Truthfulness, avoidance of excessive uncertainty (`Gharar`) | Legal, UX, technical | Critical |
| 5 | A public event's end time is not a reliable financial release trigger. Events can be multi-day, online, open-ended, or prayer-relative, so paid publication needs a separate, explicit settlement-release timestamp or milestone. | Truthfulness, non-harm | Domain, technical | High |
| 6 | A blanket “no refunds” term is neither adequate user protection nor a reliable legal shield. It cannot remove card disputes or mandatory consumer rights, and it is unjust for organizer cancellation, non-delivery, duplicate charging, or material changes. | Justice, mercy (`Rahmah`), trust | Legal, product | Critical |
| 7 | Currency must be explicit and immutable per published ticket catalog and order. Event location can suggest a currency, but must not silently choose it. | Truthfulness, avoidance of uncertainty | Domain, UX | High |
| 8 | “Dirham” is ambiguous: `MAD` is the Moroccan dirham and `AED` is the UAE dirham; Saudi Arabia uses the `SAR` riyal. UI and storage must use exact ISO currency codes and names. | Truthfulness, excellence (`Ihsan`) | UX, domain | High |
| 9 | Stripe identity verification reduces payment-account risk but does not prove that an advertised event is real. ISLAMU still needs organizer eligibility, evidence, sales limits, complaints, cancellation, and incident controls. | Non-harm, stewardship | Governance, operations | Critical |
| 10 | No upstream implementation can force a malicious self-hoster to keep the intended recipient, disclosures, or refund behavior after modifying the source. ISLAMU must never share its Stripe platform credentials with third-party instances or imply that unrelated instances are ISLAMU-protected. | Truthfulness, trust | Ecosystem, security | Critical |
| 11 | The paid-event recipient must be the organizer actor that holds commercial authority for the event, not whichever user has an instance or tenant role. Recipient changes after sales must never reroute existing payments. | Justice, trust | Authorization, domain | Critical |
| 12 | Refund and cancellation processing is an asynchronous, idempotent reconciliation workflow. “Requested” must never be displayed as “refunded” until provider evidence confirms completion. | Truthfulness, excellence | Technical, operational | High |

## Recommendations

### Decisive Product Direction

Adopt two clearly separated operating profiles:

| Profile | Intended operator | Charge and liability model | Payout promise | Recommendation |
|---|---|---|---|---|
| `OrganizerDirect` | Every self-hosted instance and ISLAMU phase 1 | Direct charge on the organizer connected account; request Stripe as fees and losses collector; Stripe-hosted onboarding and Dashboard or required embedded components | Normal Stripe payouts; the platform does **not** promise funds are held until the event | Default and first implementation |
| `ProtectedDelayedPayout` | ISLAMU-hosted service, or another professionally operated deployment with its own legal and risk program | Direct charge on the organizer account, but the operator accepts the Stripe configuration needed to control payouts and the resulting negative-balance/operational responsibility | Explicit release milestone within Stripe and legal holding limits; never described as escrow | Future, separately approved ADR and operational product |

Do not offer a generic “collect into the tenant/instance admin account” mode. Do not make a fallback recipient configurable. If the organizer has no eligible connected account, paid publication is unavailable.

The wording “all money goes to the organizer” also needs precision. With direct charges, the ticket payment is created on the organizer's Stripe account, but Stripe can deduct processing fees, refunds, disputes, reserves, taxes, and other valid adjustments. Any optional platform fee or voluntary platform contribution must be a separately disclosed line item; it must never be hidden inside organizer proceeds or used to change the merchant recipient.

### Configuration Hierarchy

Maximum configurability should mean safe narrowing, not arbitrary weakening:

| Level | May configure | Must not configure |
|---|---|---|
| Hard product invariant | Organizer-recipient binding, immutable charge currency, no fallback administrator account, verified provider webhooks, truthful refund state, historical recipient pinning | No operator can redirect an existing order to a new recipient through normal administration |
| Instance | Payments enabled, allowed provider/profile, allowed organizer kinds, minimum local verification, maximum currency set, default currency, sales/risk ceilings, minimum refund protections, permitted release policies | Cannot call manual payout “escrow”; cannot declare mandatory rights or card disputes waived |
| Tenant | Disable payments, choose a subset of instance currencies and organizer kinds, require stricter verification, choose a stricter refund policy, reduce ticket/sales ceilings | Cannot broaden the instance's provider, currency, organizer, or risk permissions |
| Organizer/event | Connect its own Stripe merchant account, choose one permitted event currency, set prices, choose a refund policy above the minimum floor, provide support and settlement details | Cannot nominate an unrelated recipient, weaken the cancellation floor, or change the currency/recipient for existing orders |

For ISLAMU's hosted instance, start with:

- Paid events enabled only for locally verified organizations.
- Stripe-hosted merchant onboarding and current account requirements satisfied.
- `OrganizerDirect` only.
- A conservative first-event and high-value review policy.
- No paid publication until the exact organizer actor has commercial authority and an eligible connected account.

Self-hosters may enable paid events for users or groups, but the upstream default should remain organization-only. Enabling individuals is an explicit operator risk choice, not a hidden convenience setting.

### Multi-Currency Policy

Use an effective allowed-currency intersection:

`instance allowed currencies ∩ tenant allowed currencies ∩ provider/account support`

Then require the organizer to confirm exactly one currency before publishing the paid ticket catalog.

Recommended policies:

| Deployment | Allowed set | Selection behavior |
|---|---|---|
| Belgium-only instance | `{ EUR }` | Locked to EUR; no currency control shown to the organizer |
| Global ISLAMU instance | Operator-approved ISO set, initially ordered `EUR`, `USD`, `MAD`, `SAR`, `AED` in the UI | Organizer explicitly chooses from the effective set |
| Online event | Same effective set | Organizer explicitly chooses; no address inference |
| In-person global event | Same effective set | Address may preselect a suggestion, but the organizer must confirm it |

Do not make venue address authoritative. The venue may differ from the merchant's business country, the intended audience, settlement account, tax obligations, or pricing strategy. Some countries and regions commonly transact in more than one currency. Silent inference would create price uncertainty and costly correction.

Use exact labels:

- `EUR` — euro.
- `USD` — United States dollar.
- `MAD` — Moroccan dirham.
- `SAR` — Saudi riyal, not “Saudi dirham.”
- `AED` — United Arab Emirates dirham.

The existing domain already has the correct foundation: ISO currency metadata, currency-specific minor-unit handling, one currency per ticket catalog version, one currency per order, and no internal foreign-exchange calculation. Preserve that boundary.

At paid publication and again at checkout, verify that Stripe supports the chosen presentment currency for the connected account, country, requested payment methods, and current capabilities. Stripe lists EUR, USD, MAD, SAR, and AED as presentment currencies, but this does not mean every connected-account country, settlement bank, payment method, or cross-border payout corridor supports every combination. Provider capability checks are authoritative; a static list is only the instance's policy ceiling.

Do not enable Stripe Adaptive Pricing in phase 1. It can present or charge a buyer in a currency different from the event's stored order currency. That conflicts with the current immutable money snapshot unless a future ADR explicitly models provider-presentment amount, exchange rate, settlement amount, refunds, receipts, and reconciliation.

### Organizer Account and Commercial Authority

Bind the connected account to an organizer actor, not to the login session or an administrator role. The actor may be an organization, group, or individual only when policy permits that actor type.

Paid publication must verify all of the following:

1. The event has an organizer actor with commercial authority under ADR-017.
2. The actor type is allowed by the effective instance and tenant policy.
3. Required local verification is complete.
4. The actor owns an active Stripe connected-account link created through this instance's Connect platform.
5. Stripe reports the necessary charges capability and no blocking requirements.
6. The event currency is allowed and compatible with that account.
7. The refund policy, support contact, merchant identity, settlement milestone, and buyer disclosures are complete.

Snapshot the organizer actor ID, Stripe connected-account ID, merchant country, currency, charge profile, and policy versions when the paid catalog is published and on every payment attempt. A later organizer claim or account replacement must not rewrite historical recipients. If the merchant must change after sales begin, stop new sales and use a governed refund-and-rebook process.

### Stripe Charge Model

Use Stripe-hosted Checkout with direct charges created in the connected-account context. This keeps card data off ISLAMU servers, supports Stripe's authentication flows, places the charge on the organizer account, and lets Stripe display connected-merchant branding and statement information.

For the default profile, request the current controller/Accounts configuration equivalent of:

- Stripe collects payment-processing fees.
- Stripe is the connected account's losses collector.
- Stripe collects changing KYC/KYB requirements.
- The connected merchant receives full Stripe Dashboard access, or the platform supplies every embedded component Stripe requires for Managed Risk.
- Direct charges use Radar as required by Stripe Managed Risk.

Do not design around legacy `Standard`, `Express`, and `Custom` names alone. Stripe's current documentation recommends controller-property/Accounts v2 responsibilities; those responsibilities are materially important and can be immutable after merchant configuration.

Reject destination charges and separate charges and transfers for the core self-hosted profile. For those indirect charge types, Stripe debits refunds and disputes from the platform account, and transfer reversal/recovery becomes the platform's problem. That turns an event publishing platform into the financial risk bearer the design is trying to avoid.

### Refund Policy

Do not remove refunds globally. Establish an immutable minimum protection floor, then allow organizers to be more generous.

| Situation | Minimum platform behavior |
|---|---|
| Organizer or authorized operator cancels the event before delivery | Full automatic refund of the attendee's event-related payment; processing-cost allocation is between organizer/operator and Stripe, not silently deducted from the attendee |
| Event is rescheduled or materially changed | Notify the buyer and offer acceptance of the new terms or a full refund |
| Duplicate or incorrect charge | Full refund of the erroneous amount |
| Event is not delivered substantially as sold | Full or proportionate refund according to the evidenced failure and applicable law |
| Attendee changes their mind | Organizer policy may be non-refundable, partially refundable, or refundable until a disclosed deadline, subject to mandatory law |
| Buyer raises a card dispute | Follow Stripe/card-network dispute rules; terms cannot waive this mechanism |
| Platform contribution or fee tied to a cancelled event | Default to refunding it with the event payment unless a legally reviewed, genuinely separate voluntary contribution agreement says otherwise |

EU consumer rules contain an exception to the ordinary 14-day withdrawal right for leisure services on a specific date or period, including qualifying event tickets. That exception does not mean “the organizer may cancel and keep the money,” and it does not displace national contract law, unfair-terms controls, non-delivery remedies, or card disputes. Belgium's official ConsumerConnect guidance states that when an artist cancels a concert, the consumer is entitled to reimbursement or a new date.

Display and snapshot before payment:

- Merchant/organizer legal or public business identity.
- Instance operator identity and whether it is ISLAMU-hosted or independent.
- Event identity, date/milestone, and delivery format.
- Exact charge currency, total, taxes, fees, and voluntary contribution.
- Refund deadlines and cancellation/material-change rules.
- Support and complaint contacts.
- Statement descriptor and payment provider.

Terms of service are not the refund engine. A self-hoster may edit terms, but runtime policy must still implement the configured protection floor and preserve the exact policy accepted with each order.

### Cancellation and Refund Processing

Cancellation is a business workflow, not a delete operation:

1. An authorized organizer or trust-and-safety actor requests cancellation.
2. The event enters a non-selling cancellation state immediately.
3. A transactional outbox records one refund job per captured payment.
4. The refund worker creates an idempotent Stripe refund in the connected-account context.
5. Provider webhooks and reconciliation move each `RefundAttempt` through requested, pending, succeeded, failed, or requires-action states.
6. Buyers receive outcome notifications and can see unresolved refunds.
7. The event is only financially closed when all attempts have a terminal, reconciled outcome or a documented operator case.

With direct charges, a refund debits the connected account. If its available balance is insufficient, Stripe can leave the refund pending until funds become available. Therefore, the UI must never say “refunded” merely because the API request was accepted.

Do not run provider calls inside the event-cancellation database transaction. Persist intent and idempotency first, call Stripe outside the transaction, and reconcile ambiguous timeouts. This follows the repository's existing deferred payment design.

### Payout Timing and Event Completion

Do not promise “money is held until midnight after the event.” That rule fails for:

- Multi-day events.
- Events ending after midnight.
- Online events crossing time zones.
- Open-ended or prayer-relative events.
- Events sold more than Stripe's maximum manual-payout holding period before delivery.
- Events with a postponed or disputed completion.

Instead, if the future protected profile is approved, introduce a separate `SettlementReleaseAt` or equivalent financial milestone. It is distinct from the public schedule and is snapshotted before sales. A fixed event can suggest the last scheduled session plus a configurable review buffer; an open-ended, flexible, or online event must provide an explicit release timestamp that policy validates.

Release only when:

- The milestone has passed.
- The event is not cancelled, suspended, materially disputed, or under trust-and-safety review.
- Required refund jobs are resolved.
- The connected account remains payout-eligible.
- The release remains within Stripe's country-specific holding limit.

Stripe states that manual payouts are not escrow and currently limits holding to 10 days for Thailand, two years for the United States, and 90 days for other countries. Long-advance ticket sales cannot rely on a post-event payout lock outside those limits.

More importantly, Stripe states that when Stripe is responsible for a connected account's negative balance, the platform cannot pause its payments or payouts. Stripe also documents that Dashboard-connected accounts can retain a manual-payout path even when the platform controls the schedule. A strict organizer lock therefore requires a different Connect responsibility and Dashboard configuration, Stripe approval, legal review, monitoring, reserves, and acceptance that the operator may become responsible for unrecoverable losses. This is why `ProtectedDelayedPayout` must be a separate product and governance decision, not a tenant toggle.

For phase 1, use normal organizer payouts and be honest about the protection boundary. Cancellation still initiates refunds; Stripe can recover negative connected balances and handle disputes according to the account configuration, but ISLAMU must not claim that ticket funds remained untouched until delivery.

### Fraud and Scam Controls

Delegate what Stripe is designed to do:

- Hosted or embedded KYC/KYB onboarding and changing verification requirements.
- Card data handling, Strong Customer Authentication, and payment-method authentication.
- Radar transaction screening.
- Sanctions, prohibited-business, account-risk, and negative-balance interventions within the selected Managed Risk configuration.
- Card disputes, connected-account balance recovery, and provider receipts.

Retain what only the event platform can know:

- Whether the organizer has genuine commercial authority for the event.
- Whether a venue, speaker, schedule, or online delivery claim is plausible.
- Whether event edits constitute a material change.
- Whether sales velocity, price, capacity, or lead time is abnormal for this organizer.
- Whether attendees report non-delivery or misleading content.

Recommended platform controls:

- Organization-only paid-event default and local verification tier.
- Stripe capability checks on every paid publish and checkout.
- Review of an organizer's first paid event and high-value/far-future events.
- Per-event and rolling sales ceilings that can grow with successful history.
- Immutable audit of merchant, recipient, currency, prices, refund terms, and material edits.
- Immediate stop-sale on cancellation, merchant verification loss, or credible fraud review.
- Buyer notifications for recipient-neutral event changes, cancellation, and refund status.
- A visible report/complaint route with defined response ownership.
- Minimal data collection: use provider risk outcomes and event evidence rather than duplicating identity dossiers.

Stripe verification is necessary but not sufficient. It verifies a merchant/payment account under Stripe's rules; it does not certify the truth of every future event listing.

### Self-Hosted Trust Boundary

Each deployment must register and operate its own Stripe Connect platform. Third-party self-hosters must never receive ISLAMU's secret keys, webhook secret, platform account, connected-account records, or ISLAMU-hosted payment routes.

At checkout, disclose:

- The independent instance operator.
- The connected event organizer/merchant.
- Whether the instance is operated by ISLAMU.
- Which party handles event support and which party processes the payment.
- A warning that unrelated self-hosted instances are not verified or financially guaranteed by ISLAMU.

A malicious operator controls its deployed source, database, DNS, UI, and Stripe account. It can remove upstream safeguards or advertise fake events. Open-source code cannot make that operator trustworthy. The upstream project can provide safe defaults, signed releases, audit logs, tests, and clear branding boundaries, but the actual payment relationship remains the one shown by that deployment and Stripe.

Federation must not transfer payment trust. A federated event may be discoverable elsewhere, but checkout should open the event's origin and clearly restate the origin operator and merchant. An ISLAMU badge must only identify an ISLAMU-operated origin, not software lineage.

### Minimal Future Architecture

The later payment ADR should add only the boundaries demanded by the first Stripe implementation:

- A provider connection owned by the organizer actor.
- A `PaymentAttempt` aggregate recording order, merchant snapshot, provider identity, idempotency key, amount, currency, and state before external handoff.
- A `RefundAttempt` tied to the original payment, with its own amount, reason, idempotency, and provider state.
- A webhook inbox/ledger that verifies signatures, deduplicates provider event IDs, and preserves auditable state changes.
- Transactional outbox messages for checkout handoff, cancellation refunds, notifications, and reconciliation.
- A dispute projection for organizer/operator action without pretending local state supersedes Stripe.
- A settlement-release record only if the protected profile is approved.
- HAL action relations for connect, publish-paid, cancel, refund, retry, and reconcile; clients must not reproduce role or payment-policy logic.

Keep the domain vocabulary provider-neutral, but implement only a Stripe adapter in phase 1. Do not add a provider factory or speculative lowest-common-denominator API until a second provider is actually selected.

Critical flows:

| Flow | Required behavior |
|---|---|
| Connect | Create Stripe-hosted onboarding link; return URL is not proof of completion; retrieve account state and process account webhooks |
| Checkout | Persist attempt and recipient/currency snapshots; create connected-account Checkout session outside the transaction; reconcile timeout |
| Webhook | Verify signature; deduplicate; map provider event to known attempt; apply monotonic state transition; acknowledge safely |
| Cancel | Stop sales in the same local transaction; outbox idempotent refunds; notify and reconcile each result |
| Refund | Use original connected-account context; never reroute; expose pending/failed honestly |
| Account change | Affect only new catalogs/payments; historical charges remain pinned |
| Reconciliation | Scheduled comparison of non-terminal local attempts with Stripe; alert on orphan or contradictory records |

### Common Overlooked Failures and Outcomes

| Failure | Likely outcome | Required control |
|---|---|---|
| Organizer completes the Stripe return URL but onboarding is incomplete | Paid event appears enabled without charge capability | Treat return as navigation only; verify account requirements and capability |
| Checkout creation times out after Stripe accepted it | Duplicate charges or an order stuck unpaid | Persist idempotency first and reconcile before retry |
| Event is deleted instead of cancelled | Buyers receive no refund | Block destructive deletion once paid orders exist; route through cancellation |
| Organizer changes connected account after sales | Funds or refunds target the wrong merchant | Pin recipient per catalog/payment; never rewrite history |
| Connected balance is empty during cancellation | Refund remains pending | Show pending, reconcile, notify, and escalate instead of declaring success |
| Payment succeeds but webhook is delayed or duplicated | Capacity and payment state diverge | Idempotent inbox plus scheduled reconciliation |
| Tenant broadens instance currency or organizer policy | Local governance bypass | Tenant settings may only narrow the instance ceiling |
| Venue address silently sets currency | Wrong price and irreversible paid orders | Suggest only; require explicit organizer confirmation |
| Event is sold months in advance under “held until event” promise | Manual-payout limit is exceeded | Reject protected profile for that sales window or use normal payouts with truthful disclosure |
| Stripe approves the merchant but the event is fake | Buyers still suffer non-delivery | Local verification, risk caps, complaints, stop-sale, cancellation/refund, disputes |
| Malicious self-hoster copies ISLAMU branding | Buyers assume ISLAMU protection | Origin/operator disclosure and protected official-instance identity |
| Organizer cancels after payout | Refund creates negative/pending balance | Direct-charge recovery and Stripe liability rules; do not promise pre-funded refund |
| Public event end is open-ended | Settlement job never releases or releases arbitrarily | Separate explicit settlement milestone |
| Payment provider is temporarily unavailable | Organizer retries and creates duplicates | Durable attempt state, idempotency, retry backoff, reconciliation |

## Stakeholders

| Stakeholder | Legitimate interest | Risk carried |
|---|---|---|
| Attendee/buyer | Truthful merchant, price, currency, delivery, refund, and complaint information | Fraud, non-delivery, currency surprise, delayed refund |
| Organizer merchant | Direct receipt of proceeds, predictable fees, fair disputes, privacy, and payout visibility | Negative balance, refund/dispute liability, account restriction |
| Event contributor | Accurate attribution without accidental commercial authority | Being treated as merchant without consent |
| Organization/group | Controlled delegation and verified account ownership | Insider misuse or unauthorized payout account |
| Tenant administrator | Ability to narrow risk and protect its community | Pressure to act as merchant or custodian without authority |
| Instance operator | Safe configuration, incident tools, and accurate legal boundary | Platform loss, regulatory exposure, reputational harm |
| ISLAMU nonprofit | Trustworthy hosted service without implying control over unrelated deployments | Brand abuse, community harm, excessive financial power |
| Stripe and financial partners | Compliant merchants and truthful transactions | Fraud, prohibited activity, unrecoverable balances |
| Regulators, scholars, and consumer bodies | Lawful, fair, and ethically coherent operation | Misclassification, unfair terms, financial harm |

## I-VSD Principles and Domains

| Principle | Design consequence | Domains |
|---|---|---|
| Trust (`Amanah`) | Bind money to the actual organizer merchant; preserve evidence and refund status | Strategic, technical, financial |
| Justice (`'Adl`) | Do not shift organizer cancellation loss to attendees or silently to unrelated administrators | Product, financial, legal |
| Truthfulness (`Sidq`) | Name the merchant, currency, fees, self-hosted operator, payout boundary, and non-escrow status | UX, legal, ecosystem |
| Non-harm (`Lā Darar`) | Default to verified organizations, direct charges, risk caps, complaints, and cancellation refunds | Governance, operational |
| Mercy (`Rahmah`) | Provide fair remedies for cancellation, rescheduling, non-delivery, and genuine hardship | Product, support |
| Ease (`Taysir`) | Delegate identity and payment handling to Stripe; hide unavailable choices; use hosted checkout | UX, technical |
| Excellence (`Ihsan`) | Use idempotency, outbox, webhook verification, reconciliation, and immutable money snapshots | Technical, operational |
| Privacy | Avoid collecting bank details or duplicating Stripe identity dossiers | Security, governance |
| Avoidance of `Gharar` | Fix merchant, amount, currency, refund policy, and settlement milestone before sale | Domain, UX, financial |
| Avoidance of `Riba` | Do not make claims about permissibility of reserves, delayed balances, credit, or fees without scholarly review | Financial, religious-legal |

## Validation Gaps

Before implementation, validate these assumptions with live Stripe test accounts and contractual support:

1. The ISLAMU platform account country and supported connected-account countries.
2. Availability of Stripe-loss-liability direct charges for each intended organizer country.
3. Exact controller/Accounts v2 configuration and whether Stripe will approve Managed Risk.
4. Presentment, settlement, bank payout, and payment-method support for EUR, USD, MAD, SAR, and AED by organizer country.
5. Whether any desired application fee changes Stripe fee allocation or Managed Risk eligibility.
6. Refund behavior, fee treatment, negative-balance recovery, and dispute ownership under the actual contract.
7. Dashboard and platform-control behavior for any proposed delayed-payout profile.
8. Stripe's permitted manual-payout holding period and cross-border payout corridor for each country.
9. Tax, invoice, VAT, charity/nonprofit, and platform reporting obligations for the organizer and operator.
10. Consumer-law requirements for the markets where events are offered, not only where the server is hosted.

Test scenarios must include successful payment, SCA challenge, webhook duplication, webhook delay, ambiguous timeout, insufficient-balance refund, partial refund, cancellation batch restart, dispute, connected-account restriction, recipient change attempt, unsupported currency, open-ended event settlement, and sales beyond the manual-payout holding window.

## Escalation Needed

### Legal and Regulatory

Obtain Belgian/EU payments counsel before ISLAMU launches paid events, and again before any protected delayed-payout profile. Counsel should determine:

- Whether the direct-charge structure keeps each organizer as merchant of record and avoids ISLAMU providing a regulated payment service.
- Whether payout control, reserves, platform fees, donations, or refund guarantees change that conclusion under PSD2 and Belgian National Bank supervision.
- Required organizer, consumer, distance-selling, unfair-terms, tax, invoice, AML, privacy, and complaint disclosures.
- The legal treatment of global sales and non-EU connected merchants.

### Islamic Finance and Scholarly Review

Seek a qualified Islamic finance scholar or board before production. Review:

- Stripe processing fees, platform fees, optional contributions, reserves, negative balances, and dispute fees.
- Delayed payout and whether any balance treatment, credit, or contractual benefit creates a `Riba` concern.
- Refund fairness, cancellation losses, uncertainty, and the permissibility of specific organizer/attendee terms.
- Whether ISLAMU's nonprofit role creates additional stewardship or charitable-fund restrictions.

The design should present facts and contracts to scholars; it must not encode a claim of Sharia compliance by itself.

### Stripe and Operations

Ask Stripe to review the planned Connect configuration, country rollout, manual-payout requirements, Radar for direct charges, and incident responsibilities. ISLAMU must also assign real human ownership for merchant review, complaints, event cancellation, refund exceptions, disputes, reconciliation alerts, and account restrictions before enabling production payments.

## Evidence Reviewed

### Repository Evidence

- `docs/adr/ADR-017-event-participation-authority-model.md` — separates provenance, publishing, organizer, and commercial authority.
- `docs/adr/ADR-018-registration-order-ticketing-aggregate.md` — defines immutable ticket/order money snapshots and the deliberate `AwaitingPayment` boundary.
- `dev/active/registration-data-collection/deferred-design-records.md` — requires provider identity, idempotency before handoff, external calls outside transactions, reconciliation, and independent refund snapshots.
- `src/Explore.Domain/RegistrationOrder.cs` — stores one currency and snapshotted organizer, platform, contribution, and total amounts.
- `src/Explore.Domain/EventTicketCatalogVersion.cs` — keeps one currency across a published catalog version.
- `src/Explore.Domain/EventTicketType.cs` — uses integer minor-unit prices.
- `src/Explore.Domain/ValueObjects/CurrencyMetadata.cs` — supplies ISO currencies and exponent-aware minor units, including EUR, USD, MAD, SAR, and AED.
- `src/Explore.Domain/PlatformFeePolicy.cs` — default-off, instance-scoped platform fee policy.
- `src/Explore.Domain/PlatformContributionSetting.cs` — separate, default-off platform contribution setting.
- `islamic-value-sensitive-design/consultations/i-vsd-compliance-check.md` — identifies refund, cancellation, financial, legal, and scholarly gaps.
- `islamic-value-sensitive-design/consultations/i-vsd-flexible-event-end-times.md` — confirms that public event endings may be open-ended or contextual and therefore cannot double as payout milestones.

### External Functional Evidence

Only official public product, legal, regulatory, and consumer guidance was used. No third-party implementation source, snippets, schemas, migrations, tests, or assets were inspected or imported.

| Source | Fact used | Accessed |
|---|---|---|
| [Stripe: Risk and liability management with Connect](https://docs.stripe.com/connect/risk-management) | Direct charges affect connected balances; indirect charges affect the platform; Stripe-loss liability limits platform payout/payment controls | 2026-08-13 |
| [Stripe: Managed Risk](https://docs.stripe.com/connect/risk-management/managed-risk) | Managed Risk requirements include direct charges, Radar, hosted/embedded onboarding, full service agreement, and required account UX | 2026-08-13 |
| [Stripe: Recommended Connect integrations](https://docs.stripe.com/connect/integration-recommendations) | Stripe recommends direct charges and Stripe negative-balance responsibility for SaaS-style platforms | 2026-08-13 |
| [Stripe: Connect charges](https://docs.stripe.com/connect/charges) | Charge type determines whose balance receives funds and bears refunds/chargebacks | 2026-08-13 |
| [Stripe: Direct charges](https://docs.stripe.com/connect/direct-charges) | A direct charge is created on the connected account | 2026-08-13 |
| [Stripe: Connected-account configuration](https://docs.stripe.com/connect/accounts-v2/connected-account-configuration) | Current fees/losses responsibilities and Dashboard settings govern account behavior and can be immutable | 2026-08-13 |
| [Stripe: Manual payouts](https://docs.stripe.com/connect/manual-payouts) | Manual payouts are not escrow and have country-specific holding limits | 2026-08-13 |
| [Stripe: Platform controls for Dashboard accounts](https://docs.stripe.com/connect/platform-controls-for-stripe-dashboard-accounts) | Platform scheduling controls do not necessarily remove connected-account manual payouts | 2026-08-13 |
| [Stripe: Place a hold on a payment method](https://docs.stripe.com/payments/place-a-hold-on-a-payment-method) | Card authorization/capture windows are short and unsuitable for events months away | 2026-08-13 |
| [Stripe: Refunds](https://docs.stripe.com/refunds) | Refunds can be pending when the relevant balance lacks funds | 2026-08-13 |
| [Stripe: Connect disputes](https://docs.stripe.com/connect/disputes) | Charge type and loss responsibility determine dispute debit and handling | 2026-08-13 |
| [Stripe: Hosted onboarding](https://docs.stripe.com/connect/hosted-onboarding) | Stripe dynamically collects verification requirements; return URL is not proof of completed onboarding | 2026-08-13 |
| [Stripe: Radar for Connect](https://docs.stripe.com/connect/radar) | Direct-charge Radar behavior belongs to the account creating the charge unless Radar for Platforms is used | 2026-08-13 |
| [Stripe: Checkout](https://docs.stripe.com/payments/checkout) | Hosted Checkout keeps payment collection in Stripe's hosted surface | 2026-08-13 |
| [Stripe: Security integration guide](https://docs.stripe.com/security/guide) | Hosted/low-risk integrations reduce direct card-data handling | 2026-08-13 |
| [Stripe: Supported currencies](https://docs.stripe.com/currencies) | EUR, USD, MAD, SAR, and AED are listed presentment currencies; settlement and payment-method support vary | 2026-08-13 |
| [Stripe: Cross-border payouts](https://docs.stripe.com/connect/cross-border-payouts) | Cross-border availability is region- and platform-dependent | 2026-08-13 |
| [Stripe Connected Account Agreement](https://stripe.com/legal/connect-account) | The connected user remains responsible for its goods/services and customer obligations | 2026-08-13 |
| [EU Consumer Rights Directive 2011/83/EU](https://eur-lex.europa.eu/legal-content/EN/ALL/?uri=CELEX:32011L0083) | Article 16(l) contains the specific-date leisure-service withdrawal exception | 2026-08-13 |
| [CJEU case C-96/21, CTS Eventim](https://eur-lex.europa.eu/legal-content/EN/ALL/?uri=CELEX:62021CJ0096) | Explains application of the event-ticket withdrawal exception to an intermediary | 2026-08-13 |
| [Your Europe: returns and cooling-off](https://europa.eu/youreurope/citizens/consumers/shopping/returns/index_en.htm) | Specific-date concert tickets are an example of an online cooling-off exception | 2026-08-13 |
| [European Commission: event cancellation FAQ](https://commission.europa.eu/live-work-travel-eu/consumer-rights-and-complaints/resolve-your-consumer-complaint/european-consumer-centres-network-ecc-net/faq-cancellations-individually-booked-accommodations-car-rental-and-events-due-covid-19_en) | Cancellation remedies depend on contract and national law; unfair terms remain controlled | 2026-08-13 |
| [Belgian ConsumerConnect: artist cancels a concert](https://consumerconnect.be/fr/themas/reizen-en-evenementen/evenementen/evenementen/l-artiste-annule-son-concert) | Belgian consumer guidance identifies reimbursement or a new date as remedies | 2026-08-13 |
| [National Bank of Belgium: payment institutions](https://www.nbb.be/en/financial-oversight/prudential-supervision/areas-responsibility/payment-institutions-and-electroni-3) | Belgian payment services operate under PSD2 supervision | 2026-08-13 |
| [PSD2, Directive (EU) 2015/2366](https://eur-lex.europa.eu/legal-content/EN/TXT/HTML/?uri=CELEX:32015L2366) | EU payment-services framework requiring legal classification review | 2026-08-13 |

Clean-room attestation: external research was reduced to factual functional constraints and independently mapped to the existing ISLAMU architecture. No external source code, copied implementation sequence, dependency, or restricted artifact was introduced. Tavily's deep-research endpoint returned a quota/usage error during this consultation; official-source web research and Tavily official-source search results were used instead.

## Missing Evidence

- An executed Stripe Connect agreement and approved platform-country configuration for ISLAMU.
- Stripe confirmation of the precise Accounts v2/controller properties available to this nonprofit and its intended connected countries.
- Live test-account evidence for Moroccan, Saudi, UAE, EU, and US merchant/currency combinations.
- Legal opinions on merchant-of-record status, PSD2/payment-service scope, VAT/tax, invoices, donations, and global consumer law.
- Scholarly review of fee, reserve, delayed-payout, negative-balance, and refund structures.
- A production incident, support, dispute, reconciliation, reserve, and complaint operating model.
- Evidence that the protected delayed-payout profile can be offered within Stripe contractual controls without creating unacceptable operator liability.

These gaps block production launch and any claim of legal, financial, or Sharia compliance. They do not block an ADR or a Stripe test-mode prototype of `OrganizerDirect`.

## Context Inventory

| Context dimension | Consultation assumption |
|---|---|
| Provider role | ISLAMU Event mediates organizer onboarding, checkout initiation, cancellation/refund workflows, and truthful status; Stripe processes payments and provider risk functions |
| Default deployment | Independently self-hostable; each operator supplies its own Stripe Connect platform |
| Official hosted service | ISLAMU nonprofit operates a globally discoverable Islamic event instance with stricter paid-organizer eligibility |
| Paid organizer | Organization by default; group or individual only when effective policy explicitly permits and verification succeeds |
| Recipient invariant | The event's snapshotted organizer merchant; never a tenant/instance administrator fallback |
| Buyer group | Local and international event attendees, including potentially vulnerable or low-recourse buyers |
| Data sensitivity | Identity/account status, payment metadata, disputes, support evidence; bank/card/KYC data should remain with Stripe |
| Money model | Integer minor units, one immutable order currency, no internal FX |
| Geographic scope | Belgium-first operator with global-event ambition; cross-border availability remains provider- and law-dependent |
| Public event scheduling | May be fixed, multi-day, open-ended, or contextual; separate from settlement timing |
| Intended rollout | Stripe test mode → ISLAMU organization-only `OrganizerDirect` pilot → evidence review → broader organizers/countries → separately approved protected profile if justified |
