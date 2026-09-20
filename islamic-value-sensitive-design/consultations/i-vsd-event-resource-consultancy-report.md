<!-- ABOUTME: I-VSD consultancy for a first-class EventResource capability with governed audience, timing, and delivery policies. -->
<!-- ABOUTME: Maps repository evidence to provider duties, architecture recommendations, risks, mitigations, and validation gates. -->

# EventResource I-VSD Consultancy Report

Last Updated: 2026-09-02

## Review Metadata

- Mode: standalone
- Subject: EventResource capability
- Workstream: none
- Report kind: consultancy-report
- Report status: current
- Disposition: ready-for-planning
- Evidence cutoff: 2026-09-02
- Reviewed input revision: sha256 `ebf1709b01eaf980a305a0802f572d84d7ce125d2b9e144dbb3f6a3c1a89cb48`
- Supersedes: none

## Scope

This consultation reviews whether and how ISLAMU Event should add first-class
event resources such as documents, presentations, attendee handbooks, speaker
materials, virtual-meeting links, livestreams, recordings, worksheets, and
certificates.

The review covers:

- the existing Event visibility, publication, schedule, and partial-public
  location-disclosure architecture;
- semantic resource kinds versus delivery mechanisms;
- registration, approval, ticket, check-in, completion, event-speaker, and
  session-speaker entitlements;
- protected file and external-link delivery;
- HAL, MediatR, Cerbos, local-authorization, and tenant-governance boundaries;
- publication, availability, revocation, deletion, audit, and support
  lifecycles;
- privacy, accessibility, content governance, portability, federation, and
  self-hosting implications;
- implementation direction and evidence gates for a later repository-grounded
  implementation plan.

This report does not define final API route names, database migrations, UI
screens, Cerbos policy syntax, provider integrations, or implementation tasks.
Those belong in the implementation plan after the threat model and policy
vocabulary are accepted.

## Claim Boundary

This is I-VSD provider-responsibility design reasoning traceable to Trust,
Non-Harm, Justice, Rights of People, Truthfulness, Promise-Keeping, Excellence,
Modesty, and Avoiding Spying. It is not a fatwa, Sharia certification, legal
opinion, security certification, accessibility conformance claim, or proof of
ethical outcomes.

Repository code, tests, policies, and documentation support implementation
traceability for the current architecture. Stakeholder and operational
validation were not available. Any religious-legal conclusion about particular
uploaded content remains outside I-VSD authority.

## Findings

### IVSD-F001 - A first-class Layer 1 resource model is justified

- Lifecycle: open
- Severity: high
- Claim type: design direction supported by implementation traceability
- Principle and domain: Trust, Excellence, Promise-Keeping; strategic and
  technical
- Stakeholders: organizers, attendees, speakers, tenant operators, self-hosters,
  API consumers
- Provider-controlled decision: whether resources become governed domain state
  or remain plain URLs, custom properties, or generic attachments
- Evidence: `src/Explore.Domain/Event.cs:14-155` already treats publication,
  schedule, registration policy, sessions, and tickets as first-class event
  concerns. `docs/ARCHITECTURE.md` and `docs/DOMAIN.md` distinguish universal
  Layer 1 concepts from typed sector schemas and tenant custom properties.
- Validation level: design and implementation traceability
- Linked mitigation: IVSD-M001
- Owner or next validation: product/domain planning must confirm the smallest
  universal semantic vocabulary
- Escalation boundary: none

Event resources have their own meaning, publication, audience, timing,
delivery, security, and lifecycle. Treating them as EAV properties or bare
links would hide domain invariants and bypass the authorization and disclosure
architecture.

### IVSD-F002 - Semantic kind and delivery mechanism are orthogonal

- Lifecycle: open
- Severity: medium
- Claim type: design concern
- Principle and domain: Truthfulness, Excellence; design and technical
- Stakeholders: resource consumers, organizers, assistive-technology users,
  client developers
- Provider-controlled decision: whether one `File`/`Link` lookup is asked to
  represent both meaning and transport
- Evidence: the user-provided examples include meeting links, slide decks,
  handbooks, recordings, and certificates, each of which can be delivered by
  more than one mechanism
- Validation level: design validation
- Linked mitigation: IVSD-M002
- Owner or next validation: product and UX review
- Escalation boundary: none

A `File`/`Link` kind cannot communicate user intent, select the right UI,
support accessibility metadata, or establish appropriate policy defaults.

### IVSD-F003 - EventResource needs its own authorization identity

- Lifecycle: open
- Severity: high
- Claim type: security architecture concern
- Principle and domain: Trust, Justice, Non-Harm; technical and governance
- Stakeholders: attendees, speakers, organizers, tenant operators, unauthorized
  users
- Provider-controlled decision: whether authorization is evaluated only
  against the parent Event or against the individual resource and its parent
  authority
- Evidence: `src/Explore.Application/Authorization/ResourceDescriptors.cs:65-88`
  gives Event a stable resource identity and trusted facts.
  `src/Explore.Application/Behaviors/AuthorizationBehavior.cs:25-91` and
  `src/Explore.API/Hateoas/HateoasAuthorizationEvaluator.cs:39-153` enforce
  resource/action checks and fail-closed link suppression.
- Validation level: implementation traceability
- Linked mitigation: IVSD-M003
- Owner or next validation: security architecture and authorization-parity
  review
- Escalation boundary: Tier 1 threat-model review is required before
  implementation

Using only the Event ID would make two resources on the same event
indistinguishable to policy evaluation. Each EventResource therefore needs a
stable resource ID and typed facts, while management authority is derived from
its trusted parent Event.

### IVSD-F004 - Access must use live domain entitlements, not role claims

- Lifecycle: open
- Severity: high
- Claim type: security and fairness concern
- Principle and domain: Justice, Trust, Rights of People; technical and
  governance
- Stakeholders: registered attendees, cancelled attendees, speakers, session
  speakers, ticket holders, staff
- Provider-controlled decision: how audience membership is determined and
  revoked
- Evidence: `src/Explore.Domain/EventSessionSpeaker.cs:10-26` stores
  session-scoped Actor assignment. `src/Explore.Domain/RegistrationOrder.cs:70-111`
  has separate submitted, confirmed, rejected, and cancelled state.
  `src/Explore.Domain/ParticipantAdmissionEligibility.cs:38-76,168-244,292-333`
  separately governs approval, completion, revocation, and readiness.
  `src/Explore.Domain/AdmissionTicket.cs:62-105` defines active ticket and
  issuance authority. `src/Explore.Domain/AdmissionCheckInRules.cs:8-111`
  validates target-specific check-in.
  `src/Explore.Domain/TenantUser.cs:8-31`,
  `src/Explore.Domain/EventRoleAssignment.cs:10-35,47-88`, and
  `src/Explore.Domain/TicketTypeEntitlement.cs:44-95` provide the remaining
  tenant-membership, event-staff, and session-ticket scope authorities.
- Validation level: implementation traceability with missing end-to-end
  EventResource evidence
- Linked mitigation: IVSD-M004
- Owner or next validation: domain, security, and persistence design
- Escalation boundary: authorization must fail closed when entitlement facts
  cannot be resolved

`Speaker` must never mean a broad platform role. The repository has
session-speaker assignments, not a first-class event-speaker role. An
event-speaker predicate must therefore mean "the Actor currently controls at
least one EventSessionSpeaker assignment under this Event." Session-speaker
access requires an assignment to the resource's session. Registration
cancellation, approval withdrawal, ticket invalidation, check-in reversal, or
assignment removal must deny every subsequent authorization decision without
waiting for stale identity claims to expire.

### IVSD-F005 - Metadata disclosure and content delivery are separate rights

- Lifecycle: open
- Severity: high
- Claim type: privacy and security concern
- Principle and domain: Modesty, Non-Harm, Truthfulness, Rights of People;
  design and technical
