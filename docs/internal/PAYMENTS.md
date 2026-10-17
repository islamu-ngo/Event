<!-- ABOUTME: Authoritative architectural and operational documentation for the provider-neutral payment subsystem. -->
<!-- ABOUTME: Covers OrganizerDirect, Stripe Connect adapter, multi-tenant policy hierarchy, and provider extension guide. -->

# Payments Architecture And Provider Integration

> **Audience:** Operators | Integrators | Contributors | AI agents
> **Status:** Implemented
> **Owner:** Platform/Commerce
> **Last Verified:** 2026-08-29
> **Source Anchors:** `Explore.Domain/PaymentAttempt.cs`, `Explore.Domain/OrganizerPaymentProviderConnection.cs`, `Explore.Domain/PaidEventPolicyVersion.cs`, `Explore.Domain/Services/Registration/PaidEventPolicyRules.cs`, `Explore.Application/Contracts/Payments/`, `Explore.Application/Contracts/Services/IOrganizerPaymentOnboardingProvider.cs`, `Explore.Application/Services/Registration/RegistrationPaymentAttemptClaimService.cs`, `Explore.Infrastructure/Payments/Stripe/`, `Explore.API/Controllers/PaidEventPolicySettingsController.cs`, `Explore.Blazor/Extensions/BffRegistrationPaymentEndpoints.cs`, `docs/adr/ADR-022-paid-event-commerce-and-stripe-connect.md`, `docs/adr/ADR-024-external-business-integrations-and-protected-payout-boundaries.md`

ISLAMU Event provides a robust, multi-tenant, and **provider-neutral payment architecture** for paid event ticketing. The subsystem is decoupled through Clean Architecture ports and adapters: domain and application logic remain completely independent of any specific payment vendor.

## Paid Checkout Activation Safety

New paid Checkout is disabled by default and fails closed until persisted
`instance.operator_identity` is complete and `Payments:CheckoutGovernance`
defines complaint, refund, dispute, reconciliation, activation, statement, and
charge-type operations. The browser first reads exact server-authored facts and
explicitly acknowledges their SHA-256 revision. The resulting
`PaidOrderAcceptanceSnapshot` pins the organizer actor, payment-connection ID,
Connect platform ID, external account ID, merchant country, tenant directory
document/revision, complete instance identity, separately grouped payment
operations, delivery, typed lines, aggregate money, refund/support facts, and
provider descriptor before a provider session can be created. Historical
attempts remain readable and reconcilable with a null acceptance reference;
they are never backfilled. A global or event stop-sale blocks new claim and
dispatch while signed webhooks, reconciliation, support, and reads continue.
Refund initiation is not implemented and is not a stop-sale capability.

`OrganizerDirect` describes the technical direct-charge profile. It does not establish who legally controls an account, bears loss, or owes a remedy in a particular deployment. Operators must retain provider, contractual, legal, and operational evidence for those deployment-specific conclusions.

## Durable Refund Operation And Handoff Ownership

`RegistrationRefundService` binds a native operation key and SHA-256 input
digest to tenant, payment, order, initiating principal, authority, reason and
the original nullable requested amount. An omitted/full amount differs from
an explicit amount. Exact authorized recovery precedes capacity checks; an
omitted amount resolves once under the reservation lock. Changed inputs return
a conflict without the earlier refund, even when capacity is now exhausted.

`RefundAttempt` keeps separate buyer and application-fee handoff phases,
dispatch tokens, concurrency stamps and positive no-handoff evidence.
`RefundAttemptRepository` commits handoff ownership with recovery outbox work,
then atomically settles the observed outcome with its next native work. A
pending marker, process death, cancellation or empty provider listing cannot
reset that ownership or authorize another mutation. Only positively recorded
pre-handoff evidence permits a new owner.

Stripe recovery reads pinned buyer-refund metadata and fee evidence. Its
refund-specific 23-hour mutation deadline is captured at the first handoff and
never renewed, independently of checkout/session expiry. Provider/account/
payment/key bindings stay immutable. SDK retries remain disabled. Signed
callbacks use the same atomic settlement owner; stale dispatch results cannot
overwrite committed callback success, and contention requests webhook retry.

Committed refund obligations survive initiating-actor revocation; command
authorization still precedes user-facing recovery disclosure. Unresolved
ambiguity retains capacity and the obligation for provider/manual reconciliation.
Deadline expiry or an empty listing is not proof of failure. Native regression
source is authored; generated migrations, actual provider retention contracts,
concurrency and programme assurance remain queued.

## Reconciliation Acquisition Recovery

`RegistrationPaymentReconciliationService` creates one
`PaymentReconciliationAcquisition` before database work: a UUIDv7 lease token,
bounded owner and batch size, UTC claimed time and fixed lease deadline.
Timestamps are captured at PostgreSQL's microsecond precision. Retries retain
these values; they refresh only the observation used to check expiry.

Global admission returns ownership identifiers, not payment content.
`PaymentReconciliationAdmission` and the direct repository first recover
still-processing rows matching the token, owner, claimed timestamp and exact
deadline. Recovery preserves the tenant, effect, payment attempt, processing
fence, attempt count and captured checkout-dispatch identity. It neither
renews a lease nor fills the batch with newly due rows. A mismatched existing
token, expired lease or reclaimed owner returns no authority. Payment reads,
provider retrieval and settlement still require the exact ordinary tenant
execution scope and its current row fence.

