# Plan-driven Plane tracking for ISLAMU Event (`EVENT`)

**Read when:** writing, reviewing, or executing an implementation plan (`dev/active/<task>/`) in this repository. The sizing policy (how many items a plan may create) is authoritative in [taxonomy standard §4.1](../../../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md#41-tracking-budget-per-implementation-plan-plan--plane-mapping); this file is the command protocol that applies it. Project, Epic, state, label, and module UUIDs live in [`islamu-event-project.md`](../../plane-mcp/resources/islamu-event-project.md).

## The one rule

**One tracked item per separately merged pull request.** A single-PR plan (shape **S**) owns `1` Work Item. A plan that merges `2–5` PRs for one capability (shape **M**) owns `1` parent Work Item plus `1` Sub-item per PR. A program (shape **L**) owns one S- or M-shaped Work Item per shippable capability under an existing Epic. Phases inside one PR are `tasks.md` checkboxes only. More than `6` new items, or any new Epic, needs explicit user approval in the plan's Decision Brief.

## Programme lifetime: tracking follows the canonical plan, not the branch

A plan that merges several PRs stays **one programme with one canonical triad** in the main checkout until its final PR scope is merged and reconciled. Each PR scope in the triad's PR registry maps to exactly one tracked item; the registry, not Plane, records which scope is delegated to which execution worktree and its separate delivery, reconciliation, and cleanup facts ([PR scope contract](../../implementation-plan/resources/pr-scope-contract.md), [PR execution lifecycle](../../implement-tasks/resources/pr-execution-lifecycle.md)).

- **Intermediate merges retire nothing.** After a non-final PR merges, the canonical triad and `event/work/active-<task>.yaml` both stay in place, with the remaining scopes and their items. Execution worktrees reach the same active file through the `event/` symlink; it is never copied into a worktree.
- **A larger programme keeps one triad.** When honest PR scoping needs more than `6` new items, or a new Epic, ask for that budget explicitly in the Decision Brief (one line of reason per item). Never split a coherent programme into several plans, or merge independently reviewable PRs into one card, only to stay under the budget.
- **Plane state is not reconciliation.** `Review Needed` and a merged PR record review and merge only. An item becomes `Done` after the merged delivery has been reconciled against its scope's required acceptance. Worktree and branch cleanup is tracked in the PR registry and never maps to a Plane state.

## Who writes what

| Change | Tool | Why |
|---|---|---|
| Create items, mint `EVENT-XXX` | `plane-compose` push with `id:` omitted | Writes the ID back into YAML and records the item in `.plane/state.json` |
| `state`, `priority`, `labels`, `parent`, `assignees`, description, `dependencies` | `plane-compose` (edit YAML, push) | One writer per field; MCP edits to these fields make the YAML stale |
| Version Module membership | Plane MCP `module manage_workitems` | `modules.yaml` pushes module metadata only (v0.5.2) |
| Blocker or decision comment, external link | Plane MCP `workitem_comment` / `workitem_link` | Not expressible in YAML |
| Branch, commit, and PR links | Nothing: Plane's GitHub integration | Uppercase `EVENT-XXX` in branch, `Refs:` trailer, and PR `## Issues` |

## Stage 1 — Planning (`implementation-plan`): local files only

1. **Shape:** size PR scopes first under the [PR sizing default](../../implementation-plan/resources/pr-scope-contract.md#pr-sizing-default-and-anti-over-splitting) (default to 1 multi-phase PR / Shape S, or 2 PRs along a real risk/contract boundary; use 3+ PRs only for genuinely multi-week or multi-surface programmes with explicit justification), then count the PR scopes in the plan's registry and classify as trivial / S / M / L. Do not reshape PRs to fit or fill the Plane budget.
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
plane push --work-only                    # interactive; v0.5.2 has no --yes; keep the conflict check on
```

Run the push interactively and read each prompt before you answer it. Confirm only when the prompt shows exactly the reviewed dry-run plan. Never answer a conflict prompt or any prompt you didn't expect: decline, then follow [conflict resolution](conflict-resolution.md). Don't pipe input into the push, because an unexpected leading prompt would consume it before anyone could stop. Existing receipts from earlier create-only pushes stay as they are.

1. If the dry run lists another task's pending file, hold that file back (move it out of `work/`, push, restore it unchanged). Never edit another task's file.
2. Read the minted ID from the active file and its UUID with `jq -r '.work_items["id:EVENT-N"].remote_id' .plane/state.json`.
3. **M plans:** append the Sub-items to the active file with `parent: <parent UUID>`, `state: Todo`, no assignees, then dry-run and push again.
4. Set the S item or parent to `state: In Progress` with `assignees:` = the developer driving the plan (member UUID via Plane MCP `member`), then push.
5. Add every new item to its Version Module with MCP `module manage_workitems` (`add_ids`) and read it back with `workitem count` (`pql: module = "<module uuid>" AND id = "EVENT-N"`).
6. Branch: `feat/EVENT-<S item or first Sub-item>-<slug>`.

## Stage 4 — During execution

- Phase commits carry `Refs: EVENT-<item this PR belongs to>`. Phases never change Plane state; `tasks.md` tracks them.
- When an M plan starts its next PR scope (the registry marks it Ready only after its predecessors are merged and reconciled), set that Sub-item to `In Progress` with an assignee and push. Delegating a scope to a worktree changes the registry, not Plane.
- **Blocked** by work outside the plan: `state: Waiting Dependency`, `dependencies.blocked_by: [EVENT-M]` when the blocker is an item, and one MCP comment naming the blocker and the unblock condition.
- **Scope grows** by a whole new pull request: add one Sub-item when the plan stays within its approved budget; otherwise ask the user with a Decision Brief for the larger budget. Folding a separately merged PR into another PR's Sub-item is wrong, because that item would then cover two PRs.
- Ideas found along the way go to `dev/backlog/<slug>.md`, not to Plane.

## Stage 5 — Pull request, merge, and reconciliation (every PR)

1. PR `## Issues`: `- Plane: [EVENT-<this PR's item>]` (bracketed: links the PR and drives PR state automation where enabled). Mention an M parent unbracketed (`EVENT-<parent>`) so automation never moves it.
2. PR opened: `Review Needed`, after the review snapshot has been reconciled into the canonical PR scope. Review asks for changes: `Changes Requested`.
3. **Merged:** confirm the actual merge state and target branch, then reconcile the final reviewed delivery against the scope's required acceptance in the canonical triad. Only then set the S item or Sub-item to `Done`. A squash or rebase merge counts as merged even though the original commit identities changed. A PR closed without merging, or only approved, is not delivered: the item stays open (`In Progress` or `Changes Requested`) until the user re-scopes it; `Won't Do` or `Replaced` needs the user's explicit direction.
4. **Unmet acceptance after merge:** a merged PR does not silently complete a required criterion. Keep the item open, or record the remaining obligation as an approved successor scope in the same programme (with its own item when it will merge separately). Never move required programme scope to `dev/backlog/` or close it by checking a task.
5. **M parent:** `Done` only after every Sub-item is `Done` and every retained programme obligation is satisfied, not merely when the last PR merges.
6. If PR automation already moved an item remotely, mirror that state in YAML before the next push (check with MCP `retrieve_by_identifier`, `fields: state,updated_at`); otherwise the conflict check reports it ([conflict resolution](conflict-resolution.md#default-targeted-cloud-read-then-a-local-field-merge)). If automation set `Done` before reconciliation finished, finish the reconciliation; if it shows an unmet criterion, move the item back to an open state and record why in one MCP comment.

After a non-final PR, stop here. The active YAML file and the canonical triad stay in place for the next scope.

## Stage 6 — Programme retirement (after the final reconciled PR only)

Retire tracking once, when the registry shows every PR scope merged (or explicitly removed by the user) and reconciled, and the programme's final acceptance is satisfied:

1. **Retire by folding, not by pulling:** move the item blocks (with their `id:`) from `work/active-<task>.yaml` to the end of `work/workitems.yaml`, delete the active file, then confirm `plane validate --offline` passes and `plane push --work-only --dry-run` reports no changes. Moving a tracked block between files changes nothing remotely.
2. Never fold early to tidy `work/`: one ID may live in only one `work/*.yaml` file, so a programme that folded early has to move its blocks back before its next push.
3. Folding retires tracking only. Worktree, branch, and triad removal follow the execution lifecycle and need the user's current instruction plus merge and preservation evidence.

## v0.5.2 facts behind this protocol (verified 2026-11-13 on `EVENT`, 1,355 items)

- The push conflict check fetches only the items being **updated**, so task-sized pushes check in seconds. `--no-conflict-check` is reserved for maintainer-approved bulk pushes after an MCP drift check (`updatedAt >= "<last sync date>"` compared with each item's `last_synced`).
- `plane diff` fetches every tracked item (about three calls each): over an hour on this project. Use `plane status` and `plane push --dry-run` in the task loop instead.
- `plane pull` takes about `items ÷ 50` minutes and needs the three repairs in the [journal](../../../../dev/_journal/domains/plane-project-management.md) (label UUIDs, missing state entries for Plane-only items, stale hashes) before the next push. It is a maintainer-scheduled resync, never a per-task step.
- The client limits itself to 50 requests per minute, and Plane Cloud allows about 60 per minute per key, shared with MCP. Never run two `plane` commands, or bulk MCP calls, alongside a push.
- The project's default assignee was cleared on 2026-11-13, so created items start unassigned.
