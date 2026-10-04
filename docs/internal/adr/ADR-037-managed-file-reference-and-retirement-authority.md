# ADR-037: Managed File Reference And Retirement Authority

- **Status:** Accepted architecture; automated native runtime and five-engine provider gates reconciled, browser acceptance pending.
- **Date:** 2026-10-03
- **Decision scope:** Approved storage decisions D4-D7: captured targets, logical references, shared-reference fencing and durable retirement. Remote Asset activation remains deferred.
- **Refines:** [ADR-033 governed resource delivery](ADR-033-governed-event-resource-delivery.md), extending its exact-target retirement authority without replacing resource inspection, audience or retention policy.

## Context

One managed object can serve several owners. Uploader identity, creator audit
identity, purpose and an owning-resource label do not enumerate those uses.
Tenant and soft-delete filters are presentation boundaries, not proof that
physical references have disappeared. Deleting provider bytes on that assumption
can destroy another owner's file.

The embedded Local and S3-compatible providers must remain operational without
ISLAMU Asset. Stable `StorageObject.Id` references identify application objects;
an immutable `StorageProviderBinding`, object key and acknowledged version
identify bytes. `SourceUri` records foreign provenance only. Application delivery
URLs are derived from ID and current policy, never used as ownership evidence.
See [STORAGE.md](../STORAGE.md) for targeting, delivery and operator procedures.

## Decisions

### 1. Physical foreign keys are reference authority

[StorageObjectReferenceRepository](../../../src/Explore.Persistence/Repositories/StorageObjectReferenceRepository.cs)
builds an existence predicate from EF Core's mapped relational foreign-key
constraints referencing `StorageObject`, joining every column of composite keys.
It does not query filtered owner DTOs or maintain a parallel reference count.
Hidden, soft-deleted and cross-tenant owners therefore block retirement without
disclosing their names or identities.

Upload sessions and `StorageProducerOperation` are excluded from readable-owner
scanning: they are producer custody that must transfer, not attachments that
vanish when metadata is hidden. Published registration CSV retention and
unresolved delivery remain a separate hold predicate evaluated at a server-owned
UTC instant. Reference and hold admission scans require a caller-owned
transaction; neither a false predicate nor a fence alone authorizes provider I/O.

### 2. Attachment changes and retirement share the source-row CAS

[ExploreDbContext.StorageReferences](../../../src/Explore.Persistence/ExploreDbContext.StorageReferences.cs)
enrolls persisted old and proposed new object IDs before tracked saves. It reads
database values rather than trusting detached originals, updates the source
concurrency stamp by compare-and-swap, and refreshes tracked stamps. Discovery,
enrollment and persistence share one transaction.

Acquisition follows tenant/object order. Multi-save and bulk owners declare the
complete target set through `IStorageObjectReferenceRepository.FenceAsync`
before their first mutation. Tracked enrollment does not implicitly cover
`ExecuteDelete`, bulk reorder or ancestor database cascades. Failed enrollment
poisons a joined unit of work; translating an exception cannot permit a later
commit. Committed retirement or deleted source state rejects a new attachment.
Resource activation additionally requires its native settled-session activation
CAS, not an arbitrary `DeleteRequested` to `Active` edit.

Registration authoring's own transaction preserves pending tracked writes with
`SaveChanges(false)` until commit completes. Its execution strategy restores
fence stamps before retry and verifies a lost commit acknowledgement through a
unique persisted graph stamp before accepting changes. Caller-owned transactions
retain their own commit and retry boundary. Real PostgreSQL regression cases
cover both a rolled-back commit attempt and a committed but unacknowledged one.

### 3. Native lifecycle admission transfers custody before deletion

[EventResourceStorageLifecycleRepository](../../../src/Explore.Persistence/Repositories/EventResourceStorageLifecycleRepository.cs)
is the shared admission owner despite its resource-origin name.
`TryQueueRetirementAsync` fences the source, flushes tracked detachment, scans
physical references and holds, validates exact target agreement, and transfers
custody into `StorageObjectDeletionTombstone`. Source/session/operation removal
and quota projection commit in the same transaction.

[StorageRetirementTarget](../../../src/Explore.Domain/StorageRetirementTarget.cs)
rejects missing bindings/keys, competing producer custody and mismatched
tenant/provider/binding/key/version. A known captured version cannot be replaced
by an incompatible acknowledgement. Active metadata cannot override an unsettled
producer: that work remains `AwaitingProducer`, not `Ready`.

Heavy redaction detaches references rather than reassigning or deactivating a
shared source. Privacy erasure includes detached pictures in its candidate
union. Registration retention, federation replacement, stored-resource
conversion and reconciliation converge on this admission owner. Indirectly
selected shared or held sources survive; explicit still-attached source removal
fails closed. Quarantine age selects candidates but does not authorize deletion.

### 4. Source-less acknowledgement preserves disposal, not access

An object-bearing upload session can outlive removal of readable metadata.
Admission can transfer its exact captured target under the session CAS without
manufacturing a `StorageObject`. Replay validates existing tombstone agreement
and removes remaining closed custody transactionally.

