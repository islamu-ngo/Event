ABOUTME: Documents implemented AT Protocol authentication and governed event federation boundaries.
ABOUTME: Covers DB-first PDS delivery, exhaustive projection, Jetstream discovery, HAL, and roadmap protocols.

# Federation

> **Audience:** Contributors | Integrators | AI agents
> **Status:** Implemented AT Protocol integration; ActivityPub/PDS hosting remain roadmap
> **Owner:** API
> **Last Verified:** 2026-07-29
> **Source Anchors:** `src/Explore.API/Controllers/ActorController.cs`, `src/Explore.API/Controllers/ActorSubscriptionController.cs`, `src/Explore.API/Controllers/AtprotoSessionController.cs`, `src/Explore.API/Controllers/EventController.cs`, `src/Explore.API/BackgroundServices/PdsSyncWorker.cs`, `src/Explore.Blazor/Authentication/AtprotoAuthenticationHandler.cs`, `src/Explore.Infrastructure/Services/Federation/AtprotoJetstreamSubscriber.cs`, `src/Explore.Infrastructure/Services/Federation/AtprotoJetstreamRuntimeStore.cs`, `src/Explore.Infrastructure/Services/Federation/AtprotoThumbnailBlobGateway.cs`, `src/Explore.Application/Features/Federation/Atproto/Handlers/Commands/ImportAtprotoFederatedEventCommandHandler.cs`, `src/Explore.Application/Features/Federation/Atproto/Services/AtprotoFederatedEventImportPlanFactory.cs`, `src/Explore.Persistence/Repositories/ActorRepository.cs`, `src/Explore.Persistence/Repositories/ActorSubscriptionRepository.cs`, `src/Explore.Persistence/Repositories/AtprotoJetstreamRepository.cs`, `docs/LEXICONS.md`, `docs/OUTBOX_PATTERN.md`

## Status

AT Protocol OAuth authentication is implemented for accounts that are already linked to a platform user. Governed outbound event/RSVP publication, exact-collection Jetstream ingestion, tenant-gated discovery, safe source HAL, and administrator/user client controls are implemented. ActivityPub bridging and operating an ATProto PDS/AppView remain roadmap features.

### ✅ Implemented (Current Runtime + Foundation)
- **Domain entities**: federation models exist in `Explore.Domain`:
  - `Actor` (federated identity representation)
  - `AtprotoRecord` (ATProto record storage)
  - `PdsSyncOutbox` (outbound sync queue model)
  - `IndexedDid` (DID cache model)
  - `ActorKeyStore` (key storage model)
  - supporting enums: `ActorType`, `DidCustodyType`
