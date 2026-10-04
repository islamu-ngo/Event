---
description: One program with relevant sessions, public regional discovery, and source attribution.
---

# Publication & Sessions

Create an event for a real program, then add sessions for its dates and locations. A session can be a meeting, a stop on a tour, or another scheduled part of that program. Adding a date or city does not require another event listing.

## What attendees see

Discovery selects a session that matches the requested dates and area. If a program began in April but has a matching June session, its discovery card presents that June occurrence. The full program schedule remains available on the event.

Dates use the event timezone. A finite session ending exactly at midnight does not occupy the following day. An explicitly open-ended session remains ongoing until it is closed; an unspecified end is not automatically an indefinite session.

An additional-session count describes other currently eligible matches. If the count is unknown, the interface does not replace it with the program's total session count.

## Regional visibility

Featured highlights events independently. Upcoming lists the next relevant published occurrences chronologically, including featured events: a carousel slide is not proof that an attendee has seen a deadline. Each section still shows a listing identity only once.

Recently Added prefers eligible listings absent from both Featured and Upcoming, then featured listings absent from Upcoming, then Upcoming listings when needed to fill its shelf. Candidates are selected newest-added first within each group and displayed in newest-added order. Other homepage shelves retain their earlier-section exclusions.

Small catalogs can therefore overlap across these sections without leaving artificial gaps. The layout renders only actual cards and adapts to their count; it never repeats a listing within a section or invents freshness to fill an empty position.

Upcoming includes ongoing and future occurrences, rather than events that
already ended today. Its date and actual start time remain visible on narrow
screens. Today and Tomorrow are relative to the event's configured timezone,
not the attendee's browser clock; ongoing occurrences are distinguished from
ones that have not started. Absolute dates remain visible when timezone
guidance is unavailable, and later-year occurrences include their year.

A short section can mean its eligible candidates are exhausted. If a section cannot finish within its bounded work or deadline, it reports failure separately from an empty result; other completed sections remain available.

Operators configure named discovery areas with locations belonging to their directory. Public search uses these areas rather than accepting arbitrary private venue identifiers.

A regional occurrence must currently permit disclosure of both city and country. Private, undisclosed, to-be-announced or review-required venue associations must not reveal regional membership. An unknown or inactive area returns no matches.

If configuration or current disclosure authority cannot be established safely, discovery reports temporary unavailability. It does not return an uncertain result with an apparently authoritative count.

Changes to public eligibility and location disclosure require fresh reads. Public discovery responses are not reusable shared-cache or conditional-304 representations. Public details also rebuild their current fields after a program is redacted.

## Contributor and publisher information

A community-reported listing identifies its contributor separately from its source publisher. Attribution does not establish organizer ownership or platform endorsement.

Available editing and publishing actions come from the server-provided capabilities. Follow the actions offered for the current event and session.

## Listing corrections

Related listings can share a discovery identity without merging their original
records. Registration, tickets, sessions, payments and organizer obligations stay
with the event where they were created. The event page never redirects those
actions to another listing.

When the related listing is independently public, the identity panel offers a
link to it. Private or unavailable targets are not identified. Authorized
organizers can inspect bounded public candidate suggestions and use the offered
correction route. A matching title is not proof that two events are one offering.

Following that link opens the related listing with its own public address and
actions. It does not move commitments from the listing you left. Reloading after
a loading failure retains the original public address, even if other navigation
state changed in the meantime.
If you return to the previous listing while another one is still loading, the
late response cannot replace the returned listing's actions or program.
An unfinished cancellation, archive or moderation confirmation does not apply to
a different listing opened meanwhile. Open a new confirmation on that listing.

Reviewers with the required current authority can confirm the same offering,
record a different offering, or reverse a relationship when the server offers
that action. If another decision changes the revision, the form retains input
but requires a reload and renewed confirmation. Recording a different offering
does not penalize either listing.

A saved decision confirms the correction result only. It does not mean a
publisher notification was delivered.
Current active publishers receive corrections through the durable in-app
notification pipeline. Background retries may delay delivery; operators should
use the existing outbox and notification status controls to investigate failed
or dead-lettered delivery. Notifications identify only the publisher's own
event, not a private counterpart.