`RecordProducerSettlementAsync` accepts a validated exact producer identity and
settles retained work even when erasure or cancellation removed the session.
It does not resurrect metadata, reopen authorization or delete bytes.
A positively stopped pre-I/O attempt can acknowledge no-write completion;
an unknown provider outcome cannot become settled merely because time elapsed.
CSV and federation writers similarly persist producer identity before I/O and
consume it with owner activation or transfer it into independent retirement.

### 5. One leased worker proves provider absence

[EventResourceStorageCleanupService](../../../src/Explore.Application/Services/EventResourceStorageCleanupService.cs)
is the existing bounded cleanup worker for both generic and resource targets.
It claims tombstones under CAS, resolves the captured binding, deletes the exact
key/version outside the admission transaction, rejects delete-marker-only or
mismatched results, checks existence, then records absence under the lease stamp.
Claim and absence authority reject surviving source/session/producer custody.
Failures retain independent retry work rather than depending on removed source
metadata or repeating moderation. The former direct deletion service is removed;
there is no second generic deletion engine.

### 6. Generic authorization remains source-only and acknowledgements bounded

[DeleteStorageObjectCommandHandler](../../../src/Explore.Application/Features/StorageObjects/Handlers/Commands/DeleteStorageObjectCommandHandler.cs)
re-reads generic eligibility in one serializable unit and has no provider
dependency. Resource-owned bytes and retained organization evidence remain
excluded. A tenant-qualified tombstone or source-less session cannot authorize
a fresh generic request.

[StorageObjectController](../../../src/Explore.API/Controllers/StorageObjectController.cs)
returns 202 with the UUID command acknowledgement for `Pending`, not proof of
absence. Missing eligible sources return 404; in-use, retention, invalid-target
and concurrency failures map to bounded 409 responses. Another owner's identity
or physical locator is never returned to explain the conflict.

[StorageObjectLinkPolicy](../../../src/Explore.API/Hateoas/Policies/StorageObjectLinkPolicy.cs)
gates `delete` using authorization plus the server-only retirement eligibility
hint; `edit` remains independent. The hint uses the same persisted predicates
but is neither serialized nor command authority. Clients use `_links`;
a stale link still faces transactional admission. Content projections refresh
server time after asynchronous eligibility reads to preserve disclosure expiry.

### 7. Quota is an idempotent persisted-cohort projection

[StorageUsageCounterRepository](../../../src/Explore.Persistence/Repositories/StorageUsageCounterRepository.cs)
CAS-fences the tenant/provider counter before reading persisted cohorts in the
caller transaction. Activation, retirement and recalculation use that same
projection. Hidden or soft-deleted ownership does not remove an active charge;
transferred custody does. Reserved/uploading sessions supply reservations.

Retries never debit quota again. Retiring a previously uncounted CSV/import
cannot subtract another object's charge, and replacement activation cannot add
bytes already counted by retirement projection. Provider absence and logical
quota transfer are different events.

## Alternatives

- **Filtered owner queries or local reference counts:** rejected; they can omit
  hidden/cross-tenant references and drift from mapped physical relationships.
- **Independent attach/delete locks:** rejected; a reference can race retirement.
- **Synchronous generic provider deletion or moderation retry:** rejected;
  database commit and provider completion are not atomic, and source loss must
  not discard disposal authority.
- **Timeout as producer settlement:** rejected; delayed completion can recreate
  bytes after an apparent successful cleanup.
- **Copies per use or remote IDs as local FKs:** rejected for this workstream;
  copies change shared identity/quota, while remote FKs couple Event to an
  unavailable service.
- **Selectable dormant Asset adapter:** rejected; standalone must work with
  implemented providers, not fake health or fallback ownership.

## Consequences And Deferred Boundaries

Application uses native CQS and entity-returning persistence ports; Domain
captures exact target invariants, Persistence owns transactional authority, and
provider adapters own byte transport. Retired roots/buckets and secret references
must remain available until their work settles. Restore must preserve outstanding
producer/tombstone custody and independent privacy-erasure authority.

[Future ISLAMU Asset integration](../../../dev/backlog/islamu-asset-provider-integration.md)
must map remote asset/version identity to existing local UUIDs and deliberately
hand over byte authority without adding a competing retirement worker.
Deployment migration requires provable historical targets; current settings
cannot reconstruct old binding history.

## Evidence And Acceptance Boundary

This ADR records the runtime integration committed as `80e8070b8`.
[Storage architecture and operations](../STORAGE.md) records the durable
reference, custody and cleanup contracts. Native coverage includes
`StorageObjectPhysicalReferencePostgresTests`, `ManagedStorageRetirementTests`,
`StorageRetentionHateoasTests` and `NativeStorageObjectHttpTests`.
Task-local inventories and execution receipts remain working memory, not
published link targets. Model coverage does not prove unimplemented attachment
surfaces or arbitrary future bulk callers.
PostgreSQL/SQLite/SQL Server/MySQL/MariaDB migration lifecycle and runtime gates
have actual engine receipts; the final Architecture suite passed 679/679.
Final API coverage is reconciled through exhaustive cohorts and corrected owning
gates. Desktop/mobile browser proof is still pending.
Documentation validation here is not a new runtime test receipt or release approval.
