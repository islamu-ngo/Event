<!-- ABOUTME: I-VSD planning assessment for ASP.NET Core Identity embedded authentication provider. -->
<!-- ABOUTME: Evaluates data sovereignty, credential stewardship, abuse defense, and transparent provider selection. -->

# ASP.NET Core Identity Authentication Provider — I-VSD Planning Assessment

Last Updated: 2026-09-04 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: ASP.NET Core Identity embedded JWT authentication provider
- Workstream: `aspnet-identity-auth`
- Report kind: provider-responsibility implementation assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-04
- Reviewed input revision: plan `cc5c3c4f408f13e18f1fd4b2a464f728986b519d599089a5e352dad9401dd3a8`; context `625b57fa4d6d2463d27788500d9162b2041731c03889dd75bd62265b6c5e414d`; tasks `ec08e4f2653263a442a851fc61fa0ce88b9c64dd396f8ac9ebefb7a697b1b46d`
- Reviewed plan triad revision: the three SHA-256 digests above, representing the CTO-revised pre-approval content
- Implementation evidence: pending implementation
- Supersedes: none
- **Revalidation triggers evaluated:**
  1. Runtime provider switching changes provider authority (operator can change without restart)
  2. Two-axis multi-provider coexistence changes user defaults/consent model (ATProto independently toggleable)
  3. Session continuity during transitions changes failure behavior
- **Revalidation outcome:** `IVSD-F005` and `IVSD-M005` were added, `IVSD-F004` was expanded for the two-axis provider model, and all finding-to-scenario/task mappings were checked against the revised plan.

## Scope

This assessment covers the provider-controlled design of an embedded authentication provider implemented using ASP.NET Core Identity, issuing and validating JWT tokens for standalone, single-container, and lightweight deployments. It evaluates the addition of `AUTHENTICATION_PROVIDER=local` alongside existing Keycloak (OIDC) and ATProto (OAuth) options, the persistence topology (`IDENTITY_DATABASE_TOPOLOGY=colocated` in primary `ExploreDbContext` by default, or `external` for dedicated databases), the onboarding and admin configuration experience, user credential storage, rate limiting, and user aggregate synchronization.

## Claim Boundary

This is provider-responsibility design reasoning, not a fatwa, Sharia certification, legal opinion, or proof that the resulting deployment is ethically sufficient. The report evaluates choices under the platform maintainer's control: identity sovereignty, password stewardship, abuse prevention, operator clarity, and failure behavior. No halal/haram or contested religious-legal conclusion is made.

## Findings

### IVSD-F001 — Data Sovereignty, Zero Infrastructure Barriers & Self-Contained Deployment

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** provider-controlled equity, inclusivity, and stewardship
- **Principle/domain:** amanah (custody & trust), 'adl (justice & equitable access), self-determination
- **Stakeholders:** self-hosters, small grassroots organizations, mosques, non-profits, air-gapped deployments
- **Provider-controlled decision:** whether to require external enterprise servers (Keycloak), external public HTTPS domains (ATProto OAuth), or separate databases for user authentication
- **Context:** While Keycloak provides enterprise federation and ATProto provides decentralized zero-infrastructure authentication, both introduce hard prerequisites: Keycloak demands substantial server RAM and container orchestration, while ATProto OAuth rejects non-HTTPS domains outside localhost. Moreover, forcing an auxiliary database for authentication introduces operational complexity for single-container self-hosters.
- **Risk:** Excluding resource-constrained organizations or forcing them to route user authentication through external commercial identity infrastructures compromises local autonomy and data custody.
- **Mitigation (IVSD-M001):** Introduce a built-in, lightweight ASP.NET Core Identity authentication provider (`AUTHENTICATION_PROVIDER=local`). Persist identity tables directly in the primary database (`ExploreDbContext`) by default (`colocated`), while providing an optional `external` topology for enterprise operators with existing identity databases.
- **Rejected alternatives:** Requiring SQLite-backed Keycloak containers (heavy, complex orchestration for simple sites); forcing a separate database context for local deployments when the self-hoster already selected a primary database (causes unnecessary operational friction).

### IVSD-F002 — Credential Stewardship & Cryptographic Rigor

