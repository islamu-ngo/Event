---
name: refactor-safely
description: "Load for behavior-preserving rename, move, decomposition, dead-code removal, dependency cleanup, record/class conversion, or immutable contract migration that needs caller, flow, test, and impact analysis; not for feature work or one-line cleanup."
type: workflow
enforcement: suggest
priority: high
---
<!-- ABOUTME: Knowledge-graph workflow for behavior-preserving structural refactoring. -->
<!-- ABOUTME: Requires preview, impact, affected-flow, and post-change verification. -->

## Resources
- [Record contracts](../../../docs/internal/RECORD_CONTRACTS.md) — load for record/class conversion, immutable collections/results, semantic values, generated contracts, and focused ratchets.
- [Architecture](../../../docs/internal/ARCHITECTURE.md) — load when the refactor crosses projects or layers.

## Rules

- Preserve observable behavior before changing structure. Lock the exact consumer seam with a failing regression test; do not weaken tests or retain compatibility shims to make the migration easier.
- Start with the knowledge graph: minimal context, impact radius, affected flows, callers, and tests. Escalate detail only when the bounded result cannot resolve ownership.
- Use LSP rename for symbols when available. Preview every rename/move and keep generated artifacts under their owning generator.
- A record/class conversion must classify identity, construction, equality, collection ownership, serialization, PATCH presence, HAL behavior, framework mutation, and diagnostic privacy.
- Change the innermost owning contract first and migrate every caller outward. Do not create duplicate DTOs, transitional aliases, public setters, or legacy generated clients.
- Generated C# contracts change through `eng/tools/Explore.GeneratedContracts` and MSBuild, never by editing `EventApiClient.g.cs`.
- Stop and re-baseline when impact evidence reveals feature behavior rather than a structural transformation.
- **The Expand–Contract Protocol for Wide Refactors**:
  When a mechanical refactor (e.g. renaming a database column, converting an aggregate identity to UUIDv7, modifying a shared CQS request/result shape) has a blast radius too wide for a single atomic PR:
  1. **Phase 1 (Expand)**: Introduce the new model, property, or handler method alongside the existing one without breaking or modifying old call sites. Ensure build and tests stay green.
  2. **Phase 2 (Migrate in Batches)**: Migrate callers and tests in bounded slices (per feature directory or layer), keeping CI green at every batch because the old form remains functional.
  3. **Phase 3 (Contract)**: Delete the obsolete form, remove deprecated fields, and clean up transitional shims once zero callers remain.

## Workflow

1. Use `get_minimal_context`, `get_impact_radius_tool`, and `get_affected_flows_tool`; query callers and tests for each changed symbol.
2. Add a focused failing test for the behavior the old structure currently supplies.
3. Preview the rename/move or write the exact record eligibility classification before editing.
4. Apply the smallest structural change and migrate all callers without unrelated cleanup.
5. Run `detect_changes_tool`, affected tests, architecture ratchets, and one real consumer surface.

## Verification

- For record migrations, run the focused commands in [RECORD_CONTRACTS.md](../../../docs/internal/RECORD_CONTRACTS.md#focused-verification).
- Run LSP diagnostics on every changed source file.
- Run the changed domain's tests and project build once, then inspect `detect_changes_tool` for unexpected flows.
