# I-VSD: Persistent Plans With Bounded Pull Request Execution

Last Updated: 2026-11-14

## Review Metadata

- Mode: planning
- Subject: Responsibility for preserving engineering commitments across AI implementation, review, merge, and cleanup.
- Workstream: multi-pr-agentic-workflow
- Report kind: provider-responsibility planning assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-14
- Reviewed input: multi-pr-agentic-workflow revision `2026-11-13.r1`, with its approved in-tree local execution override and authored ownership/lifecycle/tracking instruction changes.
- Supersedes: none

## Executive Summary

The maintainer currently entrusts an AI agent with a large implementation plan whose ignored working files move into one execution worktree. If that worktree lives for weeks, completed capabilities remain unavailable through the integration branch. If it is removed before planning information is preserved, future requirements and implementation discoveries can disappear despite the product commits having merged.

The recommended design keeps the complete programme triad in the main checkout and delegates one bounded pull request scope to an execution copy. The agent must account for actual delivery, changed decisions, and outstanding requirements before retiring that copy. This connects Amanah, responsible stewardship of entrusted information, with Sidq, truthful statements about completion and evidence. Bounded acceptance criteria also reduce the burden that one expanding implementation places on other contributors.

The completed triad maps all three findings and their mitigations to named scenarios and implementation tasks. The report remains current and plan-aligned for the same design, now expressed in the shared ownership contract, execution lifecycle and programme tracking guidance. These are authored documentation mitigations, not proof of operational enforcement. The user approved local in-tree implementation with tracking pending; this report itself grants no implementation, cleanup or publication authority. No operational reduction in conflicts or data loss has been measured.

## Scope

Internal AI-assisted engineering governance: planning, scoped execution, session continuity, review, evidence, Plane tracking, and worktree retirement. The affected provider-controlled system is the repository's agent workflow. Product authentication, tenant isolation, business logic, data processing, deployment, and end-user interfaces are unchanged.

## Claim Boundary

This is design reasoning supported by repository-document traceability. It is neither a religious-legal ruling nor certification of ethical, operational, or security outcomes. Existing product security requirements remain binding when later programmes use this workflow.

## Findings

### IVSD-F001: Preserve entrusted requirements before removing execution storage

The maintainer relies on an implementation plan to preserve requested work across sessions. Current execution guidance moves the whole ignored triad into a worktree and describes its disappearance during cleanup as automatic garbage collection. That lifetime is inappropriate when several separately merged PRs still depend on the same plan.

The provider controls where canonical information lives and when cleanup is allowed. Amanah and Non-Harm apply because losing future requirements, discovered constraints, or verification evidence creates foreseeable rework and potentially conceals missing obligations. A merged product branch cannot preserve files that were never tracked.

IVSD-M001 requires persistent canonical ownership, scoped delegation, and preservation before deletion. Its cost is an explicit reconciliation step and a dependency on local working-memory survival. This does not create a backup guarantee: workstation loss remains outside the delivered mechanism.

| Field | Assessment |
|---|---|
| Lifecycle / severity | accepted / high |
| Claim type | Observed governance gap with proposed mitigation |
| Principles / domains | Amanah; Non-Harm / technical, operational, governance |
| Stakeholders | Maintainer, implementing agents, reviewers, future contributors |
| Controlled decision | Canonical location, execution ownership, cleanup prerequisites |
| Evidence / level | E01, E02, E03 / repository-document traceability |
| Mitigation | IVSD-M001: retain the canonical triad; reconcile and preserve execution information before authorized teardown |
| Owner / next validation | Workflow maintainer / scenarios SC-01, SC-05, SC-06, SC-08 and tasks TASK-1.1, TASK-2.2 |
| Escalation | New destructive automation, external storage, or retention authority requires separate alignment |

### IVSD-F002: Report the delivered result without rewriting unmet promises as completion

Implementation and review change a PR's actual contents. Copying checkmarks back into the original plan can hide work that moved, failed acceptance, or never shipped. Approval, merge, validation, reconciliation, and cleanup are different events.

Sidq and Promise-Keeping require the agent to distinguish those events and retain unfulfilled scope. The provider controls status vocabulary, evidence requirements, and whether the original intent is preserved alongside the actual result. IVSD-M002 requires two reconciliation checkpoints and explicit acceptance accounting. A merge with unresolved required behavior remains a visible follow-up obligation; it cannot silently complete the programme.

The evidence proves that the documented current lifecycle lacks this complete distinction. It does not prove that every historical agent failed to preserve it.