- **Lifecycle:** accepted
- **Severity:** critical
- **Claim type:** provider-controlled privacy, data integrity, and security
- **Principle/domain:** amanah (entrusted credentials), hifdh al-mal wa al-'ird (protection of property and dignity)
- **Stakeholders:** all registered users, system operators
- **Provider-controlled decision:** password hashing algorithm, salt generation, secret isolation, and credential exposure across layers
- **Context:** Storing user credentials locally shifts the security boundary from external IDPs onto the application database.
- **Risk:** Weak hashing, accidental credential exposure in logs or DTOs, or hardcoded signing keys could lead to credential theft and mass account compromise.
- **Mitigation (IVSD-M002):** Enforce ASP.NET Core Identity's standard `PasswordHasher<ApplicationUser>` (PBKDF2 HMAC-SHA512 with per-user cryptographic salt and high work factor). Isolate credentials into distinct `identity_users` tables rather than merging into the PII-separated domain `User` table. Mark password properties write-only in DTOs. Never return password hashes in API responses, logs, or traces. Resolve JWT signing keys strictly from environment secrets (`AUTHENTICATION_LOCAL_JWT_KEY` / Infisical) and reject default placeholder keys in non-development environments.
- **Rejected alternatives:** Merging password hashes into domain `User` entity (breaks the 1:1 `UserPii` privacy architecture); hand-rolled hashing (insecure).

### IVSD-F003 — Account Integrity & Abuse Defense

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** provider-controlled abuse resistance and failure safety
- **Principle/domain:** raf' al-haraj (prevention of harm), 'adl (fairness)
- **Stakeholders:** system users, platform operators
- **Provider-controlled decision:** brute-force defense, lockout policies, rate limiting, and credential verification latency
- **Context:** Public authentication endpoints (`/auth/local/login`, `/api/auth/local/login`) are prime targets for automated credential stuffing and dictionary attacks.
- **Risk:** Denial of service, account lockouts of legitimate users, or successful unauthorized access via brute force.
- **Mitigation (IVSD-M003):** Apply ASP.NET Core Identity's built-in lockout mechanism (`LockoutOnFailure` with configurable attempts and duration). Apply ASP.NET Core fixed/sliding window rate limiting policies (`write` rate limiter) to all local auth endpoints. Return uniform error messages (RFC 7807 ProblemDetails) that do not leak whether an email exists or whether only the password was incorrect.
- **Rejected alternatives:** Unlimited retry attempts (vulnerable to dictionary attack); verbose account enumeration error messages (exposes user existence).

### IVSD-F004 — Transparent Two-Axis Provider Selection & Domain Synchronization Integrity

- **Lifecycle:** accepted
- **Severity:** medium
- **Claim type:** provider-controlled transparency and architectural correctness
- **Principle/domain:** sidq (truthfulness), amanah (consistency)
- **Stakeholders:** instance administrators, onboarding operators, authenticated users
- **Provider-controlled decision:** deployment configuration precedence, independent ATProto availability, onboarding UI presentation, and synchronization into the domain `User` aggregate
- **Context:** The revised architecture has two axes: exactly one primary credential provider (`local` XOR `keycloak`) plus an independent ATProto login toggle. Every authenticated identity must synchronize deterministically into domain `User`, `UserPii`, and `Actor` aggregates without collision.
- **Risk:** Presenting ATProto as mutually exclusive, allowing both primary credential providers to appear active, silently falling back between providers, or creating orphan identity records would misrepresent operator configuration and user authority.
- **Mitigation (IVSD-M004):** Model `AUTHENTICATION_PROVIDER` as `local` or `keycloak`, model `ATPROTO_LOGIN_ENABLED` independently, reject contradictory primary-provider state, disclose every available login path in onboarding and login UI, and synchronize local users through `SyncUserCommandHandler` with `AuthProvider = "local"` and an authority-qualified account key.
- **Rejected alternatives:** A single enum containing ATProto (cannot express coexistence); implicit provider fallback (violates fail-closed security invariants); divergent domain user models (violates Clean Architecture and tenant isolation).

### IVSD-F005 — Runtime Provider Transition Safety

- **Lifecycle:** accepted
- **Severity:** critical
- **Claim type:** provider-controlled continuity, recoverability, and access stewardship
- **Principle/domain:** amanah (entrusted access), raf' al-haraj (prevention of avoidable harm), sidq (truthful transition state)
- **Stakeholders:** instance administrators, currently authenticated users, self-hosting operators
- **Provider-controlled decision:** whether provider switching invalidates sessions, permits administrator self-lockout, or requires a restart-based recovery path
- **Context:** The revised plan allows an administrator to switch the primary provider at runtime while Keycloak- or Local Identity-issued sessions remain active.
- **Risk:** Disabling the previous bearer scheme or switching before the administrator has target-provider credentials could invalidate all active sessions and leave the instance administratively inaccessible.
- **Mitigation (IVSD-M005):** Always register both bearer validation schemes; make the runtime setting control only new-login discovery; require verified target-provider administrator credentials before switching; preserve old-provider sessions until natural expiry; invalidate only the provider-selection cache; and document deployment-environment rollback as an operator recovery path.
- **Rejected alternatives:** Removing the inactive validation scheme (breaks continuity); automatic cross-provider credential linking (creates takeover risk); unconditional switching with restart-only recovery (avoidable operator harm).

