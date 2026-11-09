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
| `plane pull` | **Writes local schema/work YAML and sync state** from Plane. |
| `plane pull --merge` | Merge remote into existing items instead of wholesale replacement; still a local write. |
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

`plane pull` refreshes local schema and work. Without `--merge`, it can replace existing work-item files. With `--merge`, it preserves more local items, but it still **modifies local files**, and remote changes may alter item fields. Inspect Git changes, take a backup, review the affected files after pulling and revalidate.

**Special hazard:** a project with task shards such as `work/backend.yaml` and `work/frontend.yaml` may receive remote items in `work/workitems.yaml` on pull. That can leave the same item duplicated across files. Detect and reconcile repeated IDs before a push. Do not create backups ending in `.yaml` inside `work/`.

For read-only remote inventory, prefer a fresh isolated `plane clone` to a different directory if the context allows, then inspect without touching the active project.

## Version traps

- `plane push --json` **implies `--force`**. Do not use for routine machine-readable writes.
- `plane push --force` skips prompts **including conflicts**; `--no-conflict-check` also bypasses remote changes. Don't use for convenience.
- `plane push --exit-code`: `0` no work items to push, `1` error, `2` work-item changes applied (or detected with dry-run). Schema/cycle/module/page-only changes may not yield `2`.
- `plane diff --exit-code` and `plane schema diff --exit-code`: `0` in sync, `1` error, `2` differences. Their scope is different.
- `plane push --resume` retries only work items that failed in the preceding push, after the error cause is fixed.
- `-c` on `push`/`pull` affects a rate counter, **not credentials**.
- Do not run concurrent pushes for one project: state writes are only locked inside a process.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/project-files
