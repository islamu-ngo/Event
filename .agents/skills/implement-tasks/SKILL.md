---
name: implement-tasks
description: "Load when executing, running, or implementing an approved task plan from `dev/active/<task>/`; orchestrates phase execution, canonical worktree isolation (`git worktree` in `.worktrees/` + plan `mv`), Red/Green/Refactor task loops, semantic phase commits, pre-PR rebase conflict protection, and PR lifecycle teardown."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Workflow skill for executing approved implementation tasks from dev/active/<task>/. -->
<!-- ABOUTME: Guides isolated worktree execution (.worktrees/<task>), plan mv, semantic phase commits, pre-PR rebase, and PR teardown. -->

## Must-Read Docs
- [../../../AGENTS.md](../../../AGENTS.md)
- [../../../.agents/CONTEXT_ENGINEERING.md](../../../.agents/CONTEXT_ENGINEERING.md)
- [../conventional-commit/SKILL.md](../conventional-commit/SKILL.md)

## Top Invariants

1. **Execute Only Approved Plans**: Never implement without an approved task plan (`tasks.md`, `context.md`, `plan.md`). Verify approval state before modifying runtime code.
2. **Canonical Execution Topology (Root-Scoped Worktree Isolation)**: Never switch or hijack the main repository workspace branch (which remains cleanly parked on `develop` for developer use and concurrent sessions). The standard and default execution mode is **Worktree Isolation in `.worktrees/<task-name>`**:
   - Keep worktrees within the repository workspace root under `.worktrees/<task-name>` (which is gitignored). This guarantees that all AI agent tools (file viewing, editing, bash execution with `Cwd`) operate strictly within the workspace sandbox without crossing harness boundaries.
   - Upstream base freshness: `git fetch origin develop`
   - Create worktree: `git worktree add -b feat/<task-name> .worktrees/<task-name> origin/develop`
3. **Plan Transfer Protocol (`plan mv`)**:
   - Move (do not copy) the active task folder into the worktree:
     ```bash
     mkdir -p .worktrees/<task-name>/dev/active && mv dev/active/<task-name> .worktrees/<task-name>/dev/active/
     ```
   - Moving preserves strict single-source-of-truth for `tasks.md` and `context.md`, eliminates split-brain checklists, and ensures that upon worktree removal, ephemeral planning debris is automatically garbage-collected without polluting the parent workspace.
