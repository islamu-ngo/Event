# Reject reversed relative event-resource availability in persistence

- Owner: Domain and persistence maintainers.
- Activate when: Closing the [PR #50 CodeRabbit review finding](https://github.com/islamu-ngo/Event/pull/50) or before trusting direct persisted rows as valid availability values.
- Status: Valid post-publication review finding, not a claim that ordinary aggregate writes bypass domain validation.

## Problem

`EventResourceAvailability.Create` rejects an end offset at or before the start offset when both bounds use the same schedule anchor. `EventResourceConfiguration` enforces the order of absolute timestamps but has no corresponding same-anchor relative-order database check. A malformed direct row can therefore survive persistence and fail when the aggregate reconstructs its value object.

## Acceptance

- Define the same-anchor relative-order invariant in the canonical EF entity configuration while preserving the existing absolute-order and nullable-boundary semantics. A relative window with distinct anchors must continue to use schedule-aware resolution, not a false offset-only comparison.
- Prove a malformed direct insert/update is rejected at the persistence boundary and a valid same-anchor or different-anchor value round-trips. Include aggregate reconstruction and current-access behavior so a row cannot turn into a late runtime exception.
- Regenerate, never hand-edit, the affected PostgreSQL, SQLite, SQL Server and shared MySQL/MariaDB migration/snapshot artifacts. Decide whether an unapplied development migration can be regenerated or an already-applied history needs a generated corrective migration; validate real provider SQL and pending-model checks.
- Run focused domain/persistence tests, the selected five-provider behavior gate and the owning Release build. Keep original migrated data safe; do not disguise malformed legacy rows by relaxing the domain rule.