- Stakeholders: prospective attendees, restricted attendees, speakers,
  organizers
- Provider-controlled decision: whether a person may discover that a resource
  exists independently of opening or downloading it
- Evidence:
  `src/Explore.Application/Contracts/LocationPrivacy/EventLocationDisclosureContract.cs:12-190`
  defines Public, Attendee, and Management purposes plus Hidden, TBA,
  Available, PrivateVenue, Unavailable, and NeedsPrivacyReview states.
  `src/Explore.API/Hateoas/HateoasAuthorizationEvaluator.cs:39-153` materializes
  only authorized links.
- Validation level: implementation traceability
- Linked mitigation: IVSD-M005
- Owner or next validation: API, authorization, and UX design
- Escalation boundary: sensitive metadata categories require privacy review

Some users should see a teaser such as "Attendee handbook - available after
approval" without receiving the file or destination. Other resources, such as
an internal speaker brief, should be completely hidden from unauthorized
people.

### IVSD-F006 - Protected destinations must never appear in ordinary contracts

- Lifecycle: open
- Severity: high
- Claim type: security concern
- Principle and domain: Trust, Non-Harm, Rights of People; technical and
  operational
- Stakeholders: attendees, speakers, organizers, external meeting providers
- Provider-controlled decision: whether backing object keys, presigned URLs,
  meeting URLs, or provider tokens appear in DTOs, logs, caches, analytics, or
  federation records
- Evidence: the BFF upload flow in
  `src/Explore.Blazor/Extensions/BffStorageEndpoints.cs:45-220` uses
  server-issued upload sessions rather than browser-authored destinations.
  Existing HAL rules make the API the hard authorization boundary.
- Validation level: implementation traceability
- Linked mitigation: IVSD-M006
- Owner or next validation: security architecture, API, and observability
  review
- Escalation boundary: any destination containing credentials or bearer-like
  query parameters requires secret-handling review

Hiding a URL in the Blazor UI does not protect it. Protected destinations must
be disclosed only through a request-time authorized access endpoint and stored
behind a classified persistence boundary. The platform must state truthfully
that a presigned or externally reusable bearer destination can still be copied
and can remain usable after later entitlement revocation until it expires or is
rotated.

### IVSD-F007 - StorageObject is reusable, but its current visibility is not the policy model

- Lifecycle: open
- Severity: high
- Claim type: implementation gap
- Principle and domain: Trust, Non-Harm, Excellence; technical and operational
- Stakeholders: uploaders, resource consumers, operators, incident responders
- Provider-controlled decision: whether EventResource duplicates file metadata
  or reuses the storage authority
- Evidence: `src/Explore.Domain/StorageObject.cs:8-83` already owns tenant,
  provider, object key, checksum, purpose, visibility, ownership, quarantine,
  lifecycle, audit, and soft deletion. `src/Explore.Domain/StorageConstants.cs:24-75`
  defines PublicImage, AuthenticatedTenant, and PrivateOwner visibility plus
  Pending, Active, Quarantined, DeleteRequested, and Deleted lifecycle states.
  `src/Explore.Application/Services/StorageObjectContentReader.cs:143-170`
  permits PrivateOwner only to the creating user.
- Validation level: implementation traceability
- Linked mitigation: IVSD-M007
- Owner or next validation: storage, security, and persistence design
- Escalation boundary: no restricted file may rely on
  `AuthenticatedTenant` when its audience is narrower than the tenant

EventResource should reference StorageObject rather than duplicate storage
metadata. Its audience policy remains the authority for access; restricted
files should use PrivateOwner storage visibility. The existing generic reader
and presigned path cannot be reused unchanged because attendee access is
narrower than tenant access but broader than uploader-only ownership.

### IVSD-F008 - File safety and private object-delivery evidence are incomplete

- Lifecycle: open
- Severity: high
- Claim type: missing operational and security evidence
- Principle and domain: Non-Harm, Trust, Excellence; technical and operational
- Stakeholders: uploaders, consumers, operators, hosting providers
- Provider-controlled decision: which files can leave quarantine and how
  private bytes are delivered
- Evidence:
  `src/Explore.Application/Services/StorageContentSignaturePolicy.cs:8-116`
  and
  `tests/Event.Application.UnitTests/Services/StorageContentSignaturePolicySecurityTests.cs:35-43,59-66,77-96,200-205`
  cover container/signature checks.
  `src/Explore.Application/Services/StorageObjectContentReader.cs:54-60`
  denies quarantined registration files. Bounded scan `SCAN-STORAGE-001` found
  no malware-scanner integration. `docker-compose.yml:547-578` creates an
  optional MinIO service whose initializer makes the bucket public.
- Validation level: implementation traceability plus missing operational
  validation
- Linked mitigation: IVSD-M008
- Owner or next validation: security, platform operations, and self-hosting
  review
- Escalation boundary: scanner policy and production bucket posture must be
  resolved before protected file release

The architecture must not silently equate "signature accepted" with
"malware-free." It must also avoid turning a protected API object into a public
bucket object.

### IVSD-F009 - Publication, availability, and schedule-relative release are distinct

- Lifecycle: open
- Severity: medium
- Claim type: domain-state concern
- Principle and domain: Truthfulness, Promise-Keeping, Justice; design and
  technical
- Stakeholders: attendees, speakers, organizers, support staff
- Provider-controlled decision: how draft state, release windows, event/session
  schedule changes, and withdrawal interact
- Evidence: `src/Explore.Domain/Event.cs:105-155` treats UTC session instants as
  schedule authority and maintains event schedule projections. Public
  eligibility in
  `src/Explore.Persistence/Extensions/PublicEventEligibilityQueryExtensions.cs:10-116`
  separately requires publication, public visibility, and valid schedule
  children.
- Validation level: implementation traceability
- Linked mitigation: IVSD-M009
- Owner or next validation: domain and persistence planning
- Escalation boundary: none

An `IsPublished` boolean plus two timestamps is not sufficient for relative
rules such as "30 minutes before this session." Persisting only a calculated
timestamp can become stale when the schedule changes. Crucially, the parent
Event's lifecycle, publication, and visibility state strictly gate child
resource accessibility: an EventResource must fail closed (inaccessible and
hidden) whenever its parent Event is soft-deleted, in Draft, Cancelled,
Archived, or unpublished, regardless of the child resource's individual
publication or timing state.

### IVSD-F010 - Organizer choice requires Instance and Tenant guardrails

- Lifecycle: open
- Severity: high
- Claim type: governance concern
- Principle and domain: Trust, Justice, Non-Harm; governance and strategic
- Stakeholders: organizers, tenant administrators, instance operators,
  attendees, speakers
- Provider-controlled decision: whether organizers can author arbitrary
  policies or only configure governed domain predicates
- Evidence: `docs/MULTI_TENANCY.md:161-175` defines the
  Instance-to-Tenant-to-Organization-to-Group-to-User governance cascade and
  higher-tier lock behavior. UR-002 continued the active report goal after the
  recommendation to make governed typed presets the v1 baseline.
- Validation level: design validation supported by architecture documentation
- Linked mitigation: IVSD-M010
- Owner or next validation: product governance and authorization planning
- Escalation boundary: widening organizer authority requires explicit threat
  and abuse review

An unrestricted policy language would transfer security, fairness, and support
burden to organizers while making Cerbos/local parity difficult to prove. A
single visibility enum is too weak. The appropriate middle ground is a closed,
typed, domain-specific policy vocabulary that higher authorities may restrict.

### IVSD-F011 - Access audit must be useful without becoming surveillance

- Lifecycle: open
- Severity: medium
- Claim type: privacy and operations concern
- Principle and domain: Trust, Avoiding Spying, Rights of People; operational,
  governance, and evaluation
- Stakeholders: attendees, speakers, organizers, support and security staff
- Provider-controlled decision: what individual access evidence is retained,
  who can inspect it, and for how long
