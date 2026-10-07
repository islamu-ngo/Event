# Event API Security Remediation - Planning Assessment

Last Updated: 2026-10-07

## Review Metadata

- Mode: planning
- Subject: complete pre-release API security remediation and prevention; unified phased programme with delivered one-time issuance baseline
- Workstream: event-api-security-remediation
- Report kind: planning-assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-07
- Reviewed input: `event-api-security-remediation-r5-cto-20261007`; exact triad hashes in R5 Revalidation Evidence. Read-only source snapshot `0d4ab8d5a62e74ebcd9476a1b47eddda7ea270e1`; prior R1-R4 and PC-CTO-r2 claims retain their historical limits.
- Supersedes: none; the maintained consultation remains the source assessment

## Executive Summary

The complete API-security programme is **current / plan-aligned to event-api-security-remediation-r5-cto-20261007 for planning**, not implemented, independently security-reviewed or release-certified. The user requested one authoritative phase-ordered triad rather than scattered mandatory backlog. All 31 API finding/mitigation pairs, 209 reference assignments, 53 families, S01–S50 and 22 bug/vulnerability classes remain; provider-credential findings keep a qualified namespace. API-key one-time disclosure is now a delivered baseline from merged PR71, not a pending history rewrite. Broader authority, privacy, custody, resource consumption and operational findings remain open for their actual evidence.

The consolidated contract protects affected users/operators by retaining current persisted authority, least privilege, non-recoverable credential receipts, explicit external authority and honest recovery. Ordinary settings are reference-only; only explicitly approved writable provider capabilities may use narrow write-only selected-Infisical operations. Non-writable authorities require truthful operator injection, not fallback. Consumer replacement, no-resurrection fences and replica drain precede exact cleanup/rotation; Cerbos publication readiness precedes retirement of runtime administration. These restrictions preserve the original provider-duty decisions rather than using consolidation to silently expand write or operator authority.

Revalidation reviewed the complete triad, original API assessment, provider-credential assessment, 11 fully absorbed briefs and current read-only 25-phase source inventory. It checked every inherited mapping, 19 provider task aliases, 28 supplementary scenarios, distinct namespace ownership, release admission, recovery and unknowns. Relocation cannot turn source-only risk hypotheses into reproduced defects or passed controls. Resource/browser/operational proof remains actual candidate admission input; privacy/keyring defaults remain authored recommendations for user review. No new religious-legal conclusion, consented product expansion, blanket administrator power, identified browsing audit or risk waiver is authorized.

Technical/CTO refinements are Applied & Aligned for r5. The user's explicit 2026-10-07 instruction authorizes full programme execution, not deployment, destructive cleanup or release. Scoped revalidation is required if provider authority, retention, disclosure, defaults, lifetime, roots, role claims or required evidence changes. Independent operator/scholarly/legal evidence stays with its qualified owner.

## Scope

The user requests exhaustive planning for every point in the [API security consultation](../consultations/i-vsd-event-api-security-compliance-check.md), expanded on 2026-10-02 by ten bug classes and twelve vulnerability classes. This includes the original 27 findings and mitigations, 53 documented families, all 209 reference records, ten API security categories, 22 additional mandatory class entries and explicitly missing release evidence. Protected records require regression evidence; Unverified records require actual investigation and closure; Not applicable records require bounded, revision-bound justification rather than invented implementation.

The intended software scope includes Split API/BFF and Combined hosting, supported authentication and authorization modes, all five relational providers, API-triggered integrations, files, public projections, and API-fed browser consumption where the consultation deliberately withheld an end-to-end conclusion. The Official Instance is not the only supported security profile.

R3 revalidation evaluates only the split, phase 3 issuance design, receipt retention/minimization, loss-of-disclosure recovery, authority/revocation ordering and ownership mappings. The full programme specification remains normative; this review does not re-audit every historical source observation against the newer codebase or infer closure from merged foundational work.

This prerequisite track changes only this assessment in `.worktrees/event-api-security-remediation`. It does not edit runtime code, the triad or backlog, run product tests, commit, perform destructive cleanup or credential rotation, or write to external systems. Endpoint-test investigation belongs to the lead and is not claimed as evidence here.

## Claim Boundary