## Recommendations

1. Implement all five accepted mitigations as mandatory acceptance criteria, with `IVSD-M005` treated as a release-blocking security invariant.
2. Keep local credential storage instance-scoped and tenant authorization separate through the existing synchronized domain user and tenant-grant model.
3. Document HMAC key rotation and emergency provider override without presenting operational guidance as proof of ethical or security sufficiency.

## Stakeholders

- Self-hosters and small community organizations needing a low-resource deployment.
- Users entrusting credentials and relying on stable authenticated sessions.
- Instance administrators responsible for provider selection and recovery.
- Enterprise operators using Keycloak or a separately hosted Identity database.

## I-VSD Principles And Domains

- **Amanah:** credential custody, signing-key custody, and continuity of entrusted access.
- **'Adl:** equitable access for resource-constrained communities and consistent treatment across providers.
- **Sidq:** truthful provider presentation and unambiguous authentication authority.
- **Raf' al-haraj:** avoidance of preventable lockout, session loss, and abuse harm.

## Validation Gaps

- Implementation evidence is pending for real lockout behavior, scheme isolation, cache invalidation, and provider-switch session continuity.
- Operational usability evidence is pending for the onboarding and emergency rollback flows.
- No stakeholder usability study has yet validated that the two-axis model is understood without assistance.

## Escalation Needed

No scholarly or religious-legal escalation is required for the current technical scope. Legal and security review remain required for any future third-party identity dependency or externally sourced implementation material.

## Evidence Reviewed

- CTO-revised `aspnet-identity-auth-plan.md`, digest recorded in Review Metadata.
- CTO-revised `aspnet-identity-auth-context.md`, digest recorded in Review Metadata.
- CTO-revised `aspnet-identity-auth-tasks.md`, digest recorded in Review Metadata.
- `aspnet-identity-auth-cto-review.md`, including the transition-safety failure analysis.
- Repository authentication, BFF, configuration, persistence, and security invariants cited by the plan.

## Missing Evidence

- Runtime test results and Tier 1 invariant-breaker evidence.
- Rendered accessibility and provider-disclosure evidence from the Blazor flows.
- Operator recovery exercise demonstrating environment override and cache behavior.

## Context Inventory

- Provider-mediated capability: embedded credentials, JWT issuance, provider discovery, switching, and persistence topology.
- Provider authority: deployment operator and authenticated instance administrator.
- Data in scope: password hashes, identity metadata, JWT signing keys, domain user links, and role claims.
- Out of scope: religious rulings, claims of Sharia compliance, and full parity features deferred to backlog.

## Planning Handoff

| Finding ID | Mitigation ID | Target Scenario | Target Tasks | Disposition |
|---|---|---|---|---|
| `IVSD-F001` | `IVSD-M001` | Scenario 3.1A, Scenario 3.1B | Tasks 1.1, 1.2, 2.1, 2.2 | Plan-aligned |
| `IVSD-F002` | `IVSD-M002` | Scenario 3.2A, Scenario 3.2B | Tasks 2.1, 2.3, 4.1 | Plan-aligned |
| `IVSD-F003` | `IVSD-M003` | Scenario 3.3A, Scenario 3.3B | Tasks 4.2, 5.1 | Plan-aligned |
| `IVSD-F004` | `IVSD-M004` | Scenario 3.4A, Scenario 3.4B | Tasks 1.3, 3.2, 6.1, 7.1 | Plan-aligned |
| `IVSD-F005` | `IVSD-M005` | Scenario 3.5A, Scenario 3.5B, Scenario 3.7A | Tasks 4.5, 4.6, 5.5 | Plan-aligned |

## Common Overlooked Failures And Outcomes

- A provider switch that changes token validation rather than login discovery can evict every active user and the initiating administrator.
- A local JWT routed by issuer but validated without an issuer-specific audience and signing key can create token confusion across schemes.
- Treating ATProto as a primary-provider enum value hides valid coexistence and misstates user choice.
- Co-located credentials omitted from normal backup guidance can create an unexpected account-loss boundary for standalone operators.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-03 | draft | stale | CTO materially rewrote provider authority, coexistence, and transition behavior | CTO review and revised plan triad |
| 2026-09-04 | stale | current | Revalidated affected findings and mappings against the revised triad | SHA-256 revisions in Review Metadata |
