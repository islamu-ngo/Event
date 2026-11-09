---
name: plane-compose
description: >-
  Manage software projects end-to-end with Plane Compose (the `plane` CLI) and version-controlled Plane YAML. Use to plan and prioritize engineering work, create/deduplicate work items, implement features or fixes while updating their progress, report remaining work, manage cycles/releases, or safely validate/diff/push/pull project and workspace configuration. In a repository already using Plane Compose, proactively apply during substantial coding tasks; do not create tracking noise for trivial edits.
type: workflow
enforcement: suggest
priority: high
compatibility: >-
  Requires shell/file access for local YAML; Plane Compose CLI (behavior verified for v0.5.2). Remote operations require Plane credentials and network access.
---

# Plane Compose — engineering execution and project management

**Mission:** Act as an accountable software engineer *and* project manager. Maintain coherent Plane work items as code while doing the requested implementation. A ticket is an actionable record, not a substitute for shipping code. Follow the repository's `AGENTS.md`, team policies, and the user's instructions over this skill.

Use **progressive disclosure**: this entrypoint is the operating contract; read only the relevant `resources/*.md` file(s) for the active task, not the entire folder.

## Activate when

- The user mentions Plane, Plane Compose, `plane.yaml`, a Plane workspace/project, YAML work items, backlog, cycle, release, or Plane synchronization.
- A **substantial** feature, defect, refactor, integration, or release is being implemented in a repo that already contains a Plane Compose **project**; maintain a work item through delivery even if the user only requests the code.
- The user asks what is next, what is blocked, how much remains, or which work should be prioritized in a Plane-managed repo.

**Do not activate** for a one-line unrelated edit, casual brainstorm, generic coding Q&A, or a repo without Plane Compose unless the user asks to set it up. Never initialize a Plane project just because a code repository exists.

## Start: establish facts before acting

1. Find the repository root and candidate `plane.yaml` files. Distinguish `type: project` from `type: workspace`. If there are several, determine the project covering the affected code; never silently choose the first.
2. Check `git status --short` and existing YAML before making changes. Preserve uncommitted edits by other people/agents. Check `plane --version` and `plane auth list-connections` if CLI/remote access is required; never display tokens.
3. Inspect `workspace:`, `connection:`, `api_url:` and `project.key` in the selected `plane.yaml`. Do not guess which Plane server a write will reach.
4. Establish a **local baseline** (`plane status`, `plane validate --offline`, `plane schema validate`) and, when remote freshness matters, a **remote comparison** (`plane diff`, `plane schema diff`). `status` is offline; `diff` does **not** discover remote-only work items.
5. Identify task scope and write policy. If the user delegated ongoing Plane management for this repo, routine **additive work-item** synchronization may proceed after checks. Otherwise prepare local YAML and ask before publishing remote changes. Structural, destructive, or conflict-overriding operations always need explicit approval.

For detailed discovery, connection selection, version behavior and project setup read [environment-and-targets](resources/environment-and-targets.md) and [project-bootstrap](resources/project-bootstrap.md).

## Default engineering loop

**Inspect → Reconcile → Plan → Implement → Verify → Synchronize → Report.**

1. **Inspect:** Understand code, relevant tests, existing Plane items, team conventions, existing priorities and blockers. Never infer the entire remote backlog from local YAML alone.
2. **Reconcile:** Match the request to an existing work item by key or meaning; update it instead of duplicating it. Address remote edits safely before changing the same item. If no item exists, create one meaningful, scoped item (or a bounded parent and children for a large feature).
3. **Plan:** Record acceptance criteria, risk, dependencies, realistic priority, and owner where known. Do not invent team assignments, dates, story points or statuses. For large tasks, decompose along independently verifiable deliverables.
4. **Implement:** Make the requested code changes. Update the *tracked* item through the actual workflow (`Ready` → `In Progress` → `In Review` / `Testing` → `Done`, if those states exist). Do not advance an item to `Done` until acceptance evidence exists.
5. **Verify:** Run relevant tests, lint, builds and/or manual checks. Note precisely what ran, what passed, what failed and what was not tested. A merged PR, green CI, successful local test and written code are different evidence levels.
6. **Synchronize:** Edit Plane YAML minimally; validate **before** any push; review the diff and dry-run; push only to the confirmed project under the write policy. Run only one `plane push` per project at a time. Check resulting IDs and YAML write-back.
7. **Report:** Say what was implemented, which Plane work item(s) changed, which checks ran, whether remote Plane was actually updated, what remains and the highest-value next action. If CLI/network is unavailable, explicitly mark Plane sync as **pending**, not successful.

