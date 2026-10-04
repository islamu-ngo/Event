# Event API Security Remediation - Planning Assessment

Last Updated: 2026-10-02

## Review Metadata

- Mode: planning
- Subject: complete pre-release API security remediation and prevention
- Workstream: event-api-security-remediation
- Report kind: planning-assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-02
- Reviewed input: `event-api-security-remediation-r2`; unchanged consultation at SHA-256 `c01031ea05b993965b1104cf203977414168ca965d2a09d6512c4feb0cffeee4`; bounded source/test intake at Event `54391020ff7d0c31318d542f7c68d14fde69d4d9` plus shared changes, final identity `3d502c4d096695ce2fdd7cf85691e7a333ed35d2` qualified below
- Supersedes: none; the maintained consultation remains the source assessment

## Scope

The user requests exhaustive planning for every point in the [API security consultation](../consultations/i-vsd-event-api-security-compliance-check.md), expanded on 2026-10-02 by ten bug classes and twelve vulnerability classes. This includes the original 27 findings and mitigations, 53 documented families, all 209 reference records, ten API security categories, 22 additional mandatory class entries and explicitly missing release evidence. Protected records require regression evidence; Unverified records require actual investigation and closure; Not applicable records require bounded, revision-bound justification rather than invented implementation.

The intended software scope includes Split API/BFF and Combined hosting, supported authentication and authorization modes, all five relational providers, API-triggered integrations, files, public projections, and API-fed browser consumption where the consultation deliberately withheld an end-to-end conclusion. The Official Instance is not the only supported security profile.

Planning changes only local working-memory documents and this assessment. Implementation, destructive cleanup, credential rotation, external-provider writes, deployment and release are not performed by planning.

## Claim Boundary

This report supplies provider-responsibility design validation and source traceability. It does not establish secure operation, ASVS certification, legal compliance, a scholarly ruling, or release approval. The consultation's findings remain open until the implementing revision and required runtime evidence exist.

Inherited identifiers below retain the consultation's subject identity. `IVSD-F001` in this report means the same finding as `IVSD-F001` in that consultation, not a new finding with a coincidentally identical number. R2 adds workstream-owned IVSD-F028 through IVSD-F031 without silently rewriting the consultation. BUG/VULN labels are user-supplied priorities and investigation contracts, not exploit findings or verified global prevalence rankings.

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

### R2 Workstream Findings and Qualified Claims

All four rows are open / accepted-for-remediation-or-assurance. Evidence is implementation traceability and planned design validation, not executed operational validation.

| Finding / mitigation | Severity / claim and evidence | Principles / domains / stakeholders | Provider decision, plan ownership and closure |
| --- | --- | --- | --- |
| IVSD-F028 / IVSD-M028 | High configuration concern; confirmed `docker-compose.yml:90-94` fallback database passwords; actual deployed compromise untested | Amanah, Non-Harm / Technical, Operational / operators, tenants and data subjects | Require explicit selected authority and reject missing/empty/default rendered deployment credentials; phases 8.3/17.3/26.2, S47; actual binding/rendered-profile proof plus separately approved credential rotation if needed |
| IVSD-F029 / IVSD-M029 | High robustness concern; custom-property Options `_options!` can expose explicit null to validator `options.Count`; runtime reproduction not performed | Ihsan, Non-Harm / Design, Technical / organizers and users sharing capacity | Define null/omitted/empty/value admission, preserve valid null clearing and shared candidate validation; phases 7.1-7.3, S35/S37; actual JSON, unchanged-state, fresh relational and generated-wire evidence |
| IVSD-F030 / IVSD-M030 | Medium lifecycle concern; DefaultDoctorProcessRunner cancellation has no explicit child termination/reaping; no orphan demonstrated | Amanah, Non-Harm / Technical, Operational / operators and shared-host users | Own approved argv, output and process tree; phase 25.1-25.3, S43/S49; activated OS-child cancellation/exit/readers and safe output proof |
| IVSD-F031 / IVSD-M031 | Critical-priority user threat class, not confirmed exploit; existing server prompt/tool/native action boundaries need full adversarial assurance | Amanah, Rights of People, Non-Harm / Technical, Governance / users, resource owners and model-provider subjects | Model/reference/output text never grants authority; phase 24.1-24.3, S45; current native auth, strict tools/schema/budgets, exact confirmation, no context/credential handoff, cancel/replay/disclosure proof |

