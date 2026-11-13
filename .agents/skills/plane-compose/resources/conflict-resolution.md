# Resolve local/remote conflicts without discarding teammates' changes

**Read when:** `plane diff` reveals drift, push reports conflicts, pull may overwrite work, or another agent/human has changed the same work item.

## Observe and choose a winner per field

1. Stop publishing. Record the exact project, item IDs and remote/local field differences. Check `git status` and preserve uncommitted local edits before any pull.
2. Use `plane diff` and `plane schema diff` as applicable. Remember: `plane diff` only knows remote objects **already tracked** in `.plane/state.json`.
3. When completeness matters, use an isolated remote clone or a reviewed pull. Do not mistake a missing entry in `plane diff` for proof no remote item exists.
4. Compare the last-synced baseline (state), the present local YAML/code and the present remote record. Decide field by field: new acceptance criteria, state, assignee, labels, description, dependencies and dates may have different owners.
5. Prefer merge of non-overlapping intent. Escalate genuine ambiguity or contradictory human changes; do not silently choose local because it is easiest.
6. After review, edit YAML, validate, dry-run and publish with normal conflict checking. Re-check remote state and Git diff.

## Example: reviewer moved work to Testing, agent still has In Progress

If the reviewer changed the state and added acceptance notes while the agent added implementation details locally, preserve the human's state and notes, merge the verified implementation evidence, and keep the ticket in Testing unless tests/review are actually complete. Never force `Done` from old local YAML.

## Pull for reconciliation

```bash
plane diff
# Take a Git checkpoint or backup outside work/ before any pull.
plane pull --merge
# Inspect git diff, including schema and any work/workitems.yaml ID collisions.
plane validate
plane push --dry-run
```

This command is a **local write**, not read-only. `--merge` does not guarantee a conflict-free, intention-preserving merge of all fields. If the project's task files are partitioned, review whether pulled items now duplicate those shards.

## Unsafe shortcuts

- Do not solve an ordinary conflict with `plane push --force`, `--no-conflict-check` or `--json` (which implies force).
- Do not delete remote objects, reset state or edit remote UUIDs to "fix" drift.
- Avoid `plane push --all` for conflict resolution; its multi-project mode forces pushes.
- If another process is pushing this project, wait for it to finish in the current work session or stop and report a concurrency blocker; never run concurrent pushes.

## When to ask for a decision

Ask when a conflict changes user-visible behavior, makes a completed task incomplete, loses human-authored acceptance criteria, reassigns ownership, or crosses authority boundaries. Provide a concise comparison and your recommended resolution; continue independent implementation work if possible.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/reference