Execution-strategy re-entry is recovery-only. If acknowledgement is lost after
commit, admission also makes one read-only recovery attempt through a fresh
context with an independent five-second cancellation budget. A caller's
cancellation can therefore recover committed ownership without allowing
provider work: the service checks cancellation before entering each tenant.
If this independent recovery cannot establish a live acquisition, the original failure
propagates; a later scheduler pass can reclaim expired work normally.
There is no owner-name enumeration or payment mutation under Global.

Initial PostgreSQL reconciliation-role provisioning requires a database
administrator. That session may create the exact role graph for a separately
enrolled ordinary migrator. Later installation requires the enrolled migrator
session and the unchanged exact privilege guard. An ordinary `CREATEROLE`
login cannot perform first provisioning: PostgreSQL automatically gives it
administrator memberships granted by the bootstrap superuser, which that login
cannot revoke. The installer rejects this path before creating roles with
`55000` / `payment_reconciliation_administrator_bootstrap_required`; it does
not silently accept the extra memberships or repair administrator-owned grants.

Tenant settlement retains only the payment and newly created observation/outbox
objects from the locked live reconciliation claim. Their foreign-key visibility
attestation is bound to the original tenant, attempt, order, physical transaction
and connection. A different tracked object or a later transaction cannot borrow
that attestation. Normal persisted visibility checks and database foreign-key
constraints remain active; Runtime receives no private tenant-table read grant.

The PostgreSQL `SECURITY DEFINER` claim function accepts the caller's fixed
identity instead of generating another token. Its installer replaces the
obsolete overload and retains non-login, non-bypass owner roles, the restricted
column grants and Global execute-only admission. The portable compare-and-set
path and direct PostgreSQL query use the same recovery fences. Provider keys,
payment-provider contracts and economic settlement are unchanged.

The acknowledgement-loss, cancellation, retry, competing-owner and expiry
regressions are authored against native databases. This source delivery does
not certify runtime behavior; provider execution, installer/role assurance and
the deferred programme checks remain required.

## Ticket Purchase Authority And Ceilings

### Mutation Transport Retry Ownership

Unknown checkout redrive additionally requires the current provider descriptor's
`CheckoutMutationIdempotencyRetentionFloor`, with the same provider, profile
and pinned API revision. Stripe declares a 24-hour checkout key-retention floor.
The dispatch service measures from original `PaymentAttempt.CreatedAt`, not
the renewable checkout cutoff, and reserves a one-minute transport margin.
It checks before preparation and immediately before the provider handoff.
Absent, expired or changed guarantees return the attempt/effect to durable
Unknown without a new provider call, key or active-slot release. Proven
pre-handoff retries remain separate. Retrieval of a known session is read-only
and remains available outside this mutation replay window.

Five native regression cases cover missing retention, the exact margin boundary,
renewed cutoff, changed API revision and expiry while preparing the dispatch.
Provider-contract verification of the declared Stripe floor and runtime native
execution remain queued for final programme assurance.

Checkout replacement captures attempt and dispatch-effect IDs before native
transaction replay. It validates current acceptance, composition and provider
cutoff before releasing a terminal active slot or extending order/hold deadlines.
Negative admission results therefore commit no preparatory payment writes.
An existing queued replacement must match current composition and acceptance.
Persistence catches only metadata-classified payment uniqueness contention
outside an enclosing transaction; it recovers the exact provider operation,
composition, money and recipient binding, never an arbitrary active attempt.
Other database failures propagate to the native transaction owner for rollback.

The checkout, refund and Connect adapters construct Stripe SDK clients with
`MaxNetworkRetries = 0`. Named HTTP clients retain existing request and
connection deadlines, without a transport retry layer around mutations.
One native dispatch makes at most one handoff per mutation. Stable provider
operation keys remain attached where the port supplies them.

Timeouts, connection loss and retryable provider responses after possible
handoff do not prove that no mutation occurred. Native payment/refund state
machines own reconciliation and authorized redrive, including lease, current
authority and provider idempotency-retention constraints. Connect callers
must resolve uncertain onboarding mutations rather than rely on SDK replay.
This setting does not certify business recovery. Refund retrieval can itself
perform a fee-refund mutation; that handoff also has no transparent SDK retry.

Purchase governance runs before payment-provider I/O. The Application handler derives quantity from the persisted registration-order lines, resolves the current tenant, and asks `TicketPurchaseAuthorityResolver` for a server-proven enforcement dimension. A command cannot supply an account ID, contact hash, enforcement key, or quantity.

Hard cross-order ceilings use only stable authority:

- an authenticated account remains the controlling key when the purchaser switches between personal, group, or organization actor contexts;
- a verified guest uses the SHA-256 hash of the persisted, verified normalized contact; and
- name-only access is honestly order-scoped and receives no claimed per-person cross-order guarantee.

The pinned effective ceiling is the literal minimum of instance, tenant, and event policy versions. PostgreSQL serializes an authoritative operation/authority lock set before opening the serializable transaction, then records cumulative authority usage and the tenant-qualified durable operation outcome. An exact key and fingerprint replays the original result. Reusing a key with a different tenant, principal, route-equivalent command scope, policy, quantity, actor context, or body conflicts rather than duplicating authority.