### R2 Class Traceability

Every entry below is mandatory in the candidate register and maps to plan Sections 9.3-9.5. Scheduling is not closure.

| User class | Observable scenarios | Mitigation ownership / acceptance evidence |
| --- | --- | --- |
| BUG-01 Null access | S35, S37, S42 | 7/20/23; explicit optionality and saved-state/wire null proof; IVSD-M029 |
| BUG-02 Race conditions | S36, S16, S24, S27, S45 | 3/8/19/21/24; forced contention with persisted winners/effects |
| BUG-03 Create/update drift | S37, S35, S42 | 7/19/20; same full-candidate invariant validation with omission semantics |
| BUG-04 Disabled critical logic | S38, S02 | 1/8/17/18/24/26; actual non-Testing activation/failure/shutdown, not comments |
| BUG-05 Async/await misuse | S39, S43, S27 | 19/21/24/25; real transaction outcome/cancellation and awaited settlement |
| BUG-06 Boolean/boundary mistakes | S40, S12, S24, S33 | 7/8/13/15/18/19/21; exact truth tables and adjacent representable bounds |
| BUG-07 Hardcoded dynamic values | S41, S47 | 1/2/5/8/10/12/17/24/26; current catalogue/options/state and isolated nondefault configs |
| BUG-08 Schema consumer breakage | S42, S35 | 3/7/8/20/23/24/26; generated actual wire and consumer behavior |
| BUG-09 Resource leaks | S43, S48, S49 | 4/8/13/15/23/24/25; explicit ownership/cleanup; IVSD-M030 |
| BUG-10 Database anomalies | S44, S24, S33 | 1/8/19/21/26; five-provider duplicate/empty/normalized/range outcomes |
| VULN-01 SQL injection | S50, S24, S25 | 8/19/22/26; real parameter/data and closed identifier boundary |
| VULN-02 Path traversal | S29, S28, S49 | 16/22/25; approved root/operation custody and bounded archive paths |
| VULN-03 Missing authorization | S25, S32, S38, S45 | 1/19/23/24; actual direct dispatch/tool/job/resource authority |
| VULN-04 Attribute XSS | S31, S13 | 16/22/23; rendered text/attribute/protocol/interop contexts |
| VULN-05 SSRF | S09-S11, S45 | 4/5/6/24; actual destination and model-selected link boundaries |
| VULN-06 Source secrets | S15, S16, S23, S47, S30 | 2/3/8/12/17/22/26; external custody/artifact proof; IVSD-M028 |
| VULN-07 Open redirect | S46, S22 | 17/23; actual final Location and safe callback authority |
| VULN-08 Sensitive logs | S30, S07, S45, S49 | 9/22/23/24/25; actual final sinks and bounded safe diagnosis |
| VULN-09 Prompt injection | S45, S25, S27, S30 | 24/5/19/21; model-independent authority; IVSD-M031 |
| VULN-10 Default credentials | S47, S41 | 2/6/8/17/26; fail-closed profile and forgotten administrator input; IVSD-M028 |
| VULN-11 Command injection | S49, S45 | 25/24/1; approved typed argv/no shell; IVSD-M030 |
| VULN-12 PostMessage trust | S48, S43 | 23/26; exact Window origin/source versus owned worker/port correlation |

