# I-VSD — PR 79 Testing Assurance Follow-Up

Last Updated: 2026-11-22
Report revision: 6

## Review Metadata

- Mode: plan-review
- Subject: Trustworthy testing assurance and owned execution
- Workstream: pr79-testing-assurance-follow-up
- Report kind: workstream assessment
- Report status: draft
- Disposition: changes-required
- Evidence cutoff: 2026-11-22
- Reviewed input:
  - dev/backlog/pr79-testing-assurance-follow-up.md
  - Planning triad revision 1, as supplied in chat and saved locally
  - Planning triad revision 9, 2026-11-20
  - Working-tree plan revision 13 and execution context, 2026-11-21
  - Plan revision 15, maintainer deferral/merge decisions, 2026-11-22
  - Published candidate `cb6f084285` and independent bounded security/privacy
    and correctness/documentation reviews, 2026-11-22
  - Observer repair `26e34c1991`: exact image-declared mount validation,
    no parent volume deletion, and passing native interruption control
  - Subsequent ownership 77/77 and browser-security modules 36/36; standalone
    native SQLite migration failure reproduced on untouched `ed15a99dd7`
  - Standalone relational provider/host-graph fixture 22/22 and resource-cookie
    consumers 2/2; stored session acceptance and server-owned correlation verified
  - [ADR-021](../../docs/internal/adr/ADR-021-instance-scoped-hal-authorization.md)
- Supersedes: none

This update reconciles the original findings with actual delivery, maintainer
decisions and two independent bounded reviews. Those reviews did not find a new
confirmed exploitable authorization/privacy regression in the inspected paths;
they did identify documentation corrections and missing runtime evidence.
They do not establish exhaustive source coverage or runtime certification.

## Executive Summary

This work protects people indirectly through the reliability and honesty of
the evidence used to release ISLAMU Event.

The most important responsibilities are:
- Do not claim a runtime, security or performance property that was not proved.
- Do not expose private user information through diagnostic artifacts.
- Do not damage unrelated developer/operator resources during cleanup.
- Do not weaken authorization or custody to make tests pass.
- Preserve accessible and non-misleading user interactions.

The proposed plan addresses those responsibilities through exact-candidate
evidence, positive ownership, private artifact custody, native authority tests
and explicit incomplete/unavailable outcomes.

The independent implementation review is bounded source review. The recorded B20
authorization slice passed 75/75 and the category relationship class passed
5/5 on earlier sources. The earlier B22 data-protection witness passed 2/2 while
retaining the developer volumes, but review found that its stop proof was too
weak; the corrected witness has not run. A later tenant-discovery run was interrupted and is not a
passing result. These receipts are scoped observations, not proof of the final
candidate. Current ownership and browser module checks passed 77/77 and 36/36.
Standalone's relational fixture passed 22/22 provider/host-graph cases and 2/2
resource-cookie consumers. It uses SQLite `EnsureCreated` and an explicit
in-process session authority; these are not native migration or distributed
Redis qualification. Native staging SQLite migration fails identically on
untouched upstream and is quarantined in develop's backlog. Security CI on
`75c8e5097d` now exits 3 without the former observer-failed suffix; no admitted
full-lane receipt is claimed.
Actual-host browser, worker/restart, load and final provider/scanner execution
remain unqualified; independent source review is recorded below. Its draft / changes-required
disposition permits planning reconciliation, not an implementation-complete,
security or merge-readiness claim.

## Scope

Included:
- Testing and release-evidence admission.
- Client metadata and private transport.
- Native grant custody and durable recovery.
- Process/container/resource cleanup.
- Benchmark and load measurements.
- Browser/BFF/worker/provider qualification.
- Maintainer/operator communication.

Excluded:
- Religious-legal rulings.
- Product or Sharia certification.
- Authentication-provider strategy redesign.
- New monetization, moderation policy or payment design.
- Unrelated PR 80 feature completion.

## Claim Boundary

This is provider-responsibility design reasoning.

