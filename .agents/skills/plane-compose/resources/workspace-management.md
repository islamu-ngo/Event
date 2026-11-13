# Workspace-level Plane Compose management

**Read when:** touching workspace types/properties, release definitions, initiative labels, relations, project states, webhooks, automations, scripts, templates or customer configuration.

## Keep workspace and project configuration separate

A `plane ws clone` folder has `plane.yaml` with `type: workspace`, `schema/` and `work/`. Workspace schema may contain `features.yaml`, `members.yaml`, `workitemtypes.yaml`, `project.yaml`, `initiatives.yaml`, `releases.yaml`, `relations.yaml`, `webhooks.yaml`, `automations.yaml`, `scripts.yaml`, `templates_workitem.yaml`, `templates_project.yaml`, `templates_page.yaml` and (when enabled) `customers.yaml`. Workspace work files include `releases.yaml` and optional wiki `pages.yaml`. Files may be absent when a feature is off or fetch fails.

The workspace `.plane/secrets.json` stores webhook secrets locally; never commit it or echo it. Workspace members and the type hierarchy can be pulled but **aren't pushed** in v0.5.2.

## Common workflow

```bash
# Explicit connection is required for ws clone
plane ws clone myteam -c main
cd myteam
plane ws diff
# Only when local overwrite has been reviewed:
plane ws pull --merge
plane ws push --dry-run
# Approval required for workspace-wide mutation:
plane ws push
```

`ws pull --merge` preserves local-only additions and applies remote changes **without asking**, so do not treat it as read-only. Default `ws pull` asks before overwriting differences; `--force` lets Plane win wholly. `ws push` is additive by default and writes server-assigned IDs into YAML.

`--skip` accepts `releases`, `pages`, `webhooks`, `automations`, `scripts` despite v0.5.2 help listing only releases. Use `plane ws diff --skip ...` to narrow review.

## Operations and risk

| Operation | Policy |
| --- | --- |
| `ws diff` | Read-only and suitable for inspection. |
| `ws pull` / `ws pull --merge` | Local overwrite/merge; checkpoint and review before running. |
| `ws push --dry-run` | Preview, safe to inspect. |
| `ws push` | Workspace-wide external change; explicit approval for the exact scope. |
| `ws push --prune` | **Destructive:** deletes remote-only webhooks, automations, customer properties, templates and type properties. Explicit high-risk authorization after `--dry-run --prune`. |
| `ws state remove` / `ws state reset` | Destructive local sync-state mutation; avoid as default troubleshooting. |
| `ws upgrade --dry-run` | **Not** a read-only preview unless also `--skip-pull`. |

`ws template <name> [path]` saves a sanitized reusable template: IDs, `.plane/`, secrets and `plane.yaml` are excluded. Check contents before publication; other configuration details may still be sensitive.

## Scope discipline

Changes to webhooks, automations and shared types can affect multiple projects. Don't create them as a side effect of fixing one feature. Ask for authorization, plan impact, inspect the preview, and report cross-project behavior explicitly.

**Official:** https://developers.plane.so/dev-tools/plane-compose/workspace