This report supplies provider-responsibility design validation and source traceability. It does not establish secure operation, ASVS certification, legal compliance, a scholarly ruling, or release approval. The consultation's findings remain open until the implementing revision and required runtime evidence exist.

Inherited identifiers below retain the consultation's subject identity. `IVSD-F001` in this report means the same finding as `IVSD-F001` in that consultation, not a new finding with a coincidentally identical number. R2 adds workstream-owned IVSD-F028 through IVSD-F031 without silently rewriting the consultation. R3 refines IVSD-M016 and the issuance-specific applications of IVSD-M025/IVSD-M027; it neither creates replacement findings nor resolves their broader obligations. BUG/VULN labels are user-supplied priorities and investigation contracts, not exploit findings or verified global prevalence rankings.

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

### R3 Revalidation: One-time Disclosure Requires Durable, Minimal Operation Memory

**IVSD-F016 / IVSD-M016 remains accepted for remediation, not resolved; High credential-custody concern.** A key owner reasonably understands "shown once" to mean that losing the response cannot be repaired by reading a second durable copy. HTTP no-store alone does not make that promise true. R3 D03 and tasks 3.1-3.4 address the provider-controlled second store by suppressing generic response capture and replacing it with a feature-owned receipt that contains no plaintext or decryptable raw credential. Suppression also skips the generic middleware's key admission, so the explicit HTTP parser and native Application validation are necessary parts of the mitigation, not optional adapter polish.

The non-null operation fingerprint binds kind/version, current principal, explicit instance-or-tenant scope, authorized owner and operation key. The separate canonical input digest makes changed policy under the same intended operation a conflict rather than a second credential. This supports Amanah and Promise-Keeping by aligning durable state with the promised operation, and Justice by preventing one scope from consuming or recovering another's result. Tasks 3.1/3.4 still owe actual keyed-route, global-null-tenant, cross-principal/tenant/owner, changed-input and forced-contention proof on the supported engines. Reading the formula does not prove its canonical encoding or database uniqueness.

Receipt retention has a narrow purpose: remember that an accepted operation cannot become a fresh issuance merely because time passed, a key was revoked, or metadata was cleaned up. R3's minimal tombstone or monotonic rejection fence is preferable to a convenient TTL that forgets this fact. Retain only the digest/state and references needed to reject replay or authorize safe recovery; full response bodies, duplicate secret values and unrelated personal or policy history have no such purpose. Live recovery metadata and a terminal replay-rejection tombstone are different needs. The implementation must document the actual field inventory, deletion behavior and access boundary in task 3.3; this review does not prescribe an arbitrary retention duration or approve indefinite retention of every live-receipt field.

Rights of People and Avoiding Spying still apply to hashes and linked key/owner identifiers: pseudonymous or guessable input digests are not anonymous data and must not become telemetry labels, public lookup authority or a second identity registry. Under the current design, the rejection fact survives for as long as that operation can be accepted. Future minimization/erasure may remove more information only when the same operation remains unconditionally rejected through a monotonic fence; that change triggers focused I-VSD revalidation. Backups that lose a receipt or newer revocation remain an inherited restore risk under IVSD-F003/IVSD-F023 and S34, not something this plan review proves safe.

Evidence: R3-E01 D03, D14, Sections 7.0/9/14.1; R3-E02 tasks 3.1-3.4; R3-E05 consultation F016. Level: design validation with inherited source traceability. Owners: Domain/Application/Persistence issuance maintainers for state and minimization; API/client/documentation owners for admission and truthful disclosure; operator/security owners for separately authorized contaminated-receipt/backup cleanup and rotation. Escalation: any recoverable-secret receipt, TTL reopening, expanded retention purpose or weakened erasure fence invalidates alignment.

### R3 Revalidation: Lost Disclosure Must Have An Honest, Authorized Remedy

**IVSD-F027 / IVSD-M027 remains accepted for remediation, not resolved; Medium-to-High by consequence.** The issuance-specific repair commits the key and receipt together through the native handler's unit of work. Stable identity/time/entropy outside retryable delegates and receipt-based recovery address a database commit whose acknowledgement or HTTP response is lost. Cancellation or an exception is not evidence that the transaction failed to commit. A normally returned business failure is also distinct from rollback. Tasks 3.1/3.2/3.4 require persisted outcomes for these cases rather than a mock transaction that always does what the test expects.

