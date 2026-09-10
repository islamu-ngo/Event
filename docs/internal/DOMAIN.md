ABOUTME: Documents the current domain model and persistence-enforced rules.
ABOUTME: Prioritizes non-inferable patterns (PII split tables, aspects, filters, and constraints).

# Domain Model

> **Audience:** Contributors | Architects | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-03
> **Source Anchors:** `src/Explore.Domain/`, `src/Explore.Persistence/ExploreDbContext.cs`, `tests/Event.Domain.UnitTests/`

This project stores entities directly under `Explore.Domain/` (not in a nested `Entities/` subfolder).

For Domain value semantics, entity-versus-record selection, and the scalar EF persistence boundary, see [RECORD_CONTRACTS.md](RECORD_CONTRACTS.md).

---

## Domain Layer Profile & Scale (2026-09-03 Checkpoint)

| Dimension | Measured Value | Architectural Context |
|---|---|---|
| **Production LOC** | **55,000 LOC** | Pure C# domain model, zero infrastructure dependencies |
| **Unit Test Coverage** | **125 test files / 25,100 LOC** | Fast, in-memory TUnit tests in `tests/Event.Domain.UnitTests` |
| **Code Hygiene** | **0 TODO / 0 FIXME / 0 HACK** | 0 `[Obsolete]`, 0 `NotImplementedException`, nullable enabled |
| **Header Convention** | **>95% ABOUTME coverage** | Two-line summary header on all files |
| **Lookup Entities** | Normalized `int` PKs | Stable integer IDs, uppercase codes, human labels |
| **Aggregate Entities** | `Guid` PKs (UUIDv7) | Time-ordered sequential UUIDs for optimal B-tree indexing |

---

## Architectural Rationale: Unified Domain & Persistence Model (Pragmatic DDD)

In theoretical Domain-Driven Design (DDD) literature, a common recommendation is strict physical separation between the "pure domain model" (POCO classes with private setters and encapsulated state) and the "persistence model" (database-specific entities decorated with ORM attributes, foreign keys, and navigation properties). In such architectures, every business entity exists twice (e.g., 200+ domain entities and 200+ persistence mirror entities), interconnected by hundreds of object-to-object mappers.

In the **ISLAMU Event** platform, we **intentionally and deliberately reject this dual-model duplication**. Domain entities in `Explore.Domain` serve as **both** the rich business model and the EF Core persistence entity. They feature EF-friendly navigation shapes, foreign key attributes (`[ForeignKey]`), and public settable properties, while simultaneously embedding rich domain behavior (such as `Event.Publish()`, `Cancel()`, `ApplyScheduleTimeZone()`, `RecalculateScheduleSummaryFromSessions()`, and delegation to `EventLifecycleRules`).

This is not an accidental compromise or lack of architectural discipline; it is an **intentional design decision** that delivers massive concrete benefits:

### 1. Elimination of Dual-Model Maintenance Burden
Maintaining separate domain and persistence entity hierarchies for 126 feature slices would require duplicating over 200 entity classes and maintaining 140+ mappers. This would introduce tens of thousands of lines of boilerplate code with zero business value, significantly increasing development friction and onboarding complexity.

### 2. Zero Object-to-Object Mapping Overhead and Zero Mapping Drift
Mapping between domain objects and persistence entities incurs either runtime reflection overhead (e.g., AutoMapper) or vast handwritten mapping code. More critically, dual-model architectures suffer from **mapping drift**: when a domain property is added or altered, developers frequently forget to update the persistence entity, mapper, or query projector, leading to subtle runtime bugs. With a single unified model, mapping drift between domain and persistence is impossible by design.

### 3. High-Performance EF Core LINQ Projections and Expression Trees
EF Core's relational query engine relies on C# Expression Trees (`IQueryable<T>`). When domain entities are the persistence entities, repositories and query specifications can compose native LINQ expressions, navigation joins, and selective `.Select()` projections directly against the domain model. In a strictly separated model, EF Core cannot translate expressions referencing domain classes that the DbContext does not map. Systems are then forced to either:
- Expose persistence entities through repositories (which destroys the separation anyway), or
- Fetch entire database rows/aggregates into memory before mapping to domain entities, destroying query performance and preventing efficient SQL projection.

### 4. Direct Integration with 339 EF Core Global Query Filters
The platform enforces multi-tenancy and soft-deletion centrally via **339 EF Core named global query filters** on `ExploreDbContext`. These filters rely on entities implementing domain interface markers (`ITenantEntity`, `ISoftDeletable`). Because domain entities are directly mapped by EF Core, every query automatically and transparently inherits tenant isolation and soft-delete scoping at the database level.

### 5. Efficient Change Tracking and Unit of Work Mechanics
EF Core's change tracker monitors property mutations on tracked entity instances. When domain methods mutate entity state (such as updating `EventStatusId`, recomputing schedule summaries, or modifying aspect records), EF Core detects only the modified properties and generates minimal, optimal SQL `UPDATE` statements. A detached domain model requires complex snapshot diffing or manual state synchronization back into the persistence entity before saving.

### 6. Pragmatic Rich Behavior (Not Anemic)
Unifying persistence and domain models does not make the model anemic. Core entities in `Explore.Domain` encapsulate validation, state transitions, and business rules:
- `Event.Publish()` and `Cancel()` execute domain lifecycle rules and protect invariant state.
- `Event.RecalculateScheduleSummaryFromSessions()` recomputes schedule bounds across child sessions.
- `Location.PromoteAddressToTenantApproved()` enforces address provenance and reuse invariants.
- Complex state transitions delegate to domain rule engines like `EventLifecycleRules` and `EventSessionLifecycleRules`.

### 7. Compile-Time Clean Architecture Preservation
The unification of domain and persistence entities does not violate Clean Architecture. `Explore.Domain.csproj` has **zero dependencies** on external packages, frameworks, or other solution projects (referencing only `Event.Wire.Contracts`). EF Core is designed so that POCO entities with minimal annotations compile purely as standard .NET classes. Compile-time assembly isolation guarantees that Domain cannot reference Persistence, Application, or API.

---

## Live AT Protocol Identity

Live AT Protocol identifiers enter Domain behavior as `AtprotoDid`. The value
validates generic DID syntax and the protocol length bound once, preserves exact
ordinal case, and emits its scalar only through `.Value` at persistence, provider,
or wire boundaries. Method support such as `plc` or `web` belongs to the adapter,
not the generic value. Diagnostics are redacted and never contain the raw DID.

`AtprotoIdentity` owns its scalar `Did` property through typed construction and
refresh. Privacy erasure invokes the aggregate transition, which writes the
internal `did:deleted:*` tombstone, clears handle/PDS/signing metadata, and marks
the identity inactive and deleted. Tombstones are persisted anti-resurrection
state and are never parsed or accepted as live DIDs.

## Legal-Identity Authority

Legal identity is split by responsibility:

- `TenantDirectoryOperatorIdentity` is the normalized tenant-owned directory
  authority stored in the canonical typed settings document;
- `InstanceOperatorIdentity` is immutable startup configuration for the
  general platform operator;
- organizer merchant identity comes from the event organizer actor and current
  provider recipient lineage;
- `PaidCheckoutGovernanceOptions` owns payment operations, not identity.

`TenantDirectoryOperatorReadinessEvaluator` evaluates the exact document for
`Activation`, `PublicDisclosure`, or `PaidCommerce` and returns stable blocker
codes without identity payloads. `PaidOrderAcceptanceSnapshot` stores immutable
structured evidence: acceptance-template identity/text, organizer actor,
tenant directory document/revision and normalized facts, instance operator,
provider recipient, policies, schedule, lines, and money. Historical snapshots
are never rewritten when any authority changes.

## Core Aggregates

1. Tenant and access scope:
   `Tenant`, `TenantUser`, `TenantUserRoleGrant`, `TenantSetting`, `TenantSettingsDocument`, `TenantNavigationLink`, `TenantInvitation`, `TenantLifecycleLog`
2. Identity and actor model:
   `User`, `Actor`, `ActorSubscription`, `Group`, `Organization`, `Role`, `Permission`, `RolePermission`, `PlatformUserRole`
3. Events, registration, and admission ticketing:
   `Event`, `EventParticipationConfiguration`, `EventPublicAction`, `EventSession`, `RegistrationOrder`, `RegistrationOrderLine`, `RegistrationParticipant`, `RegistrationTicketAssignment`, `EventRegistration`, `AdmissionTicket`, `AdmissionTicketCredential`, `AdmissionRecoveryCapability`, `EventTicketCatalogVersion`, `EventTicketType`, `EventCapacityPool`, `TicketTypeEntitlement`, `CapacityOversellPolicy`, `PlatformFeePolicy`, `PlatformFeeFixedCharge`, `PlatformContributionOption`, `PlatformContributionSetting`, `PromotionDefinition`, `PromotionCode`, `PromotionReservation`, `EventSessionSpeaker`, `EventSessionLanguage`, `EventSessionAgendaItem`, `Notification`, `NotificationFanoutRun` (see [ADMISSION_AND_REGISTRATION.md](ADMISSION_AND_REGISTRATION.md) for architecture & zero-knowledge credentialing)