| Field | Assessment |
|---|---|
| Lifecycle / severity | accepted / high |
| Claim type | Design responsibility and evidence integrity |
| Principles / domains | Sidq; Promise-Keeping / governance, evaluation |
| Stakeholders | Maintainer, reviewers, downstream implementers |
| Controlled decision | Completion claims, acceptance mapping, treatment of scope changes |
| Evidence / level | E01, E02, E04 / repository-document traceability and design validation |
| Mitigation | IVSD-M002: reconcile for review and after merge; retain approved intent, actual delivery, missing criteria, and subsequent ownership |
| Owner / next validation | Planner and executor / SC-04 through SC-07, SC-10 and TASK-1.2, TASK-2.2, TASK-3.1 |
| Escalation | Removing requested scope or accepting a material unmet criterion requires the maintainer's decision |

### IVSD-F003: Bound one contributor's work without abandoning the whole commitment

The user reports that sprint-sized implementation branches require repeated integration across worktrees. Merely naming a large group of phases a PR would preserve the same burden. Conversely, shrinking a PR by silently sending requested requirements to backlog would abandon the original commitment.

Adl, fair distribution of burdens, and Ihsan, maintainable and reliable engineering, support independently mergeable outcomes with retained programme-level responsibility. IVSD-M003 requires dependency-aware PR selection, a mergeability readiness test, verification for each merged scope, and truthful handling of larger tracking budgets. Additional tasks necessary for the approved outcome are recorded; unrelated improvements do not expand that PR automatically.

This recommendation adds readiness and reconciliation work. It does not promise a universal line limit, a fixed completion time, or elimination of merge conflicts.

| Field | Assessment |
|---|---|
| Lifecycle / severity | accepted / medium |
| Claim type | User-reported operational problem and proposed process constraint |
| Principles / domains | Adl; Ihsan; Promise-Keeping / strategic, technical, operational |
| Stakeholders | Concurrent contributors, reviewers, maintainer, future implementers |
| Controlled decision | PR sizing, dependencies, work in progress, verification placement, tracking granularity |
| Evidence / level | E02, E04, E05 / reported experience, document traceability, design validation |
| Mitigation | IVSD-M003: preserve all required outcomes; execute one eligible PR; scope checks to every PR; retain one item per merged PR |
| Owner / next validation | Planning and review skill maintainers / SC-02, SC-03, SC-07, SC-09, SC-11 and TASK-1.2, TASK-2.1, TASK-2.3, TASK-3.1 |
| Escalation | New trust boundaries, removal of required scope, or more than six new Plane items require explicit alignment |

## Recommendations

Adopt the persistent programme and bounded PR lifecycle through the existing Markdown skill system. Keep programme decisions and accepted outcomes canonical, with the delegated worktree as the sole live detailed ledger for its selected PR.

Reject continued whole-plan relocation because it ties required future work to one branch lifetime. Reject independent editable full copies because they create competing live ledgers. Reject a new orchestration service because the requested behavior can be defined using current native tools and documents; this work authorizes no daemon, lock service, workflow manifest, or custom Plane client.

Preserve applicable local configuration and secrets restrictions. Scope extraction copies engineering requirements and working memory; it does not broaden authority to copy secrets, inspect sibling worktrees, publish internal notes, or delete storage.

## Stakeholders

The maintainer owns requested scope and consequential approval decisions. Agents own faithful execution and accurate reporting within that authority. Reviewers need bounded, coherent changes and final evidence. Concurrent contributors need merged contracts they can use without importing another agent's unfinished branch. Future contributors need surviving requirements and current architectural decisions.

## I-VSD Principles And Domains

The findings apply Amanah, Sidq, Promise-Keeping, Non-Harm, Adl, and Ihsan as defined in the repository's principles resource. Strategic review addresses oversized delivery batches; design review addresses understandable status and decision requests; technical review addresses ownership and dependencies; operational review addresses preservation and recovery; governance review addresses authorization; evaluation review separates proposed behavior from observed results.

Financial, religious-content, monetization, ranking, advertising, and new personal-data decisions are not introduced. No religious-legal conclusion is needed for this bounded workflow change.

## Validation Gaps

No pilot execution, branch lifecycle, destructive cleanup, or programme performance measurement has run. Documentation review can establish coherent instructions and scenario coverage, not enforcement against arbitrary concurrent writers. Canonical ownership is cooperative and must not be described as an atomic lock.

