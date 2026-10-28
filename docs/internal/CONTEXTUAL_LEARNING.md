# Private Learning State And Authored Practice

> **Audience:** Developers and operators
> **Status:** Implementation in progress; Domain and SQLite storage verified, authorized operations and transport pending
> **Last Verified:** 2026-10-28
> **Source Anchors:** `src/Explore.Domain/Learning/`, `ILearningRepository`, `ILearningPrivacyErasureStore`, `PrivacyErasureApplier`

## Delivery Boundary

This workstream adds a private learning backend, not rendered tours, a learning
workspace or a live AI mode. Required successors first compose isolated practice
in the actual event/chat presentation, then add contextual invitations and
guidance. Completing this backend cannot discharge those successors'
accessibility, comprehension or package-admission release gates.

Learning is optional. An authenticated, active tenant member may learn in self
context without publishing, organization administration or event-creation
authority. An actor context requires current membership in that actor's actual
tenant placement. Learning never grants production authority, changes provider
readiness or completes tenant provisioning.

## Domain Ownership And Bounds

`TenantLearningSpace` is the tenant's single `learning-core` material-version-1
source. Its unique tenant key resolves concurrent first starts. Removal is a
durable tombstone: a read must not reconstruct the source, and recreation must
be an explicitly authorized operation. `RemovedAtUtc` retains the last removal
even after explicit recreation.

`UserLearningState` belongs to one tenant and one resolved internal `User.Id`,
not a provider subject, device identifier or administrator-supplied owner. It
owns private preferences, tour progress and practice. Tenant identifiers reject
reassignment. No practice-to-production user/actor/event foreign key is added;
actor context is a canonical scalar key.

There are four tour families: Shell, Studio, EventManagement and NormalAi. Each
retains only the current and previous material versions, at most eight progress
rows. One learner has at most one active claim across tabs. The inactivity
deadline is 120 server seconds. Only an advancing explicit progress step renews
it; retries, unchanged steps, preference writes and practice activity do not.
An invitation may be skipped without claiming. Other outcomes are claim-bound.
Manual replay preserves the earlier completed/skipped outcome.

Practice uses canonical `self` or `actor:{guid:D}` context keys, at most 20
contexts, six recipe keys per context, 1 KiB per record and 128 KiB per learner.
State consists of bounded action/choice identifiers and integers, never arbitrary
prompts or production payloads. An owner can clear a practice in an orphaned
context without regaining preview authority.

Caller-supplied UUIDv7 identifiers and explicit UTC times are retained across the
one original attempt. Exact retained operation receipts are idempotent; changed
payloads conflict. Receipt retention is bounded to the aggregate's latest intent
and each retained child's latest intent, not an unlimited request journal.

## EF Persistence And Isolation

All four entity types carry the named tenant filter. Repository reads include
explicit tenant and internal-user predicates; knowing a practice UUID does not
authorize its retrieval. Queries do not create either root.

Root `Revision` values and practice `Revision` are application-managed `long`
concurrency tokens. Insert uniqueness is a separate conflict path from EF
update/delete token mismatches. `LearningWriteConflict` contains the narrow
provider exception classification; handlers must receive a typed conflict,
not a provider-specific error or a silent retry.

Both roots cascade from the tenant. Children cascade only from their learner
through the composite `(TenantId, UserLearningStateId)` key. This enforces child
tenant consistency without adding a second production-owner cascade route,
including on SQL Server. No actor or production user is deleted by learning.

The generated `AddTenantLearningState` migration cohorts belong to the primary,
SQLite, SQL Server and MySQL migration owners. MariaDB consumes the MySQL cohort
but requires independent execution evidence. Migrations and snapshots are
generated artifacts; edit entities/configurations and regenerate instead.

## Common Erasure Boundary

`ILearningPrivacyErasureStore` does not grant erasure authority or record
coverage. Its caller is the existing protected erasure workflow.
`PrivacyErasureApplier` first verifies retained recognition and fences the
internal subject. It then purges learning across tenants and proves absence
inside that original transaction, before either its coverage return or native
PostgreSQL return.

Covered retained replay must enter this common applier too. Skipping directly
from retained coverage would leave later private learning rows unexamined.
An absence failure aborts settlement; a rollback retains both the prior learning
rows and the prior coverage state. Provider/native-role and both race-order
evidence remain mandatory before backend completion.

## Closed Simulation Contract

The immutable wire contracts live in `Event.Wire.Contracts/Learning`, namespace
`ISLAMU.Wire.Contracts.Learning`. This follows the project's `ISLAMU` root;
introducing a root `Event` namespace shadows the existing Domain `Event` type.
Enums accept exact named strings only, not numbers or numeric strings. Requests
reject unmapped ownership/effect fields, and published collections snapshot
their inputs. HAL extension data remains available.

`LearningLessonCatalog` owns finite transitions and reconstructs authored
presentation from the persisted machine state:

| Recipe | Allowed choices |
| --- | --- |
| `event-details` | `in-person`, `online` |
| `sessions-program` | `single-session`, `two-sessions` |
| `ticket-capacity` | `capacity-12`, `capacity-24` |
| `attendees-team` | `attendee`, `helper` |
| `publication-lifecycle` | `keep-draft`, `preview-publication` |
| `ai-example-review` | `accept-example`, `reject-example` |

Start/reset persist step 0, selection persists step 1, and confirmation persists
the authored result step 2. Receipts explicitly identify simulation and the
absence of production effects. No live provider, conversation/history, outbox,
production event/order/attendee repository or external-effect dependency belongs
in this execution path.

## Remaining Exposure Gate

Before exposing `/api/learning`, the native Application/API slice must prove
current human and tenant authority, exact selected-context/original-transaction
custody, retained erasure exclusion, completion-image validation and terminal
credential/claim deadlines. Closed commands must not accept caller delegates or
a reusable authorization grant.

Private GET handlers must require the current human despite controller-level
`[AllowAnonymous]`, return `no-store`, and expose only server-authorized HAL
actions. Writes use `[Authorize]`; support sessions, machine credentials,
foreign/stale scopes and stale revisions must fail closed. Generated OpenAPI and
clients follow those verified routes, not hand-maintained parallel DTOs.

The [operator guide](../public/documentation/readme/getting-started/contextual-learning.md)
describes the adopter boundary. The task context records actual commands,
evidence and open gates; source structure alone is not acceptance.
