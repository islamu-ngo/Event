# Optional repository integration for proactive skill activation

**Read when:** the user wants a coding agent to automatically invoke Plane Compose while editing software, rather than only when they mention Plane by name.

The skill's `description` already matches substantive engineering work in a repo using Plane Compose. However, a short repository-specific `AGENTS.md` instruction can establish the **actual Plane project folder, authority and work policy**, eliminating ambiguity. Do not overwrite an existing `AGENTS.md`; integrate only with the user's permission.

## ISLAMU Event Production Integration Reference

For this repository, Plane Compose is configured as follows:
- **Project Root:** `./event/plane.yaml` (Workspace `islamu`, Project Key `EVENT`, Connection `connection-1`).
- **Enabled Project Features (`event/schema/features.yaml`):** `cycles`, `modules`, `pages`, `views`, `intakes`, `epics`, `work_item_types`, `project_updates`, `milestones`, `time_tracking` (`workflows: false` on current workspace plan).
- **Privacy Boundary:** `event/` and `event-backup-*/` are gitignored to prevent leaking internal operational data to public version control.
- **The Dual-Plane Cognitive Model:** Separates macro deliverable tracking (`event/work/active-<task>.yaml`) from machine-native micro-execution (`dev/active/<task>/tasks.md`).
- **The Active Task Partitioning Pattern (Ephemeral Staging):** `event/work/workitems.yaml` holds the 2,616-item baseline monolith cleanly so `plane pull` does not duplicate sharded files. In-flight features author small (< 15 KB) dedicated files (`event/work/active-<task-slug>.yaml`) from `./event/templates/active-workitem.yaml` (stored outside `work/` so Plane never ingests the template itself), omitting `id:` initially so first push auto-writes canonical `EVENT-XXX`. **Delete/retire `event/work/active-<task-slug>.yaml` as soon as a post-merge `plane pull` writes `EVENT-XXX` into `work/workitems.yaml`** so `plane validate` never fails with `Duplicate ID`.
- **Native Git Development & PR Linking:** Never paste commit logs or PR URLs into Plane `description:`. Once canonical uppercase `EVENT-XXX` is known, name branches `feat/EVENT-XXX-<task>`, include `Refs: EVENT-XXX` / `Fixes: EVENT-XXX` in commit trailers, and reference `[EVENT-XXX]` in the PR description (`## Issues`) so Plane's GitHub integration populates the work item **Development** panel and PR state automation seamlessly when Pro + GitHub App integration is enabled.
- **Worktree Symlink Protocol:** When running in isolated worktrees (`.worktrees/<task>`), symlink the root project: `ln -s ../../event event`.
- **Preflight Pipeline:** Validate before push: `cd event && plane schema validate && plane validate --offline && plane validate && plane push --dry-run`. Additive updates push via `plane push --work-only`. Destructive operations (`--force`, `schema sync`, `--prune`) are strictly forbidden. Remember `plane diff` queries each tracked ID individually (slow on 2,600+ items); use `plane status` + `plane push --dry-run` for fast hash-based inner-loop previews.

## Suggested `AGENTS.md` excerpt (customize, don't paste blindly)

```markdown
## Project tracking with Plane Compose

Use the `plane-compose` skill for significant feature, bug, refactor,
release and backlog work. The Plane Compose project is at
`./project-management/plane.yaml` (update this path to the real one).

For substantive coding tasks: identify/reuse an existing work item,
record scope and acceptance criteria, implement and verify the code,
update the work item's actual state and evidence, and report any
remaining blockers. Avoid tickets for trivial edits.

Authority: local work-item YAML updates are allowed. Remote additive
work-item pushes are [allowed only after preflight / require approval]
(select the appropriate policy). Schema/workspace/destructive changes
always require explicit approval. Never force conflicts.

Before any push: verify the effective Plane server/workspace, run
`plane schema validate`, `plane validate`, and `plane push --dry-run`.
Keep `.plane/` and tokens out of git. Do not assume `plane status` is
live or that `plane diff` lists remote-only items.
```

The specific path and permission choice **must be supplied by the repo/user**; the example path above is not evidence about a real repository. A centralized `AGENTS.md` can also identify the project's states, coding verification command and what counts as Done for that team.

## Why not hardcode these assumptions in the global skill?

The same skill may serve many repositories, connections and projects. Generic docs should govern *how* to work safely; repo-specific instructions govern *which project*, *when a remote write is allowed*, and *what verification is required*. This separation keeps the skill reusable and protects unrelated Plane instances.

## Operating limitations

A skill guides an **agent currently executing in the repository**. It cannot make agents continuously monitor Plane while not running, grant credentials, or perform pushes without tools. For scheduled monitoring, supply a separately authorized runner/automation with appropriate credentials, testing and approvals.

**Skill specification:** https://github.com/agentskills/agentskills/blob/main/docs/specification.mdx