## Escalation Needed

The user approved local implementation, explicitly in-tree and with tracking pending. Linked commits and publication remain pending; no tracking identity or remote synchronization is implied. Deleting worktrees or branches remains gated by the user's current instruction and verified merge/preservation facts. A future proposal adding execution automation, remote storage, or a new trust boundary requires fresh analysis.

## Evidence Reviewed

- E01: `docs/internal/AGENTIC_CONTEXT_ENGINEERING.md`, sections 5 and Autonomous Implementation Execution Protocol; canonical ownership, topology, slicing, verification, and cleanup.
- E02: `.agents/skills/implementation-plan/SKILL.md`, `.agents/skills/implement-tasks/SKILL.md`, and planning templates; root planning, whole-triad movement, full-plan execution, and phase/PR mismatch.
- E03: `.gitignore:443`, `.agents/skills/conventional-commit/SKILL.md`; active plans are untracked local working memory.
- E04: `.agents/skills/plane-compose/resources/plan-tracking.md`, taxonomy section 4.1, and Senior CTO rewrite guidance; PR-granular tracking and retention of requested scope.
- E05: User's 2026-11-13 account of long-running PRs and requested copy, reconcile, merge, and cleanup cycle.
- E06: Completed triad revision `2026-11-13.r1`: `dev/active/multi-pr-agentic-workflow/multi-pr-agentic-workflow-plan.md`, `multi-pr-agentic-workflow-tasks.md`, and `multi-pr-agentic-workflow-context.md`. Section 9 maps IVSD-F001/M001 to ownership/preservation, F002/M002 to truthful reconciliation, and F003/M003 to bounded delivery and retained programme scope.
- E07: Authored instruction changes on 2026-11-14: [scope ownership](../../.agents/skills/implementation-plan/resources/pr-scope-contract.md), [execution lifecycle](../../.agents/skills/implement-tasks/resources/pr-execution-lifecycle.md), [tracking protocol](../../.agents/skills/plane-compose/resources/plan-tracking.md) and [architecture decision](../../docs/internal/adr/ADR-multi-pr-agentic-workflow.md). These preserve the same IVSD-M001-M003 constraints: canonical survival, two reconciliation checkpoints, independently accepted PRs and retained required scope. Acceptance here means the documented mitigation has an owner, not that a live cleanup pilot or external audit passed.

## Missing Evidence

No stakeholder study, measured branch-age baseline, historical reconciliation audit, disaster-recovery test, or operational pilot is claimed. Live Plane duplicate search is unavailable in this session; local tracking evidence cannot establish remote uniqueness.

## Context Inventory

One shared repository investigation supports this assessment and the implementation plan. The root checkout was clean on `develop`; upstream synchronization reported already up to date at `ec5c3fd5c336e17fd445ade74da60539c17298f2`. That object identifies reviewed repository evidence, not future implementation approval.

## Common Overlooked Failures And Outcomes

A PR closed without merge must not become delivered. A squash merge must not be rejected solely because original commit identities differ. A missing execution directory is an ownership/recovery problem, not permission to create a duplicate. Cleanup interrupted after reconciliation must resume cleanup without replaying implementation. A future scope edited during delegation must be preserved during section-level reconciliation.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
|---|---|---|---|---|
| 2026-11-13 | none | draft | Integrated planning intake | E01-E05 |
| 2026-11-13 | draft | current / plan-aligned | Revalidated completed triad revision `2026-11-13.r1` against all findings, mitigations, scenarios, and owning tasks | E06; plan section 9; task acceptance criteria |
| 2026-11-14 | current / plan-aligned | current / plan-aligned; findings accepted with ownership | Rechecked authored ownership, reconciliation, cleanup and tracking instructions against unchanged IVSD-M001-M003; no provider-authority or scope-removal change | E07; operational validation remains unperformed |

## Planning Handoff

- Workstream: multi-pr-agentic-workflow
- Status: current
- Reviewed input: completed programme revision `2026-11-13.r1`
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002; IVSD-F003 -> IVSD-M003
- Required plan mappings: Section 9 retains every finding and mitigation's scenario/task ownership; the authored documentation implements those safeguards without claiming operational enforcement.
- Escalations required before: any actual destructive cleanup; new authority outside the documented workflow. Tracking and linked publication remain pending under the explicit local-execution override.
- Refresh triggers: changes to canonical ownership, cleanup authorization, scope-removal policy, evidence requirements, external persistence, or mapped mitigations.