These local database steps perform no payment-provider call. Provider claims and dispatch remain later durable operations, preserving the rule that remote I/O never occurs inside the purchase-governance transaction.

**Stripe is currently the initial concrete payment provider adapter** implemented in Infrastructure via Stripe Connect. The platform is designed so additional payment gateways (such as PayPal, Mollie, Lemonsqueezy, or regional processors) can be added as modular Infrastructure adapters without altering the core Domain or Application business rules.

---

## 1. Provider-Neutral Architecture (Ports & Adapters)

The payment subsystem strictly follows Clean Architecture:

```mermaid
graph TD
    subgraph Domain ["Domain Layer (Core Rules & Invariants)"]
        PA[PaymentAttempt]
        PAE[PaymentAttemptEffect]
        OPC[OrganizerPaymentProviderConnection]
        PEPV[PaidEventPolicyVersion]
        OPS[OrganizerPaymentRecipientSnapshot]
    end

    subgraph Application ["Application Layer (Use Cases & Capability Ports)"]
        Preflight[PaidEventPublicationPreflightService]
        Claim[RegistrationPaymentAttemptClaimService]
        Recon[OrganizerPaymentReadinessReconciliationService]
        PortOnboarding[IOrganizerPaymentOnboardingProvider]
        PortCheckout[IHostedCheckoutSessionCreator / Retriever]
        PortIntent[IPaymentIntentRetriever]
        PortDesc[IPaymentProviderDescriptor]
    end

    subgraph Infrastructure ["Infrastructure Layer (Provider Adapters)"]
        StripeConnect[StripeConnectAccountAdapter]
        StripeCheckout[StripeCheckoutAdapter]
        StripeWebhooks[Stripe Incoming Webhook Verifier & Handler]
        FutureAdapter[Future Provider Adapter: PayPal / Mollie / etc.]
    end

    Domain --> Application
    Application -.->|implements ports| Infrastructure
```

### Layer Responsibilities

| Layer | Responsibility | Key Components |
|---|---|---|
| **Domain** | Provider-neutral aggregates, financial value objects, immutable recipient snapshots, and pure policy narrowing rules. | [`PaymentAttempt`](../../src/Explore.Domain/PaymentAttempt.cs), [`OrganizerPaymentProviderConnection`](../../src/Explore.Domain/OrganizerPaymentProviderConnection.cs), [`PaidEventPolicyVersion`](../../src/Explore.Domain/PaidEventPolicyVersion.cs), [`PaidEventPolicyRules`](../../src/Explore.Domain/Services/Registration/PaidEventPolicyRules.cs) |
| **Application** | Use-case orchestration, payment claim management, preflight checks, and provider capability interfaces. | [`IOrganizerPaymentOnboardingProvider`](../../src/Explore.Application/Contracts/Services/IOrganizerPaymentOnboardingProvider.cs), [`IHostedCheckoutSessionCreator`](../../src/Explore.Application/Contracts/Payments/IHostedCheckoutProvider.cs), [`PaidEventPublicationPreflightService`](../../src/Explore.Application/Features/EventTicketing/Services/PaidEventPublicationPreflightService.cs) |
| **Infrastructure** | Concrete third-party SDK calls, HTTP clients, signature verifications, retries, and error mapping. | [`StripeConnectAccountAdapter`](../../src/Explore.Infrastructure/Payments/Stripe/StripeConnectAccountAdapter.cs), [`StripeCheckoutAdapter`](../../src/Explore.Infrastructure/Payments/Stripe/Checkout/StripeCheckoutAdapter.cs) |
| **Presentation (API/BFF)** | HAL resource links, antiforgery-protected checkout redirection, and incoming webhook ingestion endpoints. | `PaidEventPolicySettingsController`, `OrganizerPaymentConnectionsController`, `BffRegistrationPaymentEndpoints`, `IncomingWebhooksController` |

---

## 2. Multi-Tenancy & Responsibility Hierarchy