- Evidence: `src/Explore.Domain/StorageObject.cs:8,33,39-46` provides generic
  audit fields.
  `src/Explore.Application/Telemetry/BusinessMetrics.cs:542-555,1362-1398`
  records low-cardinality storage metrics rather than immutable access history.
  Bounded scan `SCAN-STORAGE-001` found no EventResource access-audit entity,
  repository, handler, test, or retention policy.
- Validation level: missing implementation and operational evidence
- Linked mitigation: IVSD-M011
- Owner or next validation: privacy, security, support, and tenant-governance
  review
- Escalation boundary: identified access history, IP addresses, or user agents
  require a documented purpose and retention review

Individual access logs may support leak investigation and revocation, but
organizers do not automatically need a permanent attendance-surveillance
record.

### IVSD-F012 - The standalone floor must remain viable

- Lifecycle: open
- Severity: high
- Claim type: architecture and stewardship concern
- Principle and domain: Trust, Promise-Keeping, Rights of People; strategic and
  technical
- Stakeholders: mosques, nonprofits, small communities, self-hosters,
  enterprise operators
- Provider-controlled decision: whether Cerbos, S3, CDN, malware services, or a
  meeting-provider integration becomes mandatory
- Evidence: `docs/SELF_HOSTING.md:416-429,483-506` requires core behavior to
  work in one Event.Standalone process with durable SQLite and no mandatory
  sidecar. However, `src/Event.Standalone/appsettings.json:11-14`,
  `src/Event.Standalone/Dockerfile:41,48,51`, and
  `docs/SELF_HOSTING.md:71-100` persist `/app/data`, while
  `src/Explore.Infrastructure/Storage/LocalFileStorageOptions.cs:6-12`
  defaults file bytes under relative `storage-data/local`, outside that volume.
- Validation level: documented commitment plus implementation traceability
- Linked mitigation: IVSD-M012
- Owner or next validation: architecture and self-hosting planning
- Escalation boundary: none

Enterprise controls should strengthen the same resource model through optional
providers, not create a separate feature that small deployments cannot use.
Importantly, the storage path mismatch in `LocalFileStorageOptions.cs` is an
existing platform-level infrastructure defect affecting all file storage in
Event.Standalone, not merely EventResource: the container mounts `/app/data`
while file bytes default to `/app/storage-data/local`, meaning any container
recreation destroys uploaded files while leaving SQLite database records
dangling. Event.Standalone requires a platform-wide configuration baseline
fix (`Storage:Local:RootPath = "/app/data/storage"`) to guarantee durability.

### IVSD-F013 - Accessibility, content governance, federation, and templates can leak responsibility

- Lifecycle: open
- Severity: medium
- Claim type: overlooked cross-domain risk
- Principle and domain: Justice, Non-Harm, Truthfulness, Rights of People;
  design, operational, and governance
- Stakeholders: disabled users, content owners, affected non-users, moderators,
  federated audiences, future event organizers
- Provider-controlled decision: what metadata, alternatives, reports,
  destinations, and defaults are copied or published
- Evidence: `docs/DOMAIN.md:399-420` and `docs/API.md:194-199` define generic
  template provenance and diff/apply boundaries. `docs/PROJECT.md:45-56`
  defines governed database-first federation, while
  `docs/API.md:153-158,220-223` rejects caller-authored destinations and
  provider-key disclosure. Bounded scan `SCAN-RESOURCE-001` found no
  EventResource-specific accessibility, acceptable-use, takedown, template, or
  federation contract.
- Validation level: not reviewed because the feature is not implemented
- Linked mitigation: IVSD-M013
- Owner or next validation: accessibility, content governance, federation, and
  template planning
- Escalation boundary: platform curation or classification of contested
  religious content requires qualified scholarly governance input

Templates must never copy a live private meeting destination or file into a new
event. Federation must never publish a protected destination or imply that a
remote instance can enforce local attendee entitlements.

### IVSD-F014 - Credential-bearing destinations need a protected persistence boundary

- Lifecycle: open
- Severity: high
- Claim type: security and data-governance concern
- Principle and domain: Trust, Non-Harm, Rights of People; technical and
  operational
- Stakeholders: attendees, speakers, organizers, database operators, backup
  operators
- Provider-controlled decision: whether a persistent external destination is
  ordinary resource data or a bearer-equivalent protected value
- Evidence: `docs/API.md:153-158,220-223` requires reviewed server-owned
  destinations and prevents provider keys or arbitrary caller URLs from
  entering public contracts. `src/Explore.Domain/Secrets/SecretBinding.cs:9-28,41-91`
  shows reference-only operator secret binding, while
  `src/Explore.Application/Contracts/Services/IRegistrationSensitiveValueProtector.cs:1-12`
  and
  `src/Explore.Infrastructure/Services/RegistrationSensitiveValueProtector.cs:10-34`
  show a domain-separated encrypted-value pattern. The current feature has no
  EventResource persistence boundary.
- Validation level: design validation with missing implementation evidence
- Linked mitigation: IVSD-M014
- Owner or next validation: security, persistence, secrets, backup, and export
  design
- Escalation boundary: a credential-bearing or bearer-equivalent destination
  may not be implemented as a plain mapped string

Protected response-time disclosure is insufficient if database mapping,
change tracking, diagnostics, backups, templates, or exports can materialize a
raw meeting or streaming credential. Furthermore, relying on default ASP.NET
Core Data Protection creates severe operational risk: default keys expire after
90 days and reside in ephemeral container storage. Without explicitly
configuring a persistent keyring under the durable volume (`/app/data/keyring`)
for Standalone or database/blob key storage in enterprise multi-node clusters,
any container recreation or key expiration renders all encrypted meeting
destinations permanently unreadable (`CryptographicException`).

### IVSD-F015 - Revocation guarantees must account for issued bearer artifacts

- Lifecycle: open
- Severity: high
- Claim type: security and truthfulness concern
- Principle and domain: Trust, Truthfulness, Non-Harm; technical and
  operational
- Stakeholders: attendees, speakers, organizers, incident responders
- Provider-controlled decision: whether access is streamed through the
  authority or converted into a reusable bearer destination
- Evidence: existing presigned downloads accept an expiry bounded up to 60
  minutes at `src/Explore.API/Controllers/StorageObjectController.cs:164-183`
  and
  `src/Explore.Application/Features/StorageObjects/Handlers/Queries/GetPresignedDownloadUrlRequestHandler.cs:42-57,92-109`.
- Validation level: implementation traceability
- Linked mitigation: IVSD-M015
- Owner or next validation: security, storage, API, and incident-response design
- Escalation boundary: no immediate-revocation claim is valid for an already
  issued bearer URL

Current entitlement state can deny the next access request, but it cannot
recall a presigned URL or third-party destination already disclosed to a
recipient. This vulnerability is exacerbated by session schedule drift: if an
organizer delays or shifts a session while attendees hold active 30-to-60-minute
presigned URLs or meeting redirects, attendees retain premature or out-of-schedule
access until the bearer token expires. Bearer TTLs must be strictly bounded and
clamped to the remaining session window (5–15 minutes maximum) to mitigate
schedule drift exposure.

## Recommendations

### Decision

Implement EventResource as a first-class, tenant-scoped, event-owned Layer 1
aggregate with a stable authorization identity. Separate semantic kind,
delivery type, metadata disclosure, audience entitlement, publication, and
availability. Use a closed, typed, tenant-governed policy vocabulary evaluated
against live server-resolved facts. Defer arbitrary organizer-authored rules,
selected-user allowlists, embedded active content, and provider-specific
generated links until concrete use cases justify their governance burden.
Persist dynamic credential-bearing destinations only as tenant/resource-bound
protected envelopes.

### Options considered

