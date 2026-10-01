# Event API Security Remediation - Planning Assessment

Last Updated: 2026-10-01

## Review Metadata

- Mode: planning
- Subject: complete pre-release API security remediation and prevention
- Workstream: event-api-security-remediation
- Report kind: planning-assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-01
- Reviewed input: `event-api-security-remediation-r1`; consultation at SHA-256 `c01031ea05b993965b1104cf203977414168ca965d2a09d6512c4feb0cffeee4`; Event `22909436883ea90f3e1477685ce3020b230c41da` plus the inspected shared working tree
- Supersedes: none; the maintained consultation remains the source assessment

## Scope

The user requests exhaustive planning for every point in the [API security consultation](../consultations/i-vsd-event-api-security-compliance-check.md). This includes its 27 findings and mitigations, 53 documented families, all 209 reference records, ten API security categories, and explicitly missing release evidence. Protected records require regression evidence; Unverified records require actual investigation and closure; Not applicable records require bounded, revision-bound justification rather than invented implementation.

The intended software scope includes Split API/BFF and Combined hosting, supported authentication and authorization modes, all five relational providers, API-triggered integrations, files, public projections, and API-fed browser consumption where the consultation deliberately withheld an end-to-end conclusion. The Official Instance is not the only supported security profile.

Planning changes only local working-memory documents and this assessment. Implementation, destructive cleanup, credential rotation, external-provider writes, deployment and release are not performed by planning.

## Claim Boundary

This report supplies provider-responsibility design validation and source traceability. It does not establish secure operation, ASVS certification, legal compliance, a scholarly ruling, or release approval. The consultation's findings remain open until the implementing revision and required runtime evidence exist.

Inherited identifiers below retain the consultation's subject identity. `IVSD-F001` in this report means the same finding as `IVSD-F001` in that consultation, not a new finding with a coincidentally identical number. New workstream-specific findings, if needed, start after `IVSD-F027`.

## Findings

All inherited findings are **accepted for remediation**, not resolved. Each row imports the consultation's detailed threat prerequisites, evidence locators and escalation limits by reference. The common evidence level is implementation traceability, supplemented by design validation; operational validation remains absent.