For an authorized retry, `DisclosureStatus=PreviouslyIssued`, the same key ID and `ApiKey=null` communicate that issuance happened but its secret cannot be recovered. `Issued` with a raw key belongs only to the winning committed invocation. The real service/dialog must retain the operation key through transport uncertainty and present a deliberate revoke/reissue remedy. Automatic replacement is rejected because it can accumulate usable credentials and hide an unresolved earlier outcome. Unauthorized, revoked, expired or hidden state cannot be presented as newly usable authority, and the former issuer does not regain metadata or revocation privileges merely by knowing the operation key. Recovery uses current authorized ownership and existing HAL affordances, with independent server checks.

This makes Sidq concrete: the interface says what was committed and what was lost without promising exactly-once network delivery. Non-Harm and Promise-Keeping require a usable explanation and a way for a currently authorized owner to retire the inaccessible key before deliberately issuing another. No raw exception body may be reflected as diagnosis. Generated schema/client and actual component behavior belong in the same slice, because a nullable response that the old dialog treats as an ordinary failure would undermine the remedy.

Evidence: R3-E01 D03/D12/D14 and Section 7.0; R3-E02 tasks 3.1-3.4; R3-E04 phase 21; R3-E05 consultation F027. Level: design validation, not commit-loss or usability proof. Owners: issuance handler/UoW, persistence, API/generated-client and dialog maintainers. General checkout, refund, registration, remote-provider ambiguity and transactional-outbox duties remain with phase 21; this receipt does not resolve them. No outbox is invented for issuance without an actual external side effect.

### R3 Revalidation: Current Authority Needs Shared Revocation Ordering

**IVSD-F025 / IVSD-M025 remains accepted for remediation, not resolved; Medium systemic hardening concern with security-critical issuance consequences.** R3 identifies the final persisted owner-authority decision inside the issuance transaction as the linearization point: the point at which competing issuance and revocation have a defined order. Revocation committed before that decision denies issuance and leaves neither row; issuance ordered first may commit one key/receipt pair. The same governing revocation mechanism must participate in the fence. A newly opened transaction, cached administrator check, fresh-looking read or lock used only by issuance does not establish this ordering under snapshots or competing writers.

Recovery must authorize the exact persisted principal/scope/owner and current key state, including foreign pretracked entities and changed membership/admin authority. A committed revocation cannot be bypassed through transport replay. The plan does not promise recall of raw bytes already legitimately disclosed or make issuer-permission revocation synonymous with retroactively revoking every previously issued key; key validity remains its own current authority. Tests must establish the promised ordering, not assert an impossible reversal of an already completed disclosure.

This is an Amanah and Justice obligation because the provider, not the former member or resource owner, controls whether obsolete authority continues to act. The design is aligned precisely because tasks 3.1/3.2 make identifying the shared owner-specific fence a gate before Green. No existing fence is certified here. If implementation needs additional owner-revocation paths, their exact ownership and contract amendment must be recorded before editing; a material behavioral weakening reopens this assessment. General bypass inventory and resource-family repairs remain with phase 19.

Evidence: R3-E01 D03, Sections 7.0/14.1/16; R3-E02 tasks 3.1/3.2/3.4; R3-E04 normative correction 8 and phase 19; R3-E05 consultation F025. Level: design validation only for the R3 ordering contract. Owners: issuance and corresponding membership/admin revocation maintainers jointly, with persistence/security review. Next validation: signal-coordinated independent-context races in both allowed orderings, current recovery denial and real handler/endpoint authorization; no test double may grant the authority under test.

### R3 Split And Ownership Reconciliation

#### Focused implementation amendment revalidation

The lead's D03 owner-fence amendment preserves IVSD-F016/M016, IVSD-F025/M025 and IVSD-F027/M027 while making their concrete enforcement boundaries explicit. Actual owner/membership/role/permission row fences, fresh no-tracking authority, and the retained-erasure serialization gate protect against stale grants and account resurrection. No new credential archive, broader retention purpose, remote side effect or general authorization framework is introduced. The unchanged launch backlog is now present inside the execution worktree and matches the bound shared-root SHA-256 `ceb18a43ccb33ab82ea5ac03be5dfe8169f842fada4907c8c589b60f7349803a`; its other phases remain unimplemented launch blockers.

