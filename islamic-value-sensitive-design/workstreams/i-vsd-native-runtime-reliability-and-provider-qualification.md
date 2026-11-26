# Native Runtime Reliability and Provider Qualification — I-VSD Planning Assessment

Last Updated: 2026-11-22

## Review Metadata

- Mode: planning
- Subject: native startup, authority/privacy behavior and trustworthy recovery evidence
- Workstream: native-runtime-reliability-and-provider-qualification
- Report kind: integrated workstream assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-22
- Reviewed input: canonical plan/tasks/context revision 1; merged develop `9abca5e5c70f9758c6eae97cd61f22c36663544d`
- Supersedes: archived `i-vsd-pr79-testing-assurance-follow-up.md` for this remaining-work scope only

## Executive Summary

The plan addresses concrete risks to self-hosters, administrators and people whose
identity or private information is processed: an installation that fails before
readiness, permission workarounds that weaken isolation, incomplete erasure, and
recovery or assurance reports that claim more than they establish. It keeps the
provider responsible for application authorization, honest operational claims,
private evidence handling and safe cleanup of resources it owns.

The plan is aligned because each material risk below maps to required observable
behavior and named tasks. This permits implementation planning; it does not certify
the implementation, privacy compliance, capacity, enterprise readiness or a religious
ruling. Actual native/provider/browser execution and changed-boundary review remain
required. The old benchmark waiver and absence of Loki do not excuse missing worker,
privacy, session or controlled-load correctness evidence.

## Scope

Five PR outcomes: fresh SQLite startup; complete PostgreSQL purpose readiness;
privacy lifecycle and sixteen uncovered fields; current client/transport contracts;
original worker/session recovery and attributable browser/load/native results.
Broader EF-first replacement, release pipeline modernization, independent identity
topologies, optional Loki and data-pump development retain their existing owners.

## Claim Boundary

This assessment uses current repository source, retained execution reports and
source-free official framework facts. Source inspection and prior receipts cannot
establish unexecuted current behavior. Known setup failures do not prove that later
business assertions failed, nor that erasure is safe. No scholarly or legal conclusion
is issued. New retention/disclosure or licensing decisions require their appropriate
owner; no such new policy is invented here.

## Findings

### IVSD-F001 — Unsupported assurance can mislead release decisions

Lifecycle: open. Severity: high. Claim type: provider-controlled assurance risk.
Principle/domain: truthfulness and trustworthiness; engineering operations.
Stakeholders: operators, maintainers and people relying on their deployment.

An operator can reasonably interpret “passed” as proof that the actual worker,
session or privacy behavior ran. Historical module/parser/ownership results and
health-only restart witnesses do not establish that claim. The merged work retained
unqualified journeys and a security child exit failure; neither a merged status nor
a CI badge resolves those outcomes. The provider controls result wording, selection,
candidate/profile attribution and whether failures remain visible.

Evidence: plan §2 source/receipt boundaries, corrected restart test and current
security workflow; validation level is source inspection plus historical reports.
Mitigation IVSD-M001: require R11, T8.2–T8.4 and every PR acceptance to reconcile
actual discovered/terminal results, unrun checks and current candidate. Owner:
selected capability implementer, with CI owner for shared contracts. Escalation:
any newly requested acceptance waiver must name its scope before claiming completion.

### IVSD-F002 — Assurance artifacts can disclose private information

Lifecycle: open. Severity: high. Claim type: privacy/exposure risk.
Principle/domain: confidentiality and prevention of harm; identity and erasure.
Stakeholders: users, administrators and self-hosters.

Runtime diagnosis touches cookies, key authorities, configured credentials and
subject-linked records. Useful failure evidence must not publish those inputs.
The preserved worktree/run archives may contain private configuration, so archiving
them is a custody operation rather than approval to add them to Git. Shared Data
Protection storage also needs deliberate access isolation.

Evidence: private preservation inventories, authority fixtures, provider metadata
tests and the official Data Protection functional reference. Validation: source and
custody inspection; no fresh emitted-artifact leak test performed in planning.
Mitigation IVSD-M002: R5/R9/R10, T4.1–T5.2, T7.2 and T8.1–T8.3 require bounded
redacted output, private raw evidence and actual emitted-artifact canary checks
where the journey produces artifacts. Owner: privacy/runtime implementer.
Escalation: new data disclosure, evidence publication or external secret authority.

