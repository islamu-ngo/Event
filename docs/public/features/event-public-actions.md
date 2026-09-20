# Event public actions

Reviewed public actions link visitors to an event's original source, external
event page, registration, optional questionnaire, livestream or organizer contact.
The available kinds depend on the event's participation mode. External
registration requires external-managed participation; information-only events do
not expose optional questionnaires.

Only Active actions on an eligible published public event appear in anonymous
reads. Private, draft, deleted, foreign-tenant and otherwise ineligible events do
not expose these destinations. Creating or updating an action puts it in pending
review, so a successful write does not make it immediately public.

Authorized event managers use `/api/events/{eventId}/public-actions` and its
`/{actionId}` resource. Updates require the current concurrency stamp in the body;
deletes require one strong quoted GUID stamp in `If-Match`. Invalid or stale
stamps return validation errors rather than overwriting another change. Use HAL
links to decide which management affordances to show.

Destinations must be absolute HTTPS URLs without embedded user credentials or URL
fragments. Public redirect links return 302 to the reviewed stored URL and are
not cached. Missing or ineligible actions return 404 and do not count as
engagements. Engagement metrics have bounded action-kind, surface and outcome
labels; they do not contain visitor identity, event IDs or destination URLs and
do not prove that the destination was visited.

The native application-operation migration changes no HTTP routes or payloads and
requires no configuration change, database migration or operator action.
