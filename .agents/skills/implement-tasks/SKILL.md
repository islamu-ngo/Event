---
name: implement-tasks
description: "Load when executing, running, or resuming an approved task plan from `dev/active/<task>/` or `.worktrees/<task>`; orchestrates fresh worktree setup, in-flight worktree/develop resume, Red/Green/Refactor task loops, semantic phase commits, pre-PR rebase conflict protection, PR creation with pre-flight Release Impact, and parked worktree lifecycle."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Workflow skill for executing or resuming approved implementation tasks from dev/active/<task>/ or .worktrees/<task>. -->
<!-- ABOUTME: Guides isolated worktree execution, develop in-tree resume, plan mv, semantic phase commits, pre-PR rebase, and parked worktree lifecycle. -->

## Must-Read Docs
- [../../../AGENTS.md](../../../AGENTS.md)
- [../../../.agents/CONTEXT_ENGINEERING.md](../../../.agents/CONTEXT_ENGINEERING.md)
- [../conventional-commit/SKILL.md](../conventional-commit/SKILL.md)

## Top Invariants

1. **Execute Only Approved Plans**: Never implement without an approved task plan (`tasks.md`, `context.md`, `plan.md`). Verify approval state before modifying runtime code.
2. **Topology Discovery & Execution Context (Worktree vs In-Tree)**:
   Before executing or resuming, discover the active execution topology for `<task>`:
   - **Case A: Isolated Worktree In-Flight (`.worktrees/<task-name>`)**:
     If `.worktrees/<task-name>` already exists (check `git worktree list` or `.worktrees/<task-name>` path):
     - Active branch: `feat/<task-name>`
     - Task folder: `.worktrees/<task-name>/dev/active/<task-name>/`
     - Execution context: all shell commands set `Cwd: .worktrees/<task-name>`; all file edits target `.worktrees/<task-name>/...`
     - **Action**: Do NOT recreate worktree or run `mv`. Skip setup steps and resume directly inside the existing worktree. If `AGENTS.local.md` exists in the repository root and is missing in `.worktrees/<task-name>`, copy it: `[ -f AGENTS.local.md ] && [ ! -f .worktrees/<task-name>/AGENTS.local.md ] && cp AGENTS.local.md .worktrees/<task-name>/`.
   - **Case B: In-Tree / Develop In-Flight (`dev/active/<task-name>`)**:
     If `dev/active/<task-name>` exists in the repository root and work is already in-progress (e.g. checked items `[x]` in `tasks.md`, existing commits, or user explicitly requested running directly on `develop` or current branch):
     - Active branch: current branch (e.g. `develop` or current feature branch)
     - Task folder: `dev/active/<task-name>/`
     - Execution context: repository root (`Cwd: .`)
     - **Action**: Do NOT create a worktree or move files. Respect in-tree execution and resume directly in the root workspace.
   - **Case C: Fresh Worktree Setup (Default New Implementation)**:
     If starting a brand-new plan where `dev/active/<task-name>` exists at the root, `.worktrees/<task-name>` does not exist, and user did not mandate in-tree execution:
     - Canonical root-scoped worktree isolation applies:
       ```bash
       git fetch origin develop
       git worktree add -b feat/<task-name> .worktrees/<task-name> origin/develop
       mkdir -p .worktrees/<task-name>/dev/active && mv dev/active/<task-name> .worktrees/<task-name>/dev/active/
       [ -f AGENTS.local.md ] && cp AGENTS.local.md .worktrees/<task-name>/
       ```
     - Moving preserves strict single-source-of-truth, eliminates split-brain checklists, and ensures clean garbage collection on worktree removal.
     - **Copy `AGENTS.local.md` (Never Move)**: If `AGENTS.local.md` exists in the repository root, copy it into `.worktrees/<task-name>/AGENTS.local.md`. It must be **copied (never moved)** so that local developer overrides and environment constraints remain in effect inside the isolated worktree while preserving the root configuration for subsequent sessions or tasks. (Because `AGENTS.local.md` is gitignored, it will not be staged or committed).
3. **Resume Protocol & Working Memory Discipline**:
   When instructed to `resume <task>`, or when auto-detecting an in-flight plan:
   - **Holistic Orientation (Read Once per Session)**: On session start or cold resume, read `*-context.md` (`## Quick Resume`, current milestone, blockers), `*-tasks.md` (identify active phase and unchecked `[ ]` tasks), and read `*-plan.md` to establish the holistic mental model (system architecture, cross-cutting invariants, and downstream phase contracts). Never implement blind to future phase dependencies.
   - **Execution Economy (Inner Loop Zooming)**: Once oriented within the session, do NOT re-read the entire plan on every task turn. Zoom into the active phase heading in `*-plan.md` and manage granular state via `*-tasks.md`.
   - **Re-Orientation Triggers**: Re-read the full plan (or downstream phases) immediately if an unexpected blocker arises, domain model friction occurs, cross-phase contracts conflict, or the user redirects requirements.
   - **Inner Loop Baseline Sanity**: Run a fast Ring 1 sliced test (`--treenode-filter`) in the target execution context (`Cwd`) to verify the previous session's green baseline before modifying code.
   - **Quarantine Rot**: If an unrelated pre-existing test failure is encountered, follow the Yak-Shaving Quarantine Rule (log in `context.md`, do not derail the task to fix unrelated issues).
   - **Continue the Phased Loop**: Pick up execution directly at the first unchecked task `[ ]` in the active phase.