Personal tenant-scoped issuance and recovery now require active exact tenant membership as well as a current nondeleted, nonerased User. The material choice was offered explicitly; it timed out, and the user directed continuation on best judgment. The recommended fail-closed default is selected rather than invented as an answered choice. Removing issuance's transitional empty-permission-table grant likewise keeps authority tied to positive persisted management permission. These restrictions preserve resource owners' control and make revoked participation effective; they can deny a previously permitted issuance, so documentation and tests must state the changed contract. They do not retroactively recall bytes already disclosed or automatically revoke every previously issued key.

Application's in-memory tests guard operation-identity and recovery-result serialization; real native handler authority remains mandatory in relational/HTTP tests. Moving that proof to its actual database seam avoids mocks that could grant the permission being tested without weakening the acceptance criteria. These are design-aligned amendments. The complete shared-fence, co-located outer commit, rollback/commit-loss, tenant/pending-tracker, five-engine and rendered-consumer evidence gates remain open.

The split reduces review size without reducing provider responsibility. General evidence tooling is not a prerequisite for writing the issuance regression, but the slice must capture its own revision-bound evidence and finish real boundary proof before closure. The overall programme still needs that tooling and candidate admission before launch.

| Obligation | Active issuance ownership | Retained programme ownership / gate |
| --- | --- | --- |
| IVSD-F016 / IVSD-M016; S16, S36 | Tasks 3.1-3.4 own operation admission, uniqueness, one disclosure, minimal receipt and actual replay proof | Contaminated legacy rows/backups and rotation targets need exact inventory and separate destructive-action approval; successful new issuance cannot erase historical exposure |
| IVSD-F025 / IVSD-M025; S25 | Tasks 3.1/3.2/3.4 own issuance/recovery authority and the shared owner-revocation fence | Phase 19/tasks 19.1-19.5 retain the full caller/bypass/resource inventory; no global closure from this slice |
| IVSD-F027 / IVSD-M027; S27, S39 | Tasks 3.1/3.2/3.4 own atomic key/receipt and ambiguous-commit recovery | Phase 21/tasks 21.1-21.5 retain all other consequential operations, commerce S33 and remote-provider ambiguity |
| Safe response/consumer and receipt fields; S30, S42, S44 | Tasks 3.3/3.4 own nullability/status, stable client retry key, deliberate recovery, generated wire/migrations, bounded errors and provider uniqueness | Phases 20/22/23 retain general identity projections, sink inventory and client/ingress assurance; issuance scope does not waive relevant local obligations |
| IVSD-F003 / IVSD-M003 and IVSD-F023 / IVSD-M023; S03, S23, S34 | Slice proof must truthfully distinguish planned, failed, unexecuted and passing evidence; receipt cleanup cannot reopen an operation | Phases 12/26 retain independent key custody, restore fencing and candidate-bound operator evidence |
| Every remaining inherited and R2 finding, reference and class | No closure or reassignment to the five issuance tasks | Plan Sections 9-9.5 and the complete mandatory backlog retain all mappings; all 25 deferred phases remain launch blockers |

The shared-root backlog contains 25 phase headings and 127 task entries; the active ledger contains five. The plan retains the complete scenario/reference/class mapping, including all four R2-owned finding pairs. Its source observation that merged identity/discovery foundations exist is not a substitute for the still-open field policy, retention, restore or current-runtime assurance. Publication/identity defaults remain recommendations, not approved decisions inferred from implementation authorization.


### R4 Unified Programme Revalidation

The split was a document/topology choice, not permission to defer harm prevention. Reunification keeps one accountable owner for all mandatory controls while preserving bounded reviewable releases. Existing API IVSD-F015/016/023/025/026/027 and provider PC:IVSD-F001–F006 retain their substantive duties: no recoverable runtime secret store; request-scoped human credentials; consumer-isolated acquisition before fetch; current resource authority and minimum-purpose retention; usable operator/manual recovery; and no inflated publication/release claim. Missing or historical proof remains missing/historical.

The provider-specific write-only capability is narrower than ordinary configuration: it requires a Ready explicitly writable selected binding and owning server operation authority. A routine settings PATCH is reference-only. Non-writable authority cannot justify silent fallback. This reconciles the existing security D02 recommendation with ADR-031/PC-CTO-r2 without authorizing arbitrary external writes. Exact cleanup preserves unrelated tenant state; the replacement BFF consumer and old-writer/reader fences precede deletion/retirement. D4/D5 policy ownership and administrator removal share one safe deployment boundary.