- **Persistence**: federation tables are represented through `ExploreDbContext` DbSets, entity configurations, and repositories, including ATProto record and PDS sync outbox repositories. `IndexedDid`, `UserExternalLogin`, `ActorKeyStore`, and legacy `SyncState` are internal persistence only; none has a public CRUD controller, HAL resource, or generated client contract. The private fenced `AtprotoJetstreamConsumerState` workflow owns ingestion cursor advancement instead of generic `SyncState` writes.
- **OAuth authentication**: the BFF uses CarpaNet confidential-client OAuth with protected single-use state, PKCE, DPoP, a server-private bootstrap bridge, encrypted DID-keyed session persistence, and a short-lived first-party ES256 API session JWT. The browser receives only the HttpOnly BFF cookie and safe display state.
- **Linked-account boundary**: the verified DID, PDS, tenant, and existing `UserExternalLogin` must agree. ATProto login does not create or email-match platform users, and clients cannot directly create or mutate login/DID ownership rows, encrypted actor keys, or federation cursor state.
- **Safe public API**: `GET /api/event` returns a typed local-or-federated discovery collection. Federated items receive only a policy-produced `source` relation to `GET /api/event/federated/{atprotoRecordId}/source`; no raw `AtprotoRecord` read or mutation API exists.
- **Governance**: `federation.atproto_events_enabled` controls inbound tenant presentation/stream demand and eligible outbound enqueue. `federation.atproto_event_validation_profile` selects platform or community-lexicon publication requirements, subject to instance locks; the community profile relaxes only required local business fields. `federation.atproto_publish_my_events` remains self-scoped user consent.
- **Projection**: one typed community event record maps native lexicon fields and renders every other public event value—including all sessions, aspects, resolved lookups, and EAV values—into one deterministic description. Coverage, privacy, or exact-size failures prevent enqueue; values are never silently dropped or truncated.
- **Inbound ingestion and local import**: one leased CarpaNet Jetstream consumer accepts exactly the community event and RSVP collections from a fixed endpoint. An empty DID filter discovers all public publishers of those collections; a configured `AllowedDids` list restricts ingestion to curated publishers. Accepted event records keep one global authoritative DID/collection/record-key row and the complete source JSON, then an internal MediatR command creates or updates one tenant-local `Event` and one `EventSession` for each visible tenant. Authoritative state, typed projection, tenant presentation, local aggregates, tombstone/quarantine effects, and cursor advancement commit atomically.
- **Outbound delivery**: event lifecycle handlers atomically commit the local publication and immutable `PdsSyncOutbox` intent. A fenced worker rechecks capability, self-consent, linked session, source version, payload, and public-location privacy immediately before CarpaNet PDS I/O, then settles URI/CID and links the primary record back to the committed local event.
- **Client surfaces**: instance administrators manage defaults/locks, unlocked tenant administrators manage effective capability/profile, and users manage only their own publication consent. Federated cards and local delivery status use text plus color and render actions only from HAL.
- **Authorization fallback**: local fallback authorization treats actor records as read-only for authenticated users and denies ATProto record/indexed DID writes except for instance-admin bypass.

### Global Actor Profiles And Tenant-Local Subscriptions

`Actor`, its concrete subject, and exact ATProto identities are global. Public Actor list, ID, and DID reads return only active global profile data. They do not serialize tenant participation IDs, private User ownership, or tenant storage-object IDs. The tenant Actor collection starts from current-tenant discoverability and may apply only approved, visible, unsuspended public Organization or Group profile overrides.

Local discoverability requires an active global Actor plus either an approved visible unsuspended Organization/Group participation in the selected tenant or an eligible public federated Event in that tenant. Observation does not create participation or an Actor-presence row. `ActorSubscription` remains tenant-local: create, detail, list, and notification fanout use the same discoverability predicate, while unsubscribe retains access to its existing durable row if the target later becomes hidden.

The API computes a request-local, non-serialized discoverability marker for Actor HAL assembly. Organization and Group resources advertise `subscribe` and `subscription` only when that marker is true and the normal authorization pipeline also allows the operation. Global collections do not advertise tenant-local subscription actions; clients must use `_links` rather than infer them from Actor kind, participation, roles, or claims.

Blazor renders only `/actors/{actorId}`, backed by the authoritative global detail
read. No tenant-contextual Actor profile page is implemented. The separate exact
`GET /api/actor/by-tenant/{tenantId}/{id}` API lookup applies the same fail-closed
discoverability predicate as the tenant collection, can return approved public
participation overrides, and returns `404` for hidden or cross-tenant targets.
Consumers render subscription controls only from the returned detail HAL.

Organization legitimacy evidence is not global identity evidence. It is retained private `OrganizationTenant` review input in one tenant, attached through a server-bound PDF upload session and reviewed by tenant authority without changing the global Actor, global Organization, exact DID identity, or any other tenant participation.

### ATProto/PDS Account Email Ownership

ATProto OAuth login is implemented, while PDS account hosting is not. Identity lifecycle email remains PDS/account-authority owned:

- External PDS hosts own email confirmation, password reset, email update, account migration, and PDS security email for their accounts.
- Future ISLAMU-operated PDS cells also own those PDS credential emails for the accounts they host, even when ISLAMU operates the infrastructure or shares SMTP plumbing.
- ISLAMU Event must not send PDS credential-token emails through product `EmailDispatchOutbox`, `IEmailService`, RabbitMQ, the Quartz scheduler, or product unsubscribe flows.
- If ATProto account email is unavailable or unverified for product notification purposes, collect a separately verified app-level notification email or use in-app notifications.

