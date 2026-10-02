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

Operators configure named discovery areas with locations belonging to their directory. Public search uses these areas rather than accepting arbitrary private venue identifiers.

A regional occurrence must currently permit disclosure of both city and country. Private, undisclosed, to-be-announced or review-required venue associations must not reveal regional membership. An unknown or inactive area returns no matches.

If configuration or current disclosure authority cannot be established safely, discovery reports temporary unavailability. It does not return an uncertain result with an apparently authoritative count.

Changes to public eligibility and location disclosure require fresh reads. Public discovery responses are not reusable shared-cache or conditional-304 representations. Public details also rebuild their current fields after a program is redacted.

## Contributor and publisher information

A community-reported listing identifies its contributor separately from its source publisher. Attribution does not establish organizer ownership or platform endorsement.

Available editing and publishing actions come from the server-provided capabilities. Follow the actions offered for the current event and session.

## API consumers

Public discovery accepts `areaId` alongside local `dateFrom` and `dateTo` filters. Its card payload includes `matchingSession` and a nullable `additionalSessionCount`. Regional matching-session fields contain only the currently permitted public city and country.

An unavailable response uses HTTP `503` and the machine code `discovery_unavailable`. Retry the search when the service is available rather than presenting old cached results.