Plan Section 9.6 assigns qualified provider F001/M001 to Phases 2/5/8; F002/M002 to Phases 2/10; F003/M003 to Phase 2; F004/M004 to Phases 6/8; F005/M005 to Phases 2/5/8; F006/M006 to Phases 2/5/6/8/10. Original scenarios and PC/KC/CB/DP aliases remain explicit. Supplementary resource, route, uniqueness and retry invariants have dedicated observable scenarios/tasks. Future MFA, scanner selection, certificates, identified browsing audits and remote-control-plane products are not implied by this security closure.

### R4 Revalidation Evidence

- Exact unified plan SHA256: `83db2c6408529f149ab4ff40f788f5b2c76a61b96b6a1244e77654e57297abbe`
- Exact unified tasks SHA256: `204bb9d46402d18732b375066d8d7c899e7fe08cba975cb265b234e47039d0ba`
- Exact unified context SHA256: `6c8c1fcd824978b8369f177d68e6a1ac5f2bf86dbb1d80588a1bb7affe75ab63`
- Read-only current source: `0d4ab8d5a62e74ebcd9476a1b47eddda7ea270e1`; source/class/project inventory covers 25/25 legacy outstanding phases, with implemented/source-confirmed/hypothesis/proposed distinctions, no new executed product proof.
- Backlog intake: 44/44 files classified; 11 complete directly relevant contracts absorbed, 33 mixed/unrelated/optional/quarantined briefs retained. Historical source names and qualifications remain in the triad's consolidation ledger.
- Delivered baseline: PR71 merge `ed0b26dfa688ae2dd0f84c6e9ab35a2e0f505858`, head `3f5b5212bb6a27eb88dcd9da6fe3011d83e6dd3e`; no old history-rewrite blocker.
- Reviewed primary/secondary sources: maintained API consultation/assessment, provider-credential assessment and ADR-031, R3 delivered contract, inherited matrices and all absorbed brief acceptance/exclusions/recovery. No external product/source research or scholarly/legal determination was performed.

### R5 Revision-Bound Revalidation - 2026-10-07

**Disposition: current / plan-aligned for `event-api-security-remediation-r5-cto-20261007` planning, not implemented-security or release certification.** The complete sole triad and existing assessment were reviewed. Comparison with the initial read snapshots confirmed that subsequent execution-status updates changed no behavior, mitigation, default or design.

R5 strengthens the existing provider duties without replacing findings. IVSD-M023 distinguishes retained expired decryption keys from revoked/deleted keys and separately enforces capability expiry and monotonic restore/revocation authority. Proposed ring bounds cannot discard material needed by live protected values. This preserves usable recovery without turning key rotation into a false revocation promise. IVSD-M024 freezes EF and database lease identity together and requires actual ordinary-role isolation proof, protecting tenants from inherited connection authority.

IVSD-M017/M027 prohibit reopening potentially committed work after response failure, cancellation, abort, serialization failure or oversize output. Later feature recovery must preserve that rejection fact. Credential consumer replacement and replica drain precede exact cleanup: task 2.6 executes before 2.5. Cerbos publication readiness precedes coordinated runtime-administrator/publisher retirement: 8.4 before 8.5, within one safe release boundary. These orders prevent a custody improvement from disabling legitimate service or creating duplicate effects.

All 31 API finding/mitigation pairs retain scenario and executable ownership through the current-to-legacy alias mapping. Provider `PC:` and resource `RESOURCE:` identifiers remain distinct. The single triad preserves 29 phases, 158 outstanding records, five delivered historical records, all 209 reference assignments, 53 families, S01-S50 and all 22 BUG/VULN obligations. No finding closes through relocation.

Binding CTO corrections and plan Sections 4/7.2 govern inherited final-only gate wording: each bounded delivery requires its actual affected boundary Green before its security claim. Programme-exit evidence aggregates that proof; it cannot retroactively certify an earlier delivery. Independent outcomes require complete owned source/test/generated/migration/operator-documentation packets.

