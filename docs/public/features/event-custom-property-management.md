# Event Custom Property Management

Event custom properties hold governed, event-local definitions and values.
Administrators manage definitions and values through authenticated endpoints;
clients should follow returned HAL links rather than infer permissions from roles.

- Definition pages are isolated by the resolved tenant. Successful definition
  changes refresh all affected pages, including pages that were previously empty.
  A failed database commit leaves existing definitions and cached pages unchanged.
- Creating a definition for an event outside the resolved tenant is rejected.
  Definitions with options can be created together without leaving partial rows.
- Single-value writes and multi-value replacement commit their query projections
  atomically. An empty replacement clears both the values and projections.
- PATCH requires the current `If-Match` concurrency stamp. Reload after a conflict.
  Validation uses ProblemDetails; quota failures remain HTTP 422.
- Permanent purge requires administrator access, a reason and no blocking values,
  projections, audit references or template provenance. A successful purge writes
  its audit atomically; a blocked purge leaves the definition unchanged.
- The MCP custom-property management context requires event-management authority,
  supports private events for their owner, and returns bounded descriptors rather
  than tenant or audit metadata. It does not grant outsiders management access.

Deploy by replacing all older API instances. No database migration, configuration
change or manual cache flush is required. If a request fails after the database
commit, reload before retrying: a later cache failure cannot undo a committed write.
