# Plane Project Management Knowledge Ledger

> **Scope:** Operating the single `EVENT` Plane project as code with
> `plane-compose` (YAML truth, ID minting) and the Plane MCP server (module
> membership, comments, links, approved deletions).

## Durable Decisions

- One `EVENT` project. Surfaces are labels, capability programs are Epics,
  release scope is Version Modules (`v0.1.0`, `v1.0.0`), and `priority` means
  functional criticality. See
  [the ADR](../../../docs/internal/adr/ADR-plane-project-management-standardization.md)
  and the
  [taxonomy standard](../../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md).
- The 15 capability Epics (`EVENT-6850..6864`) and their UUIDs live in
  [`islamu-event-project.md`](../../../.agents/skills/plane-mcp/resources/islamu-event-project.md).
  Each Epic holds one `Delivered foundation: <Epic> (v0.1.0 rollup)` Feature
  that replaced the per-commit and per-surface `Done` stubs; Git history stays
  the commit-level record.
- Consolidation keeps one existing ID as the vertical Feature and folds sibling
  titles and descriptions into its acceptance criteria before the siblings are
  deleted. IDs cited anywhere in Git or the repository are never deleted; they
  move to `Replaced`.

## [2026-11-12 Europe/Brussels] — Hybrid plane-compose + MCP batch order

**Context:** Backlog standardization (`EVENT-6849`) had to create Epics, rewrite
1,255 items, assign modules and delete about 1,300 items without duplicating
or reverting anything.

**Observation:** `plane push` has no per-file filter and pushes every pending
`event/work/*.yaml`. A new item's `parent:` needs the parent's UUID, which only
exists after the parent is pushed. MCP-created items get an `EVENT-XXX` that
plane-compose has no state entry for.

**Resolution:** Order is fixed: (1) backup `event/`; (2) push Epics alone from a
staging file and read UUIDs from `event/.plane/state.json`; (3) rewrite existing
items in `workitems.yaml` and put only net-new items in a separate
`active-refactor-*.yaml`; (4) module membership through MCP
`module manage_workitems`; (5) deletions through MCP `workitem delete` in
checkpointed waves; (6) resync. Hold back unrelated tasks' active files by
moving them outside `work/` for the duration of each push.

**Why This Matters for Future Work:** Writing an MCP-minted `id:` into YAML makes
the next push create a duplicate, and pushing parents and children together
leaves children without parents.

**References:**
- `.agents/skills/plane-compose/resources/sync-protocol.md`
- `.agents/skills/plane-mcp/SKILL.md`

## [2026-11-12 Europe/Brussels] — Large-project pull and conflict check hang

**Context:** `plane-compose` v0.5.2 against `EVENT` with about 2,650 items.

**Symptom / Observation:** `plane pull --work-only` and the default
`plane push` conflict check both stopped after "Found project: ISLAMU Event"
and sat idle on an open HTTPS socket (3 s CPU in 22 min). Running two pulls at
once also exhausted the rate limit, and a pull that fails its fetch prints
`Work items: 0 → work/workitems.yaml` without rewriting the file.

**Root Cause:** The bulk work-item listing used by pull and the conflict check
does not return for this project size, and the client sets no request timeout.

**Resolution:** Verify remote drift with MCP instead: `workitem list` with PQL
`updatedAt >= "<last sync date>"` (fields `sequence_id,updated_at`), compare
each hit's `updated_at` with that item's `last_synced` in `state.json`, and
check any remaining hits field by field. Then push with
`--no-conflict-check` (not `--force`). Find remote-only items with PQL
`createdAt` windows and edit them through MCP, never by hand-writing their IDs
into YAML. Always use `--no-properties` on pull when no custom properties exist.

**Why This Matters for Future Work:** A hung pull looks like a slow pull. Check
process CPU time before waiting longer, and never run two plane-compose
commands at once.

**References:**
- `event/.plane/state.json` (`work_items.<id>.last_synced`)
- `.agents/skills/plane-compose/resources/sync-protocol.md`

## [2026-11-13 Europe/Brussels] — Resync local YAML after MCP deletions without pull

**Context:** 1,326 items were deleted remotely through MCP. `plane pull --force`
would normally rewrite `work/workitems.yaml`, but pull hangs at this project
size and `--force` needs explicit approval.

**Observation:** plane-compose only learns about remote deletions through pull.
Until then the deleted items stay in `workitems.yaml` and `state.json`, and
editing any of them makes the next push try to PATCH an item that no longer
exists.

**Resolution:** Back up `event/`, then drop each deleted item's YAML block and
its `id:EVENT-N` key in `state.json` (awk and jq over the wave ID list). Fold
active staging files whose items already have minted `id:`s into
`workitems.yaml`, unchanged. Accept the resync only when
`plane push --work-only --dry-run --no-conflict-check` shows 0 updates and
MCP `workitem count` equals the local tracked count plus the known remote-only
items.

**Why This Matters for Future Work:** Count checkpoints after every deletion
batch (`workitem delete` returns `null` whether or not anything was removed)
are the only proof that a wave finished, and they let an interrupted wave
resume without re-deleting anything.

**References:**
- `event/.plane/state.json` (`work_items`)
- `.agents/skills/plane-mcp/SKILL.md`

## [2026-11-12 Europe/Brussels] — Flow-style label lists split on commas

**Context:** Authoring an Epic with the `KMP (mobile, watch & TV)` label.

**Symptom / Observation:** `labels: [KMP (mobile, watch & TV), TUI]` failed
validation with `Unknown label 'KMP (mobile'` and `Unknown label 'watch & TV)'`.

**Resolution:** Use block lists with quoted names for any label containing a
comma:

```yaml
labels:
- 'KMP (mobile, watch & TV)'
- TUI
```

**References:**
- `event/schema/labels.yaml`

## [2026-11-12 Europe/Brussels] — Default type and rename quirks

**Context:** Phase 3 schema reconciliation for `EVENT-6849`.

**Observation:** `event/plane.yaml` shipped `defaults.type: Story`, a type the
project does not have, so untyped items failed. Renaming a label in
`schema/labels.yaml` leaves every work item still carrying the old name, and
the next push fails with `Unknown label` until those strings are normalized; the
normalization then shows as content changes on every affected item.

**Resolution:** `defaults.type: Feature`. Rename labels and normalize item
strings in the same change, then confirm the reported "conflicts" are only the
rename before accepting them.

**References:**
- `event/plane.yaml`
- `.agents/skills/plane-compose/resources/repo-integration.md`