4. Event reporting and moderation review:
   `EventReport`, `EventReportTarget`, `EventReportEvidence`, `EventReportCase`, `EventReportSignal`, `EventReportDecision`, `EventReportDecisionExecution`, `EventReportExternalLink`, `EventModerationRecord`, `ActorModerationRecord`, `AtprotoIdentityModerationRecord`
5. Privacy erasure saga & compliance:
   `PrivacyErasureIntent`, `PrivacyErasureSaga`, `PrivacyErasureProviderWork`, `PrivacyErasureReplayCheckpoint`, `PrivacyErasureCounter`
6. Web push & messaging outbox:
   `WebPushSubscription`, `WebPushDispatchOutbox`, `IncomingWebhookEffectOutbox`, `IncomingWebhookMessage`, `IntegrationSyncOutbox`, `EmailDispatchOutbox`
7. Managed provider & provisioning:
   `ManagedControlPlaneRegistration`, `ManagedTenantProvisioningOperation`, `ExternalBinding`
8. Classification/lookups:
   `EventType`, `EventStatus`, `VisibilityType`, `EventFormat`, `RegistrationMode`, `TicketCatalogStatus`, `TicketPricingMode`, `Category`, `Tag`, `Language`, `Madhab`, `AudienceAge`, `AudienceGender`
9. Federation:
   `AtprotoRecord`, `IndexedDid`, `SyncState`, `ActorKeyStore`
10. Settings and governance:
   `SystemSetting`, `AppSetting`, `ConfigurationChangeLog`
11. Module governance:
   `ModuleDefinition`, `TenantCapability`, plus event aspect entities

## Normalized Lookup Families

Several previously enum-shaped persistence fields are now modeled as lookup/reference rows with stable integer IDs, stable `MasterCode` values, human-readable `FullName`, and optional `Description`. The persisted entity stores the `{LookupName}Id` FK plus a navigation; any enum property that remains on the domain entity is a convenience wrapper ignored by EF, not a database column.

| Domain field | Persisted FK | Lookup entity/table |
|---|---|---|
| `Role.Scope` | `RoleScopeId` | `RoleScope` / `role_scopes` |
| `Permission.Scope` | `RoleScopeId` | `RoleScope` / `role_scopes` |
| `SystemSetting.ValueType` | `SettingValueTypeId` | `SettingValueTypeLookup` / `setting_value_types` |
| `ConfigurationChangeLog.Scope` | `SettingScopeId` | `SettingScopeLookup` / `setting_scopes` |
| `SecretBinding.Scope` | `SettingScopeId` | `SettingScopeLookup` / `setting_scopes` |
| `SecretBinding.SourceType` | `SecretSourceTypeId` | `SecretSourceTypeLookup` / `secret_source_types` |
| `SecretBinding.LastValidationResult` | `SecretValidationStatusId` | `SecretValidationStatus` / `secret_validation_statuses` |
| `ExternalApiKey.OwnerType` | `ExternalApiKeyOwnerTypeId` | `ExternalApiKeyOwnerTypeLookup` / `external_api_key_owner_types` |
| `ExternalApiKey.Status` | `ExternalApiKeyStatusId` | `ExternalApiKeyStatus` / `external_api_key_statuses` |
| `ExternalApiKey.CreditPeriod` | `ExternalApiKeyCreditPeriodId` | `ExternalApiKeyCreditPeriod` / `external_api_key_credit_periods` |
| `Notification.Scope` | `NotificationScopeId` | `NotificationScopeType` / `notification_scope_types` |
| `NotificationChannelPreference.Category` | `CategoryId` | `NotificationPreferenceCategory` / `notification_preference_categories` |
| `NotificationChannelPreference.Channel` | `ChannelId` | `NotificationPreferenceChannel` / `notification_preference_channels` |
| `ActorSubscription.Status` | `StatusId` | `ActorSubscriptionStatus` / `actor_subscription_statuses` |
| `ActorSubscription.NotificationLevel` | `NotificationLevelId` | `ActorSubscriptionNotificationLevel` / `actor_subscription_notification_levels` |
| `Event.SessionStatus` | `EventSessionStatusId` | `EventSessionStatus` / `event_session_statuses` |
| `EventParticipationConfiguration.HandlingMode` | `ParticipationHandlingModeId` | `ParticipationHandlingMode` / `participation_handling_modes` |
| `EventParticipationConfiguration.AdvanceObligation` | `AdvanceRegistrationObligationId` | `AdvanceRegistrationObligation` / `advance_registration_obligations` |
| `EventParticipationConfiguration.IdentityAccess` | `IdentityAccessModeId` | `IdentityAccessMode` / `identity_access_modes` |
| `EventTicketCatalogVersion.Status` | `TicketCatalogStatusId` | `TicketCatalogStatus` / `ticket_catalog_statuses` |
| `EventTicketType.PricingMode` | `TicketPricingModeId` | `TicketPricingMode` / `ticket_pricing_modes` |
| `TicketTypeEntitlement.ScopeType` | `EntitlementScopeTypeId` | `EntitlementScopeType` / `entitlement_scope_types` |
| `TicketTypeEntitlement.SelectionRule` | `EntitlementSelectionRuleId` | `EntitlementSelectionRule` / `entitlement_selection_rules` |
| `EventCapacityPool.OversellPolicy` | `CapacityOversellPolicyId` | `CapacityOversellPolicy` / `capacity_oversell_policies` |
| `EventTicketType.ParticipantDataCollectionMode` | `ParticipantDataCollectionModeId` | `ParticipantDataCollectionMode` / `participant_data_collection_modes` |

API DTOs expose lookup primitives (`*Id`, `*Code`, `*Name`) rather than domain enum values. Repositories query on the normalized FK IDs. Handlers may convert IDs to internal enums only for business-rule switches while keeping persistence and public contracts normalized.

## Non-Inferable Modeling Patterns

### 1) PII Split (1:1 extension tables)

Some entities keep sensitive fields in dedicated PII tables and expose convenience properties via `NotMapped` wrappers. This allows hard-deletion of PII while preserving the main entity for auditing/history.

- `User` -> `UserPii` (`Email`, `FirstName`, `LastName`)
- `Organization` -> `OrganizationPii` (`FullName`, `Email`, address fields)
- `Actor` -> `ActorPii` (`DisplayName`, `Did`, `Handle`, `ProfilePictureUri`)
- `Location` -> `LocationPii`

`EnsurePii()` helper methods create PII objects lazily when mapped properties are set.

Exact proximity discovery is **not implemented**. [ADR-013](adr/ADR-013-postgis-proximity-discovery.md) proposes a separate governed `LocationDiscoveryPoint` projection for a future PostGIS phase. It would be tenant-scoped, explicitly approved, revocable, and stored as `geography(Point,4326)` with GiST indexing; it would not replace or automatically publish `LocationPii` coordinates. Generic location DTOs remain coordinate-free.

The proposed distance unit is an eligible future public `EventSession` occurrence: scheduled, published, non-deleted, tenant-matching, under a public published event, and attached to a location with an active governed point. Online-only, past, draft, private, moderated, unscheduled, deleted, or unapproved occurrences do not participate. PostgreSQL would select the minimum qualifying occurrence distance per event; no current entity, migration, or runtime query provides that capability.

### Location Data Model & Three-Tier Hierarchy

The platform isolates **physical venue master records** from **per-event disclosure rules** and **program schedules** through a three-tier architecture:

```
┌────────────────────────────────────────────────────────┐
│                   Location (Venue)                     │
│  - Physical place (e.g., "Grand Hall", "Community Ctr")│
│  - Country, City, Timezone, Privacy State              │
│  - Sub-rooms (LocationRooms)                           │
│  - Coordinates & Address (LocationPii)                 │
└──────────────────────────┬─────────────────────────────┘
                           │ 1
                           │
                           │ 0..* (Reusable across events)
                           ▼
┌────────────────────────────────────────────────────────┐
│                    EventLocation                       │
│  - Mediates (Event ⇄ Location) OR (Event ⇄ TBA)        │
│  - Disclosure & Privacy Authority                      │
│  - Field Flags: ShowCity, ShowStreet, ShowCoords...    │
│  - Audience Gating: Public, RegisteredOnly, etc.       │
│  - Timed Reveal: RevealFullDetailsFromUtc              │
└──────────────────────────┬─────────────────────────────┘
                           │ 1
                           │
                           │ 0..* (Scoped to the event's location)
                           ▼
┌────────────────────────────────────────────────────────┐
│          EventSession / EventAgendaItem                │
│  - References: EventLocationId (Canonical)             │
│  - Optionally specifies a specific RoomId              │
│  - Inherits disclosure rules & timed reveal            │
└────────────────────────────────────────────────────────┘
```

#### 1. `Location` (Physical Venue Master Record)
- Represents a physical venue (building, community hall, mosque, private residence).
- Scoped by `TenantId` and reusable across multiple events.
- Sensitive exact addresses and coordinates reside in `LocationPii`.
- Physical sub-divisions (e.g. "Hall A", "Room 204") reside in `LocationRoom` (`ak_location_rooms_tenant_id_location_id_id`).
- Governed by privacy states (`PublicVenue`, `PrivateHome`, `Erased`).