The user's explicit 2026-10-07 instruction satisfies programme execution authorization. No new material provider-policy decision or I-VSD design blocker was identified. Authored defaults remain recommendations rather than fabricated answered choices; changes to authority, defaults, retention, lifetime, cutover or evidence requirements trigger focused revalidation. Deployment, destructive cleanup/rotation and release authority remain distinct. Actual operational and independent release evidence remains open.

### R5 Revalidation Evidence

| Reviewed artifact | SHA-256 |
| --- | --- |
| Sole plan | `3e79079f47df83d9e9ab45c976bdf24993b555341b2a8a69cc34f5033f62afe8` |
| Sole tasks | `d64ec9d857bb1d98ad04de4f7ed68af364a387323e3150bc9f1d8efd187145d4` |
| Sole context | `bcb53b7c93e0441c24730a16999e48480184a2258c424c878e823b71260ea8fe` |
| Existing assessment, unchanged review input | `845ce0d82265e4beb6665d7580d784e81473193293b5c64cf0cd83567fd33cdb` |

Evidence level: revision-bound design validation with supplied implementation traceability. The independent read-only assessment performed no product build/test, provider operation, deployment or runtime security verification. The 42-test baseline is lead-executed evidence, not an independently rerun assessment result. Subsequent status/packet-only synchronization does not change the reviewed behavior contracts.

## Recommendations

1. Preserve all stable finding and reference IDs in a candidate-bound, machine-consumed security evidence register. A green check with no executed tests is not closure evidence.
2. Sequence secret custody, one-time issuance, actual egress connections, bounded evaluators and PostgreSQL isolation before broader release-readiness hardening.
3. Keep authority in Domain invariants and native Application CQS handlers; HTTP, MCP, BFF and worker adapters must not invent different authorization semantics.
4. Keep selected secret authority absolute. Source disappearance, permission failure or outage must not select another source or resurrect database values.
5. Treat identity disclosure, rate-limit fairness, incident ownership and restore safety as provider decisions, not merely library settings.
6. Require evidence for API-fed browser contexts that the consultation marked Unverified; an API-only claim cannot close them.
7. Implement R3 as one complete issuance contract with the owned receipt, shared revocation ordering, generated consumer and real boundary proof. Keep all broader mappings attached to the mandatory launch programme; do not promote plan alignment into a passed test or launch decision.

Rejected alternatives: reducing scope to the intended Keycloak Official Instance; calling all Protected rows secure without execution; copying educational prevention snippets; accepting platform-admin status as network authority; treating UUIDs as secrets; adding a generic security framework; retaining unsafe compatibility readers; declaring launch ready from plan quality. R3 also rejects suppression without feature-owned deduplication, encrypted secret readback, input digest inside unique operation identity, nullable global uniqueness, TTL reopening, automatic replacement after response loss and a fresh authorization read without shared revocation serialization. Each either breaks the one-time promise, duplicates authority, misrepresents recovery or leaves a race the resource owner cannot control.

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

No product build, test, exploitation, external provider operation or deployment was executed by this revalidation. Historical test inspections remain source evidence only; no issuance result from the lead's parallel endpoint investigation was independently verified here. Planned tests must preserve real signing, handler authorization, persisted ownership, socket destination and transaction behavior.

Before the bounded slice can be called implemented, tasks 3.1-3.4 still require: the observed pre-fix keyed-endpoint failure; exact shared owner-fence ownership and ordering; one committed pair under forced contention; neither row after pre-commit failure; actual committed-loss metadata-only recovery; current revoked/expired/foreign-scope denial; final generated HTTP/client/component behavior in both ingress profiles; and generated receipt migration/uniqueness proof on all five engines. The actual retained-field inventory, cleanup/erasure behavior and safe operational diagnostics must agree with the documented purpose. No passing or failing product run is asserted here.

At intake the triad still said R3 I-VSD was stale and user approval was awaited. During revalidation the lead updated context to record execution authorization, a reported baseline pass and the in-progress Red investigation; that status-only update does not change the reviewed design or establish a passing issuance result. Remaining stale report/approval references predate this report and the explicit 2026-10-05 execution instruction. The lead owns synchronization of the execution ledger and the absent worktree backlog copy; this track cannot edit them. This is a working-memory handoff gate, not an unresolved provider-policy choice or permission to omit the 25 launch-blocking phases.

## Escalation Needed

