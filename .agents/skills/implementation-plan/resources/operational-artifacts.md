<!-- ABOUTME: Templates for synchronized implementation context and task checklist artifacts. -->
<!-- ABOUTME: Keeps workstreams resumable, status-consistent, and maintainable during implementation. -->

# Operational Artifacts

## Dev-Doc Triad Responsibility Matrix

| Artifact | Single Source of Truth For | Forbidden Content (Belongs Elsewhere) |
|---|---|---|
| `dev/active/<task>/<task>-plan.md` | Strategic architectural design, current-state evidence, design decisions (ADRs), phase-level boundaries and exit criteria | Granular task checklists (`- [ ]`), dynamic execution statuses (`IN PROGRESS`), session handoffs, dirty worktree logs |
| `dev/active/<task>/<task>-tasks.md` | Hot execution ledger, granular Red/Green/Refactor task breakdown (`- [ ] **N.M**`), task checkboxes, phase verification gates | Lengthy architectural narrative, design trade-off debates, deep security/I-VSD analysis |
| `dev/active/<task>/<task>-context.md` | Active session working memory (`COMPLETED`, `IN PROGRESS`, `NEXT`, `BLOCKERS`), quick resume, validation baseline, dated session handoffs | Granular task execution checklists, full architectural specifications |

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
2. First implementation agent starts with task <id/name>.
3. Refresh context after the first implementation slice.

### ⚠️ BLOCKERS
- None known, or the exact decision/evidence blocker.

## Quick Resume
1. Read this context and `<task-name>-tasks.md`.
2. Read only the current phase, constraints, or changed decisions from `<task-name>-plan.md`; do not reread the full unchanged plan on every resume.
3. Start from the first unchecked high-priority task unless the user overrides it.
4. Keep `tasks.md` current during implementation and update context/plan only at their defined triggers.

## Key Files And Responsibilities
Table: Path, Existing/New, Layer, Purpose, Notes.

## Key Decisions
Concise decision log synchronized with the plan.

## Constraints And Rules To Remember
Matched intents, relevant rules/skills, and task-specific invariants.

## Validation Baseline
For every implementation phase: one Release build and at most one fastest relevant non-browser project test, both run once after the phase tasks. Record any proven unrelated shared-tree failure separately from the phase-owned verification result; never relabel it green.

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
- **Current Phase:** Phase 1
- **Review state:** I-VSD status, CTO review, and user approval from the shared contract.

## Implementation Maintenance Rules
- Read the active plan and tasks once at implementation start; on resume, read tasks first and only relevant plan sections.
- Do not reread unchanged artifacts after every task.
- Batch task checkbox updates at logical phase milestones or phase verification gates rather than churning status on every minor edit.
- Add discovered work where it belongs and keep phase task lists accurate.
- Close every verified phase immediately on the task branch (`feat/<task-name>`) using the planned declarative Conventional Commit contract(s); for large phases (touching dozens or hundreds of files) or multi-concern scopes, sequence multiple atomic commits following `conventional-commit` rules 1 & 13.
- Use the planning-authored default title, description, changelog treatment, and trailers unchanged when they remain truthful; do not spend implementation context recomposing them.
- Do not load `conventional-commit` merely to reuse the approved contract; load it only when a permitted material divergence requires replacement contracts.
- Update `context.md` only when pausing, transferring across sessions, or handling unexpected blockers.
- Update the plan only when scope, architecture, sequencing, acceptance criteria, or risk strategy changes.
- Do not run build/tests after individual tasks; verify once at phase end.
- planning itself never creates branches or worktrees; plan artifacts live in the root dev/active/ directory.
- Do not start the app, browser, Docker, Aspire, Playwright, Chrome DevTools MCP, or live services for verification.

## Phase 1: <Name> ⏳ NOT STARTED
**Phase-owned paths:** exact files this phase may stage; update before verification when legitimate phase work discovers or generates another file.

- [ ] **1.1 <Task name>**
  - **Files:** exact paths marked existing/new
  - **Acceptance:** observable outcome
  - **Effort:** S/M/L/XL
  - **Dependencies:** task ids