For execution details read [implementation-loop](resources/implementation-loop.md), [work-item-authoring](resources/work-item-authoring.md), [sync-protocol](resources/sync-protocol.md) and [progress-reporting](resources/progress-reporting.md).

## Task-to-resource router

| Intent / signal | Read |
| --- | --- |
| Install/login, self-hosting, wrong workspace, correct connection | [environment-and-targets](resources/environment-and-targets.md) |
| New project, clone existing, fresh checkout, template initialisation | [project-bootstrap](resources/project-bootstrap.md) |
| Understand folder layout, `plane.yaml`, schema/work boundaries | [project-files](resources/project-files.md) |
| Create/update tasks, types, dependencies, IDs, YAML fields | [work-item-authoring](resources/work-item-authoring.md) |
| Intake requirements, dedupe, break down, assess urgency / priority | [backlog-triage](resources/backlog-triage.md) |
| Implement code and maintain accurate ticket lifecycle | [implementation-loop](resources/implementation-loop.md) |
| "What remains?", blockers, status, progress, project health | [progress-reporting](resources/progress-reporting.md) |
| Push, pull, merge, validate, drift detection | [sync-protocol](resources/sync-protocol.md) |
| Conflicting local and remote edits | [conflict-resolution](resources/conflict-resolution.md) |
| Lost `.plane/`, duplicates, partial pushes and CLI errors | [state-recovery](resources/state-recovery.md) |
| Types, states, labels, workflows, feature flags | [schema-management](resources/schema-management.md) |
| Cycles, modules, milestones, releases and dependencies | [delivery-planning](resources/delivery-planning.md) |
| Project or workspace reusable templates / upgrades | [templates-and-upgrades](resources/templates-and-upgrades.md) |
| Workspace-wide webhooks, automations, types, release rules | [workspace-management](resources/workspace-management.md) |
| Many projects, CI, headless automation, shared agents | [multi-project-and-ci](resources/multi-project-and-ci.md) |
| Command safety, approvals, secrets and destructive flags | [safety-boundaries](resources/safety-boundaries.md) |
| Versions, command quirks, trusted docs, checklists / skill tests | [compatibility-and-evals](resources/compatibility-and-evals.md) |
| Make the skill activate reliably for future coding work in this repo | [repo-integration](resources/repo-integration.md) |

## Invariants — never skip

- **Right target first:** `plane.yaml` selects credentials for project commands. `-c` on `push` / `pull` only changes a rate-counter label, **not credentials** (v0.5.2).
- **Existing work wins:** prefer updating a known item over creating a near-duplicate. Give every new item a stable, unique `id`; don't fabricate an existing Plane sequence ID.
- **No unsupported fields:** use top-level work items plus `parent`; `children` and `watchers` do not push. Match existing state/type/label names and project feature flags.
- **Preflight local validity:** `plane schema validate` and `plane validate` before push. `plane push` may already have changed the remote schema by the time work-item validation fails.
- **Preview before publishing:** `plane diff`, `plane schema diff` if relevant, and `plane push --dry-run`. Do not treat `plane diff` as a full remote inventory.
- **No silent destructive or conflict bypass:** no `schema sync`, `--prune`, `state reset`, `ws state reset`, `--no-conflict-check`, `push --all`, `push --json`, or write with `--force` without explicit high-risk authorization. `push --json` *implies* `--force` in v0.5.2. Never use such modes merely to avoid interaction.
- **'Dry run' can lie:** `plane upgrade --dry-run` and `plane ws upgrade --dry-run` modify local schema unless also given `--skip-pull`.
- **State is local and ephemeral:** commit `plane.yaml`, `schema/`, `work/` as appropriate; never commit `.plane/`, API tokens, secrets or debug logs. After a fresh checkout with no state, use `plane schema import` and reconcile before pushing.
- **No fictional progress:** never mark work completed based only on intention, generated code, assumed tests or unverified remote synchronization.

## Expected final answer for a managed coding task

A concise operational handoff containing: **code outcome; Plane project and item key(s); item state/priority and any blockers; test/build evidence; remote sync status (synced, local-only, blocked, or pending); remaining work / next priority.** Clearly distinguish observed facts from suggestions.

## Versioned authority

Based on **Plane Compose v0.5.2**. Always check installed version and `plane <command> --help`; if behavior differs, confirm in the live docs rather than blindly applying v0.5.2 flags. Canonical sources: [Plane Compose docs](https://developers.plane.so/dev-tools/plane-compose), [project files](https://developers.plane.so/dev-tools/plane-compose/project-files), [sync projects](https://developers.plane.so/dev-tools/plane-compose/sync-projects), [reference](https://developers.plane.so/dev-tools/plane-compose/reference), [Agent Skills specification](https://github.com/agentskills/agentskills/blob/main/docs/specification.mdx).