| Stable finding / mitigation | Severity and claim | Principle / domain | Affected stakeholders; provider-controlled decision | Evidence and next validation owner |
| --- | --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | Medium; verification-routing defect | Sidq, Ihsan / Governance, Evaluation | Maintainers and adopters; which revisions execute security tests | Consultation F001; `.github/workflows/security-tests.yml`; release engineering validates executed versus no-op selection |
| IVSD-F002 / IVSD-M002 | Medium; contradictory assurance | Sidq, Amanah / Technical, Governance | Operators and tenants; accuracy of isolation and pipeline promises | Consultation F002; API/persistence documentation owners reconcile source and operator contracts |
| IVSD-F003 / IVSD-M003 | High evidence priority; operation not reviewed | Amanah, Non-Harm / Operational, Evaluation | All users and responders; launch claims and release approval | Consultation F003 and Missing Evidence; operator and release owner supply candidate-bound evidence |
| IVSD-F004 / IVSD-M004 | Medium; public account-state oracle | Rights of People, Justice / Design, Technical | Local account holders; unauthenticated failure envelope | Consultation F004; identity/API owners prove missing/wrong/locked uniformity |
| IVSD-F005 / IVSD-M005 | Medium; password-policy gap | Amanah, Ihsan / Technical, Design | Local users; one policy for every credential mutation | Consultation F005; identity/security owners validate length, blocklist, salt and calibrated cost |
| IVSD-F006 / IVSD-M006 | Medium, potentially High; token-context gap | Amanah, Non-Harm / Technical | API resource owners; accepted resource audience and clients | Consultation F006; auth/realm owners validate genuinely signed claim combinations |
| IVSD-F007 / IVSD-M007 | Conditional Low-Medium; recovery error oracle | Rights of People, Sidq / Technical, Operational | Recovering users; public response after target-only failure | Consultation F007; lifecycle/email owners validate exceptional uniformity and durable diagnosis |
| IVSD-F008 / IVSD-M008 | Medium; misleading security-test evidence | Sidq, Ihsan / Evaluation | Reviewers and adopters; what passing token tests actually prove | Consultation F008; test owners isolate signature, lifetime, issuer, audience and purpose |
| IVSD-F009 / IVSD-M009 | Conditional High; webhook SSRF | Amanah, Non-Harm / Technical, Operational | Internal-service owners and tenants; actual connected address | Consultation F009; webhook owners prove connection-time destination enforcement |
| IVSD-F010 / IVSD-M010 | Conditional High; AI SSRF | Amanah, Avoiding Spying / Technical | Operators and provider owners; discovery/runtime endpoint authority | Consultation F010; AI owners validate restrictive and explicit local-origin profiles |
| IVSD-F011 / IVSD-M011 | Conditional High; tenant BYO SSRF | Justice, Non-Harm / Technical | Tenants and operators; delegated authorization-provider network power | Consultation F011; authorization infrastructure proves BYO restrictions without permissive fallback |
| IVSD-F012 / IVSD-M012 | Medium; synchronous CPU exhaustion | Justice, Ihsan / Technical | Other users sharing capacity; evaluator execution budget | Consultation F012; Domain/Application owners validate syntax, size and bounded matching |
| IVSD-F013 / IVSD-M013 | Low; final file-policy composition | Non-Harm, Ihsan / Technical | File recipients; actual browser policy on delivered content | Consultation F013; API delivery owners assert final attachment, nosniff and sandbox |
| IVSD-F014 / IVSD-M014 | Medium; AI response allocation | Amanah, Non-Harm / Technical, Operational | Shared-capacity users; bytes/items/depth accepted from providers | Consultation F014; AI infrastructure validates headers-first bounded consumption |
| IVSD-F015 / IVSD-M015 | High; recoverable OAuth credential custody | Amanah, Promise-Keeping / Technical, Governance | Identity users and operators; sole secret authority | Consultation F015; secrets/identity owners eliminate ordinary-setting copies and unsafe readback |
| IVSD-F016 / IVSD-M016 | High; one-time API-key replay/custody | Amanah, Promise-Keeping / Design, Technical | API-key owners; persistence after shown-once issuance | Consultation F016; key/idempotency owners inspect actual keyed issuance and retries |
| IVSD-F017 / IVSD-M017 | Medium; response-buffer resource exposure | Non-Harm, Ihsan / Technical | Shared-capacity users; memory before capture eligibility decisions | Consultation F017; middleware owners validate threshold crossing and byte preservation |
| IVSD-F018 / IVSD-M018 | Medium; topology-dependent transport gap | Non-Harm / Operational, Technical | Browser users; effective external TLS scheme | Consultation F018; hosting owners validate trusted forwarding before HSTS |
| IVSD-F019 / IVSD-M019 | Medium; expensive work before admission | Justice, Non-Harm / Operational | Shared users; pre-auth resource admission | Consultation F019; API owners prove rejected traffic cannot invoke expensive downstream work |
| IVSD-F020 / IVSD-M020 | Medium; proxy/fleet abuse-budget gap | Justice, Amanah / Operational | NAT users and fleet operators; exemptions and aggregate quotas | Consultation F020; hosting/operator owners prove finite peer and fleet budgets |
| IVSD-F021 / IVSD-M021 | Medium; moderation response allocation | Amanah, Ihsan / Technical | Moderation users; bytes parsed from configured provider | Consultation F021; moderation owners validate bounded consumption and stable errors |
| IVSD-F022 / IVSD-M022 | Conditional Medium; host/operational exposure | Rights of People, Avoiding Spying / Operational | Tenants and telemetry subjects; accepted hosts and scrape authority | Consultation F022; hosting owners prove domain authority and private operational surfaces |
| IVSD-F023 / IVSD-M023 | Medium; key-custody threat-model gap | Amanah, Promise-Keeping / Operational | Session/capability holders; database-compromise and backup boundaries | Consultation F023; secrets/operator owners prove independent key custody, overlap and restore fencing |
| IVSD-F024 / IVSD-M024 | High isolation priority; RLS deployment unverified | Justice, Amanah / Technical, Operational | Tenants and workers; coherent policies, sessions and roles | Consultation F024; persistence/operator owners test actual application tables under least-privilege roles |
| IVSD-F025 / IVSD-M025 | Medium; caller-owned bypass hazard | Justice, Ihsan / Technical | Resource owners; persisted authority at every global lookup | Consultation F025; Application/persistence owners inventory callers and test tracked cross-tenant entities |
| IVSD-F026 / IVSD-M026 | Medium; publication-policy uncertainty | Rights of People, Avoiding Spying / Design | Contributors, members and non-users; public identity/relationship fields | Consultation F026; privacy/product owners select minimal public identity or explicit attribution, then validate serialization and caches |
| IVSD-F027 / IVSD-M027 | Medium-High by effect; receipt/business atomicity gap | Amanah, reducing uncertainty / Technical | Purchasers, organizers and providers; retry after committed work | Consultation F027; Application owners prove one logical effect and bounded ambiguity recovery |

