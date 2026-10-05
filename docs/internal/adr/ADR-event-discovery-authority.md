# Event discovery authority and bounded traversal

Status: Implemented; adversarial native repairs verified, real-browser and attendee evaluation gates remain open.
Scope: Public discovery membership, current disclosure, native transaction ordering.

## Decision

Freeze a finite ordered set of canonical source references, not card payloads.
Reproject exact matching sessions on every batch. Identity/disclosure epochs
invalidate authority changes; captured time boundaries invalidate clock-only
changes. Snapshot count is not a global count. No obsolete public offset adapter
or historical response fallback remains in the public list endpoint.

Use the configured ASP.NET Core Data Protection authority for purpose-bound
continuations. Authenticate tenant, snapshot, criteria digest, ordinal, expiry and
epochs. All replicas require the same key ring and application identity.

Persist portable rank keys rather than trusting each database's title/GUID
collation. Invariant-uppercase UTF-16 hex and canonical source GUID `N` encodings
have one ordinal ASCII order. Both seeks and the bounded merger use it, including
explicit null placement and ascending source-kind/source-key ties. Backfill runs
bounded keysets through migration authority before readiness. The criteria digest
includes the rank-contract version; there is no obsolete ordering adapter.

## Directed transaction order

Capture reads initial epochs outside Serializable, avoiding a retained shared
epoch lock before source locks on SQL Server. Inside Serializable it takes a
dedicated native snapshot reservation before reading source candidates. A native
write, not a process mutex or advisory-only read, protects capacity against stale
transaction snapshots. After consistent reprojection it obtains the terminal
native epoch fence, validates, and writes only snapshot-owned data.

Writers collect affected tenants across saves. They finish domain, settings,
native-operation, audit and outbox writes before acquiring sorted terminal epoch
rows. Failure poisons finalization; later source writes after sealing are rejected.
Bounded global changes resolve old and new dependencies before deletion. Snapshot
and pure view-count writes are excluded. Unsupported global fanout fails closed.

Source writers fence Tenant parents before implicit source foreign-key locks.
Authority planning on SQL Server and InnoDB uses a separate ReadUncommitted
connection solely to propose the ownership fence set. The owning transaction
takes sorted Actor, Tenant and Event fences, then rereads ownership under those
fences before loading grants. Ownership drift aborts with a concurrency conflict;
dirty planning data never establishes authorization.

Tenant creation and global fanout share a transaction-owned native enrollment
gate before terminal epoch locks. PostgreSQL retains a row write for nonempty
catalogs; when an old snapshot is empty, a separate ReadCommitted connection
checks committed membership under that gate. Truly empty permits cold lookup
bootstrap; newly committed membership rejects the stale fanout. Global reads
retain forced-RLS fail-closed checks rather than treating hidden tenants as an
empty catalog.

HAL assembly precedes a separate short fresh native ReadCommitted comparison
against the prepared server-only stamp. No source-dependent enrichment follows
that release check. This is not proof that arbitrary unregistered raw SQL or
out-of-process configuration changes automatically participate.

## Resource and recovery semantics

Defaults: 1000 canonical identities, 15 minutes, 200 live snapshots per tenant,
400000 physical membership rows and independently bounded headers, 10000 examined
rows and 32 combined source seeks. Only finite downward tuning is supported.
Expired rows consume physical capacity until bounded Quartz cleanup removes them.
Logical expiry is immediate and does not depend on the scheduler.

Retention ownership is independent of source-directory existence. The bounded
cleanup path uses reservations and fences only an existing epoch, never recreating
public authority for an orphan. PostgreSQL uses a purpose-bound ownership-only
SECURITY DEFINER enumeration function installed by normal migration bootstrap.
Its owner is NOLOGIN/NOBYPASSRLS with narrowly scoped header columns; runtime gets
execution through a separate role, not owner membership or global RLS bypass.
Ordinary exact-tenant deletion and FORCE RLS remain in effect.
Enumeration and physical purge cap requested expiry at PostgreSQL statement time,
so a caller-supplied future clock cannot disclose live-only foreign owners or
delete their live snapshots.

Invalid continuation returns 400, expiry 410, changed authority 409 and unavailable
authority/capacity 503, without historical membership metadata. Attendees retain
filters and place until choosing an explicit new search that replaces results.
Captured rank changes do not move original registration, payment or session IDs.

## Evidence boundary

Real SQLite storage tests cover 201 contenders, reservation exclusion, actual-row
capacity, tenant separation, rollback and logical expiry without purge. Native
HTTP tests cover forward traversal, authenticated criteria, removed sessions and
metadata-free failures. Those tests and generated schemas do not establish
PostgreSQL, SQL Server, MariaDB or MySQL lock behavior on their own. Native
authority14 and writer3 cases subsequently passed on each of those engines and
SQLite without skips. Identity/traversal migrations applied, rolled back and
reapplied on disposable databases; all four catalogs had clean model checks.
Three PostgreSQL enrollment cases distinguish cold bootstrap from stale empty
and nonempty membership, and the maintenance witness passed under a distinct
non-owner, non-bypass runtime login after the production migration bootstrap.
Actual HTTP PNG/304/privacy-concealment checks also passed.

After independent review identified two additional lock inversions, SQL Server
passed seven source-writer races and three authority-planning cases without skips.
The latter pause the actual planner before its first Actor command and require a
detached writer to commit, then verify revoked grants and ownership drift. The
restricted PostgreSQL maintenance witness also passed with finite future-cutoff
enumeration and deletion attempts against a live-only foreign owner. The new
MySQL and MariaDB each subsequently passed the same seven writer and three
authority-planning cases, and PostgreSQL passed all seven writer cases. All
executed without skips on fresh generated catalogs; disposed lane containers
were removed. This is native database evidence, not a browser acceptance claim.

The execution ledger owns exact commands and counts. The ten receipt-generation
failures were caused by this workstream expanding the migration catalog, not by
an unrelated baseline defect. Both selectors now require the unique initial
migration; the receipt target passed ten cases and the complete architecture
suite passed 654, without skips. Desktop/mobile/RTL screenshots, keyboard search,
correction-dialog dismissal and native organizer authentication are recorded.
Alias review and reversal, multi-batch continuation and attendee authorization
evaluation remain open; native checks do not establish those outcomes.