Resource lifetime, dynamic configuration, race and null priorities protect availability and fair shared resource use; candidate parity and schema correctness protect promises about stored state; prompt/message/process authority protect people from an intermediary interpreting attacker content as permission. Delimiters and model refusals cannot replace authorization. Dedicated-worker replies are not authenticated by Window event.origin.

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
- A normally returned business failure may still commit its UoW; test the exact caller contract, not only exception rollback.
- Two concurrently launched tests may never overlap at the critical state; force contention before claiming race evidence.
- A process disposed after canceled WaitForExitAsync may still run; cancellation needs explicit owned termination/reaping.
- Treating Window and dedicated-worker messages as identical can create an ineffective “origin fix.”
- A safety instruction in the system prompt cannot authorize or constrain the server's tool execution by itself.

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
- At intake HEAD and origin/develop both equal `54391020ff7d0c31318d542f7c68d14fde69d4d9` after exit-0 `git pull --ff-only`; no branch switch. Concurrent work advanced both to `3d502c4d096695ce2fdd7cf85691e7a333ed35d2` before handoff.
- R2 intake tracked backend/test diff SHA-256: `bfe04ed2dcb3d3b48d8212570ad66b382ac49a806209216b78d1829339d1d4f6`; final working diff is empty. Final comparison against the intake base differs (`7b096f74eb7cfeea72684e2e110b58aa8075a03a063d4c854d1ce59882c4f8bb`), while direct new source-anchor probes returned no changes. Test-body claims remain intake-bound and complete candidate refresh is mandatory. Original R1 fingerprint is historical; no whole-snapshot unchanged claim.
- OpenAPI SHA-256: `26d3a63374f28b6d2f8d2ae6fa479ce2521c54beecbc280b6f59bb7bd5ee1fec`.
- Prior identity-linking decisions in `pre-release-contract-foundations`; publication/discovery scope and stale review state in `event-publication-and-identity-discovery`.
- Official NIST password guidance, OWASP verification/API security guidance, PostgreSQL RLS documentation and ASP.NET Core Data Protection documentation; independently summarized functional constraints only.
- R2 bounded data/runtime/AI/client source and actual test-body inspections; no test result inferred from reading. Current SecretBinding MySQL mitigation qualifies the recalled older defect. Dated earlyoom note informs final runner resource budgeting, not product behavior.
- OWASP LLM01 functional trust-boundary guidance, rechecked 2026-10-02 without external implementation copying.

## Missing Evidence

Actual launch infrastructure, provider accounts, realm policy, stakeholder review, passing candidate tests/scans, egress enforcement, restore exercises and response staffing are not supplied. Their absence remains a release blocker, not a fabricated source vulnerability or a planning-time test result.

## Context Inventory

The shared evidence packet includes parent investigation plus three bounded read-only subsystem investigations: identity/custody, egress/parsing and host/isolation/replay. Knowledge-graph tools are unavailable; native source search/read is the fallback. Product code and unrelated workstream documents remain untouched.

## Planning Handoff

- Workstream: event-api-security-remediation
- Status: current
- Reviewed input: event-api-security-remediation-r2
- Findings and mitigations: inherited IVSD-F001 through IVSD-F027 plus qualified workstream IVSD-F028 through IVSD-F031, each linked to its corresponding IVSD-M identifier
- Required plan mappings: Sections 9 and 9.5 bind all 31 qualified finding/mitigation pairs; Sections 9.3-9.4 independently bind every one of 22 user classes; S01-S50 and task IDs across 26 phases define observable outcomes. Section 9.1 still assigns all 209 reference records; source dispositions remain distinct.
- Revalidation result: completed triad `event-api-security-remediation-r2` preserves R1 authority/custody/disclosure/recovery and adds candidate parity, runtime activation/lifetime, dynamic values, actual schema consumers/provider anomalies and model/message/process trust boundaries. All 132 checkbox tasks have concrete acceptance; no finding or class is called implemented or operationally resolved.
- Escalations required before: implementation for user approval and shared ownership; release for actual operator/security evidence
- Refresh triggers: scope, identity disclosure, credential custody, tenancy, topology, provider origin, budgets, replay semantics or release-claim changes

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-10-01 | none | draft | Exhaustive security-remediation planning requested | Consultation, current revision and shared source packet; final triad mappings pending |
| 2026-10-01 | draft | current | Completed triad revalidated with explicit recommended defaults | 27 mitigation mappings, 34 scenarios, 24 phases, 209 reference assignments and candidate-bound operational admission; disposition plan-aligned, not release approval |
| 2026-10-02 | current | current | User added all ten bug and twelve vulnerability classes; R2 triad revalidated | 26 phases, 50 scenarios, 132 tasks, unchanged 209-reference coverage, 31 qualified finding mappings and all 22 distinct class obligations; plan-aligned, not implemented or release-approved |