No row is risk-accepted merely by scheduling it. Confirmed unsafe paths require remediation before exposure; supported dormant modes require safe software contracts, not a waiver based on the Official Instance's intended configuration.

## Recommendations

1. Preserve all stable finding and reference IDs in a candidate-bound, machine-consumed security evidence register. A green check with no executed tests is not closure evidence.
2. Sequence secret custody, one-time issuance, actual egress connections, bounded evaluators and PostgreSQL isolation before broader release-readiness hardening.
3. Keep authority in Domain invariants and native Application CQS handlers; HTTP, MCP, BFF and worker adapters must not invent different authorization semantics.
4. Keep selected secret authority absolute. Source disappearance, permission failure or outage must not select another source or resurrect database values.
5. Treat identity disclosure, rate-limit fairness, incident ownership and restore safety as provider decisions, not merely library settings.
6. Require evidence for API-fed browser contexts that the consultation marked Unverified; an API-only claim cannot close them.

Rejected alternatives: reducing scope to the intended Keycloak Official Instance; calling all Protected rows secure without execution; copying educational prevention snippets; accepting platform-admin status as network authority; treating UUIDs as secrets; adding a generic security framework; retaining unsafe compatibility readers; declaring launch ready from plan quality.

## Stakeholders

Attendees, guest registrants, contributors, organizers, organization/group members, tenant administrators, operators, non-users whose relationships or homes appear in content, integration providers, maintainers and responders all retain independent interests. SingleTenant does not collapse these people into one mutually trusted principal.

## I-VSD Principles And Domains

Trust requires constrained authority and independent credential custody. Truthfulness requires bounded security claims and revision-bound test evidence. Justice requires same-tenant isolation, shared-network fairness and usable recovery. Non-Harm requires finite computation, network and storage budgets. Rights of People and Avoiding Spying require minimized public fields and zero-sensitive telemetry. Promise-Keeping requires durable retry/recovery semantics. Excellence requires maintainable native ownership and meaningful negative tests.

Strategic, Design, Technical, Operational, Governance and Evaluation domains are applicable. Religious-content and financing judgments are not introduced by this security work; no riba or religious-legal conclusion is inferred from a security test.

## Common Overlooked Failures And Outcomes

- A correctly signed token for another resource is still the wrong authority.
- A public DNS answer checked before a later lookup does not constrain the socket.
- A redacted read DTO does not remove secret-bearing database or replay copies.
- A response-store threshold applied after allocation does not bound memory.
- An EF filter does not authorize an identity-map entity or a global worker.
- A rollback of application state must not rollback newer erasure or revocation authority.
- Blocking recovery notifications globally to hide an account oracle removes a remedy rather than repairing confidentiality.
- An old success body must not restore a revoked membership or consumed capability.
- A privacy fix must preserve intentional public publisher identity without accidentally exposing its private membership evidence.

## Validation Gaps