It is not:
- A fatwa.
- Proof of security or ethical outcomes.
- A substitute for native execution.
- A substitute for qualified scholarly authority where a religious-legal
  question arises.

Selected lenses include truthful accountability, confidentiality, avoiding
harm, justice in authority handling, inclusive access and resource stewardship.
Industry guidance supports technical operationalization; it does not replace
the project's normative authority.

## Findings

### IVSD-F001 — Unsupported assurance can mislead release decisions

Lifecycle: open
Severity: high
Claim type: operational responsibility
Principle/domain: truthful accountability; governance and evaluation
Affected stakeholders: users, maintainers, adopters and operators

Practical consequence:
A green summary assembled from stale or different binaries can lead operators
to deploy software whose important properties were never verified.

Provider-controlled decision:
What evidence is admitted and what completion claims are published.

Evidence:
The supplied backlog assessment distinguishes old failures, static gaps and
missing final proof. Historical counts and cancelled/skipped jobs are not
current passing evidence.

Validation level:
Supplied repository/document evidence only; current execution absent.

Mitigation: IVSD-M001
Require exact candidate/input/invocation binding, exact discovery, actual
execution and cleanup settlement before admission.

Owner:
Implementation lead and release reviewer.

Next check:
Fresh-candidate reconciliation and final admission.

Escalation:
Unresolvable required evidence blocks completion rather than being relabeled.

### IVSD-F002 — Assurance artifacts can disclose private information

Lifecycle: open
Severity: high
Claim type: privacy responsibility
Principle/domain: confidentiality and harm prevention; data and operations
Affected stakeholders: account holders, administrators and test participants

Practical consequence:
Raw browser, transport or diagnostic evidence may expose credentials, receipt
tokens, birth dates or private metadata.

Provider-controlled decision:
Artifact collection, retention and public projection.

Evidence:
The supplied backlog assessment requires private roots, safe evidence projections
and preserved birth-date/declaration privacy.

Validation level:
Repository contracts reported inspected by the planner; actual artifact privacy
not validated.

Mitigation: IVSD-M002
Retain raw evidence privately, use bounded value-safe diagnostics and admit only
the existing safe public projection.

Owner:
Privacy/security reviewer and assurance owner.

Next check:
Client privacy regressions and final browser/evidence inspection.

Escalation:
Any new secret authority, collection field or public exposure needs an explicit
decision before implementation.

### IVSD-F003 — Cleanup can harm unrelated work

Lifecycle: open
Severity: high
Claim type: operational harm prevention
Principle/domain: non-harm and stewardship; infrastructure operations
Affected stakeholders: developers, neighbouring workloads and operators

Practical consequence:
Cleanup by PID, name or broad daemon commands can terminate or delete resources
that do not belong to this test invocation.

Provider-controlled decision:
Ownership proof and fallback cleanup authority.

Evidence:
The supplied backlog assessment identifies child-tree intermittency, parent
fallback gaps and an old volume with unknown ownership.

Validation level:
Static gap/historical observation; current native behaviour unvalidated.

Mitigation: IVSD-M003
Use positive lifetime/resource identity, settle observers and preserve foreign
or unknown resources. Retain incomplete evidence when safe cleanup is uncertain.

Owner:
Owned-execution implementer and independent security reviewer.

Next check:
Controlled killed-child and foreign-consumer native tests.

Escalation:
No global prune, foreign deletion or broad process termination is authorized.

### IVSD-F004 — Passing tests must not replace native authority

Lifecycle: open
Severity: high
Claim type: security and privacy responsibility
Principle/domain: justice and entrusted authority; architecture and workflow
Affected stakeholders: tenants, moderation participants and erased subjects

Practical consequence:
Fake receipts, widened authority or borrowed transaction custody could make
tests pass while violating actual access and privacy boundaries.

Provider-controlled decision:
Native issuance, settlement, recovery and erasure semantics.

Evidence:
The supplied planning evidence reports receipt issuance failing before downstream
assertions; the backlog requires original actor/tenant/transaction custody and
anti-resurrection.

Validation level:
Supplied source/backlog evidence; current native outcome absent.

