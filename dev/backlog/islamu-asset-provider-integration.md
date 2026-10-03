# ISLAMU Asset Provider Integration

> **Status:** Deferred; actionable integration boundary, not an implemented adapter.
> **Date:** 2026-10-03
> **Origin:** Approved pre-release contract foundations decisions D4-D7.
> **Decision anchor:** [ADR-037](../../docs/internal/adr/ADR-037-managed-file-reference-and-retirement-authority.md).

## Outcome

Integrate a real ISLAMU Asset byte provider while preserving existing
`StorageObject.Id` UUIDs, attachment references, tenant/purpose authorization and
standalone Local/S3 operation. Remote asset/version identifiers map to local
objects; they must not replace Event relational FKs or become browser-authored
physical destinations. This is a separately approved activation and migration
project, not a prerequisite for embedded storage graduation.

## Existing Native Boundary

- [StorageProviderBinding](../../src/Explore.Domain/StorageProviderBinding.cs)
  pins a physical destination and retained secret references. Existing bytes
  cannot move merely because effective provider settings change.
- [StorageRetirementTarget](../../src/Explore.Domain/StorageRetirementTarget.cs)
  captures exact target/version and producer settlement.
- [StorageObjectReferenceRepository](../../src/Explore.Persistence/Repositories/StorageObjectReferenceRepository.cs)
  owns physical-reference and hold predicates; Event keeps attachment authority.
- [EventResourceStorageLifecycleRepository](../../src/Explore.Persistence/Repositories/EventResourceStorageLifecycleRepository.cs)
  transfers native producer/source custody into tombstones.
- [EventResourceStorageCleanupService](../../src/Explore.Application/Services/EventResourceStorageCleanupService.cs)
  owns leased cleanup and absence acknowledgement. Remote transport must join
  this protocol rather than introduce a second Event disposal loop.

## Required Integration Contract

1. **Define identity and version mapping.** Specify remote asset, immutable
   version and operation identifiers, their uniqueness scope, and the persisted
   mapping to local UUID/binding identity. Foreign `SourceUri` remains provenance;
   remote delivery URLs and expiring grants are not ownership or stable identity.
2. **Keep Event reference authority local.** Physical FK scanning must continue
   to ignore presentation filters. Old/new attachment CAS, complete-set
   enrollment for bulk/cascade owners, registration holds and resource
   inspection/audience restrictions remain prerequisites for retirement.
3. **Persist custody before network I/O.** Define remote create/write
   acknowledgements, idempotency and reconciliation keyed by durable operations.
   Lost replies leave unresolved producer work, not guessed success or absence.
   Source-less late acknowledgements must settle exact retained work after
   cancellation/erasure without recreating readable metadata.
4. **Specify one deletion authority handover.** Decide which service owns
   remote byte disposal and how Event's existing tombstone lease receives a
   durable exact-version completion/absence receipt. Prevent concurrent local
   and remote deletion owners. A delete marker, accepted request or expired
   URL alone is not absence proof. Define retries, lease loss and replay.
5. **Preserve public semantics.** Generic writes authorize from eligible source
   metadata, never tenant-only cleanup work. HAL hints stay server-only and
   advisory; commands rescan. Admission stays bounded 202/409/404 rather than
   synchronously promising remote absence. Do not disclose cross-tenant owner
   names, provider keys or private remote locators.
6. **Keep quota idempotent.** Event's persisted-cohort projection remains local
   logical usage authority unless a separately approved contract replaces it.
   Define remote billing separately; acknowledgements, retries and migration
   replay cannot double-charge or subtract another object's bytes.
7. **Implement actual operational support before selection.** Define
   authentication, credential rotation, capability/readiness checks, network
   failure behavior and backup/restore obligations. Use approved secret
   authorities, not source/appsettings credentials. No no-op selectable provider,
   fake healthy result or silent Local/S3 fallback may ship.

## Migration And Authority Cutover

Inventory exact historical bindings, keys, versions, checksums, references,
holds and outstanding producers/tombstones before migration. Unprovable targets
require reviewed repair or trusted re-upload, never inference from current
settings or URL shape. Preserve all local UUIDs and external provenance.

Design a resumable manifest and idempotent copy/verification protocol. Define
the write fence or maintenance boundary, verified byte equivalence, exact
mapping commit and point at which old-target retirement becomes authorized.
Drain or explicitly transfer outstanding producer and cleanup custody; never
drop it to make counts agree. Keep old roots/buckets and retained credentials
until their exact work is resolved.

Document forward recovery and rollback before activation. Restoring a matching
application/database pair must not restore erased readable data or revive old
byte authority after handover. Preserve independent erasure authority, migration
receipts and provider custody through backup/restore. A remote service outage
must not redirect requests to another target.

## Acceptance Work

- Implement a native adapter and persisted mapping with generated migrations
  and API/client ownership where required; update internal and public operator
  documentation together.
- Exercise actual remote write/read/exact-version delete/absence behavior,
  interrupted writes, lost replies, late ACKs, lease loss and receipt replay.
  Include shared/hidden/cross-tenant references, legal holds and quota races.
- Verify migration resume, pinned-old-target access, cutover rollback and
  privacy-erasure replay against restored data.
- Run applicable five-engine reference/CAS/migration gates and real API/BFF
  flows; inspect HAL-driven desktop/mobile actions and disclosure boundaries.
  Include standalone tests with Asset absent.

The remote API, credentials, transport and receipt schema must be specified
against the actual service in that future project. No remote protocol or
dependency is selected by this backlog. The current embedded workstream's
native five-engine migration/runtime and automated API gates are reconciled;
browser acceptance is still pending. Neither is evidence that this remote
integration exists.

## Planning Inputs

- [Managed-file authority decision](../../docs/internal/adr/ADR-037-managed-file-reference-and-retirement-authority.md)
- [Identity and optional-service authority boundary](../../docs/internal/adr/ADR-036-verified-identity-correlation-authority.md)
- [Storage architecture and operations](../../docs/internal/STORAGE.md)