To maintain Islamic Value-Sensitive Design principles (Trust / *Amanah*, Justice / *'Adl*, and Non-Harm / *Lā Darar*), payment authority is divided across four distinct roles:

| Dimension | Instance Administrator (Platform Host) | Tenant Administrator | Event Organizer (Merchant) | Buyer / Attendee |
|---|---|---|---|---|
| **Scope** | Platform-wide / All tenants | Single Tenant | Specific Event / Organization | Individual Order |
| **Owns Secrets?** | **Yes** (`.env` / Infisical server-only platform API keys & webhook secrets) | **No** (Zero access to platform secrets) | **No** (Never enters or manages API secrets) | **No** |
| **Policy Authority** | Defines the **Global Policy Ceiling** (allowed currencies, allowed organizer kinds, minimum verification, refund floors, platform fee). | Governs **Policy Narrowing** (can disable payments, restrict currencies, enforce stricter verification or refund floors). | Chooses single event currency, ticket prices, and refund policy from within effective tenant policy. | Accepts ticket pricing and published refund terms. |
| **Financial Authority** | Receives optional transparent platform fee or platform tip if configured. | **Zero financial authority**; cannot act as merchant or divert ticket funds. | **Full commercial merchant authority**; receives net ticket proceeds directly. | Pays order total via provider-hosted checkout. |
| **Account Setup** | Configures the Connect Platform account with payment provider(s). | Manages tenant settings via Admin UI. | Completes provider-hosted onboarding (e.g. Stripe Connect) to link bank account. | Completes checkout on provider's domain. |

### Policy Ceiling vs. Policy Narrowing

Under [`PaidEventPolicyRules`](../../src/Explore.Domain/Services/Registration/PaidEventPolicyRules.cs), tenant policies can **only narrow** the instance policy ceiling, never broaden it:

1. **Payments Activation**: If disabled at the instance level (`IsPaymentsEnabled = false`), no tenant can enable payments. If enabled at the instance level, a tenant can disable payments (`IsPaymentsEnabled = false`) for itself.
2. **Allowed Currencies**: Effective currencies = `Instance Currencies ∩ Tenant Currencies ∩ Provider Capabilities`.
3. **Organizer Types**: If the instance allows `[Organization, Group, User]`, a tenant can restrict its policy to `[Organization]` only.
4. **Verification Floor**: If the instance requires local verification, a tenant cannot waive it.
5. **Refund Floor**: A tenant or organizer can offer more generous refunds, but can never reduce protections below the instance baseline.

---

## 3. Structured Legal-Identity And Paid Acceptance

The payment model preserves four explicit roles. It does not derive a legal
identity from cosmetic branding and does not infer legal conclusions for an
operator:

| Role | Authority source | Paid responsibility represented by the system |
|---|---|---|
| Tenant directory operator | `tenant.directory-operator-identity` typed settings document | Public accountability for the tenant directory and its legal/contact links |
| Instance operator | Persisted `instance.operator_identity` (`IInstanceOperatorIdentityReadinessEvaluator`) | General platform identity and operator legal/contact links |
| Organizer merchant | Event organizer actor plus connected provider account | Recipient/merchant lineage for the direct charge |
| Payment operations | `Payments:CheckoutGovernance` | Complaint, refund, dispute, reconciliation, activation, statement, and charge-type operations |

`PaidCommerce` readiness is an intersection, not a fallback chain. Paid event
publication and Checkout activation require a complete tenant directory
operator identity, instance identity explicitly assessed for `PaidCommerce`, active payment
operations, current policies, and an eligible organizer payment connection.
Missing or corrupt tenant identity blocks the operation with stable reason
codes; branding is never consulted as a substitute. A valid administrative draft
or successful `PublicDisclosure` assessment does not authorize commerce. Commercial
terms remain mandatory, and recipient, revision, money, idempotency, acknowledgement,
and immutable-acceptance checks are unchanged.

Before provider handoff, `PaidOrderAcceptanceService` returns a structured
`PaidOrderAcceptanceDisclosureDto` containing:

- a code-owned, versioned acceptance template identifier and exact text;
- organizer actor plus immutable payment-connection, Connect-platform,
  external-account, and merchant-country lineage;
- the exact tenant directory document/revision and normalized identity facts;
- the exact instance operator identity, including legal name, operator kind,
  and optional registration identifier;
- a separate `paymentOperations` group for complaint, refund, dispute,
  reconciliation, activation, statement, and charge-type facts;
- policy, schedule, money, line, and support facts.

The browser sends only the `DisclosureRevision` and explicit acknowledgement.
The server recomputes the disclosure, rejects stale evidence, and persists an
immutable `PaidOrderAcceptanceSnapshot`. Any directory identity revision,
instance identity fact, payment-operation fact, organizer actor, connection,
platform, external account, merchant country, provider descriptor, policy,
schedule, line, or money change invalidates an earlier acceptance. Claim-time
fencing repeats the exact recipient comparison before any payment-attempt or
dispatch write.

```mermaid
sequenceDiagram
    autonumber
    actor Buyer
    participant UI as Blazor Checkout
    participant App as Application
    participant Tenant as Tenant Identity Document
    participant Instance as Instance Operator Identity
    participant Provider as Organizer Payment Provider

    App->>Tenant: Evaluate PaidCommerce readiness
    App->>Instance: Evaluate persisted identity for PaidCommerce
    App->>Provider: Resolve organizer recipient lineage
    App-->>UI: Structured multi-party disclosure + revision
    Buyer->>UI: Acknowledge revision
    UI->>App: Revision + acknowledgement
    App->>App: Recompose and compare exact facts
    App-->>Provider: Handoff only when evidence is fresh
```

The public footer renders the tenant **Directory operator** and instance
**Platform operator** separately. Paid event details and ticket selection render
the structured tenant directory operator; payment review renders organizer,
directory, and instance groups. No prose disclaimer DTO, branding interpolation,
dual read, or compatibility alias remains.

---

## 4. Core Operating Profiles

### `OrganizerDirect` (Default & Active)
In the `OrganizerDirect` profile:
- Ticket charges are created as **direct charges** in the event organizer's connected merchant account context (`StripeAccount: acct_...`).
- The payment provider assesses its processing fees directly against the organizer's account and acts as the collector of payment losses.
- Funds flow directly from the ticket buyer to the organizer's connected account and into their linked bank account via standard provider payout schedules.
- **Zero Intermediary Escrow**: The platform host and tenant administrators never hold, pool, or escrow organizer ticket funds.

### `ProtectedDelayedPayout` (Deferred under ADR-024)
- An optional, future operating profile for institutional deployments with dedicated risk and compliance programs.
- Requires explicit operator approval and legal holding agreements.
- Never described as generic "escrow"; governed by bounded milestone releases within provider capabilities.

---

## 5. Financial Invariants & Safety Rules

1. **Integer Minor Units**: All monetary amounts are stored and calculated as integer minor units (`long` or `int`, e.g., cents, pence) using the [`Money`](../../src/Explore.Domain/ValueObjects/Money.cs) value object. Floating-point types (`float`, `double`, `decimal`) are strictly forbidden in domain money calculations to eliminate rounding errors.
2. **Explicit Single Currency**: Every ticket catalog version and registration order is bound to exactly one immutable ISO-4217 currency code (e.g. `EUR`, `USD`, `SAR`). Mixed-currency orders and silent foreign exchange conversions are forbidden.
3. **Zero Cardholder Data Liability (PCI DSS)**: ISLAMU servers never handle, transmit, or store credit card numbers, CVVs, or bank account credentials. All payment capture occurs via provider-hosted checkouts (e.g. Stripe Checkout) or provider-hosted onboarding.
4. **Pre-Commit Idempotency**: Mutating provider requests require a deterministic, unique idempotency key persisted in [`PaymentAttempt`](../../src/Explore.Domain/PaymentAttempt.cs) before executing remote network I/O.
5. **Asynchronous Provider Truth**: Browser return URLs are treated as navigation only, not proof of payment. Terminal payment state is advanced strictly through signed webhooks and background reconciliation jobs.
6. **Immutable Historical Snapshots**: When a ticket catalog is published, an [`OrganizerPaymentRecipientSnapshot`](../../src/Explore.Domain/OrganizerPaymentRecipientSnapshot.cs) records the exact organizer actor, connected account ID, country, currency, and policy version. Replacing a connected account applies only to future sales and never alters historical order records.

---

## 6. End-to-End Payment Flow

```mermaid
sequenceDiagram
    autonumber
    actor Buyer as Attendee / Buyer
    participant BFF as Blazor BFF
    participant API as Explore.API
    participant DB as PostgreSQL
    participant Provider as Payment Provider (e.g., Stripe)

    Note over Buyer, API: 1. Order Creation & Payment Handoff
    Buyer->>BFF: Submit Registration Order (Positive Total)
    BFF->>API: POST /api/registration/orders (AwaitingPayment)
    API->>DB: Save Order & Create PaymentAttempt Claim (Pending)
    API->>Provider: Create Hosted Checkout Session (Direct Charge + Idempotency Key)
    Provider-->>API: Return Checkout URL & Session ID
    API->>DB: Record Dispatch Effect & Session ID
    API-->>BFF: Return RegistrationPaymentCheckoutTargetDto (typed generated client)
    BFF-->>Buyer: Set protected checkout ticket cookie, then 303 on navigation

    Note over Buyer, Provider: 2. Payment Execution on Provider Domain
    Buyer->>Provider: Enter Payment Details & Complete Authorization
    Provider-->>Buyer: Redirect back to BFF Return URL

    Note over Provider, API: 3. Authoritative Reconciliation (Asynchronous)
    Provider->>API: POST /api/integrations/{provider}/connect (Signed Webhook)
    API->>API: Verify Raw HMAC Signature & API Version
    API->>DB: Store Webhook in Inbox & Apply Monotonic State Transition
    API->>DB: Mark PaymentAttempt Succeeded -> Finalize Order (Confirmed)
    API->>DB: Enqueue OrderFinalized Outbox Message
    API-->>Provider: 200 OK

    Note over Buyer, BFF: 4. Buyer Polling & Confirmation
    Buyer->>BFF: Poll Order Status
    BFF->>API: GET /api/registration/orders/{id}
    API-->>BFF: Order Confirmed
    BFF-->>Buyer: Display Confirmation & Admission Badges
```

### Stateless Checkout Handoff Ticket

The browser hop from our origin to the provider's hosted Checkout page carries no
server-side state. `Explore.Blazor` holds no Redis entry, no in-memory dictionary,
and no nonce table for checkout tickets; every fact needed for the redirect travels
inside one ASP.NET Core Data Protection payload.

**Protected payload contents**

| Field | Purpose |
|---|---|
| Target URL | Absolute provider Checkout URL, taken from the `RegistrationPaymentCheckoutTargetDto.Url` the BFF receives from `Explore.API` through the generated typed client. |
| Audience | Request scheme, host, path base, tenant slug, event id, and order id at issue time. |
| Session digest | Binds the ticket to the dedicated `__Secure-islamu-registration-payment-session` checkout-session cookie. This is deliberately *not* the authentication BFF session, so refreshing or rotating the auth session does not invalidate an in-flight checkout hop. |
| Expiry | Issue time plus 5 minutes. |

The protected value is ASCII and capped at 3,072 bytes, comfortably inside the
4,096-byte browser cookie ceiling. Issuance rejects anything larger rather than
emitting a cookie the browser would silently drop.

**Navigation contract (`GET /bff/registration-payments/checkout`)**

The endpoint accepts same-origin navigations only and takes no query string. Before
it emits anything, it unprotects the payload and checks the audience, the session
digest, the expiry, HTTPS, and an exact match against the configured allowed
Checkout host. Only then does it return `303 See Other` together with a deletion
cookie carrying `Max-Age=0`.

**Honest security boundary.** The deletion cookie clears the copy the requesting
browser holds. It is not a server-side single-use guarantee. A ticket copied out of
the browser before navigation stays replayable until its 5-minute expiry, and
issuing a replacement ticket does not revoke copies already made. What the deletion
cookie buys is hygiene, not revocation.

Be precise about what a replay gets you. Anyone holding a copied ticket cookie plus
the matching checkout-session cookie can revisit the same hosted Checkout session
until the ticket expires. The BFF prevents neither access to that hosted URL nor
completion of a payment there; the hosted URL itself may confer access to the
session. Authorization for what may be paid, and by whom, rests with the provider's
session rules and the API's order authorization, not the deletion cookie.

What the BFF does guarantee is narrower: navigation is inert. It never creates a
payment, never settles one, and never mutates order state. Money moves only through
`Explore.API` with provider idempotency keys, signed webhook ingestion, and
reconciliation, none of which this ticket changes. A replayed redirect does not
dispatch another payment-create request.
[Stripe expires Checkout Sessions](https://docs.stripe.com/api/checkout/sessions/expire),
and an expired session cannot complete. Treat that as a bound on session lifetime,
not as proof about what was or wasn't billed. Billing facts come from webhook
ingestion and reconciliation.

**Key ring requirements.** The ticket is only readable by a host that shares the
issuing key ring. In Split topology, `BffDataProtectionExtensions` pins the application name to
`islamu-event` and persists keys to Redis only when a `cache` connection string is
configured; with no cache configured the BFF falls back to the local native key
store. Multi-replica BFF deployments therefore MUST share a key ring, whether by
mounting a shared key directory or by configuring the Redis key ring, and MUST keep
the same application name. Database-backed Data Protection in `Explore.API` does not
cover the Split BFF; the BFF has its own key ring decision.

---

## 7. Organizer Payment Onboarding Flow

To publish paid events, an organizer actor must connect an eligible merchant account:

```text
[Organizer in Studio UI]
       │
       ▼ 1. Request Onboarding Link
[API: OrganizerPaymentConnectionsController]
       │
       ▼ 2. Claim Operation & Call Provider Adapter
[Infrastructure: IOrganizerPaymentOnboardingProvider]
       │  - Creates Connected Account on Platform
       │  - Generates Secure Hosted Onboarding Link
       ▼
[Organizer Browser navigates to Hosted Provider Onboarding]
       │  - Completes KYC/KYB & Bank Details directly on Provider domain
       ▼
[Provider Callback / Background Reconciliation Worker]
       │  - Polling worker checks charges_enabled & payouts_enabled
       │  - Signed webhooks deliver account.updated events
       ▼
[OrganizerPaymentProviderConnection: Status -> Ready]
       │
       ▼
[PaidEventPublicationPreflightService: PASS -> Affordance "Publish" Unlocked]
```

---

## 8. Refunds And Balance Liability In OrganizerDirect

`OrganizerDirect` is a technical direct-charge profile, not a universal legal or loss-allocation conclusion. Provider balance recovery, bank debit eligibility, negative-balance collection, and liability vary by provider configuration, controller country, connected-account agreement, payment method, and applicable law. Operators must verify those facts against current provider and contractual evidence; this document does not state that a provider will debit an organizer automatically, pursue one party, or insulate another party from loss.

ISLAMU Event does **not** currently implement refund initiation, a `RefundAttempt` aggregate, a refund provider port, or a refund API. A stop-sale therefore preserves no in-product refund command. Operators must not advertise an application refund workflow until the separate refund workstream ships and is operationally rehearsed.

Any future refund implementation must:

1. pin the original tenant, order, provider, connected account, charge, currency, and amount;
2. use durable idempotency and an explicit reconciliation state machine;
3. preserve buyer remedy access while new sales are stopped;
4. expose only server-authorized HAL affordances;
5. document deployment-specific responsibility without deriving legal conclusions from the charge profile; and
6. prove webhook, retry, duplicate-delivery, insufficient-balance, and restore behavior before activation.

```mermaid
stateDiagram-v2
    [*] --> Paid: Webhook Confirmed
    Paid --> RefundRequested: Buyer requests refund via self-service portal
    RefundRequested --> OrganizerApproved: Organizer approves in Studio Console
    RefundRequested --> Rejected: Organizer rejects with reason code
    Rejected --> Paid: Order remains active
    OrganizerApproved --> StripeInitiated: API calls Stripe Refund API
    StripeInitiated --> Refunded: Signed charge.refunded webhook confirmed
    StripeInitiated --> RefundFailed: Stripe processing error
    RefundFailed --> OrganizerApproved: Retry refund dispatch
    Refunded --> [*]: Admission tickets invalidated
```

---

## Fair Return Replacement Settlement

Fair return reuses the registration payment and refund aggregates; it does not create a second provider integration. A `WaitlistPaymentIntent` points to the buyer's replacement `PaymentAttempt` and to one capacity-reserving `RefundAttempt` for the original charge. `FairReturnOrchestrationEffect` owns the stable operation UUID, provider idempotency key, bounded retry count, processing fence, and lease. Unknown provider outcomes remain durable and retryable; they never release refund capacity or synthesize success.

Replacement settlement is the hard ordering fence. Persistence records `ReplacementPaymentSettledAt` before it can create the pointer-only `WaitlistRefundIntent` and generic refund-dispatch outbox row in the same transaction. Provider refund I/O happens later through the existing refund dispatcher/reconciler. Replays return the existing stable intent, stale leases can be reclaimed after restart, and a refund cannot be staged from pending, failed, missing, or contradictory replacement evidence.

The waitlist itself never accepts paid priority. Queue order is policy priority descending, enqueue time ascending, then UUID ascending. Commercial substitution requires exact tenant, event, ticket type, catalog, purchase-policy snapshot, currency, commerce digest, entitlement digest, gross minor units, and refund-funding mode equality.

## 9. Developer Guide: Adding a New Payment Provider

To integrate a new payment gateway (e.g. `paypal`, `mollie`, `lemonsqueezy`), follow this step-by-step procedure:

### Step 1: Implement Application Capability Ports
In `src/Explore.Infrastructure/Payments/<ProviderName>/`, create classes implementing the provider-neutral contracts defined in `Explore.Application.Contracts.Payments`:

```csharp
namespace Explore.Infrastructure.Payments.Mollie;

// 1. Merchant Onboarding & Account Management
public sealed class MollieConnectAccountAdapter(
    IHttpClientFactory httpClientFactory,
    ISecretResolver secretResolver) : IOrganizerPaymentOnboardingProvider
{
    public async Task<OrganizerPaymentProviderAccountCreationResult> CreateAccountAsync(
        OrganizerPaymentProviderAccountCreationRequest request,
        CancellationToken cancellationToken)
    {
        // Call provider API to create connected merchant account
    }

    public async Task<OrganizerPaymentOnboardingLinkCreationResult> CreateOnboardingLinkAsync(
        OrganizerPaymentOnboardingLinkRequest request,
        CancellationToken cancellationToken)
    {
        // Generate hosted onboarding URL
    }

    public async Task<OrganizerPaymentProviderReadinessResult> GetReadinessAsync(
        OrganizerPaymentProviderReadinessRequest request,
        CancellationToken cancellationToken)
    {
        // Query provider account capabilities and restrictions
    }
}

// 2. Hosted Checkout & Intent Retrieval
public sealed class MollieCheckoutAdapter(
    IHttpClientFactory httpClientFactory,
    ISecretResolver secretResolver) :
    IHostedCheckoutSessionCreator,
    IHostedCheckoutSessionRetriever,
    IPaymentIntentRetriever,
    IPaymentProviderDescriptor
{
    public PaymentProviderDescriptor Describe() =>
        new("mollie", "OrganizerDirect", "v2");

    public async Task<HostedCheckoutCreateResult> CreateAsync(
        HostedCheckoutCreateRequest request,
        CancellationToken cancellationToken)
    {
        // Create hosted payment session using request.ExternalAccountId and request.ProviderIdempotencyKey
    }

    public async Task<HostedCheckoutRetrieveResult> RetrieveAsync(
        HostedCheckoutRetrieveRequest request,
        CancellationToken cancellationToken)
    {
        // Retrieve session status from provider
    }

    public async Task<PaymentIntentRetrieveResult> RetrievePaymentIntentAsync(
        PaymentIntentRetrieveRequest request,
        CancellationToken cancellationToken)
    {
        // Retrieve charge status from provider
    }
}
```

### Step 2: Implement Webhook Ingestion & Signature Verification
Create an incoming webhook verifier and handler in `src/Explore.Infrastructure/Payments/<ProviderName>/`:
- Verify the unmodified raw payload bytes using the provider's signature header and shared secret.
- Enforce timestamp freshness tolerances to protect against replay attacks.
- Map the provider's payment and account events into the system's idempotent message pipeline.

### Step 3: Register Options & DI Services
1. Add configuration options in `Explore.Infrastructure`:
   ```csharp
   public sealed class MolliePaymentOptions
   {
       public const string SectionName = "Payments:Mollie";
       public string Mode { get; set; } = "Test";
       public string[] AllowedCheckoutHosts { get; set; } = ["www.mollie.com"];
   }
   ```
2. Register secrets in [`SecretDefinitionRegistry.cs`](../../src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs).
3. Bind the adapter implementations in `Explore.Infrastructure/Payments/PaymentServicesRegistration.cs`.

---

## 10. Configuration & Secrets Reference

### Environment & AppSettings Keys

| Key | Environment Variable | Default | Purpose |
|---|---|---|---|
| `Payments:Stripe:Mode` | `PAYMENTS_STRIPE_MODE` | `Test` | Payment execution mode (`Test` or `Live`). |
| `Payments:Stripe:AllowedCheckoutHosts` | `Payments__Stripe__AllowedCheckoutHosts__0` | `checkout.stripe.com` | Whitelisted destination hosts for checkout redirects. |
| `Payments:OrganizerDirect:ProviderCode` | `Payments__OrganizerDirect__ProviderCode` | `stripe` | Active commercial provider code. |
| `Payments:OrganizerDirect:ConnectPlatformId` | `Payments__OrganizerDirect__ConnectPlatformId` | `""` | Non-secret Connect platform identifier. |

### Secrets Resolution (`Explore.Secrets`)

| Secret Key | Registry Binding Key | Scope | Description |
|---|---|---|---|
| `STRIPE_PLATFORM_SECRET_KEY` | `payments.stripe.platform_secret_key` | Instance (Server-only) | Platform secret API key (`sk_test_...` or `sk_live_...`). |
| `STRIPE_WEBHOOK_SECRET` | `payments.stripe.webhook_secret` | Instance (Server-only) | Signing secret for verifying raw webhook signatures (`whsec_...`). |

---

## 11. Configuration Manifest Tenant Payment Boundary

Tenant bootstrap may declare only the provider-neutral narrowing document
`tenant.paid_event_policy`. The document is pinned to the active
`instancePolicyVersion`, validated with `PaidEventPolicyRules`, and applied
through `PaidEventPolicyMutationBoundary` under serializable transaction
isolation in the same transaction as tenant creation and manifest audit.

Allowed values narrow payment enablement, organizer kinds, local verification,
currencies/default currency, refund-protection floors, integer minor-unit and
count risk ceilings, rolling windows, and review thresholds. The manifest
cannot select a provider, supply credentials, name connected accounts, choose
charge ownership, establish operator or official status, accept buyer terms,
execute refunds, control stop/review state, hand work to a provider, or alter
reconciliation. Those authorities remain in the Phase 18C chain:

1. active instance policy;
2. valid tenant narrowing;
3. persisted sale-control/review state;
4. server-authored acceptance;
5. freshness validation;
6. provider handoff and reconciliation.

Both regular policy revisions and manifest bootstrap acquire the same
instance/tenant named locks. A stale instance version or concurrently created
tenant policy returns a retryable conflict and rolls back the entire manifest
transaction. Work already handed to a provider is immutable and is never
rewritten by configuration import.

Exports include no payment secrets or PII. `Overrides` emits only an active
tenant narrowing; `Portable` flattens the active safe policy while explicitly
marking sovereign values omitted and locked. Neither export claims refund,
liability, dispute, or provider behavior.

## 12. Event-Bound Add-On Commerce

Event add-ons are organizer-authored optional commerce items in a separate
versioned catalog. One event catalog may contain multiple items. Every item
owns its immutable name, integer-minor-unit unit price, finite quantity,
fulfillment disclosure, and refund disclosure.

Buyer selection is bounded to the original registration-order checkout:

1. the browser submits catalog item IDs and quantities only;
2. the browser also submits the exact catalog ID it was shown;
3. the server resolves that still-published tenant/event catalog, pins it to
   the order, and rejects a replaced or retired catalog instead of silently
   changing the offer;
4. checked `long` multiplication creates separate add-on line snapshots;
5. a serializable item/order/line fence reserves finite stock;
6. the registration order discloses add-on and grand totals before payment
   handoff.

Add-ons do not introduce a new platform-fee authority. Phase 7 preserves the
order's already-pinned platform fee while adding the add-on amount to the
organizer-directed total. A later fee on add-ons requires an explicit paid
event policy and I-VSD decision; it must not be inferred from ticket fees.
Ticket promotions remain scoped to ticket lines and do not discount add-on
lines unless a later explicit add-on promotion policy is approved.

Refund allocation and provider-confirmed refund are distinct. The add-on API
may report `allocated_pending_provider` after exact quantity/value allocation,
but it never labels that amount succeeded or refunded until the existing
provider reconciliation authority confirms it. Pending allocation does not
release stock for resale. Only a later provider-confirmed refund transition may
release add-on stock. Fulfillment and refund replay are idempotent and never
mutate tickets, participant readiness, credentials, ticket capacity, admission,
or check-in.

Refund allocation requires the exact latest provider-reconciled `Succeeded`
payment ID and amount for the order. Provider failure marks the allocation
failed and reopens refundable quantity. Provider success releases stock
atomically; if stock release cannot complete, the durable
`provider_confirmed_inventory_release_pending` state preserves provider truth
for recovery instead of reverting it to pending.

The add-on allocation, generic `RefundAttempt`, and PII-free refund dispatch
outbox message commit in one serializable transaction. `RefundOperationId`
therefore identifies the authoritative `RefundAttempt`; it is not a browser-
invented provider authority. Dispatch and reconciliation synchronize terminal
provider evidence back into the add-on allocation idempotently.

The catalog response discloses the current maximum selectable quantity and the
order response discloses the current maximum refundable quantity. These are
neutral operational limits, not scarcity-pressure copy.

Specialist-system boundaries remain unchanged: Event does not become the
merchant's accounting, tax, invoice, credit-note, banking, or escrow system.

## 13. Related Documentation & Decisions

- [ADR-022: Paid Event Commerce And Stripe Connect](adr/ADR-022-paid-event-commerce-and-stripe-connect.md)
- [ADR-024: External Business Integrations And Protected Payout Boundaries](adr/ADR-024-external-business-integrations-and-protected-payout-boundaries.md)
- [CONFIGURATION.md](CONFIGURATION.md) - Configuration settings and provider options
- [SECRETS.md](SECRETS.md) - Multi-provider secret management
- [SECURITY-MODEL.md](SECURITY-MODEL.md) - Platform security invariants and privilege boundaries
- [WEBHOOKS.md](WEBHOOKS.md) - Webhook delivery, callback verifications, and operational runbooks
- [I-VSD Paid Event Payments Consultation](../../islamic-value-sensitive-design/consultations/i-vsd-paid-event-payments-consultation.md)