- Authored planning decisions: minimal intentional public publisher identity and operator-provisioned external keyrings are selected recommendations under best judgment. Questions were issued asynchronously; public identity timed out unanswered. No recommendation is represented as user approval. A differing answer invalidates the affected mappings and requires focused revalidation.
- Implementation authorization: the user's explicit 2026-10-05 full-implementation instruction in the named worktree satisfies the earlier approval prerequisite for the active R3 slice. This report does not authorize other tracks to edit additional paths, commit, deploy or release; this assessment track has report-only ownership.
- Before Green: exact shared issuance/revocation fence and additional-owner paths, if needed, must be reconciled under tasks 3.1/3.2. If the proposed implementation weakens current authority, permits secret readback or changes retention purpose, stop that change for focused revalidation rather than substituting an optimistic read.
- Deferred defaults: public identity/publication policy and external keyring recommendations remain unapproved recommendations where the evidence previously recorded no answer. Resolve any material choice before its owning phase is promoted; this slice introduces no such choice.
- Before release: named operator/security approval of actual topology, realm MFA/recovery, egress, limits, scans, key/identity/media restore and incident response evidence.
- Destructive cleanup or credential rotation needs exact target identification and explicit authorization at execution.
- Qualified legal/privacy and Sunni scholarly review only where a concrete jurisdictional or religious-legal question arises; none is manufactured by ordinary security remediation.

## Evidence Reviewed

### R3 Revalidation Evidence And Exact Revision Binding

All paths in the following table are relative to `/home/amir/ISLAMU/Github/Event/.worktrees/event-api-security-remediation` unless an absolute path is given. SHA-256 values bind the reviewed document bytes, not runtime correctness. Worktree HEAD was `7de34469786d368728c8db6f7211cefcd383d35f`; the triad's synchronized baseline `bf030e413810cf69043b0880e1da5fac3c2be54b` is its recorded earlier source baseline, not a claim that the worktree still has that HEAD.

| Evidence ID | Artifact and reviewed scope | SHA-256 / authority |
| --- | --- | --- |
| R3-E01 | `dev/active/event-api-security-remediation/event-api-security-remediation-plan.md`; R3 scope, D03/D14, Sections 6.1-6.2, 7.0, 9-9.5 and 14.1-16 | `c9bddf659418283faeb7f1131251436ad7fae7a7496281aefe06465b5b1f2b26` |
| R3-E02 | `dev/active/event-api-security-remediation/event-api-security-remediation-tasks.md`; five active tasks, exact ownership and acceptance | `34494975ce06e3830917a52ca813b22950b14fade4ba8ddf7702880568664fb8` |
| R3-E03 | `dev/active/event-api-security-remediation/event-api-security-remediation-context.md`; current-state handoff, lead's execution-status update and qualified historical evidence | `8f62cd1f69276901e202e5a200ff042d81839e96c22bcea13034f36ab3056fc2` |
| R3-E04 | `/home/amir/ISLAMU/Github/Event/dev/backlog/event-api-security-launch-programme.md`; shared-root source, ownership/admission, normative corrections, all deferred task identities and phases 19-21 | `ceb18a43ccb33ab82ea5ac03be5dfe8169f842fada4907c8c589b60f7349803a` |
| R3-E05 | `islamic-value-sensitive-design/consultations/i-vsd-event-api-security-compliance-check.md`; unchanged inherited source identity, focused F016/F025-F027 and preserved full obligations | `c01031ea05b993965b1104cf203977414168ca965d2a09d6512c4feb0cffeee4` |
| R3-E06 | This assessment before revalidation; complete inherited finding/class tables and review history preserved | `57d9a7029354abc4d9258b77882346e86924e8e4081e4f9e5be4dafa9343f3bd` |
| R3-E07 | Explicit request received 2026-10-05: revalidate R3 for phase 3; implementation authorized in the worktree; other phases remain launch blockers; no invented default approval | Conversation authority; overrides historical pending-approval wording only within the stated scope |

The worktree-relative `dev/backlog/event-api-security-launch-programme.md` did not exist at intake. R3-E04 is an explicit external-to-worktree local evidence source, not an asserted local copy. Its inherited R2 execution packets still require promotion and fresh source/review binding; checking their preserved task identities does not approve or execute those packets.

