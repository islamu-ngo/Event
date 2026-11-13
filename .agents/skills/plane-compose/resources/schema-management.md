# Project schema: features, states, types, labels and workflows

**Read when:** a task requires new statuses, labels, work item types/custom properties, project workflows, templates, member roles or project metadata.

## Ownership distinction

- `schema/` is **project structure**. Changes can affect many items or team behavior, so they are *not* routine ticket updates.
- `work/` contains operational items; use [work-item-authoring](work-item-authoring.md) for everyday work.
- Workspace-level types, automation, webhooks, and release tags are managed with `plane ws`; see [workspace-management](workspace-management.md).

## Project schema files

| File | Role / caveat |
| --- | --- |
| `schema/features.yaml` | Toggle features. `cycles`, `modules` and `pages` also gate publishing their work files. `work_item_types` enables custom types; `workflows` enables workflows. |
| `schema/states.yaml` | Status name → group (`backlog`, `unstarted`, `started`, `completed`, `cancelled`). Exactly one default when specified. |
| `schema/labels.yaml` | Named project labels. Names in work items must match. |
| `schema/types.yaml` | Built-in type custom properties or new custom types; property types/requiredness must be supported. |
| `schema/workflows.yaml` | State transitions/approvals; requires `workflows: true` and defined states/types. |
| `schema/members.yaml` | Project members, optional `guest`/`member`/`admin` role. |
| `schema/workitem_templates.yaml`, `schema/page_templates.yaml` | Named templates, subject to instance feature availability. |

`types.yaml` property kinds in v0.5.2 include text, number, decimal, date, datetime, option, boolean, url, email, file, member_picker, relation, release_picker and formula. **`release_picker` is read but not pushed; `formula` cannot be pushed** in v0.5.2. Don't invent unsupported schema YAML. Plane's built-in types include Task, Bug, Feature, Chore, Improvement. Custom types need feature support.

## Safe schema workflow

```bash
plane schema validate
plane schema diff
plane schema push --dry-run
# Explicit authorization for structural changes:
plane schema push
```

`schema push` is additive by default. It creates/updates project metadata, feature flags, types/properties, states, labels, workflows and templates; it may create the remote project if missing. Without `--prune`, missing remote type properties are retained. Run a schema-specific preview before committing.

`plane schema import` (default) records remote IDs in `.plane/state.json` but doesn't modify schema YAML. With `--merge` it adds remote schema items to YAML; with `--force` it **overwrites** local schema files.

## Dangerous schema operations

**`plane schema sync` is destructive.** In v0.5.2 it deletes remote states, types and labels not defined locally, notwithstanding misleading old `--help` output. In-use/default states may refuse deletion. Workspace-level work-item-type modes can also skip some type edits/deletes. Run `schema sync --dry-run` only as a review step when explicit remote deletion is intended; require explicit authorization to execute. `schema push --prune` can delete type properties and their data.

Changing a state used by existing work items, a required custom property or an approval workflow can invalidate the current backlog. Check reachability and affected work items before publishing; preserve existing names unless a migration was requested.

**Official:** https://developers.plane.so/dev-tools/plane-compose/schema-and-templates · https://developers.plane.so/dev-tools/plane-compose/project-files
