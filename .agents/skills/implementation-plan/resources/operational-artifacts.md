<!-- ABOUTME: Templates for synchronized implementation context and task checklist artifacts. -->
<!-- ABOUTME: Keeps workstreams resumable, status-consistent, and maintainable during implementation. -->

# Operational Artifacts

## Dev-Doc Triad Responsibility Matrix

| Artifact | Single Source of Truth For | Forbidden Content (Belongs Elsewhere) |
|---|---|---|
| `dev/active/<task>/<task>-plan.md` (canonical, main checkout) | Original requested outcomes, current-state evidence, shared architecture and invariants, scenarios, PR scope boundaries and dependency contracts, phase-level exit criteria | Granular task checklists (`- [ ]`), registry or delegation state, dynamic execution statuses, session handoffs, dirty worktree logs |
| `dev/active/<task>/<task>-tasks.md` (canonical) | PR registry and delegation metadata, retained obligations, detailed Red/Green/Refactor tasks for undelegated scopes, delegated snapshots for delegated scopes, phase gates | Lengthy architectural narrative, design trade-off debates, deep security/I-VSD analysis |
| `dev/active/<task>/<task>-context.md` (canonical) | Programme resume pointer (active scope, execution location), material decisions, blockers, concise evidence, dated handoffs | Granular task execution checklists, full architectural specifications |
| Scoped execution copy (executor-created) | The one live detailed ledger and session memory for a delegated PR, with its canonical path, source revision, scope ID and owner | Future-scope task detail, a second editable copy of the whole triad |
| `event/work/active-<task>.yaml` | The Plane items this plan owns (the S item, or an M parent plus its Sub-items once execution creates them): canonical `EVENT-XXX` (minted by the first execution push), acceptance criteria, `parent:` Epic UUID, labels, priority, state, native `dependencies`/`relations`; folded into `work/workitems.yaml` only after final programme acceptance and reconciliation. | Granular inner-loop Red/Green/Refactor subtasks, commit logs, PR URLs (handled natively by Plane Git Development panel via `EVENT-XXX`), temporary test scratch. |

## The Dual-Plane Cognitive Model

The repository maintains an explicit operational boundary between **Macro Project Tracking** and **Micro Engineering Execution**:

