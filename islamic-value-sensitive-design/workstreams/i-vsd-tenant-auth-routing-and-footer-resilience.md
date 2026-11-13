# I-VSD: Tenant Authentication Routing, Registration And Footer Resilience

Last Updated: 2026-11-05 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject / Workstream: tenant-auth-routing-and-footer-resilience
- Report kind: implementation-plan-review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-05
- Reviewed input: design ER-20261105-1 on develop b6ad1594b7f92ac3399a5a8bf8d241e4f8d69c66 and the working-tree artifacts below.
- Supersedes: the 2026-11-03 revision of this same report, whose current/plan-aligned label did not establish complete server enforcement.
- Decision authority: user's default-open registration, unconditional useful footer, public browsing/private option, global auth routes/tenant affinity and explicit execution-ready/breaking-change direction.
- Binding: [plan](../../dev/active/tenant-auth-routing-and-footer-resilience/tenant-auth-routing-and-footer-resilience-plan.md), [tasks](../../dev/active/tenant-auth-routing-and-footer-resilience/tenant-auth-routing-and-footer-resilience-tasks.md), [context](../../dev/active/tenant-auth-routing-and-footer-resilience/tenant-auth-routing-and-footer-resilience-context.md). Material findings were re-evaluated after the independent source/plan reviews; the hashes below bind this assessment to the final design, not merely its date.
- Plan SHA-256: 72722d00e559d077e6e3e012fbc5bbcfbafbce7f83d72598c267709d1419a42c
- Tasks SHA-256: 0a0cf6a2d3ecad39bbcf1f13ebb0a8d8a17071bd7955800b28ebef4259a22b5b

## Executive Summary

The design preserves public access to beneficial community events while giving operators an honest authentication requirement and control over new tenant enrollment. Access checks belong at server data boundaries, not only at navigation links. Closing signup must not strand existing users, and incomplete legal identity must not remove general navigation or cookie controls.

The rebaseline corrects the earlier report's overclaim that an AuthenticatedOnly option creates a vetted private community: any eligible authenticated principal may pass that gate, while tenant/resource ACLs remain independently authoritative. A stronger members-only promise is outside this requirement and cannot be inferred.

The plan also makes no-email Local signup practical without weakening password policy or claiming email verification. Separate-store identity activation remains pending until primary admission is committed; failures are recoverable rather than concealed. Origin-bound handoff does not require an ATProto-only key on Local deployments.

Final review grounded that design in the actual native credential ledger and writer-permission installer, removed SMTP policy as ordinary session authority, and specified both callback-origin browser proofs. Local/Keycloak retain their existing authentication authorities; targetless ATProto uses restricted verification state before a tenant session. Direct Google is retired in favor of the existing Keycloak broker because the API has no corresponding direct-token validator. Historical identity bindings are preserved without email-based relinking.

Plan alignment concerns the stated design and mitigation ownership. It is not evidence that private reads, credential recovery or accessibility work in a running deployment. Each finding remains accepted-with-required-verification until its implementation tests pass.

## Scope

Seven-phase implementation of server visitor privacy, tenant registration policy, Local/federated enrollment, footer data/rendering, root and auth continuity, and final qualification. All original requirements remain in the same triad. No compatibility shim, credential reset, production deployment or unrelated session/migration repair is authorized by this report.

## Claim Boundary

This is a provider-responsibility design assessment, not a scholarly ruling or legal certification. Amanah (trust), truthful communication, ease of use and protection from avoidable disclosure guide the engineering choices. We do not infer vetted membership from authentication, successful signup from a link, verified email from an optional address, or runtime correctness from a checked planning box.

## Findings

### IVSD-F001 — Public access must not become an accidental account wall

Lifecycle: accepted. Severity: high. Claim: design risk. Principle/domain: ease, equitable participation and autonomy in public discovery. Stakeholders: unauthenticated visitors and community operators. Provider decision: default public tenant browsing and single-tenant home, versus login at an untargeted multi-tenant root.

A universal login-first route would exclude visitors from public event information despite the operator choosing public access. The plan's S01–S05 keep explicit public tenant pages accessible while providing a useful global authentication entry. Source evidence: current StartupRoutingService/HomeStart and server tenant resolution. Mitigation IVSD-M001: implement the complete route matrix and explicit-target precedence in Phases 1/6; validate through native and component contracts. No religious/legal escalation is required for this engineering distinction.

### IVSD-F002 — Authenticated-only is a real data boundary, not vetted membership

Lifecycle: accepted. Severity: critical. Claim: confidentiality design requirement. Principle/domain: trust and protection from unwanted exposure. Stakeholders: event subjects, operators, anonymous readers and authenticated users. Provider decision: which data can be read and when revocation takes effect.