### ⏳ Not Yet Implemented (Protocol Roadmap)
- **Public protocol endpoints** are still not implemented:
  - WebFinger (`/.well-known/webfinger`)
  - Actor profile endpoints (`/actors/{handle}`)
  - ActivityPub inbox/outbox
  - ATProto PDS server behavior
  - ATProto AppView indexing behavior
- **Bridge/gateway** translation (ActivityPub ↔ ATProto) is not implemented.
- **Cryptographic federation verification** (HTTP signatures / full protocol validation paths) is not implemented.
- **Public social collections** (followers/following/liked) are not exposed as protocol collections.

**Important**: ATProto login and ATProto Events governance are independent. A successful login never enables ingestion or publication, and enabling the administrator capability never substitutes for user publication consent.

## Protocol Overview

> **Note**: This section describes the **intended** federation model for ISLAMU Event.
> **Current Status**: ATProto OAuth, governed event/RSVP projection and delivery, exact-collection Jetstream ingestion, tenant-gated discovery, and client governance/status surfaces are implemented. Public ActivityPub endpoints and ATProto PDS/AppView server behavior are not implemented.

- **Server-to-Server (S2S)** (planned): Instances exchange activities via inbox/outbox.
- **HTTP Signatures** (planned): Cryptographic verification of federated messages.
- **WebFinger** (planned): Actor discovery via `/.well-known/webfinger`.
- **Collections** (planned): Followers, Following, Liked as ordered collections.

## Planned Architecture Philosophy

> **⚠️ Planned Feature**: The architecture diagrams below represent the **intended** public federation design.
> **Current Implementation**: Domain persistence, confidential ATProto OAuth, event/RSVP projection and DB-first delivery, governance, exact-collection ingestion, tenant discovery, and HAL-driven client surfaces exist. Public ActivityPub endpoints, ATProto PDS/AppView server behavior, and bridge/gateway behavior remain planned.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           ISLAMU Event Architecture                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────┐     ┌─────────────┐     ┌─────────────────────────────┐   │
│   │   Users     │────▶│    PDS      │────▶│    ATProto Network          │   │
│   │  (DIDs)     │     │  (Hosting)  │     │  (Relay/Firehose/AppView)   │   │
│   └─────────────┘     └─────────────┘     └─────────────────────────────┘   │
│         │                                              │                    │
│         │                                              │                    │
│         ▼                                              ▼                    │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                    ISLAMU Event AppView                              │   │
│   │  • Indexes community event and RSVP records                          │   │
│   │  • Provides search/discovery APIs                                    │   │
│   │  • Manages cultural/audience filtering                               │   │
│   │  • Hosts ActivityPub Gateway                                         │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
│                                        ▼                                    │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │               ActivityPub Gateway (Bridge)                           │   │
│   │  • Exposes ATProto events as ActivityPub Event objects              │   │
│   │  • Translates ActivityPub Follow → ATProto follow records           │   │
│   │  • Translates ActivityPub RSVP → ATProto participation records      │   │
│   │  • Would provide WebFinger, Actor endpoints, Inbox/Outbox (planned) │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                        │                                    │
│                                        ▼                                    │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                      Fediverse                                       │   │
│   │              (Mastodon, Mobilizon, Pleroma, etc.)                   │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Event Data Flow

