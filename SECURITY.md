# Security Policy

This document outlines the security protocols and vulnerability reporting guidelines for the ISLAMU Event project. Ensuring the security of our systems is a top priority, and while we work diligently to maintain robust protection, vulnerabilities may still occur. We highly value the community’s role in identifying and reporting security concerns to uphold the integrity of our systems and safeguard our users.

## Reporting a Vulnerability

If you have identified a security vulnerability, submit your findings to [contact@openislamu.org](mailto:contact@openislamu.org). 
Ensure your report includes all relevant information needed for us to reproduce and assess the issue. Include the IP address or URL of the affected system.

To ensure a responsible and effective disclosure process, please adhere to the following:

- Maintain confidentiality and refrain from publicly disclosing the vulnerability until we have had the opportunity to investigate and address the issue.
- Refrain from running automated vulnerability scans on our infrastructure or dashboard without prior consent. Contact us to set up a sandbox environment if necessary.
- Do not exploit any discovered vulnerabilities for malicious purposes, such as accessing or altering user data.
- Do not engage in physical security attacks, social engineering, distributed denial of service (DDoS) attacks, spam campaigns, or attacks on third-party applications as part of your vulnerability testing.

## Out of Scope

While we appreciate all efforts to assist in improving our security, please note that the following types of vulnerabilities are considered out of scope:

- Vulnerabilities requiring man-in-the-middle (MITM) attacks or physical access to a user’s device.
- Content spoofing or text injection issues without a clear attack vector or the ability to modify HTML/CSS.
- Issues related to email spoofing.
- Missing DNSSEC, CAA, or CSP headers.
- Absence of secure or HTTP-only flags on non-sensitive cookies.

## Ticket Purchase Security Invariants

Ticket-purchase limits do not trust browser identity or quantity fields. Tenant identity comes from the active tenant context, authenticated account identity comes from the canonical current-user service, actor context is independently checked against persisted user ownership or current group/organization membership, and quantity is recomputed from persisted order lines.

Durable operation keys are tenant-qualified and fingerprint the server-resolved account/contact authority, actor context, event, order, policy, and quantity. Exact retries replay; changed scope conflicts. Verified-contact enforcement hashes the persisted normalized verified contact before it leaves the authority resolver. Name-only purchases are explicitly order-scoped and are never represented as having a hard cross-order per-person limit.

## Participant Admission Readiness Invariants

- Payment or a confirmed order never grants admission by itself. Credential issuance and check-in both call the same tenant-qualified readiness authority.
- Adult completion is accepted only from the participant's linked account and canonical finalized requirement submissions. Purchaser-provided provisional data is never promoted to adult consent.
- Consent remains an immutable canonical `RegistrationConsentRecord`; readiness stores only its identifier and bounded timestamps, never answer values, names, contact data, or consent text.
- Organizer approval and revocation resolve the operator actor server-side. Revocation is terminal and invalidates an issued active admission ticket in the same local transaction.
- Completion, approval, revocation, issuance, and check-in serialize on the same assignment fence. Cross-tenant and cross-order identifiers resolve to the same bounded unavailable outcome.
- The exact-resource read accepts only authenticated subject/organizer authority or the opaque order capability. Missing, malformed, expired, wrong-resource, and wrong-tenant capability values are indistinguishable, are never echoed, and return private/no-store ProblemDetails.
- Browser writes cross the cookie-authenticated BFF with antiforgery and partitioned rate limiting. The browser never supplies tenant, subject, operator, readiness, payment, or lifecycle authority; action controls render only from `complete-participant-readiness`, `approve-participant-readiness`, and `revoke-participant-readiness` HAL relations.

## Ticket Transfer And Credential Rotation Invariants

- Transfer policy is pinned to the ticket catalog version and ticket type. The server enforces enablement, offer lifetime, event cutoff, maximum hops, current holder, check-in state, and one-open-offer uniqueness.
- Offer, acceptance, cancellation, correction, recovery reissue, revocation, and check-in serialize in canonical assignment → eligibility → ticket → transfer order. Acceptance changes the holder once and rotates the credential generation; a stale credential can never regain authority.
- Order, purchaser, order line, amount, currency, payment, refund, and append-only check-in lineage remain immutable across transfer. Holder identity is a subject reference, not copied participant PII.
- Claim capabilities are high-entropy, exact-resource, expiring, single-use bearer values. Persistence retains only their digest; missing, malformed, consumed, expired, wrong-tenant, and wrong-resource values return the same generic unavailable response.
- A capability travels only in `X-Ticket-Transfer-Capability`. It never enters a URL, log, trace, metric, ProblemDetails body, HAL link, or persisted plaintext field. Claim capability and replacement credential plaintext are returned once and generated diagnostic records redact all members.
- Browser writes require cookie authorization, antiforgery before partitioned rate limiting, and independent API authorization. Transfer controls render only when the matching server HAL relation is present.

## Ticketing Restore And Operator-Control Invariants

- A restored database never reopens ticket writes by itself. Runtime begins in
  recovery-only mode and validates one tenant-qualified consistency manifest.
- Release/schema revision, retained key inventory, authority floor, provider
  cursor, durable idempotency floor, and worker fence all fail closed when
  absent, stale, mixed, or cross-tenant.
- Pre-restore transfer, waitlist, and recovery capabilities are cancelled.
  Active admission credentials are revoked and replaced through one durable,
  digest-free reissue intent per ticket before reopening.
- In-flight provider work becomes `Unknown`; stale workers lose through the
  advanced fence. Retry/dead-letter requires authenticated operator action and
  authoritative provider evidence.
- Recovery HMAC material comes only from Infisical or environment. Manifests,
  health, logs, metrics, traces, ProblemDetails, and support output never expose
  key material, bearer values, digests, provider objects, payment amounts, or
  tenant/user identifiers.
- Workers reopen before sales and only at the exact rotated fence. Sales remain
  closed while credential reissue or provider ambiguity is unresolved.

## Our Commitment

At ISLAMU Event, we are committed to maintaining transparent and collaborative communication throughout the vulnerability resolution process. Here's what you can expect from us:

- **Response Time** <br/>
We will acknowledge receipt of your vulnerability report within an undefined amount of business days as we are Still Students! and provide an estimated timeline for resolution.
- **Legal Protection** <br/>
We will not initiate legal action against you for reporting vulnerabilities, provided you adhere to the reporting guidelines.
- **Confidentiality** <br/>
Your report will be treated with confidentiality. We will not disclose your personal information to third parties without your consent.
- **Recognition** <br/>
With your permission, we are happy to publicly acknowledge your contribution to improving our security once the issue is resolved.
- **Timely Resolution** <br/>
We are committed to working closely with you throughout the resolution process, providing timely updates as necessary. Our goal is to address all reported vulnerabilities swiftly, and we will actively engage with you to coordinate a responsible disclosure once the issue is fully resolved.

We appreciate your help in ensuring the security of our platform. Your contributions are crucial to protecting our users and maintaining a secure environment. Thank you for working with us to keep ISLAMU Event safe.