#### 2. `EventLocation` (Per-Event Association & Disclosure Policy Authority)
- Acts as the first-class link between an `Event` and a `Location` (or an explicit To-Be-Announced / TBA placeholder when `IsToBeAnnounced = true` and `LocationId = null`).
- Controls what attendees and the general public can see:
  - 7 Granular Visibility Flags: `ShowVenueName`, `ShowCity`, `ShowCountry`, `ShowRoomName`, `ShowStreetAddress`, `ShowPostcode`, `ShowCoordinates`.
  - Audience Gating (`FullDetailsAudienceId`): Public, RegisteredOnly, TicketHoldersOnly, StaffOnly.
  - Timed Address Reveal (`RevealFullDetailsFromUtc`): Allows organizers to keep exact addresses hidden until a specified date/time before the event.
  - Full audit logging for policy modifications and privileged exact reads (`EventLocationDisclosureAudit`, `EventLocationExactReadAudit`).

#### 3. `EventSession` & `EventAgendaItem` (Mediation Invariant)
- Rather than pointing directly to an unmediated `LocationId`, sessions and agenda items reference `EventLocationId`.
- This ensures session schedules cannot accidentally leak physical addresses, violate parent event privacy policies, or reference a physical venue in a different city from the event.
- Enforced at the database level by check constraints (`ck_event_session_physical_location_requires_event_location`).
- Deleting a session, session group, or agenda item clears its canonical event-location reference and the physical location/room keys and navigations together. EF persists deletion as an audited soft-delete UPDATE, so retaining a physical key after detachment would violate the same mediation constraint. The reusable venue and other carriers remain intact.

### Location Address Source, Visibility, And Promotion

A `Location` keeps address origin and reuse scope as independent lookup-backed axes. Source is
`UnknownLegacy`, `Manual`, or `ProviderSelection`; visibility is `Quarantined`, `CreatorPrivate`,
`OrganizationScoped`, or `TenantApproved`. New or retained rows whose provenance is not established
remain `UnknownLegacy` and `Quarantined`. Approval never invents or rewrites provenance.

`PromoteAddressToTenantApproved` is the only tenant-wide visibility transition. It accepts an active,
non-Private-Home address from the quarantined, creator-private, or organization-scoped states and
changes only visibility plus normal update audit/concurrency fields. It preserves `CreatedBy`, the
owning organization reference, tenant, address/postcode, coordinate pair, location metadata, kind,
owner, and source. An already tenant-approved address is an exact no-op. Missing PII, non-active PII,
Private Homes, and erased rows fail closed.

Address visibility controls local application reuse only. It does not alter EventLocation disclosure,
and application-owned address data is never submitted, exported, merged, or backfilled into a
geocoding-provider dataset.

### Location Unicode Search Text

`LocationTextNormalization` validates complete names and addresses before aggregate mutation:
non-null, non-whitespace, at most 500 UTF-16 code units, strict Rune decoding, and no NUL or
Unicode noncharacters. It derives NFC → invariant uppercase → NFC text and rejects a result
over 2,000 UTF-16 code units. Neither source nor output is truncated or silently trimmed.
The complete source remains the display value. Combining marks, joiners, emoji modifiers,
variation selectors, supplementary letters, and private-use scalars remain valid.

`DisplaySortKey` and the removable `LocationPii.AddressSubstringKey` share normalization
revision 2. Database constraints accept only current, nonempty keys; the suggestion predicate
also rejects unsupported revisions. Explicit authorized promotion repairs stale derived state
before publishing, while erased rows cannot regain PII. Grouped PATCH prevalidates every
effective name/address before changing tracked fields, including protected provider selections.

Search is a literal substring of normalized text. Canonical `é`/`e` plus combining acute match;
accent removal, transliteration, full Unicode case folding, and Turkish linguistic casing are
not promised (`Straße` need not match `STRASSE`, and `İstanbul` does not match `ISTANBUL`).
Tenant, current membership, privacy, and governance filtering happen in SQL before projection
and limit. `%`, `_`, backslash, and brackets are literals. Derived text is PII and never becomes
display text, an external geocoder request, or diagnostic output.

Provider-specific Unicode binary collations protect match membership: PostgreSQL `C`, SQLite
`BINARY`, SQL Server Unicode `nvarchar` with `Latin1_General_100_BIN2`, and MySQL/MariaDB
`utf8mb4_bin`. Ordering stays `DisplaySortKey`, then ID, then SQL limit; it is stable within a
provider, with no universal alphabetical or UUID ordering guarantee. Substring matching can
scan filtered candidates; a result limit does not bound scanned rows.