Mitigation: IVSD-M004
Execute adversarial native authority tests before repairs; preserve C#
application authority, transactions, outbox and uncertain-outcome handling.

Owner:
Native workflow owner and security/privacy reviewers.

Next check:
Selected-provider reproduction and final supported-provider assurance.

Escalation:
New trust boundaries or unapproved privileges require a maintainer decision.

### IVSD-F005 — Metadata and UI can mislead or exclude users

Lifecycle: open
Severity: medium
Claim type: UX responsibility
Principle/domain: truthful interaction and inclusive access; UX
Affected stakeholders: users, including keyboard and RTL users

Practical consequence:
A declared-age label may imply verified status or grant actions it does not
authorize. Broken focus or RTL behaviour can prevent meaningful use.

Provider-controlled decision:
Metadata wording, action gating and accessible interaction.

Evidence:
Declared-age rendering cases failed historically; browser keyboard/focus/RTL
acceptance remains required.

Validation level:
Supplied contract evidence; current render/browser execution absent.

Mitigation: IVSD-M005
Preserve declared-versus-verified meaning, HAL-only actions, private rendering,
accessible names/focus and actual RTL geometry.

Owner:
Blazor implementer and accessibility reviewer.

Next check:
Settings component class and final registered browser journeys.

Escalation:
Do not expand into an unrelated age-verification or translation programme.

### IVSD-F006 — Invalid measurements waste resources and misstate capacity

Lifecycle: open
Severity: medium
Claim type: operational stewardship
Principle/domain: resource stewardship and truthful evaluation; performance
Affected stakeholders: operators, adopters and shared infrastructure owners

Practical consequence:
Unqualified metrics, smoke benchmarks or unachieved arrival rates can produce
misleading sizing recommendations and unnecessary infrastructure cost.

Provider-controlled decision:
Benchmark mode, attribution, workload bounds and interpretation.

Evidence:
Adaptive worker qualification and native exporter attribution remain open.
The backlog distinguishes smoke from baseline and unavailable from zero.

Validation level:
Official interface/documentation facts and repository evidence reported by the
planner; no baseline or native measurement executed.

Mitigation: IVSD-M006
Use genuine bounded adaptive runs, owned metric attribution, offered/completed/
dropped accounting and explicit inconclusive results.

Owner:
Performance assurance owner and release reviewer.

Next check:
Native benchmark and Programme load qualification.

Escalation:
Do not broaden machine-wide load or promise capacity from incomplete evidence.

## Recommendations

1. Resolve the actual candidate and restrictions before product changes.
2. Repair shared root causes rather than weakening failing tests.
3. Keep raw artifacts private and unavailable evidence explicit.
4. Require positive resource ownership before cleanup.
5. Preserve native authority, durable recovery and erasure boundaries.
6. Complete real browser/accessibility and bounded performance evidence at
   the approved final gate.

Rejected alternatives:
- Treating stale green counts as current proof would conceal missing execution.
- Cleanup by PID/name or daemon-wide prune could harm unrelated workloads.
- Fake receipts or broadened permissions would replace the authority being tested.
- Smoke benchmarks or invented zero measurements would misrepresent capacity.

These alternatives are rejected in the supplied plan's architecture section;
this report records their provider-responsibility consequences.

## Stakeholders

Direct:
- Maintainers and contributors executing assurance.
- Operators using release and sizing evidence.
- Users whose private data or access boundaries are exercised.

Indirect:
- Other workloads sharing a daemon or machine.
- Future adopters relying on documented support claims.

No stakeholder interviews or deployment observations were collected.

## I-VSD Principles And Domains

Applied:
- Truthful accountability in release claims.
- Confidentiality and data minimization.
- Avoidance of operational harm.
- Justice in tenant/actor authority.
- Inclusive access.
- Resource stewardship.

Domains:
UX, architecture, data, operations, governance, documentation and evaluation.

Business model, AI ranking and monetization are not changed by this workstream.

## Validation Gaps

- Current upstream/PR state.
- Current native execution and provider behaviour.
- Actual final private/public artifact review.
- Current scanner disposition.
- Stakeholder and operational feedback.
- Final implementation revision.