The diagram below is a product-level illustration, not the record-shape contract. The implemented projection creates one community event record: native lexicon fields are mapped directly, and every remaining public event value—including every session, aspect, resolved lookup, and EAV value—is rendered into the same deterministic description. No companion session records are emitted, and any coverage, privacy, or size failure stops PDS enqueue instead of truncating data. The transactional outbox worker writes only after the local publication commits and uses a stable record key plus URI/CID reconciliation for retry safety.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Event Data Flow                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ATProto/ActivityPub Federation                    ISLAMU Event WebApp      │
│  ─────────────────────────────────                 ───────────────────      │
│                                                                             │
│  ┌─────────────────────────────┐                  ┌─────────────────────┐   │
│  │  Federated Event Record     │                  │  Full Event Detail  │   │
│  │  ─────────────────────────  │                  │  ─────────────────  │   │
│  │  • Title                    │                  │  • All event info   │   │
│  │  • Summary/Description      │    Link to       │  • Session 1        │   │
│  │  • Start Date (first)       │ ──────────────▶  │    - Time, Location │   │
│  │  • Location (primary)       │                  │    - Agenda         │   │
│  │  • Cover Image              │                  │    - Speakers       │   │
│  │  • URL → webapp             │                  │  • Session 2        │   │
│  │  • Basic audience info      │                  │    - Time, Location │   │
│  └─────────────────────────────┘                  │    - Agenda         │   │
│                                                    │  • Session N...     │   │
│  What Mastodon/Bluesky users see:                 │  • Registration     │   │
│  "🗓️ Community Iftar 2025                         │  • Comments         │   │
│   March 15-17, Amsterdam                          │  • Participants     │   │
│   Join us for 3 days of..."                       └─────────────────────┘   │
│   [View Event →]                                                            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Inbound Event Import And Database Materialization

Inbound federation is an internal import path, not a public create endpoint and not a second outbound publication path. The application deliberately keeps two representations:

1. **Authoritative protocol record** — `AtprotoRecord` owns the global DID, collection, record key, CID, AT URI, source cursor/version, tombstone state, complete accepted `RecordJson`, and `RecordHash`. `AtprotoEventProjection` stores bounded fields used by discovery.
2. **Tenant-local application aggregates** — one normal `Event` and one implicit `EventSession` are created or synchronized for every tenant whose `AtprotoRecordTenantPresentation` is visible. These rows participate in the normal event/session data model and are linked back through `Event.AtprotoRecordId`.

This separation is the no-data-loss boundary. Semantically compatible fields become first-class local values; producer-specific, unsupported, or future fields remain available in the normalized JSON rather than being forced into unrelated columns. Structurally invalid records are not imported: they are quarantined with bounded hashes and reason codes.

### Runtime Flow

```text
Jetstream commit or bounded PDS snapshot
    -> generated community-calendar parsing and semantic validation
    -> authoritative AtprotoRecord + typed AtprotoEventProjection
    -> AtprotoJetstreamRuntimeStore
    -> MediatR: ImportAtprotoFederatedEventCommand
    -> AtprotoFederatedEventImportPlanFactory
       -> manually instantiated AtprotoFederatedEventImportInputValidator
       -> one plan per visible tenant
    -> optional thumbnail fetch/stage through the verified DID/PDS boundary
    -> AtprotoJetstreamRepository fenced transaction
       -> primary record/projection/presentation
       -> global Actor + tenant Event + EventSession (+ StorageObject when valid)
       -> cursor or complete-snapshot settlement
```

`ReconcileAtprotoPdsSnapshotsCommandHandler` uses the same import-plan factory, thumbnail boundary, and persistence transaction. Recovery therefore cannot produce a different local shape from live Jetstream ingestion.

### Import Validation

The local import validator mirrors the community lexicon's minimal contract while protecting the ISLAMU data model:

- `name` and `createdAt` are the only required producer values; `name` must be non-empty and at most 200 characters.
- Optional `description` is bounded to 4,000 characters.
- Optional source URIs must pass the hardened external-URI policy.
- Optional mode and status tokens must be members of the supported community calendar sets.
- When both are present, `endsAt` must be later than `startsAt`.
- Only visible tenant presentations generate local import plans.

Validators are instantiated inside the Application path, following the repository's CQRS convention. Repositories receive validated plans and return/persist entities; they do not expose DTO-shaped import logic.

### Lexicon-To-Application Mapping