### IVSD-F003 — Cleanup can harm unrelated work

Lifecycle: open. Severity: high. Claim type: resource-custody risk.
Principle/domain: protection of others' property and prevention of harm; hosting.
Stakeholders: other developers, tenants and operators sharing a daemon or host.

A timeout or killed child is not permission to delete its apparent neighbors.
Workers, volumes and authoritative erasure history may outlive an application
process. Positive ownership and actual foreign-consumer checks must govern cleanup;
the programme must not recreate old destructive drafts merely to obtain a pass.

Evidence: `OwnedAppHostFixture`, distributed assurance guidance and retained cleanup
review. Validation: source/historical focused evidence; final journey cleanup unrun.
Mitigation IVSD-M003: R8/R10, T1.2 and T7.1–T8.4 retain positively owned cleanup,
foreign-resource preservation and original authoritative history. Owner: hosting
implementer. Escalation: destructive action outside disposable proven-owned storage.

### IVSD-F004 — Passing tests must not replace native authority

Lifecycle: open. Severity: high. Claim type: security/privacy boundary risk.
Principle/domain: justice, trust and minimization of harm; authorization and privacy.
Stakeholders: tenants, administrators, erased and retained subjects.

Grant expansion, helper-role enrollment or replacing a selected native authority
with a permissive fixture can hide real isolation failure. Conversely, database
business workflows are not the accepted final architecture: C# owns authorization,
state and concurrency, while fixed permissions, constraints and tenant RLS support
legitimate application operations. Erasure must honor legal holds and exact subject/
claim custody. The unclassified fields need actual treatment, not a cosmetic allowlist.

Evidence: accepted application-owned persistence ADR, actual Global EF query/test,
settings installer, erasure tests and sixteen-field inventory report. Validation:
current source and prior failures, not whole-provider qualification.
Mitigation IVSD-M004: R2–R6 and T2.1–T5.2 require real-role/tenant/race denial,
coherent EF-first repair, authority-first erasure and verified field dispositions.
Owner: security/privacy capability implementer. Escalation: a genuinely new trust,
retention or disclosure boundary; not ordinary fixed grants for an accepted purpose.

### IVSD-F005 — Metadata and UI can mislead or exclude users

Lifecycle: open. Severity: medium. Claim type: privacy/interaction risk.
Principle/domain: dignity, truthful representation and accessibility; user interfaces.
Stakeholders: attendees and administrators, including keyboard and RTL users.

Declared age is private metadata, not verified age or access authority. Client
representation failures must not be “fixed” by showing unauthorized actions,
discarding validation or replacing server HAL links with local roles. A valid
classification read must retain original native coordinates and private frozen
state through later writes. Browser module success does not prove actual-host focus,
wrong-tenant denial or private-input scrubbing.

Evidence: historical client leads, current service/component sources and actual-host
browser scenarios. Validation: source/retained failures; current cluster execution
is unresolved. Mitigation IVSD-M005: R7/R10, T6.1–T6.3 and T8.2 require current
wire/rendered outcomes, HAL negatives, date-only semantics and real-host accessibility/
privacy checks. Owner: client/runtime implementer. Escalation: changing age collection,
verified-age semantics or user action authority, rather than repairing representation.

### IVSD-F006 — Invalid measurements waste resources and misstate capacity

Lifecycle: accepted. Severity: medium. Claim type: measurement/operational risk.
Principle/domain: stewardship and truthfulness; observability and performance.
Stakeholders: self-hosters and shared-host users.

A valid metric document does not identify the process that produced it. Saturated
load generation or missing pressure data can make capacity claims false. Repeated
broad test runs also consume time and resources without proportionate new evidence.
The accepted benchmark waiver removes adaptive benchmark qualification only; it
does not turn uncontrolled load into a production capacity claim.

Evidence: `RuntimeMetricsWireTests`, `K6LoadRunner`, retained programme constraints
and maintainer's 55 GiB ceiling. Validation: source/constraints, not new measurement.
Mitigation IVSD-M006: R10 and T8.1–T8.4 require runtime identity joins, correctness
and generator-pressure limits; plan §7 batches tests and records actual durations.
Owner: runtime implementer. Escalation: new production SLA, benchmark threshold,
tool licensing/distribution or resource-intensive external target.

