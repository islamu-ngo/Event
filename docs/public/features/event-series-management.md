# Event Series management

Event Series group related events. Anonymous list, detail, and top responses now expose only published public Series in the current tenant. Draft, private, deleted, and foreign Series are not public details.

Nested events and event counts follow the same public eligibility rules as event discovery. Draft, private, deleted, foreign, or otherwise ineligible events do not appear or inflate a Series' ranking. Top ranks eligible upcoming/ongoing and undated events; events ending exactly at the current instant are no longer upcoming. This also works on SQLite.

## Administration

Creating or deleting a Series requires active persisted administration of the current tenant. Authentication or an administrative claim alone is insufficient. Updates continue to use the configured Actor update policy; with Local authorization this requires tenant administration. This change does not grant Series actors new write permissions.

Authorized administrators can edit unpublished and private Series without first making them public. PATCH still requires the current revision in a strong quoted `If-Match` header. Stale or simultaneous conflicting writes return `409`; reload the revision and review changes before retrying. Missing, deleted, or foreign update targets fail closed.

Omitted PATCH groups preserve existing values. Nullable fields can be explicitly cleared or replaced. Featured images must reference active public safe-raster metadata in the current tenant. Denied, invalid, and stale writes do not partially update the Series. An unavailable external authorization provider prevents the write rather than falling back to Local policy.

Clients should continue using server-issued HAL links for action affordances. Route names and public request/response shapes are unchanged. Existing controller-produced validation and not-found responses remain ProblemDetails JSON.

## Upgrade

Deploy the updated application normally. No configuration, policy grant, database migration, or manual data conversion is required. Applications that relied on anonymously reading unpublished Series or counting hidden nested events must stop relying on that disclosure.