4. **Dev-Doc Working Memory & Native Tools**: Active plan files (`tasks.md`, `context.md`) are gitignored local working memory living in `.worktrees/<task-name>/dev/active/<task-name>/`. Read and edit them using native harness file tools by deterministic path. Do not use ad-hoc shell scripts (`cat`, `sed`, `awk`) for file manipulation (Critical Rule #9).
5. **Phase-by-Phase Execution Cadence & Semantic Commits**:
   - **Red**: Author failing invariant/specification tests first for core domain, concurrency, state machines, and security boundaries. Scaffold compilable stub types/interfaces so the project builds cleanly while the test fails at runtime.
   - **Green**: Implement production code to satisfy invariants.
   - **Sliced Verification**: Run targeted test class via `--treenode-filter` (~1.5s) inside the worktree directory.
   - **Phase Verification**: Run Release build and single selected project test within the worktree.
   - **Immediate Phase Commit**: Inspect ownership, stage exact phase-owned paths (`git add -- <paths>`), and execute each planned atomic contract from `tasks.md`. Isolation does not imply ownership of every dirty file. Preserve unrelated edits; verify the committed inventory and index. No post-commit hash recording.
   - **Reconcile Ledger**: Batch task checkbox updates at phase gates in `tasks.md`.
6. **Self-Contained Phase Reporting & Zero Plan-Opening Prompts**:
   When pausing for user feedback, milestone approvals, or architectural decisions between phases, executing agents must **never** send cryptic prompts referencing bare IDs (e.g. *"Do you approve proceeding with P04/P06 while keeping both P03 gates open?"*). The developer should **never** have to open `tasks.md` or `plan.md` to understand an agent's prompt. Always provide an inline **Decision Brief**:
   - Current progress milestone in plain English.
   - Descriptive names of components/services involved (e.g., *"Phase 4: Outbox Worker & Retries"* instead of *"P04"*).
   - The concrete decision required, why it matters, and trade-offs of deferrals.
   - Explicit numbered options with a recommended default.
   - Immediate next action upon reply.
7. **Knowledge Graduation Gate (Mandatory Before PR)**:
   Before declaring work complete or pushing, promote durable knowledge within the worktree:
   - **Deferred Work**: Create `dev/backlog/<topic-slug>.md` with problem statement and acceptance criteria.
   - **Architectural Decisions**: Create an ADR in `docs/internal/adr/ADR-XXX-<name>.md`.
   - **Lessons & Quirks**: Append to `dev/_journal/domains/<domain>.md` or `dev/_journal/journal.md`.
   - Stage and commit these persistent files on `feat/<task-name>` so they merge into `develop`!
8. **Pre-PR Rebase Gate (Concurrency Conflict Protection)**:
   Before pushing, absorb any concurrent merges from other tasks/agents:
   ```bash
   git fetch origin develop && git rebase origin/develop
   ```
   - If clean: proceed to push.
   - If merge conflicts occur: resolve conflicts inside `.worktrees/<task-name>`, run project verification tests, and complete the rebase (`git rebase --continue`).
9. **Pull Request Lifecycle & Worktree Disposal**:
   - Push branch to origin: `git push -u origin feat/<task-name> --force-with-lease`
   - Open Pull Request for CI/CD and review: `gh pr create --base develop --fill`
   - Teardown: From the root workspace (or once PR is submitted):
     ```bash
     git worktree remove .worktrees/<task-name>
     ```
   - Ephemeral plan files in `dev/active/<task-name>` vanish cleanly with the worktree.

## Workflow

```text
1. Inspect approved dev/active/<task>/ (tasks.md, context.md, plan.md)
2. Setup isolated worktree under workspace root:
   git fetch origin develop
   git worktree add -b feat/<task> .worktrees/<task> origin/develop
   mkdir -p .worktrees/<task>/dev/active && mv dev/active/<task> .worktrees/<task>/dev/active/
3. Loop through Phases inside .worktrees/<task>:
   a. Red: compilable stubs + failing invariant test
   b. Green: implementation code
   c. Verify: sliced test -> phase build & test (Cwd: .worktrees/<task>)
   d. Commit: git add -- <phase-owned paths> && git commit using each atomic contract from tasks.md
   e. Update: batch checkbox updates in tasks.md
4. Knowledge Graduation (in worktree):
   a. Any deferred items? -> write dev/backlog/<slug>.md
   b. Any non-obvious lessons? -> append to dev/_journal/
   c. Any new architectural invariants? -> write ADR in docs/internal/adr/
   d. Stage and commit graduation files on feat/<task>
5. Pre-PR Rebase Gate:
   git fetch origin develop && git rebase origin/develop
   dotnet test (verify regression-free rebase)
6. PR Creation & Teardown:
   git push -u origin feat/<task> --force-with-lease
   gh pr create --base develop --fill
   (from root) git worktree remove .worktrees/<task>
```

## Verification Hooks
- `git diff --check -- .agents/skills/implement-tasks`
- Manually validate changed frontmatter against `.agents/skills/_SKILL_SCHEMA.md`

## Related Skills
- [../implementation-plan/SKILL.md](../implementation-plan/SKILL.md)
- [../senior-cto-feedback/SKILL.md](../senior-cto-feedback/SKILL.md)
- [../conventional-commit/SKILL.md](../conventional-commit/SKILL.md)
- [../finding/SKILL.md](../finding/SKILL.md)
