# Accurate status, remaining work, blockers and management reports

**Read when:** "what is left?", "how are we doing?", "what should we work on next?", "what's blocked?", or a status/roadmap update is requested.

## Three distinct evidence layers

1. **Git and implementation:** code, test runs, CI/PR state (if inspected), release artifacts. This supports claims that behavior is implemented/verified.
2. **Local Plane YAML and state:** `work/`, `schema/`, `plane status`, and Git changes. This is a local snapshot plus a last-sync baseline; it is not a live, complete remote inventory.
3. **Plane remote:** check via relevant CLI comparison or safely reconcile from a remote pull/clone. `plane diff` compares items known in local sync state, **not** all remote-only work items. `plane schema diff` is about schema, not the total backlog.

State which layer your conclusion is based on. Timestamp/freshness matter in shared workspaces. If Plane is inaccessible, never report “all remaining tasks” as a verified remote fact.

## Read-only inspection

```bash
plane status                 # OFFLINE: local pending/synced summary
plane diff                   # remote comparison of tracked items, not exhaustive
plane schema diff            # remote schema comparison
plane validate --offline
```

When the question requires a **complete** remote inventory and local YAML might be incomplete, investigate with an isolated fresh `plane clone <workspace>/<KEY> --directory <new-path>` (where practical) or a reviewed pull, and compare to local work. `plane pull` **writes local YAML and sync state**; do not issue it as a supposedly read-only check. Keep the isolated snapshot separate from the tracked repo and avoid inadvertently editing the remote. After comparing, deduplicate by ID and meaning.

## Compute useful status

Report with grounded numbers only when inventory is complete enough:

- By state group: backlog, unstarted, started, completed, cancelled. Respect actual customized state names and group mappings.
- By priority (`urgent`, `high`, `medium`, `low`, `none`) and by module/cycle when available.
- Blockers: incomplete `blocked_by` dependencies, outstanding approvals, missing acceptance evidence, failing tests, or external decisions. Differentiate inferred from explicit blockers.
- Critical path: ready/unblocked work that unlocks another task or blocks a milestone.
- Change since last verified checkpoint: newly created/completed/reprioritized items, resolved/opened blockers, test progress. Do not manufacture historical deltas without a baseline.
- Delivery confidence: concrete observable risks, not invented percentages or story-point velocity.

When reporting a count, give its scope (project, file set, remote clone date) and exclude cancelled items from the remaining-work count unless the question calls for them.

## Quality of recommendations

Recommend the *next* task based on actual scope, blocker status, deadlines and impact. Explain the relevant dependency and verification criteria. If all high-priority work is blocked, identify the smallest unblock action instead of sending the agent to random low-value work. State assumptions when prioritization relies on incomplete product context.

## Suggested report structure (adapt to request)

1. **Snapshot:** project/workspace, as-of and evidence level (local only vs remote reconciled).
2. **Delivery:** achieved outcomes and tested or pending evidence.
3. **Backlog:** verified remaining counts and highest priority items (IDs), if available.
4. **Risks/blockers:** owner or decision required, and exact dependency.
5. **Next:** one recommended unblocked task and why.
6. **Sync:** whether Plane remote actually matches this summary.

No synthetic "progress %" without a defensible denominator. Separate the agent's recommendations from persisted tasks.

**Official:** https://developers.plane.so/dev-tools/plane-compose/sync-projects · https://developers.plane.so/dev-tools/plane-compose/reference