The earlier client-focused plan could redirect a browser while alternate API/export/cache reads remained exposed. S06–S08 require live server admission, non-HTTP query policy, public discovery filtering and no stale-cache bypass. Authentication is not a promise of approved membership. Mitigation IVSD-M002: Phase 1 direct-request/warm-cache/export/MCP tests and truthful privacy timing. Evidence: lifecycle middleware protects lifecycle only; VisitorAccessCapability governs new participation. Already-downloaded public content and previously admitted reads cannot be recalled; disclose this limit. Escalate any later proposal to claim members-only privacy without an actual membership rule.

### IVSD-F003 — Registration closure must be effective and must preserve existing access

Lifecycle: accepted. Severity: high. Claim: user-agency and authorization risk. Principle/domain: honest affordances, operator stewardship and fair existing-user access. Stakeholders: newcomers, existing account holders, suspended members and operators. Provider decision: global identity creation versus target tenant membership.

S09–S12 tie new self-service account creation to explicit intended-tenant admission; untargeted signup selects a community without enumerating private tenants. Closing registration cannot invalidate an existing account or silently reactivate a removed membership. Source evidence: operator-only Local creation, provider JIT, separate subject onboarding and the signup-dependent capability predicate. Mitigation IVSD-M003: Phases 2–4 common final policy, no-email Local signup, explicit join, uniqueness/idempotency and close/enroll races. External provider verification never becomes automatic consent to all tenant memberships. Refresh this assessment if account/global-enrollment defaults change.

### IVSD-F004 — Footer resilience must not fabricate accountability

Lifecycle: accepted. Severity: high. Claim: accessibility/truthfulness design requirement. Principle/domain: dignity, clear communication and accountable operation. Stakeholders: visitors, white-label operators and people using cookie/accessibility controls. Provider decision: whether general public utility depends on complete legal identity.

Omitting all navigation because a legal record is incomplete penalizes visitors and obscures cookie controls. Copying tenant branding into a missing accountable-operator role would instead invent a legal assertion. S13–S14 separate minimum safe presentation from optional independently validated roles and prevent tenant A's late response appearing in tenant B. Mitigation IVSD-M004: Phase 5 existing footer resource, generation-bound cache, minimal render and role-separated contract/component tests. Existing legal/publication readiness remains authoritative elsewhere. Escalate any change that misrepresents operator status or substitutes a different party's legal identity.

### IVSD-F005 — Workspace continuity must preserve authority and destination intent

Lifecycle: accepted. Severity: high. Claim: navigation/session privacy risk. Principle/domain: trust, understandable failure and user control. Stakeholders: people using multiple communities or custom domains. Provider decision: last-active preference, authentication destination and failure messages.

A stored tenant preference is not membership authority. A relative path alone does not identify a custom-domain destination, and a host-scoped cookie cannot simply be shared across sites. S03–S05/S15–S16 reuse active membership fallback and exact bound continuation. Mitigation IVSD-M005: Phase 6 verified origin, original browser proof, session-bound single-use handoff, no arbitrary redirect or shared Domain cookie, and distinct lookup-unavailable/no-membership behavior. Source evidence: navigation handler, BffReturnUrlService and ATProto proof/handoff precedent. Refresh on changes to origin trust or cross-domain/session custody.

Phase 4/S12 also requires narrow exact-account bootstrap and restricted tenantless ATProto verification; a verification receipt cannot become a general API token. Fixed cross-origin callbacks establish independent source and destination browser proofs before OAuth, and each host validates its own cookie. Final expiry/routing/account checks occur after awaited work before session issuance. Direct Google removal preserves the old namespace as stored data only; brokered Google is a distinct Keycloak identity. This prevents convenience routing from silently changing account authority.

### IVSD-F006 — Credential intake and recovery must remain fail-closed and usable without email

Lifecycle: accepted. Severity: critical. Claim: identity integrity and data-minimization risk. Principle/domain: custody, truthful recovery and minimum personal data. Stakeholders: new Local users, account owners, operators and erased/suspended identities. Provider decision: password storage, contact-email requirements and uncertain cross-store settlement.

Reusing administrator temporary credentials for public signup would cross an authority boundary. Claiming cross-store atomicity would conceal partially committed identity state. The decided protocol retains ordinary username/password login, makes optional email unverified, and keeps prepared credentials ineligible until primary admission. Recovery cannot adopt an unrelated account by username or resurrect erased membership. Mitigation IVSD-M006: Phases 3/4/6 purpose-bound native custody, receipt/crash/replay/race tests, existing calibrated password policy, no plaintext or credential telemetry, protected BFF intake and pending-state recovery. No-email users must receive truthful recovery guidance; do not pretend mail recovery is available.

The final design uses VisitorCreate in the existing native credential operation ledger and preserves its eligibility foreign key. Only exact primary admission permits direct Ready completion; no temporary administrator password or fabricated verified email. Ordinary login/current-session validation is independent of email delivery, while trusted-email recovery retains verification. Maintenance reads bounded receipt metadata and reacquires target-bound selected writer custody; the privileged permission installer upgrades only the exact known profile non-destructively and rejects unknown drift. These details are required mitigation ownership, not evidence the transitions already work.

