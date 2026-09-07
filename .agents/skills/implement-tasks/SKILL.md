---
name: implement-tasks
description: "Load when executing, running, or implementing an approved task plan from `dev/active/<task>/`; orchestrates phase execution, canonical worktree isolation (`git worktree` in `.worktrees/` + plan `mv`), Red/Green/Refactor task loops, semantic phase commits, pre-PR rebase conflict protection, PR creation with pre-flight Release Impact, and parked worktree lifecycle."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Workflow skill for executing approved implementation tasks from dev/active/<task>/. -->
<!-- ABOUTME: Guides isolated worktree execution (.worktrees/<task>), plan mv, semantic phase commits, pre-PR rebase, and parked worktree lifecycle. -->

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
5. **Phase-by-Phase Execution Cadence & Progressive Verification**:
   - **Red**: Author failing invariant/specification tests first for core domain, concurrency, state machines, and security boundaries. Shift pure domain invariants to `Event.Domain.UnitTests`. Scaffold compilable stub types/interfaces so the project builds cleanly while the test fails at runtime.
   - **Green**: Implement production code to satisfy invariants.
   - **Ring 1 Sliced Verification (Inner Loop, < 2s)**: Run targeted test class via `--treenode-filter "/*/*/*<TestClass>/*"` in-memory (`Event.Domain.UnitTests` or `Event.Application.UnitTests`). Zero Docker containers, zero network I/O, zero database setup lag.
   - **Ring 2 Phase Verification (Phase Exit Gate, < 15s)**: Run Release build (`dotnet build -c Release -v q`) and at most ONE selected project test against ONE canonical provider (e.g. SQLite in-memory or single PostgreSQL container) within the worktree. Forbid multi-database provider matrices during intermediate phases.
   - **Yak-Shaving Quarantine Rule**: If an unrelated test fails outside the phase path, verify reproduction on clean base branch, log in `context.md` (`Validation Baseline / Pre-Existing Technical Debt`), quarantine it, and proceed with phase deliverable. Never attempt to fix unrelated test rot or broken fixtures outside the task scope.
   - **Semantic Phase Commit**: In the isolated worktree, all file changes belong exclusively to this task phase. Stage changes via `git add -A` (or phase-touched paths) and commit using the planned semantic Conventional Commit contract (type, scope, title, description, trailers) from `tasks.md`. Planning defines semantic meaning; execution handles file discovery.
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
8. **Ring 3 Plan Exit Gate & Pre-PR Rebase Gate**:
   - **Ring 3 Plan Exit Gate**: Run full 5-database matrix, EF Core migrations, and `Event.Architecture.Tests` once at workstream completion before PR creation.
   - **Pre-PR Rebase (Concurrency Conflict Protection)**:
     ```bash
     git fetch origin develop && git rebase origin/develop
     ```
     - If clean: proceed to push.
     - If merge conflicts occur: resolve conflicts inside `.worktrees/<task-name>`, run project verification tests, and complete the rebase (`git rebase --continue`).
9. **Pull Request Creation & Parked Worktree Protocol**:
   - **Push Branch**: `git push -u origin feat/<task-name> --force-with-lease`
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
   - **Park the Worktree (DO NOT DELETE)**:
     Never delete `.worktrees/<task-name>` upon PR creation. The worktree must remain parked and intact so that any subsequent bot reviews (Copilot, CodeQL) or CI check failures can be resolved immediately in-place with zero setup overhead.
   - **Halt and Await User Direction**:
     Immediately after PR creation, the agent must halt its execution and deliver a self-contained status brief:
     1. PR URL and branch name.
     2. Confirmation that the worktree remains parked at `.worktrees/<task-name>`.
     3. Notification that CI checks and automated bot reviewers (GitHub Copilot, CodeQL, SonarCloud) are running.
     4. Clear instruction to the user: inform the agent if there are any review comments, bot suggestions, or failing checks to address; OR confirm if everything is good/merged so the agent can delete the worktree.
   - **Worktree Teardown (Only Upon Explicit User Confirmation)**:
     Only when the user confirms that the PR is approved/merged or explicitly instructs to clean up, remove the worktree from the root workspace:
     ```bash
     git worktree remove .worktrees/<task-name>
     ```
     Ephemeral plan files in `dev/active/<task-name>` vanish cleanly with the worktree.

## Workflow

```text
1. Inspect approved dev/active/<task>/ (tasks.md, context.md, plan.md)
2. Setup isolated worktree under workspace root:
   git fetch origin develop
   git worktree add -b feat/<task> .worktrees/<task> origin/develop
   mkdir -p .worktrees/<task>/dev/active && mv dev/active/<task> .worktrees/<task>/dev/active/
3. Loop through Phases inside .worktrees/<task>:
   a. Red: compilable stubs + failing invariant test (in-memory domain first)
   b. Green: minimal implementation code
   c. Verify: Ring 1 sliced test (< 2s) -> Ring 2 phase build & single-provider test (< 15s)
      (Quarantine any unrelated pre-existing test rot into context.md)
   d. Commit: git add -A && git commit using semantic phase contract from tasks.md
   e. Update: batch checkbox updates in tasks.md
4. Knowledge Graduation (in worktree):
   a. Any deferred items? -> write dev/backlog/<slug>.md
   b. Any non-obvious lessons? -> append to dev/_journal/
   c. Any new architectural invariants? -> write ADR in docs/internal/adr/
   d. Stage and commit graduation files on feat/<task>
5. Ring 3 Plan Exit Gate & Pre-PR Rebase:
   a. Ring 3: Run full multi-provider matrix & architecture tests
   b. git fetch origin develop && git rebase origin/develop
   c. dotnet test (verify regression-free rebase)
6. PR Creation & Parked Worktree Handoff:
   a. git push -u origin feat/<task> --force-with-lease
   b. Inspect changed files and construct PR body with mandatory `## Release Impact` checklist
   c. gh pr create --base develop --title "..." --body "..."
   d. PARK the worktree (.worktrees/<task>) — DO NOT remove it!
   e. Stop and report to user with PR URL, parked worktree status, and request user direction:
      - Notify agent if there are any review/bot comments to address
      - OR confirm everything is good to delete the worktree
7. Worktree Teardown (Deferred — Only Upon User Confirmation):
   (from root workspace) git worktree remove .worktrees/<task>
```

## Verification Hooks
- `git diff --check -- .agents/skills/implement-tasks`
- Manually validate changed frontmatter against `.agents/skills/_SKILL_SCHEMA.md`

## Related Skills
- [../implementation-plan/SKILL.md](../implementation-plan/SKILL.md)
- [../senior-cto-feedback/SKILL.md](../senior-cto-feedback/SKILL.md)
- [../conventional-commit/SKILL.md](../conventional-commit/SKILL.md)
- [../finding/SKILL.md](../finding/SKILL.md)