| Option | User value | Complexity | Security/governance risk | Self-hosting | Disposition |
| --- | --- | --- | --- | --- | --- |
| Plain URL/file fields on Event | Low | Low | High | Easy | Rejected: no per-resource lifecycle or policy |
| Layer 3 custom properties/EAV | Medium | Medium | High | Easy | Rejected: hides core semantics and authorization |
| One access-level enum | Medium | Low | Medium-high | Easy | Rejected: cannot express live scoped entitlements |
| Generic policy DSL/rules engine | High in theory | High | High | Poor | Deferred: premature and hard to govern |
| Governed typed policy composition | High | Medium | Medium, controllable | Good | Recommended |

### IVSD-M001 - Create a separate event-owned aggregate

Use a `Guid` EventResource ID, mandatory Tenant and Event IDs, and an optional
EventSession ID. Keep Event as ownership and management authority without
forcing every resource mutation through the Event concurrency token. However,
enforce the domain invariant that child EventResource access is strictly bounded
by parent Event lifecycle: if an Event is soft-deleted, in Draft, Cancelled,
Archived, or unpublished, all child resources are fail-closed inaccessible
regardless of their own publication or availability policies.

Recommended core state:

```text
EventResource
- Id
- TenantId
- EventId
- EventSessionId?
- EventResourceKindId
- EventResourceDeliveryTypeId
- PublicationStateId
- MetadataDisclosureModeId
- Title
- Description?
- LanguageCode?
- AccessibilityNote?
- SortOrder
- StorageObjectId?
- ExternalDestinationCiphertext?
- ExternalDestinationProtectionVersion?
- ProviderReference?
- AvailabilityPolicy
- CreatedAt / CreatedBy
- UpdatedAt / UpdatedBy
- IsDeleted
- ConcurrencyStamp
```

Enforce exactly one delivery payload for the selected delivery type. Do not
store storage-provider keys on EventResource.

### IVSD-M002 - Separate semantic kind from delivery type

Use normalized lookup metadata with stable IDs, codes, and names.

Initial semantic kinds may include:

```text
GeneralDocument
Presentation
SpeakerMaterial
AttendeeHandbook
Schedule
Worksheet
Recording
Livestream
VirtualMeeting
Certificate
SponsorMaterial
OrganizerInternal
Other
```

Initial delivery types should be:

```text
StoredFile
ExternalLink
ProviderGeneratedLink
```

Implement `StoredFile` and `ExternalLink` first. Reserve
`ProviderGeneratedLink` for an integration that can issue short-lived
destinations. Do not add arbitrary embedded HTML or active content in v1.

### IVSD-M003 - Add an EventResource authorization descriptor

Introduce a distinct authorization resource kind using the EventResource ID.
Its trusted facts should include Tenant ID, Event ID, optional EventSession ID,
organizer authority, publication state, metadata-disclosure mode, audience
policy summary, delivery type, and availability result.

Parent Event authority may grant management, but access to content is always a
decision on the individual EventResource. Add every action across the canonical
action catalog, descriptor/fact projection, Cerbos policy, local evaluator,
HAL policy, and parity tests.

Add a persisted-resource context resolver that reloads EventResource, parent
Event, tenant, policy, and delivery facts. Add a subject-specific live
entitlement resolver for registration, participant eligibility, ticket,
check-in, and speaker state. Endpoint and HAL checks must consume those trusted
resolvers rather than DTO-authored, client-authored, claim-cached, or
link-cached facts. Missing, unknown, stale, cross-tenant, or ambiguous facts
deny. Standardize on the Specification/Query-Filter pattern for public discovery
(analogous to `PublicEventEligibilityQueryExtensions.cs`) before invoking
authorization behaviors, keeping Cerbos policies focused strictly on
authenticated subject entitlements and role assignments to prevent dual-engine
drift.

Recommended actions:

```text
view-metadata
access
download
manage
publish
unpublish
delete
view-audit
```

The final action spelling must follow the repository catalog.

### IVSD-M004 - Use a typed entitlement policy, not an arbitrary DSL

Model one or more typed audience variants. Variants are OR alternatives;
requirements inside one variant are AND constraints. Zero variants, unknown
variants, invalid qualifiers, and failed fact resolution mean deny-all.

```text
Public
AuthenticatedTenantMember
SessionRegistrant
  - EventSessionId
  - RequireConfirmedOrder
  - RequireParticipantApproval
  - RequireParticipantCompletion
TicketHolder
  - EventSessionId
  - EventTicketTypeId?
CheckedInParticipant
  - AdmissionTargetKind
  - AdmissionTargetId
AnyEventSessionSpeaker
SessionSpeaker
  - EventSessionId
EventStaff
Organizer
```

The variants map to repository authorities: EventRegistration coverage,
RegistrationOrder confirmation, ParticipantAdmissionEligibility
approval/completion/revocation, active AdmissionTicket with
EventTicketTypeId, target-specific active check-in, and
EventSessionSpeaker. `AnyEventSessionSpeaker` is derived from at least one
current session assignment under the Event; it is not a new platform role.

The remaining variants have exact authority:

- `AuthenticatedTenantMember` requires a current, active, non-deleted
  TenantUser in the ambient Tenant.
- `TicketHolder` requires an active AdmissionTicket held by the current subject
  plus EventRegistration lineage and a TicketTypeEntitlement that covers the
  resource's EventSession.
- `EventStaff` requires an effective, non-revoked EventRoleAssignment whose
  role/capability is explicitly permitted for the requested resource action and
  whose validity window includes the access time.
- `Organizer` requires current control of the exact Event.OrganizerActorId,
  resolved through existing Actor authority; generic event-management or
  administrator status does not substitute for this predicate.

Domain invariants reject Public mixed with restricted alternatives and any
session/target predicate without its required scope. Selected-user allowlists,
arbitrary groups, and custom expressions remain deferred.

To preserve dual-database parity between PostgreSQL (multi-tenant) and SQLite
(Standalone) without breaking EF Core query translation, model audience
variants either as a normalized relational child table
(`EventResourceAudienceRule` with a discriminator and nullable scope keys) or
as a strongly-typed Value Object whose predicates are evaluated
in-memory/application-space after batch-loading subject entitlement facts.
Prohibit PostgreSQL-only `jsonb` queries that break SQLite translation.

Resolve all facts from current tenant-scoped data during authorization. Do not
trust client facts or broad identity-provider claims. Batch-resolve facts for
HAL collections to avoid N+1 queries, but re-evaluate at content access.

The current Cerbos service requires a user or machine principal. Public
EventResource eligibility should therefore use a separate server-owned
specification query pre-filter, analogous to public Event eligibility in
`PublicEventEligibilityQueryExtensions.cs`, filtering public resources before
invoking Cerbos policies. Public metadata or content must never depend on a
synthetic authenticated user.

Guest registrations, unclaimed participants, and dependent attendees need a
separate purpose-bound capability design. Until that threat model exists, they
deny rather than inheriting another person's user identity.

### IVSD-M005 - Define metadata disclosure independently

Use a disclosure mode inspired by EventLocation:

```text
Hidden
Teaser
VisibleWhenEligible
Public
```

`Teaser` may expose safe title, semantic kind, availability explanation, and
the requirement category without exposing a destination. Sensitive titles or
speaker-only internal resources use `Hidden`.

The API may return metadata while omitting `access` and `download` links.
Blazor must render actions only from HAL link presence, never from local
registration, ticket, role, or timing checks.

A HAL link is a discoverable affordance, not an access grant. Every access
endpoint reloads current resource, timing, tenant, and subject entitlement.

### IVSD-M006 - Deliver protected content through authorized endpoints

Use an authorized access endpoint that resolves current policy immediately
before delivery:

- stored file: stream same-origin content or issue a short-lived URL for a
  private object;
- external link: return a controlled redirect only after authorization;
- provider-generated link: request a short-lived provider destination after
  authorization.

