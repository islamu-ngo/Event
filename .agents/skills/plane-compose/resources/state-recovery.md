# Recovery for lost state, failed pushes and duplicate items

**Read when:** `.plane/state.json` disappeared, the local checkout is new, push creates duplicates, the API partially failed, or state commands are being considered.

## First: stop speculative writes

Inspect the target `plane.yaml`, current Git changes, `.plane/` status, existing work IDs, CLI version, and remote connectivity. **Do not run `plane state reset`** as a generic repair. It removes mappings and can cause existing tasks and schema objects to be created again.

## Symptoms and responsible actions

| Symptom | First response |
| --- | --- |
| Fresh checkout; no `.plane/` | `plane schema import` to map existing schema IDs; reconcile existing work before pushing. |
| Push wants to recreate known schema items | Check project/connection; default `plane schema import` to rebuild mappings. |
| Item edited without `id` creates duplicate | Locate actual Plane ID; restore stable identifier; reconcile duplicate carefully. Don't erase work without approval. |
| Some work items failed to push | Fix validation/API issue; use `plane push --resume` to retry only failed items. |
| Unknown label/state/type | Fix spelling or reconcile relevant schema; run `plane validate` and schema checks. |
| Local and remote edits conflict | See [conflict-resolution](conflict-resolution.md); do not force. |
| Remote deleted an item | Confirm intended deletion and authoritative record; don't immediately recreate without user intent. |
| A workspace `ws state` mapping is broken | Inspect `plane ws state show` and official docs; avoid resetting without approval. |

## Safe inspection

```bash
plane state show
plane state show --json
plane status
plane diff
plane validate --offline
```

`plane state show` is read-only; `plane state remove <path>`, `plane state clear-items` and `plane state reset` all mutate local state. `clear-items` preserves schema mappings but forgets work-item mappings. **These are not harmless maintenance commands.**

## Reconstruct missing state

1. Verify `workspace:`, `project.key`, selected connection and remote project really match the repo.
2. Inspect YAML IDs for collisions and check the actual remote items. Do not manufacture UUIDs or sequence IDs.
3. Run `plane schema import` (default: state mappings only). `--merge` adds missing remote schema entries locally; `--force` overwrites local schema YAML.
4. For work item tracking, reconcile through a guarded `plane pull` or isolated `plane clone`. A pull can overwrite/merge YAML; checkpoint first and inspect results, especially projects using multiple task shards.
5. Validate, diff, dry-run before any resumed write; confirm that the plan **updates** known items rather than recreating them.

## Duplicates

An `id` omitted from an item causes content-hash tracking. Editing its title changes its key; the next push may create a new remote work item. Preserve canonical ID written by Plane; reattach local work to the correct existing task. Review which remote duplicate should be archived/closed or deleted with human authorization—don't lose comment history or relations by silently deleting an item.

## Partial failures

Plane records failed work items in `.plane/resume.json`. Correct the cause, then `plane push --resume`. This is not a substitute for reading the original result: schema or other work sections may already have been applied. Report what succeeded versus failed explicitly.

## Debugging and throttling

```bash
plane rate stats
plane --debug diff
```

Debug logging writes to `~/.config/plane-compose/plane.log`; inspect locally and redact tokens before sharing. A rate limiter defaults to 50 requests/minute (`PLANE_RATE_LIMIT_PER_MINUTE`), but lowering the throttle won't fix invalid credentials or bad schema.

**Official:** https://developers.plane.so/dev-tools/plane-compose/reference · https://developers.plane.so/dev-tools/plane-compose/schema-and-templates
