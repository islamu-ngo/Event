<!-- ABOUTME: I-VSD planning review for Stateless Payment Checkout Tickets. -->
<!-- ABOUTME: Evaluates provider responsibility for infrastructure independence, community self-hosting autonomy, financial integrity, and privacy. -->

# I-VSD Stateless Payment Checkout Tickets Planning Review

Last Updated: 2026-09-05 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: Eliminating mandatory Redis infrastructure dependency for payment-checkout tickets across all hosting presets
- Workstream: `stateless-payment-checkout-tickets`
- Report kind: implementation-planning review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-05
- Evidence-packet revision: Git commit `b9c3bfaea`
- Reviewed input revision: `dev/active/stateless-payment-checkout-tickets/stateless-payment-checkout-tickets-plan.md`
- Approval boundary: Technical and architectural review only. Qualified scholarly/legal authorities own formal religious and regulatory conclusions.
- Refresh trigger: Any material change to payment redirect security, cookie protection, session binding, or deployment topology requirements.

## Scope

This report evaluates provider-controlled design choices for making payment checkout tickets completely stateless, eliminating the mandatory Redis requirement in Split deployment presets:
- Removing infrastructure barriers (mandatory Redis container) for small community self-hosters and non-profit organizers;
- Preserving transaction reliability and financial safety during attendee registration checkouts;
- Cryptographic protection of checkout destinations against open redirects, interception, and tampering;
- Idempotency and anti-double-charge guarantees during payment redirects.

## Claim Boundary

This is Islamic Value-Sensitive Design (I-VSD) reasoning concerning provider responsibility, stewardship (*amanah*), equity (*‘adl*), and harm prevention (*daf‘ al-darar*) in open-source software architecture. It is not a formal fatwa, Sharia ruling, legal opinion, or payment-service compliance audit.

## Findings

### IVSD-F001 — Unnecessary Infrastructure Complexity Hinders Community Self-Hosting Autonomy (*Raf‘ al-Haraj*)
- Lifecycle: accepted
- Severity / claim type: Medium / provider-responsibility design
- Principle and domain: Removal of unnecessary burden (*raf‘ al-haraj*), communal empowerment, resource stewardship; self-hosting operations
- Stakeholders: Small community centers, mosques, low-resource non-profits, volunteer sysadmins
- Provider-controlled decision: Requiring Redis even for single-replica or lightweight Split deployments just to facilitate a 5-second HTTP redirect.
- Evidence: In `BlazorHostServiceCollectionExtensions.cs`, `RequiresRedis: profile == BlazorHostProfile.Split` forces Redis dependency. Without Redis, checkouts fail closed with HTTP 503.
- Mitigation: `IVSD-M001` — Adopt a stateless cryptographic ticket embedded within the Data Protection cookie, removing per-ticket storage. Replicas still need compatible shared keys; Redis remains an operational dependency when selected as the key-ring authority.

### IVSD-F002 — Financial Transaction Hand-off Must Guard Against Disruption and Deception (*Amanah & Gharar*)
- Lifecycle: accepted
- Severity / claim type: High / financial integrity & trust
- Principle and domain: Trustworthiness (*amanah*), elimination of deception (*gharar*), prevention of harm (*daf‘ al-darar*); payment security
- Stakeholders: Event attendees paying for registrations, event organizers expecting reliable revenue
- Provider-controlled decision: How payment checkout target URLs are verified, protected, and redirected.
- Evidence: `BffRegistrationPaymentEndpoints.cs` validates destination against `Payments:Stripe:AllowedCheckoutHosts` and binds the request to tenant, event, order, and browser session digest.
- Mitigation: `IVSD-M002` — Maintain Data Protection's authenticated encryption (default AES-256-CBC plus HMACSHA256), audience binding, dedicated checkout-session digest binding, short 5-minute TTL, and host allowlist validation within the stateless ticket.

## Mitigations

### IVSD-M001 — Stateless Cryptographic Ticket Design
Embed the resolved target URL directly into the authenticated and encrypted `TicketPayload` protected by ASP.NET Core Data Protection. Eliminate per-ticket Redis Lua scripts and in-memory dictionaries. The ticket path no longer depends on a database provider or hosting profile; multi-replica operation additionally requires shared Data Protection keys and the same application name.

### IVSD-M002 — Fail-Closed Cryptographic and Browser Protections
Ensure that tampering with the ticket payload, altering destination URLs, navigating from cross-site origins, or replaying expired tickets triggers immediate, fail-closed rejection (`HTTP 400`/`404`) without leaking order or financial details. Immediately expire/delete the ticket cookie upon redirect (`Max-Age=0`).

Deletion does not provide server-side single-use or revoke copied tickets. A copied
ticket plus its matching checkout-session cookie can revisit the same hosted URL
until expiry; replacement issuance does not revoke older copies. Navigation never
creates or settles payments. API order authorization, provider session rules,
signed webhook ingestion, and reconciliation remain the financial authorities.

## Planning Handoff

- Workstream: `stateless-payment-checkout-tickets`
- Status: current
- Disposition: plan-aligned
- Reviewed input revision: `b9c3bfaea`
- Findings and mitigations: `IVSD-F001` -> `IVSD-M001`, `IVSD-F002` -> `IVSD-M002`
- Required plan mappings: Mapped to Section 3 Requirements and Phases 1–3 in `stateless-payment-checkout-tickets-plan.md`.
- Refresh triggers: Material changes to ticket encryption or payment routing security.