## Escalation Needed

No religious-legal question has been identified in this technical scope.

Operational escalation is required for:
- Still-applicable PR 80 execution restrictions.
- New trust boundaries or credential authorities.
- Destructive cleanup.
- Inability to meet a mandatory acceptance obligation.

The supplied handoff records prior approval of the final-only native
verification scheduling exception. This document update does not itself
authorize execution or waive PR 80 restrictions.

## Evidence Reviewed

Evidence update (2026-11-21): the worktree execution context records category
relationship tests 5/5, tenant-creation permission tests 25/25, and the earlier
B22 data-protection witness 2/2. That B22 result cannot prove the intended restart
because the previous test accepted a health timeout without a terminal state; its
corrected witness remains unrun. The subsequent tenant-discovery cohort was
interrupted without a result. These are ledger-reported receipts; this report
does not independently certify their logs or the final committed candidate.
An additional mixed-batch regression demonstrated that an empty tenant profile
could reach an instance-admin shortcut. The local provider now denies mixed
instance-scope batches with an empty resolved tenant, while instance settings
and tenant creation retain their separate authority path. The runtime provider
may split dedicated-resource and discovery checks before delegation, so this is
not a blanket claim about every top-level batch. An experimental runtime routing
change was removed because an unbound tenant accessor can resolve a tenant lazily;
the existing tenant-specific Cerbos selection is retained. Whole-workstream security review and the
final candidate's provider parity remain necessary before release. The final local
authorization regression class passed 9/9, including denied non-administrator
and missing-tenant cases and selected-tenant behavior; two focused independent
source reviews found no further proven bypass within that local-provider scope.

Implementation refresh (2026-11-20):
- B20: `PrivateInstanceManagementHttpTests` 24/24 and the complete four-class
authorization slice 75/75, with the incomplete-setup host origin and the
instance-scoped HAL batch root causes repaired. Decision: ADR-021.
- B21: Runtime bootstrap reads use `IInstanceBootstrapStateReader`; mutation and
startup owners retain the tracked repository. Category/category-type Runtime DML
is narrow and attested; the detached read-modify-write guard passed 9/9.
- B19: the SECURITY DEFINER recipient projection and its native drift/authority
checks passed 4/4 in the prior recorded run.
- B22: the cleanup-proof retry and concurrent-removal settlement are implemented
in `OwnedAppHostFixture`; the native Distributed witness is running and no result
is claimed yet.

Evidence carried forward from the supplied planner assessment:
- PR 79 backlog and its explicit evidence limitations.
- PR 80 overlap/restriction handoff.
- Relevant repository contracts, project map and testing rules.
- Existing source/test owner locations.
- Planning triad revision 1.
- Sanitized official documentation conclusions recorded in plan.md.

Document-update review:
- Supplied four-document text.
- Local planning/I-VSD artifact contracts.
- Stable finding/mitigation, behaviour and task mappings.

Independent security/privacy review on `cb6f084285` inspected instance-management
exemptions and credential-cutover authority, local permission-batch fail-closed
behavior, administrative EF enumeration and named soft-delete filters, recipient
projection/verified evidence, accepted Global-by-ID delivery scope, secret-binding
scope and prepared-write transaction custody. No new confirmed exploitable
regression was found in that bounded inspection. The review was risk-based,
not an exhaustive review of all changed files; it ran no builds or tests.

Independent correctness/documentation review confirmed the paired release docs
and fragments, identified stale ledger/assessment claims and the implied downgrade
promise in the runbook, and retained worker, provider, browser/load and native
observer gaps. The runbook now requires fresh development provisioning for an
earlier candidate. Neither review converts missing provider evidence into a pass.

Review of the subsequent observer repair rejected an unsafe draft that inferred
volume-deletion authority from a new mount. The delivered `26e34c1991` API lane
never deletes volumes: it removes only proven container IDs with volume removal
disabled, and withholds cleanup admission if observed volumes remain. The real
interrupted-owner control passed 1/1, preserving a newly created foreign named
volume and both anonymous volumes. Ten parser cases, the existing native-resource
contract and sixteen consumer-boundary cases passed. This control proves that
specific cleanup boundary, not complete security-lane or product qualification.