Never return the backing object key, persistent external destination, provider
credential, or presigned URL in ordinary metadata. Apply private/no-store cache
rules to protected metadata and access responses. Redact destinations from
logs, traces, analytics, ProblemDetails, and support artifacts.

Same-origin streaming is the default for revocation-sensitive resources.
Presigned or provider-generated bearer destinations require a server-fixed
maximum TTL, a documented maximum stale-grant interval, private cache rules,
destination rotation/revocation behavior, and truthful UI/audit language.
Organizer-selected expiry is not permitted. A later entitlement change denies
new grants but cannot recall an already issued bearer artifact.

External links should default to HTTPS, display their origin before navigation,
avoid server-side preview fetching, and support Instance/Tenant domain policy.
If future code fetches a URL, it needs a separate SSRF threat model including
DNS rebinding and private-network denial.

### IVSD-M007 - Reuse StorageObject as the storage authority

EventResource owns meaning and entitlement; StorageObject owns provider,
object key, size, content type, checksum, lifecycle, quarantine, and deletion.
Restricted EventResource files should remain `PrivateOwner` at the storage
layer.

Authorization order:

1. load tenant-scoped EventResource and trusted parent facts;
2. evaluate metadata/content action and current availability;
3. verify StorageObject is active, same-tenant, PrivateOwner, and owned by the
   resource;
4. open or redirect through the selected storage provider;
5. record only the justified audit event.

Implement a dedicated EventResource content-delivery service after the
EventResource authorization decision. Do not call the current generic
StorageObjectContentReader or presigned handler as the attendee decision:
their PrivateOwner rule authorizes only the creating user.

Registration-answer, participation-requirement, and retained-evidence
attachments remain separate owners with their existing retention semantics.

### IVSD-M008 - Make file-safety state explicit

New uploads begin unavailable while validation is pending. Signature
validation and malware disposition are separate facts. Do not label an
unscanned object as clean.

Preserve the standalone floor with progressive policy:

- Tier 1: strict document allowlist, file-size limits, signature checks, safe
  filenames, private local storage, and deny-by-default publication for
  unscanned content; an operator may explicitly accept a narrowly documented
  unscanned policy without the platform describing it as clean;
- Tier 2/3: optional scanner integration that Instance governance can require
  for all tenants;
- every tier: quarantine denial, replacement/revocation support, and clear
  operator documentation.

Private S3-compatible buckets are mandatory for restricted content. Public
sample-bucket behavior must be explicitly documented as development-only or
changed before production use.

### IVSD-M009 - Separate lifecycle and preserve relative timing intent

Use an explicit publication state such as Draft, Published, Withdrawn, and
Archived. Compute availability separately as Scheduled, Available, or Expired.

Support:

```text
AbsoluteWindow
RelativeToEventStart
RelativeToEventEnd
RelativeToSessionStart
RelativeToSessionEnd
AfterRegistrationApproval
AfterCheckIn
AfterCompletion
```

Store the relative rule as source of truth. A projected UTC instant may support
queries, but it must be recomputed transactionally when the authoritative
event/session schedule changes or checked live before access. A scheduler is
not required merely to make time pass; request-time evaluation remains the
security boundary. In all cases, parent Event lifecycle strictly dominates: if
the parent Event is not in an active, published, and eligible state, child
resources must immediately evaluate to unavailable and hidden.

### IVSD-M010 - Apply hierarchical governance

Instance governance should be able to lock:

- allowed delivery types and file types;
- file-size and retention ceilings;
- whether unscanned files may be published;
- external-link schemes and domain restrictions;
- which audience predicates tenants may delegate;
- whether individual access audit is permitted;
- whether presigned, redirect, or same-origin delivery is allowed.

Tenant governance may narrow those choices. Organization and event organizers
configure resources within the allowed envelope. No lower authority can widen
an upper-level restriction.

Planning must use native typed setting definitions, hierarchical resolution,
lock-aware mutation, effective-value cache invalidation, and configuration
manifest import/export parity rather than introducing a parallel resource
settings mechanism.

### IVSD-M011 - Minimize and govern access evidence

Audit policy changes, publication changes, granted access, denied access,
download-grant issuance, and destination rotation with reason categories.
Default fields should be resource ID, tenant ID, actor/user ID when justified,
action, outcome, reason category, and timestamp.

Do not retain IP address, user agent, full destination, object key, or URL query
parameters by default. Separate low-cardinality operational metrics from
identified audit records. Give each record class an owner, retention period,
authorized readers, export behavior, and deletion or legal-retention rule.

### IVSD-M012 - Preserve provider neutrality and portability

The same EventResource domain and API must work with:

- Event.Standalone, SQLite, local authorization, and local file storage;
- multi-tenant deployments using local or Cerbos authorization;
- optional private S3-compatible storage and CDN delivery;
- future meeting or streaming providers.

External providers are adapters, not domain authorities. Export should include
resource metadata, policy vocabulary, timing, and owned files where rights
permit. Provider-generated destinations and secrets are not portable content.

Standalone local bytes must live under the durable mounted data root, for
example `/app/data/storage`, not the current relative default outside the
volume. To resolve the platform-level defect in `LocalFileStorageOptions`,
the deployment baseline in `src/Event.Standalone/appsettings.json` must be
explicitly configured with `Storage:Local:RootPath = "/app/data/storage"`.
Container-recreation and backup/restore verification must prove that
SQLite metadata and referenced bytes survive and remain consistent together.

### IVSD-M013 - Add accessibility, content, federation, and template safeguards

Expose file type, size, language, external origin, accessibility notes, and
available alternatives before access. Support captions/transcripts for
recordings and accessible alternatives for presentation or document formats.

Define acceptable-use, reporting, takedown, correction, and appeal ownership
before enabling broad organizer uploads. Preserve evidence only under an
explicit retention purpose.

Federation may announce a public or intentionally disclosed teaser with a web
URL to the authoritative instance. It must not federate private destinations,
object keys, access-policy internals, or claims that remote instances can
enforce local entitlement.

Templates may create draft placeholders containing semantic kind, default
policy, and timing intent. They must never clone a live file, private
destination, provider token, or prior event's access record.

### IVSD-M014 - Protect dynamic external destinations at persistence time

Classify external destinations before persistence:

- ordinary public HTTPS destination: validated server-owned value with no
  bearer or credential material anywhere in the normalized URI, including
  userinfo, deceptive host form, path, query, or fragment;
- organizer-authored credential-bearing or bearer-equivalent destination:
  encrypted envelope;
- operator-provisioned provider credential or connection: existing registered
  SecretBinding/secret-resolver architecture.

For dynamic protected destinations, follow the repository's domain-separated
envelope pattern with a new Application
`IEventResourceDestinationProtector` and an Infrastructure Data Protection
implementation. Bind the versioned protection purpose to Tenant ID and
EventResource ID. Persist ciphertext and protection version only; decrypt only
after the live EventResource access decision.

Do not reuse admission or registration protectors because their purpose,
validation, and lifecycle are intentionally domain-specific. Exclude raw
destinations from ordinary DTOs, change/audit payloads, logs, traces,
ProblemDetails, configuration export, templates, federation, and generic
resource export. Furthermore, the Data Protection keyring must be explicitly
configured for durable persistence: store keys under `/app/data/keyring` for
Event.Standalone (within the mounted `/app/data` volume) and use database or
blob-backed keyring storage in enterprise multi-node deployments. Document
key-ring backup and rotation consequences to prevent unrecoverable decryption
failures.

### IVSD-M015 - Define revocation as a measurable delivery contract

For same-origin streaming, current entitlement denial stops the next byte
request. For presigned, redirected, or third-party bearer destinations,
document:

- fixed maximum TTL (clamped to 5–15 minutes, never exceeding the remaining
  session window) and maximum stale-grant interval;
- mitigation of session schedule drift: dynamic re-check of session timing
  before issuing new grants;