## Recommendations

Implement the five capability outcomes with their own acceptance before merge.
Keep every required retained obligation visible until delivery is reconciled.
Reject grant-widening shortcuts, a second assurance framework, source-text testing,
an invented benchmark pass and a catch-all final QA phase that first validates
earlier merged code. Preserve the accepted C# authority decision and no-compatibility
development mode. No new infrastructure/provider choice is required to start.

## Stakeholders

Users whose identity and private metadata are processed; erased/retained subjects;
tenant and instance administrators; self-hosters; contributors sharing host/daemon
resources; privacy/security and CI/release maintainers.

## I-VSD Principles And Domains

Trustworthiness in claims, confidentiality, justice in authorization, protection of
others' resources, dignity/accessibility and proportionate resource stewardship.
Applied to provider-controlled identity, privacy, hosting, measurement and engineering
operations. These engineering mappings are not scholarly certification.

## Common Overlooked Failures And Outcomes

Empty database `Migrate` differs from `EnsureCreated`; setup failures never execute
the intended business assertions; table access differs from applicable row policy;
helper-role tests can hide production denial; HTTP failure can leave an old process
alive; retry can accidentally create replacement work; private archives can leak
when force-added; inventory coverage can hide unimplemented treatment; metric
syntax can hide the wrong emitter. Each has an explicit R1–R11/task mapping.

## Validation Gaps

Current native provider, actual-role, erasure, client, worker/restart, actual browser,
load and selected security acceptance remain unexecuted in this planning task.
Independent changed-workstream review occurs in each implementing PR. No claim of
full assistive-technology certification, verified age or capacity is made.

## Escalation Needed

None blocks the present plan. Reopen before implementation of a genuinely new
retention/disclosure decision, trust boundary, package distribution/license choice,
public evidence publication or destructive non-owned operation. Maintain established
contracts meanwhile; do not ask again about already accepted fixed-purpose rights.

## Evidence Reviewed

- Merged develop object and source paths enumerated in plan §2 and tasks.
- Accepted [application-owned persistence ADR](../../docs/internal/adr/ADR-application-owned-persistence.md).
- [Native runtime qualification](../../docs/internal/testing/NATIVE_RUNTIME_QUALIFICATION.md), including source-free official research register.
- [Owned distributed assurance](../../docs/internal/testing/DISTRIBUTED_ASSURANCE.md).
- Canonical plan/tasks/context revision 1 and consolidation source accounting.
- Archived old workstream assessment and remaining-obligation reports; historical
  results remain historical and do not certify a new candidate.

## Missing Evidence

All current execution gates listed under Validation Gaps; emitted-artifact privacy
checks for final runtime outputs; actual current scanner/result disposition. Remote
tracking deduplication is an execution prerequisite, not a privacy pass criterion.

## Context Inventory

Shared repository evidence packet; five bounded PR outcomes; accepted greenfield,
application authority, no-Loki, benchmark waiver, smart batching and owned-cleanup
decisions; field and topology scope; source-free official facts. No private values,
third-party implementation snippets or speculative retention policy were consumed.

## Review Lifecycle

| Date | Previous | New | Trigger | Evidence |
|---|---|---|---|---|
| 2026-11-22 | none | draft | Fresh remaining-work consolidation | Source/obligation packet at merged develop |
| 2026-11-22 | draft | current / plan-aligned | Revalidate all six finding/mitigation mappings against triad revision 1 | Plan §9 and named tasks; implementation still unproved |

## Planning Handoff

- Workstream: native-runtime-reliability-and-provider-qualification
- Status: current
- Reviewed input: canonical triad revision 1
- Findings and mitigations: IVSD-F001–F006 → IVSD-M001–M006
- Required mappings: plan §9 binds each pair to R1–R11 and exact task ranges.
- Escalations required before: new policy/trust/license/destructive boundary only.
- Refresh triggers: changed collection/retention/disclosure/defaults/authority,
  evidence publication, topology, cleanup scope, measurement claims or mapped tasks.
  Update the report after actual execution before claiming mitigations resolved.