| Community calendar value | Authoritative/typed storage | Local application mapping |
| --- | --- | --- |
| DID + collection + record key + CID + source cursor | `AtprotoRecord` identity and source version | `Event.AtprotoRecordId`; `ProvenanceSource = "atproto"`; `ProvenanceExternalId` is the bounded AT URI |
| Complete accepted record | `AtprotoRecord.RecordJson` plus SHA-256 `RecordHash` | Remains the lossless source for producer extensions that have no compatible local field |
| `name` | `AtprotoEventProjection.Name` | `Event.Title` and `EventSession.Title` |
| `name`-derived identifier | N/A | On first import, `Event.Slug = SlugGenerator.FromTitle(name, "event")`; the implicit session uses `SlugGenerator.FromTitle($"{name}-session-1", "session")`. Later source updates preserve those stable slugs |
| `description` | `AtprotoEventProjection.Description` | Full value in `Event.Content`; a Unicode-safe first 150 runes in `Event.Description`; the implicit session does not duplicate it |
| `createdAt` | `AtprotoEventProjection.CreatedAt` | `Event.CreatedAt` and `EventSession.CreatedAt`, converted to UTC |
| `startsAt` / `endsAt` | Typed projection timestamps | `EventSession.StartTime`, `EndTime`, and `EndTimeType`; the parent event recalculates its schedule summary from the session |
| `timezone` | Complete value remains in `RecordJson` | A valid IANA zone is normalized into `Event.EventTimeZoneId`, `Event.Timezone`, and session local-time projections; absent or invalid values fall back to UTC |
| `mode` | Normalized projection token | `#virtual` -> Digital, `#hybrid` -> Hybrid, `#inperson` or absent -> Local |
| `status` | Normalized projection token | `#scheduled`, `#rescheduled`, or absent -> Published; `#cancelled` -> Cancelled; `#planned` / `#postponed` -> Draft, for both event and session |
| `rsvpExpected` | Typed nullable boolean | `true` maps to `EXTERNAL_MANAGED + REQUIRED`; false or absent maps to `INFORMATION_ONLY + NOT_APPLICABLE` |
| `uris[]` | All entries remain in `RecordJson`; first hardened external URI becomes `AtprotoEventProjection.SourceUrl` | A safe source URI becomes a reviewed typed stored public action; no URL field is written on `Event` |
| `locations[]` | Bounded human-readable `AtprotoEventProjection.LocationSummary`; complete structures remain in `RecordJson` | No synthetic local venue is created because location variants do not necessarily satisfy the local location/privacy model |
| `media[]` thumbnail | Blob metadata remains in `RecordJson`; a validated candidate carries DID, CID, MIME type, and declared size | Verified bytes become a public event-image `StorageObject`, linked through `Event.FeaturedImageId` |
| `theme`, `preferences`, `createdWith`, `bskyPostRef`, additional URIs/media, aspect ratios, and future producer extensions | Complete accepted values remain in `AtprotoRecord.RecordJson` | No unrelated relational field is invented; future mappings can be added without losing the original record |

Imported events reference one global external `Actor` keyed by the exact source DID, even when separate tenant Events materialize from the same publisher. Observation creates no tenant participation. Protocol record state remains authoritative in `AtprotoRecord`, while `AtprotoIdentity` is the exact-DID identity authority for the represented Actor.

### Thumbnail Blob Boundary

The importer looks for the first `media[]` entry whose `role` is `thumbnail`. The community record's authoritative shape uses `media[].content`; the parser also tolerates the generic `media[].blob` shape. A usable blob must contain:

- `$type: "blob"`;
- `ref.$link` with a structurally valid CID;
- an exact parameter-free MIME type from `image/jpeg`, `image/png`, `image/gif`, `image/webp`, or `image/avif`;
- a positive declared size within the configured bound.

The command handler fetches and stages the optional image before opening the EF transaction:

1. Resolve the record DID without cache and require exactly one bound `AtprotoPersonalDataServer` service.
2. Apply the hardened HTTPS/SSRF policy to the PDS origin.
3. Fetch `com.atproto.sync.getBlob` with the DID and CID.
4. Require successful status, exact allowlisted MIME type, exact declared/actual byte count, the configured maximum size, a constant-time SHA-256/CID match, and bounded whole-container structural validation through the exact end of a coherent JPEG, PNG, GIF87a/GIF89a, RIFF/WEBP, or AVIF file.
5. Write through the registered provider-neutral `IFileStorageProvider`.
6. Inside the fenced database transaction, recheck the staged provider result against the candidate's exact MIME type, size, and CID-bound SHA-256 checksum; derive the extension through the shared safe-raster policy; then create a tenant-owned public-image `StorageObject` and set `Event.FeaturedImageId`.

Missing, unknown, active-content, malformed-container, or MIME/container-mismatched optional media fails soft: the event still imports and the complete original media metadata remains in `RecordJson`, but no image is staged or linked. If the database apply is rejected or throws, staged but unconsumed bytes are deleted. Replacement marks the previous image for lifecycle deletion; a record tombstone clears the featured image and requests deletion of owned storage objects.

The gateway and PostgreSQL materialization boundary both consume the Application-owned `SafeRasterContentPolicy`; Infrastructure does not keep a second raster parser or SVG fallback. Persistence revalidates the staged MIME, declared and actual size, provider metadata, and CID-bound SHA-256 checksum before creating storage metadata. Rejecting an optional image never rewrites the accepted authoritative `RecordJson`, local Event/EventSession graph, or ingestion cursor settlement.

### Atomicity, Replay, And Tombstones

- The consumer lease token and monotonic fence are rechecked before commit.
- Primary record/projection changes, tenant presentation, `Event`, `EventSession`, optional `StorageObject`, quarantine effects, and cursor advancement share one transaction.
- Local identity is `(TenantId, AtprotoRecordId)`, so replay updates the existing aggregate instead of duplicating it.
- A healthy imported event is preserved when an older or non-authoritative replay does not permit replacement.
- A current update synchronizes mapped fields and revives previously tombstoned rows.
- A primary event tombstone soft-deletes the tenant-local event and all its sessions, clears `FeaturedImageId`, and requests storage deletion.
- Inbound imports never enqueue `PdsSyncOutbox` and never echo the source record back to a PDS.

After a successful mutation, the discovery cache is invalidated. Public discovery de-duplicates the authoritative projection against the tenant-local event linked by `AtprotoRecordId`, so clients see one event rather than a projection/import pair.

