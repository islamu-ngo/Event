# Bootstrap a Plane-managed project without overwriting existing work

**Read when:** no local project config exists; the user asks to adopt Plane Compose; a remote Plane project must be cloned; or a fresh git checkout has lost local sync state.

## Decision tree

| Situation | Action |
| --- | --- |
| Existing `plane.yaml` and `.plane/` present | Work in existing folder; inspect status, diff and git changes. |
| Project already exists remotely but is not local | Use `plane clone <workspace>/<KEY>` into a **new** folder. Do not `plane init` a duplicate. |
| New project explicitly requested and workspace chosen | Use `plane init <KEY> -w <slug>`; it scaffolds locally only. |
| YAML committed but `.plane/` ignored (fresh checkout) | Run `plane schema import` to rebuild schema IDs and reconcile work with a carefully reviewed pull before push. |
| Unsure if remote project exists | Investigate first. Never create automatically merely to unblock a coding task. |

## New project

```bash
plane init APP -w myteam -c main
cd app
plane schema validate
plane schema push --dry-run
# Only after explicit authorization to create the remote project:
plane schema push
```

`init` accepts a key of at most 10 uppercase alphanumeric characters (normalizes input) and creates a lowercased folder. The built-in template supplies initial schema files; it doesn't create the project remotely until `schema push` or `push` runs. `init` can ask before overwriting an existing directory: stop rather than overwrite.

## Adopt an existing remote project

```bash
plane clone myteam/APP -c main --directory ./plane-app
# Or use: plane clone APP --workspace myteam
cd plane-app
plane status
plane diff
```

`clone` accepts remote UUID, key or `workspace/KEY`; `--schema-only` avoids operational data, and `--no-properties` speeds huge imports but omits custom values. Avoid intentional partial imports if the user expects a full inventory.

## A fresh git checkout

`.plane/state.json` is intentionally not tracked in git. It records name-to-ID mappings and hashes. Do **not** compensate by inventing UUIDs or immediately pushing everything: you can create duplicates. Instead:

1. Verify `plane.yaml` targets the correct workspace/project/connection; inspect all work item IDs.
2. `plane schema import` (default writes state mappings, **not** YAML). Use `--merge` only if intentional; `--force` overwrites schema YAML.
3. Compare against Plane. Back up locally modified tracked YAML **outside `work/`** before considering `plane pull` (which writes to project files).
4. Review the incoming changes, particularly if work items are split into files. Reconcile without duplicating; validate and dry-run before push.

## External template at initialization

Use `-t ./templates/engineering` or a Git URL pointing to a path inside a repository, e.g. `https://github.com/org/repo/templates/engineering@v1.2.0`. A valid template has `schema/` and `work/`; only top-level `*.yaml` files are copied. Favor pinned tags for reproducibility; assess untrusted template content before any push.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/project-files