Revalidation used the local I-VSD skill, integration/report/scope/readability contracts, evidence-level and data-governance resources, AGENTS/local scratch constraints and Quick Reference. The registry has no dedicated report-only I-VSD intent; the explicit single-file scope and Tier 4 documentation verification apply. No new dependency, external research, product-code inspection or external write was needed. Historical evidence below is retained as historical, not silently refreshed.

### Historical R1/R2 Evidence

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

The historical shared evidence packet includes parent investigation plus three bounded read-only subsystem investigations: identity/custody, egress/parsing and host/isolation/replay. Knowledge-graph unavailability and native source fallback describe that historical intake, not a new structural audit. R3 adds the exact document packet above and the explicit current user instruction. This revalidation edits only the permitted assessment; product code, triad, backlog and other contributors' work remain untouched by this track.

## Planning Handoff

- Workstream: event-api-security-remediation
- Status: current; disposition plan-aligned for r5 design, not runtime/security/release certification
- Reviewed input: event-api-security-remediation-r5-cto-20261007, complete plan/tasks/context and existing assessment; exact hashes in R5 Revalidation Evidence
- Findings and mitigations: API IVSD-F001–F031 / IVSD-M001–M031 retained with original qualified lifecycle; provider `PC:IVSD-F001–F006` / `PC:IVSD-M001–M006` retain their original assessment identity, not renumbered as API findings
- Required plan mappings: plan Sections 3/9 retain every original scenario, reference/family and BUG/VULN row; Section 6 maps each legacy phase to current execution order; tasks retains all 127 outstanding and five delivered original records plus 31 additive records, with 19 provider aliases and 28 supplementary scenarios. No mapping points to a deleted brief or requires another active plan.
- Revalidation result: current / plan-aligned for the unified design and custody/publication reconciliation; no finding is technically closed by relocation. The API-key disclosure aspect is delivered in merged PR71; broader current-resource authority/business recovery/operator evidence remain open.
- User/technical authority: full programme execution explicitly instructed on 2026-10-07; CTO refinements Applied & Aligned. Historical pending-implementation wording does not renew an approval request. The old R3/PC handoff's per-follow-up triad topology is superseded, not its provider duties or actual proof requirements.
- Escalations: focused review before materially changing retention/erasure/disclosure/provider defaults or roots; real version-bound role/worker/socket/SDK/process/host proof before corresponding claims; actual supported-profile/operator/independent evidence before release. Authored privacy/keyring recommendations are not a claimed user answer.
- Refresh triggers: changed authority, lifetime, roots, purpose, output, default, cleanup/restore ordering, publication ownership, identity disclosure, profile budgets, scenario ownership or evidence requirements. Merely renumbering/retargeting unchanged ownership cannot certify implementation.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-10-01 | none | draft | Exhaustive security-remediation planning requested | Consultation, current revision and shared source packet; final triad mappings pending |
| 2026-10-01 | draft | current | Completed triad revalidated with explicit recommended defaults | 27 mitigation mappings, 34 scenarios, 24 phases, 209 reference assignments and candidate-bound operational admission; disposition plan-aligned, not release approval |
| 2026-10-02 | current | current | User added all ten bug and twelve vulnerability classes; R2 triad revalidated | 26 phases, 50 scenarios, 132 tasks, unchanged 209-reference coverage, 31 qualified finding mappings and all 22 distinct class obligations; plan-aligned, not implemented or release-approved |
| 2026-10-05 | current for R2; stale for R3 | current | Substantive planning-mode revalidation of R3 split, minimal receipt retention, lost-disclosure remedy, shared revocation ordering and ownership | R3-E01-R3-E07; five active issuance tasks and 127 mandatory deferred tasks; current / plan-aligned design only, no runtime closure or deferred-default approval |

| 2026-10-06 | current for bounded R3 | current | Substantive r4-unified revalidation after user-directed programme reunification and delivered PR71 baseline | Complete triad hashes above; all original and qualified provider mappings retained; 29 outstanding phases, 158 records; plan-aligned design only |
| 2026-10-07 | current for r4; stale for r5 | current | Complete r5 revalidation of key lifecycle, lease identity, non-reopening recovery, safe cutover order, all 31 mappings and bounded delivery admission | Exact r5 triad hashes; plan-aligned design only; execution authorization separate from operational/release approval |
