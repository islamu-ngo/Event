# Native Session Custom Properties

> **Audience:** Contributors and reviewers
> **Status:** Implemented
> **Source anchors:** `EventSessionCustomPropertyController`,
> `Features/EventSessionCustomProperties`, `EventSessionCustomPropertyProjectionUpdater`

## Closed operation boundary

The session-local definition/value cohort has nine native operations: definition
list/detail and session-value queries; create/update/delete/purge definition and
single/multiple-value commands. Requests implement only `IQuery<TResult>` or
`ICommand<TResult>`. The controller constructor injects nine closed handler ports
plus its HAL assembler. Application discovery supplies scoped authorization ->
performance -> business handlers; there is no sender, compatibility dispatch or
MCP consumer. The separately migrated session-projection administration operations
remain unchanged.

Commands retain tenant-update authorization metadata. The existing typed update
enricher remains registered and derives authority from the tenant-filtered
persisted definition rather than the request's `TenantId`. Create additionally
loads the requested session through the tenant-filtered repository and refuses
missing/foreign ownership before writing. Current tenant and user remain ambient
services, never body authority. The protected HTTP reads remain authenticated;
detail and value queries remain uncached and tenant-filtered. Mapping, read-only
DTO collection snapshots and exposure fields are not redefined here.

## Tenant-partitioned cache and transaction boundary

`SessionCustomPropertyCache` namespaces HybridCache list keys by trusted ambient
tenant, session and normalized page/size. Each page has tenant-list and
tenant/session-list tags. Create, metadata/options update and retirement evict
the persisted session tag after commit; purge evicts the persisted dependency
summary's tenant tag, including already-retired definitions. Purge conservatively
refreshes all session-definition lists within that tenant, not other tenants.
Unused synthetic detail invalidations are removed.

Post-commit invalidation uses `CancellationToken.None`. Validation failures,
blocked/missing purge and rollback do not evict; a cache failure after commit is
not swallowed and cannot undo the database mutation. This uses native HybridCache
tag semantics, not epochs, distributed locks or an expiry wait. Replace every old
API instance when deploying: new keys cannot repair requests served by old code.

Definition creation allocates UUIDv7 before forming option foreign keys. Parent,
options and selected default stay in the owning unit-of-work transaction.
Single-value and definition projection updater methods now save their tracked
changes before returning to that transaction. Multi-value replacement removes old
projection rows before replacing source values, then persists replacement
projections in the same transaction. This prevents restrictive foreign-key
failures and stale derived rows, including when clearing an optional collection.
The shared unit of work and event-property sibling are unchanged.

Purge retains dependency checks before and during repository deletion. Historical
values, projections, audit references and template provenance block hard purge.
Successful physical deletion and its audit commit together; injected commit
failure restores both and leaves cached pages valid. Retirement remains soft
deletion, not history erasure.

## HTTP machine contracts

Routes, verbs, response DTOs, concurrency headers and authorization attributes
are preserved. Collection creation HAL now uses the tenant-update capability
directly instead of an unregistered DTO-type mapping that threw at runtime.
Definition/value handler validation failures use `CommandFailurePolicy` and
ProblemDetails: HTTP 400, code `validation_failed`, error keys
`eventSessionCustomPropertyDefinition` / `eventSessionCustomPropertyValue`.
Quota failures retain HTTP 422 and quota fields; stale updates retain 409
`concurrent_update`; missing/foreign detail retains 404 `resource_not_found`.
Purge remains administrator-only. Missing/foreign ordinary delete retains 204
without mutating another tenant's definition.

## Evidence and scope

`NativeSessionCustomPropertyOperationTests` starts Red for nine native contracts
and discovery. `SessionCustomPropertyNativeTests` exercises the real protected
host, local persisted role grants, SQLite repositories and shared HybridCache.
It covers same-ID warm-tenant isolation, page variants for every definition write,
foreign ownership, forged claims and update facts, option creation, HAL/errors,
purge/audit refusal and rollback, signal-coordinated commit visibility,
post-commit cancellation, value constraints and durable projection replacement.
The old session purge mediator stub is replaced by real anonymous/member/admin
HTTP checks. Related session-projection administration tests remain in the gate.

This is not a provider-matrix, PostgreSQL locking, Redis cross-process coherence,
stale in-flight fill, or commit/invalidation crash-window guarantee. Existing
repository methods without cancellation parameters remain unchanged; no new
end-to-end database cancellation guarantee is claimed.

Operator guide: [Session custom-property management](../../public/features/session-custom-property-management.md).
