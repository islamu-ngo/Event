<!-- ABOUTME: I-VSD consultation on merchant-of-record identity, instance-operator Stripe credentials, tenant-secret prohibition, and attendee-facing payment disclosure. -->
<!-- ABOUTME: Answers who supplies Stripe secrets, whose identity appears on financial documents, and the full fail-closed Stripe setup inventory. -->

# I-VSD Payment Identity and Stripe Secrets Consultation: Merchant-of-Record, Operator Credentials, and Attendee-Facing Disclosure

Last Updated: 2026-09-15

## Review Metadata

| Field | Value |
|---|---|
| Mode | Standalone consultation |
| Subject | Merchant-of-record identity on financial documents vs. platform disclosure identity; per-tenant vs. instance-operator Stripe secret ownership in `OrganizerDirect` zero-custody payments |
| Workstream | None (post-implementation advisory on shipped payment/identity code) |
| Report kind | Consultation |
| Report status | Current |
| Disposition | Advisory |
| Evidence cutoff | 2026-09-15 |
| Reviewed input | Working tree |
| Supersedes | None (complements `i-vsd-paid-event-payments-consultation.md` and `i-vsd-branding-legal-identity-authority.md`) |

## Scope

This report answers a steward consultation on the shipped paid-events implementation (`OrganizerDirect`, ADR-022). It covers:

- Whether instance-operator identity makes sense in single-tenant and multi-tenant deployments, given that payment processing runs on the instance operator's Stripe secrets.
- Whether multi-tenant instances should allow each tenant to supply its own Stripe secrets.
- Whose identity the attendee sees on financial documents (Stripe receipts, card statement descriptor) and whether instance-operator branding there would confuse attendees or cause erroneous chargebacks.
- Whether invoices should carry dual identity (operator as legally responsible party + tenant to avoid confusion).
- The complete, ordered inventory of Stripe secrets and configuration an instance operator must supply to activate paid ticketing.

Out of scope: changing the implemented `OrganizerDirect` architecture, payout timing policy (deferred under ADR-024 `ProtectedDelayedPayout`), and any jurisdiction-specific legal determination.

## Claim Boundary

This report provides I-VSD design reasoning grounded in repository code and documents. It is not a fatwa, Sharia certification, legal opinion, accounting or tax opinion, merchant-of-record determination, or payment-institution/financial-facilitation ruling. Whether a given deployment's contracts, invoices, or tax registrations impose additional duties on an instance operator is a question for qualified counsel in the operator's jurisdiction. Stripe capabilities per country corridor must be confirmed with Stripe at setup time.

## Findings

| ID | Finding | I-VSD principle | Domain | Severity |
|---|---|---|---|---|
| IVSD-F001 | The instance operator's Stripe secrets are **Connect platform credentials, not merchant credentials**. They authorize session creation, webhook verification, and refunds on the organizer's connected account; they never receive ticket funds. | Trust (`Amanah`) | Architecture, operations | Informational |
| IVSD-F002 | Tenant-scoped Stripe secrets are architecturally forbidden and correctly so. `SecretScope.Tenant` for Stripe secrets is rejected in code; only two instance-scoped Stripe secrets exist. | Trust, stewardship | Security, governance | High (protective invariant) |
| IVSD-F003 | Checkout acceptance already discloses a **triple identity** — organizer merchant, tenant directory operator, instance operator — each pinned immutably in a SHA-256-revisioned acceptance snapshot. | Truthfulness (`Sidq`), excellence (`Ihsan`) | UX, data | Informational (implemented) |
| IVSD-F004 | The organizer is the sole merchant of record on financial documents. Stripe receipts, emails, and the card statement descriptor come from the organizer's connected account; operator identity never appears as merchant. | Truthfulness, avoidance of uncertainty (`Gharar`) | UX, financial | Informational (implemented) |
| IVSD-F005 | Operator responsibility is operation-scoped, not blanket transaction liability: `CheckoutGovernance` owners (`Platform`/`Organizer`) pin who handles complaints, refunds, disputes, and reconciliation per deployment. | Justice (`'Adl`), non-harm (`Lā Darar`) | Governance, legal-adjacent | Medium |
| IVSD-F006 | Single-tenant deployments still require both identity documents. `PaidCommerce` readiness is an intersection (tenant directory operator identity ∧ instance operator identity ∧ active payment operations ∧ policies ∧ eligible organizer connection) and fails closed. | Trust, excellence | Governance | Informational |
| IVSD-F007 | Documentation drift breaks dual-documentation parity: `PAYMENTS.md` §10 names a stale secret key, and the public Infisical guide states governance enum values the code rejects. | Truthfulness | Documentation | Medium |
| IVSD-F008 | The optional platform fee is a transparent Stripe application fee on the direct charge, disclosed in acceptance money facts and snapshot-pinned; it never changes the merchant recipient. | Justice, truthfulness | Financial, UX | Informational |