### Phase 1 Verification — RUN ONCE AFTER ALL PHASE TASKS
- [ ] Run `dotnet build --configuration Release --verbosity quiet` once.
- [ ] Run `dotnet test --project <one-relevant-project>.csproj --configuration Release --verbosity quiet` once.
- [ ] Confirm the phase-owned verification lane is green.

### Phase 1 Commit(s) — RUN IMMEDIATELY AFTER VERIFICATION
*(Note: If Phase 1 is large or touches dozens/hundreds of files across multiple concerns, sequence multiple atomic commit contracts below instead of one monolithic umbrella commit).*

#### Planned Commit Contract [Contract 1 of N for multi-commit phases]
- **Type & Scope:** `type(scope)`
- **Title:** `benefit-led phase outcome`
- **Description:** Exact motivation and data/control-flow description for this phase.
- **Changelog treatment:** Public feature/fix | Change fragment `CHG-YYYY-NNNN` | `Changelog: skip`
- **Required trailers:** Exact terminal trailer lines, or `None`
- **Commit paths:** Exact ordered list of wholly phase-owned files for this commit.
- **Message override:** Not overridden

<!-- Repeat Planned Commit Contract block for Contract 2, 3, etc. if phase is large -->

- [ ] Stage exact phase-owned paths using `git add -- <paths>` and execute commit using the declarative contract on `feat/<task-name>`.
- [ ] Only when the default will not be used, load `conventional-commit` and record `Message override: Yes`, `Reason`, and complete replacement contract.
- [ ] Confirm clean git status on feature branch before completing Phase 1.

## Remaining / Deferred Work & Knowledge Graduation
- **Deferred Work:** Explicit deferral, reason, trigger, and target backlog item (`dev/backlog/<slug>.md`).
- **Durable Findings:** Non-obvious quirks or bug root causes promoted to `dev/_journal/domains/<domain>.md`.
- **Architectural Decisions:** Permanent system invariants promoted to `docs/internal/adr/ADR-XXX-<name>.md`.
*(Note: These persistent files must be committed on the feature branch before workstream close so they merge into develop).*
```

Repeat phase sections for every phase in the plan. Task ids, names, status, dependencies, acceptance criteria, and the single selected test project must match the plan exactly.

## Synchronization Rules

After writing all three artifacts, compare:

- planning and overall status;
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
| Phase implementation completed | Reconcile phase tasks and run the two phase-end checks once | Run tests after individual tasks |
| Phase verification resolved | Use planned declarative contract without loading `conventional-commit`, commit exact phase-owned paths on task branch | Reload/recompose truthful contract, defer to new session, or record commit hashes |
| Planned commit message became false | Load `conventional-commit`, record override reason and updated contract | Silently override for style or preference |
| New task discovered | Add it under the owning phase with acceptance criteria and dependencies | Hide it in chat or context only |
| Blocker or validation failure | Mark affected work blocked and record cause plus recovery action in tasks; investigate root cause | Claim the repository is green without resolving failure |
| Scope, architecture, sequence, risk, or acceptance changed | Update plan, then mirror task impact | Re-baseline unchanged sections |
| Pause, compaction, or session transfer | Reconcile affected tasks and add a concise context handoff | Leave incomplete work undocumented |

Implementation completion, verification disposition, and phase completion are distinct. Check an implementation task when its acceptance criteria are satisfied. The phase completes only after its own verification is green and all phase-owned Conventional Commits succeed.

## Read Cadence

- Initial implementation start: read all three artifacts once.
- Same uninterrupted session: do not reread unchanged artifacts after each task.
- Cold resume or new agent: read context and tasks, then the current phase plus referenced decisions/constraints from the plan.
- Scope conflict or stale evidence: reread only the affected plan/current-state sections before changing direction.
- After writing a checkbox or status update: do not reopen the file merely to restate what was just written; continue implementation.

## Stale-State Recovery

If an agent inherits stale docs, perform one bounded reconciliation for the current workstream using the conversation, changed files, validation, and phase commits already produced in the session. Update the plan first only when strategy drifted, then reconcile context and task checkboxes. Do not sweep unrelated `dev/active/` workstreams, absorb unrelated dirty files into a recovery commit, rerun already-green commands, or turn the journal into a session log; record a journal entry only for a non-obvious durable finding likely to recur.
