# Event aspect management

Islamic and Tech aspects remain optional event characteristics. Existing event-management links and API routes continue to work; no configuration, database migration or generated-client update is required.

## Public and managed reads

`GET /api/event/{id}/aspects/islamic` and `/aspects/tech` return200 only when the event is publicly eligible and that aspect exists. Missing aspects, missing events and events unavailable to the public return **404 with ProblemDetails**. This corrects the previous unintended204 empty response to match the documented API contract. The response does not distinguish a private event from an unavailable one.

Authorized editors use `/api/event/{id}/management-aspects/islamic` or `/management-aspects/tech` to read non-public aspects. These routes still require view-management authority; an authorized read with no configured aspect retains its existing204 empty response. Being able to read an event publicly does not grant editing rights. Clients should follow the server's management links rather than infer permission from roles or claims.

## Writes and caching

POST creates an aspect and returns201, or409 if it already exists. PATCH updates an existing aspect using the current grouped fields, retains omitted values and returns404 when the aspect is missing. Invalid fields/groups return400. Clearing a nullable PATCH value uses the existing `hasValue`/`value` wrapper, not an unwrapped JSON null. DELETE returns204, including repeated deletion when the caller still has authority over the parent event. All writes require permission to update that event; deleting an aspect does not delete the event.

Create and update retain their existing event-detail and tenant-event-list cache invalidation. Delete's existing cache behavior is unchanged; this release does not promise immediate refresh of cached public responses. Request cancellation propagates at supported boundaries, but is not a guarantee that an already-started database write is rolled back.

## AI proposals are not activated

This release does not enable execution of Islamic or Tech aspect AI proposals. Confirmation remains create-event-draft-only. Aspect tool payloads, their confirmation requirements and the existing Upsert-to-Update mapping are unchanged; a proposal is not evidence that an aspect was changed or that its concurrency stamp was enforced.