Use the event Team's **Discovery Reviewer** preset to assign an independent
reviewer on every affected record. Assignment requires the same-event authority
to delegate all of that preset's permissions. Event owners have the delegation
permissions, but ownership or contribution to either record still prevents them
from reviewing that relationship themselves. The preset includes event update
authority needed for management access, together with review and reversal.
Existing managers receive no new review permission, and upgrading does not
automatically assign anyone to the reviewer preset.

For Development browser testing, the opt-in `local-agent` profile includes two
public **Agent discovery duplicate workshop** listings owned by the synthetic
organizer. Assign a different person through each listing's Team before testing
review or reversal. The fixture does not assign reviewers, restore revoked
ownership, or change unrelated listings.

## API consumers

Public event browsing uses forward continuation. Start with the desired filters
and `pageSize` (1 through 100), then follow the returned HAL `next` link with those
same filters. Public `pageNumber`, random pages and total-page counts are removed.
`snapshotCount` describes only the captured bounded membership; `truncated`
means the capture stopped before proving that all matches were examined.

The default traversal lasts at most 15 minutes and retains at most 1000 distinct
listings. Current eligibility can shorten that lifetime. Rankings are frozen for
that traversal, while each batch checks current visibility and matching sessions.
Registration and other attendee actions still address the original listing.

HTTP `400` with `discovery_cursor_invalid` means the continuation cannot be used;
`410` with `discovery_cursor_expired` means it expired; `409` with
`discovery_restart_required` means current authority or membership changed.
Keep the attendee's filters and offer an explicit new search. Replace the old
results when they choose it; never append a fresh search to an older traversal.

An event detail read can also lose authority while loading. The page offers
**Reload event** when information changes or becomes temporarily unavailable;
it does not describe these failures as a removed event or show rejected details.
Reloading is an explicit action. Hidden or missing events still show not-found.

### Operating bounded browsing

Apply the provider-specific `EventDiscoveryTraversal` migration before running
the updated API and generated consumers, then allow normal migration bootstrap to
finish its bounded ordering-key backfill before readiness. This includes
suppressed listings; it changes internal rank keys, not attendee commitments.
Title order uses one invariant case-folded contract across database engines,
with original-source identity ties. Replicas must use the existing shared
Data Protection key authority and application identity. A missing key, unavailable
authority or saturated snapshot store fails closed; do not substitute cached cards.

Keep Quartz and `event-discovery-snapshot-purge` running to reclaim expired
membership. The job runs once per minute after its initial delay, selecting at
most five tenants and deleting at most ten expired snapshots per tenant per pass.
Expiry is immediate even if cleanup stops, but expired rows still consume storage.
Restore cleanup when capacity is exhausted; do not increase bounds to bypass it.
Each tenant defaults to 200 live snapshots and 400000 physical membership rows.
Identical current searches can reuse one snapshot. Empty captures are bounded too.

The tenant governance settings under `event_discovery.*` offer finite downward
tuning of membership, lifetime, live snapshots, physical rows, examined rows and
source seeks. They introduce no new environment variable or deployment service.

For PostgreSQL, use the normal application migration bootstrap, not only
`dotnet ef database update`: it also installs the bounded retention function.
The runtime login needs `event_discovery_maintenance_runtime` membership with
effective inheritance. Keep its existing restricted role; never give it
maintenance-owner/migrator membership or BYPASSRLS. The function can enumerate
only bounded expired ownership keys, not foreign cards or membership.
Cleanup continues after physical directory deletion and does not recreate it.
PostgreSQL cleanup never advances expiry beyond database time, even if the
application clock is ahead; a future cutoff cannot remove live searches.
Missing operational grants stop cleanup, so treat purge errors as capacity
warnings rather than widening the runtime role.

Public discovery accepts `areaId` alongside local `dateFrom` and `dateTo` filters. Its card payload includes `matchingSession` and a nullable `additionalSessionCount`. Regional matching-session fields contain only the currently permitted public city and country.

Identity status and candidate responses use HAL and `Cache-Control: no-store`.
Follow the current resource's links; do not derive review authority from account
roles or candidate IDs. Review submissions include the current tenant identity
revision, and HTTP `409` requires reloading before another decision.

An unavailable response uses HTTP `503` and the machine code `discovery_unavailable`. Retry the search when the service is available rather than presenting old cached results.
