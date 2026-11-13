# Plan-driven Plane tracking for ISLAMU Event (`EVENT`)

**Read when:** writing, reviewing, or executing an implementation plan (`dev/active/<task>/`) in this repository. The sizing policy (how many items a plan may create) is authoritative in [taxonomy standard §4.1](../../../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md#41-tracking-budget-per-implementation-plan-plan--plane-mapping); this file is the command protocol that applies it. Project, Epic, state, label, and module UUIDs live in [`islamu-event-project.md`](../../plane-mcp/resources/islamu-event-project.md).

## The one rule

**One tracked item per separately merged pull request.** A single-PR plan (shape **S**) owns `1` Work Item. A plan that merges `2–5` PRs for one capability (shape **M**) owns `1` parent Work Item plus `1` Sub-item per PR. A program (shape **L**) owns one S- or M-shaped Work Item per shippable capability under an existing Epic. Phases inside one PR are `tasks.md` checkboxes only. More than `6` new items, or any new Epic, needs explicit user approval in the plan's Decision Brief.

## Who writes what

| Change | Tool | Why |
|---|---|---|
| Create items, mint `EVENT-XXX` | `plane-compose` push with `id:` omitted | Writes the ID back into YAML and records the item in `.plane/state.json` |
| `state`, `priority`, `labels`, `parent`, `assignees`, description, `dependencies` | `plane-compose` (edit YAML, push) | One writer per field; MCP edits to these fields make the YAML stale |
| Version Module membership | Plane MCP `module manage_workitems` | `modules.yaml` pushes module metadata only (v0.5.2) |
| Blocker or decision comment, external link | Plane MCP `workitem_comment` / `workitem_link` | Not expressible in YAML |
| Branch, commit, and PR links | Nothing: Plane's GitHub integration | Uppercase `EVENT-XXX` in branch, `Refs:` trailer, and PR `## Issues` |

## Stage 1 — Planning (`implementation-plan`): local files only

1. **Shape:** count the pull requests the plan will merge and classify it as trivial / S / M / L.
2. **Deduplicate:** `rg -n -i '<keyword>' event/work/*.yaml`, then Plane MCP `workitem list` with `pql: title ~ "<keyword>"` and `fields: sequence_id,name,state,parent`. If an item already describes the outcome, bind the plan to it (step 5) instead of creating one.
3. **Parent Epic:** pick the matching capability Epic from the project reference. Every new non-Epic item gets `parent: <Epic UUID>`.
4. **New item:** copy `event/templates/active-workitem.yaml` to `event/work/active-<task>.yaml` holding only the S item or the M parent: `id:` omitted, `state: Backlog`, no `assignees:`, Surface + Domain labels, `priority` = functional criticality. Do **not** write Sub-items yet: their `parent:` needs the parent's UUID, which exists only after the first push.
5. **Bound existing item:** move its block (with `id: EVENT-N`) from `event/work/workitems.yaml` into the active file, because one ID may live in only one `work/*.yaml` file. If the ID is missing from `.plane/state.json` (created in the Plane UI after the last full pull), add a state entry before writing the ID into YAML:

   ```bash
   jq --arg k "id:EVENT-N" --arg u "<uuid from MCP retrieve_by_identifier>" \
     '.work_items[$k] = {remote_id: $u, content_hash: "", source: "work/active-<task>.yaml:1", hash_version: 3, sequence_id: null}' \
     event/.plane/state.json > event/.plane/state.json.new && mv event/.plane/state.json.new event/.plane/state.json
   ```

   Without that entry the next push creates a duplicate. With it, the push updates the existing item.
6. **Record the Plane Tracking Plan** in `plan.md` §0 (table: item, role, type, labels, parent Epic, Version Module, priority, PR it links to) and the Plane action checkboxes in `tasks.md` (template in `implementation-plan/resources/operational-artifacts.md`).

## Stage 2 — Senior CTO review: local files only

Audit the Tracking Plan against the budget (no item per phase/test/review, no PR without an item, no item outside the plan's capability, correct Epic and module). On `Applied & Aligned`, set the active item to `state: Ready` in YAML. No push.

## Stage 3 — Execution start (`implement-tasks`)

```bash
cd event
plane schema validate && plane validate --offline
plane push --work-only --dry-run          # must list exactly this plan's creates/updates
printf 'y\n' | plane push --work-only     # v0.5.2 has no --yes; keep the conflict check on
```

1. If the dry run lists another task's pending file, hold that file back (move it out of `work/`, push, restore it unchanged). Never edit another task's file.
2. Read the minted ID from the active file and its UUID with `jq -r '.work_items["id:EVENT-N"].remote_id' .plane/state.json`.
3. **M plans:** append the Sub-items to the active file with `parent: <parent UUID>`, `state: Todo`, no assignees, then dry-run and push again.
4. Set the S item or parent to `state: In Progress` with `assignees:` = the developer driving the plan (member UUID via Plane MCP `member`), then push.
5. Add every new item to its Version Module with MCP `module manage_workitems` (`add_ids`) and read it back with `workitem count` (`pql: module = "<module uuid>" AND id = "EVENT-N"`).
6. Branch: `feat/EVENT-<S item or first Sub-item>-<slug>`.

## Stage 4 — During execution

- Phase commits carry `Refs: EVENT-<item this PR belongs to>`. Phases never change Plane state; `tasks.md` tracks them.
- When an M plan starts its next PR, set that Sub-item to `In Progress` with an assignee and push.
- **Blocked** by work outside the plan: `state: Waiting Dependency`, `dependencies.blocked_by: [EVENT-M]` when the blocker is an item, and one MCP comment naming the blocker and the unblock condition.
- **Scope grows** by a whole new pull request: add one Sub-item only if the plan stays within budget; otherwise fold the work into an existing Sub-item or ask the user with a Decision Brief.
- Ideas found along the way go to `dev/backlog/<slug>.md`, not to Plane.

## Stage 5 — Pull request, merge, and retirement

1. PR `## Issues`: `- Plane: [EVENT-<this PR's item>]` (bracketed: links the PR and drives PR state automation where enabled). Mention an M parent unbracketed (`EVENT-<parent>`) so automation never moves it.
2. PR opened: `Review Needed`. Review asks for changes: `Changes Requested`. Merged with acceptance evidence: `Done`. An M parent becomes `Done` when its last Sub-item is `Done`.
3. If PR automation already moved an item remotely, mirror that state in YAML before the next push (check with MCP `retrieve_by_identifier`, `fields: state,updated_at`); otherwise the conflict check reports it.
4. **Retire by folding, not by pulling:** move the item blocks (with their `id:`) from `work/active-<task>.yaml` to the end of `work/workitems.yaml`, delete the active file, then confirm `plane validate --offline` passes and `plane push --work-only --dry-run` reports no changes. Moving a tracked block between files changes nothing remotely.

## v0.5.2 facts behind this protocol (verified 2026-11-13 on `EVENT`, 1,355 items)

- The push conflict check fetches only the items being **updated**, so task-sized pushes check in seconds. `--no-conflict-check` is reserved for maintainer-approved bulk pushes after an MCP drift check (`updatedAt >= "<last sync date>"` compared with each item's `last_synced`).
- `plane diff` fetches every tracked item (about three calls each): over an hour on this project. Use `plane status` and `plane push --dry-run` in the task loop instead.
- `plane pull` takes about `items ÷ 50` minutes and needs the three repairs in the [journal](../../../../dev/_journal/domains/plane-project-management.md) (label UUIDs, missing state entries for Plane-only items, stale hashes) before the next push. It is a maintainer-scheduled resync, never a per-task step.
- The client limits itself to 50 requests per minute, and Plane Cloud allows about 60 per minute per key, shared with MCP. Never run two `plane` commands, or bulk MCP calls, alongside a push.
- The project's default assignee was cleared on 2026-11-13, so created items start unassigned.
