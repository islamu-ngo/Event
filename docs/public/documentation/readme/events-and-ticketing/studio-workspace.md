---
description: Review a saved event and manage its current program through permitted Studio actions.
---

# Studio Event Workspace

After **Save draft** or a successful **Publish**, the event opens at
`/studio/events/{eventId}`. Overview shows its current status, participation
mode, managed program and available publication readiness. Saving does not
grant new permissions; follow the actions currently offered by the server.

## Details and program

**Details and appearance** opens `/studio/events/{eventId}/details`, using the
existing event editor. **Schedule** opens `/studio/events/{eventId}/schedule`
and shows the actual managed program, including draft or unscheduled items.
Return to Overview for server-provided publication readiness guidance. **Add session** opens
`/studio/events/{eventId}/sessions/create`. Each editable saved session opens
`/studio/events/{eventId}/sessions/{sessionId}/edit` and returns to Schedule
after saving.

Moving a saved session's start date shifts its end date by the same number of
days, preserving a same-day or overnight interval. You can still change the
end date explicitly when the duration itself needs to change.

Before saving an event, **Move session earlier** and **Move session later**
reorder prepared items within the same displayed day, or within Unscheduled.
The controls stop at each group's first and last item; they do not change dates.

If an imported proposal names a timezone unavailable on this installation,
the page retains its program and asks you to select a valid timezone before
editing or saving. That selection converts the retained session instants; it
does not invent a schedule for title-only items.

Use **All events** to return to the existing list with your active publisher
selection. The workspace does not introduce portfolio filters or switch your
publisher when you return.

## Available modules

Resources, registration, tickets, forms, orders, integrations and **Answer
analytics** appear when the event's current permissions make them applicable.
Answer analytics contains governed aggregates for registration answers, not
a general event performance report. Resource and module actions may require
additional permissions on their loaded collection or item.
Resource management follows those collection permissions even when the event
is not public; a public resources link alone does not grant management.

A missing action or an unavailable direct route is not a reason to use public
event data for management. Reload after permissions change. Publication
readiness and publication are part of Overview, not a separate placeholder
destination. No Team or Danger destination is invented when there is no
implemented management surface.

## Leaving and recovering a form

An edited creation, details or session form asks before an internal navigation
discards unsaved changes. Choose Stay to keep the form and its entered values.
A successful save clears this guard before entering the workspace.

If a confirmed save cannot navigate, the page still reports that it saved and
offers a workspace link. Use that link instead of creating the event or
session again. If the save response was lost, event creation keeps the local input
and disables another create; check managed events in another tab and reconcile
the authoritative outcome first.

The browser may also warn before an external unload. That warning is advisory:
browser restrictions, mobile termination or a closed process can prevent it.
Do not treat it as durable storage for an unsaved form.

## Browser addresses

The old standalone event-created page and old `/events/{id}/edit` and session
management addresses are removed without aliases. Update management bookmarks
and links to the Studio addresses above. Public event creation and public
session detail remain separate attendee-facing entry points.