- which resources are forbidden from bearer delivery;
- whether provider-side rotation or revocation exists;
- cache-control and CDN behavior;
- audit semantics for grant issuance versus actual downstream use;
- incident response when a destination leaks;
- user-facing wording that does not promise immediate recall.

Resource unpublish, registration cancellation, approval withdrawal, ticket
invalidation, check-in reversal, or speaker removal must deny every new grant
immediately. Existing bearer artifacts expire or are rotated within the stated
revocation SLA.

### Planning inputs

#### "The Worst Break" Adversarial Scenario & Invariant-Breaker Test

- **Adversarial Scenario:** Silent leakage of confidential gathering
  coordinates or pre-release materials to cancelled/unapproved attendees or
  public search indexers via stale client caching or premature HAL affordance
  emission.
- **Mandatory Red-Phase Invariant Test:** Author an adversarial integration test
  where an attendee's registration order is cancelled concurrently with an
  active access request for a restricted resource. The API must fail closed
  with RFC 7807 ProblemDetails 403, emit zero presigned tokens or destination
  redirects, and record a zero-PII audit event.

#### The 4-Point Right-Sizing Rule & 4-PR Decoupled Sequence

To avoid monolithic PR failure, planning must decouple implementation into four
discrete, reviewable PR slices:

1. **PR 1: Platform Storage Durability & Core Domain Aggregate**
   - Fix `src/Event.Standalone/appsettings.json` to set
     `Storage:Local:RootPath = "/app/data/storage"`.
   - Implement `EventResource` aggregate, normalized lookups (`EventResourceKind`,
     `EventResourceDeliveryType`), and `StoredFile` linking to `StorageObject`.
2. **PR 2: Authorization, Entitlement Resolvers & Private File Delivery API**
   - Implement `IEventResourceAuthorizationResolver` (resolving registration,
     order, ticket, check-in, and speaker assignments).
   - Cerbos policy (`cerbos/policies/islamuevent_event_resource.yaml`) and
     local fallback evaluator with 100% parity tests.
   - Dedicated private content streaming service bypassing generic reader
     limitations.
3. **PR 3: Protected External Destinations & Keyring Durability**
   - Implement `IEventResourceDestinationProtector` backed by ASP.NET Core Data
     Protection with durable keyring under `/app/data/keyring`.
   - Normalized URL classification, validation, and authorized redirect
     endpoints.
4. **PR 4: HAL Hypermedia Policies, Blazor UI & Governance**
   - Implement `EventResourceLinkPolicy` for HAL affordance emission.
   - Blazor UI components gating user actions strictly via `_links` presence.
   - Instance/Tenant hierarchical governance locking.

## Common Overlooked Failures And Outcomes

Feature type: event-owned protected files and destinations

Common overlooked failures:

- placing a meeting URL in a DTO and merely hiding it in the UI;
- treating all tenant members, all speakers, or all registered users as the
  same audience;
- using a nonexistent generic ticket product or one registration-status field
  instead of the repository's separate registration, order, participant
  eligibility, ticket, check-in, and session-speaker authorities;
- treating an empty, unknown, stale, or ambiguous policy as public rather than
  deny-all;
- sending anonymous public access through an authorization path that requires
  an authenticated user or machine principal;
- keeping a cancelled attendee or removed speaker authorized through stale
  claims or cached links;
- making resource metadata public when its title reveals a private session,
  speaker concern, or participant status;
- using a public bucket or cache for content that was authorized by the API;
- persisting standalone SQLite metadata while storing local file bytes outside
  the mounted durable volume;
- equating MIME type or file signature validation with malware clearance;
- fetching organizer-supplied URLs for previews without an SSRF boundary;
- copying private destinations through templates, federation, logs, analytics,
  support records, or generated clients;
- storing individual access history indefinitely because it might be useful;
- storing bearer-like meeting destinations as plain mapped strings or exporting
  them as ordinary resource metadata;
- relying on default ephemeral ASP.NET Core Data Protection keys, causing
  permanent loss of encrypted destinations on container reboot;
- allowing a published child resource to remain accessible when the parent
  event is unpublished, cancelled, or soft-deleted;
- issuing long-lived presigned URLs that remain valid after an event or session
  schedule delay or cancellation;
- promising immediate revocation after issuing a reusable presigned or
  third-party bearer destination;
- combining domain aggregate, storage bugfixes, crypto protection, Cerbos
  policies, and UI into a single unreviewable monolithic PR;
- offering inaccessible PDFs, presentations, recordings, or external tools
  without alternatives;
- promising that access controls prevent legitimate recipients from
  resharing an external URL;
- enabling uploads without reporting, takedown, rights, incident, backup, and
  deletion ownership.

Possible bad outcomes:

- unauthorized event entry, harassment, disruption, or exposure of private
  religious/community gatherings;
- disclosure of speaker-only material or attendee status;
- malware delivery, provider suspension, public object exposure, or CDN cache
  leakage;
- unfair exclusion caused by stale, ambiguous, or unappealable entitlement
  decisions;
- surveillance of attendee learning or participation;
- inaccessible event participation and support burden;
- copyright, privacy, reputational, moderation, or operational disputes;
- loss of trust when "private" means only hidden in the browser.

Positive outcomes if implemented responsibly:

- one consistent portal for public, attendee, speaker, staff, and organizer
  materials;
- immediate denial of new grants from current domain state and bounded,
  truthful expiry or rotation of already issued bearer artifacts;
- truthful distinction between discoverability, eligibility, and delivery;
- safer self-hosting with the same domain model at every deployment tier;
- stronger privacy, accessibility, incident-response, and control evidence;
- less organizer confusion and fewer support incidents than an unrestricted
  rules engine would create.

Provider questions before implementation:

- Which audience predicates are universal enough for v1?
- Which resource titles may be teased, and which must be completely hidden?
- What exact file-safety decision permits release at each deployment tier?
- Which individual access records are necessary, and who may inspect them?
- Which restrictions can Instance administrators lock against Tenant or
  organizer override?
- What user-facing promise will explain that recipients can still reshare some
  external destinations?

## Stakeholders

- Event attendees, including pending, approved, cancelled, checked-in, and
  completed participants.
- Event and session speakers, including people represented by managed Actors.
- Event organizers, staff, volunteers, moderators, and support teams.
- Tenant and Instance administrators responsible for policy and incidents.
- Small single-tenant self-hosters and larger managed/enterprise operators.
- Disabled users and people dependent on accessible alternatives.
- People appearing in uploaded documents, recordings, or screenshots.
- Copyright holders and people affected by abusive or deceptive material.
- External storage, meeting, streaming, CDN, and security providers.
- API clients, federated instances, and future users receiving template-derived
  events.

## I-VSD Principles And Domains

| Principle | EventResource application |
| --- | --- |
| Trust / Amanah | Protect files and destinations, constrain admin power, revoke access, and maintain recoverable storage. |
| Truthfulness / Sidq | Explain eligibility, timing, external origin, limitations, and inability to prevent recipient resharing. |
| Justice / Adl | Use precise event/session entitlements, accessible alternatives, consistent decisions, and correction paths. |
| Non-Harm / La Darar | Deny leaks, malware, stale grants, public caching, SSRF, and unsafe defaults. |
| Rights of People | Respect privacy, content rights, export, correction, deletion, contestability, and bounded audit. |
| Promise-Keeping | Make privacy, availability, self-hosting, backup, retention, and EOL behavior operationally credible. |
| Excellence / Ihsan | Reuse mature authorization/storage seams, test parity, document operations, and validate accessibility. |
| Modesty / Haya | Keep sensitive metadata and gathering locations hidden unless disclosure is intentional and justified. |
| Avoiding Spying / Tajassus | Minimize identified access records and avoid unnecessary attendee surveillance. |

All six I-VSD domains apply:

- Strategic: preserve the self-hosting and provider-neutral product thesis.
- Design: protective disclosure defaults, clear locked states, accessibility,
  and external-origin transparency.
