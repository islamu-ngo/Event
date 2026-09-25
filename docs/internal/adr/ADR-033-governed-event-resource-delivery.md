# ADR-033: Governed Event-Resource Delivery and Revocation

- Status: Accepted for the implemented pre-release capability; rendered accessibility and full scenario acceptance remain open.
- Date: 2026-09-24

## Context

An event may have private materials with their own lifecycle, audience,
availability and owner. Event visibility, a storage uploader's identity and a
once-advertised link are insufficient authority to disclose material metadata
or deliver bytes or an external destination. Standalone installations must
also recover bytes and Data Protection keys with the database.

The planning I-VSD assessment in
`islamic-value-sensitive-design/workstreams/i-vsd-event-resources.md` is
plan-aligned, not an implementation accessibility or deployment certification.
Default denial of unscanned content and an explicit, constrained instance
opt-in remain the selected baseline; no scanner provider has been selected.

## Decision

Resources are tenant-qualified UUIDv7 aggregates owned by exactly one event
and optionally one of its sessions. Their audience and UTC availability rules
are separate from publication; withdrawn, deleted, ineligible-parent,
expired, unsafe or policy-disallowed resources cannot disclose protected
metadata or deliver content. A bounded per-event active-resource ceiling and
tuple-level relational ownership constrain supported writes and direct rows.
Repositories return domain entities. Native CQS commands and queries map
immutable results in Application; HTTP exposes independent management,
discovery, export and delivery capabilities through server-authored HAL links.
A link is an affordance, never durable authorization.

The Application layer uses current subject-specific participation and the
intersection of instance, tenant and storage ceilings. Instance locks remain
effective in SingleTenant mode. Tenant resource-settings reads project this
same effective intersection; the `source` field reports where the stored
override originated, which may differ from the tighter effective `value`.
Malformed or unavailable policy denies access rather than widening it.

For each delivery request, obtain a fresh, consistent final authorization
snapshot after preliminary work. A revocation committed before that read
begins denies; a revocation racing after it begins can overlap already
authorized delivery. Recheck time before headers, never claim to recall
delivered bytes, and authorize later range/retry requests anew. Storage
objects retain the resource-owner tuple and cannot be fetched through the
generic uploader path. Reservations, finalization, replacement, quota and
minimal success audit follow the same versioned, idempotent mutation
protocol; the old attachment remains active until replacement commits.
Cleanup tombstones remain non-public, minimal and retryable independently of
management-audit retention.

External destinations are validated HTTPS URLs, protected with tenant- and
resource-bound Data Protection purpose and stored write-only. Ordinary
contracts show only the safe origin. Authorized access produces a temporary
no-store/no-referrer redirect through the application origin; neither the
server nor its BFF fetches the destination. The attendee surface warns before
navigation. Revoking platform rights denies future redirects but cannot
revoke an external URL already revealed; its provider owns link rotation.

Standalone defaults to private local bytes and persistent shared Data
Protection keys. Restore the stopped SQLite database, local bytes and keys as
one coordinated set before starting a fresh host. PostgreSQL, SQLite, SQL
Server and the shared MySQL/MariaDB EF histories are generated from model
configuration. The ASCII ordinal collation on both AT Protocol DID join
columns is required for SQL Server public-event eligibility.

## Deferred Responsibility

Malware scanning, guest/dependent access, provider-generated expiring links,
identified attendee access history, template/federation publication, portable
file bundles and personalized certificates are separate backlog decisions.
No placeholder adapters, external-provider credentials, surveillance history
or implied permissions are introduced by this decision.

## Consequences And Evidence Limits

The selected `PrimaryDatabaseProviderBehaviorContractTests` class passed
9/9 on each of SQLite, PostgreSQL, SQL Server, MariaDB and MySQL after the
generated migrations: six resource cases per provider. The offline
Standalone-to-Combined restore test passed after migration; focused
governance HTTP and resource component tests cover policy tightening and
HAL-only actions. Those results do not establish that all 42 planned
scenarios are accepted. In particular, the fresh browser host could not
reach resource pages while its tenant lifecycle was unprovisioned; keyboard,
focus, RTL, theme and assistive-technology behavior on those pages remain
unverified in real use. Full unrelated integration suites also were not
green; their failures must not be silently counted as resource passes.

The technical contract and recovery details live in
[`EVENT_RESOURCES.md`](../EVENT_RESOURCES.md); the operator and attendee
guides live under `docs/public/documentation/readme/`.
