# Resolve local/remote conflicts without discarding teammates' changes

**Read when:** `plane diff` reveals drift, push reports conflicts, pull may overwrite work, or another agent/human has changed the same work item.

## Observe and choose a winner per field

1. Stop publishing. Record the exact project, item IDs and remote/local field differences. Check `git status` and preserve uncommitted local edits before any pull.
2. Read the affected items from Cloud (see the [targeted default](#default-targeted-cloud-read-then-a-local-field-merge) below) and use `plane schema diff` for schema edits. Project-wide `plane diff` fetches every tracked item (over an hour on `EVENT`) and only knows objects **already tracked** in `.plane/state.json`, so it isn't the task-loop default.
3. When completeness of the whole remote inventory matters, use a fresh isolated `plane clone` in another directory, or a separately scheduled [maintenance pull](#scheduled-full-pull-maintenance-only). A missing entry in `plane diff` isn't proof that no remote item exists.
4. Compare the last-synced baseline (state), the present local YAML/code and the present remote record. Decide field by field: new acceptance criteria, state, assignee, labels, description, dependencies and dates may have different owners.
5. Prefer merge of non-overlapping intent. Escalate genuine ambiguity or contradictory human changes; do not silently choose local because it is easiest.
6. After review, edit YAML, validate, dry-run and publish with normal conflict checking. Re-check remote state and Git diff.

## Example: reviewer moved work to Testing, agent still has In Progress

If the reviewer changed the state and added acceptance notes while the agent added implementation details locally, preserve the human's state and notes, merge the verified implementation evidence, and keep the ticket in Testing unless tests/review are actually complete. Never force `Done` from old local YAML.

## Default: targeted Cloud read, then a local field merge

The default reconciliation isn't a pull. For each affected item, read the Cloud record with Plane MCP `workitem retrieve_by_identifier` (sparse `fields`, for example `id,name,state,labels,priority,assignees,parent,updated_at`), compare it with the item's `last_synced` in `.plane/state.json` and the local YAML, then merge field by field in YAML. Ownership follows the [sync protocol](sync-protocol.md#hybrid-plane-compose--plane-mcp-batches): Compose owns YAML-held fields, and MCP owns Version Module membership, comments and links.

Supported drift on YAML-held fields (a human in the UI, PR automation, or a direct MCP edit) must be mirrored into local YAML before the next push that touches that item. Then validate, dry-run and push with normal conflict checking.

This isn't a universal procedure. It covers the common cases below; anything outside them gets the observe-and-decide steps above.

| Situation | What to do |
| --- | --- |
| Cloud-only items unrelated to your change, and you're creating one new disjoint item | Nothing to import. Create the item with `id:` omitted; you don't need the whole Cloud project locally first. |
| You need to adopt an existing Cloud-only item with no state entry | Follow [plan-tracking](plan-tracking.md) stage 1, step 5 (state entry first). Never mint or bind IDs through MCP. |
| Remote-only change to an affected item, no pending local change | Still reconcile it. An unchanged local hash means `plane push --dry-run` can show nothing while Cloud moved, so read the item and mirror the change before you edit and push it. |
| Local update to an item | The push conflict check runs on updates; keep it on. It can't close a race with another writer pushing the same item at the same moment, so don't run concurrent project pushes. |
| Both sides changed the same field | Ask the owner for a decision, then apply it and keep every accepted field from both sides. |
| You adopted the exact remote value, the content hash now matches, but a stale baseline still reports drift | Don't reset or hand-edit the cache to certify sync. If the ordinary conflict check can't converge, use the reviewed native maintenance recovery in the [journal](../../../../dev/_journal/domains/plane-project-management.md). |

The push preview covers every pending `work/*.yaml` file, not just yours. To hold another task's file back, move it out of `work/` for that one push and restore it unchanged, as the [sync protocol](sync-protocol.md#hybrid-plane-compose--plane-mcp-batches) describes.

## Scheduled full pull (maintenance only)

A full `plane pull` is an explicit, isolated, backed-up maintenance resync, not a conflict fix. In v0.5.2 `--merge` is accepted but ignored, and the pull overwrites canonical `work/workitems.yaml` ([sync protocol](sync-protocol.md#pulling-safely)). It doesn't preserve local intent.

```bash
# Maintainer-scheduled only. Take a Git checkpoint or backup outside work/ first.
plane pull
# Inspect git diff, including schema and any work/workitems.yaml ID collisions.
plane validate
plane push --dry-run
```

This command is a **local write**, not read-only. If the project's task files are partitioned, pulled items will duplicate those shards; drop the duplicates and apply the repairs the journal lists before the next push.

## Unsafe shortcuts

- Do not solve an ordinary conflict with `plane push --force`, `--no-conflict-check` or `--json` (which implies force).
- Do not delete remote objects, reset state or edit remote UUIDs to "fix" drift. Don't fake synced hashes or `last_synced` timestamps to silence it.
- Don't auto-answer a detected conflict prompt (for example by piping `y`). Stop and reconcile instead.
- Avoid `plane push --all` for conflict resolution; its multi-project mode forces pushes.
- If another process is pushing this project, wait for it to finish in the current work session or stop and report a concurrency blocker; never run concurrent pushes.

## When to ask for a decision

Ask when a conflict changes user-visible behavior, makes a completed task incomplete, loses human-authored acceptance criteria, reassigns ownership, or crosses authority boundaries. Provide a concise comparison and your recommended resolution; continue independent implementation work if possible.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/reference