- Technical: typed policy, tenant isolation, authorization parity, private
  storage, revocation, and portability.
- Operational: scanning, incidents, destination rotation, support, backup,
  deletion, and provider failure.
- Governance: Instance/Tenant delegation, audit access, content reports,
  corrections, and escalation.
- Evaluation: access-control incidents, stale grants, broken resources,
  accessibility findings, support burden, and stakeholder feedback.

## Validation Gaps

- No stakeholder interviews or usability testing validated organizer policy
  comprehension, attendee expectations, or speaker workflows.
- No deployed EventResource implementation, runtime logs, incident records,
  support data, or operational audit exists.
- No end-to-end threat model covers destination leakage, link resharing,
  cache/CDN behavior, scanner failure, schedule changes, and entitlement
  revocation races.
- No accepted guest, unclaimed-participant, or dependent-attendee capability
  model exists.
- No standalone container-recreation or backup/restore evidence proves that
  SQLite resource metadata and local file bytes survive together.
- No EventResource destination-protection implementation or key-ring
  backup/rotation evidence exists.
- No accessibility evaluation covers PDFs, presentations, recordings,
  locked-resource explanations, or external meeting tools.
- No retention analysis establishes whether individual access events are
  necessary or proportionate.
- Cerbos/local parity, tenant isolation, and multi-provider persistence have
  not been tested for an EventResource resource kind.

The current evidence supports design and implementation traceability only. It
does not establish stakeholder acceptance, operational effectiveness, or
absence of harm.

## Escalation Needed

- Security architecture: approve the Tier 1 threat model, fail-closed facts,
  destination classification, cache posture, revocation races, scanner policy,
  and private-bucket delivery before implementation.
- Privacy/legal: review individual access-record purpose, retention, organizer
  visibility, export, deletion, incident evidence, and jurisdiction-specific
  notice requirements before retention is enabled.
- Accessibility: validate resource metadata, locked states, document
  alternatives, captions/transcripts, keyboard flows, and external-provider
  limitations.
- Platform operations: define private storage, backup/restore, destination
  rotation, malware response, provider outage, and deletion runbooks.
- Qualified Sunni scholarly governance is needed only if the platform itself
  curates, endorses, classifies, or adjudicates contested religious content.
  The generic resource capability does not itself require a religious-legal
  ruling.

## Evidence Reviewed

- UR-001: user-provided EventResource proposal and examples, including files,
  links, attendee-only meeting access, speakers, timing, StorageObject, Cerbos,
  HAL, templates, and federation.
- UR-002: user instruction to continue the active report goal after the
  governed typed-policy baseline and report path were presented.
- ARCH-001: `docs/PROJECT.md`, `docs/ARCHITECTURE.md`, `docs/DOMAIN.md`,
  `docs/API.md`, `docs/BLAZOR.md`, `docs/SECURITY-MODEL.md`,
  `docs/MULTI_TENANCY.md`, `docs/SELF_HOSTING.md`,
  `docs/DEPLOYMENT_TIERS.md`, and `docs/CONFIGURATION.md`.
- EVENT-001: `src/Explore.Domain/Event.cs:14-155` and
  `src/Explore.Persistence/Extensions/PublicEventEligibilityQueryExtensions.cs:10-116`.
- LOCATION-001:
  `src/Explore.Application/Contracts/LocationPrivacy/EventLocationDisclosureContract.cs:12-190`,
  the disclosure evaluator, purpose-specific DTOs, EventLocation controller,
  and related public/attendee/management tests.
- AUTH-001:
  `src/Explore.Application/Behaviors/AuthorizationBehavior.cs:25-91`,
  `src/Explore.Application/Authorization/ResourceDescriptors.cs:65-88`,
  `src/Explore.Infrastructure/Services/RuntimeAuthorizationProvider.cs:150-225`,
  fallback authorization, typed fact projection, and
  `cerbos/policies/islamuevent_event.yaml:17-122`.
- HAL-001:
  `src/Explore.API/Hateoas/HateoasAuthorizationEvaluator.cs:39-153`,
  trusted Event reload at `:209-287`,
  `ResourceAssemblerBase.cs:259-342`, and Event link-policy tests.
- STORAGE-001: `src/Explore.Domain/StorageObject.cs:8-83`,
  `src/Explore.Domain/StorageConstants.cs:24-75`,
  `src/Explore.Application/Services/StorageObjectContentReader.cs:16-155`,
  local/S3 providers, signature policy, deletion service, storage repository,
  and BFF upload endpoints.
- ADJACENT-001: EventSessionSpeaker API/tests, ticketing contract tests,
  RegistrationAnswerFile, ParticipationRequirementAttachment,
  EventReportEvidence, OrganizationTenantEvidence, and
  EventReportExternalLink.
- TEST-001: authorization parity, Cerbos fail-closed, fallback authorization,
  tenant-isolation, Event visibility, EventLocation disclosure, storage
  signature, storage provider, deletion, and storage API test suites identified
  during repository inspection.
- IVSD-001: I-VSD scope, integration, action routing, report contract, context
  discovery, consultancy workflow, feature risks, principles/domains, evidence
  levels, architecture, technical, UX, data, operations, governance, and
  evaluation resources.
- SCAN-STORAGE-001: bounded repository search across `src`, `tests`, `docs`,
  `deploy`, and Docker/Compose inputs for malware/antivirus/scanner
  implementations and EventResource access-audit state. No implementation or
  test was found; generic audit fields and metrics were reviewed separately.
- SCAN-RESOURCE-001: bounded search of required product/architecture documents
  for EventResource-specific accessibility, acceptable-use, takedown,
  federation, and template behavior. No EventResource-specific contract was
  found.
- Evidence-set digest: sha256
  `ebf1709b01eaf980a305a0802f572d84d7ce125d2b9e144dbb3f6a3c1a89cb48`.
  It covers the exact repository-file manifest below. UR-001 is conversational
  user input and UR-002 is conversational approval; both are identified
  separately rather than included in the file digest.
  `docs/CONFIGURATION.md` and `docs/SELF_HOSTING.md` contained unrelated
  worktree changes and were therefore hashed by current content rather than
  represented only by Git object
  `df999a3f6b13a7a7362f30f44242dd81b8f10e38`.

Digest procedure: from the repository root, pass every manifest path to GNU
`sha256sum` in the exact order shown, concatenate its stdout byte-for-byte
(including the two-space filename separator and newline for each entry), then
hash that stream with a second `sha256sum`:

```text
sha256sum <manifest paths in listed order> | sha256sum
```

A missing, unreadable, reordered, added, or removed path invalidates the
revision.

### Evidence revision manifest

