# Plan delivery with cycles, modules, milestones and release dependencies

**Read when:** creating a roadmap, sprint/cycle plan, feature module, milestone, launch checkpoint, or deciding how remaining work maps to delivery.

## Different organizational constructs

- **Work items & Epics:** individual outcomes (`Feature`, `Bug`, `Security`, etc.) and finite capability programs (`Epic`, when `epics: true` and `Epic` type exist).
- **Modules:** flexible cross-cutting containers used either for **feature areas** or **version / release tracking** (e.g., `v0.1.0`, `v1.0.0` when higher-tier/workspace Plane Releases are unused — Plane docs explicitly support Release modules alongside Feature modules). Module status is one of `backlog`, `planned`, `in-progress`, `paused`, `completed`, `cancelled`. Assigning an item to the active next-release module signals time urgency cleanly alongside `due_date`. *v0.5.2 limit:* `plane pull` exports `work_items:` in `work/modules.yaml`, but `plane push` only creates/updates module metadata (`ModuleDefinition` forbids extra fields and does not sync module work-item membership on push).
- **Cycles:** time windows; Plane infers status from start/end dates. Do not set a `status` expecting it to persist. *v0.5.2 limit:* like modules, `plane push` creates/updates cycle metadata only and does not push `work_items:` membership in v0.5.2; assign items to cycles/modules in Plane UI, MCP, or REST API.
- **Milestones:** project checkpoints with a `target_date` and referenced item IDs (`work_items: [APP-12, APP-15]`); requires the Milestones feature enabled in Plane. Unlike cycles/modules, `plane push` **does** synchronize milestone work-item membership in `v0.5.2`.
- **Releases:** primarily workspace-level `plane ws` data (`work/releases.yaml` and schema release definitions); don't invent project-level `releases.yaml` as if it were a standard project work file (`release_picker` custom property push is also blocked in v0.5.2).

## YAML examples

```yaml
# work/cycles.yaml — example dates; set real, approved dates
cycles:
  - name: Iteration 1
    description: Authentication foundation
    start_date: "2026-11-09"
    end_date: "2026-11-22"
```

```yaml
# work/modules.yaml
modules:
  - name: Identity
    description: User registration, login and token lifecycle
    status: planned
```

```yaml
# work/milestones.yaml — use real, already-known task IDs
milestones:
  - name: Authentication ready
    target_date: "2026-12-01"
    work_items: [APP-12, APP-15]
```

The dates and IDs above are illustrative, **not** defaults to inject into a project.

## Planning from actual remaining work

1. Inventory current items and evidence freshness; don't treat `plane status` as a complete remote backlog.
2. Identify true dependencies via `dependencies.blocked_by`; draw a critical path only from verified edges.
3. Group by delivered capability/module, not by files touched. Assign a cycle only when there is an actual iteration goal/date; otherwise leave unscheduled.
4. Choose milestones tied to demonstrable acceptance outcomes and known required tasks; don't invent a deadline from the current date.
5. Respect project feature flags: if `cycles: false`, `modules: false`, or `pages: false`, their files may be silently skipped on push.
6. For workspace release config, use `plane ws` and the relevant workspace resource, with separate approval.

## Verify the plan

Check for orphaned, duplicate or circular parent/dependency references; dates that contradict one another; unrealistic WIP; items blocked past a milestone; and tasks marked complete without test evidence. Use `plane validate` and `plane push --dry-run` for local work. Reassess and explicitly report assumptions when more data is required.

**Official:** https://developers.plane.so/dev-tools/plane-compose/project-files · https://developers.plane.so/dev-tools/plane-compose/workspace