No product build, test, exploitation, external provider operation or deployment was executed in planning. Existing tests are source evidence only. Planned tests must preserve real signing, handler authorization, persisted ownership, socket destination and transaction behavior.

## Escalation Needed

- Authored planning decisions: minimal intentional public publisher identity and operator-provisioned external keyrings are selected recommendations under best judgment. Questions were issued asynchronously; public identity timed out unanswered. No recommendation is represented as user approval. A differing answer invalidates the affected mappings and requires focused revalidation.
- Before implementation: user approval of the plan; exact ownership reconciliation for shared dirty files.
- Before release: named operator/security approval of actual topology, realm MFA/recovery, egress, limits, scans, key/identity/media restore and incident response evidence.
- Destructive cleanup or credential rotation needs exact target identification and explicit authorization at execution.
- Qualified legal/privacy and Sunni scholarly review only where a concrete jurisdictional or religious-legal question arises; none is manufactured by ordinary security remediation.

## Evidence Reviewed

- Complete source consultation, including all 209 individually compared records and all 27 findings.
- Planning skill and resource contracts, I-VSD report/integration/evidence contracts, relevant security/privacy/persistence/API rules.
- Event HEAD and origin/develop are equal after `git pull --ff-only`; no branch switch.
- Relevant tracked backend/test diff SHA-256: `3147ebb500d3ae239b86484dfc7f69d4194ae14d580af03058a366a6464d0c24`. This differs from the consultation's diff fingerprint; the workstream therefore refreshes subsystem evidence rather than inheriting an unchanged-snapshot claim.
- OpenAPI SHA-256: `26d3a63374f28b6d2f8d2ae6fa479ce2521c54beecbc280b6f59bb7bd5ee1fec`.
- Prior identity-linking decisions in `pre-release-contract-foundations`; publication/discovery scope and stale review state in `event-publication-and-identity-discovery`.
- Official NIST password guidance, OWASP verification/API security guidance, PostgreSQL RLS documentation and ASP.NET Core Data Protection documentation; independently summarized functional constraints only.

## Missing Evidence

Actual launch infrastructure, provider accounts, realm policy, stakeholder review, passing candidate tests/scans, egress enforcement, restore exercises and response staffing are not supplied. Their absence remains a release blocker, not a fabricated source vulnerability or a planning-time test result.

## Context Inventory

The shared evidence packet includes parent investigation plus three bounded read-only subsystem investigations: identity/custody, egress/parsing and host/isolation/replay. Knowledge-graph tools are unavailable; native source search/read is the fallback. Product code and unrelated workstream documents remain untouched.

## Planning Handoff

- Workstream: event-api-security-remediation
- Status: current
- Reviewed input: event-api-security-remediation-r1
- Findings and mitigations: inherited IVSD-F001 through IVSD-F027, each linked to IVSD-M001 through IVSD-M027
- Required plan mappings: plan Section 9 binds all 27 inherited finding/mitigation pairs to S01-S34 and existing task IDs across 24 phases; Section 9.1 assigns all 209 reference records, including supplementary host/transport records; source Protected/Unverified/Not applicable dispositions remain distinct
- Revalidation result: completed triad `event-api-security-remediation-r1` preserves exclusive external authority, current persisted resource checks, finite egress/compute/response boundaries, minimal disclosure, non-enumerating remedies, honest retry/recovery and candidate evidence. 121 unique checkbox tasks have explicit acceptance assertions; no finding is called implemented or resolved.
- Escalations required before: implementation for user approval and shared ownership; release for actual operator/security evidence
- Refresh triggers: scope, identity disclosure, credential custody, tenancy, topology, provider origin, budgets, replay semantics or release-claim changes

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-10-01 | none | draft | Exhaustive security-remediation planning requested | Consultation, current revision and shared source packet; final triad mappings pending |
| 2026-10-01 | draft | current | Completed triad revalidated with explicit recommended defaults | 27 mitigation mappings, 34 scenarios, 24 phases, 209 reference assignments and candidate-bound operational admission; disposition plan-aligned, not release approval |