Runtime/ICU changes can change persisted normalization even without a revision change. Follow
the coordinated stopped-write procedure in [OPERATIONS.md](OPERATIONS.md#location-unicode-runtime-compatibility)
and [ADR-028](adr/ADR-028-location-unicode-search-text.md); mixed normalizer writers are unsupported.

### 2) Optional Event Aspects (Layer 2 typed schema)

Base event data stays in `Event`. Optional modules add 1:1 aspect records sharing the same primary key:

- `EventIslamicAspect` (Id = Event.Id)
- `EventTechAspect` (Id = Event.Id)
- `EventSessionIslamicAspect` (session-level extension)

Aspects are optional; an event/session can exist without aspect rows. Sector-standard semantics belong here, not only in Layer 3 custom properties.

`EventSessionIslamicAspect` owns Islamic session scheduling metadata without changing the UTC schedule source of truth.

- **Start Time:** `StartTimeType = Fixed` means the session's UTC `StartTime` is authoritative and `ReferencePrayer`/`OffsetMinutes` must be null. `StartTimeType = RelativeToPrayer` requires `ReferencePrayer` and `OffsetMinutes` (constrained to `-180..180` minutes); application validation also requires `LocationId` so prayer-time resolution has a location anchor.
- **End Time:** Exposes flexible ending logic via `EventSession.EndTimeType` (`Fixed`, `OpenEnded`, `RelativeToPrayer`). When `EndTimeType = RelativeToPrayer`, the ending is relative to `EndReferencePrayer` and `EndOffsetMinutes` (constrained to `-180..180` minutes) on `EventSessionIslamicAspect`. When `EndTimeType = OpenEnded`, the session does not have a set end time and `EndTime` is stored as null.

EF/PostgreSQL check constraints enforce the fixed/relative field shapes, offset ranges, and prayer enum ranges.

### 3) Event And Session Lifecycle

Events remain a single aggregate table. Draft, published, cancelled, completed, archived, and moderated event states are represented by `Event.EventStatusId`; there is no separate event-draft table. Lifecycle writes use explicit commands such as publish, archive, cancel, moderation, and import instead of a generic public status update.

Ordinary command handlers use a two-step validation model. They first perform transport/application checks (manual FluentValidation, authorization, tenant access, and optimistic concurrency) and then preflight the Domain lifecycle predicate before any mutation-side-effect I/O. Same-target retries such as Publish on an already Published event, Cancel on an already Cancelled event, or Archive on an already Archived event are successful no-ops: they do not run readiness evaluation, transactions that write state, federation/outbox/reminder work, cache invalidation, metrics, or timestamp changes. At most one structured idempotent outcome log may be emitted after the unit of work completes; that log is observability, not a lifecycle side effect. Non-idempotent invalid transitions return stable application failure codes without exposing Domain exception text.

Domain lifecycle rules own fixed state-transition, parent-state, and schedule-shape predicates. Application readiness evaluation reuses those predicates but retains policy-selected required fields, validation profiles, provenance, and machine-readable blocker codes/messages. Event-publication preflight evaluates child-session parent compatibility against the intended Published target state rather than rejecting the still-Draft aggregate before mutation; direct session publication still requires an already Published parent. Location publication readiness remains Application-owned because it depends on repository-loaded location facts rather than a pure aggregate invariant.

Ordinary Event transitions are:

| From | Publish | Cancel | Archive | Notes |
|---|---|---|---|---|
| Draft | Allowed after readiness | Allowed | Allowed | Draft-only update commands may edit fields. |
| Published | Same-target no-op | Allowed | Rejected | Published events must cancel before archive. |
| Cancelled | Rejected | Same-target no-op | Allowed | Cancellation is the archive staging state for published events. |
| Completed | Rejected | Rejected | Allowed | Completion can be archived but not re-published. |
| Archived | Rejected | Rejected | Same-target no-op | Archived is terminal for ordinary handlers. |
| Moderated | Rejected | Rejected | Rejected | Moderation restore has its own explicit command path. |

Create Event supports only `Draft` and `Published` requested states. Input `0` is treated as the default Draft request; malformed, undefined, Cancelled, Completed, Archived, or Moderated creation states are rejected before actor resolution or persistence. New `Event` and `EventSession` instances default to Draft through Domain property initialization. For requested Published creation, the handler constructs the Event with the controlled explicit Published constructor before dynamic readiness evaluation so readiness sees the intended target state; after readiness succeeds, it constructs the sessions with controlled explicit Published constructors inside the transaction. It does not treat `Event.Publish(...)` or `EventSession.Publish(...)` as the creation mechanism. Those semantic methods remain the normal transition path for existing aggregates. Import remains Draft/default and emits no lifecycle side effects.

Development seed repair does not force every known seed row to Published. `SeedData` uses controlled Published constructors for the canonical published graph; `DatabaseSeeder` only promotes an existing seed session when `EventSessionLifecycleRules.CanPublish(...)` accepts its current status, the Published parent state, and its schedule. Terminal or otherwise non-publishable states, including Moderated, are left unchanged.

Event sessions also remain normal `EventSession` rows. Draft/internal sessions are represented by `EventSessionStatusId = Draft`, can be unscheduled, and are hidden from anonymous/public program surfaces until they are scheduled and published. This allows a published event to own an internal draft session without leaking it through public session list/detail, program summary, calendar export, agenda projection, or event-list schedule facets. Session publication is subordinate to event publication: an `EventSession` cannot move to `Published` unless its parent `Event` is already `Published`.

Session moderation is event-scoped, not independently session-scoped. Light event moderation moves every session in the event to `Moderated`. Heavy event moderation also redacts event-owned session text/custom-property values to `Redacted`, clears session image references, and moves the sessions to `Moderated`. If one session violates listing rules, the entire event is removed from listing because sessions are tightly bound to the event container.

`EventSessionStatus` is a seeded lookup with stable IDs/codes:

| ID | Code | Meaning |
|---:|---|---|
| 1 | `DRAFT` | Internal editable session draft. |
| 2 | `SUBMITTED` | Submitted for review. |
| 3 | `UNDER_REVIEW` | Under active review. |
| 4 | `APPROVED` | Approved but not public. |
| 5 | `PUBLISHED` | Publicly visible when the parent event is public/published and the session is scheduled. |
| 6 | `REJECTED` | Rejected during review. |
| 7 | `CANCELLED` | Cancelled and not public. |
| 8 | `ARCHIVED` | Archived and not public. |
| 9 | `COMPLETED` | Completed and not actionable for public publishing. |
| 10 | `MODERATED` | Hidden through event-level moderation. |

### 4) Typed Event Participation

`EventParticipationConfiguration` is a required tenant-scoped one-to-one extension of `Event` with a shared event primary key and independent optimistic-concurrency stamp. It replaces the former registration-required boolean and external-registration URL. Three normalized lookup families define handling mode (`INFORMATION_ONLY`, `WALK_IN`, `EXTERNAL_MANAGED`, `PLATFORM_MANAGED`), advance-registration obligation (`NOT_APPLICABLE`, `OPTIONAL`, `REQUIRED`), and optional identity access (`ACCOUNT_REQUIRED`, `GUEST_ALLOWED`, `CAPABILITY_TOKEN_ALLOWED`). Guest recovery remains a typed scalar policy.

Domain rules reject illegal combinations. Information-only and walk-in require a not-applicable obligation and no identity/recovery values. External-managed requires optional or required advance registration and no platform identity/recovery values. Platform-managed requires optional or required advance registration plus a valid identity mode; recovery is absent for account-required and constrained to the recovery policies allowed by guest or capability-token access.

External participation destinations are reviewed `EventPublicAction` records, not fields on the event. Public HAL synthesis may emit one stored-ID redirect only when the participation mode permits that action. Native workflow authorization is permitted only for `PLATFORM_MANAGED`; a click or redirect is engagement, never proof of registration.

### Anonymous Registration Retention

`RegistrationOrder.AnonymousPiiRetentionUntilUtc` records the original guest
allocation's event-purpose deadline even when no PII exists. It is independent
of the extendable private-status promise. `AnonymousRegistrationRetentionPolicy`
uses pinned guest participation and the persisted bound, not missing email or
current account linkage. Claiming an order, editing data, changing settings,
rescheduling or deleting an earlier PII row cannot grant a later deadline.

Ordinary anonymous names and answers use the earlier of their existing row
deadline and the allocation bound. Collection None creates Unnamed participants
without contact PII; required names do not create purchaser email records.
Legal-hold rows remain physically retained, while operational reads still stop
at the anonymous bound. Consent and export evidence retain their separate policy.

`StorageObject.RegistrationContentRetentionUntilUtc` retains the minimum
included-content deadline for generated registration exports after source
answers disappear. Neither scalar replaces the existing row retention categories
or user-erasure authority. Historical guest data with no original bound fails
closed for PII rather than deriving a new window from today's event schedule.

### Registration Workflow Authoring

`RegistrationWorkflow` is the event- and purpose-owned authoring aggregate. It owns ordered `RegistrationRequirement` rows, and each requirement owns ordered `RegistrationChannel` rows. Requirements and channels require positive owner-scoped ordinals; aggregate mutators reject duplicate IDs or ordinals. A native channel has no provider binding; a provider channel carries `RegistrationProviderBindingId`.

The four Task 7.1 lookup families are normalized rows with stable integer IDs and stable `MasterCode` values (enum mirrors are convenience constants, not persisted enum columns):

| Lookup family | Stable code examples | Meaning |
|---|---|---|
| `RegistrationRequirementCriticality` | `REQUIRED`, `OPTIONAL`, `INFORMATIONAL`, `POST_REGISTRATION` | Blocking and lifecycle criticality. |
| `RegistrationRequirementCompletionEffect` | `BLOCKS_REGISTRATION`, `ENRICHES_REGISTRATION`, `NO_REGISTRATION_EFFECT` | Effect of completion on registration. |
| `RegistrationAnswerSyncMode` | `NONE`, `COMPLETION_ONLY`, `SELECTED_FIELDS`, `FULL_CANONICAL`, `MIRROR_ONLY` | What completion may synchronize. |
| `RegistrationRequirementSubjectType` | `ALL_ORDERS`, `SPECIFIC_TICKET_TYPE`, `EVERY_PARTICIPANT`, `LEAD_BOOKER_ONLY`, `CHILD_PARTICIPANTS`, `SPECIFIC_SESSION_SELECTION` | Typed applicability target. |

Evaluation is pure. The workflow applies **ALL** semantics across applicable requirements; a requirement applies **ANY** semantics across its channel completions (subject to its sync mode). Required incomplete requirements block registration. Optional, informational, and post-registration requirements are nonblocking. A permitted registrant skip returns `SkippedByRegistrant`; required or non-skippable requirements reject the skip. This result is not durable registrant state: Task 8.5 owns subject-scoped `RegistrationRequirementFulfillment` and durable skip/finalization persistence.

All three entities are tenant-scoped, audited, soft-deletable, and concurrency-aware. EF named Tenant and SoftDelete filters provide default isolation, while composite tenant/event/workflow foreign keys prevent cross-tenant lineage and generated constraints enforce ownership and ordinal uniqueness.

### Registration Form Authoring

The form authoring aggregate is exactly five persisted entities: `RegistrationForm`, `RegistrationFormVersion`, `RegistrationFormSection`, `RegistrationFormField`, and `RegistrationFormFieldOption`. `FormVersionRules` is a pure domain rules service; `RegistrationFormRule` adds the bounded-condition rules described below. A form owns versioned authoring graphs, and a version is either draft, published, or explicitly retired. Published versions reject graph mutation; edits deep-clone into fresh version/section/field/option IDs while retaining source-template provenance and stable field identity.

Fields have dual identity: immutable graph IDs identify a specific versioned row, while normalized `Namespace/Key` is the stable machine identity for the field across versions. Provider question IDs and provider labels are mapping metadata only and cannot become canonical identity. `Namespace/Key` is unique across all active fields in a version, including fields in different sections; the aggregate rejects duplicates and persistence retains a version-wide active-row unique index as defense in depth. Sections, fields, and options use explicit positive owner-scoped ordinals.

Field governance is explicit and provider-neutral: organizer visibility, explicit-consent requirement, provider-transfer allowance, and positive retention-policy identity are validated by the domain. The model stores no provider-owned question entity and does not reuse custom-property tables. Every form version requires a normalized BCP-47 `LanguageTag`; translation tables and `MULTILINGUAL` content support are intentionally absent until the localization decision in Task 7.8. Form content localization must not be inferred from UI/TMS localization.

#### Bounded Condition Language

`RegistrationFormRule` stores one ordered, immutable-version-owned rule with a typed `FormCondition` and one bounded effect: `Show`, `Hide`, `Require`, or `MakeOptional`. The closed JSON syntax has nine tokens — `equals`, `notEquals`, `in`, `contains`, `exists`, `compare`, `all`, `any`, and `not` — representing ten semantic operations because numeric and `DateOnly` comparison are distinct typed cases of `compare`. No tenth syntax token or arbitrary expression language is supported.

Conditions reference only normalized fields earlier in the same form version. Scalar values are limited to null, text, boolean, decimal number, and `DateOnly`; list answers are supported for membership checks. Evaluation is a pure, deterministic function over an answer snapshot: it performs no I/O, reflection, delegates, ambient time, culture-dependent conversion, authorization, capacity, payment, or registration-state mutation.

#### Deterministic Schema Artifacts And Publication Authority

Each immutable form version pins exactly four deterministic artifacts: the JSON Schema 2020-12 data schema, UI layout, closed condition/rule logic, and the empty provider-mapping shape reserved for Task 9.3. `FormSchemaArtifactGenerator` owns canonical non-indented `System.Text.Json` serialization to UTF-8 bytes and computes lowercase SHA-256 over the complete bundle, including normalized consent purpose code and text version whenever a field requires explicit consent. `FormSchemaArtifactPublicationService` is the Application-owned generate-and-publish facade: it generates from the live relational aggregate and passes the result to the Domain's internal atomic pinning seam. Callers cannot supply artifact JSON or a hash.

Persistence stores all four artifact values and the 64-character hash together. Draft versions require all artifact columns to be null; published and retired versions require all of them to be non-null. The generated `20260801192258_init` migration and model snapshot carry this constraint; generated migration artifacts are not hand-edited. The initial adversarial review found the former caller-authored `Publish(string ...)` authority defect; the repair moved authority to the Application facade/internal Domain seam and was independently confirmed at 0.99.

`EventParticipationConfiguration` owns `ParticipationRequirementAttachment` children. Attach validates exact tenant/event/workflow lineage, active requirement and native/external channel compatibility, rotates the parent concurrency stamp, and stores form-version identity only for a walk-in standalone questionnaire backed by a published pinned schema. Active database uniqueness permits one attachment per requirement and at most one standalone questionnaire per participation configuration. Detach soft-deletes only the attachment, is idempotent, and never mutates its requirement, form, registration orders, or participants.

### Registration Provider Framework

Phase 9 adds provider-neutral integration metadata only; no Formbricks, Google Forms, or Microsoft Forms adapter is claimed. The model is `RegistrationProviderConnection` → `RegistrationProviderBinding` → `RegistrationChannel`. Connections are tenant-scoped, audited, soft-deletable rows that store only `ApiTokenSecretBindingId` and `WebhookSecretBindingId`, plus up to 20 approved HTTPS origins. Approved origins reject user-info, paths, queries, fragments, localhost, link-local, private, loopback, multicast, and metadata hosts.

Bindings pin one form/version and the provider tuple evidence used by capabilities: `(ProviderCode, DeploymentKind, ApiVersion, AdapterPolicyVersion, ConformanceEvidenceRevision)`. The ten D3 capability interfaces are `IRegistrationProviderDescriptor`, `IRegistrationProviderPresentation`, `IRegistrationProviderSchemaReader`, `IRegistrationProviderFormProvisioner`, `IRegistrationProviderSubmissionWriter`, `IRegistrationProviderSubmissionReader`, `IRegistrationProviderCallbackVerifier`, `IRegistrationProviderSubscriptionManager`, `IRegistrationProviderReconciliationProvider`, and `IRegistrationProviderSubmissionSink`. Runtime capability is the intersection of proven tuple support, configured capability rows, governance, mapping compatibility, and authorization; unknown tuples fail closed for automatic finalization.

Drift is one of eight lookup classes: `NoDrift`, `AdditiveOptionalChange`, `LabelOnlyChange`, `MappingRequired`, `RequiredFieldRemoved`, `TypeChanged`, `OptionSetChanged`, or `UnsupportedChange`. `MappingRequired` and worse block publication. Draft mappings can be replaced before publication; published or pinned mappings are immutable and require a new binding revision. Requirement answer sync modes are `NONE`, `COMPLETION_ONLY`, `SELECTED_FIELDS`, `FULL_CANONICAL`, and `MIRROR_ONLY`; trust gates require at least `CompletionOnly`, `SelectedFields`, or `FullCanonical` respectively, while mirror-only requires a sink capability and otherwise parks for reconciliation.

### Ticketing And Instance Monetization

`EventTicketCatalogVersion` owns immutable published catalog revisions. Drafts contain `EventTicketType` rows with one of five normalized pricing modes, optional shared `EventCapacityPool` references, and `TicketTypeEntitlement` rows targeting the Event, a day, or a session. Published edits clone to a new draft. Ticket and capacity rows are tenant-scoped, concurrency-protected, and soft-deletable where their lifecycle permits it.

#### Ticket Type Entitlements (`TicketTypeEntitlement`)

`TicketTypeEntitlement` defines what admission, access rights, or privileges a given `EventTicketType` grants to an attendee. Each entitlement encapsulates:
- **Scope (`EntitlementScopeTypeEnum`)**:
  - `Event` (1): Grants admission to the entire overall event (all days and sessions).
  - `EventDay` (2): Grants admission to a specific day (`EventDayId`) of a multi-day event.
  - `EventSession` (3): Grants admission to a specific session (`EventSessionId`).
- **Selection Rule (`EntitlementSelectionRuleEnum`)**:
  - `AllIncluded` (1): All sub-resources under the scope are automatically included (e.g., all sessions on that day).
  - `FixedSelection` (2): Predefined fixed selection of admission targets.
  - `ChooseOne` (3): Registrant selects 1 session option during registration.
  - `ChooseUpToN` (4): Registrant selects up to *N* session options (e.g., pick 3 workshops out of 10).
- **Included Quantity (`IncludedQuantity`)**: The number of admission units or entries granted per entitlement.

Entitlements feed directly into capacity pool enforcement (`EventCapacityPool`), attendee check-in lists, and location privacy disclosure gating (`EventLocation` reveal policy).
`ScopeId` is the canonical entitlement identity: session ID, then day ID, then target event ID.
PostgreSQL, SQLite, and SQL Server persist it as a stored computed column; MariaDB and MySQL
populate the same value in the save pipeline and generated migration backfill, so the unique
tenant/ticket-type/event/scope index has identical semantics on every provider.

Persisted and API ticketing amounts use `long` integer minor units, named with the `...Minor` suffix. Percentage values use integer basis points, where `10_000 = 100%`. Catalog rows carry a three-character currency code, and published Event summaries derive their currency and lowest available amount from those rows. The implemented persistence model has no scalar Event or EventSession price and defines no foreign-exchange conversion.

`PlatformFeePolicy` and `PlatformContributionSetting` are separate versioned instance aggregates. Fee policies contain basis points and per-currency fixed minor-unit charges. Contribution settings contain DB-stored heading/body text and ordered basis-point options with exactly one zero default. Both start disabled or zero. A platform contribution is instance-directed and never enters organizer earnings, ticket price, capacity, or organizer export totals.

`PaidEventPolicyVersion` is versioned at instance and tenant scope. The instance version is the ceiling; a tenant version can only narrow organizer kinds, currencies in instance order, risk ceilings, review thresholds, and never weaken local-verification, first-paid-event-review, or refund-protection floors. Paid publication requires a fresh policy-valid catalog, merchant/refund/support disclosures, an eligible persisted organizer actor, configured commerce, and a ready connection for that exact `(tenant, organizer actor, provider, platform)` scope. `OrganizerPaymentProviderConnection` preserves replacement and readiness state, while a fenced `OrganizerPaymentProviderAccountOperation` makes hosted account creation safe to retry. No administrator or historical recipient becomes a merchant fallback.

`PromotionDefinition` is the provider-neutral, versioned discount authority for one tenant, Event, published ticket-catalog version, catalog version number, and currency. A definition is either draft, published, or revoked. Published definitions are never edited in place: revision creates the next immutable version in the same definition group. Eligibility is frozen as either all ticket types or an explicit ticket-type set. The definition also pins its UTC window, optional total-redemption limit, optional per-verified-purchaser limit, and exactly one discount rule: a positive fixed minor-unit amount or `1..10_000` basis points, optionally capped by a positive maximum minor-unit amount. Revocation is immediate at the server-owned `TimeProvider` decision instant; it prevents new redemptions without rewriting reservations or order snapshots already accepted. There is no scheduled future-effective revocation or caller-supplied revocation timestamp.

`PromotionCode` stores only a display mask plus persistence-owned `LookupDigest`, `LookupKeyVersion`, active state, retirement time, and the same tenant/event/catalog/currency scope. Plaintext is transient. The lookup digest is HMAC-SHA256 over the normalized code and tenant/Event scope using the instance secret `promotions.code_lookup_hmac_key` qualified as `v{keyVersion}`. Code rotation retires the prior active row and inserts a new digest row while existing reservations keep their original code identity. `PromotionReservation` provides one portable active slot per order; terminal consumed, released, and expired rows move to their own ID-valued slot so history remains append-only. Active plus consumed reservations count toward total and purchaser limits. Verified-purchaser counting uses account identity first, then verified normalized email only when no account is linked, then purchaser actor only when neither stronger identity is available.

One registration order can have at most one applied promotion. Allocation uses integer minor units only. The discount basis is the sum of eligible positive line subtotals. Basis-point discounts round with `(basis * points + 5_000) / 10_000`; fixed and capped results cannot exceed the eligible basis. Allocation first assigns each eligible line `floor(totalDiscount * lineSubtotal / eligibleBasis)`, then awards remaining minor units by descending remainder with line ID as the deterministic tie-breaker. The order and every line snapshot pre-discount subtotal, allocated discount, and post-discount subtotal. The order additionally snapshots definition version, code identity, masked label, reservation, and aggregate pre/post-discount totals.

`RegistrationOrder` is the sole authority for its lifecycle status and transition timestamps. Expected-state writes call `TryTransitionFrom`; ordinary writes call `TransitionTo`; both enforce `RegistrationOrderRules`. Commands and workers use `RegistrationOrderTransitionCoordinator` only to lock, invoke the tracked aggregate, and persist. Authenticated and guest HAL assembly consume `RegistrationOrderRules.DescribeLifecycle`, so command eligibility and advertised affordances cannot drift into separate status-string state machines. Inventory-hold expiry locks the order before the hold, asks `RegistrationInventoryHold` and `RegistrationOrder` to accept their semantic changes, and commits both together.

After allocation, the post-discount line sum is the organizer-directed total; both `PostDiscountOrganizerDirectedTotalMinorSnapshot` and `OrganizerDirectedTotalMinorSnapshot` hold that same amount. The version pinned on the order lines recalculates the platform fee from that post-discount total; organizer earnings equal post-discount organizer total minus fee. An optional platform contribution is recalculated independently from the same post-discount organizer total and is never itself discounted or included in organizer earnings. Total due equals post-discount organizer total plus the contribution. These commercial snapshots remain mutable only before the order crosses its current workflow freeze boundary. Free-order finalization consumes the active promotion reservation in the same serializable lifecycle transaction; cancellation, rejection, waitlisting, and recovery paths release it once. Removing a promotion before that boundary releases the reservation, clears the promotion snapshots, restores line subtotals, and reprices fees and contribution.

`PaymentAttempt` is the independent payment aggregate for a positive registration order. It pins the organizer recipient actor and connected account, merchant country, provider/profile/API revision, currency, organizer amount, platform fee, contribution, total, composition revision, provider idempotency identity, provider object identifiers, expiry, and monotonic status. Its statuses are `Created`, `DispatchPending`, `RequiresAction`, `Processing`, `Succeeded`, `Failed`, `Cancelled`, and `Unknown`; terminal success and failure cannot regress. Checkout dispatch and reconciliation use separate durable effect rows, so provider I/O never occurs in the order transaction. A verified, money-matching `Succeeded` observation is only one input to the existing requirements, approval, hold, and capacity finalization transaction; it cannot bypass those authorities or double-confirm an order.

Stripe hosted Checkout/direct charges and signed payment reconciliation are implemented for the `OrganizerDirect` profile. Admission credentials and online QR check-in are implemented as a provider-neutral domain boundary independent of payment-provider types. Refunds, disputes, transfers, payouts, and legal/tax/invoice support remain separate boundaries. `ProtectedDelayedPayout` remains approval-gated and absent from the default profile.

### 5) Event Reporting And Moderation Review

`EventReport` is the tenant-scoped aggregate for user-facing event reports. It references the reported event, optional reporter user/actor identity, reason code, report status, priority, severity hint, duplicate grouping, reporter contact consent, and hashed reporter fingerprints. Reporter IP/User-Agent fingerprints are hashed at the API boundary before the command leaves the controller.

The aggregate owns:

- `EventReportTarget` rows for event/session/field/storage-object targets.
- `EventReportEvidence` rows for sensitive evidence. Reporter text is encrypted before persistence and exposed only through authorized management detail projections.
- `EventReportCase` rows for local moderation queue state, SLA, assignment, and optimistic concurrency.
- `EventReportSignal` rows for bounded provider verdict metadata such as Osprey signals.
- `EventReportDecision` rows for local moderator or provider decisions before enforcement.
- `EventReportDecisionExecution` as the required one-to-one durable execution state for each decision.
- `EventReportExternalLink` rows for provider sync state, retry metadata, external case/signal IDs, and correlation IDs.

Submit-report writes create the report, primary target, encrypted reporter-text evidence, initial local case, and provider-sync outbox intent in one unit-of-work transaction. The outbox payload is metadata-only: it carries tenant/report/event/case IDs, the authoritative case concurrency stamp, reason/status/priority codes, idempotency/correlation metadata, and evidence descriptors. The Coop mirror sends that stamp as `expected_case_concurrency_stamp`, which a genuinely new signed decision callback must echo unchanged. It must not contain reporter text, reporter IP hashes, user-agent hashes, event titles, slugs, URLs, raw provider payloads, provider secrets, or raw exception text.

Moderation review is CQRS-driven. Triage, assignment, decision capture, and decision execution all require event-management authorization and validate the report/event/case graph plus `EventReportCase.ConcurrencyStamp`. Creating either a local or Coop decision also creates its `Requested` execution row in the same aggregate write, selects it as the current decision, and moves only the case to `DecisionReady`; capture never applies the report outcome. A conditional PostgreSQL claim moves that row through `Requested -> InProgress -> CompletionPending -> Completed`; expired leases are reclaimable, while an exact enforcement receipt is immutable once recorded. Executable decisions reuse the existing light-moderation and heavy-redaction command paths rather than writing event moderation state directly. Those actions must resolve one `EventModerationRecord` by the exact tenant/report/decision source key before the case may complete, and the database makes that key unique.

Decision completion and recipient materialization share one application-owned serializable transaction. That transaction alone applies the report/case lifecycle outcome after the exact receipt is present. `NoViolation` and `Duplicate` record a no-action receipt; `LightModerate` and `HeavyRedact` require their exact moderation-record receipt; `WarnOrganizer` requires a generic warning for every effective active `EventOwner`; `Escalate` and `NeedsMoreInfo` remain nonterminal and produce no final reporter outcome. `NeedsMoreInfo` additionally moves the case to `WaitingReporter` and materializes one decision-scoped `report.needs-more-information` intent: required linkless in-app delivery plus optional email governed by the separate follow-up-contact consent. A persisted non-deleted reporter and active tenant membership are revalidated before graph preparation and again inside completion; absent authority aborts before the business transition and leaves the receipted execution resumable in `CompletionPending`. Email address, verification, trust-safety preference, and follow-up consent are narrower optional-channel checks: their failure records a typed skipped email while preserving required in-app delivery. Dispatch revalidates consent again before provider handoff, and exact execution replay creates no second intent. Organizer authority is re-queried at a fresh time inside every completion attempt and must match the prepared cohort before any recipient row is materialized. Final reporter outcomes use required linkless in-app delivery plus optional verified, preference- and case-update-consent-gated email. Reporter copy exposes only the allowed lifecycle meaning, never evidence, event-private data, moderator/provider identity, reason codes, notes, or invented response links. A completion retry resumes from `CompletionPending` and does not repeat enforcement.

Provider integrations remain metadata-only. Osprey signals and Coop review-queue/callback state are stored as bounded codes and external IDs with idempotency indexes. Signed, authenticated Coop callbacks are retained with one unique `IncomingWebhookEffectOutbox` pointer. The pointer's fenced worker loads and revalidates the retained callback, invokes canonical decision execution outside intake, and commits the applied-effect receipt with pointer completion only after command success. Retryable failures reschedule; poison callbacks dead-letter for authenticated, generation-checked operator redrive. Osprey remains signal-only.

Registration provider callbacks reuse the same incoming-webhook ledger. `RegistrationProviderCallbackController` only reads bounded exact bytes, adds provider/binding route metadata, and acknowledges with `202 Accepted`; malformed, duplicate, stale, out-of-order, unknown-tuple, or unverifiable evidence is acknowledged and either deduped or parked as `NeedsReconciliation`. The worker validates the Data Protection receipt purpose `Explore.RegistrationProviderCallbackReceipt` / `v1` against tenant, connection, binding, provider, tuple key, payload hash, submission id, and timestamp before any Phase 8 submission persistence. Completion is never inferred from redirect return, iframe navigation, or external clicks.

### 6) Event Schedule Source Of Truth

Event scheduling uses UTC instants as the authoritative write model when a session or agenda item is scheduled. `EventSession.StartTime/EndTime` are nullable for draft-capable sessions; `EventAgendaItem.StartTime/EndTime` remain required because agenda items represent concrete schedule blocks. Local dates, local times, and minute-of-day values are generated by the domain scheduling services and persisted only as query/display projections.

The approved write paths are:

- `EventSession.Reschedule(...)` and `EventAgendaItem.Reschedule(...)` for scheduled child items.
- `Event.ApplyScheduleTimeZone(...)` when an event timezone changes and the full schedule graph is loaded for update.
- `Event.RecalculateScheduleSummaryFromSessions()` for event-level schedule rollups.

`ScheduleTimeZoneResolver` normalizes blank timezone input to UTC and validates non-blank values with `TimeZoneInfo.FindSystemTimeZoneById`. Invalid timezone IDs fail validation instead of silently falling back. `Timezone` and `EventTimeZoneId` are treated as aliases during writes and are kept in sync while the product is still in development.

Database constraints provide defense in depth:

- event schedule rollups cannot store inverted first/last local dates or UTC starts;
- event timezone IDs cannot be blank strings;
- scheduled session and agenda item end times must be strictly after start times;
- session local projection constraints are conditional so unscheduled drafts can keep all schedule projection columns null;
- persisted local minute-of-day values must match persisted local time fields and stay within `0..1439` when present;
- active room-bound scheduled sessions cannot overlap in the same tenant/location/room. `EventSessionConfiguration` declares `EX_EventSession_RoomNoOverlap` as model-owned PostgreSQL metadata, and `PostgresModelConstraintApplier` applies the GiST exclusion constraint over `tstzrange(StartTime, EndTime, '[)')` only when `StartTime` and `EndTime` are non-null; adjacent sessions are allowed and soft-deleted or unscheduled sessions release the room.

PostgreSQL generated columns were not selected for timezone projection ownership because timezone conversion depends on system timezone data and is a poor fit for immutable generated expressions. Keeping projection ownership in the domain/application layer preserves deterministic tests, explicit validation, and Clean Architecture boundaries.

### 7) Layer 3 Governed Custom-Property Extension Model

The platform provides a flexible EAV-based extension system across multiple scopes:

- **Shared Definitions**: `CustomPropertyDefinition` for Organization and Group extensions, plus "Shared Event Definitions".
- **Event Templates**: `EventTemplate` blueprints with `EventTemplateCustomPropertyDefinition`.
- **Event Runtime**: `EventCustomPropertyDefinition` tied to specific events, materialized from templates or created directly.
- **Event Values**: `EventCustomPropertyValue` stores typed runtime data with multi-value ordinal support.
- **Event Session Runtime**: `EventSessionCustomPropertyDefinition` and `EventSessionCustomPropertyValue` mirror the event model for scheduled child content.
- **Projections**: `EventCustomPropertyProjection` and `EventSessionCustomPropertyProjection` provide denormalized read models for discovery/filtering.

**Key Rule**: Layer 3 exists for long-tail extensions. Standard sector fields must use Layer 2 typed schema.

Explicit admin purge is the only hard-delete path for dependency-free custom-property definitions. Normal delete remains retire + soft delete so historical values, projections, and audit evidence stay recoverable.

### 8) Polymorphic Reference Registry

Polymorphic references that cannot use a direct FK are governed by `Explore.Domain.References.ReferenceTypeRegistry`. The registry is the domain source of truth for target kind, ID shape, ownership, tenant-scope rule, cleanup behavior, and validation wording. Current registries cover:

- `ExternalBinding`: allowed external/internal type pairs from `ExternalBindingTypes`, including the tenant/customer provisioning binding, admin user, tenant-local user state, profile, actor, login, organization, and group organizer bindings.
- `Notification`: every `NotificationEntityTypeEnum` value maps to a registered target kind. `Notification.EntityId` is a string column for compatibility with lookup-driven deep links, but registered targets currently require Guid-form entity IDs and retain historical references when the linked entity is deleted or hidden.
- Shared custom properties: every `EntityTypeName` value is represented. `Organization` and `Group` support shared `CustomPropertyDefinition`/`CustomPropertyValue` rows. `Event` is deliberately registered as unsupported for shared definitions because event custom properties use `EventCustomPropertyDefinition`, `EventCustomPropertyValue`, and template materialization instead.

Write-time enforcement happens at the repository/application boundary: external-binding, notification, and shared custom-property definition writes validate against the registry before saving. EF model metadata also declares check constraints for registered external-binding pair/scope combinations, shared custom-property target types, and notification entity reference shape. Migration regeneration is intentionally separate in the development workflow, so the registry and repository guards remain the immediate runtime enforcement until generated migrations are refreshed.

### 9) Tenant and Soft-Delete Interfaces

Isolation and lifecycle are enforced via marker interfaces:

- `ITenantEntity` -> `TenantId` (Global filter in DbContext)
- `IAuditableEntity` -> `CreatedAt/By`, `UpdatedAt/By` (Auto-populated in SaveChanges)
- `ISoftDeletable` -> `IsDeleted`, `DeletedAt/By` (Converted from Delete state in SaveChanges)

### 10) Tenant-Local User Authority

`TenantUser` is the tenant-local user root. It owns tenant participation status, moderation lifecycle, actor/profile links, and soft-delete state for a global `User` inside one tenant.

Tenant role authority is represented by `TenantUserRoleGrant`, not by a direct `User`/`Tenant` membership row. The database enforces this with:

- a composite FK from `TenantUserRoleGrant(TenantId, TenantUserId)` to `TenantUser(TenantId, Id)`;
- a composite FK from `TenantUserRoleGrant(RoleId, RoleScopeId)` to `Role(Id, RoleScopeId)`;
- a check constraint forcing `RoleScopeId = Tenant`;
- a filtered unique index allowing only one active grant per `(TenantId, TenantUserId, RoleId)`.

Revocation is explicit (`RevokedAt`, `RevokedBy`, `RevocationReason`) so historical authority evidence remains auditable while active checks ignore revoked grants.

### 11) ATProto External Subject Promotion And Consolidation

An unknown exact DID materializes one global `AtprotoIdentity`, one `Actor` with `ActorTypeId = ExternalUnclassified`, and one `ExternalActorSubject`. Verified Organization or Group classification can promote that Actor in place, preserving the Actor, identity, profile, and Event identifiers while retiring the external owner evidence.

Consolidation into an existing same-kind Actor is stricter. The signed bootstrap request must name the canonical Actor and its expected concurrency stamp, and the authenticated User must already hold active OrgAdmin or GroupAdmin authority over an approved, non-suspended participation in the current tenant. Exact DID proves the external source only; names, handles, URLs, profile similarity, and classification intent do not prove authority over the canonical target.

The serializable onboarding transaction moves active operational references for the identity, Events, EventSeries, session speakers, and tenant-local subscriptions. It records one immutable `ActorMerge` with the identity ID and a bounded SHA-256 DID digest, then retires the source Actor. Consent, reports, organizer claims, notifications, moderation records, exports, canonical records, and other historical evidence remain attached to the source. Prepared encrypted OAuth-session persistence commits in the same retry attempt; JWT issuance occurs only after commit.

### 12) Four-Level Actor And Event Moderation

Moderation is split by authority and effect:

| Level | State and effect | Authority |
|---|---|---|
| Global Actor | `Actor.IsSuspended` blocks the represented subject across the instance. | Instance administrator only. |
| Exact ATProto credential | `AtprotoIdentity.IsSuspended` blocks that exact DID credential globally without suspending the Actor. Reinstatement preserves the identity's independent `IsActive` value. | Instance administrator only. |
| Tenant participation | `TenantUser`, `OrganizationTenant`, and `GroupTenant` hold tenant-local status, visibility, suspension, approval, organizer eligibility, and import policy. | Tenant authority in that tenant only. |
| Event content | Event moderation changes the tenant-local Event lifecycle and public availability. | Event moderation policy in scope. |

Actor and identity suspend or reinstate transitions append immutable `ActorModerationRecord` or `AtprotoIdentityModerationRecord` rows only when state changes. A retry requesting the current state succeeds without adding another record or persisting another transition.

Event creation eligibility and public visibility are separate rules. Creation requires an active Actor plus an active local `TenantUser`, or an approved, organizer-eligible, unsuspended Organization or Group participation. Public visibility requires a published, public, non-deleted Event and active Actor. A local User Event also requires an active `TenantUser`. A local Organization or Group Event requires approved, visible, unsuspended participation, but does not require organizer eligibility after creation. Outbound-owned ATProto records stay on this local branch. Inbound federated Events instead require a non-tombstoned record, its current visible tenant presentation, and an exact active, unsuspended, non-deleted DID identity owned by the Event Actor.

`OrganizationTenantEvidence` retains one private tenant-owned Document against a concrete Organization participation. It transitions once from pending to approved or rejected with reviewer audit and concurrency control. The review state is evidence workflow state only: it never changes the participation approval state automatically.

### 13) Actor Subscriptions And Notification Fanout

`ActorSubscription` is the canonical durable relationship for user subscriptions to subscribable actors. V1 supports organization and group target actors only. The subscription stores the active tenant-local subscriber (`SubscriberTenantUserId`), denormalized global `SubscriberUserId` for notification delivery, target actor, target actor type, subscription status, notification level, audit fields, soft-delete fields, and a concurrency stamp.

Unsubscribe is modeled as a status transition to `UNSUBSCRIBED`, not as deletion. Resubscribe reactivates the same durable row and resets the notification level to the v1 default. Command handlers and fanout scans require an active, non-deleted `TenantUser` so suspended, banned, removed, or deleted tenant-local users do not receive subscription fanout.

`Notification.DeduplicationKey` is required for fanout-created notifications. Event-published fanout uses deterministic keys so outbox retries or duplicate internal dispatches do not create duplicate inbox rows for the same tenant/event/subscriber tuple.

`NotificationFanoutRun` records resumable worker state for a fanout source: tenant, fanout kind, entity type, entity ID, source actor, status, subscriber cursor, aggregate processed/created counts, failure text, and timestamps. It intentionally stores no PII.

Notification preference matrix state is normalized separately from the in-app `Notification` rows. `NotificationPreferenceCategory` and `NotificationPreferenceChannel` are stable lookup rows; `NotificationChannelPreference` stores scoped category/channel choices; `NotificationPreferenceProfile` stores scoped global mute state. Preference rows are tenant-scoped, soft-deletable, audited, concurrency-aware, and constrained so user, organization, and group scopes carry exactly the matching target id.

Delivery services call the effective notification preference resolver before creating non-required in-app fanout rows. Trust-safety is user-controllable for optional case-update and light-moderation delivery; required heavy-moderation availability notices bypass preferences through their server-owned delivery policy rather than client-side checks.

## Fair Return And Waitlist

`FairReturnSupplyPolicy`, `FairReturnSupplyUnit`, `EventWaitlistEntry`, `EventWaitlistOffer`, and `FairReturnSourceBinding` form one tenant-qualified lifecycle. Open-slot nullable keys enforce one open supply, queue entry, offer, and binding without reusing closed-row sentinels. Allocation locks canonical entity fences and rolls losers back atomically. Withdrawal may substitute only a commercially equivalent source before payment handoff; expiry and finalization share the same offer, entry, supply, and binding fence.

Public queue position is calculated from literal domain order but capped at 999, with zero meaning unavailable. API and UI contracts expose only bounded status/reason state. Participant, account, seller, payment instrument, provider payload, commerce amount, and queue priority remain server-side. HAL link presence is the sole browser action authority and durable paid-sale controls suppress new allocation and withdrawal links.

`WaitlistProviderObservation` is monotonic, digest-only provider evidence. Durable payment/refund orchestration uses stable operation identity, processing fences, expiring leases, bounded retries, dead letters, and fixed-cardinality telemetry; no provider call occurs while a database transaction is open.

## Messaging and Reliability

### RefundAttempt, RefundCampaign, And Material-Change Choice

`RefundAttempt` is the tenant-qualified, provider-neutral refund truth. It pins the captured `PaymentAttempt`, immutable `PaidOrderAcceptanceSnapshot`, original connected account/payment/currency, accepted refund policy, stable non-PII provider idempotency key, authority/reason audit, and independent asynchronous status. Allocation is computed as the delta between cumulative captured-component allocations while the payment lock is held, so sequential partial refunds consume organizer, fee, contribution, and per-line minor units exactly once. Buyer-refund success and exact application-fee settlement are persisted separately: buyer totals follow proven main-refund evidence, while the operation remains action-required or unknown until its platform-fee leg is exact. Every state except definitive `Failed` or `Cancelled` reserves captured capacity; an open dispute blocks ordinary new reservation. PostgreSQL locks the exact payment row and other providers use the repository's transaction-scoped named lock, so concurrent partial/full requests cannot over-reserve.

`RefundCampaign` is the bounded event-cancellation or material-change fanout aggregate. Event cancellation stops sales and creates the campaign plus one outbox trigger in the lifecycle transaction. Payment-attempt creation assigns a tenant-scoped persisted `long` campaign cursor under the existing relational named lock; a fenced worker pages that immutable pre-decision cohort in batches of at most 100. Durable refund attempts and choices are the counter authority, so resume may rescan idempotently without double-counting. Captured cancellation rows become full remaining refund intents; uncaptured rows cancel locally before provider handoff or use idempotent provider-cancellation work. Late capture returns to the same campaign refund key. Definitive provider blocks stop automatic mutation retries; the authorized campaign resume action explicitly requeues the existing attempt.

`RegistrationMaterialChangeChoice` pins one paid order, payment, accepted snapshot, and material-change campaign. A buyer may transition once from `Pending` to `AcceptedNewTerms` or `RefundRequested`; contradictory decisions fail closed. A protected pre-capture choice becomes terminal `NotApplicable` if its pinned payment later fails or is cancelled, and projections never attach an older payment's choice to a newer attempt. Refund choice and refund reservation/outbox creation commit atomically. Published session/timezone changes create material-change campaigns alongside immutable attendee-change notification evidence. Events with paid acceptance or succeeded payment evidence cannot be deleted.

`PaymentDispute` remains independent provider evidence. Multiple inquiries/formal disputes may exist for one payment; provider stage/status/money/deadline observations advance monotonically and never manufacture refund success.

### OutboxMessage

Transactional outbox entity for reliable asynchronous event dispatch (at-least-once delivery):

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | UUID v7 (time-sortable) |
| `AggregateType` | `string` | e.g., "Event", "Actor" |
| `AggregateId` | `Guid` | Source entity ID |
| `EventType` | `string` | Event classification |
| `Payload` | `string?` | JSONB serialized data |
| `Status` | `Enum` | Pending, Processing, Completed, Failed, DeadLettered |
| `NextRetryAt` | `DateTime?`| Exponential backoff schedule |

Event publication writes an internal `EventPublishedNotificationFanoutRequested` outbox message for actor-subscription fanout. The fanout message is routed by the composite dispatcher to the application fanout service, which writes durable `Notification` rows and advances `NotificationFanoutRun` state idempotently. External broker publication for `EventPublished` is retired from this workstream; future integration-event broker work needs a separate product requirement and failure model.

Specialized variants: `PdsSyncOutbox` (federation), `PolicyChangeOutbox` (governance), `EmailDispatchOutbox` (basic email dispatch state).

## Persistence-Enforced Rules (from EF configuration)

- `Event.Title`: Required, max 200.
- `Event` and `EventSession`: No scalar price or currency columns; published price summaries derive from the published `EventTicketCatalogVersion` and its ticket types.
- `Event.EventTimeZoneId`: Optional, max 100; blank strings rejected.
- `Event`: Schedule rollups reject inverted first/last local date and UTC start ranges.
- `EventSession`: `EventSessionStatusId` is required; schedule and local projection fields are nullable for drafts; if a schedule is present, UTC end must be after UTC start and local minute projections must match local time projections.
- `EventAgendaItem`: UTC end must be after UTC start; local minute projections must match local time projections.
- `AppSetting`: Blocks high-value secret keys (e.g., `Database:`, `ConnectionStrings:`) via DB constraint.
- `Actor`: Unique nullable owner FKs (exactly one of UserId, OrganizationId, or GroupId).
- `ActorSubscription`: Unique non-deleted subscription row per `(TenantId, SubscriberTenantUserId, TargetActorId)`; target actor type is limited to organization/group in v1.
- `Notification`: Fanout rows require deterministic `DeduplicationKey` for duplicate prevention.
- `NotificationChannelPreference`: Unique non-deleted row per tenant/scope/target/category/channel; scope-target check constraints enforce no target for system/instance/tenant scopes and exactly one matching target for organization, group, or user scopes.
- `NotificationPreferenceProfile`: Unique non-deleted row per tenant/scope/target for global mute state with the same scope-target constraints.
- `NotificationFanoutRun`: Unique source tuple per `(TenantId, FanoutKind, NotificationEntityTypeId, EntityId, SourceActorId)`.
- `EventContactShareConsent`: Exactly one typed subject FK (`User`, registration purchaser order, registration participant, or guest-contact order) and one current row per `(TenantId, SubjectTypeId, SubjectId, RecipientActorId, PurposeCode)`; grant/regrant/withdrawal evidence is append-only in `EventContactShareConsentHistory`.
- Registration answer and PII retention: policy duration resolves to UTC `RetentionUntil`. Standard operational data uses 730 days, sensitive data 90 days, marketing-consent evidence 2555 days, and legal hold has no automatic physical-deletion deadline. Anonymous names/answers additionally obey the original event-purpose bound above; edits cannot increase their finite row deadline.
- `EventReport`: Composite tenant/event alternate keys enforce same-tenant event ownership; status/priority/reporter/source enum ranges are DB constrained; terminal statuses require `ClosedAt`.
- `EventReportCase`: Composite tenant/report/case keys enforce queue ownership; queue code is required; status/priority ranges are constrained; concurrency stamp is the optimistic write guard.
- `EventReportEvidence`: Reporter-text evidence rows require encrypted text; content hashes are optional but non-blank when present; retention and content-hash indexes support cleanup/deduplication without exposing raw evidence.
- `EventReportDecision`: Local decisions require `ModeratorUserId`; provider decisions may use external decision IDs with a tenant/source uniqueness guard.
- `EventReportExternalLink` and `EventReportSignal`: Provider correlation/external IDs are unique per tenant/provider and store bounded failure categories only.
- `RefundAttempt`: unique tenant/provider idempotency and campaign/payment/acceptance reservation keys; accepted snapshot, original account/payment, currency, and per-line allocation use tenant-qualified restrictive foreign keys.
- `RefundCampaign`: indexed tenant/status/lease and stable cursor ordering; optimistic concurrency plus lease token/fence rejects stale workers.
- `RegistrationMaterialChangeChoice`: unique `(TenantId, RefundCampaignId, PaymentAttemptId, PaidOrderAcceptanceSnapshotId)` and restrictive tenant-qualified campaign/payment/acceptance foreign keys.
- `PaymentDispute`: unique tenant/provider dispute identity with indexed payment/status projection and optional provider response deadline.

## Related
- [ARCHITECTURE.md](ARCHITECTURE.md)
- [CUSTOM_PROPERTIES.md](CUSTOM_PROPERTIES.md)
- [MULTI_TENANCY.md](MULTI_TENANCY.md)
- [OUTBOX_PATTERN.md](OUTBOX_PATTERN.md)