```text
docs/PROJECT.md
docs/ARCHITECTURE.md
docs/DOMAIN.md
docs/API.md
docs/BLAZOR.md
docs/SECURITY-MODEL.md
docs/MULTI_TENANCY.md
docs/SELF_HOSTING.md
docs/DEPLOYMENT_TIERS.md
docs/CONFIGURATION.md
docs/AUTHORIZATION.md
README.md
.env.example
docker-compose.yml
src/Explore.Domain/Event.cs
src/Explore.Domain/EventRegistration.cs
src/Explore.Domain/EventSessionSpeaker.cs
src/Explore.Domain/TenantUser.cs
src/Explore.Domain/EventRoleAssignment.cs
src/Explore.Domain/ParticipantAdmissionEligibility.cs
src/Explore.Domain/AdmissionTicket.cs
src/Explore.Domain/AdmissionCheckInRules.cs
src/Explore.Domain/AdmissionCheckInState.cs
src/Explore.Domain/TicketTypeEntitlement.cs
src/Explore.Domain/RegistrationOrder.cs
src/Explore.Domain/Enums/RegistrationOrderStatusEnum.cs
src/Explore.Domain/StorageObject.cs
src/Explore.Domain/StorageConstants.cs
src/Explore.Domain/ParticipationRequirementAttachment.cs
src/Explore.Domain/RegistrationAnswerFile.cs
src/Explore.Domain/EventReportEvidence.cs
src/Explore.Domain/OrganizationTenantEvidence.cs
src/Explore.Domain/EventReportExternalLink.cs
src/Explore.Domain/RegistrationProviderConnection.cs
src/Explore.Domain/Secrets/SecretBinding.cs
src/Explore.Domain/Secrets/SecretBinding.Factory.cs
src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs
src/Explore.Application/Contracts/LocationPrivacy/EventLocationDisclosureContract.cs
src/Explore.Application/DTOs/Location/EventLocationDtos.cs
src/Explore.Application/Services/EventLocationDisclosureEvaluator.cs
src/Explore.Application/Services/StorageObjectContentReader.cs
src/Explore.Application/Services/StorageContentSignaturePolicy.cs
src/Explore.Application/Services/StoragePresentationUrlResolver.cs
src/Explore.Application/Behaviors/AuthorizationBehavior.cs
src/Explore.Application/Authorization/ISecureRequest.cs
src/Explore.Application/Authorization/AuthorizationResourceContextResolver.cs
src/Explore.Application/Authorization/ResourceDescriptors.cs
src/Explore.Application/Telemetry/BusinessMetrics.cs
src/Explore.Application/Contracts/Secrets/ISecretResolver.cs
src/Explore.Application/Contracts/Services/IRegistrationSensitiveValueProtector.cs
src/Explore.Application/Contracts/Admissions/AdmissionIssuanceContracts.cs
src/Explore.Application/Features/StorageObjects/Handlers/Queries/GetPresignedDownloadUrlRequestHandler.cs
src/Explore.Infrastructure/Services/RuntimeAuthorizationProvider.cs
src/Explore.Infrastructure/Services/CerbosAuthorizationService.cs
src/Explore.Infrastructure/Services/FallbackAuthorizationService.cs
src/Explore.Infrastructure/Services/FallbackAuthorizationService.Batch.cs
src/Explore.Infrastructure/Services/FallbackAuthorizationService.Evaluators.cs
src/Explore.Infrastructure/Services/AuthorizationFactAttributeProjection.cs
src/Explore.Infrastructure/Services/RegistrationSensitiveValueProtector.cs
src/Explore.Infrastructure/Services/Registration/AdmissionDeliveryEnvelopeProtector.cs
src/Explore.Infrastructure/Geocoding/DataProtectionAddressSelectionProtector.cs
src/Explore.Infrastructure/Storage/LocalFileStorageProvider.cs
src/Explore.Infrastructure/Storage/S3FileStorageProvider.cs
src/Explore.Infrastructure/Storage/LocalFileStorageOptions.cs
src/Explore.Infrastructure/StorageObjectDeletionService.cs
src/Explore.Secrets/Services/SecretResolver.cs
src/Explore.Persistence/Extensions/PublicEventEligibilityQueryExtensions.cs
src/Explore.Persistence/Repositories/StorageObjectRepository.cs
src/Explore.API/Controllers/EventLocationController.cs
src/Explore.API/Controllers/StorageObjectController.cs
src/Explore.API/Hateoas/HateoasAuthorizationEvaluator.cs
src/Explore.API/Hateoas/ResourceAssemblerBase.cs
src/Explore.API/Hateoas/Policies/EventLinkPolicy.cs
src/Explore.API/Hateoas/Policies/EventLocationLinkPolicy.cs
src/Explore.Blazor/Extensions/BffStorageEndpoints.cs
src/Event.Standalone/appsettings.json
src/Event.Standalone/Dockerfile
cerbos/policies/islamuevent_event.yaml
tests/Event.Application.UnitTests/Services/StorageContentSignaturePolicySecurityTests.cs
tests/Event.API.IntegrationTests/Features/EventSessionSpeakerControllerTests.cs
tests/Event.Domain.UnitTests/Services/Registration/RegistrationApprovalStatusRulesTests.cs
tests/Event.Domain.UnitTests/Entities/AdmissionTicketLifecycleTests.cs
tests/Event.Domain.UnitTests/Entities/AdmissionTicketAuthorityForgeryTests.cs
tests/Event.Domain.UnitTests/Entities/AdmissionCheckInInvariantRedTests.cs
tests/Event.Domain.UnitTests/Entities/RegistrationRequirementFulfillmentTests.cs
tests/Event.Persistence.IntegrationTests/ParticipantAdmissionEligibilityPersistenceTests.cs
tests/Event.Standalone.IntegrationTests/StandaloneProviderCompositionTests.cs
.agents/skills/i-vsd/SKILL.md
.agents/skills/i-vsd/resources/scope-boundaries.md
.agents/skills/i-vsd/resources/integration-contract.md
.agents/skills/i-vsd/resources/action-routing.md
.agents/skills/i-vsd/resources/report-contract.md
.agents/skills/i-vsd/resources/context-discovery.md
.agents/skills/i-vsd/resources/consultancy-workflow.md
.agents/skills/i-vsd/resources/feature-risk-patterns.md
.agents/skills/i-vsd/resources/framework-overview.md
.agents/skills/i-vsd/resources/principles-and-domains.md
.agents/skills/i-vsd/resources/evidence-and-validation-levels.md
.agents/skills/i-vsd/resources/scholarly-consultation-boundaries.md
.agents/skills/i-vsd/resources/architecture-heuristics.md
.agents/skills/i-vsd/resources/technical-decision-framework.md
.agents/skills/i-vsd/resources/ux-and-defaults-heuristics.md
.agents/skills/i-vsd/resources/data-governance-heuristics.md
.agents/skills/i-vsd/resources/operational-framework.md
.agents/skills/i-vsd/resources/governance-and-accountability-framework.md
.agents/skills/i-vsd/resources/evaluation-metrics.md
```

## Missing Evidence

- Organizer, attendee, speaker, tenant-operator, accessibility, privacy, and
  support stakeholder research.
- Current acceptable-use, upload-content, takedown, appeal, access-audit, and
  EventResource retention policies.
- A selected malware-scanning or file-safety operating model.
- Production private-bucket/CDN configuration and cache tests.
- Meeting/streaming provider contracts for generated links, rotation, expiry,
  and revocation.
- Runtime incidents, support tickets, access logs, accessibility reports,
  security audits, and restore evidence.
- Final lexicon/federation and template requirements.
- Specialized code-review-graph queries: the repository graph database existed,
  but its MCP query tools were not exposed in this session. Bounded source,
  policy, documentation, and test discovery was used instead.

## Context Inventory

- Available: repository/workspace architecture, domain, API, security,
  multi-tenancy, self-hosting, deployment, and configuration documentation.
- Available: current code, Cerbos policy, local authorization, HAL, storage,
  location-disclosure, adjacent registration/speaker/ticket concepts, and
  relevant tests.
- Available: the user's proposed model, scenarios, security concerns, template
  direction, and federation direction.
- Not visible: a relevant connected issue tracker, roadmap, support system,
  incident system, analytics source, customer-feedback source, or external
  project knowledge base.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-01 | none | current | Standalone EventResource consultancy requested and governed-preset baseline accepted through continuation | Evidence digest `ebf1709b01eaf980a305a0802f572d84d7ce125d2b9e144dbb3f6a3c1a89cb48` |
| 2026-09-02 | current | current | Senior CTO review and architecture hardening (hierarchical state invariants, storage durability, keyring persistence, Cerbos/anonymous strategy, Worst-Break adversarial check, 4-PR decoupling sequence) | Updated consultancy report with platform durability fixes, Data Protection keyring guarantees, and decoupled 4-PR delivery plan |

Refresh this report when resource scope, audience predicates, metadata
disclosure, destination handling, scanner policy, access-audit retention,
governance delegation, federation, templates, or provider integrations change
materially.