## Missing Evidence

Independent bounded workstream review has been performed as described above;
exhaustive source coverage and whole-runtime security/privacy proof remain absent.
Worker recovery, browser journeys, K6 load, provider matrix and final
scanner/Sonar evidence are not complete at this evidence cutoff. The corrected
B22 witness remains unrun and does not certify cleanup or restart behavior.
The maintainer accepted deferral of unresolved PostgreSQL qualification and does
not require every CI check green for a practical merge decision. Those choices
do not turn the reproducible trusted-settings installation defect or missing
runtime evidence into successful qualification. The development-checkout backlog
retains concrete follow-ups. No Loki instance or Loki acceptance is required.
No claim of ethical outcome or security certification follows from these reviews.

## Context Inventory

Stable inputs:
- Repository-native architecture and assurance contracts.
- Supplied user rejection of backward-compatibility baggage.
- Supplied record of user approval for final-gate native verification.

Unstable inputs:
- Candidate SHA and PR merge state.
- Current discovery/failure/scanner results.
- Environment/provider/authority availability.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-10-30 | Not persisted | draft | Supplied revision 1 saved with metadata normalization | Supplied triad/report; document-only review, not current-candidate validation |
| 2026-11-20 | draft | draft / implementation evidence refreshed | B20 75/75, category mutation 9/9, B22 repair compiled and native witness started | Planning revision 9, ADR-021, local test logs; independent review and remaining native lanes still open |
| 2026-11-21 | draft | draft | Reconcile later scoped receipts and interrupted discovery run | Worktree context and plan revision 13; final candidate and independent review remain open |
| 2026-11-22 | draft | draft / bounded implementation review reconciled | Maintainer completion and deferral decisions; independent security/privacy and correctness reviews | Plan revision 14 and `cb6f084285`; unverified runtime paths and native-observer repair remain open |

Current status:
Draft, with revision-14 decisions and bounded implementation review reconciled;
full execution of the repaired native observer, runtime receipts and acceptance
remain open. The observer repair itself and its focused custody control passed.

Before implementation:
Refresh evidence and restrictions.

After material plan changes:
Revalidate all findings and mappings.

Before final acceptance:
Bind review to the exact implementation candidate and evaluate actual receipts.

A mapping check is not implementation validation.

## Planning Handoff

Bound planning revision:
PR 79 Testing Assurance Follow-Up triad revision 14, dated 2026-11-22.

- Workstream: pr79-testing-assurance-follow-up.
- Status: draft / bounded implementation review reconciled.
- Reviewed input: triad revision 14 and the bounded candidate reviews above.
- Escalations required before: implementation for unresolved authority/scope;
  release for missing mandatory acceptance evidence.
- Refresh triggers: candidate/ownership reconciliation, material scope or trust
  boundary changes, altered mitigations/task mappings and final runtime receipts.

| Finding | Mitigation | Scenarios | Tasks |
|---|---|---|---|
| IVSD-F001 | IVSD-M001 | B17, B19 | T0.1–T0.5, T8.3–T8.6 |
| IVSD-F002 | IVSD-M002 | B03, B04, B18 | T3.1–T4.2, T7.7, T8.4 |
| IVSD-F003 | IVSD-M003 | B07, B08, B11 | T5.1–T5.3, T7.2–T7.3 |
| IVSD-F004 | IVSD-M004 | B05, B12, B14–B16 | T7.1, T7.5–T7.7 |
| IVSD-F005 | IVSD-M005 | B02, B03, B14 | T2.1–T3.3, T7.7 |
| IVSD-F006 | IVSD-M006 | B09–B13 | T6.1–T6.3, T7.4–T7.5 |

Disposition:
Proceed with fresh-candidate planning reconciliation.
Do not label the report implementation-validated or the workstream complete
until the actual final evidence and review are available.