4. **Dev-Doc Working Memory & Native Tools**: Active plan files (`tasks.md`, `context.md`) live inside the resolved task folder (`.worktrees/<task>/dev/active/<task>/` or `dev/active/<task>/`). Read and edit them using native harness file tools by deterministic path. Do not use ad-hoc shell scripts (`cat`, `sed`, `awk`) for file manipulation (Critical Rule #9).
5. **Phase-by-Phase Execution Cadence & Progressive Verification**:
   - **Red**: Author failing invariant/specification tests first for core domain, concurrency, state machines, and security boundaries. Shift pure domain invariants to `Event.Domain.UnitTests`. Scaffold compilable stub types/interfaces so the project builds cleanly while the test fails at runtime.
   - **Green**: Implement production code to satisfy invariants.
   - **Ring 1 Sliced Verification (Inner Loop, < 2s)**: Run targeted test class via `--treenode-filter "/*/*/*<TestClass>/*"` in-memory (`Event.Domain.UnitTests` or `Event.Application.UnitTests`). Zero Docker containers, zero network I/O, zero database setup lag.
   - **Ring 2 Phase Verification (Phase Exit Gate, < 15s)**: Run Release build (`dotnet build -c Release -v q`) and at most ONE selected project test against ONE canonical provider within the execution context. Forbid multi-database provider matrices during intermediate phases.
   - **Semantic Phase Commit**: In the execution context, stage changes and commit using the planned semantic Conventional Commit contract (type, scope, title, description, trailers) from `tasks.md`. Planning defines semantic meaning; execution handles file discovery.
   - **Reconcile Ledger**: Batch task checkbox updates at phase gates in `tasks.md`.
6. **Self-Contained Phase Reporting & Zero Plan-Opening Prompts**:
   When pausing for user feedback, milestone approvals, or architectural decisions between phases, executing agents must **never** send cryptic prompts referencing bare IDs. Always provide an inline **Decision Brief**:
   - Current progress milestone in plain English.
   - Descriptive names of components/services involved.
   - The concrete decision required, why it matters, and trade-offs.
   - Explicit numbered options with a recommended default.
   - Immediate next action upon reply.
7. **Knowledge Graduation Gate (Mandatory Before PR)**:
   Before declaring work complete or pushing, promote durable knowledge within the execution context:
   - **Deferred Work**: Create `dev/backlog/<topic-slug>.md` with problem statement and acceptance criteria.
   - **Architectural Decisions**: Create an ADR in `docs/internal/adr/ADR-XXX-<name>.md`.
   - **Lessons & Quirks**: Append to `dev/_journal/domains/<domain>.md` or `dev/_journal/journal.md`.
   - Stage and commit these persistent files on the task branch so they merge into `develop`.
8. **Ring 3 Plan Exit Gate & Pre-PR Rebase Gate**:
   - **Ring 3 Plan Exit Gate**: Run full 5-database matrix, EF Core migrations, and `Event.Architecture.Tests` once at workstream completion before PR creation.
   - **Pre-PR Rebase (Concurrency Conflict Protection)**:
     ```bash
     git fetch origin develop && git rebase origin/develop
     ```
     Resolve any conflicts inside the execution context, verify tests, and complete rebase (`git rebase --continue`).
9. **Pull Request Creation & Lifecycle Protocol**:
   - **Push Branch**: `git push -u origin <branch> --force-with-lease`
   - **Pre-Flight PR Release Impact Generation (Zero CI Failures)**:
     Never use a bare `gh pr create --fill` that omits metadata. PR descriptions MUST contain the `## Release Impact` checklist mandated by `.ci/scripts/validate-release-impact-pr.cs`. Inspect changed files against category rules:
     - `security` (auth, cerbos, keycloak, cla, secrets): `- [x] Security/auth impact documented`
     - `migration` (migrations, seed data): `- [x] Migration/data/rollback impact documented`
     - `configuration` (config, secrets, appsettings, compose, Dockerfile): `- [x] Configuration/secrets/deployment impact documented`
     - `openapi` (openapi schemas, api changelog, api controllers): `- [x] OpenAPI/client contract impact documented`
     - `operator` (self-hosting, operations, deployment, release checklist): `- [x] Operator/self-hosting/release-note impact documented`
     - If none apply: `- [x] Not applicable`
     Always provide a non-empty `Details:` section explaining the impact, release-note location, or why no release note is needed.
     Submit the PR using:
     ```bash
     gh pr create --base develop --title "<type>(<scope>): <title>" --body "<body-with-release-impact>"
     ```
   - **Park the Worktree (When using Worktree isolation)**:
     Never delete `.worktrees/<task-name>` upon PR creation. The worktree must remain parked and intact so that any subsequent bot reviews (Copilot, CodeQL) or CI check failures can be resolved immediately in-place with zero setup overhead.
   - **Halt and Await User Direction**:
     Immediately after PR creation, the agent must halt its execution and deliver a self-contained status brief:
     1. PR URL and branch name.
     2. Confirmation of worktree status (e.g. parked at `.worktrees/<task-name>`).
     3. Notification that CI checks and automated bot reviewers are running.
     4. Clear instruction to user: notify agent of any review comments or CI failures; OR confirm PR approval/merge to trigger teardown.
   - **Worktree Teardown (Only Upon Explicit User Confirmation)**:
     Only when the user confirms that the PR is approved/merged or explicitly instructs to clean up:
     - If Worktree topology: `git worktree remove .worktrees/<task-name>` (from root workspace).
     - Ephemeral plan files in `dev/active/<task-name>` vanish cleanly with the worktree.

## Workflow

```text
1. Topology & Execution Context Discovery:
   Resolve target task directory and execution context (Worktree vs In-Tree):
   - Check if .worktrees/<task> exists (or git worktree list):
     -> FOUND: Topology = Worktree. Set Cwd = .worktrees/<task>, PlanPath = .worktrees/<task>/dev/active/<task>/.
        Skip worktree creation and plan mv. If AGENTS.local.md exists in root and is missing in worktree, copy it:
        [ -f AGENTS.local.md ] && [ ! -f .worktrees/<task>/AGENTS.local.md ] && cp AGENTS.local.md .worktrees/<task>/
     -> NOT FOUND:
        - If dev/active/<task> exists and (resuming OR user mandated in-tree):
          Topology = In-Tree. Set Cwd = repo root, PlanPath = dev/active/<task>/.
        - If fresh execution on default topology:
          Topology = New Worktree.
          git fetch origin develop
          git worktree add -b feat/<task> .worktrees/<task> origin/develop
          mkdir -p .worktrees/<task>/dev/active && mv dev/active/<task> .worktrees/<task>/dev/active/
          [ -f AGENTS.local.md ] && cp AGENTS.local.md .worktrees/<task>/
          Set Cwd = .worktrees/<task>, PlanPath = .worktrees/<task>/dev/active/<task>/.

2. Context Load & Holistic Orientation:
   - Read <PlanPath>/<task>-context.md (Quick Resume, blockers, baseline).
   - Read <PlanPath>/<task>-tasks.md (find first unchecked [ ] task and active Phase).
   - Read <PlanPath>/<task>-plan.md once per session to establish holistic context (architecture, cross-phase contracts); zoom into the active phase heading for execution.
   - (If Resuming): Run quick Ring 1 test in Cwd to verify baseline health before editing.

3. Loop through Remaining Phases (in resolved Cwd):
   a. Red: compilable stubs + failing invariant test (in-memory domain first)
   b. Green: minimal implementation code
   c. Verify: Ring 1 sliced test (< 2s) -> Ring 2 phase build & single-provider test (< 15s)
      (Quarantine any unrelated pre-existing test rot into context.md)
   d. Commit: git add -A && git commit using semantic phase contract from tasks.md
   e. Update: batch checkbox updates in tasks.md
   f. Pause: If phase boundary requires user decision, output Decision Brief.

4. Knowledge Graduation (in resolved Cwd):
   a. Any deferred items? -> write dev/backlog/<slug>.md
   b. Any non-obvious lessons? -> append to dev/_journal/
   c. Any new architectural invariants? -> write ADR in docs/internal/adr/
   d. Stage and commit graduation files on task branch

5. Ring 3 Plan Exit Gate & Pre-PR Rebase:
   a. Ring 3: Run full multi-provider matrix & architecture tests in Cwd
   b. git fetch origin develop && git rebase origin/develop (in Cwd)
   c. dotnet test (verify regression-free rebase)

6. PR Creation & Handoff:
   a. git push -u origin <branch> --force-with-lease
   b. Inspect changed files and construct PR body with mandatory `## Release Impact` checklist
   c. gh pr create --base develop --title "..." --body "..."
   d. If Worktree topology: PARK .worktrees/<task> — DO NOT remove it!
   e. Stop and deliver self-contained status brief to user (PR link, checks running, next steps).

7. Teardown (Deferred — Only Upon User Confirmation):
   If Worktree: (from root workspace) git worktree remove .worktrees/<task>
```

## Verification Hooks
- `git diff --check -- .agents/skills/implement-tasks`
- Manually validate changed frontmatter against `.agents/skills/_SKILL_SCHEMA.md`

## Related Skills
- [../implementation-plan/SKILL.md](../implementation-plan/SKILL.md)
- [../senior-cto-feedback/SKILL.md](../senior-cto-feedback/SKILL.md)
- [../conventional-commit/SKILL.md](../conventional-commit/SKILL.md)
- [../finding/SKILL.md](../finding/SKILL.md)