## CID
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          How CID Works                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. User creates event record                                               │
│     ┌─────────────────────────────────┐                                     │
│     │ {                               │                                     │
│     │   "$type": "community.lexicon.calendar.event",│                       │
│     │   "name": "Community Iftar",    │                                     │
│     │   "startsAt": "2025-03-15...",  │                                     │
│     │   ...                           │                                     │
│     │ }                               │                                     │
│     └─────────────────────────────────┘                                     │
│                    │                                                        │
│                    ▼                                                        │
│  2. PDS serializes to DAG-CBOR (deterministic binary format)               │
│                    │                                                        │
│                    ▼                                                        │
│  3. PDS computes SHA-256 hash → CID                                        │
│     ┌─────────────────────────────────────────────────────────────┐        │
│     │ bafyreigxyz123... (base32 encoded multihash)                │        │
│     └─────────────────────────────────────────────────────────────┘        │
│                    │                                                        │
│  4. CID is included in the repo commit (signed by user's key)              │
│                                                                             │
│  WHY THIS MATTERS:                                                          │
│  ─────────────────                                                          │
│  • Anyone can verify the record hasn't been tampered with                  │
│  • If content changes → CID changes → old references become invalid        │
│  • Enables "strong references" (URI + CID = exact version)                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Identity System Hybrid Approach

> **Current State**: Keycloak/OIDC and CarpaNet ATProto OAuth are both active BFF authentication choices when configured. ATProto accepts only an already-linked DID and never creates or email-matches a platform account. Custodial DID provisioning for Keycloak users in the diagram remains planned and is separate from login.

The current login paths converge on the existing platform user identifier so authorization and tenant isolation remain unchanged:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    Hybrid Auth Architecture                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│                      ┌─────────────────┐                                    │
│                      │   Login Page    │                                    │
│                      └────────┬────────┘                                    │
│                               │                                             │
│              ┌────────────────┼────────────────┐                            │
│              ▼                                 ▼                            │
│    ┌──────────────────┐              ┌──────────────────┐                  │
│    │ Login with Email │              │ Login with ATProto│                  │
│    │   (Keycloak)     │              │ (Bring your DID)  │                  │
│    └────────┬─────────┘              └────────┬─────────┘                  │
│             │                                  │                            │
│             ▼                                  ▼                            │
│    ┌──────────────────┐              ┌──────────────────┐                  │
│    │ Keycloak returns │              │ ATProto OAuth    │                  │
│    │ UUID + email     │              │ returns DID      │                  │
│    └────────┬─────────┘              └────────┬─────────┘                  │
│             │                                  │                            │
│             ▼                                  ▼                            │
│    ┌──────────────────┐              ┌──────────────────┐                  │
│    │ Create custodial │              │ Link existing    │                  │
│    │ DID for user     │              │ DID to account   │                  │
│    └────────┬─────────┘              └────────┬─────────┘                  │
│             │                                  │                            │
│             └────────────────┬─────────────────┘                            │
│                              ▼                                              │
│                    ┌──────────────────┐                                     │
│                    │  user + actor    │                                     │
│                    │  records created │                                     │
│                    └──────────────────┘                                     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Verified external subject classification

Inbound observation creates one global `ExternalUnclassified` Actor per exact DID and never creates tenant participation. During verified onboarding, explicit Organization or Group classification may promote that Actor in place. This preserves its identity and imported Event references while creating participation only in the onboarding tenant.

An explicit authoritative Actor target is not authorized by DID verification alone. The request carries a signed authoritative Actor ID and expected concurrency stamp, and the authenticated User must already be an active administrator of the matching approved OrganizationTenant or GroupTenant in the current tenant. Same-kind consolidation moves active operational references, writes immutable `ActorMerge` evidence using a bounded DID digest, and retires the external source. Cross-kind, User-owned, stale, suspended, deleted, unauthorized, or inferred matches fail closed.

### Public projection and outbound compensation

Inbound event projections use the current tenant presentation, record source version, record tombstone, Event visibility and lifecycle, active Actor, and exact active DID identity as one base gate. Public Draft, Cancelled, and Completed imported projections remain visible. Published imported projections are deduplicated to the local Event branch. Moderated, Archived, deleted, non-public, stale-presentation, tombstoned, Actor-suspended, and identity-ineligible projections are hidden. Exact source redirects use the same visible projection gate, so a hidden record cannot retain a public source redirect.

Outbound publication checks the exact active DID identity at planning and again immediately before delivery. An ineligible Create with no grounded remote mutation is skipped. Grounded `PdsSyncOutbox` work that becomes ineligible because of Actor or exact-DID suspension transactionally converts to a fenced Delete. Moderation reconciliation includes settled ownership and pending or processing Event mutations; exact-identity moderation limits unsettled work to the affected DID. Delete delivery requires the original tenant, user, DID, PDS session, source version, exact outbound-owned record, collection, record key, and, when present, CID to still match. Once those fences pass, a Delete may compensate for later Actor, identity, participation, Event, or payload ineligibility. Source Events remain selectable after soft deletion so privileged remote cleanup can finish. In-flight Create compensation waits beyond the predecessor retry or processing lease safety window. RSVP planning and delivery retain their existing behavior.

Public Event eligibility differs by record ownership. Outbound-owned records and local echoes remain local only when exact outbound ownership and current local eligibility both pass. Inbound records require the current visible tenant presentation, a non-tombstoned primary record, and the exact active, unsuspended, non-deleted DID identity owned by the Event Actor. The same central Event eligibility gate protects public actions, locations, program, agenda, days, sessions, session languages, aspects, sitemap, AI reference search, and Open Graph reads before disclosure, counting, or pagination.

Authorized management reads are separate from anonymous reads. Event days, session languages, Islamic aspects, and Tech aspects expose dedicated authenticated management routes that recheck `view-management` on the parent Event. The generated client, MCP management context, and Blazor management flow use those routes instead of weakening public eligibility. Management Event collections emit management detail/session affordances and omit public-only report links; the request-local management marker is not serialized.

Global moderation invalidates tagged Event-detail HybridCache entries and five output-cache tags: `event-discovery`, `public-home-discovery`, `list-data`, `detail-data`, and `seo-sitemap`. Output-cache eviction uses the process-local store. It prevents stale responses in the current API process but does not provide cross-replica cache consistency.

## Handling Nullable DID (`did_status`)

The key insight is that DID creation is **async**. Here's how to handle it:
```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    DID Status State Machine                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────┐     Background job      ┌─────────┐                           │
│  │ pending │ ──────────────────────▶ │ active  │                           │
│  └─────────┘     creates DID         └─────────┘                           │
│       │                                                                     │
│       │ DID creation fails                                                  │
│       ▼                                                                     │
│  ┌─────────┐     Retry succeeds      ┌─────────┐                           │
│  │ failed  │ ──────────────────────▶ │ active  │                           │
│  └─────────┘                         └─────────┘                           │
│                                                                             │
│  WHAT USERS CAN DO:                                                         │
│  ──────────────────                                                         │
│  pending → Browse, edit profile (read-heavy)                                │
│  failed  → Same as pending, show "retry" button                            │
│  active  → Full access: post events, comment, etc. (write access)          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Lexicon Boundary

[LEXICONS.md](LEXICONS.md) defines the exact vendored community event/RSVP, `strongRef`, location, and session lexicons compiled by CarpaNet. It also records the stricter product mapping: one exhaustive event description and outbound `#going` only. The lexicons define record vocabulary; lifecycle handlers, governance, privacy evaluation, `PdsSyncOutbox`, Jetstream persistence, and HAL remain the publication authorities.

## Outbox Boundary

[OUTBOX_PATTERN.md](OUTBOX_PATTERN.md) documents the generic transactional outbox and names `PdsSyncOutbox` as a specialized AT Protocol sync variant. This supports reliable outbound processing, but it is not equivalent to a public ActivityPub inbox/outbox server API.

## Operator And Product Guidance

- Treat `federation.atproto_events_enabled` as one governed capability for both authoritative Jetstream ingestion and eligible outbound PDS delivery. Keep it disabled until migrations, worker configuration, and operator recovery procedures are ready.
- The `community_lexicon` validation profile changes required local publication fields only; it never authorizes invalid supplied values, incomplete projections, private disclosure, or oversized records.
- Treat `auth.atproto_login_enabled` as authentication only; it does not enable event federation or user publication consent.
- Keep public-facing release notes precise: AT Protocol OAuth, event discovery, Jetstream ingestion, and lifecycle-owned PDS publication are implemented; ActivityPub interoperability and first-party PDS hosting remain roadmap work.
- Keep ATProto/PDS account email separate from ISLAMU product notification email. PDS SMTP is account-authority transport, not a general product-email provider.
- When adding another federation protocol, update this document with its exact routes, auth/trust boundaries, conformance tests, and rollback guidance without weakening the AT Protocol database-first publication invariant.

## Related

- [PROJECT.md](PROJECT.md) — product scope and maturity.
- [LEXICONS.md](LEXICONS.md) — NSID hierarchy and schema boundaries.
- [OUTBOX_PATTERN.md](OUTBOX_PATTERN.md) — transactional outbox and PDS sync variant.
- [AUTHORIZATION.md](AUTHORIZATION.md) — authorization provider model.
