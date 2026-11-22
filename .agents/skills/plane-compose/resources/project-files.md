# Plane project-as-code file contract

**Read when:** locating which YAML to edit, understanding the project file format, or deciding whether an operation is schema or work data.

## Project layout

```text
<project>/
├── plane.yaml               # workspace, project key, connection, template, API server
├── workspace.yaml           # workspace feature snapshot (auto-managed)
├── schema/
│   ├── features.yaml        # enable/disable project features
│   ├── types.yaml           # work item types/custom properties
│   ├── states.yaml          # status names and groups
│   ├── workflows.yaml       # transitions and approvals
│   ├── labels.yaml          # label definitions
│   ├── members.yaml         # users and project roles
│   ├── workitem_templates.yaml
│   └── page_templates.yaml
├── work/
│   ├── workitems.yaml       # or additional functional *.yaml files
│   ├── cycles.yaml
│   ├── modules.yaml
│   ├── milestones.yaml
│   └── pages.yaml
└── .plane/                  # git-ignored sync state/resume; never commit
    └── state.json
```

All `work/*.yaml` files except `cycles.yaml`, `modules.yaml`, `milestones.yaml` and `pages.yaml` are interpreted as work-item files, **in alphabetical order**. A casual backup named `work/workitems.backup.yaml` is ingested as additional tasks. Store backups outside `work/`, or use non-`.yaml` extensions. Prefer keeping the existing partition strategy; don't reorganize entire files during a small task.

## `plane.yaml`

```yaml
type: project
workspace: myteam
connection: main
project:
  key: APP
  name: Application
  description: Application delivery
  network: private
  timezone: Europe/Brussels
# Optional for a self-hosted server; don't set unless needed:
# api_url: https://plane.example.com
# schema:
#   extends: ../shared/schema
# template: default
```

`project.key` must be at most 10 uppercase letters/digits. Do not manually fabricate `project.uuid`; Plane writes it. `defaults.type` is read but ignored in v0.5.2; set `type:` on each work item if needed.

## Schema versus operational work

- `schema/features.yaml`: cycles, modules, pages, views, intakes, epics, work_item_types, workflows, time tracking and other project features. Disabled cycles/modules/pages are skipped during push even when work YAML exists.
- `schema/states.yaml`: state names with group `backlog`, `unstarted`, `started`, `completed` or `cancelled`; exactly one state may be default. Check actual names; don't presume a uniform workflow.
- `schema/labels.yaml`: known label names/colors.
- `schema/types.yaml`: custom types or properties on built-in Task/Bug/Feature/Chore/Improvement.
- `schema/workflows.yaml`: rules for transitions/approvals; requires the workflows feature.
- `work/*.yaml`: tasks, cycles, modules, milestones, and pages (as their feature support allows).

Don't silently add a novel label, type, status or workflow just to make a task validate. That is a schema change and needs separate consideration.

## Sync state (important)

`.plane/state.json` maps YAML names and stable item IDs to remote UUIDs, plus last-synced hashes. Local edits differ from previously synced state; remote changes can conflict. It is ignored in git, but its presence affects push behavior. `plane status` uses only local files/state and cannot prove remote freshness.

**Official:** https://developers.plane.so/dev-tools/plane-compose/project-files