## Recommendations

Implement the accepted mitigations in their owning slices; do not lower them to UI-only tests. Stable legacy labels IVSD-01..05 map respectively to F001..F005; checked historical planning intentions are superseded by explicit accepted findings and their validation owners.

Rejected alternatives:
- Client route guards as privacy enforcement: bypassable through direct data reads.
- Empty signup links as admission policy: allows alternate enrollment paths and strands existing accounts.
- A tenant membership grant on every visit or external login: lacks explicit consent and may reactivate a barred member.
- Blocking the whole footer on identity readiness: hides general utility; spoofed legal fallbacks are also rejected.
- Shared parent-domain cookies or arbitrary redirect URLs: weaken origin/session boundaries.
- Renaming the ATProto transient bridge into a generic bridge: adds ATProto credentials to Local-only operation.
- A distributed-transaction claim for separate stores: unsupported; pending eligibility and durable reconciliation are required.
- Compatibility aliases: not justified during pre-release development.

## Stakeholders

Unauthenticated visitors; existing/new Local and federated account holders; community operators; instance administrators; people with events or attachments in private tenants; users of assistive technology and cookie controls; self-hosters operating without SMTP; people with removed/suspended/erased accounts.

## I-VSD Principles And Domains

Trust/custody applies to accounts, transient session material and membership. Truthful communication applies to signup status, legal disclosure and failure states. Ease/accessibility applies to public browsing and the minimum footer. Protection against avoidable disclosure applies to server admission, cache/index visibility and cross-origin identity transport. These are bounded provider choices, not a claim that the design exhausts Islamic ethical or legal duties.

## Validation Gaps

No product build, test, exploit, runtime login, provider matrix or accessibility audit ran during this planning pass. Existing SQLite/session/fixture blockers remain recorded separately. Accepted mitigations become operational evidence only after their named security/native/component tests and final provider gates pass.

## Escalation Needed

None for the explicit current engineering scope. Re-open planning for members-only promises, different global account defaults, removing password/erasure policy, broadening a BFF secret/cookie boundary or legal-status claims. Scholarly/legal conclusions require qualified authority and are not supplied here.

## Evidence Reviewed

Repository files and Tavily/Context7 research locators are recorded in plan §2 and context §3. This report evaluates the resolved ER-20261105-1 behavior, scenarios S01–S16, phases 1–7 and atomic provider/credential/bootstrap/handoff packets. Independent final reviews returned READY after the source-grounded corrections above; their anonymized receipts are in context. Final reviewed-content hashes are in metadata. Research retained functional facts only, no copied external implementation or additional dependency.

## Missing Evidence

Implementation and live behavior, by design; they are execution gates, not planning facts. Provider-specific callback provisioning is operator configuration, not implicit readiness. A successful planning review does not establish successful Local-only host startup.

## Context Inventory

Original user product direction, 2026-11-05 execution-ready request, current repository evidence, Tavily-extracted OWASP constraints, Context7 framework guarantees, complete replacement plan/tasks and independent review inputs. Prior 2026-11-03 report assumptions are retained here as superseded intentions rather than fabricated implemented controls.

## Common Overlooked Failures And Outcomes

Direct export/media/cached reads; closing signup breaking existing participation; public discovery snapshots retaining private metadata; verifying email by administrator-default mistake; duplicate enrollment on replay; activating a credential before primary commit; orphan pending identities; callback origin remapping; wrong-browser redemption; legal role substitution; stale tenant cache; treating lookup failure as no membership. Each maps to S06–S16 and a specific implementation slice.

## Planning Handoff

- Workstream: tenant-auth-routing-and-footer-resilience.
- Status: current / plan-aligned for the content hashes above; implementation/security qualification remains required.
- Mitigations: F001->M001->Phases 1/6; F002->M002->1; F003->M003->2–4; F004->M004->5; F005->M005->4/6; F006->M006->3/4/6.
- Escalations before implementation: none under the decided scope; material changes trigger revalidation.
- Refresh triggers: registration/account/member defaults, read scope/timing, origin/credential/session authority, footer legal roles, recovery, telemetry or the scenario/task mappings.

## Review Lifecycle

| Date | Previous | New | Trigger / evidence |
|---|---|---|---|
| 2026-11-03 | draft | historical current/plan-aligned | Earlier plan, incomplete server/enrollment scope. |
| 2026-11-05 | historical current | stale | Source-grounded CTO review found missing authority and fallback contracts. |
| 2026-11-05 | stale | draft | ER-20261105-1 resolves server privacy, enrollment, recovery, footer and origin contracts; final content binding pending. |
| 2026-11-05 | draft | current / plan-aligned | Re-evaluated F001–F006 against final source-grounded design, recorded both READY rechecks and bound final plan/tasks hashes; no implementation or runtime proof claimed. |