- **Macro project plane** (`event/work/active-<task>.yaml` and the native Git Development panel): one item per separately merged PR scope (S programme = 1 Work Item; M programme = 1 parent + 2–5 Sub-items), Epic parent UUID, native dependencies/relations, acceptance criteria and human-visible progress. Branches (`feat/EVENT-XXX-<slug>`), commits (`Refs: EVENT-XXX`) and PRs (`[EVENT-XXX]`) link natively.
- **Micro execution plane** (canonical triad plus the selected scope's execution copy): plan holds architecture and RFC 2119 scenario contracts; tasks hold the PR registry and Red/Green/Refactor loops; context holds working memory; phase commits are declarative Conventional Commits.
- The canonical triad and the shared active Plane file survive intermediate merges and retire only under the [contract's completion rule](pr-scope-contract.md#completion-and-retention).

## Review State Contract

Plan, context, and tasks repeat only this compact shared state:

```text
- I-VSD report: <relative path>
- I-VSD status / disposition: <current + plan-aligned, or blocking state>
- CTO review: Not reviewed | Changes required | Approved | <linked review artifact>
- User approval: Awaiting approval | Approved
```

The I-VSD report owns moral analysis; the CTO review owns technical-readiness detail. The artifacts store links and current status, not duplicate narratives.

## Context File

Use this structure for `dev/active/<task-name>/<task-name>-context.md`:

```markdown
# <Human Title> — Context

Last Updated: YYYY-MM-DD Europe/Brussels

## Review State
<Use the shared Review State Contract above.>

## SESSION PROGRESS (YYYY-MM-DD Europe/Brussels)

### ✅ COMPLETED
- Planning created or re-baselined.
- Current-state report completed with evidence.

### 🟡 IN PROGRESS
- Awaiting user review of the implementation plan.

### ⏭️ NEXT
1. User reviews and corrects or approves the plan.
2. Implementation evaluates the earliest Planned PR scope <ID: outcome> against its readiness conditions and starts it only once Ready.
3. Refresh context after each PR reconciliation checkpoint.

## Programme Resume Pointer
- **Active scope:** <PR ID and outcome, or none>
- **Delivery / reconciliation / cleanup:** <registry values>
- **Execution location / owner / live ledger:** <path, branch, owner, or not delegated>
- **Canonical source revision delegated:** <revision or n/a>

### ⚠️ BLOCKERS
- None known, or the exact decision/evidence blocker.

## Quick Resume
1. Read this context, then the PR registry in `<task-name>-tasks.md`.
2. Resume an Implementing or In review scope or its pending finalization; otherwise evaluate the earliest Planned candidate whose required predecessors are merged and reconciled. A missing execution location is recovery, never automatic recreation.
3. Read only the selected scope's live ledger and the plan headings it names; do not reread the full unchanged plan on every resume.
4. Never pick work from the first phase or first unchecked historical task; old checked tasks prove neither topology nor delivery.
5. Keep the live ledger current during implementation and update context/plan only at their defined triggers.

## Key Files And Responsibilities
Table: Path, Existing/New, Layer, Purpose, Notes.

## Key Decisions
Concise decision log synchronized with the plan.

## Constraints And Rules To Remember
Matched intents, relevant rules/skills, and task-specific invariants.

## Validation Baseline
Record the actual results of each phase gate and PR acceptance gate defined in plan Section 7 (or the Tier 4 documentation checks), with exact commands. Record any proven unrelated shared-tree failure separately from the scope-owned verification result; never relabel it green. Record any explicit user verification override and the checks it leaves unrun.

## Current Known Risks / Unknowns
Short list with owning task ids.

## Handoff Notes

### Handoff — YYYY-MM-DD Europe/Brussels
- **Current state:**
- **Next action:**
- **Blockers:**
- **Modified files:**
- **Validation:**
- **Documentation impact:**
- **Risks:**
- **Notes for next contributor/agent:**
```

Put immediately actionable state at the top. Replace template status with investigated reality; never leave “None” when a blocker or failed baseline is known.

## Tasks File

Use this structure for `dev/active/<task-name>/<task-name>-tasks.md`:

```markdown
# <Human Title> — Task Checklist

Last Updated: YYYY-MM-DD Europe/Brussels

## Status Summary
- **Overall status:** Draft / Approved / In implementation / Complete
- **Active PR scope:** <ID: outcome, or none>; its current phase and next task live in its live ledger.
- **Review state:** I-VSD status, CTO review, and user approval from the shared contract.

## PR Registry

| Scope | Outcome | Prerequisites | Delivery | Reconciliation | Cleanup | Delegation (source revision, location, branch, owner, live ledger) | PR | Blockers / retained obligations |
|---|---|---|---|---|---|---|---|---|
| PR-<OUTCOME> | <usable result> | <scope IDs or none> | Planned | Pending | Not requested | Not delegated | — | <owners of unmet criteria> |

Delivery values: Planned, Ready, Implementing, In review, Merged, Closed unmerged. Readiness, merge, reconciliation and cleanup are separate facts ([PR scope contract](pr-scope-contract.md#required-scope-definition)).

## Implementation Maintenance Rules
- At implementation start or resume, read context, select the scope from the PR registry, then read only its live ledger and named plan sections.
- Do not reread unchanged artifacts after every task.
- Batch task checkbox updates at logical phase milestones or phase verification gates rather than churning status on every minor edit.
- Add discovered work where it belongs and keep phase task lists accurate.
- Close every verified phase immediately on the selected PR's task branch using the planned declarative Conventional Commit contract(s); for large phases (touching dozens or hundreds of files) or multi-concern scopes, sequence multiple atomic commits following `conventional-commit` rules 1 & 13.
- Use the planning-authored default title, description, changelog treatment, and trailers unchanged when they remain truthful; do not spend implementation context recomposing them.
- Do not load `conventional-commit` merely to reuse the approved contract; load it only when a permitted material divergence requires replacement contracts.
- Update `context.md` only when pausing, transferring across sessions, or handling unexpected blockers.
- Update the plan only when scope, architecture, sequencing, acceptance criteria, or risk strategy changes.
- Keep required Ring 1 slices and Red invariant checks; do not repeat full-project gates after individual tasks. Run the planned phase gate once at phase end and the PR acceptance gate before review handoff. Add no browser, container or live-service check that no rule or changed surface requires.
- Planning never creates branches or worktrees; the canonical triad stays in the root `dev/active/` for the whole programme.
- When a scope is delegated, mark its section here **delegated snapshot** with owner and location; the execution copy is the only live ledger for it.
- Stop at the selected PR's review handoff; the next scope starts only after this one is merged, reconciled, and the next candidate passes readiness.
- Plane state changes only at the moments listed under Plane Tracking; phases and tasks never change Plane state.

## Plane Tracking ([plan-tracking](../../plane-compose/resources/plan-tracking.md))
- **Items:** copy the plan §0 Plane Tracking Plan rows with their IDs once minted (S item, or parent + Sub-items).
- [ ] **Execution start:** validate, dry-run, and push the active file (mints the S item / parent); M plans: push Sub-items with `parent: <parent UUID>`, `state: Todo`.
- [ ] **Execution start:** S item / parent → `In Progress` with the driving developer as assignee; add every new item to its Version Module through Plane MCP and read it back.
- [ ] **Each PR start (M plans):** that Sub-item → `In Progress` with assignee.
- [ ] **Each PR opened:** its item → `Review Needed`; PR `## Issues` lists `- Plane: [EVENT-<item>]` (an M parent unbracketed).
- [ ] **Each PR merged with reconciled acceptance evidence:** its item → `Done`; the M parent → `Done` after every Sub-item and retained programme obligation.
- [ ] **Programme close (after the final reconciled PR only):** fold the active file into `event/work/workitems.yaml`, delete it, and confirm `plane push --work-only --dry-run` reports no changes.

## PR-<OUTCOME>: <Descriptive outcome>
**Readiness:** <conditions from the plan scope; mark Ready only when all hold>
**Snapshot state:** Live here | Delegated snapshot — owner <owner>, live ledger <location>

### Phase 1: <Name> ⏳ NOT STARTED
**Phase-owned paths:** exact files this phase may stage; update before verification when legitimate phase work discovers or generates another file.

- [ ] **1.1 <Task name>**
  - **Files:** exact paths marked existing/new
  - **Acceptance:** observable outcome
  - **Effort:** S/M/L/XL
  - **Dependencies:** task ids

#### Phase 1 Verification — RUN ONCE AFTER ALL PHASE TASKS
- [ ] Run the targeted phase gate from plan Section 7 once (for code: one Release build of the touched project and at most one relevant project test on one provider; for Tier 4 documentation: metadata, link and diff checks).
- [ ] Confirm the phase-owned verification lane is green.

#### Phase 1 Commit(s) — RUN IMMEDIATELY AFTER VERIFICATION
*(Note: If Phase 1 is large or touches dozens/hundreds of files across multiple concerns, sequence multiple atomic commit contracts below instead of one monolithic umbrella commit).*

##### Planned Commit Contract [Contract 1 of N for multi-commit phases]
- **Type & Scope:** `type(scope)`
- **Title:** `concrete changed behavior or engineering outcome`
- **Description:** Exact problem and reader-visible consequence, followed by the relevant technical mechanism, data/control flow, and constraints.
- **Changelog treatment:** Public feature/fix | Change fragment `CHG-YYYY-NNNN` | `Changelog: skip`
- **Required trailers:** Exact terminal trailer lines, or `None`
- **Commit paths:** Exact ordered list of wholly phase-owned files for this commit.
- **Message override:** Not overridden

<!-- Repeat Planned Commit Contract block for Contract 2, 3, etc. if phase is large -->

- [ ] Stage exact phase-owned paths using `git add -- <paths>` and execute commit using the declarative contract on the selected PR's branch.
- [ ] Only when the default will not be used, load `conventional-commit` and record `Message override: Yes`, `Reason`, and complete replacement contract.
- [ ] Account for remaining working-tree changes before completing Phase 1.

### PR-<OUTCOME> Acceptance Gate — BEFORE REVIEW HANDOFF
- [ ] Run the checks plan Section 7 assigns to this PR (or record the explicit user override and the checks left unrun).
- [ ] Reconcile delivery against original intent and acceptance; record discovered work, changed contracts and unmet criteria with owners; update affected later scopes' dependencies.

## Retained Obligations, Deferred Work & Knowledge Graduation
- **Retained obligations:** unmet required acceptance stays assigned to its scope or an explicitly approved successor scope, never to backlog.
- **Deferred Work:** only optional follow-ups the user explicitly deferred, with reason, trigger, and target backlog item (`dev/backlog/<slug>.md`).
- **Durable Findings:** Non-obvious quirks or bug root causes promoted to `dev/_journal/domains/<domain>.md`.
- **Architectural Decisions:** Permanent system invariants promoted to `docs/internal/adr/ADR-XXX-<name>.md`.
*(Note: Commit these persistent files in the PR that owns them so they merge before its execution copy is cleaned up).*
```

Repeat PR scope and phase sections for every scope in the plan. Later scopes may carry rolling detail, but every required outcome, dependency and acceptance criterion appears. Scope IDs, task ids, names, status, dependencies, acceptance criteria, and assigned gates must match the plan exactly.

## Synchronization Rules

After writing all three artifacts, compare:

- planning and overall status;
- PR scope IDs, registry state, delegation metadata and the programme resume pointer;
- phase/task ids and names;
- blockers and unknowns;
- decisions and constraints;
- validation baseline;
- phase-owned paths, verification disposition, planned declarative commit contract, and override reason;
- deferred work and risks;
- I-VSD path/status, CTO review, and user approval.

Any disagreement is a planning defect; fix it before handoff.

## Progressive Maintenance Cadence

Use the lightest update that keeps repository state truthful:

| Trigger | Required update | Do not do |
|---|---|---|
| Task(s) completed | Batch checkbox updates at logical phase milestones or phase verification | Churn status on every minor edit |
| Phase implementation completed | Reconcile phase tasks and run the applicable phase gate once | Repeat full-project checks after individual tasks |
| Phase verification resolved | Use planned declarative contract without loading `conventional-commit`, commit exact phase-owned paths on the PR branch | Reload/recompose truthful contract, defer to new session, or record commit hashes |
| PR review handoff or merge | Reconcile delivery into the canonical scope section and registry; patch only delegated sections and justified shared decisions | Replace the canonical triad with the execution copy or mark delivery complete from checkboxes |
| Planned commit message became false | Load `conventional-commit`, record override reason and updated contract | Silently override for style or preference |
| New task discovered | Add it under the owning phase with acceptance criteria and dependencies | Hide it in chat or context only |
| Blocker or validation failure | Mark affected work blocked and record cause plus recovery action in tasks; investigate root cause | Claim the repository is green without resolving failure |
| Scope, architecture, sequence, risk, or acceptance changed | Update plan, then mirror task impact | Re-baseline unchanged sections |
| Pause, compaction, or session transfer | Reconcile affected tasks and add a concise context handoff | Leave incomplete work undocumented |

Implementation completion, verification disposition, phase completion and PR completion are distinct. Check an implementation task when its acceptance criteria are satisfied. The phase completes only after its own verification is green and all phase-owned Conventional Commits succeed. The PR completes only after its acceptance gate, verified merge and acceptance reconciliation; cleanup is a separate authorized step.

## Read Cadence

- Initial implementation start: read context, the PR registry, and the selected scope with its governing shared plan sections once.
- Same uninterrupted session: do not reread unchanged artifacts after each task.
- Cold resume or new agent: read context, select the scope from the registry, then its live ledger plus referenced decisions/constraints from the plan.
- Scope conflict or stale evidence: reread only the affected plan/current-state sections before changing direction.
- After writing a checkbox or status update: do not reopen the file merely to restate what was just written; continue implementation.

## Stale-State Recovery

If an agent inherits stale docs, perform one bounded reconciliation for the current scope using the conversation, changed files, validation, phase commits and native PR/merge evidence already available; never infer delivery or topology from checkbox state alone. Update the plan first only when strategy drifted, then reconcile context and task checkboxes. Do not sweep unrelated `dev/active/` workstreams, absorb unrelated dirty files into a recovery commit, rerun already-green commands, or turn the journal into a session log; record a journal entry only for a non-obvious durable finding likely to recur.
