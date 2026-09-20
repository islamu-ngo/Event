# Native Event Custom Properties

> **Audience:** Backend contributors
> **Source anchors:** `Explore.Application/Features/EventCustomProperties`,
> `EventCustomPropertyController`, `EventManagementMcpTools`

## Operation and authority boundary

The feature has three native queries (definition list, definition detail, values)
and six result commands (definition create, update, delete, purge, single value,
multiple values). Consumers inject closed `IQueryHandler<,>` and
`ICommandHandler<,>` ports. Application discovery supplies scoped authorization
and timing decorators; no feature-specific handler registration is needed.
The existing update enricher resolves tenant authority from the persisted
definition. Request-body fields never establish current tenant or user.

Definition creation checks the parent event through the tenant-filtered event
repository before writing. The definition receives its UUIDv7 before option
foreign keys are constructed. Manual validators, governance, quotas, concurrency
checks and trusted audit ownership remain in handlers.

## Cache and transaction boundary

Shared HybridCache definition-list keys include trusted ambient tenant, event,
normalized page and size. Entries carry tenant and tenant/event tags. Successful
create, update and delete invalidate the tenant/event tag after commit. Purge
invalidates the persisted dependency summary's tenant tag, including soft-deleted
definitions whose ordinary detail query cannot resolve their event. This broader
tenant-only purge invalidation never evicts another tenant's warm pages.
Detail and value queries are uncached.

Invalidation uses `CancellationToken.None` after the owning transaction commits.
Rejected writes and failed commits retain warm entries, including genuinely empty
owned-event pages. A successful create invalidates every such page. Inline value
and metadata projection changes are saved inside the handler-owned transaction.
Multiple-value replacement deletes source rows whose existing database cascade
removes their projections, then persists replacements in the same transaction.
The event-specific runtime tests verify this existing cascade; unlike the session
repair, no additional projection removal is needed here.
Dependency-free purge and its audit commit together; blocked purge writes no new
audit, and rollback preserves the definition and removes the attempted audit.

## HTTP and MCP contracts

HTTP routes, names, verbs, grouped PATCH bodies and response schemas stay fixed.
Collection HAL create permission uses tenant update, matching commands. Validation
uses RFC 7807 with `validation_failed` and the definition/value error key. Existing
quota 422, stale-stamp 409, missing-detail 404, and missing-delete 204 are retained.
Otherwise-valid JSON containing forged authority is rejected under `errors.body`;
removing only that field makes the same create body valid.

`get_event_custom_properties_context` uses the two native list/value ports behind
the existing management-detail/HAL edit gate. Owner access includes private
events; outsider and foreign targets remain denied. Descriptor pagination and
text limits remain enforced, and tenant/audit/provenance details are not promoted
into the MCP representation. Existing Day, Program and AgendaItem ports are
unchanged.

## Evidence and limits

`EventCustomPropertyNativeTests` exercises actual SQLite repositories, protected
DI ports, HTTP middleware, shared HybridCache, transaction fault injection and
MCP `tools/call`. SQL read observation distinguishes a true hit from a reload.
Commit coordination uses pre-subscribed signals with bounded timeouts, not sleeps.
Native declaration contracts and existing in-memory conversion/quota invariants
cover the Application boundary.

This cohort does not claim multi-provider, distributed Redis, in-flight stale-fill
or crash-between-commit-and-invalidation guarantees. A post-commit cache failure
does not roll back a committed write. Replace old API processes on deployment;
there is no schema, shared policy, registration or dependency change.

Operator guidance: [Event custom property management](../../public/features/event-custom-property-management.md).
