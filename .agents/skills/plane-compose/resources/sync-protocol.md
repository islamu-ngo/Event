# Safe validation, diff, pull and push protocol

**Read when:** publishing edits to Plane, importing remote changes, resolving drift, or automating synchronization for one project.

## Classify command effects (v0.5.2)

| Command | What it really means |
| --- | --- |
| `plane status` | Offline read of local YAML and `.plane/state.json`; not remote truth. |
| `plane schema validate` | Offline schema validation. |
| `plane validate --offline` | Offline work validation; skips live type/state/label/property lookup. |
| `plane validate` | More complete work validation; read-only remote lookups. |
| `plane diff` | Remote-aware comparison for **tracked** work; not a full remote inventory. |
| `plane schema diff` | Remote-aware project schema comparison. |
| `plane push --dry-run` | Preview only; no intended writes. |
| `plane push` | Push schema, then cycles/modules/milestones/pages, then work items. Creates/updates, not work-item deletes. |
| `plane push --work-only` | Skip schema step; still updates relevant work files. |
| `plane pull` | **Overwrites `work/workitems.yaml`** (`sync/writer.py#L259`) and schema/operational YAML from Plane, resetting work-item `source` in `.plane/state.json`. |
| `plane pull --merge` | Flag accepted by CLI in v0.5.2, **but currently unused inside `_pull()`** — still overwrites `work/workitems.yaml`. |
| `plane schema import` | Connect local schema names to remote IDs in state; default doesn't change YAML. |

## Before every remote write

1. Confirm target folder, `type: project`, `workspace:`, `connection:`, effective server, `project.key`, CLI version, and authorization.
2. Check `git status --short`; preserve other contributors' edits. For planned local-overwrite commands, establish a rollback checkpoint using Git or a backup **outside `work/*.yaml`**.
3. Reconcile remote changes if relevant: `plane diff` plus, for schema edits, `plane schema diff`. Guard against remote-only items and sharded task files.
4. Validate **both** local schema and work before a full push:

```bash
plane schema validate
plane validate
plane push --dry-run
```

5. Read the preview for target, creates/updates, unexpected schema effects, duplicate IDs or unknown dependencies. If it doesn't match intent, stop. `plane validate` can miss a mistyped work item type if Plane returns no work-item types; still inspect the actual schema/remote types.
6. Publish with normal conflict checking, after the user has delegated this category of write or explicitly approved it:

```bash
plane push --work-only  # when only work data changed
# OR:
plane push              # when authorized schema + work changes are intended
```

7. Inspect exit code and output. Rerun `plane status` / relevant `diff`, inspect Git diff for write-backs, and report *observed* remote status. There can be partial failures; don't report blanket success on a warning.

## Pulling safely

`plane pull` refreshes local schema and operational YAML. Because v0.5.2 `write_work_items_canonical()` unconditionally overwrites `work/workitems.yaml` and resets `.plane/state.json` `source` pointers (`--merge` is currently ignored in project `_pull()`), **inspect local uncommitted diffs and take an isolated backup outside `work/*.yaml` before running `plane pull`**.

**One-way multi-file hazard & ephemeral task retirement:** `push` / `validate` read all `work/*.yaml` files, whereas `pull` dumps all remote work items back into `work/workitems.yaml`. Any item pushed from a shard or ephemeral file (`work/active-<task>.yaml`, `work/backend.yaml`) will now also appear in `work/workitems.yaml`, triggering `Duplicate ID` errors on `plane validate` (note the error cites `item.title[:50]`, not the colliding filename). **Delete/retire ephemeral `work/active-<task>.yaml` files once `plane pull` brings their canonical IDs into `work/workitems.yaml`.** Do not place templates or backups ending in `.yaml` inside `work/`.

**Large-project performance (2,000+ items):** `plane diff` fetches every tracked `remote_id` in `.plane/state.json` one by one (`Semaphore(3)` -> retrieve + relations + properties per item), and `plane pull` hydrates relations per item plus fetches `list_work_items_raw()` twice unless `--no-properties` is passed. Prefer `plane status` + `plane push --dry-run` for fast hash-based inner-loop previews, `--skip workitems` on schema diffs, and `--no-properties` on bulk pulls when custom properties aren't needed.

For read-only remote inventory, prefer a fresh isolated `plane clone` to a different directory if the context allows, then inspect without touching the active project.

## Hybrid `plane-compose` + Plane MCP batches

When one change set mixes YAML pushes with Plane MCP mutations (module membership, comments, deletions), keep this order:

1. Reconcile pre-existing pending items first. `plane push` has **no per-file filter**: every pending `work/*.yaml` change goes out together, including other tasks' `active-*.yaml` files. Inspect `plane status` and `plane push --dry-run`; to hold back another task's file for one push, move it out of `work/` and restore it right after (never edit its content).
2. `plane schema push` (only for approved schema changes).
3. `plane push --work-only` for new parents (Epics), then read their minted IDs and UUIDs back.
4. `plane push --work-only` for children and edited items (`parent:` takes the parent's **UUID**).
5. Plane MCP mutations (Version Module membership, comments, links, approved deletions).
6. `plane pull` (after backing up `event/`) so YAML reflects what MCP changed, then retire consumed `active-*.yaml` files.

**Disjoint `work/*.yaml` rule:** an `EVENT-XXX` ID lives in exactly one `work/*.yaml` file at a time. Edit existing items in place in `work/workitems.yaml`. Put only **new** items (with `id:` omitted) in staging files such as `work/active-<task>.yaml`. Copying an existing ID into a staging file fails validation with `Duplicate ID`.

**Never mint through MCP:** an item created with MCP `workitem create` is unknown to `.plane/state.json`. Hand-writing its `id:` into YAML makes the next `plane push` create a duplicate. Mint IDs through `plane push`, or create remotely and then `plane pull`.

**Conflict tables on push:** the push conflict check compares remote and state hashes. Before overriding a reported conflict, confirm with MCP `workitem retrieve_by_identifier` (`fields=id,state,updated_at`) that the remote item has not changed since the last sync. Never override blind.

## Version traps

- `plane push --json` **implies `--force`**. Do not use for routine machine-readable writes.
- `plane push --force` skips prompts **including conflicts**; `--no-conflict-check` also bypasses remote changes. Don't use for convenience.
- `plane push --exit-code`: `0` no work items to push, `1` error, `2` work-item changes applied (or detected with dry-run). Schema/cycle/module/page-only changes may not yield `2`.
- `plane diff --exit-code` and `plane schema diff --exit-code`: `0` in sync, `1` error, `2` differences. Their scope is different.
- `plane push --resume` retries only work items that failed in the preceding push, after the error cause is fixed.
- `-c` on `push`/`pull` affects a rate counter, **not credentials**.
- Do not run concurrent pushes for one project: state writes are only locked inside a process.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/project-files
