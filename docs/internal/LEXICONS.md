ABOUTME: Documents the exact vendored AT Protocol lexicons compiled and accepted by ISLAMU Event.
ABOUTME: Defines event/RSVP mapping, exhaustive description, strongRef ordering, and evolution boundaries.

# AT Protocol Lexicons

> **Audience:** Contributors | Integrators | AI agents
> **Status:** Implemented
> **Owner:** Federation
> **Last Verified:** 2026-07-25
> **Source Anchors:** `schemas/lexicons/`, `src/Explore.Infrastructure/Explore.Infrastructure.csproj`, `src/Explore.Infrastructure/Services/Federation/`, `src/Explore.Application/Features/Federation/Atproto/`, `src/Explore.Persistence/Repositories/AtprotoJetstreamRepository.cs`

## Purpose

The JSON files under `schemas/lexicons/` are the executable AT Protocol vocabulary. `Explore.Infrastructure.csproj` includes them as `LexiconFiles`, and the pinned CarpaNet source generator produces the typed JSON and DAG-CBOR bindings used by outbound mapping and Jetstream parsing. Runtime lexicon discovery and network auto-resolution are not enabled.

Unvendored proposal namespaces are not accepted federation collections. Only the checked-in lexicons below are executable.

## Executable Boundary

| NSID | Runtime use |
|---|---|
| `community.lexicon.calendar.event` | The only event collection accepted from Jetstream or emitted to an owner's PDS. |
| `community.lexicon.calendar.rsvp` | The only RSVP collection accepted from Jetstream. Outbound ISLAMU RSVP is stricter and emits only `#going`. |
| `com.atproto.repo.strongRef` | RSVP subject with required settled event `at://` URI and CID. |
| `community.lexicon.location.address` | Typed address member allowed by the event `locations` union after public-disclosure evaluation. |
| `community.lexicon.location.geo` | Typed geographic point allowed only after public-disclosure evaluation. |
| `community.lexicon.location.fsq` | Typed Foursquare location reference accepted by the event union. |
| `community.lexicon.location.hthree` | Typed H3 location reference accepted by the event union. |
| `com.atproto.server.getSession` | OAuth/session verification binding; not a federation record collection. |

The global Jetstream subscriber requests exactly the event and RSVP collections. Unknown/wildcard collections are never subscribed, and the parser quarantines an admitted envelope whose collection, `$type`, operation, DID, CID, record key, shape, or encoded size is invalid.

## Community Event Record

The vendored event lexicon requires `name` and `createdAt`. It also defines optional `description`, `startsAt`, `endsAt`, `mode`, `status`, `locations`, `uris`, and `rsvpExpected` fields. Known mode tokens are `#inperson`, `#virtual`, and `#hybrid`; known status tokens are `#planned`, `#scheduled`, `#rescheduled`, `#cancelled`, and `#postponed`.

ISLAMU uses one record for the complete public event graph:

- Native lexicon values are mapped by `AtprotoCalendarEventRecordMapper`.
- `AtprotoEventSourceFieldManifest` independently classifies every public source value as native, rendered in the single `description`, or excluded for a precise privacy/internal reason.
- `AtprotoEventDescriptionFormatter` deterministically renders all remaining eligible event, session, agenda, group, speaker, aspect, resolved-lookup, public-media, and public EAV values into that one description.
- Raw locations and location PII are never sources. Only the public disclosure evaluator's returned values may enter native location fields or the description.
- Any uncovered field, invalid projection, unsafe URI, privacy failure, invalid lexicon value, JSON size above 2,097,152 bytes, or DAG-CBOR size above 1,048,576 bytes prevents enqueue. No value is truncated or silently dropped.

`federation.atproto_event_validation_profile=community_lexicon` relaxes only which local business fields must be present to publish: title, tenant, owner, and status remain required. It does not relax validation of supplied values, authorization, moderation, privacy, complete source-field disposition, or final record validation.

For inbound records, `name` and `createdAt` are likewise the only required producer values. Accepted source JSON is retained completely in `AtprotoRecord.RecordJson`; a separate validated import plan maps compatible fields into one tenant-local `Event` and one implicit `EventSession`. This includes authoritative slug generation, UTC/IANA-aware scheduling, mode/status/RSVP mapping, the first safe source URI, and an optional CID-verified thumbnail. Producer extensions with no compatible local field remain in the normalized JSON. See [FEDERATION.md](FEDERATION.md#inbound-event-import-and-database-materialization) for the complete mapping and lifecycle.

## Community RSVP Record

The vendored RSVP lexicon requires `subject` and `status`. Its `strongRef` subject requires an event URI and CID; its vocabulary recognizes `#interested`, `#going`, and `#notgoing` for inbound interoperability.

The local outbound contract is intentionally narrower:

- Only a successfully committed, active `EventRegistrationIntent` maps to `community.lexicon.calendar.rsvp#going`.
- Organizer `ApprovalStatus` never changes that user-intent mapping.
- `#interested` and `#notgoing` have no local user-intent model and are rejected for outbound publication.
- RSVP enqueue waits for the locally owned event record to settle its exact URI/CID. The outbox stores both the event-record dependency and captured CID before a worker can claim it.
- Final cancellation deletes a known remote RSVP only when no active registration intent remains.
- Attendee profile/PII, registration answers, payment, moderation, audit, approval state, and local IDs never enter the RSVP payload.

## Ownership And Persistence

Lexicons define wire shapes, not mutation authority. Local event and registration lifecycle handlers are the only outbound authority and write immutable `PdsSyncOutbox` intents inside the local transaction. CarpaNet PDS I/O happens only after commit and under a renewable fenced worker claim.

Inbound records are globally authoritative by DID, collection, and record key with one current source version. `ImportAtprotoFederatedEventCommand` builds one validated plan per visible tenant; authoritative materialization, typed event projection, tenant presentation, tenant-local `Event`/`EventSession`, optional thumbnail `StorageObject`, tombstone/quarantine effects, and cursor advancement are atomic under the one leased consumer. Bounded PDS snapshot reconciliation uses the same plan factory and persistence path. Tenant discovery remains separately gated by the effective `federation.atproto_events_enabled` capability, and inbound imports never enqueue an outbound echo.

## Evolution Rules

1. Change a record shape only by updating its vendored JSON and the typed mapper/parser/validator tests in the same workstream.
2. Keep `LexiconFiles` explicit and hermetic; do not add runtime resolution or wildcard imports.
3. Treat constraint tightening, required-field additions, token removal, or field removal as compatibility-impacting changes that need an upstream/versioning decision before adoption.
4. An internal EAV field does not create a new AT Protocol schema. Eligible public EAV values continue through the exhaustive description contract until an approved vendored lexicon field exists.
5. Adding a third ingress collection requires an explicit product/architecture decision plus subscriber, parser, persistence, privacy, HAL, and recovery coverage. Do not widen `WantedCollections` speculatively.

## Related

- [FEDERATION.md](FEDERATION.md) — governance, DB-first delivery, Jetstream ownership, and roadmap boundary.
- [API.md](API.md#at-protocol-event-federation-contract) — typed discovery, safe source redirect, and absent raw mutation surface.
- [ADR-015](adr/ADR-015-atproto-event-federation-ownership.md) — authoritative ingress and lifecycle-owned egress decision.
- [OUTBOX_PATTERN.md](OUTBOX_PATTERN.md) — generic and PDS-specific transactional outbox boundaries.