### Finding Detail

**IVSD-F001 — Operator secrets are Connect platform credentials.**
- Lifecycle: current. Severity: informational. Stakeholder: instance operator, organizer.
- Evidence: `StripeCheckoutAdapter` sets `RequestOptions.StripeAccount = request.ExternalAccountId` (the organizer's `acct_...`) — a direct charge in the organizer's connected-account context using the platform secret key. `Describe()` discloses `CredentialOwner = "instance-operator"`, which is pinned into the buyer acceptance snapshot.
- Provider-controlled decision: Stripe's Connect platform binding (one platform per instance, connected accounts attached to it).
- Linked mitigation: IVSD-M001 (keep disclosing credential ownership in acceptance facts — already shipped).
- Escalation boundary: none.

**IVSD-F002 — Tenant-scoped Stripe secrets are forbidden.**
- Lifecycle: current. Severity: high (protective invariant). Stakeholder: tenant admin, instance operator.
- Evidence: `SecretDefinitionRegistry` defines exactly two Stripe secrets — `payments.stripe.platform_secret_key` (`STRIPE_PLATFORM_SECRET_KEY`, Infisical `/stripe/STRIPE_PLATFORM_SECRET_KEY`, `sk_test_`/`sk_live_`) and `payments.stripe.webhook_secret` (`STRIPE_WEBHOOK_SECRET`, `whsec_`) — both `SecretScope.Instance` only; `SecretBinding.CreateInfisical` with `SecretScope.Tenant` throws, verified by `StripeSecretDefinitionTests`.
- Rationale: Stripe Connect binds connected accounts to one platform per instance; tenant-held raw keys would fracture webhook signing integrity, secret hygiene (tenant admins must never touch raw keys), and liability allocation (per-tenant platforms would make each tenant a payment facilitator). A tenant wanting its own Connect platform should run its own instance — a sovereignty boundary, not a configuration option.
- Linked mitigation: IVSD-M002 (preserve the registry scope rejection and the tenant policy can-only-narrow rule).
- Escalation boundary: any request to enable tenant-scope payment secrets requires architectural re-approval.

**IVSD-F003 — Triple identity disclosure is implemented.**
- Lifecycle: current. Severity: informational. Stakeholder: attendee, organizer, tenant, operator.
- Evidence: `PaidOrderAcceptanceService` emits a versioned disclosure before provider handoff: organizer actor + immutable connection lineage (connection ID, Connect platform ID, external account, merchant country); exact tenant directory document/revision + normalized identity facts; exact instance operator identity (legal name, operator kind, registration identifier); a separate `paymentOperations` group. The browser acknowledges the SHA-256 `DisclosureRevision`; the server recomputes and persists an immutable `PaidOrderAcceptanceSnapshot` pinning owners, operator legal URLs, statement descriptor, credential owner, and activation status.
- Linked mitigation: none needed (shipped); IVSD-M003 guards against future erosion.
- Escalation boundary: none.

**IVSD-F004 — Organizer is merchant of record on financial documents.**
- Lifecycle: current. Severity: informational. Stakeholder: attendee, organizer.
- Evidence: in `OrganizerDirect` the charge is created on the organizer's connected account; Stripe therefore issues receipts/emails from the organizer's account, and the attendee's bank statement descriptor comes from the organizer's own Stripe account settings. The adapter sends no `statement_descriptor`; the governance 22-character `StatementDescriptor` is a disclosed, pinned acceptance fact only. `PAYMENTS.md` §12: the platform is not the merchant's accounting, tax, invoice, credit-note, banking, or escrow system.
- Consequence for the consultation: the premise "all tenancies will have instance operator branding on the invoices" is false. Operator identity appears as platform legal-notice/terms/privacy links and operation ownership in checkout disclosure and the public footer — transparency roles, never merchant identity. The attendee's financial counterparty is unambiguous.
- Linked mitigation: IVSD-M004 (organizer guidance to set a recognizable statement descriptor in their Stripe account settings).
- Escalation boundary: none.

**IVSD-F005 — Operator responsibility is operation-scoped.**
- Lifecycle: current. Severity: medium. Stakeholder: instance operator, organizer, attendee.
- Evidence: `IPaidCheckoutGovernance` options `ComplaintOwner`, `RefundOwner`, `DisputeOwner`, `ReconciliationOwner` (each `Platform` or `Organizer`) are pinned per order in the acceptance snapshot. The operator supplies platform legal terms and can disable paid ticketing entirely; the operator holds zero financial authority (optional transparent application fee/tip only) and cannot act as merchant or divert funds.
- The pasted "dual identity because the operator is legally responsible" argument is therefore imprecise for this architecture: the organizer is the transaction's merchant; the operator's responsibility is whatever its platform terms and governance owners declare per operation.
- Linked mitigation: IVSD-M005 (operators must set governance owners deliberately; defaults fail closed under `suspended`).
- Escalation boundary: actual legal responsibility allocation per jurisdiction → qualified counsel (see Escalation Needed).

**IVSD-F006 — Single-tenant still requires both identity documents.**
- Lifecycle: current. Severity: informational. Stakeholder: single-tenant operators.
- Evidence: `PaidCommerce` readiness is the intersection of complete tenant directory operator identity, complete instance operator identity, active payment operations, current policies, and an eligible organizer connection. Even when one legal entity is operator, tenant, and organizer, both documents are modeled separately and branding never substitutes for identity.
- Linked mitigation: none (fail-closed by design).
- Escalation boundary: none.

**IVSD-F007 — Documentation drift breaks parity.**
- Lifecycle: current. Severity: medium. Stakeholder: adopters following public docs, operators following internal docs.
- Evidence: `docs/internal/PAYMENTS.md` §10 table still names `payments.stripe.platform_api_key` / `PAYMENTS_STRIPE_PLATFORM_API_KEY` (stale — registry, tests, environment catalogue, CONFIGURATION.md, and SECRETS.md all say `platform_secret_key` / `STRIPE_PLATFORM_SECRET_KEY`). `docs/public/documentation/readme/configuration-and-operations/infisical.md` documents `ACTIVATIONSTATUS` values `Active`/`Inactive` and `CHARGETYPE` `Direct`/`Destination`, but code enforces `approved`/`suspended` and `direct-charge` only (destination charges rejected). AGENTS.md rule 13 (Dual-Documentation Parity) requires same-PR public+internal updates.
- Linked mitigation: IVSD-M007 (fix both doc files; add the public/internal pair to any future governance-value change checklist).
- Owner: docs maintainer. Escalation boundary: none.

**IVSD-F008 — Platform fee is a transparent application fee.**
- Lifecycle: current. Severity: informational. Stakeholder: organizer, attendee, operator.
- Evidence: `PlatformFeePolicy`/`PlatformFeeFixedCharge` produce `ApplicationFeeAmount` on the direct charge (flowing to the platform account), disclosed in acceptance money facts and snapshotted (`PlatformFeeMinor`, `PlatformFeeTotalMinorSnapshot`, `PlatformFeePolicyVersionSnapshot`, `ApplicationFeeRefundedAmountMinor`). It never alters the charge recipient.
- Linked mitigation: none (shipped per the predecessor consultation's recommendation).
- Escalation boundary: none.

## Recommendations

### Direct Answers to the Consultation Questions

1. **Does instance-operator identity make sense in single- and multi-tenant?** Yes, in both — reframed. The operator's Stripe secrets are Connect *platform* credentials: they facilitate onboarding, session creation on the organizer's account, webhook verification, and refunds. The operator is a technical facilitator and terms supplier, never merchant of record and never a funds holder. In single-tenant the same legal entity may wear all hats, yet the system still models operator and tenant identity as separate documents (IVSD-F006) — this is a feature: it keeps disclosure honest and the deployment portable to multi-tenant later.
2. **Should each tenant supply its own Stripe secrets?** No — and the code already forbids it (IVSD-F002). Per-tenant platforms would fragment webhook integrity, secret hygiene, and liability. A tenant that wants to be its own payment platform should run its own instance; that is the sovereignty boundary. Keep the registry rejection intact.
3. **Won't operator branding on invoices confuse attendees?** The premise is false (IVSD-F004). Stripe receipts, emails, and the bank statement descriptor come from the organizer's connected account. Operator identity appears only in checkout disclosure and footer as platform legal links and operation ownership — transparency, not merchant identity. There is no merchant confusion to mitigate, and chargeback-confusion risk is handled by the organizer owning their statement descriptor.
4. **Should invoices carry dual identity?** Not as proposed. The implemented answer is stronger: a **triple identity at checkout acceptance** (IVSD-F003) — organizer merchant, tenant directory operator, instance operator, each immutably pinned — while fiscal documents (Stripe-issued) show the organizer alone as merchant. Dual *invoice* identity would wrongly imply shared merchant responsibility. Keep the current model: single merchant on financial documents, triple identity in pre-purchase disclosure.
5. **Full Stripe setup inventory ("tell me all").** See the next section — everything is instance-operator-only, fails closed until complete.

### Complete Stripe Activation Inventory (Instance Operator)

Ordered; each step fails closed until complete:

1. **Stripe Dashboard (platform account):** obtain the secret key (`sk_test_`/`sk_live_`) and the Connect platform client ID (`ca_...`, non-secret). Register the Connect webhook endpoint `/api/integrations/stripe/connect` and obtain its signing secret (`whsec_`).
2. **Server secrets (env or Infisical, instance scope only):** `STRIPE_PLATFORM_SECRET_KEY` (Infisical path `/stripe/STRIPE_PLATFORM_SECRET_KEY`) and `STRIPE_WEBHOOK_SECRET`.
3. **Non-secret payment config:** `PAYMENTS_STRIPE_MODE` (`Test`/`Live`), `Payments:Stripe:AllowedCheckoutHosts` (default `checkout.stripe.com`), `PAYMENTS_ORGANIZER_DIRECT_PROVIDER_CODE` (default `stripe`), `PAYMENTS_ORGANIZER_DIRECT_CONNECT_PLATFORM_ID` (`ca_...`).
4. **Checkout governance env (`Payments:CheckoutGovernance`):** `ComplaintOwner`, `RefundOwner`, `DisputeOwner`, `ReconciliationOwner` (each `Platform` or `Organizer`), `ActivationStatus=approved` (default `suspended`), `RefundPolicyLanguageTag`, `StatementDescriptor` (non-empty, ≤22 chars, disclosure fact), `ChargeType=direct-charge` (only accepted value).
5. **Admin configuration:** complete `instance.operator_identity` (public/legal name, operator kind, jurisdiction, registration, contact, legal-notice/terms/privacy URLs); per-tenant `tenant.directory-operator-identity`; instance paid-event policy (currencies, organizer kinds, verification, refund floors, optional platform fee); optional tenant narrowing (disable/restrict only).
6. **Organizer side (no secrets):** hosted Stripe Connect onboarding (KYC/KYB on Stripe's domain) → signed `account.updated` webhooks → connection `Ready` (charges + payouts enabled) → `PaidEventPublicationPreflightService` unlocks publishing.

No tenant or organizer ever touches a raw Stripe key. Exports contain no payment secrets.

### Mitigations

| ID | Mitigation | Status | Owner |
|---|---|---|---|
| IVSD-M001 | Continue disclosing `ProviderCredentialOwner` in acceptance facts and snapshots | Shipped | Payments maintainers |
| IVSD-M002 | Preserve `SecretScope.Tenant` rejection for Stripe secrets and the tenant can-only-narrow rule | Shipped (guard with tests) | Secrets maintainers |
| IVSD-M003 | Do not erode triple-identity disclosure or snapshot immutability in future refactors | Standing | Payments/UX maintainers |
| IVSD-M004 | Add organizer guidance to set a recognizable statement descriptor in their own Stripe account settings (the platform cannot set it for them) | Proposed | Docs maintainer |
| IVSD-M005 | Operators must set governance owners deliberately before `approved`; defaults stay `suspended` | Shipped (fail-closed) | Instance operators |
| IVSD-M007 | Fix `PAYMENTS.md` §10 secret naming and public `infisical.md` governance enum values; same-PR parity thereafter | Proposed | Docs maintainer |

## Stakeholders

| Stakeholder | Interest in this consultation |
|---|---|
| Instance operator (self-hoster) | Sole supplier of Stripe platform credentials; wants clarity on what those credentials do and do not make them |
| Tenant admin | Confirmed zero secret access, narrowing-only policy authority |
| Organizer | Merchant of record; owns Stripe account branding (receipts, statement descriptor) and Connect onboarding |
| Attendee/buyer | Unambiguous merchant identity on financial documents; full pre-purchase disclosure of all three parties |
| ISLAMU steward | Ecosystem honesty: unrelated self-hosted instances are not ISLAMU-protected; credentials are never shared |

## I-VSD Principles And Domains

| Principle | Application here |
|---|---|
| `Amanah` (trust) | Secrets isolated to instance scope; credential ownership disclosed and pinned; organizer funds never pass through the operator |
| `Sidq` (truthfulness) | Honest statement descriptor and receipt identity; no operator masquerading as merchant; docs must match code (IVSD-F007) |
| `Gharar` avoidance | Unrecognized-descriptor chargebacks and merchant confusion prevented by organizer-owned financial identity |
| `'Adl` (justice) | Proceeds to the entitled organizer; platform fee transparent and recipient-preserving |
| `Lā Darar` (non-harm) | No accidental operator merchant-of-record/custody position; dispute fee harms assigned by deliberate governance owners |
| `Ihsan` (excellence) | SHA-256-revisioned acceptance disclosure with immutable snapshots before provider handoff |
| Stewardship | Operator supplies legal terms, sets ceilings, and can disable paid ticketing entirely |

Domains touched: strategy/business model, UX, architecture, data, operations, governance, portability.

## Validation Gaps

- IVSD-F007 doc drift is identified but not yet fixed; no automated check asserts PAYMENTS.md secret names against the registry.
- `PAYMENTS.md` §8 still marks refund initiation "not implemented" while `RefundAttempt` + `StripeRefundAdapter` exist — partial staleness not fully audited here.
- Organizer-facing guidance for statement descriptor setup (IVSD-M004) does not exist yet.
- No verification was done of what Stripe renders on receipts beyond the connected-account principle (Stripe-side presentation is provider-controlled).

## Escalation Needed

| Question | Escalate to |
|---|---|
| Whether any deployment's contracts/tax registrations impose merchant-of-record, invoicing, or payment-facilitation duties on the instance operator beyond governance owners | Qualified counsel, per operator jurisdiction |
| Stripe Connect availability, settlement currencies, and country-corridor behavior for the platform + organizer accounts | Stripe (at setup time) |
| Any Islamic-commercial ruling on platform fees or payment structures | Qualified scholars; this report makes no such ruling |

## Evidence Reviewed

- `src/Explore.Infrastructure/Payments/Stripe/Checkout/StripeCheckoutAdapter.cs` — direct charge via `StripeAccount` header; application fee; credential-owner descriptor; no `statement_descriptor` param.
- `src/Explore.Infrastructure/Payments/Stripe/Refunds/StripeRefundAdapter.cs` — refunds on connected account.
- `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs` (lines 145–146) — the only two Stripe secrets, instance scope only.
- `tests/Explore.Secrets.UnitTests/Configuration/StripeSecretDefinitionTests.cs` — tenant-scope binding throws.
- `src/Explore.Application/Contracts/Services/IPaidCheckoutGovernance.cs` — governance options, enum enforcement, fail-closed activation.
- `src/Explore.Domain/PaidOrderAcceptanceSnapshot.cs` / `PaidOrderAcceptanceFacts.cs` — immutable pinned disclosure facts.
- `src/Explore.Domain/Settings/Documents/Payloads/TenantDirectoryOperatorIdentitySettings.cs` — tenant identity document and readiness capabilities.
- `docs/internal/PAYMENTS.md` — responsibility hierarchy (§2), identity roles (§3), non-fiscal-system boundary (§12), config hierarchy (§11), stale §10.
- `docs/internal/adr/ADR-022-paid-event-commerce-and-stripe-connect.md`, ADR-024.
- `docs/internal/CONFIGURATION.md` (lines 2025–2026), `docs/internal/SECRETS.md` (lines 434–435), `docker-compose.yml` (lines 175–190), `eng/setup-assistant/generated/environment-catalogue.json`.
- `docs/public/documentation/readme/configuration-and-operations/infisical.md` (lines 369–376) — stale enum values.
- `README.md` (repository root) — zero-custody positioning.
- Prior reports: `consultations/i-vsd-paid-event-payments-consultation.md`, `governance/i-vsd-branding-legal-identity-authority.md`, `consultations/i-vsd-paid-events-deactivation-consultancy-report.md`.

## Missing Evidence

- Stripe-side receipt/email rendering specifics per connected-account country (provider-controlled).
- Jurisdiction-specific invoicing/tax obligations for organizers and operators (out of boundary — counsel).
- Hosted-official-instance policy choices for governance owner defaults.

## Context Inventory

- Skill resources: `i-vsd/SKILL.md` report contract (headings, finding/mitigation ID scheme, consultation path rule).
- Code: payment adapters, secret registry + tests, checkout governance contract, acceptance snapshot/facts, tenant identity settings.
- Docs: PAYMENTS.md, ADR-022/024, CONFIGURATION.md, SECRETS.md, public Infisical guide, docker-compose, environment catalogue, root README.
- Prior I-VSD reports listed above (cross-referenced, not duplicated).

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-15 | None | Current | Steward consultation on operator identity, tenant Stripe secrets, and invoice identity | This report (working-tree evidence, cutoff 2026-09-15) |

## Common Overlooked Failures And Outcomes

| Failure | Outcome | Status in this repo |
|---|---|---|
| Attendee doesn't recognize the charge descriptor → chargeback | Revenue loss, dispute fees, trust erosion | Mitigated: organizer owns descriptor; governance descriptor disclosed pre-purchase (IVSD-F004) |
| Operator drifts into merchant-of-record / funds custody (admin-collect temptation) | PCI liability, escrow regulation, riba/gharar exposure | Prevented: `direct-charge` only, destination charges rejected in code (IVSD-F005 context) |
| Secrets sprawl (tenant- or organizer-held raw keys) | Key leakage, webhook spoofing, liability fracture | Prevented: instance scope only, registry throws on tenant scope (IVSD-F002) |
| Branding substituted for legal identity | Dishonest disclosure, fails-closed bypass pressure | Prevented: identity documents are readiness gates; branding never substitutes (IVSD-F006) |
| Stale acceptance disclosure after policy change | Buyer agreed to different terms than enforced | Prevented: SHA-256 revision recompute + immutable snapshot (IVSD-F003) |
| Docs promise enum values code rejects | Adopter setup failure, parity breach | Open: IVSD-F007 → IVSD-M007 |
